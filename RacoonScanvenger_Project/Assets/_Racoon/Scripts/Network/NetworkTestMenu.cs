using System;
using System.Collections.Generic;
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
    ///  - ONLINE: sesión con Relay de Unity (redes distintas, sin abrir puertos). El host le pone nombre
    ///    a la partida y recibe un código. Si es pública, aparece en "Buscar partidas".
    /// Antes de conectar se elige personaje; PlayerSpawnManager se encarga del resto.
    /// La ventana se arrastra desde la barra de título y se minimiza con el botón "-".
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport), typeof(PlayerSpawnManager))]
    public class NetworkTestMenu : MonoBehaviour
    {
        const int WindowId = 7431;
        const float WindowWidth = 300f;
        const float TitleBarHeight = 20f;
        const int MaxSessionNameLength = 30;

        [SerializeField] ushort port = 7777;
        [SerializeField] float uiScale = 1.5f;
        [SerializeField] bool startMinimized;

        NetworkManager networkManager;
        UnityTransport transport;
        PlayerSpawnManager spawnManager;
        ISession session;
        int selectedCharacter;

        string joinAddress = "127.0.0.1";
        string joinCode = "";
        string sessionName = "";
        bool isPublic = true;
        string status = "";
        bool busy;

        // Búsqueda de partidas
        IList<ISessionInfo> foundSessions;
        Vector2 sessionListScroll;

        // Ventana
        Rect windowRect = new Rect(10, 10, WindowWidth, 0);
        bool minimized;

        void Awake()
        {
            minimized = startMinimized;
            sessionName = $"Partida {UnityEngine.Random.Range(1000, 10000)}";
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
                var options = new SessionOptions
                {
                    Name = SanitizedSessionName(),
                    MaxPlayers = MaxPlayers,
                    // Las privadas solo se pueden unir con código; las públicas salen en la búsqueda.
                    IsPrivate = !isPublic,
                }.WithRelayNetwork();
                // La sesión configura el transporte con Relay y llama a StartHost por nosotros.
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                status = $"Partida \"{session.Name}\" creada. Código: {session.Code}";
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
                status = $"Unido a \"{session.Name}\" ({session.Code})";
            });
        }

        string SanitizedSessionName()
        {
            string name = sessionName.Trim();
            if (name.Length == 0) name = $"Partida {UnityEngine.Random.Range(1000, 10000)}";
            return name.Length > MaxSessionNameLength ? name.Substring(0, MaxSessionNameLength) : name;
        }

        // ---------------- Buscar partidas (solo públicas) ----------------

        async void SearchSessions()
        {
            await RunBusy("Buscando partidas...", async () =>
            {
                await EnsureSignedInAsync();
                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
                foundSessions = results.Sessions;
                sessionListScroll = Vector2.zero;
                status = foundSessions.Count == 0 ? "No hay partidas públicas." : $"{foundSessions.Count} partida(s) encontrada(s).";
            });
        }

        async void JoinFoundSession(ISessionInfo info)
        {
            await RunBusy($"Uniéndose a \"{info.Name}\"...", async () =>
            {
                await EnsureSignedInAsync();
                ApplySelection();
                session = await MultiplayerService.Instance.JoinSessionByIdAsync(info.Id);
                foundSessions = null;
                status = $"Unido a \"{session.Name}\" ({session.Code})";
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

            // GUILayout.Window crece para ajustarse al contenido pero no encoge solo: al minimizar (o al
            // vaciarse la lista de partidas) se reinicia la altura para que se recalcule.
            if (Event.current.type == EventType.Layout) windowRect.height = 0;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, WindowTitle(), GUILayout.Width(WindowWidth));

            // Que no se pueda arrastrar fuera de la pantalla.
            float screenWidth = Screen.width / uiScale;
            float screenHeight = Screen.height / uiScale;
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, screenWidth - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, screenHeight - TitleBarHeight));
        }

        string WindowTitle()
        {
            if (!minimized) return "Red";
            if (networkManager.IsListening)
            {
                string role = networkManager.IsHost ? "Host" : "Cliente";
                return networkManager.IsServer
                    ? $"Red · {role} {networkManager.ConnectedClientsIds.Count}/{MaxPlayers}"
                    : $"Red · {role}";
            }
            return busy ? "Red · ..." : "Red";
        }

        void DrawWindow(int id)
        {
            // Botón de minimizar/restaurar en la barra de título (antes que DragWindow para que tenga prioridad).
            if (GUI.Button(new Rect(windowRect.width - 26, 2, 22, TitleBarHeight - 4), minimized ? "+" : "-"))
                minimized = !minimized;

            if (!minimized)
            {
                GUI.enabled = !busy;
                if (!networkManager.IsListening && session == null)
                    DrawStartMenu();
                else
                    DrawConnectedMenu();
                GUI.enabled = true;

                if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
            }

            // Arrastrar desde la barra de título (sin tapar el botón de minimizar).
            GUI.DragWindow(new Rect(0, 0, windowRect.width - 30, TitleBarHeight));
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Nombre", GUILayout.Width(55));
            sessionName = GUILayout.TextField(sessionName, MaxSessionNameLength);
            GUILayout.EndHorizontal();
            isPublic = GUILayout.Toggle(isPublic, " Pública (sale en la búsqueda)");
            if (GUILayout.Button("Crear partida online")) StartOnlineHost();
            GUILayout.BeginHorizontal();
            joinCode = GUILayout.TextField(joinCode, 12, GUILayout.Width(140));
            if (GUILayout.Button("Unirse con código")) JoinOnline();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            DrawSessionSearch();
        }

        void DrawSessionSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("BUSCAR PARTIDAS");
            if (GUILayout.Button(foundSessions == null ? "Buscar" : "Actualizar", GUILayout.Width(90))) SearchSessions();
            GUILayout.EndHorizontal();

            if (foundSessions == null || foundSessions.Count == 0) return;

            // Altura acotada: con muchas partidas aparece scroll en vez de estirar la ventana.
            float listHeight = Mathf.Min(foundSessions.Count * 26f + 6f, 160f);
            sessionListScroll = GUILayout.BeginScrollView(sessionListScroll, GUI.skin.box, GUILayout.Height(listHeight));
            foreach (ISessionInfo info in foundSessions)
            {
                int players = info.MaxPlayers - info.AvailableSlots;
                bool canJoin = info.AvailableSlots > 0 && !info.IsLocked && !info.HasPassword;

                GUILayout.BeginHorizontal();
                GUILayout.Label(info.Name, GUILayout.ExpandWidth(true));
                GUILayout.Label($"{players}/{info.MaxPlayers}", GUILayout.Width(32));
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && canJoin;
                if (GUILayout.Button(canJoin ? "Unirse" : info.AvailableSlots > 0 ? "Cerrada" : "Llena", GUILayout.Width(60)))
                    JoinFoundSession(info);
                GUI.enabled = wasEnabled;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        void DrawConnectedMenu()
        {
            string role = networkManager.IsHost ? "Host" : networkManager.IsClient ? "Cliente" : "Iniciando...";
            GUILayout.Label($"Modo: {role}");
            if (networkManager.IsServer)
                GUILayout.Label($"Jugadores: {networkManager.ConnectedClientsIds.Count}/{MaxPlayers}");

            if (session != null && !string.IsNullOrEmpty(session.Name))
                GUILayout.Label($"Partida: {session.Name}");
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
                    GUILayout.Label("Pulsa un botón del mando o una tecla EN ESTA VENTANA para controlar tu personaje.");
                else if (input.PairedDevice != null)
                    GUILayout.Label($"Controlando con: {input.PairedDevice.displayName} ({input.ActiveKind})");
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
