using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Racoon.Player
{
    /// <summary>
    /// Lee el Input System y lo expone al PlayerController.
    /// Solo se activa en el jugador que es tuyo (IsOwner): el PlayerController llama a SetInputEnabled.
    ///
    /// Dispositivo activo (según <see cref="DeviceAssignment"/>):
    ///  - AutoSwitch: el jugador usa el último dispositivo que se ha tocado en esta ventana. Tocar el
    ///    teclado pasa a teclado+ratón; tocar el mando vuelve al mando. Lanza <see cref="ActiveKindChanged"/>
    ///    para cambiar iconos.
    ///  - LockToFirstDevice: se queda con el primer mando (o teclado) que pulse un botón y SOLO escucha
    ///    ese. Útil para probar dos instancias en el mismo PC, un mando cada una.
    ///  - AllDevices: cualquier dispositivo mueve al jugador a la vez.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        // Valores explícitos: se serializan como int en los prefabs.
        public enum DeviceAssignment
        {
            AutoSwitch = 0,        // Cambia al último dispositivo usado (recomendado)
            AllDevices = 1,        // Cualquier dispositivo mueve al jugador
            LockToFirstDevice = 2, // Un dispositivo fijo por jugador (pruebas con 2 instancias en un PC)
        }

        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string actionMapName = "Player";
        [SerializeField] DeviceAssignment deviceAssignment = DeviceAssignment.AutoSwitch;

        public Vector2 Move { get; private set; }
        public bool RunHeld { get; private set; }
        /// <summary>Dispositivo que controla a este jugador (null si aún no se ha elegido).</summary>
        public InputDevice PairedDevice { get; private set; }
        public InputDeviceKind ActiveKind => InputDeviceTracker.GetKind(PairedDevice);
        public bool IsWaitingForDevice => inputEnabled && deviceAssignment != DeviceAssignment.AllDevices && PairedDevice == null;

        public event Action InteractPressed;
        public event Action SwitchItemPressed;

        public event Action UsePressed;
        public event Action UseStarted;
        public event Action UseReleased;

        public bool UseHeld { get; private set; }      // Botón oeste
        /// <summary>Se lanza cada vez que cambia el dispositivo que controla al jugador.</summary>
        public event Action<InputDevice> DevicePaired;
        /// <summary>Solo cuando cambia la familia (teclado ↔ PlayStation ↔ Xbox...). Para iconos.</summary>
        public event Action<InputDeviceKind> ActiveKindChanged;

        InputActionAsset runtimeActions;
        InputActionMap map;
        InputAction moveAction, runAction, interactAction, switchItemAction, useAction;
        bool listening;
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

                useAction.started += OnUseStarted;
                useAction.performed += OnUse;
                useAction.canceled += OnUseReleased;
                InputSystem.onDeviceChange += OnDeviceChange;

                if (deviceAssignment == DeviceAssignment.AllDevices)
                {
                    runtimeActions.devices = null;
                    map.Enable();
                    return;
                }

                // En AutoSwitch arrancamos con lo último que se usó en esta ventana (p. ej. el mando
                // con el que se navegó el menú), sin obligar a pulsar nada.
                InputDevice initial = PairedDevice;
                if ((initial == null || !initial.added) && deviceAssignment == DeviceAssignment.AutoSwitch)
                    initial = InputDeviceTracker.LastUsedDevice;

                StartListening();
                if (initial != null && initial.added) PairWith(initial);
                else WaitForDevice();
            }
            else if (map != null)
            {
                interactAction.performed -= OnInteract;
                switchItemAction.performed -= OnSwitchItem;

                useAction.started -= OnUseStarted;
                useAction.performed -= OnUse;
                useAction.canceled -= OnUseReleased;
                InputSystem.onDeviceChange -= OnDeviceChange;
                StopListening();
                map.Disable();
                ResetValues();
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

        // ---------------- Dispositivo activo ----------------

        void StartListening()
        {
            if (listening) return;
            listening = true;
            InputDeviceTracker.DeviceUsed += OnDeviceUsed;
        }

        void StopListening()
        {
            if (!listening) return;
            listening = false;
            InputDeviceTracker.DeviceUsed -= OnDeviceUsed;
        }

        void WaitForDevice()
        {
            PairedDevice = null;
            map.Disable();
            ResetValues();
        }

        // InputDeviceTracker ya filtra por foco y convierte los clics de ratón en "teclado".
        void OnDeviceUsed(InputDevice device, InputControl control)
        {
            if (device == PairedDevice) return;

            if (deviceAssignment == DeviceAssignment.AutoSwitch)
            {
                PairWith(device);
            }
            else if (deviceAssignment == DeviceAssignment.LockToFirstDevice && PairedDevice == null)
            {
                // El ratón no reclama: hacer clic para enfocar la ventana no debe quitarte el mando.
                if (control.device is Pointer) return;
                PairWith(device);
            }
        }

        void PairWith(InputDevice device)
        {
            InputDeviceKind previousKind = ActiveKind;
            bool hadDevice = PairedDevice != null;
            PairedDevice = device;

            // El teclado va junto con el ratón (clic izquierdo = usar).
            runtimeActions.devices = device is Keyboard && Mouse.current != null
                ? new InputDevice[] { device, Mouse.current }
                : new[] { device };

            // Evita que el jugador siga andando con el valor que tenía el dispositivo anterior.
            ResetValues();
            if (!map.enabled) map.Enable();

            DevicePaired?.Invoke(device);
            if (!hadDevice || ActiveKind != previousKind) ActiveKindChanged?.Invoke(ActiveKind);
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device != PairedDevice) return;
            if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected) return;

            // Mando desconectado: en AutoSwitch saltamos al teclado para no dejar al jugador bloqueado;
            // en LockToFirstDevice volvemos a esperar a que se pulse un botón.
            if (deviceAssignment == DeviceAssignment.AutoSwitch && Keyboard.current != null && Keyboard.current != device)
                PairWith(Keyboard.current);
            else
                WaitForDevice();
        }

        // ---------------- Lectura ----------------

        void Update()
        {
            if (!inputEnabled || !map.enabled) return;
            Move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            RunHeld = runAction.IsPressed();
        }

        void ResetValues()
        {
            Move = Vector2.zero;
            UseHeld = false;
            RunHeld = false;
        }

        void OnInteract(InputAction.CallbackContext _) =>
    InteractPressed?.Invoke();

        void OnSwitchItem(InputAction.CallbackContext _) =>
            SwitchItemPressed?.Invoke();

        void OnUseStarted(InputAction.CallbackContext _)
        {
            UseHeld = true;
            UseStarted?.Invoke();
        }

        void OnUse(InputAction.CallbackContext _)
        {
            UsePressed?.Invoke();
        }

        void OnUseReleased(InputAction.CallbackContext _)
        {
            UseHeld = false;
            UseReleased?.Invoke();
        }

        void OnDestroy()
        {
            SetInputEnabled(false);
            if (runtimeActions != null) Destroy(runtimeActions);
        }
    }
}
