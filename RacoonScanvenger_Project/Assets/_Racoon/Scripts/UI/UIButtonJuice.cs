using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Rebote de botón cartoon: crece un poco al pasar el ratón o seleccionarlo con mando, se aplasta al pulsar
    /// y se balancea suave mientras está seleccionado. Usa tiempo sin escalar (funciona con el juego en pausa).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UIButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] float hoverScale = 1.07f;
        [SerializeField] float pressScale = 0.92f;
        [SerializeField] float wobbleDegrees = 2f;
        [SerializeField] float speed = 14f;

        Selectable selectable;
        bool hovered, pressed, selected;
        float wobbleTime;

        void Awake() => selectable = GetComponent<Selectable>();

        void OnDisable()
        {
            hovered = pressed = selected = false;
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;
        }

        void Update()
        {
            bool interactable = selectable == null || selectable.IsInteractable();
            bool focused = interactable && (hovered || selected);
            float target = !interactable ? 1f : pressed ? pressScale : focused ? hoverScale : 1f;

            float t = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            float scale = Mathf.Lerp(transform.localScale.x, target, t);
            transform.localScale = new Vector3(scale, scale, 1f);

            wobbleTime = focused ? wobbleTime + Time.unscaledDeltaTime : 0f;
            float angle = focused ? Mathf.Sin(wobbleTime * 6f) * wobbleDegrees : 0f;
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.Euler(0f, 0f, angle), t);
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) => hovered = pressed = false;
        public void OnPointerDown(PointerEventData eventData) => pressed = true;
        public void OnPointerUp(PointerEventData eventData) => pressed = false;
        public void OnSelect(BaseEventData eventData) => selected = true;
        public void OnDeselect(BaseEventData eventData) => selected = false;
    }
}
