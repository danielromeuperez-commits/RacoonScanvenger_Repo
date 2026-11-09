using System.Collections;
using System.Collections.Generic;
using Racoon.Gameplay;
using Racoon.Network;
using Racoon.UI;
using Racoon.UI.HUD;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;


/// <summary>
/// Pantalla de fin de partida (PF_HUD_MatchResult). Un solo panel para victoria, derrota y empate.
/// - P1 a la izquierda y P2 a la derecha, como en el HUD de la partida, con el marco teñido del color de cada jugador
///   (HUDPlayerColors). Cada personaje en 3D con el mismo visor que la selección (CharacterPreviewStage).
/// - Quién gana lo cuentan los Animators: AC_MatchResult_Slot (Result 1 ganador: grande, corona y gestos; 2 perdedor:
///   pequeño, apagado y quieto; 0 empate) y AC_MatchResult_Panel (entrada del panel, título y botones; Hide = salida).
/// - Fuegos artificiales detrás si ha ganado alguien (FireworksBackdrop).
/// El script solo pone textos, colores y personajes y dispara los Animators.
/// </summary>
/// 

public class MatchResultUI : MonoBehaviour
{
    static readonly int ShowId = Animator.StringToHash("Show");
    static readonly int HideId = Animator.StringToHash("Hide");
    static readonly int ResultId = Animator.StringToHash("Result");

    [System.Serializable]
    public class PlayerSlot
    {
        public Animator animator;
        public RawImage preview;
        public TMP_Text nameText;
        public GameObject crown;
        public GameObject winnerTag;
        [Tooltip("Piezas [TINTE] del marco: se tiñen con el color del jugador.")]
        public Image[] tints;
    }

    public static MatchResultUI Instance { get; private set; }

    [Header("Panel")]
    [FormerlySerializedAs("victoryPanel")]
    [SerializeField] GameObject resultPanel;
    [SerializeField] Animator panelAnimator;
    [SerializeField] TMP_Text titleText;
    [SerializeField] string victoryTitle = "YOU WIN!!";
    [SerializeField] string defeatTitle = "YOU LOSE!!";
    [SerializeField] string drawTitle = "DRAW!!";
    [SerializeField] Color victoryColor = new(1f, 0.82f, 0.32f, 1f);
    [SerializeField] Color defeatColor = new(0.93f, 0.45f, 0.42f, 1f);
    [SerializeField] Color drawColor = Color.white;
    [Tooltip("Segundos desde el final de la partida hasta que aparece la pantalla (se ve el último momento de juego).")]
    [SerializeField, Min(0f)] float showDelay = 5f;
    [Tooltip("Segundos de la animación de salida (MR_Panel_Outro) antes de ocultar el panel.")]
    [SerializeField, Min(0f)] float outroDuration = 0.3f;

    [Header("Jugadores (P1 izquierda, P2 derecha)")]
    [SerializeField] PlayerSlot player1 = new();
    [SerializeField] PlayerSlot player2 = new();
    [Tooltip("HUDPlayerColors del PREFAB del HUD (PF_HUD_P5PastelReactivo_ConNick_ConP), no el de la escena: en partida " +
             "HUDController puede cambiarlo por los colores del personaje. Se leen los colores guardados en el prefab.")]

    [Header("Navegación")]
    [SerializeField] Button mainSelectionButton;
    [SerializeField] Button mainMenuButton; 

    [SerializeField] HUDPlayerColors hudColorsPrefab;
    [Tooltip("Colores si no hay HUDPlayerColors asignado.")]
    [SerializeField] Color fallbackP1 = new Color32(0x39, 0x5C, 0xF5, 0xFF);
    [SerializeField] Color fallbackP2 = new Color32(0xF5, 0x3F, 0x39, 0xFF);
    [Tooltip("Perdedor: tinte del modelo; el marco se mezcla hacia gris con «Loser Desaturate».")]
    [SerializeField] Color loserTint = new(0.6f, 0.6f, 0.66f, 1f);
    [SerializeField, Range(0f, 1f)] float loserDesaturate = 0.55f;

    [Header("Fuegos")]
    [SerializeField] FireworksBackdrop fireworks;

    [Header("Escenas")]
    [SerializeField] string mainMenuSceneName = "SCN_MainMenu";

    // Capas y peanas propias: 30/31 las usan la selección y los retratos del HUD, 29 los iconos, 25 los fuegos.
    const int LayerP1 = 26;
    const int LayerP2 = 27;
    static readonly Vector3 StageP1 = new(-60f, -500f, 0f);
    static readonly Vector3 StageP2 = new(60f, -500f, 0f);

    CharacterPreviewStage stageP1;
    CharacterPreviewStage stageP2;
    bool showing;
    bool onlineAtShow;
    bool changingScene;
    bool controllerNavigationMode;
    Coroutine hiding;
    Coroutine pending;

    void Awake()
    {
        Instance = this;
        stageP1 = SetupStage(player1.preview, LayerP1, StageP1);
        stageP2 = SetupStage(player2.preview, LayerP2, StageP2);
        HideImmediate();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        // Si se corta la partida (el rival sale, se cae la red...), ConnectionMenu vuelve a aparecer en este mismo
        // frame: el resultado se va con él en vez de quedarse encima.
        if (showing && onlineAtShow && !changingScene && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening))
            Hide();

        if (showing)
        {
            HandleInputMode();
        }
    }

    static CharacterPreviewStage SetupStage(RawImage image, int layer, Vector3 position)
    {
        if (image == null) return null;
        if (!image.TryGetComponent(out CharacterPreviewStage stage))
            stage = image.gameObject.AddComponent<CharacterPreviewStage>();
        stage.Layer = layer;
        stage.StagePosition = position;
        return stage;
    }

    // ============================================================
    // MOSTRAR RESULTADOS
    // ============================================================

    /// <summary>Lo llama MatchTimer en cada PC con el ganador de la partida.</summary>
    public void ShowResult(ulong winnerClientId, bool draw)
    {
        if (pending != null) StopCoroutine(pending);
        pending = StartCoroutine(ShowAfterDelay(winnerClientId, draw));
    }

    IEnumerator ShowAfterDelay(ulong winnerClientId, bool draw)
    {
        bool online = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        yield return new WaitForSecondsRealtime(showDelay);
        pending = null;
        // Si la partida se ha cortado mientras tanto, ya se ve el ConnectionMenu: no se tapa con el resultado.
        bool stillOnline = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (!changingScene && (!online || stillOnline)) ShowResultNow(winnerClientId, draw);
    }

    void ShowResultNow(ulong winnerClientId, bool draw)
    {
        ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        List<(ulong clientId, PlayerCharacterDefinition character)> players = FindPlayers();
        (ulong, PlayerCharacterDefinition) p1 = players.Count > 0 ? players[0] : (ulong.MaxValue, (PlayerCharacterDefinition)null);
        (ulong, PlayerCharacterDefinition) p2 = players.Count > 1 ? players[1] : (ulong.MaxValue - 1, (PlayerCharacterDefinition)null);

        int result1 = draw ? 0 : p1.Item1 == winnerClientId ? 1 : 2;
        int result2 = draw ? 0 : p2.Item1 == winnerClientId ? 1 : 2;
        // Sin ganador entre los dos (no debería pasar): se trata como empate.
        if (!draw && result1 != 1 && result2 != 1) { draw = true; result1 = result2 = 0; }

        string title = draw ? drawTitle : localId == winnerClientId ? victoryTitle : defeatTitle;
        Color color = draw ? drawColor : localId == winnerClientId ? victoryColor : defeatColor;

        Show(title, color,
            (p1.Item2, p1.Item1 == localId, result1),
            (p2.Item2, p2.Item1 == localId, result2), draw);
    }

    void HandleInputMode()
    {
        // ============================================================
        // RATÓN
        // ============================================================

        if (Mouse.current != null)
        {
            bool mouseUsed =
                Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame;

            if (mouseUsed)
            {
                controllerNavigationMode = false;

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }

                return;
            }
        }

        // ============================================================
        // MANDO
        // ============================================================

        if (Gamepad.current == null)
            return;

        Vector2 stick =
            Gamepad.current.leftStick.ReadValue();

        Vector2 dpad =
            Gamepad.current.dpad.ReadValue();

        bool controllerUsed =
            stick.sqrMagnitude > 0.12f ||
            dpad.sqrMagnitude > 0.01f ||
            Gamepad.current.buttonSouth.wasPressedThisFrame;

        if (!controllerUsed)
            return;

        controllerNavigationMode = true;

        // Si todavía no hay ningún botón seleccionado,
        // seleccionamos MAIN SELECTION.
        if (EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject == null &&
            mainSelectionButton != null)
        {
            EventSystem.current.SetSelectedGameObject(
                mainSelectionButton.gameObject
            );
        }
    }

    // Compatibilidad con llamadas antiguas.
    public void ShowVictory() => ShowResult(NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0, false);
    public void ShowDefeat() => ShowResult(ulong.MaxValue, false);
    public void ShowDraw() => ShowResult(0, true);

    void Show(string title, Color titleColor,
        (PlayerCharacterDefinition character, bool local, int result) a,
        (PlayerCharacterDefinition character, bool local, int result) b, bool draw)
    {
        if (resultPanel == null) return;
        if (hiding != null) { StopCoroutine(hiding); hiding = null; }

        // Al activarse, los Animators arrancan en Hidden (no guardan estado al desactivarse).
        resultPanel.SetActive(false);
        resultPanel.SetActive(true);
        showing = true;
        onlineAtShow = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        if (titleText != null)
        {
            titleText.text = title;
            titleText.color = titleColor;
        }

        Color c1 = hudColorsPrefab != null ? hudColorsPrefab.Get(1) : fallbackP1;
        Color c2 = hudColorsPrefab != null ? hudColorsPrefab.Get(2) : fallbackP2;

        Fill(player1, stageP1, a.character, a.local, a.result, c1, 25f);
        Fill(player2, stageP2, b.character, b.local, b.result, c2, -25f);

        if (panelAnimator != null) panelAnimator.SetTrigger(ShowId);

        if (fireworks != null)
        {
            if (draw) fireworks.Stop();
            else fireworks.Play(WinnerSlotX(a.result == 1 ? player1 : player2));
        }

        ShowCursor();
        StartCoroutine(SelectResultButtonNextFrame());
    }

    IEnumerator SelectResultButtonNextFrame()
    {
        yield return null;

        if (EventSystem.current == null)
            yield break;

        EventSystem.current.SetSelectedGameObject(null);

        if (mainSelectionButton != null)
        {
            EventSystem.current.SetSelectedGameObject(
                mainSelectionButton.gameObject
            );
        }
        else if (mainMenuButton != null)
        {
            EventSystem.current.SetSelectedGameObject(
                mainMenuButton.gameObject
            );
        }
    }

    // X del hueco del ganador en el panel (los fuegos van detrás de él).
    static float WinnerSlotX(PlayerSlot slot) =>
        slot.animator != null ? ((RectTransform)slot.animator.transform).anchoredPosition.x : 0f;

    void Fill(PlayerSlot slot, CharacterPreviewStage stage, PlayerCharacterDefinition character, bool local, int result,
        Color playerColor, float yaw)
    {
        bool winner = result == 1, loser = result == 2;

        if (stage != null)
        {
            // Ganador: gestos seguidos. Perdedor: quieto, girado hacia el centro. Empate: gestos de vez en cuando.
            stage.SpinSpeed = 0f;
            stage.GestureInterval = winner ? 1.6f : loser ? 9999f : 3.5f;
            stage.StartYaw = yaw;
            stage.Show(character);
        }

        if (slot.nameText != null)
        {
            string name = character != null && !string.IsNullOrEmpty(character.displayName) ? character.displayName : "???";
            slot.nameText.text = local ? $"{name} <size=60%>(YOU)</size>" : name;
            slot.nameText.alpha = loser ? 0.7f : 1f;
        }

        if (slot.crown != null) slot.crown.SetActive(winner);
        if (slot.winnerTag != null) slot.winnerTag.SetActive(winner);

        Color frame = loser ? Color.Lerp(playerColor, new Color(0.5f, 0.5f, 0.55f, 1f), loserDesaturate) : playerColor;
        if (slot.tints != null)
            foreach (Image tint in slot.tints)
                if (tint != null) tint.color = frame;
        if (slot.preview != null) slot.preview.color = loser ? loserTint : Color.white;

        if (slot.animator != null)
        {
            slot.animator.SetInteger(ResultId, result);
            slot.animator.SetTrigger(ShowId);
        }
    }

    // Personajes en orden P1, P2: por OwnerClientId y luego NetworkObjectId (igual que MatchStandings y el HUD).
    static List<(ulong, PlayerCharacterDefinition)> FindPlayers()
    {
        var found = new List<(ulong owner, ulong id, PlayerCharacterDefinition character)>();
        NetworkManager manager = NetworkManager.Singleton;
        PlayerSpawnManager spawner = manager != null ? manager.GetComponent<PlayerSpawnManager>() : null;
        PlayerCharacterRoster roster = spawner != null ? spawner.Roster : null;
        if (manager != null && manager.SpawnManager != null && roster != null)
            foreach (NetworkObject player in manager.SpawnManager.SpawnedObjectsList)
            {
                if (player == null || !player.IsPlayerObject) continue;
                PlayerCharacterDefinition character = null;
                foreach (PlayerCharacterDefinition candidate in roster.Characters)
                    if (Matches(candidate, player)) { character = candidate; break; }
                found.Add((player.OwnerClientId, player.NetworkObjectId, character));
            }
        found.Sort((x, y) => x.owner != y.owner ? x.owner.CompareTo(y.owner) : x.id.CompareTo(y.id));
        return found.ConvertAll(p => (p.owner, p.character));
    }

    static bool Matches(PlayerCharacterDefinition character, NetworkObject player) =>
        character && character.prefab && player &&
        (character.prefab.PrefabIdHash == player.PrefabIdHash || player.name.StartsWith(character.prefab.name));

    // ============================================================
    // OCULTAR
    // ============================================================

    /// <summary>Salida animada (MR_Panel_Outro) y luego se desactiva el panel.</summary>
    public void Hide()
    {
        if (!showing) return;
        showing = false;
        if (panelAnimator != null && resultPanel != null && resultPanel.activeInHierarchy)
        {
            panelAnimator.SetTrigger(HideId);
            hiding = StartCoroutine(HideAfterOutro());
        }
        else HideImmediate();
    }

    IEnumerator HideAfterOutro()
    {
        yield return new WaitForSecondsRealtime(outroDuration);
        hiding = null;
        HideImmediate();
    }

    void HideImmediate()
    {
        if (pending != null) { StopCoroutine(pending); pending = null; }
        showing = false;
        if (fireworks != null) fireworks.Stop();
        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    void ShowCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // ============================================================
    // MAIN MENU
    // ============================================================

    public void MainMenu()
    {
        if (changingScene)
            return;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
            AudioManager.Instance.PlayMusic(1);
        }

        StartCoroutine(
            LeaveNetworkAndLoadScene(
                mainMenuSceneName
            )
        );
    }

    // ============================================================
    // VOLVER A SELECT GAME MODE
    // ============================================================

    public void ReturnToMainSelection()
    {
        if (changingScene)
            return;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
            AudioManager.Instance.PlayMusic(1);
        }

        // Recargamos esta misma escena.
        // Al cargarse de nuevo, ConnectionMenu empieza
        // en SELECT GAME MODE.
        string currentScene =
            SceneManager.GetActiveScene().name;

        StartCoroutine(
            LeaveNetworkAndLoadScene(
                currentScene
            )
        );
    }

    // ============================================================
    // CERRAR RED Y CAMBIAR ESCENA
    // ============================================================

    IEnumerator LeaveNetworkAndLoadScene(
        string sceneName)
    {
        changingScene = true;

        // La pantalla se va ya (en paralelo con el cierre de red), no cuando cargue la escena.
        showing = true;
        Hide();

        Time.timeScale = 1f;

        NetworkManager oldNetworkManager =
            NetworkManager.Singleton;

        NetworkTestMenu networkTestMenu =
            FindFirstObjectByType<NetworkTestMenu>();

        // Cerramos Local u Online correctamente.
        if (networkTestMenu != null)
        {
            networkTestMenu.LeaveGame();

            while (networkTestMenu != null &&
                   networkTestMenu.IsBusy)
            {
                yield return null;
            }
        }
        else if (oldNetworkManager != null &&
                 oldNetworkManager.IsListening)
        {
            oldNetworkManager.Shutdown();
        }

        // Esperamos a que Netcode termine.
        if (oldNetworkManager != null)
        {
            while (
                oldNetworkManager.IsListening ||
                oldNetworkManager.ShutdownInProgress)
            {
                yield return null;
            }

            // Evitamos conservar el NetworkManager
            // antiguo en DontDestroyOnLoad.
            Destroy(
                oldNetworkManager.gameObject
            );

            yield return null;
        }

        SceneManager.LoadScene(
            sceneName
        );
    }
}
