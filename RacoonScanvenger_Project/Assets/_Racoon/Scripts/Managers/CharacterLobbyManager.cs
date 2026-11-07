using Racoon.Gameplay;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Lobby de selección de personaje.
    ///
    /// Cuando hay 2 jugadores conectados, cada cliente
    /// elige personaje y pulsa READY.
    ///
    /// Cuando todos están READY, el servidor crea
    /// los PlayerObjects y empieza la partida.
    ///
    /// Si la escena no tiene este componente, PlayerSpawnManager
    /// crea a cada jugador al conectarse (escenas de prueba con NetworkTestMenu).
    /// </summary>
    public class CharacterLobbyManager : NetworkBehaviour
    {
        /// <summary>El lobby de la escena cargada (null si la escena no tiene).</summary>
        public static CharacterLobbyManager Current { get; private set; }

        [SerializeField] PlayerSpawnManager spawnManager;

        readonly NetworkVariable<int> connectedCount = new(0);
        readonly NetworkVariable<int> readyCount = new(0);
        readonly NetworkVariable<bool> gameStarted = new(false);
        readonly NetworkVariable<bool> characterSelectionActive = new(false);

        /// <summary>Lo que tiene marcado cada jugador ahora mismo (para ver al rival elegir en tiempo real).</summary>
        public struct LobbyPick : INetworkSerializable, System.IEquatable<LobbyPick>
        {
            public ulong clientId;
            public int characterIndex;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref clientId);
                serializer.SerializeValue(ref characterIndex);
            }

            public bool Equals(LobbyPick other) => clientId == other.clientId && characterIndex == other.characterIndex;
        }

        readonly NetworkList<LobbyPick> picks = new();

        readonly Dictionary<ulong, int> selections = new();
        readonly HashSet<ulong> readyClients = new();

        int localSelectedCharacter;
        bool localReady;

        public int ConnectedCount => connectedCount.Value;
        public int ReadyCount => readyCount.Value;
        public bool GameStarted => gameStarted.Value;
        public bool CharacterSelectionActive => characterSelectionActive.Value;
        public bool IsLocalReady => localReady;

        /// <summary>Hay algún otro jugador READY (aparte de mí).</summary>
        public bool IsRivalReady => readyCount.Value - (localReady ? 1 : 0) > 0;

        public int MaxPlayers =>
            spawnManager != null
                ? spawnManager.MaxPlayers
                : 2;

        public int LocalSelectedCharacter => localSelectedCharacter;

        public PlayerCharacterRoster Roster => spawnManager != null ? spawnManager.Roster : null;

        public PlayerCharacterDefinition SelectedCharacter => Roster != null ? Roster.Get(localSelectedCharacter) : null;

        public string SelectedCharacterName =>
            SelectedCharacter != null ? SelectedCharacter.displayName : "NO CHARACTER";

        public Sprite SelectedCharacterPortrait =>
            SelectedCharacter != null ? SelectedCharacter.portrait : null;

        /// <summary>Personaje que tiene marcado el rival ahora mismo (-1 si no hay rival).</summary>
        public int RivalSelectedCharacter
        {
            get
            {
                if (!IsSpawned) return -1;
                ulong me = NetworkManager.LocalClientId;
                foreach (LobbyPick pick in picks)
                    if (pick.clientId != me) return pick.characterIndex;
                return -1;
            }
        }

        public PlayerCharacterDefinition RivalCharacter
        {
            get
            {
                int index = RivalSelectedCharacter;
                return index >= 0 && Roster != null ? Roster.Get(index) : null;
            }
        }

        public string RivalCharacterName => RivalCharacter != null ? RivalCharacter.displayName : "";

        void Awake()
        {
            Current = this;

            if (spawnManager == null)
                spawnManager = FindFirstObjectByType<PlayerSpawnManager>();
        }

        public override void OnDestroy()
        {
            if (Current == this)
                Current = null;

            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            localReady = false;
            localSelectedCharacter = 0;

            if (!IsServer)
            {
                // Que el rival vea desde ya lo que tengo marcado.
                SelectRpc(localSelectedCharacter);
                return;
            }

            picks.Clear();
            selections.Clear();
            readyClients.Clear();
            readyCount.Value = 0;
            gameStarted.Value = false;

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            UpdateConnectedCount(null);

            if (IsClient)
                SelectRpc(localSelectedCharacter);
        }

        public override void OnNetworkDespawn()
        {
            localReady = false;

            if (!IsServer)
                return;

            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ============================================================
        // CONEXIONES
        // ============================================================

        void OnClientConnected(ulong clientId)
        {
            UpdateConnectedCount(null);
        }

        void OnClientDisconnected(ulong clientId)
        {
            selections.Remove(clientId);
            readyClients.Remove(clientId);
            for (int i = picks.Count - 1; i >= 0; i--)
                if (picks[i].clientId == clientId) picks.RemoveAt(i);

            readyCount.Value = readyClients.Count;

            // Durante este callback el cliente que se va puede seguir en la lista: no lo contamos.
            UpdateConnectedCount(clientId);
        }

        void UpdateConnectedCount(ulong? leaving)
        {
            connectedCount.Value = NetworkManager.ConnectedClientsIds.Count(id => id != leaving);

            // Cuando están todos los jugadores conectados,
            // el servidor activa la selección para TODOS.
            if (!gameStarted.Value)
                characterSelectionActive.Value = connectedCount.Value >= MaxPlayers;
        }

        // ============================================================
        // SELECCIÓN LOCAL
        // ============================================================

        public void PreviousCharacter() => StepCharacter(-1);

        public void NextCharacter() => StepCharacter(1);

        void StepCharacter(int step)
        {
            if (localReady || Roster == null || Roster.Count == 0)
                return;

            localSelectedCharacter = (localSelectedCharacter + step + Roster.Count) % Roster.Count;

            if (IsSpawned)
                SelectRpc(localSelectedCharacter);
        }

        // El servidor apunta lo que tiene marcado cada jugador y lo replica a todos.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void SelectRpc(int characterIndex, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (gameStarted.Value || readyClients.Contains(clientId))
                return;

            var pick = new LobbyPick { clientId = clientId, characterIndex = characterIndex };
            for (int i = 0; i < picks.Count; i++)
            {
                if (picks[i].clientId != clientId) continue;
                if (!picks[i].Equals(pick)) picks[i] = pick;
                return;
            }
            picks.Add(pick);
        }

        // ============================================================
        // READY
        // ============================================================

        public void ReadyLocalPlayer()
        {
            if (localReady)
                return;

            if (!IsSpawned)
            {
                Debug.LogWarning("CharacterLobbyManager todavía no está conectado.");
                return;
            }

            localReady = true;

            ReadyRpc(localSelectedCharacter);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ReadyRpc(int characterIndex, RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;

            if (gameStarted.Value)
                return;

            if (readyClients.Contains(clientId))
                return;

            if (Roster == null || Roster.Get(characterIndex) == null)
            {
                Debug.LogWarning($"Cliente {clientId} eligió un personaje no válido.");
                ReadyRejectedRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
                return;
            }

            selections[clientId] = characterIndex;
            readyClients.Add(clientId);
            readyCount.Value = readyClients.Count;

            Debug.Log($"Jugador {clientId} READY. {readyCount.Value}/{MaxPlayers}");

            if (connectedCount.Value >= MaxPlayers && readyCount.Value >= MaxPlayers)
                StartGameServer();
        }

        // Si el servidor no acepta la elección, el jugador puede volver a elegir.
        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        void ReadyRejectedRpc(RpcParams rpcParams)
        {
            localReady = false;
        }

        // ============================================================
        // EMPEZAR PARTIDA
        // ============================================================

        void StartGameServer()
        {
            if (!IsServer || gameStarted.Value)
                return;

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!selections.TryGetValue(clientId, out int characterIndex))
                {
                    Debug.LogError($"No hay selección para el cliente {clientId}.");
                    return;
                }

                if (!spawnManager.ServerSpawnPlayer(clientId, characterIndex))
                {
                    Debug.LogError($"No se pudo crear al jugador {clientId}.");
                    return;
                }
            }

            gameStarted.Value = true;

            Debug.Log("Todos los jugadores están READY. Comienza la partida.");
        }
    }
}
