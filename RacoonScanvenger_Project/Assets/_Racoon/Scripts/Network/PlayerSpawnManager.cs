using Racoon.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Gestiona la conexión y el spawn de los jugadores.
    ///
    /// Los clientes pueden conectarse sin crear todavía su PlayerObject.
    /// El personaje se crea más tarde, cuando todos han elegido personaje
    /// y han marcado READY en el lobby.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class PlayerSpawnManager : MonoBehaviour
    {
        [SerializeField] PlayerCharacterRoster roster;
        [SerializeField, Min(1)] int maxPlayers = 2;

        NetworkManager networkManager;

        public PlayerCharacterRoster Roster => roster;
        public int MaxPlayers => maxPlayers;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = ApproveConnection;

            networkManager.OnClientDisconnectCallback += OnClientDisconnect;
            networkManager.OnServerStopped += OnServerStopped;

            ValidateRoster();
        }

        void OnDestroy()
        {
            if (networkManager == null)
                return;

            networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            networkManager.OnServerStopped -= OnServerStopped;
        }

        /// <summary>
        /// Lo usa el NetworkTestMenu en escenas sin lobby (spawn directo al conectar).
        /// Si la escena tiene CharacterLobbyManager, la selección se hace allí y esto se ignora.
        /// </summary>
        public void SetLocalSelection(int characterIndex)
        {
            networkManager.NetworkConfig.ConnectionData =
                new ConnectionPayload
                {
                    characterIndex = characterIndex
                }.Encode();
        }

        // ============================================================
        // APROBACIÓN DE CONEXIÓN
        // ============================================================

        void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            if (networkManager.ConnectedClientsIds.Count >= maxPlayers)
            {
                Reject(response, "La partida está llena.");
                return;
            }

            // Escenas de prueba sin lobby (NetworkTestMenu): el personaje elegido viene en
            // ConnectionData y se crea ya en la aprobación, como antes del lobby.
            if (CharacterLobbyManager.Current == null)
            {
                ApproveWithDirectSpawn(request, response);
                return;
            }

            // Permitimos entrar al cliente...
            response.Approved = true;

            // ...pero NO creamos todavía su personaje.
            response.CreatePlayerObject = false;
        }

        void ApproveWithDirectSpawn(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            ConnectionPayload payload = ConnectionPayload.Decode(request.Payload);
            PlayerCharacterDefinition character =
                roster != null ? roster.Get(payload.characterIndex) ?? roster.Get(0) : null;

            if (character == null || character.prefab == null)
            {
                Reject(response, "Personaje no válido.");
                return;
            }

            Pose? spawn = PlayerSpawnPoints.Reserve(request.ClientNetworkId);
            if (spawn == null)
            {
                Reject(response, "No quedan spawn points libres.");
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = true;
            response.PlayerPrefabHash = character.prefab.PrefabIdHash;
            response.Position = spawn.Value.position;
            response.Rotation = spawn.Value.rotation;
        }

        static void Reject(
            NetworkManager.ConnectionApprovalResponse response,
            string reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = reason;
        }

        // ============================================================
        // SPAWN DESPUÉS DEL READY
        // ============================================================

        /// <summary>
        /// Solo servidor.
        /// Crea el personaje elegido y lo convierte
        /// en PlayerObject del cliente indicado.
        /// </summary>
        public bool ServerSpawnPlayer(
            ulong clientId,
            int characterIndex)
        {
            if (!networkManager.IsServer)
            {
                Debug.LogWarning(
                    "ServerSpawnPlayer solo puede ejecutarse en el servidor.",
                    this
                );

                return false;
            }

            if (!networkManager.ConnectedClients.TryGetValue(
                    clientId,
                    out NetworkClient client))
            {
                Debug.LogWarning(
                    $"No existe el cliente {clientId}.",
                    this
                );

                return false;
            }

            // Evita crear el jugador dos veces.
            if (client.PlayerObject != null)
                return true;

            PlayerCharacterDefinition character =
                roster != null
                    ? roster.Get(characterIndex)
                    : null;

            if (character == null || character.prefab == null)
            {
                Debug.LogError(
                    $"Personaje {characterIndex} no válido.",
                    this
                );

                return false;
            }

            Pose? spawn =
                PlayerSpawnPoints.Reserve(clientId);

            if (spawn == null)
            {
                Debug.LogError(
                    $"No queda Spawn Point para el cliente {clientId}.",
                    this
                );

                return false;
            }

            NetworkObject playerObject =
                Instantiate(
                    character.prefab,
                    spawn.Value.position,
                    spawn.Value.rotation
                );

            playerObject.SpawnAsPlayerObject(
                clientId,
                true
            );

            Debug.Log(
                $"Jugador {clientId} creado como " +
                $"{character.displayName}."
            );

            return true;
        }

        // ============================================================
        // LIMPIEZA
        // ============================================================

        void OnClientDisconnect(ulong clientId)
        {
            if (networkManager.IsServer)
                PlayerSpawnPoints.Release(clientId);
        }

        void OnServerStopped(bool wasHost)
        {
            PlayerSpawnPoints.ReleaseAll();
        }

        // ============================================================
        // VALIDACIÓN
        // ============================================================

        void ValidateRoster()
        {
            if (roster == null || roster.Count == 0)
            {
                Debug.LogError(
                    "PlayerSpawnManager: asigna un Character Roster " +
                    "con al menos un personaje.",
                    this
                );

                return;
            }

            foreach (
                PlayerCharacterDefinition character
                in roster.Characters)
            {
                if (character == null ||
                    character.prefab == null)
                {
                    Debug.LogError(
                        $"PlayerSpawnManager: el personaje " +
                        $"'{character?.name}' no tiene prefab.",
                        this
                    );

                    continue;
                }

                bool registered =
                    networkManager.NetworkConfig.Prefabs
                        .NetworkPrefabsLists
                        .Exists(
                            list =>
                                list != null &&
                                list.Contains(
                                    character.prefab.gameObject
                                )
                        );

                if (!registered)
                {
                    Debug.LogError(
                        $"PlayerSpawnManager: añade " +
                        $"'{character.prefab.name}' a la " +
                        $"Network Prefabs List.",
                        this
                    );
                }
            }
        }
    }
}