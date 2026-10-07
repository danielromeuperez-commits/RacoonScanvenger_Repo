using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    /// Menú de red (OnGUI, no necesita Canvas) preparado para builds. Ponlo en el mismo GameObject que el NetworkManager.
    ///  - LOCAL: host y cliente por IP directa (mismo PC o misma red). El host muestra su IP de red local.
    ///  - ONLINE: sesión con Relay de Unity (redes distintas, sin abrir puertos). El host le pone nombre
    ///    a la partida y recibe un código. Si es pública, aparece en "Buscar partidas".
    /// Cada ejecución inicia sesión con una identidad anónima propia, así que se pueden abrir varias builds
    /// en el mismo PC. Las partidas se etiquetan con Application.version y la búsqueda solo muestra las de
    /// la misma versión, para que builds distintas no se mezclen.
    /// La ventana se arrastra desde la barra de título, se minimiza con "-" y se oculta/muestra con F1.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport), typeof(PlayerSpawnManager))]
    public class NetworkTestMenu : MonoBehaviour
    {
        const int WindowId = 7431;
        const float WindowWidth = 300f;
        const float TitleBarHeight = 20f;
        const int MaxSessionNameLength = 30;
        const string VersionProperty = "version";
        const float ReferenceScreenHeight = 720f;

        [SerializeField] ushort port = 7777;
        [Tooltip("Escala de la UI. 0 = automática según la altura de la pantalla.")]
        [SerializeField, Min(0f)] float uiScale;
        [SerializeField] bool startMinimized;
        [SerializeField] KeyCode toggleKey = KeyCode.F1;

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
        string localAddresses;

        // Búsqueda de partidas
        IList<ISessionInfo> foundSessions;
        Vector2 sessionListScroll;

        // Ventana
        Rect windowRect = new Rect(10, 10, WindowWidth, 0);
        bool minimized;
        bool hidden;

        static Task signInTask;

        void Awake()
        {
            // Imprescindible para probar varias builds en el mismo PC: sin esto la ventana sin foco
            // se congela y el host/cliente acaba desconectándose por timeout.
            Application.runInBackground = true;

            minimized = startMinimized;
            sessionName = $"Partida {UnityEngine.Random.Range(1000, 10000)}";
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            spawnManager = GetComponent<PlayerSpawnManager>();
            networkManager.OnClientDisconnectCallback += OnClientDisconnect;
            networkManager.OnClientStopped += OnClientStopped;
        }

        void OnDestroy()
        {
            if (networkManager != null)
            {
                networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
                networkManager.OnClientStopped -= OnClientStopped;
            }
            UnsubscribeSession(session);
        }

        void OnClientDisconnect(ulong clientId)
        {
            // En el cliente: me he desconectado yo (host cerrado, partida llena, sin conexión, timeout...).
            if (!networkManager.IsServer && clientId == networkManager.LocalClientId)
            {
                string reason = networkManager.DisconnectReason;
                status = string.IsNullOrEmpty(reason) ? "Desconectado del host." : $"Desconectado: {reason}";
            }
        }

        // Se llama al parar el cliente o el host por cualquier motivo. Si la red se ha caído pero seguimos
        // dentro de la sesión online, salimos de ella para no quedarnos colgados (ni dejarla visible en la búsqueda).
        void OnClientStopped(bool wasHost)
        {
            if (session != null) _ = LeaveSessionAsync();
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
            if (networkManager.StartHost())
            {
                LogNetworkConfig("Host");
                localAddresses = GetLocalAddresses();
                status = $"Host local en el puerto {port}";
            }
            else
            {
                status = "No se pudo iniciar el host (¿puerto ocupado?).";
            }
        }

        void StartLocalClient()
        {
            string address = joinAddress.Trim();
            if (!IPAddress.TryParse(address, out _))
            {
                status = "IP no válida.";
                return;
            }

            ApplySelection();
            transport.SetConnectionData(address, port);
            if (networkManager.StartClient())
            {
                LogNetworkConfig("Cliente");
                status = $"Conectando a {address}:{port}...";
            }
            else
            {
                status = "No se pudo iniciar el cliente.";
            }
        }

        // Si el host rechaza al cliente con "NetworkConfig mismatch", compara esta línea en las dos consolas:
        // el hash incluye los prefabs de red registrados, así que la lista de Prefabs dice cuál falta o sobra.
        void LogNetworkConfig(string role)
        {
            NetworkConfig config = networkManager.NetworkConfig;
            string prefabs = string.Join(", ", config.Prefabs.NetworkPrefabOverrideLinks.Keys.OrderBy(hash => hash));
            Debug.Log($"[Red] {role} · Config hash {config.GetConfig(false)} · TickRate {config.TickRate} · " +
                      $"Approval {config.ConnectionApproval} · SceneMgmt {config.EnableSceneManagement} · Prefabs: {prefabs}");
        }

        static string GetLocalAddresses()
        {
            try
            {
                var addresses = new List<string>();
                foreach (IPAddress address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                        addresses.Add(address.ToString());
                return addresses.Count > 0 ? string.Join(", ", addresses) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------- Online (Relay) ----------------

        // Una única inicialización/login compartida aunque se pulsen varios botones seguidos.
        Task EnsureSignedInAsync()
        {
            if (signInTask == null || signInTask.IsFaulted || signInTask.IsCanceled)
                signInTask = SignInAsync();
            return signInTask;
        }

        static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                // Perfil distinto en cada ejecución: si no, dos builds (o editores) en el mismo PC comparten
                // el token guardado y el servicio los trata como el mismo jugador (no pueden unirse entre sí).
                // Máx. 30 caracteres, solo letras, números, '-' y '_'.
                var options = new InitializationOptions();
                options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 16));
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
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        // Indexada para poder filtrar la búsqueda por versión del juego.
                        [VersionProperty] = new SessionProperty(Application.version, VisibilityPropertyOptions.Public, PropertyIndex.String1),
                    },
                }.WithRelayNetwork();
                // La sesión configura el transporte con Relay y llama a StartHost por nosotros.
                SetSession(await MultiplayerService.Instance.CreateSessionAsync(options));
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
                SetSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(code));
                status = $"Unido a \"{session.Name}\" ({session.Code})";
            });
        }

        string SanitizedSessionName()
        {
            string name = sessionName.Trim();
            if (name.Length == 0) name = $"Partida {UnityEngine.Random.Range(1000, 10000)}";
            return name.Length > MaxSessionNameLength ? name.Substring(0, MaxSessionNameLength) : name;
        }

        // ---------------- Sesión ----------------

        void SetSession(ISession newSession)
        {
            UnsubscribeSession(session);
            session = newSession;
            if (session == null) return;
            session.RemovedFromSession += OnSessionEnded;
            session.Deleted += OnSessionEnded;
        }

        void UnsubscribeSession(ISession target)
        {
            if (target == null) return;
            target.RemovedFromSession -= OnSessionEnded;
            target.Deleted -= OnSessionEnded;
        }

        // El host ha cerrado la partida o nos han expulsado.
        void OnSessionEnded()
        {
            if (this == null) return;
            SetSession(null);
            if (networkManager.IsListening) networkManager.Shutdown();
            status = "La partida se ha cerrado.";
        }

        async Task LeaveSessionAsync()
        {
            ISession leaving = session;
            if (leaving == null) return;
            SetSession(null);
            try
            {
                // En el host borra la sesión; en el cliente solo sale de ella.
                await leaving.LeaveAsync();
            }
            catch (Exception exception)
            {
                // Puede fallar si la sesión ya no existe (host caído, sin conexión...): no es grave.
                Debug.LogWarning($"NetworkTestMenu: error al salir de la sesión: {exception.Message}");
            }
        }

        // ---------------- Buscar partidas (solo públicas y de la misma versión) ----------------

        async void SearchSessions()
        {
            await RunBusy("Buscando partidas...", async () =>
            {
                await EnsureSignedInAsync();
                var options = new QuerySessionsOptions();
                options.FilterOptions.Add(new FilterOption(FilterField.StringIndex1, Application.version, FilterOperation.Equal));
                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(options);
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
                SetSession(await MultiplayerService.Instance.JoinSessionByIdAsync(info.Id));
                foundSessions = null;
                status = $"Unido a \"{session.Name}\" ({session.Code})";
            });
        }

        async void Disconnect()
        {
            await RunBusy("Saliendo...", async () =>
            {
                await LeaveSessionAsync();
                if (networkManager.IsListening) networkManager.Shutdown();
                localAddresses = null;
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
                status = $"Error: {FriendlyError(exception)}";
                // Si algo falló a medias (p. ej. la sesión se creó pero Relay no), no dejamos la red a medio abrir.
                if (session == null && networkManager != null && networkManager.IsListening) networkManager.Shutdown();
            }
            finally
            {
                busy = false;
            }
        }

        static string FriendlyError(Exception exception) =>
            Application.internetReachability == NetworkReachability.NotReachable ? "Sin conexión a internet." : exception.Message;

        // ---------------- UI ----------------

        float Scale => uiScale > 0f ? uiScale : Mathf.Max(1f, Screen.height / ReferenceScreenHeight);

        void OnGUI()
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == toggleKey)
            {
                hidden = !hidden;
                current.Use();
            }
            if (hidden) return;

            float scale = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            // GUILayout.Window crece para ajustarse al contenido pero no encoge solo: al minimizar (o al
            // vaciarse la lista de partidas) se reinicia la altura para que se recalcule.
            if (current.type == EventType.Layout) windowRect.height = 0;
            windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, WindowTitle(), GUILayout.Width(WindowWidth));

            // Que no se pueda arrastrar fuera de la pantalla.
            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;
            windowRect.x = Mathf.Clamp(windowRect.x, 0, Mathf.Max(0, screenWidth - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0, Mathf.Max(0, screenHeight - TitleBarHeight));
        }

        string WindowTitle()
        {
            string title = $"Red v{Application.version}";
            if (!minimized) return title;
            if (networkManager.IsListening)
            {
                string role = networkManager.IsHost ? "Host" : "Cliente";
                return networkManager.IsServer
                    ? $"{title} · {role} {networkManager.ConnectedClientsIds.Count}/{MaxPlayers}"
                    : $"{title} · {role}";
            }
            return busy ? $"{title} · ..." : title;
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
                GUILayout.Label($"{toggleKey}: ocultar/mostrar este menú");
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
            joinAddress = GUILayout.TextField(joinAddress, 45, GUILayout.Width(140));
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
            if (GUILayout.Button("Pegar", GUILayout.Width(50))) joinCode = GUIUtility.systemCopyBuffer.Trim();
            if (GUILayout.Button("Unirse")) JoinOnline();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            DrawSessionSearch();

            GUILayout.Space(10);
            if (GUILayout.Button("Salir del juego")) Quit();
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
            else if (networkManager.IsHost && !string.IsNullOrEmpty(localAddresses))
            {
                GUILayout.Label($"Tu IP local: {localAddresses}");
            }

            if (networkManager.IsConnectedClient && !networkManager.IsServer)
            {
                ulong ping = transport.GetCurrentRtt(NetworkManager.ServerClientId);
                GUILayout.Label($"Ping: {ping} ms");
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

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
