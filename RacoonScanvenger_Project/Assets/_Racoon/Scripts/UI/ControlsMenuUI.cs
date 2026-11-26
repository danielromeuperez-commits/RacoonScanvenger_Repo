using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ControlsMenuUI : MonoBehaviour
{
    [Header("Paneles")]
    [SerializeField] GameObject optionsPanel;
    [SerializeField] GameObject controlsPanel;

    [Header("Navegación")]
    [SerializeField] Selectable firstKeyboardSelected;
    [SerializeField] Selectable firstControllerSelected;
    [SerializeField] Selectable controlsButtonInOptions;

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
        if (optionsPanel != null)
            optionsPanel.SetActive(false);

        if (controlsPanel != null)
            controlsPanel.SetActive(true);

        if (keyboardControlsPanel != null)
            keyboardControlsPanel.SetActive(true);

        if (controllerControlsPanel != null)
            controllerControlsPanel.SetActive(false);

        SelectButton(firstKeyboardSelected);
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

        SelectButton(firstControllerSelected);
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

        SelectButton(firstKeyboardSelected);
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

        SelectButton(controlsButtonInOptions);
    }

    void SelectButton(Selectable selectable)
    {
        if (EventSystem.current == null ||
            selectable == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);

        EventSystem.current.SetSelectedGameObject(
            selectable.gameObject
        );
    }
}