using Racoon.Network;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú de conexión y selección de personaje.
///
/// La pantalla que se ve sale del estado de la red, no de lo que se pulsó:
///  - Sin red: la página elegida (modo, local u online).
///  - Conectando o esperando rival: panel de espera (IP o código, jugadores, cancelar).
///  - Todos conectados (CharacterLobbyManager.CharacterSelectionActive): selección de personaje.
///  - Partida empezada: se oculta. Si la red se cae, vuelve a aparecer con el motivo.
///
/// Así da igual quién arranque la red (este menú o el NetworkTestMenu con F1) y una desconexión
/// nunca deja el menú colgado.
///
/// Las referencias a NetworkTestMenu y CharacterLobbyManager se buscan solas si están vacías,
/// para poder usar el menú como prefab. Los campos opcionales vacíos se ignoran.
/// La UI maquetada se genera con Racoon → Menús → Montar lobby de conexión.
/// </summary>
public class ConnectionMenu : MonoBehaviour
{
    enum Page
    {
        Mode,
        Local,
        Online
    }

    [Header("Menú completo")]
    [SerializeField] GameObject connectionMenu;

    [Header("Red (se buscan solas si están vacías)")]
    [SerializeField] NetworkTestMenu networkTestMenu;
    [SerializeField] CharacterLobbyManager characterLobbyManager;

    // ============================================================
    // LOCAL
    // ============================================================

    [Header("Local")]
    [SerializeField] TMP_InputField ipInputField;
    [SerializeField] GameObject waitingPlayerText;
    [SerializeField] TMP_Text hostIPAddressText;
    [SerializeField] TMP_Text playerCountText;

    // ============================================================
    // ONLINE
    // ============================================================

    [Header("Online (opcional)")]
    [SerializeField] TMP_InputField joinCodeInputField;

    [Tooltip("Texto WAITING FOR PLAYER... del OnlinePanel.")]
    [SerializeField] GameObject onlineWaitingPlayerText;

    [Tooltip("Texto donde se muestra el código generado por Relay.")]
    [SerializeField] TMP_Text onlineSessionCodeText;

    [Tooltip("Texto PLAYERS: 1/2 del OnlinePanel.")]
    [SerializeField] TMP_Text onlinePlayerCountText;

    // ============================================================
    // ESPERA
    // ============================================================

    [Header("Espera (opcional: sin él, la espera se ve dentro del panel local/online)")]
    [SerializeField] GameObject waitingPanel;
    [SerializeField] TMP_Text waitingTitleText;
    [SerializeField] TMP_Text sessionCodeText;
    [SerializeField] GameObject copyCodeButton;

    // ============================================================
    // SELECCIÓN DE PERSONAJE
    // ============================================================

    [Header("Selección de personaje")]
    [SerializeField] GameObject characterSelectionPanel;
    [SerializeField] TMP_Text characterNameText;
    [SerializeField] Image characterPortraitImage;
    [SerializeField] TMP_Text readyStatusText;

    [Tooltip("Opcional: aro del retrato, se tiñe con el color del personaje.")]
    [SerializeField] Image characterRingImage;

    [Tooltip("Opcional: color de cada personaje del roster, en el mismo orden.")]
    [SerializeField] Color[] characterColors;

    [SerializeField] TMP_Text localReadyText;
    [SerializeField] TMP_Text rivalReadyText;

    [Header("Botones de personaje")]
    [SerializeField] Button previousCharacterButton;
    [SerializeField] Button nextCharacterButton;
    [SerializeField] Button readyButton;

    // ============================================================
    // PANELES
    // ============================================================

    [Header("Paneles")]
    [SerializeField] GameObject modeSelectionPanel;
    [SerializeField] GameObject localPanel;
    [SerializeField] GameObject onlinePanel;

    [Header("Gameplay UI")]
    [SerializeField] GameObject gameplayUI;
    // ============================================================
    // MENSAJES
    // ============================================================

    [Header("Mensajes (opcional)")]
    [SerializeField] TMP_Text statusText;

    // ============================================================
    // ESCENAS
    // ============================================================

    [Header("Escenas")]
    [SerializeField] string mainMenuSceneName = "SCN_MainMenu";

    Page page = Page.Mode;

    GameObject shownPanel;

    bool gameplayStarted;
    bool wasConnected;

    // Mensaje que ya estaba al navegar:
    // no se repite en la página nueva.
    string dismissedStatus;

    // ============================================================
    // INICIO
    // ============================================================

    void Start()
    {
        if (networkTestMenu == null)
        {
            networkTestMenu =
                FindFirstObjectByType<NetworkTestMenu>();
        }

        if (networkTestMenu == null)
        {
            Debug.LogError(
                "ConnectionMenu: no hay NetworkTestMenu en la escena (va en el NetworkManager).",
                this
            );
        }

        if (gameplayUI != null)
            gameplayUI.SetActive(false);

        SetMenuVisible(true);

        // Ocultamos los textos de espera al empezar.
        SetLocalWaitingUI(false);

        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    CharacterLobbyManager Lobby =>
        characterLobbyManager != null
            ? characterLobbyManager
            : CharacterLobbyManager.Current;

    // ============================================================
    // NAVEGACIÓN (botones)
    // ============================================================

    public void OpenMainSelection()
    {
        OpenPage(Page.Mode);
    }

    public void OpenLocal()
    {
        OpenPage(Page.Local);
    }

    public void OpenOnline()
    {
        OpenPage(Page.Online);
    }

    void OpenPage(Page target)
    {
        page = target;

        dismissedStatus =
            networkTestMenu != null
                ? networkTestMenu.Status
                : null;

        Refresh();
    }

    // ============================================================
    // LOCAL
    // ============================================================

    public void HostLocal()
    {
        if (!CanStartNetwork())
            return;

        networkTestMenu.StartLocalHost();
    }

    public void JoinLocal()
    {
        if (!CanStartNetwork())
            return;

        string address =
            ipInputField != null
                ? ipInputField.text.Trim()
                : "";

        // Sin IP escrita:
        // este mismo PC (lo normal al probar con dos ventanas).
        networkTestMenu.SetJoinAddress(
            string.IsNullOrEmpty(address)
                ? "127.0.0.1"
                : address
        );

        networkTestMenu.StartLocalClient();
    }

    // ============================================================
    // ONLINE
    // ============================================================

    public void CreateOnline()
    {
        if (!CanStartNetwork())
            return;

        networkTestMenu.CreateOnlineGame();
    }

    public void JoinOnline()
    {
        if (!CanStartNetwork())
            return;

        string code =
            joinCodeInputField != null
                ? joinCodeInputField.text.Trim()
                : "";

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning(
                "Introduce el código de la partida Online.",
                this
            );

            return;
        }

        networkTestMenu.SetJoinCode(
            code.ToUpperInvariant()
        );

        networkTestMenu.JoinOnlineGame();
    }

    public void CopySessionCode()
    {
        if (networkTestMenu != null &&
            !string.IsNullOrEmpty(networkTestMenu.SessionCode))
        {
            GUIUtility.systemCopyBuffer =
                networkTestMenu.SessionCode;
        }
    }

    /// <summary>
    /// Cancela la espera o sale de la partida
    /// y vuelve a la página en la que estaba.
    /// </summary>
    public void CancelConnection()
    {
        if (networkTestMenu != null)
        {
            networkTestMenu.LeaveGame();
        }
        else if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    bool CanStartNetwork()
    {
        if (networkTestMenu == null)
        {
            Debug.LogError(
                "No hay NetworkTestMenu asignado.",
                this
            );

            return false;
        }

        NetworkManager manager =
            NetworkManager.Singleton;

        return
            !networkTestMenu.IsBusy &&
            (manager == null ||
             !manager.IsListening);
    }

    // ============================================================
    // SELECCIÓN DE PERSONAJE
    // ============================================================

    public void PreviousCharacter()
    {
        if (Lobby != null)
            Lobby.PreviousCharacter();

        Refresh();
    }

    public void NextCharacter()
    {
        if (Lobby != null)
            Lobby.NextCharacter();

        Refresh();
    }

    public void ReadyCharacter()
    {
        if (Lobby != null)
            Lobby.ReadyLocalPlayer();

        Refresh();
    }

    // ============================================================
    // ESTADO
    // ============================================================

    void Refresh()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        CharacterLobbyManager lobby =
            Lobby;

        bool listening =
            manager != null &&
            manager.IsListening;

        bool busy =
            networkTestMenu != null &&
            networkTestMenu.IsBusy;

        // La red se ha caído
        // (host cerrado, timeout, cancelar...):
        // de vuelta al menú con el motivo.
        if (wasConnected && !listening)
        {
            dismissedStatus = null;

            if (gameplayStarted)
            {
                gameplayStarted = false;

                SetMenuVisible(true);
            }
        }

        wasConnected = listening;

        if (gameplayStarted)
            return;

        // ========================================================
        // PARTIDA EMPEZADA
        // ========================================================

        if (listening &&
            lobby != null &&
            lobby.IsSpawned &&
            lobby.GameStarted)
        {
            BeginGameplay();
            return;
        }

        // ========================================================
        // SELECCIÓN DE PERSONAJE
        // ========================================================

        bool selecting =
            listening &&
            lobby != null &&
            lobby.IsSpawned &&
            lobby.CharacterSelectionActive;

        // ========================================================
        // ESPERANDO
        // ========================================================

        bool waiting =
            !selecting &&
            (listening || busy);

        GameObject target;

        if (selecting)
        {
            target =
                characterSelectionPanel;

            RefreshCharacterUI(lobby);
        }
        else if (
            waiting &&
            waitingPanel != null)
        {
            // Si tenemos un panel de espera independiente.
            target =
                waitingPanel;
        }
        else
        {
            // Si NO tenemos un WaitingPanel separado,
            // la espera se queda dentro del panel
            // Local u Online correspondiente.

            target =
                waiting &&
                waitingPanel == null
                    ? (
                        page == Page.Online
                            ? onlinePanel
                            : localPanel
                      )
                    : page == Page.Local
                        ? localPanel
                        : page == Page.Online
                            ? onlinePanel
                            : modeSelectionPanel;
        }

        ShowOnly(target);

        if (waiting)
        {
            RefreshWaitingUI(manager);
        }

        // Controla los textos de espera dentro
        // de LocalPanel / OnlinePanel.
        SetLocalWaitingUI(
            waiting &&
            waitingPanel == null
        );

        RefreshStatus(
            selecting,
            waiting
        );
    }

    // ============================================================
    // MOSTRAR SOLO UN PANEL
    // ============================================================

    void ShowOnly(GameObject target)
    {
        SetActive(
            modeSelectionPanel,
            target == modeSelectionPanel
        );

        SetActive(
            localPanel,
            target == localPanel
        );

        SetActive(
            onlinePanel,
            target == onlinePanel
        );

        SetActive(
            waitingPanel,
            target == waitingPanel
        );

        SetActive(
            characterSelectionPanel,
            target == characterSelectionPanel
        );

        if (target == shownPanel)
            return;

        shownPanel =
            target;

        SelectFirstButton(target);
    }

    // Para poder moverse con mando o teclado:
    // al cambiar de panel se selecciona
    // su primer botón.
    static void SelectFirstButton(
        GameObject panel)
    {
        if (panel == null ||
            EventSystem.current == null)
            return;

        foreach (
            Selectable selectable
            in panel.GetComponentsInChildren<Selectable>())
        {
            if (selectable.interactable &&
                selectable is Button)
            {
                EventSystem.current
                    .SetSelectedGameObject(
                        selectable.gameObject
                    );

                return;
            }
        }
    }

    // ============================================================
    // UI DE ESPERA
    // ============================================================

    void RefreshWaitingUI(
        NetworkManager manager)
    {
        bool connected =
            manager != null &&
            manager.IsListening &&
            (
                manager.IsServer ||
                manager.IsConnectedClient
            );

        bool isHost =
            manager != null &&
            manager.IsServer;

        string code =
            networkTestMenu != null
                ? networkTestMenu.SessionCode
                : null;

        // Antes solo comprobábamos si había código.
        // Ahora también miramos si estamos
        // dentro de la página Online.
        //
        // Esto es importante porque mientras
        // Relay está creando la sesión todavía
        // no tenemos código.
        bool online =
            page == Page.Online ||
            !string.IsNullOrEmpty(code);

        int maxPlayers =
            networkTestMenu != null
                ? networkTestMenu.MaxPlayerCount
                : 2;

        int players =
            Lobby != null &&
            Lobby.IsSpawned
                ? Lobby.ConnectedCount
                : networkTestMenu != null
                    ? networkTestMenu.ConnectedPlayerCount
                    : 0;

        // ========================================================
        // WAITING PANEL INDEPENDIENTE
        // ========================================================

        if (waitingTitleText != null)
        {
            waitingTitleText.text =
                !connected
                    ? "CONECTANDO..."
                    : isHost
                        ? "ESPERANDO RIVAL"
                        : "ESPERANDO AL HOST";
        }

        if (sessionCodeText != null)
        {
            sessionCodeText.text =
                online
                    ? code
                    : "";
        }

        SetActive(
            sessionCodeText != null
                ? sessionCodeText.gameObject
                : null,
            online &&
            !string.IsNullOrEmpty(code)
        );

        SetActive(
            copyCodeButton,
            online &&
            isHost &&
            !string.IsNullOrEmpty(code)
        );

        // ========================================================
        // LOCAL
        // ========================================================

        if (hostIPAddressText != null)
        {
            hostIPAddressText.text =
                isHost && !online
                    ? $"TU IP: {networkTestMenu?.LocalIPAddress}"
                    : "";

            hostIPAddressText
                .gameObject
                .SetActive(
                    isHost &&
                    !online
                );
        }

        if (playerCountText != null &&
            !online)
        {
            playerCountText.text =
                $"JUGADORES: " +
                $"{(connected ? Mathf.Max(players, 1) : 0)}/" +
                $"{maxPlayers}";
        }

        // ========================================================
        // ONLINE
        // ========================================================

        if (online)
        {
            if (onlineWaitingPlayerText != null)
            {
                onlineWaitingPlayerText
                    .SetActive(true);
            }

            if (onlineSessionCodeText != null)
            {
                // Mientras Unity Services / Relay
                // está creando la partida.
                if (networkTestMenu != null &&
                    networkTestMenu.IsBusy &&
                    string.IsNullOrEmpty(code))
                {
                    onlineSessionCodeText.text =
                        "CREATING GAME...";
                }

                // Cuando Relay ya ha generado
                // el código de la sesión.
                else if (
                    !string.IsNullOrEmpty(code))
                {
                    onlineSessionCodeText.text =
                        $"CODE: {code}";
                }

                // Cliente intentando conectarse.
                else
                {
                    onlineSessionCodeText.text =
                        "CONNECTING...";
                }
            }

            if (onlinePlayerCountText != null)
            {
                onlinePlayerCountText.text =
                    $"PLAYERS: " +
                    $"{(connected ? Mathf.Max(players, 1) : 0)}/" +
                    $"{maxPlayers}";
            }
        }
    }

    // ============================================================
    // TEXTOS DE ESPERA DENTRO DE LOCAL / ONLINE
    // ============================================================

    void SetLocalWaitingUI(bool active)
    {
        if (waitingPanel != null)
            return;

        bool localWaiting =
            active &&
            page == Page.Local;

        bool onlineWaiting =
            active &&
            page == Page.Online;

        // ========================================================
        // LOCAL
        // ========================================================

        SetActive(
            waitingPlayerText,
            localWaiting
        );

        SetActive(
            hostIPAddressText != null
                ? hostIPAddressText.gameObject
                : null,
            localWaiting
        );

        SetActive(
            playerCountText != null
                ? playerCountText.gameObject
                : null,
            localWaiting
        );

        // ========================================================
        // ONLINE
        // ========================================================

        SetActive(
            onlineWaitingPlayerText,
            onlineWaiting
        );

        SetActive(
            onlineSessionCodeText != null
                ? onlineSessionCodeText.gameObject
                : null,
            onlineWaiting
        );

        SetActive(
            onlinePlayerCountText != null
                ? onlinePlayerCountText.gameObject
                : null,
            onlineWaiting
        );
    }

    // ============================================================
    // MENSAJES
    // ============================================================

    void RefreshStatus(
        bool selecting,
        bool waiting)
    {
        if (statusText == null)
            return;

        string status =
            networkTestMenu != null
                ? networkTestMenu.Status
                : null;

        string message =
            selecting
                ? ""
                : waiting ||
                  status != dismissedStatus
                    ? status
                    : "";

        statusText.text =
            message ?? "";

        statusText
            .gameObject
            .SetActive(
                !string.IsNullOrEmpty(message)
            );
    }

    // ============================================================
    // UI DE PERSONAJES
    // ============================================================

    void RefreshCharacterUI(
        CharacterLobbyManager lobby)
    {
        if (characterNameText != null)
        {
            characterNameText.text =
                lobby.SelectedCharacterName;
        }

        if (characterPortraitImage != null)
        {
            Sprite portrait =
                lobby.SelectedCharacterPortrait;

            characterPortraitImage.sprite =
                portrait;

            characterPortraitImage.enabled =
                portrait != null;
        }

        if (characterRingImage != null &&
            characterColors != null &&
            characterColors.Length > 0)
        {
            characterRingImage.color =
                characterColors[
                    Mathf.Clamp(
                        lobby.LocalSelectedCharacter,
                        0,
                        characterColors.Length - 1
                    )
                ];
        }

        if (readyStatusText != null)
        {
            readyStatusText.text =
                $"PLAYERS READY: " +
                $"{lobby.ReadyCount}/" +
                $"{lobby.MaxPlayers}";
        }

        if (localReadyText != null)
        {
            localReadyText.text =
                lobby.IsLocalReady
                    ? "TÚ · ¡LISTO!"
                    : "TÚ · ELIGIENDO";
        }

        if (rivalReadyText != null)
        {
            rivalReadyText.text =
                lobby.IsRivalReady
                    ? "RIVAL · ¡LISTO!"
                    : "RIVAL · ELIGIENDO";
        }

        bool canChange =
            !lobby.IsLocalReady;

        if (previousCharacterButton != null)
        {
            previousCharacterButton
                .interactable =
                canChange;
        }

        if (nextCharacterButton != null)
        {
            nextCharacterButton
                .interactable =
                canChange;
        }

        if (readyButton != null)
        {
            readyButton.interactable =
                canChange;

            TMP_Text buttonText =
                readyButton
                    .GetComponentInChildren<TMP_Text>();

            if (buttonText != null)
            {
                buttonText.text =
                    lobby.IsLocalReady
                        ? "READY!"
                        : "READY";
            }
        }
    }

    // ============================================================
    // COMIENZO DE PARTIDA
    // ============================================================

    void BeginGameplay()
    {
        gameplayStarted = true;

        shownPanel = null;

        // Ocultamos todos los menús de conexión.
        SetMenuVisible(false);

        // Mostramos la UI del gameplay.
        if (gameplayUI != null)
            gameplayUI.SetActive(true);

        Debug.Log(
            "Todos los jugadores están READY. Comienza la partida."
        );
    }

    void SetMenuVisible(bool visible)
    {
        if (connectionMenu != null)
        {
            // Si el menú es este mismo objeto,
            // no se desactiva porque dejaría
            // de vigilar la red:
            // se apagan sus Canvas.
            if (connectionMenu == gameObject)
            {
                foreach (
                    Canvas canvas
                    in GetComponentsInChildren<Canvas>(true))
                {
                    canvas.enabled =
                        visible;
                }
            }
            else
            {
                connectionMenu.SetActive(
                    visible
                );
            }
        }

        Cursor.visible =
            visible;

        Cursor.lockState =
            visible
                ? CursorLockMode.None
                : CursorLockMode.Locked;
    }

    // ============================================================
    // OTROS
    // ============================================================

    public void CloseConnectionMenu()
    {
        gameplayStarted = true;

        SetMenuVisible(false);
    }

    public void BackToMainMenu()
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(
            mainMenuSceneName
        );
    }

    static void SetActive(
        GameObject target,
        bool active)
    {
        if (target != null &&
            target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}