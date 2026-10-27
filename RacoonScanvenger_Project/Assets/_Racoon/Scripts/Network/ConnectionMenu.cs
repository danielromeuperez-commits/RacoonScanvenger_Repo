using Racoon.Network;
using Racoon.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ConnectionMenu : MonoBehaviour
{
    [Header("Menú completo")]
    [SerializeField] GameObject connectionMenu;

    [Header("Red")]
    [SerializeField] NetworkTestMenu networkTestMenu;
    [SerializeField] CharacterLobbyManager characterLobbyManager;

    [Header("Local")]
    [SerializeField] TMP_InputField ipInputField;
    [SerializeField] GameObject waitingPlayerText;
    [SerializeField] TMP_Text hostIPAddressText;
    [SerializeField] TMP_Text playerCountText;

    [Header("Selección de personaje")]
    [SerializeField] GameObject characterSelectionPanel;
    [SerializeField] TMP_Text characterNameText;
    [SerializeField] Image characterPortraitImage;
    [SerializeField] TMP_Text readyStatusText;

    [Header("Botones de personaje")]
    [SerializeField] Button previousCharacterButton;
    [SerializeField] Button nextCharacterButton;
    [SerializeField] Button readyButton;

    [Header("Paneles")]
    [SerializeField] GameObject modeSelectionPanel;
    [SerializeField] GameObject localPanel;
    [SerializeField] GameObject onlinePanel;

    [Header("Escenas")]
    [SerializeField] string mainMenuSceneName = "SCN_MainMenu";

    bool waitingAsHost;
    bool waitingAsClient;
    bool characterSelectionOpen;
    bool gameplayStarted;

    // ============================================================
    // INICIO
    // ============================================================

    void Start()
    {
        OpenMainSelection();

        SetLocalWaitingUI(false);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void Update()
    {
        if (gameplayStarted)
            return;

        NetworkManager manager = NetworkManager.Singleton;

        // ========================================================
        // HOST: espera hasta que haya 2 jugadores
        // ========================================================

        if (waitingAsHost)
        {
            UpdateHostWaitingUI();

            if (!characterSelectionOpen &&
                manager != null &&
                manager.IsServer &&
                manager.ConnectedClientsIds.Count >=
                networkTestMenu.MaxPlayerCount)
            {
                OpenCharacterSelection();
            }
        }

        // ========================================================
        // CLIENTE: cuando confirma la conexión, abre selección
        // ========================================================

        if (waitingAsClient)
        {
            if (!characterSelectionOpen &&
                manager != null &&
                manager.IsConnectedClient &&
                !manager.IsServer)
            {
                OpenCharacterSelection();
            }
        }

        // ========================================================
        // SELECCIÓN DE PERSONAJE
        // ========================================================

        if (characterSelectionOpen &&
            characterLobbyManager != null)
        {
            RefreshCharacterUI();

            if (characterLobbyManager.GameStarted)
            {
                BeginGameplay();
            }
        }
    }

    // ============================================================
    // NAVEGACIÓN
    // ============================================================

    public void OpenMainSelection()
    {
        if (connectionMenu != null)
            connectionMenu.SetActive(true);

        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(true);

        if (localPanel != null)
            localPanel.SetActive(false);

        if (onlinePanel != null)
            onlinePanel.SetActive(false);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);

        waitingAsHost = false;
        waitingAsClient = false;
        characterSelectionOpen = false;

        SetLocalWaitingUI(false);
    }

    public void OpenLocal()
    {
        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        if (localPanel != null)
            localPanel.SetActive(true);

        if (onlinePanel != null)
            onlinePanel.SetActive(false);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);

        waitingAsHost = false;
        waitingAsClient = false;
        characterSelectionOpen = false;

        SetLocalWaitingUI(false);
    }

    public void OpenOnline()
    {
        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        if (localPanel != null)
            localPanel.SetActive(false);

        if (onlinePanel != null)
            onlinePanel.SetActive(true);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);
    }

    // ============================================================
    // LOCAL
    // ============================================================

    public void HostLocal()
    {
        if (networkTestMenu == null)
        {
            Debug.LogError("No hay NetworkTestMenu asignado.");
            return;
        }

        networkTestMenu.StartLocalHost();

        waitingAsHost = true;
        waitingAsClient = false;

        SetLocalWaitingUI(true);
        UpdateHostWaitingUI();

        Debug.Log("Host local creado. Esperando al segundo jugador...");
    }

    public void JoinLocal()
    {
        if (networkTestMenu == null)
        {
            Debug.LogError("No hay NetworkTestMenu asignado.");
            return;
        }

        if (ipInputField == null)
        {
            Debug.LogError("No hay INPUT_IP asignado.");
            return;
        }

        string address = ipInputField.text.Trim();

        if (string.IsNullOrEmpty(address))
        {
            Debug.LogWarning("Introduce la IP del Host.");
            return;
        }

        networkTestMenu.SetJoinAddress(address);
        networkTestMenu.StartLocalClient();

        waitingAsClient = true;
        waitingAsHost = false;

        Debug.Log($"Conectando a {address}...");
    }

    void UpdateHostWaitingUI()
    {
        if (networkTestMenu == null)
            return;

        if (hostIPAddressText != null)
        {
            hostIPAddressText.text =
                $"HOST IP: {networkTestMenu.LocalIPAddress}";
        }

        if (playerCountText != null)
        {
            int connectedPlayers = 0;

            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening)
            {
                connectedPlayers =
                    NetworkManager.Singleton
                        .ConnectedClientsIds.Count;
            }

            playerCountText.text =
                $"PLAYERS: {connectedPlayers}/" +
                $"{networkTestMenu.MaxPlayerCount}";
        }
    }

    void SetLocalWaitingUI(bool active)
    {
        if (waitingPlayerText != null)
            waitingPlayerText.SetActive(active);

        if (hostIPAddressText != null)
            hostIPAddressText.gameObject.SetActive(active);

        if (playerCountText != null)
            playerCountText.gameObject.SetActive(active);
    }

    // ============================================================
    // SELECCIÓN DE PERSONAJE
    // ============================================================

    void OpenCharacterSelection()
    {
        if (characterSelectionOpen)
            return;

        characterSelectionOpen = true;

        waitingAsHost = false;
        waitingAsClient = false;

        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        if (localPanel != null)
            localPanel.SetActive(false);

        if (onlinePanel != null)
            onlinePanel.SetActive(false);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(true);

        RefreshCharacterUI();

        Debug.Log("Abriendo selección de personaje.");
    }

    public void PreviousCharacter()
    {
        if (characterLobbyManager == null)
            return;

        characterLobbyManager.PreviousCharacter();

        RefreshCharacterUI();
    }

    public void NextCharacter()
    {
        if (characterLobbyManager == null)
            return;

        characterLobbyManager.NextCharacter();

        RefreshCharacterUI();
    }

    public void ReadyCharacter()
    {
        if (characterLobbyManager == null)
            return;

        characterLobbyManager.ReadyLocalPlayer();

        RefreshCharacterUI();
    }

    void RefreshCharacterUI()
    {
        if (characterLobbyManager == null)
            return;

        // ---------------- Nombre ----------------

        if (characterNameText != null)
        {
            characterNameText.text =
                characterLobbyManager.SelectedCharacterName;
        }

        // ---------------- Retrato ----------------

        if (characterPortraitImage != null)
        {
            Sprite portrait =
                characterLobbyManager.SelectedCharacterPortrait;

            characterPortraitImage.sprite = portrait;
            characterPortraitImage.enabled =
                portrait != null;
        }

        // ---------------- READY ----------------

        if (readyStatusText != null)
        {
            readyStatusText.text =
                $"PLAYERS READY: " +
                $"{characterLobbyManager.ReadyCount}/" +
                $"{characterLobbyManager.MaxPlayers}";
        }

        bool canChangeCharacter =
            !characterLobbyManager.IsLocalReady;

        if (previousCharacterButton != null)
            previousCharacterButton.interactable =
                canChangeCharacter;

        if (nextCharacterButton != null)
            nextCharacterButton.interactable =
                canChangeCharacter;

        if (readyButton != null)
        {
            readyButton.interactable =
                !characterLobbyManager.IsLocalReady;

            TMP_Text buttonText =
                readyButton.GetComponentInChildren<TMP_Text>();

            if (buttonText != null)
            {
                buttonText.text =
                    characterLobbyManager.IsLocalReady
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
        if (gameplayStarted)
            return;

        gameplayStarted = true;
        characterSelectionOpen = false;

        if (connectionMenu != null)
            connectionMenu.SetActive(false);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        Debug.Log(
            "Todos los jugadores están READY. Comienza la partida."
        );
    }

    // ============================================================
    // OTROS
    // ============================================================

    public void CloseConnectionMenu()
    {
        if (connectionMenu != null)
            connectionMenu.SetActive(false);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void BackToMainMenu()
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(mainMenuSceneName);
    }
}