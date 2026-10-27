using UnityEngine;
using UnityEngine.SceneManagement;
using Racoon.Network;
using TMPro;

public class ConnectionMenu : MonoBehaviour
{
    [Header("Menú completo")]
    [SerializeField] GameObject connectionMenu;

    [Header("Red")]
    [SerializeField] NetworkTestMenu networkTestMenu;

    [Header("Local")]
    [SerializeField] TMP_InputField ipInputField;

    [Header("Paneles")]
    [SerializeField] GameObject modeSelectionPanel;
    [SerializeField] GameObject localPanel;
    [SerializeField] GameObject onlinePanel;

    [Header("Escenas")]
    [SerializeField] string mainMenuSceneName = "SCN_MainMenu";

    void Start()
    {
        // Al entrar a la escena mostramos el menú principal
        // de selección Local / Online.
        OpenMainSelection();

        // Mientras estamos en los menús necesitamos el ratón.
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    /// <summary>
    /// Pantalla principal: Local / Online.
    /// </summary>
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
    }

    /// <summary>
    /// Abre el menú de conexión local.
    /// </summary>
    /// 
    public void HostLocal()
    {
        if (networkTestMenu == null)
        {
            Debug.LogError("No hay NetworkTestMenu asignado.");
            return;
        }

        networkTestMenu.StartLocalHost();

        CloseConnectionMenu();
    }

    public void JoinLocal()
    {
        if (networkTestMenu == null)
        {
            Debug.LogError("No hay NetworkTestMenu asignado.");
            return;
        }

        if (ipInputField != null)
        {
            networkTestMenu.SetJoinAddress(ipInputField.text);
        }

        networkTestMenu.StartLocalClient();
    }

    public void OpenLocal()
    {
        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        if (localPanel != null)
            localPanel.SetActive(true);

        if (onlinePanel != null)
            onlinePanel.SetActive(false);
    }

    /// <summary>
    /// Abre el menú de conexión online.
    /// </summary>
    public void OpenOnline()
    {
        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        if (localPanel != null)
            localPanel.SetActive(false);

        if (onlinePanel != null)
            onlinePanel.SetActive(true);
    }

    /// <summary>
    /// Cierra completamente el menú.
    /// Lo usaremos cuando el jugador ya haya entrado a la partida.
    /// </summary>
    public void CloseConnectionMenu()
    {
        if (connectionMenu != null)
            connectionMenu.SetActive(false);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    /// <summary>
    /// Vuelve a la escena del menú principal.
    /// </summary>
    public void BackToMainMenu()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }
}