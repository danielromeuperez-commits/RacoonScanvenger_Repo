using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Racoon.Player;
using UnityEngine;

namespace Racoon.Network
{
    /// <summary>
    /// Menú de pruebas (OnGUI, no necesita Canvas). Ponlo en el mismo GameObject que el NetworkManager.
    ///  - LOCAL: host y cliente por IP directa (mismo PC o misma red).
    ///  - ONLINE: sesión con Relay de Unity (redes distintas, sin abrir puertos). El host recibe un código.
    /// Antes de conectar se elige personaje; PlayerSpawnManager se encarga del resto.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport), typeof(PlayerSpawnManager))]
    public class NetworkTestMenu : MonoBehaviour
    {
        [SerializeField] ushort port = 7777;
        [SerializeField] float uiScale = 1.5f;

        NetworkManager networkManager;
        UnityTransport transport;
        PlayerSpawnManager spawnManager;
        ISession session;
        int selectedCharacter;

        string joinAddress = "127.0.0.1";
        string joinCode = "";
        string status = "";
        bool busy;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            spawnManager = GetComponent<PlayerSpawnManager>();
            networkManager.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void OnDestroy()
        {
            if (networkManager != null) networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
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

        int MaxPlayers => spawnManager.MaxPlayers;

        // El personaje elegido viaja al host dentro de los datos de conexión.
        void ApplySelection() => spawnManager.SetLocalSelection(selectedCharacter);

        void StartLocalHost()
        {
            ApplySelection();
            // 0.0.0.0 = acepta conexiones de este PC y de otros de la misma red.
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            status = networkManager.StartHost() ? $"Host local en el puerto {port}" : "No se pudo iniciar el host.";
        }

        void StartLocalClient()
        {
            ApplySelection();
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
                ApplySelection();
                var options = new SessionOptions { MaxPlayers = MaxPlayers, IsPrivate = true }.WithRelayNetwork();
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
                ApplySelection();
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
            GUILayout.BeginArea(new Rect(10, 10, 280, 460), GUI.skin.box);

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
            DrawCharacterSelection();
            GUILayout.Space(10);

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
                GUILayout.Label($"Jugadores: {networkManager.ConnectedClientsIds.Count}/{MaxPlayers}");

            if (session != null && !string.IsNullOrEmpty(session.Code))
            {
                GUILayout.Label($"Código: {session.Code}");
                if (GUILayout.Button("Copiar código")) GUIUtility.systemCopyBuffer = session.Code;
            }

            PlayerController local = PlayerController.Local;
            if (local != null)
            {
                PlayerInputHandler input = local.InputHandler;
                if (input.IsWaitingForDevice)
                    GUILayout.Label("Pulsa un botón del mando (o una tecla) EN ESTA VENTANA para controlar tu personaje.");
                else if (input.PairedDevice != null)
                    GUILayout.Label($"Controlando con: {input.PairedDevice.displayName}");
            }

            if (GUILayout.Button("Desconectar")) Disconnect();
        }

        void DrawCharacterSelection()
        {
            var roster = spawnManager.Roster;
            if (roster == null || roster.Count == 0)
            {
                GUILayout.Label("Asigna un Character Roster en PlayerSpawnManager.");
                return;
            }

            selectedCharacter = Mathf.Clamp(selectedCharacter, 0, roster.Count - 1);
            var character = roster.Get(selectedCharacter);

            GUILayout.Label("PERSONAJE");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30)))
                selectedCharacter = (selectedCharacter - 1 + roster.Count) % roster.Count;
            if (character != null && character.portrait != null)
                GUILayout.Label(character.portrait.texture, GUILayout.Width(40), GUILayout.Height(40));
            GUILayout.Label(character != null ? character.displayName : "(vacío)", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30)))
                selectedCharacter = (selectedCharacter + 1) % roster.Count;
            GUILayout.EndHorizontal();
        }
    }
}
