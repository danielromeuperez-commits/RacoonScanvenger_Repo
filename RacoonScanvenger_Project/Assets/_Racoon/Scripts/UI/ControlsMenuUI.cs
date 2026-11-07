using UnityEngine;

public class ControlsMenuUI : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] GameObject optionsPanel;
    [SerializeField] GameObject controlsPanel;

    [Header("Tipos de controles")]
    [SerializeField] GameObject keyboardControlsPanel;
    [SerializeField] GameObject controllerControlsPanel;

    void Start()
    {
        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        if (keyboardControlsPanel != null)
            keyboardControlsPanel.SetActive(true);

        if (controllerControlsPanel != null)
            controllerControlsPanel.SetActive(false);
    }

    // ============================================================
    // ABRIR CONTROLES DE TECLADO
    // ============================================================

    public void OpenKeyboardControls()
    {
        Debug.Log("ABRIENDO CONTROLES DE TECLADO");

        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(true);

        if (keyboardControlsPanel != null)
            keyboardControlsPanel.SetActive(true);

        if (controllerControlsPanel != null)
            controllerControlsPanel.SetActive(false);
    }

    // ============================================================
    // VER CONTROLES DE MANDO
    // ============================================================

    public void OpenControllerControls()
    {
        if (keyboardControlsPanel != null)
            keyboardControlsPanel.SetActive(false);

        if (controllerControlsPanel != null)
            controllerControlsPanel.SetActive(true);
    }

    // ============================================================
    // VOLVER A CONTROLES DE TECLADO
    // ============================================================

    public void BackToKeyboardControls()
    {
        if (controllerControlsPanel != null)
            controllerControlsPanel.SetActive(false);

        if (keyboardControlsPanel != null)
            keyboardControlsPanel.SetActive(true);
    }



    // ============================================================
    // VOLVER A OPCIONES
    // ============================================================

    public void BackToOptions()
    {
        if (controlsPanel != null)
            controlsPanel.SetActive(false);

        if (optionsPanel != null)
            optionsPanel.SetActive(true);
    }
}