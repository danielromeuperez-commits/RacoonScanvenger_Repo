using Racoon.Network;
using Racoon.Player;
using Racoon.UI;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú de conexión y selección de personaje.
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

    [Header("Espera (opcional)")]
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

    [Header("Visor 3D y rival (se buscan solos por nombre si están vacíos)")]
    [Tooltip("RawImage del visor del personaje propio (RAW_Preview dentro de IMG_CharacterPortrait).")]
    [SerializeField] RawImage localPreviewImage;
    [Tooltip("RawImage del visor del rival (RAW_Preview dentro de IMG_RivalPortrait).")]
    [SerializeField] RawImage rivalPreviewImage;
    [SerializeField] Image rivalPortraitImage;
    [SerializeField] TMP_Text rivalNameText;

    [Header("Buscador de partidas online (se buscan solos por nombre si están vacíos)")]
    [SerializeField] Button searchSessionsButton;
    [SerializeField] Button[] sessionRowButtons;

    // ============================================================
    // PANELES
    // ============================================================

    [Header("Paneles")]
    [SerializeField] GameObject modeSelectionPanel;
    [SerializeField] GameObject localPanel;
    [SerializeField] GameObject onlinePanel;

    [Header("Gameplay UI")]
    [SerializeField] GameObject gameplayUI;

    [Header("Cuenta atrás")]
    [SerializeField] MatchCountdownUI countdownUI;

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

    string dismissedStatus;

    [Header("Navegación")]
    [Tooltip("Cuánto hay que mover el stick para activar la navegación con mando.")]
    [SerializeField, Range(0.1f, 1f)] float gamepadDeadzone = 0.35f;

    CanvasGroup menuCanvasGroup;

    bool navigationMode;

    CharacterPreviewStage localPreview;
    CharacterPreviewStage rivalPreview;
    TMP_Text onlineWaitingLabel;
    string onlineWaitingDefaultText;
    bool searchRequested;

    // ============================================================
    // INICIO
    // ============================================================

    void Start()
    {
        if (networkTestMenu == null)
        {
            networkTestMenu = FindFirstObjectByType<NetworkTestMenu>();
        }

        if (gameplayUI != null)
            gameplayUI.SetActive(false);

        if (networkTestMenu == null)
        {
            Debug.LogError(
                "ConnectionMenu: no hay NetworkTestMenu en la escena (va en el NetworkManager).",
                this
            );
        }

        AutoWireExtras();

        SetMenuVisible(true);
        SetLocalWaitingUI(false);
        SetupNavigationCanvasGroup();
        Refresh();
        SetMouseMode();
    }

    void Update()
    {
        Refresh();
        HandleInputMode();
        HandleBackInput();
    }

    void LateUpdate()
    {
        if (!navigationMode &&
            EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;

            if (selected.GetComponent<TMP_InputField>() != null)
                return;

            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ============================================================
    // VISOR 3D, RIVAL Y BUSCADOR: ENGANCHE
    // ============================================================

    // Los elementos están en PF_HUD_Connection_Menu; se buscan por nombre para no tener que tocar las escenas.
    void AutoWireExtras()
    {
        Transform selection = characterSelectionPanel != null ? characterSelectionPanel.transform : null;
        Transform online = onlinePanel != null ? onlinePanel.transform : null;

        if (localPreviewImage == null) localPreviewImage = FindIn<RawImage>(selection, "IMG_CharacterPortrait/RAW_Preview");
        if (rivalPreviewImage == null) rivalPreviewImage = FindIn<RawImage>(selection, "IMG_RivalPortrait/RAW_Preview");
        if (rivalPortraitImage == null) rivalPortraitImage = FindIn<Image>(selection, "IMG_RivalPortrait");
        if (rivalNameText == null) rivalNameText = FindIn<TMP_Text>(selection, "TXT_RivalName");

        // Cada visor con su capa y su peana para que no se vean el uno al otro.
        localPreview = SetupPreview(localPreviewImage, 30, new Vector3(-20f, -500f, 0f));
        rivalPreview = SetupPreview(rivalPreviewImage, 31, new Vector3(20f, -500f, 0f));

        if (searchSessionsButton == null) searchSessionsButton = FindIn<Button>(online, "BTN_SearchOnline");
        if (sessionRowButtons == null || sessionRowButtons.Length == 0)
        {
            var rows = new System.Collections.Generic.List<Button>();
            for (int i = 0; ; i++)
            {
                Button row = FindIn<Button>(online, $"BTN_SessionRow_{i}");
                if (row == null) break;
                rows.Add(row);
            }
            sessionRowButtons = rows.ToArray();
        }

        if (searchSessionsButton != null) searchSessionsButton.onClick.AddListener(SearchOnlineGames);
        for (int i = 0; i < sessionRowButtons.Length; i++)
        {
            int index = i;
            if (sessionRowButtons[i] != null) sessionRowButtons[i].onClick.AddListener(() => JoinFoundGame(index));
        }

        if (onlineWaitingPlayerText != null)
        {
            onlineWaitingLabel = onlineWaitingPlayerText.GetComponent<TMP_Text>();
            if (onlineWaitingLabel != null) onlineWaitingDefaultText = onlineWaitingLabel.text;
        }
    }

    static T FindIn<T>(Transform root, string path) where T : Component
    {
        if (root == null) return null;
        Transform t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    static CharacterPreviewStage SetupPreview(RawImage image, int layer, Vector3 stagePosition)
    {
        if (image == null) return null;
        if (!image.TryGetComponent(out CharacterPreviewStage stage))
            stage = image.gameObject.AddComponent<CharacterPreviewStage>();
        // La peana se monta al mostrar el primer personaje: aquí aún se pueden fijar capa y posición.
        stage.Layer = layer;
        stage.StagePosition = stagePosition;
        return stage;
    }

    // ============================================================
    // NAVEGACIÓN RATÓN / TECLADO / MANDO
    // ============================================================

    void SetupNavigationCanvasGroup()
    {
        GameObject menuRoot = connectionMenu != null ? connectionMenu : gameObject;

        menuCanvasGroup = menuRoot.GetComponent<CanvasGroup>();

        if (menuCanvasGroup == null)
        {
            menuCanvasGroup = menuRoot.AddComponent<CanvasGroup>();
        }

        menuCanvasGroup.alpha = 1f;
        menuCanvasGroup.interactable = true;
        menuCanvasGroup.blocksRaycasts = true;
    }

    void HandleInputMode()
    {
        if (MouseUsedThisFrame())
        {
            SetMouseMode();
            return;
        }

        if (GamepadUsedThisFrame())
        {
            SetNavigationMode();
            return;
        }

        if (KeyboardNavigationUsedThisFrame())
        {
            SetNavigationMode();
        }
    }

    bool MouseUsedThisFrame()
    {
        if (Mouse.current == null) return false;

        bool moved = Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f;
        bool clicked = Mouse.current.leftButton.wasPressedThisFrame ||
                       Mouse.current.rightButton.wasPressedThisFrame ||
                       Mouse.current.middleButton.wasPressedThisFrame;
        bool scrolled = Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f;

        return moved || clicked || scrolled;
    }

    bool GamepadUsedThisFrame()
    {
        if (Gamepad.current == null) return false;

        Vector2 leftStick = Gamepad.current.leftStick.ReadValue();
        Vector2 dpad = Gamepad.current.dpad.ReadValue();

        bool stickMoved = leftStick.sqrMagnitude > gamepadDeadzone * gamepadDeadzone;
        bool dpadMoved = dpad.sqrMagnitude > 0.01f;
        bool buttonPressed = Gamepad.current.buttonSouth.wasPressedThisFrame ||
                             Gamepad.current.buttonEast.wasPressedThisFrame;

        return stickMoved || dpadMoved || buttonPressed;
    }

    bool KeyboardNavigationUsedThisFrame()
    {
        if (Keyboard.current == null) return false;

        return Keyboard.current.upArrowKey.wasPressedThisFrame ||
               Keyboard.current.downArrowKey.wasPressedThisFrame ||
               Keyboard.current.leftArrowKey.wasPressedThisFrame ||
               Keyboard.current.rightArrowKey.wasPressedThisFrame;
    }

    void SetMouseMode()
    {
        navigationMode = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.blocksRaycasts = true;
        }

        if (EventSystem.current != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected == null || selected.GetComponent<TMP_InputField>() == null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    void SetNavigationMode()
    {
        navigationMode = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;

        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.blocksRaycasts = false;
        }

        if (EventSystem.current == null) return;
        if (EventSystem.current.currentSelectedGameObject != null) return;

        GameObject currentPanel = shownPanel != null ? shownPanel : modeSelectionPanel;
        SelectFirstButton(currentPanel);
    }

    // ============================================================
    // VOLVER ATRÁS
    // ============================================================

    void HandleBackInput()
    {
        bool keyboardBack = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        bool controllerBack = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;

        if (!keyboardBack && !controllerBack) return;
        if (gameplayStarted) return;

        NetworkManager manager = NetworkManager.Singleton;
        CharacterLobbyManager lobby = Lobby;

        bool listening = manager != null && manager.IsListening;
        bool selecting = listening && lobby != null && lobby.IsSpawned && lobby.CharacterSelectionActive;

        if (selecting)
        {
            if (page == Page.Online) ReturnFromOnline();
            else ReturnFromLocal();
            return;
        }

        if (page == Page.Local) { ReturnFromLocal(); return; }
        if (page == Page.Online) { ReturnFromOnline(); return; }
        if (page == Page.Mode) { BackToMainMenu(); }
    }

    CharacterLobbyManager Lobby =>
        characterLobbyManager != null ? characterLobbyManager : CharacterLobbyManager.Current;

    public void OpenMainSelection() => OpenPage(Page.Mode);
    public void OpenLocal() => OpenPage(Page.Local);
    public void OpenOnline() => OpenPage(Page.Online);

    void OpenPage(Page target)
    {
        page = target;
        dismissedStatus = networkTestMenu != null ? networkTestMenu.Status : null;
        Refresh();
    }

    // ============================================================
    // LOCAL Y ONLINE
    // ============================================================

    public void HostLocal()
    {
        if (!CanStartNetwork()) return;
        networkTestMenu.StartLocalHost();
    }

    public void JoinLocal()
    {
        if (!CanStartNetwork()) return;

        string address = ipInputField != null ? ipInputField.text.Trim() : "";
        networkTestMenu.SetJoinAddress(string.IsNullOrEmpty(address) ? "127.0.0.1" : address);
        networkTestMenu.StartLocalClient();
    }

    public void CreateOnline()
    {
        if (!CanStartNetwork()) return;
        networkTestMenu.CreateOnlineGame();
    }

    public void JoinOnline()
    {
        if (!CanStartNetwork()) return;

        string code = joinCodeInputField != null ? joinCodeInputField.text.Trim() : "";
        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Introduce el código de la partida Online.", this);
            return;
        }

        networkTestMenu.SetJoinCode(code.ToUpperInvariant());
        networkTestMenu.JoinOnlineGame();
    }

    /// <summary>Busca partidas online públicas (BTN_SearchOnline).</summary>
    public void SearchOnlineGames()
    {
        if (!CanStartNetwork()) return;
        searchRequested = true;
        networkTestMenu.SearchOnlineGames();
    }

    /// <summary>Entra en la partida de la fila <paramref name="index"/> (BTN_SessionRow_N).</summary>
    public void JoinFoundGame(int index)
    {
        if (!CanStartNetwork()) return;
        networkTestMenu.JoinFoundGame(index);
    }

    public void CopySessionCode()
    {
        if (networkTestMenu != null && !string.IsNullOrEmpty(networkTestMenu.SessionCode))
        {
            GUIUtility.systemCopyBuffer = networkTestMenu.SessionCode;
        }
    }

    public void CancelConnection()
    {
        if (networkTestMenu != null) networkTestMenu.LeaveGame();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    public void ReturnFromLocal()
    {
        page = Page.Mode;
        dismissedStatus = null;
        if (networkTestMenu != null) networkTestMenu.LeaveGame();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        Refresh();
    }

    public void ReturnFromOnline()
    {
        page = Page.Mode;
        dismissedStatus = null;
        if (networkTestMenu != null) networkTestMenu.LeaveGame();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
        Refresh();
    }

    bool CanStartNetwork()
    {
        if (networkTestMenu == null) return false;
        NetworkManager manager = NetworkManager.Singleton;
        return !networkTestMenu.IsBusy && (manager == null || !manager.IsListening);
    }

    // ============================================================
    // SELECCIÓN DE PERSONAJE
    // ============================================================

    public void PreviousCharacter() { if (Lobby != null) Lobby.PreviousCharacter(); Refresh(); }
    public void NextCharacter() { if (Lobby != null) Lobby.NextCharacter(); Refresh(); }
    public void ReadyCharacter() { if (Lobby != null) Lobby.ReadyLocalPlayer(); Refresh(); }

    // ============================================================
    // ESTADO
    // ============================================================

    void Refresh()
    {
        NetworkManager manager = NetworkManager.Singleton;
        CharacterLobbyManager lobby = Lobby;

        bool listening = manager != null && manager.IsListening;
        bool busy = networkTestMenu != null && networkTestMenu.IsBusy;

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
        if (gameplayStarted) return;

        if (listening && lobby != null && lobby.IsSpawned && lobby.GameStarted)
        {
            BeginGameplay();
            return;
        }

        bool selecting = listening && lobby != null && lobby.IsSpawned && lobby.CharacterSelectionActive;
        bool waiting = !selecting && (listening || busy);

        GameObject target;

        if (selecting)
        {
            target = characterSelectionPanel;
            RefreshCharacterUI(lobby);
        }
        else if (waiting && waitingPanel != null)
        {
            target = waitingPanel;
        }
        else
        {
            if (waiting && waitingPanel == null)
            {
                if (shownPanel == onlinePanel) target = onlinePanel;
                else if (shownPanel == localPanel) target = localPanel;
                else target = page == Page.Online ? onlinePanel : localPanel;
            }
            else
            {
                target = page == Page.Local ? localPanel : page == Page.Online ? onlinePanel : modeSelectionPanel;
            }
        }

        ShowOnly(target);

        if (waiting) RefreshWaitingUI(manager);

        SetLocalWaitingUI(waiting && waitingPanel == null);
        RefreshSessionList(target == onlinePanel && !listening, busy);
        RefreshStatus(selecting, waiting);
    }

    // Filas del buscador: «Nombre · 1/2». Sin partida, la fila se oculta; llena o cerrada, no se puede pulsar.
    void RefreshSessionList(bool browsing, bool busy)
    {
        var sessions = networkTestMenu != null ? networkTestMenu.FoundSessions : null;
        if (!browsing) searchRequested = false;

        if (searchSessionsButton != null)
        {
            SetActive(searchSessionsButton.gameObject, browsing);
            searchSessionsButton.interactable = !busy;
            TMP_Text label = searchSessionsButton.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = sessions == null ? "SEARCH" : "REFRESH";
        }

        if (sessionRowButtons != null)
        {
            for (int i = 0; i < sessionRowButtons.Length; i++)
            {
                Button row = sessionRowButtons[i];
                if (row == null) continue;
                bool show = browsing && sessions != null && i < sessions.Count;
                SetActive(row.gameObject, show);
                if (!show) continue;

                var info = sessions[i];
                bool canJoin = NetworkTestMenu.CanJoin(info);
                row.interactable = canJoin && !busy;
                TMP_Text label = row.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    int players = info.MaxPlayers - info.AvailableSlots;
                    string state = canJoin ? "" : info.AvailableSlots > 0 ? " · CLOSED" : " · FULL";
                    label.text = $"{info.Name} · {players}/{info.MaxPlayers}{state}";
                }
            }
        }

        // Estado de la búsqueda en el texto de espera del panel (fuera de la búsqueda vuelve a su texto de siempre).
        if (onlineWaitingLabel == null) return;
        if (browsing && searchRequested)
        {
            string message = busy && sessions == null ? "SEARCHING..."
                : sessions != null && sessions.Count == 0 ? "NO GAMES FOUND"
                : busy ? "SEARCHING..." : "";
            onlineWaitingLabel.text = message;
            SetActive(onlineWaitingPlayerText, !string.IsNullOrEmpty(message));
        }
        else if (onlineWaitingLabel.text != onlineWaitingDefaultText)
        {
            onlineWaitingLabel.text = onlineWaitingDefaultText;
        }
    }

    void ShowOnly(GameObject target)
    {
        SetActive(modeSelectionPanel, target == modeSelectionPanel);
        SetActive(localPanel, target == localPanel);
        SetActive(onlinePanel, target == onlinePanel);
        SetActive(waitingPanel, target == waitingPanel);
        SetActive(characterSelectionPanel, target == characterSelectionPanel);

        if (target == shownPanel) return;
        shownPanel = target;
        SelectFirstButton(target);
    }

    static void SelectFirstButton(GameObject panel)
    {
        if (panel == null || EventSystem.current == null) return;

        foreach (Selectable selectable in panel.GetComponentsInChildren<Selectable>())
        {
            if (selectable.interactable && selectable is Button)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
                return;
            }
        }
    }

    // ============================================================
    // UI DE ESPERA
    // ============================================================

    void RefreshWaitingUI(NetworkManager manager)
    {
        bool connected = manager != null && manager.IsListening && (manager.IsServer || manager.IsConnectedClient);
        bool isHost = manager != null && manager.IsServer;
        string code = networkTestMenu != null ? networkTestMenu.SessionCode : null;
        bool online = page == Page.Online || !string.IsNullOrEmpty(code);

        int maxPlayers = networkTestMenu != null ? networkTestMenu.MaxPlayerCount : 2;
        int players = Lobby != null && Lobby.IsSpawned ? Lobby.ConnectedCount : networkTestMenu != null ? networkTestMenu.ConnectedPlayerCount : 0;

        if (waitingTitleText != null)
        {
            waitingTitleText.text = !connected ? "CONECTANDO..." : isHost ? "ESPERANDO RIVAL" : "ESPERANDO AL HOST";
        }

        if (sessionCodeText != null) sessionCodeText.text = online ? code : "";

        SetActive(sessionCodeText != null ? sessionCodeText.gameObject : null, online && !string.IsNullOrEmpty(code));
        SetActive(copyCodeButton, online && isHost && !string.IsNullOrEmpty(code));

        if (hostIPAddressText != null)
        {
            hostIPAddressText.text = isHost && !online ? $"TU IP: {networkTestMenu?.LocalIPAddress}" : "";
            hostIPAddressText.gameObject.SetActive(isHost && !online);
        }

        if (playerCountText != null && !online)
        {
            playerCountText.text = $"JUGADORES: {(connected ? Mathf.Max(players, 1) : 0)}/{maxPlayers}";
        }

        if (online)
        {
            if (onlineWaitingPlayerText != null) onlineWaitingPlayerText.SetActive(true);

            if (onlineSessionCodeText != null)
            {
                if (networkTestMenu != null && networkTestMenu.IsBusy && string.IsNullOrEmpty(code))
                    onlineSessionCodeText.text = "CREATING GAME...";
                else if (!string.IsNullOrEmpty(code))
                    onlineSessionCodeText.text = $"CODE: {code}";
                else
                    onlineSessionCodeText.text = "CONNECTING...";
            }

            if (onlinePlayerCountText != null)
            {
                onlinePlayerCountText.text = $"PLAYERS: {(connected ? Mathf.Max(players, 1) : 0)}/{maxPlayers}";
            }
        }
    }

    void SetLocalWaitingUI(bool active)
    {
        if (waitingPanel != null) return;

        bool localWaiting = active && page == Page.Local;
        bool onlineWaiting = active && page == Page.Online;

        SetActive(waitingPlayerText, localWaiting);
        SetActive(hostIPAddressText != null ? hostIPAddressText.gameObject : null, localWaiting);
        SetActive(playerCountText != null ? playerCountText.gameObject : null, localWaiting);

        SetActive(onlineWaitingPlayerText, onlineWaiting);
        SetActive(onlineSessionCodeText != null ? onlineSessionCodeText.gameObject : null, onlineWaiting);
        SetActive(onlinePlayerCountText != null ? onlinePlayerCountText.gameObject : null, onlineWaiting);
    }

    void RefreshStatus(bool selecting, bool waiting)
    {
        if (statusText == null) return;

        string status = networkTestMenu != null ? networkTestMenu.Status : null;
        string message = selecting ? "" : waiting || status != dismissedStatus ? status : "";

        statusText.text = message ?? "";
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    void RefreshCharacterUI(CharacterLobbyManager lobby)
    {
        if (characterNameText != null) characterNameText.text = lobby.SelectedCharacterName;

        if (characterPortraitImage != null)
        {
            Sprite portrait = lobby.SelectedCharacterPortrait;
            characterPortraitImage.sprite = portrait;
            characterPortraitImage.enabled = portrait != null;
        }

        if (characterRingImage != null && characterColors != null && characterColors.Length > 0)
        {
            characterRingImage.color = characterColors[Mathf.Clamp(lobby.LocalSelectedCharacter, 0, characterColors.Length - 1)];
        }

        // Visor 3D propio y del rival (lo que tiene marcado ahora mismo).
        if (localPreview != null) localPreview.Show(lobby.SelectedCharacter);
        var rival = lobby.RivalCharacter;
        if (rivalPreview != null) rivalPreview.Show(rival);
        if (rivalPortraitImage != null)
        {
            Sprite rivalSprite = rival != null ? rival.portrait : null;
            rivalPortraitImage.sprite = rivalSprite;
            rivalPortraitImage.enabled = rivalSprite != null;
        }
        if (rivalNameText != null)
        {
            rivalNameText.text = rival == null ? "RIVAL..."
                : lobby.IsRivalReady ? $"RIVAL: {rival.displayName} · READY!" : $"RIVAL: {rival.displayName}";
        }

        if (readyStatusText != null) readyStatusText.text = $"PLAYERS READY: {lobby.ReadyCount}/{lobby.MaxPlayers}";
        if (localReadyText != null) localReadyText.text = lobby.IsLocalReady ? "TÚ · ¡LISTO!" : "TÚ · ELIGIENDO";
        if (rivalReadyText != null) rivalReadyText.text = lobby.IsRivalReady ? "RIVAL · ¡LISTO!" : "RIVAL · ELIGIENDO";

        bool canChange = !lobby.IsLocalReady;

        if (previousCharacterButton != null) previousCharacterButton.interactable = canChange;
        if (nextCharacterButton != null) nextCharacterButton.interactable = canChange;

        if (readyButton != null)
        {
            readyButton.interactable = canChange;
            TMP_Text buttonText = readyButton.GetComponentInChildren<TMP_Text>();
            if (buttonText != null) buttonText.text = lobby.IsLocalReady ? "READY!" : "READY";
        }
    }

    // ============================================================
    // COMIENZO DE PARTIDA
    // ============================================================

    void BeginGameplay()
    {
        gameplayStarted = true;
        shownPanel = null;

        StartCoroutine(BeginGameplayTransition());
    }

    IEnumerator BeginGameplayTransition()
    {
        if (PlayerController.Local != null)
        {
            PlayerController.Local.InputHandler.SetInputEnabled(false);
        }

        if (SceneTransitionManager.Instance != null)
        {
            yield return SceneTransitionManager.Instance.FadeToBlack();
        }

        if (PersistentMusic.Instance != null)
        {
            PersistentMusic.Instance.StopMusic();
        }

        SetMenuVisible(false);

        if (gameplayUI != null)
            gameplayUI.SetActive(true);

        while (PlayerController.Local == null)
        {
            yield return null;
        }

        PlayerController.Local.InputHandler.SetInputEnabled(false);
        yield return null;

        if (SceneTransitionManager.Instance != null)
        {
            yield return SceneTransitionManager.Instance.FadeFromBlack();
        }

        MatchTimer matchTimer = FindFirstObjectByType<MatchTimer>();

        if (countdownUI != null)
        {
            yield return countdownUI.PlayCountdown(
                () =>
                {
                    if (AudioManager.Instance != null)
                    {
                        AudioManager.Instance.PlayMusic(0);
                    }

                    StartActualMatch(matchTimer);
                }
            );
        }
        else
        {
            Debug.LogWarning("ConnectionMenu: no hay MatchCountdownUI asignado.");

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayMusic(0);
            }

            StartActualMatch(matchTimer);
        }
    }

    void StartActualMatch(MatchTimer matchTimer)
    {
        if (matchTimer != null)
        {
            matchTimer.ServerStartMatch();
        }
        else
        {
            Debug.LogWarning("ConnectionMenu: no se encontró MatchTimer.");
        }

        if (PlayerController.Local != null)
        {
            PlayerController.Local.InputHandler.SetInputEnabled(true);
        }

        Debug.Log("START!! La partida ha comenzado.");
    }

    void SetMenuVisible(bool visible)
    {
        if (connectionMenu != null)
        {
            if (connectionMenu == gameObject)
            {
                foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
                {
                    canvas.enabled = visible;
                }
            }
            else
            {
                connectionMenu.SetActive(visible);
            }
        }

        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    public void CloseConnectionMenu()
    {
        gameplayStarted = true;
        SetMenuVisible(false);
    }

    public async void BackToMainMenu()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        NetworkManager oldNetworkManager = NetworkManager.Singleton;

        if (networkTestMenu != null)
        {
            networkTestMenu.LeaveGame();

            while (networkTestMenu != null && networkTestMenu.IsBusy)
            {
                await System.Threading.Tasks.Task.Yield();
            }
        }
        else if (oldNetworkManager != null && oldNetworkManager.IsListening)
        {
            oldNetworkManager.Shutdown();
        }

        if (oldNetworkManager != null)
        {
            while (oldNetworkManager.IsListening || oldNetworkManager.ShutdownInProgress)
            {
                await System.Threading.Tasks.Task.Yield();
            }
        }

        if (oldNetworkManager != null)
        {
            Destroy(oldNetworkManager.gameObject);
            await System.Threading.Tasks.Task.Yield();
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
        {
            target.SetActive(active);
        }
    }
}