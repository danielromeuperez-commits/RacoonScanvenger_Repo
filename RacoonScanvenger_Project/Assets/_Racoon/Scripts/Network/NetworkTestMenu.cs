using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Menú de pruebas (OnGUI, no necesita Canvas). Ponlo en el mismo GameObject que el NetworkManager.
    ///  - LOCAL: host y cliente por IP directa (mismo PC o misma red).
    ///  - ONLINE: sesión con Relay de Unity (redes distintas, sin abrir puertos). El host recibe un código.
    /// Además limita la partida a 'maxPlayers' con Connection Approval.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public class NetworkTestMenu : MonoBehaviour
    {
        [SerializeField] ushort port = 7777;
        [SerializeField] int maxPlayers = 2;
        [SerializeField] float uiScale = 1.5f;

        NetworkManager networkManager;
        UnityTransport transport;
        ISession session;

        string joinAddress = "127.0.0.1";
        string joinCode = "";
        string status = "";
        bool busy;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();

            // 1vs1: el host rechaza a un tercer jugador.
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = ApproveConnection;
            networkManager.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void OnDestroy()
        {
            if (networkManager != null) networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool hasRoom = networkManager.ConnectedClientsIds.Count < maxPlayers;
            response.Approved = hasRoom;
            response.CreatePlayerObject = hasRoom;
            if (!hasRoom) response.Reason = "La partida está llena.";
        }

        void OnClientDisconnect(ulong clientId)
        {
            // En el cliente: me he desconectado yo (host cerrado, partida llena, sin conexión...).
            if (!networkManager.IsServer && clientId == networkManager.LocalClientId)
            {
                string reason = networkManager.DisconnectReason;
                status = string.IsNullOrEmpty(reason) ? "Desconectado del host." : $"Desconectado: {reason}";
            }
        }

        // ---------------- Local (IP directa) ----------------

        void StartLocalHost()
        {
            // 0.0.0.0 = acepta conexiones de este PC y de otros de la misma red.
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            status = networkManager.StartHost() ? $"Host local en el puerto {port}" : "No se pudo iniciar el host.";
        }

        void StartLocalClient()
        {
            transport.SetConnectionData(joinAddress.Trim(), port);
            status = networkManager.StartClient() ? $"Conectando a {joinAddress}:{port}..." : "No se pudo iniciar el cliente.";
        }

        // ---------------- Online (Relay) ----------------

        async Task EnsureSignedInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
#if UNITY_EDITOR
                // Cada editor/jugador virtual necesita una identidad distinta o el servicio
                // los trataría como el mismo jugador.
                options.SetProfile("editor" + Guid.NewGuid().ToString("N").Substring(0, 8));
#endif
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        async void StartOnlineHost()
        {
            await RunBusy("Creando partida online...", async () =>
            {
                await EnsureSignedInAsync();
                var options = new SessionOptions { MaxPlayers = maxPlayers, IsPrivate = true }.WithRelayNetwork();
                // La sesión configura el transporte con Relay y llama a StartHost por nosotros.
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                status = $"Partida creada. Código: {session.Code}";
            });
        }

        async void JoinOnline()
        {
            string code = joinCode.Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                status = "Escribe el código de la partida.";
                return;
            }

            await RunBusy("Uniéndose...", async () =>
            {
                await EnsureSignedInAsync();
                // Configura Relay y llama a StartClient por nosotros.
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                status = $"Unido a la partida {session.Code}";
            });
        }

        async void Disconnect()
        {
            await RunBusy("Saliendo...", async () =>
            {
                if (session != null)
                {
                    ISession leaving = session;
                    session = null;
                    await leaving.LeaveAsync();
                }
                if (networkManager.IsListening) networkManager.Shutdown();
                status = "Desconectado.";
            });
        }

        async Task RunBusy(string message, Func<Task> action)
        {
            if (busy) return;
            busy = true;
            status = message;
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                status = $"Error: {exception.Message}";
            }
            finally
            {
                busy = false;
            }
        }

        // ---------------- UI ----------------

        void OnGUI()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            GUILayout.BeginArea(new Rect(10, 10, 260, 400), GUI.skin.box);

            GUI.enabled = !busy;
            if (!networkManager.IsListening && session == null)
                DrawStartMenu();
            else
                DrawConnectedMenu();
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
            GUILayout.EndArea();
        }

        void DrawStartMenu()
        {
            GUILayout.Label("LOCAL (mismo PC / misma red)");
            if (GUILayout.Button("Host local")) StartLocalHost();
            GUILayout.BeginHorizontal();
            joinAddress = GUILayout.TextField(joinAddress, GUILayout.Width(140));
            if (GUILayout.Button("Unirse por IP")) StartLocalClient();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("ONLINE (Relay, redes distintas)");
            if (GUILayout.Button("Crear partida online")) StartOnlineHost();
            GUILayout.BeginHorizontal();
            joinCode = GUILayout.TextField(joinCode, 12, GUILayout.Width(140));
            if (GUILayout.Button("Unirse con código")) JoinOnline();
            GUILayout.EndHorizontal();
        }

        void DrawConnectedMenu()
        {
            string role = networkManager.IsHost ? "Host" : networkManager.IsClient ? "Cliente" : "Iniciando...";
            GUILayout.Label($"Modo: {role}");
            if (networkManager.IsServer)
                GUILayout.Label($"Jugadores: {networkManager.ConnectedClientsIds.Count}/{maxPlayers}");

            if (session != null && !string.IsNullOrEmpty(session.Code))
            {
                GUILayout.Label($"Código: {session.Code}");
                if (GUILayout.Button("Copiar código")) GUIUtility.systemCopyBuffer = session.Code;
            }

            if (GUILayout.Button("Desconectar")) Disconnect();
        }
    }
}
