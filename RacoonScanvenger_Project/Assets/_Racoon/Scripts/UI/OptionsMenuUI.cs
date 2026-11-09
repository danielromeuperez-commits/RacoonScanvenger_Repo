using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
public class OptionsMenuUI : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject optionsPanel;

    [Header("Pantalla")]
    [SerializeField] TMP_Text displayModeButtonText;

    [Header("Navegación")]
    [SerializeField] Selectable firstOptionsSelected;
    [SerializeField] Selectable firstMainMenuSelected;

    void Start()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (mainPanel != null)
            mainPanel.SetActive(true);

        RefreshDisplayModeText();
    }

    // ============================================================
    // MENÚ DE OPCIONES
    // ============================================================

    public void OpenOptions()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(true);

        RefreshDisplayModeText();

        // Seleccionamos el primer elemento del menú Options.
        if (EventSystem.current != null &&
            firstOptionsSelected != null)
        {
            EventSystem.current.SetSelectedGameObject(null);

            EventSystem.current.SetSelectedGameObject(
                firstOptionsSelected.gameObject
            );
        }
    }

    public void CloseOptions()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (mainPanel != null)
            mainPanel.SetActive(true);

        // Al volver, seleccionamos PLAY.
        if (EventSystem.current != null &&
            firstMainMenuSelected != null)
        {
            EventSystem.current.SetSelectedGameObject(null);

            EventSystem.current.SetSelectedGameObject(
                firstMainMenuSelected.gameObject
            );
        }
    }

    // ============================================================
    // PANTALLA COMPLETA / VENTANA
    // ============================================================

    public void ToggleDisplayMode()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
        }

        if (Screen.fullScreen)
        {
            Screen.fullScreenMode =
                FullScreenMode.Windowed;
        }
        else
        {
            Screen.fullScreenMode =
                FullScreenMode.FullScreenWindow;
        }

        RefreshDisplayModeText();
    }

    void RefreshDisplayModeText()
    {
        if (displayModeButtonText == null)
            return;

        if (Screen.fullScreen)
        {
            // Si actualmente estamos en pantalla completa,
            // el botón indica lo que hará al pulsarlo.
            displayModeButtonText.text =
                "WINDOWED SCREEN";
        }
        else
        {
            displayModeButtonText.text =
                "FULL SCREEN";
        }
    }
}