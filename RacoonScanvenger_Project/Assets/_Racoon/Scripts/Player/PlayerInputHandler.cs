using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Racoon.Player
{
    /// <summary>
    /// Lee el Input System y lo expone al PlayerController.
    /// Solo se activa en el jugador que es tuyo (IsOwner): el PlayerController llama a SetInputEnabled.
    ///
    /// Emparejado de dispositivo: el jugador se queda con el primer mando (o teclado) que pulse un botón
    /// mientras su ventana tiene el foco, y a partir de ahí SOLO escucha ese dispositivo. Así, probando
    /// dos instancias en el mismo PC, cada mando controla un único jugador.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        public enum DeviceAssignment
        {
            ClaimOnFirstPress, // Un dispositivo por jugador (recomendado)
            AllDevices,        // Cualquier dispositivo mueve al jugador
        }

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string actionMapName = "Player";
        [SerializeField] DeviceAssignment deviceAssignment = DeviceAssignment.ClaimOnFirstPress;
        [Tooltip("Solo se reclama un dispositivo si esta ventana tiene el foco (evita que dos instancias del mismo PC cojan el mismo mando).")]
        [SerializeField] bool requireFocusToClaim = true;

        public Vector2 Move { get; private set; }
        public bool RunHeld { get; private set; }
        /// <summary>Dispositivo que controla a este jugador (null si aún no se ha elegido).</summary>
        public InputDevice PairedDevice { get; private set; }
        public bool IsWaitingForDevice => inputEnabled && deviceAssignment == DeviceAssignment.ClaimOnFirstPress && PairedDevice == null;

        public event Action InteractPressed;   // Botón sur
        public event Action SwitchItemPressed; // Botón norte
        public event Action UsePressed;        // Botón oeste
        public event Action<InputDevice> DevicePaired;

        InputActionAsset runtimeActions;
        InputActionMap map;
        InputAction moveAction, runAction, interactAction, switchItemAction, useAction;
        IDisposable anyButtonListener;
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
                InputSystem.onDeviceChange += OnDeviceChange;

                if (deviceAssignment == DeviceAssignment.AllDevices)
                {
                    runtimeActions.devices = null;
                    map.Enable();
                }
                else if (PairedDevice != null && PairedDevice.added) PairWith(PairedDevice);
                else ListenForDevice();
            }
            else if (map != null)
            {
                interactAction.performed -= OnInteract;
                switchItemAction.performed -= OnSwitchItem;
                useAction.performed -= OnUse;
                InputSystem.onDeviceChange -= OnDeviceChange;
                StopListening();
                map.Disable();
                Move = Vector2.zero;
                RunHeld = false;
            }
        }

        void CreateActions()
        {
            // Copia propia del asset: así restringir sus dispositivos no afecta a nadie más.
            runtimeActions = Instantiate(inputActions);
            map = runtimeActions.FindActionMap(actionMapName, true);
            moveAction = map.FindAction("Move", true);
            runAction = map.FindAction("Run", true);
            interactAction = map.FindAction("Interact", true);
            switchItemAction = map.FindAction("SwitchItem", true);
            useAction = map.FindAction("Use", true);
        }

        // ---------------- Emparejado de dispositivo ----------------

        void ListenForDevice()
        {
            map.Disable();
            Move = Vector2.zero;
            RunHeld = false;
            StopListening();
            anyButtonListener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
        }

        void StopListening()
        {
            anyButtonListener?.Dispose();
            anyButtonListener = null;
        }

        void OnAnyButtonPress(InputControl control)
        {
            if (requireFocusToClaim && !Application.isFocused) return;
            // El ratón no reclama: hacer clic para enfocar la ventana no debe quitarte el mando.
            if (control.device is Pointer) return;
            PairWith(control.device);
        }

        void PairWith(InputDevice device)
        {
            StopListening();
            PairedDevice = device;

            // El teclado va junto con el ratón (clic izquierdo = usar).
            runtimeActions.devices = device is Keyboard && Mouse.current != null
                ? new InputDevice[] { device, Mouse.current }
                : new[] { device };

            map.Enable();
            DevicePaired?.Invoke(device);
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device != PairedDevice) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
            {
                // Mando desconectado: vuelve a esperar a que se pulse un botón.
                PairedDevice = null;
                ListenForDevice();
            }
        }

        // ---------------- Lectura ----------------

        void Update()
        {
            if (!inputEnabled || !map.enabled) return;
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
