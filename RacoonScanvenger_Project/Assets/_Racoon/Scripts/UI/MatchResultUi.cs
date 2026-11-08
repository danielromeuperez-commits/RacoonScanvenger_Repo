using System.Collections;
using Racoon.Network;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchResultUI : MonoBehaviour
{
    public static MatchResultUI Instance { get; private set; }

    [Header("Paneles")]
    [SerializeField] GameObject victoryPanel;
    [SerializeField] GameObject defeatPanel;
    [SerializeField] GameObject drawPanel;

    [Header("Escenas")]
    [SerializeField] string mainMenuSceneName = "SCN_MainMenu";

    bool changingScene;

    void Awake()
    {
        Instance = this;

        HideAllPanels();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ============================================================
    // MOSTRAR RESULTADOS
    // ============================================================

    public void ShowVictory()
    {
        HideAllPanels();

        if (victoryPanel != null)
            victoryPanel.SetActive(true);

        ShowCursor();
    }

    public void ShowDefeat()
    {
        HideAllPanels();

        if (defeatPanel != null)
            defeatPanel.SetActive(true);

        ShowCursor();
    }

    public void ShowDraw()
    {
        HideAllPanels();

        if (drawPanel != null)
            drawPanel.SetActive(true);

        ShowCursor();
    }

    void HideAllPanels()
    {
        if (victoryPanel != null)
            victoryPanel.SetActive(false);

        if (defeatPanel != null)
            defeatPanel.SetActive(false);

        if (drawPanel != null)
            drawPanel.SetActive(false);
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