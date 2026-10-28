using Racoon.Gameplay;
using System.Collections.Generic;
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
    /// </summary>
  
    public class CharacterLobbyManager : NetworkBehaviour
    {
        [SerializeField] PlayerSpawnManager spawnManager;

        readonly NetworkVariable<int> connectedCount = new(0);
        readonly NetworkVariable<int> readyCount = new(0);
        readonly NetworkVariable<bool> gameStarted = new(false);
        readonly NetworkVariable<bool> characterSelectionActive = new(false);

        readonly Dictionary<ulong, int> selections = new();
        readonly HashSet<ulong> readyClients = new();

        int localSelectedCharacter;
        bool localReady;

        public int ConnectedCount => connectedCount.Value;
        public int ReadyCount => readyCount.Value;
        public bool GameStarted => gameStarted.Value;
        public bool CharacterSelectionActive =>
    characterSelectionActive.Value;
        public bool IsLocalReady => localReady;

        public int MaxPlayers =>
            spawnManager != null
                ? spawnManager.MaxPlayers
                : 2;

        public int LocalSelectedCharacter =>
            localSelectedCharacter;

        void Awake()
        {
            if (spawnManager == null)
            {
                spawnManager =
                    FindFirstObjectByType<PlayerSpawnManager>();
            }
        }

        public override void OnNetworkSpawn()
        {
            localReady = false;
            localSelectedCharacter = 0;

            if (!IsServer)
                return;

            NetworkManager.OnClientConnectedCallback +=
                OnClientConnected;

            NetworkManager.OnClientDisconnectCallback +=
                OnClientDisconnected;

            UpdateConnectedCount();
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            NetworkManager.OnClientConnectedCallback -=
                OnClientConnected;

            NetworkManager.OnClientDisconnectCallback -=
                OnClientDisconnected;
        }

        // ============================================================
        // CONEXIONES
        // ============================================================

        void OnClientConnected(ulong clientId)
        {
            UpdateConnectedCount();
        }

        void OnClientDisconnected(ulong clientId)
        {
            selections.Remove(clientId);
            readyClients.Remove(clientId);

            readyCount.Value =
                readyClients.Count;

            UpdateConnectedCount();
        }

        void UpdateConnectedCount()
        {
            connectedCount.Value =
                NetworkManager.ConnectedClientsIds.Count;

            // Cuando están todos los jugadores conectados,
            // el servidor activa la selección para TODOS.
            if (!gameStarted.Value)
            {
                characterSelectionActive.Value =
                    connectedCount.Value >= MaxPlayers;
            }
        }

        // ============================================================
        // SELECCIÓN LOCAL
        // ============================================================

        public void PreviousCharacter()
        {
            if (localReady || spawnManager == null)
                return;

            PlayerCharacterRoster roster =
                spawnManager.Roster;

            if (roster == null || roster.Count == 0)
                return;

            localSelectedCharacter =
                (localSelectedCharacter - 1 + roster.Count)
                % roster.Count;
        }

        public void NextCharacter()
        {
            if (localReady || spawnManager == null)
                return;

            PlayerCharacterRoster roster =
                spawnManager.Roster;

            if (roster == null || roster.Count == 0)
                return;

            localSelectedCharacter =
                (localSelectedCharacter + 1)
                % roster.Count;
        }

        public string SelectedCharacterName
        {
            get
            {
                if (spawnManager == null ||
                    spawnManager.Roster == null)
                    return "NO CHARACTER";

                PlayerCharacterDefinition character =
                    spawnManager.Roster.Get(
                        localSelectedCharacter
                    );

                return character != null
                    ? character.displayName
                    : "NO CHARACTER";
            }
        }

        public Sprite SelectedCharacterPortrait
        {
            get
            {
                if (spawnManager == null ||
                    spawnManager.Roster == null)
                    return null;

                PlayerCharacterDefinition character =
                    spawnManager.Roster.Get(
                        localSelectedCharacter
                    );

                return character != null
                    ? character.portrait
                    : null;
            }
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
                Debug.LogWarning(
                    "CharacterLobbyManager todavía no está conectado."
                );

                return;
            }

            localReady = true;

            ReadyRpc(localSelectedCharacter);
        }

        [Rpc(
            SendTo.Server,
            InvokePermission = RpcInvokePermission.Everyone
        )]
        void ReadyRpc(
            int characterIndex,
            RpcParams rpcParams = default)
        {
            ulong clientId =
                rpcParams.Receive.SenderClientId;

            if (gameStarted.Value)
                return;

            if (readyClients.Contains(clientId))
                return;

            PlayerCharacterRoster roster =
                spawnManager.Roster;

            if (roster == null ||
                roster.Get(characterIndex) == null)
            {
                Debug.LogWarning(
                    $"Cliente {clientId} eligió " +
                    $"un personaje no válido."
                );

                return;
            }

            selections[clientId] =
                characterIndex;

            readyClients.Add(clientId);

            readyCount.Value =
                readyClients.Count;

            Debug.Log(
                $"Jugador {clientId} READY. " +
                $"{readyCount.Value}/{MaxPlayers}"
            );

            if (connectedCount.Value >= MaxPlayers &&
                readyCount.Value >= MaxPlayers)
            {
                StartGameServer();
            }
        }

        // ============================================================
        // EMPEZAR PARTIDA
        // ============================================================

        void StartGameServer()
        {
            if (!IsServer || gameStarted.Value)
                return;

            foreach (
                ulong clientId
                in NetworkManager.ConnectedClientsIds)
            {
                if (!selections.TryGetValue(
                        clientId,
                        out int characterIndex))
                {
                    Debug.LogError(
                        $"No hay selección para " +
                        $"el cliente {clientId}."
                    );

                    return;
                }

                bool spawned =
                    spawnManager.ServerSpawnPlayer(
                        clientId,
                        characterIndex
                    );

                if (!spawned)
                {
                    Debug.LogError(
                        $"No se pudo crear al jugador " +
                        $"{clientId}."
                    );

                    return;
                }
            }

            gameStarted.Value = true;

            Debug.Log(
                "Todos los jugadores están READY. " +
                "Comienza la partida."
            );
        }
    }
}