using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racoon.Player
{
    /// <summary>
    /// Lee el Input System y lo expone al PlayerController.
    /// Solo se activa en el jugador que es tuyo (IsOwner): el PlayerController llama a SetInputEnabled.
    /// No uses el componente PlayerInput de Unity en el prefab: se instanciaría también en la copia
    /// del rival y ambos intentarían leer tu mando.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string actionMapName = "Player";

        public Vector2 Move { get; private set; }
        public bool RunHeld { get; private set; }

        public event Action InteractPressed;   // Botón sur
        public event Action SwitchItemPressed; // Botón norte
        public event Action UsePressed;        // Botón oeste

        InputActionAsset runtimeActions;
        InputActionMap map;
        InputAction moveAction, runAction, interactAction, switchItemAction, useAction;
        bool inputEnabled;

        public void SetInputEnabled(bool enable)
        {
            if (enable == inputEnabled) return;
            inputEnabled = enable;

            if (enable)
            {
                if (runtimeActions == null) CreateActions();
                interactAction.performed += OnInteract;
                switchItemAction.performed += OnSwitchItem;
                useAction.performed += OnUse;
                map.Enable();
            }
            else if (map != null)
            {
                interactAction.performed -= OnInteract;
                switchItemAction.performed -= OnSwitchItem;
                useAction.performed -= OnUse;
                map.Disable();
                Move = Vector2.zero;
                RunHeld = false;
            }
        }

        void CreateActions()
        {
            // Copia propia del asset para no compartir estado con otros objetos que lo usen.
            runtimeActions = Instantiate(inputActions);
            map = runtimeActions.FindActionMap(actionMapName, true);
            moveAction = map.FindAction("Move", true);
            runAction = map.FindAction("Run", true);
            interactAction = map.FindAction("Interact", true);
            switchItemAction = map.FindAction("SwitchItem", true);
            useAction = map.FindAction("Use", true);
        }

        void Update()
        {
            if (!inputEnabled) return;
            Move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            RunHeld = runAction.IsPressed();
        }

        void OnInteract(InputAction.CallbackContext _) => InteractPressed?.Invoke();
        void OnSwitchItem(InputAction.CallbackContext _) => SwitchItemPressed?.Invoke();
        void OnUse(InputAction.CallbackContext _) => UsePressed?.Invoke();

        void OnDestroy()
        {
            SetInputEnabled(false);
            if (runtimeActions != null) Destroy(runtimeActions);
        }
    }
}
