using Racoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Cambia el sprite de un Image según el último dispositivo usado (teclado, Xbox, PlayStation...).
    /// Ponlo en cada prompt de botón ("Pulsa X para interactuar") con un sprite por familia.
    /// Funciona también en menús: no necesita que haya jugador spawneado.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class InputDeviceIcon : MonoBehaviour
    {
        [SerializeField] Sprite keyboardMouse;
        [SerializeField] Sprite xbox;
        [SerializeField] Sprite playStation;
        [SerializeField] Sprite nintendo;
        [Tooltip("Mandos sin familia conocida. Si está vacío se usa el de Xbox.")]
        [SerializeField] Sprite genericGamepad;

        Image image;

        void Awake() => image = GetComponent<Image>();

        void OnEnable()
        {
            InputDeviceTracker.KindChanged += Apply;
            Apply(InputDeviceTracker.CurrentKind);
        }

        void OnDisable() => InputDeviceTracker.KindChanged -= Apply;

        void Apply(InputDeviceKind kind)
        {
            Sprite sprite = kind switch
            {
                InputDeviceKind.KeyboardMouse => keyboardMouse,
                InputDeviceKind.Xbox => xbox,
                InputDeviceKind.PlayStation => playStation,
                InputDeviceKind.Nintendo => nintendo,
                _ => genericGamepad != null ? genericGamepad : xbox,
            };
            // Si falta el sprite de una familia de mando, cae en el de Xbox antes que en el de teclado.
            if (sprite == null && kind != InputDeviceKind.KeyboardMouse) sprite = xbox;
            if (sprite != null) image.sprite = sprite;
        }
    }
}
