using Racoon.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Va en el GameObject del NetworkManager. Decide, en el HOST, qué prefab y en qué spawn point
    /// aparece cada jugador que se conecta (incluido el propio host):
    ///  1. El jugador elige personaje y lo manda en ConnectionData (SetLocalSelection).
    ///  2. El host lo lee en la aprobación de conexión, reserva un spawn point libre y le dice a
    ///     Netcode qué prefab crear y dónde. Netcode lo spawnea y lo replica a todos.
    /// Así la escena no necesita ningún jugador colocado a mano.
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
            if (networkManager == null) return;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            networkManager.OnServerStopped -= OnServerStopped;
        }

        /// <summary>Llamar ANTES de hostear o unirse (local u online).</summary>
        public void SetLocalSelection(int characterIndex)
        {
            networkManager.NetworkConfig.ConnectionData = new ConnectionPayload { characterIndex = characterIndex }.Encode();
        }

        // Solo se ejecuta en el host/servidor.
        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (networkManager.ConnectedClientsIds.Count >= maxPlayers)
            {
                Reject(response, "La partida está llena.");
                return;
            }

            ConnectionPayload payload = ConnectionPayload.Decode(request.Payload);
            PlayerCharacterDefinition character = roster != null ? roster.Get(payload.characterIndex) ?? roster.Get(0) : null;
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

        static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = reason;
        }

        void OnClientDisconnect(ulong clientId)
        {
            if (networkManager.IsServer) PlayerSpawnPoints.Release(clientId);
        }

        void OnServerStopped(bool wasHost) => PlayerSpawnPoints.ReleaseAll();

        void ValidateRoster()
        {
            if (roster == null || roster.Count == 0)
            {
                Debug.LogError("PlayerSpawnManager: asigna un Character Roster con al menos un personaje.", this);
                return;
            }

            foreach (PlayerCharacterDefinition character in roster.Characters)
            {
                if (character == null || character.prefab == null)
                {
                    Debug.LogError($"PlayerSpawnManager: el personaje '{character?.name}' no tiene prefab.", this);
                    continue;
                }

                bool registered = networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists
                    .Exists(list => list != null && list.Contains(character.prefab.gameObject));
                if (!registered)
                    Debug.LogError($"PlayerSpawnManager: añade '{character.prefab.name}' a la Network Prefabs List del NetworkManager.", this);
            }
        }
    }
}
