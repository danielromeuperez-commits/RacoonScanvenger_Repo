using TMPro;
using UnityEngine;

public class OptionsMenuUI : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject optionsPanel;

    [Header("Pantalla")]
    [SerializeField] TMP_Text displayModeButtonText;

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
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
        }

        if (mainPanel != null)
            mainPanel.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(true);

        RefreshDisplayModeText();
    }

    public void CloseOptions()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(23);
        }

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (mainPanel != null)
            mainPanel.SetActive(true);
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