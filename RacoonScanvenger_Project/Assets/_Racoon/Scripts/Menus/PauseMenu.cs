using Racoon.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] GameObject pausePanel;

    [Header("Menú principal")]
    [SerializeField] GameObject pausedText;
    [SerializeField] GameObject resumeButton;
    [SerializeField] GameObject optionsButton;
    [SerializeField] GameObject mainMenuButton;

    [Header("Opciones")]
    [SerializeField] GameObject optionsPanel;

    bool isPaused;

    void Start()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        SetMainMenuObjects(true);

        isPaused = false;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (!isPaused)
            {
                Pause();
                return;
            }

            // Si estamos en Options, ESC vuelve al menú de pausa.
            if (optionsPanel != null && optionsPanel.activeSelf)
            {
                CloseOptions();
                return;
            }

            // Si estamos en el menú principal de pausa,
            // ESC vuelve al juego.
            Resume();
        }
    }

    public void Pause()
    {
        if (isPaused)
            return;

        isPaused = true;

        if (pausePanel != null)
            pausePanel.SetActive(true);

        SetMainMenuObjects(true);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        // Bloqueamos únicamente los controles del jugador local.
        if (PlayerController.Local != null)
        {
            PlayerController.Local.InputHandler.SetInputEnabled(false);
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
    public void ReturnToMainMenu()
    {
        // Cerramos correctamente la sesión de red antes
        // de abandonar la escena de juego.
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene("SCN_MainMenu");
    }

    public void Resume()
    {
        if (!isPaused)
            return;

        isPaused = false;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        SetMainMenuObjects(true);

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        // Devolvemos el control al jugador local.
        if (PlayerController.Local != null)
        {
            PlayerController.Local.InputHandler.SetInputEnabled(true);
        }

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void OpenOptions()
    {
        SetMainMenuObjects(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(true);
    }

    public void CloseOptions()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        SetMainMenuObjects(true);
    }

    void SetMainMenuObjects(bool active)
    {
        if (pausedText != null)
            pausedText.SetActive(active);

        if (resumeButton != null)
            resumeButton.SetActive(active);

        if (optionsButton != null)
            optionsButton.SetActive(active);

        if (mainMenuButton != null)
            mainMenuButton.SetActive(active);
    }
}