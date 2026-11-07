using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Escenas")]
    [SerializeField] string gameSceneName = "SCN_Level_Polish";

    [Header("Navegación")]
    [Tooltip("Primer botón que se seleccionará al empezar a usar mando o teclado.")]
    [SerializeField] Button firstSelectedButton;

    [Tooltip("CanvasGroup del menú. Se utiliza para bloquear el ratón mientras usamos mando.")]
    [SerializeField] CanvasGroup menuCanvasGroup;

    [Tooltip("Cuánto hay que mover el stick para activar la navegación con mando.")]
    [SerializeField, Range(0.1f, 1f)] float gamepadDeadzone = 0.35f;

    // False = ratón
    // True = mando / teclado
    bool navigationMode = false;

    void Start()
    {
        Time.timeScale = 1f;

        // Al entrar empezamos utilizando ratón.
        SetMouseMode();
    }

    void Update()
    {
        // ========================================================
        // RATÓN
        // ========================================================

        if (MouseUsedThisFrame())
        {
            SetMouseMode();
            return;
        }

        // ========================================================
        // MANDO
        // ========================================================

        if (GamepadNavigationUsedThisFrame())
        {
            SetNavigationMode();
            return;
        }

        // ========================================================
        // TECLADO
        // ========================================================

        if (KeyboardNavigationUsedThisFrame())
        {
            SetNavigationMode();
        }
    }

    void LateUpdate()
    {
        // Mientras usamos ratón no queremos
        // que quede ningún botón seleccionado.
        if (!navigationMode &&
            EventSystem.current != null &&
            EventSystem.current.currentSelectedGameObject != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ============================================================
    // DETECTAR RATÓN
    // ============================================================

    bool MouseUsedThisFrame()
    {
        if (Mouse.current == null)
            return false;

        bool moved =
            Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f;

        bool clicked =
            Mouse.current.leftButton.wasPressedThisFrame ||
            Mouse.current.rightButton.wasPressedThisFrame ||
            Mouse.current.middleButton.wasPressedThisFrame;

        bool scrolled =
            Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f;

        return moved || clicked || scrolled;
    }

    // ============================================================
    // DETECTAR MANDO
    // ============================================================

    bool GamepadNavigationUsedThisFrame()
    {
        if (Gamepad.current == null)
            return false;

        Vector2 leftStick =
            Gamepad.current.leftStick.ReadValue();

        Vector2 dpad =
            Gamepad.current.dpad.ReadValue();

        bool stickMoved =
            leftStick.sqrMagnitude >
            gamepadDeadzone * gamepadDeadzone;

        bool dpadMoved =
            dpad.sqrMagnitude > 0.01f;

        bool buttonPressed =
            Gamepad.current.buttonSouth.wasPressedThisFrame ||
            Gamepad.current.buttonEast.wasPressedThisFrame;

        return
            stickMoved ||
            dpadMoved ||
            buttonPressed;
    }

    // ============================================================
    // DETECTAR TECLADO
    // ============================================================

    bool KeyboardNavigationUsedThisFrame()
    {
        if (Keyboard.current == null)
            return false;

        return
            Keyboard.current.upArrowKey.wasPressedThisFrame ||
            Keyboard.current.downArrowKey.wasPressedThisFrame ||
            Keyboard.current.leftArrowKey.wasPressedThisFrame ||
            Keyboard.current.rightArrowKey.wasPressedThisFrame;
    }

    // ============================================================
    // MODO RATÓN
    // ============================================================

    void SetMouseMode()
    {
        if (!navigationMode)
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (menuCanvasGroup != null)
                menuCanvasGroup.blocksRaycasts = true;

            return;
        }

        navigationMode = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (menuCanvasGroup != null)
            menuCanvasGroup.blocksRaycasts = true;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ============================================================
    // MODO MANDO / TECLADO
    // ============================================================

    void SetNavigationMode()
    {
        navigationMode = true;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.None;

        if (menuCanvasGroup != null)
            menuCanvasGroup.blocksRaycasts = false;

        if (EventSystem.current == null)
            return;

        if (EventSystem.current.currentSelectedGameObject == null &&
            firstSelectedButton != null)
        {
            EventSystem.current.SetSelectedGameObject(
                firstSelectedButton.gameObject
            );
        }
    }

    // ============================================================
    // PLAY
    // ============================================================

    public void Play()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
        }

        if (SceneTransitionManager.Instance != null)
        {
            SceneTransitionManager.Instance
                .LoadSceneWithDiagonalWipe(gameSceneName);
        }
        else
        {
            Debug.LogWarning(
                "MainMenu: no se encontró SceneTransitionManager. " +
                "Cargando la escena sin transición."
            );

            SceneManager.LoadScene(gameSceneName);
        }
    }

    // ============================================================
    // SALIR
    // ============================================================

    public void QuitGame()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
        }

        Debug.Log("Cerrando juego...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}