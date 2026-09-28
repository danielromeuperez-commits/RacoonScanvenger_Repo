using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities; // Extensión .Call() de onAnyButtonPress

namespace Racoon.Player
{
    /// <summary>Familia de dispositivo, para elegir iconos/prompts (teclado, Xbox, PlayStation...).</summary>
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Xbox,
        PlayStation,
        Nintendo,
        GenericGamepad,
    }

    /// <summary>
    /// Vigila qué dispositivo se ha usado por última vez en ESTA ventana (botón, tecla, clic o stick
    /// pasado el umbral de pulsación). No depende de que haya jugador spawneado: sirve también para los
    /// iconos del menú. PlayerInputHandler se engancha a <see cref="DeviceUsed"/> para cambiar de mando a
    /// teclado y viceversa.
    /// </summary>
    public static class InputDeviceTracker
    {
        /// <summary>Si es true, se ignoran las pulsaciones mientras la ventana no tiene el foco.</summary>
        public static bool RequireFocus = true;

        /// <summary>Último dispositivo usado. Teclado y ratón se reportan como el teclado.</summary>
        public static InputDevice LastUsedDevice { get; private set; }
        public static InputDeviceKind CurrentKind { get; private set; } = InputDeviceKind.KeyboardMouse;

        /// <summary>
        /// Cada pulsación que cuenta como "uso". El dispositivo ya viene normalizado (ratón → teclado);
        /// el control es el que se pulsó de verdad (para saber si fue un clic).
        /// </summary>
        public static event Action<InputDevice, InputControl> DeviceUsed;
        /// <summary>Solo cuando cambia la familia (p. ej. teclado → PlayStation). Ideal para cambiar iconos.</summary>
        public static event Action<InputDeviceKind> KindChanged;

        static IDisposable listener;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // Necesario con "Enter Play Mode Options" sin recarga de dominio.
            listener?.Dispose();
            listener = null;
            LastUsedDevice = null;
            CurrentKind = InputDeviceKind.KeyboardMouse;
            DeviceUsed = null;
            KindChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (listener != null) return;
            listener = InputSystem.onAnyButtonPress.Call(OnAnyButtonPress);
            InputSystem.onDeviceChange += OnDeviceChange;

            // Estado inicial razonable: si hay un mando conectado, se asume mando.
            InputDevice initial = (InputDevice)Gamepad.current ?? Keyboard.current;
            if (initial != null) SetLastUsed(initial);
        }

        static void OnAnyButtonPress(InputControl control)
        {
            if (RequireFocus && !Application.isFocused) return;

            InputDevice device = control.device;
            // Teclado y ratón son un único "esquema": el ratón cuenta como el teclado.
            if (device is Pointer)
            {
                if (Keyboard.current == null) return;
                device = Keyboard.current;
            }
            else if (!(device is Keyboard) && !(device is Gamepad))
            {
                return; // Joysticks raros, sensores, etc.
            }

            SetLastUsed(device);
            DeviceUsed?.Invoke(device, control);
        }

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device != LastUsedDevice) return;
            if (change != InputDeviceChange.Removed && change != InputDeviceChange.Disconnected) return;

            InputDevice fallback = (InputDevice)Gamepad.current ?? Keyboard.current;
            if (fallback == device) fallback = Keyboard.current;
            if (fallback != null) SetLastUsed(fallback);
        }

        static void SetLastUsed(InputDevice device)
        {
            LastUsedDevice = device;
            InputDeviceKind kind = GetKind(device);
            if (kind == CurrentKind) return;
            CurrentKind = kind;
            KindChanged?.Invoke(kind);
        }

        /// <summary>Clasifica un dispositivo por layout y, si no, por fabricante.</summary>
        public static InputDeviceKind GetKind(InputDevice device)
        {
            if (device == null || device is Keyboard || device is Pointer) return InputDeviceKind.KeyboardMouse;

            // Por layout (no usa tipos específicos de plataforma, compila en todas).
            string layout = device.layout;
            if (IsLayout(layout, "DualShockGamepad")) return InputDeviceKind.PlayStation; // DualShock 4 y DualSense
            if (IsLayout(layout, "XInputController")) return InputDeviceKind.Xbox;
            if (IsLayout(layout, "SwitchProControllerHID")) return InputDeviceKind.Nintendo;

            // Por fabricante (mandos HID genéricos que no tienen layout específico).
            string manufacturer = device.description.manufacturer ?? "";
            string product = device.description.product ?? "";
            if (Contains(manufacturer, "Sony") || Contains(product, "DualSense") || Contains(product, "Wireless Controller"))
                return InputDeviceKind.PlayStation;
            if (Contains(manufacturer, "Microsoft") || Contains(product, "Xbox"))
                return InputDeviceKind.Xbox;
            if (Contains(manufacturer, "Nintendo"))
                return InputDeviceKind.Nintendo;

            return InputDeviceKind.GenericGamepad;
        }

        static bool IsLayout(string layout, string baseLayout)
        {
            try
            {
                return InputSystem.IsFirstLayoutBasedOnSecond(layout, baseLayout);
            }
            catch (ArgumentException)
            {
                return false; // El layout base no existe en esta plataforma.
            }
        }

        static bool Contains(string text, string value) => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
