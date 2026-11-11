using Racoon.Player;
using Racoon.UI.P5;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Aviso de «mantén para vender», pegado al jugador local (junto a la cabeza, como la bolsa) (PF_HUD_AvisoVender: arte en Photoshop, animaciones en su
    /// Animator). Lo instancia cada SellPoint en cada instancia del juego y solo mira al jugador LOCAL:
    /// aparece cuando se acerca con algo en la bolsa, el anillo se llena mientras mantiene Interact y, al vender,
    /// salta el «¡VENDIDO!» con las monedas.
    ///
    /// Este script solo coloca el aviso en pantalla, pone el glifo y el texto, rellena el anillo y mueve los
    /// parámetros del Animator (Visible, Holding, Sold). Todo lo demás (entrada, rebotes, monedas) está en los clips.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SellPromptUI : MonoBehaviour
    {
        static readonly int VisibleId = Animator.StringToHash("Visible");
        static readonly int HoldingId = Animator.StringToHash("Holding");
        static readonly int PressId = Animator.StringToHash("Press");
        static readonly int SoldId = Animator.StringToHash("Sold");

        [SerializeField] InputGlyphSet glyphs;
        [Tooltip("Acción que hay que mantener (nombre en PlayerInput.inputactions).")]
        [SerializeField] string action = "Interact";

        [Header("Piezas")]
        [Tooltip("Todo el aviso: se coloca sobre el punto de venta.")]
        public RectTransform content;
        public Animator animator;
        [Tooltip("Su alto manda; el ancho se ajusta al glifo.")]
        public Image glyph;
        [Tooltip("Filled Radial 360: fillAmount = lo que lleva mantenido.")]
        public Image ringFill;
        [Tooltip("Opcional (el aviso actual no lleva texto: solo el botón y el dial).")]
        public P5Text label;

        [Header("Textos")]
        [SerializeField] string holdText = "¡MANTÉN!";
        [SerializeField] string sellingText = "VENDIENDO";
        [SerializeField] string soldText = "¡VENDIDO!";

        // Solo sale si el punto de venta está al alcance de la interacción del jugador (PlayerController.InteractReach).

        [Tooltip("Toques en vez de mantener: cada toque cuenta como mantener durante estos segundos " +
                 "(igual que el margen del servidor entre interacciones).")]
        [SerializeField] float tapWindow = 0.7f;

        [Header("Posición (pegado al jugador, como la bolsa)")]
        [Tooltip("Altura de la cabeza sobre los pies (m). La misma que usa la bolsa (PlayerStatusUI).")]
        [SerializeField] float headHeight = 0.55f;
        [Tooltip("Desplazamiento en pantalla desde la cabeza (px a 2560×1440): a su izquierda (la bolsa va a la derecha).")]
        [SerializeField] Vector2 screenOffset = new(-200f, 0f);
        [SerializeField] float screenMargin = 90f;

        Canvas canvas;
        SellPoint sellPoint;
        PlayerController local;
        PlayerLoot localLoot;
        PlayerInputHandler localInput;
        float nextSearch, held, glyphHeight, lastPress = -10f;
        int lastPending;
        bool layered, shown;
        string shownText;

        /// <summary>Lo llama el SellPoint al instanciarlo.</summary>
        public void Bind(SellPoint point) => sellPoint = point;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (glyph != null) glyphHeight = glyph.rectTransform.sizeDelta.y;
        }

        void OnDisable() => SetLocal(null);

        void SetLocal(PlayerController player)
        {
            if (localInput != null)
            {
                localInput.ActiveKindChanged -= OnKindChanged;
                localInput.InteractPressed -= OnInteractPressed;
            }
            local = player;
            localLoot = player != null ? player.GetComponent<PlayerLoot>() : null;
            localInput = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            if (localInput != null)
            {
                localInput.ActiveKindChanged += OnKindChanged;
                localInput.InteractPressed += OnInteractPressed;
                OnKindChanged(localInput.ActiveKind);
            }
            lastPending = localLoot != null ? localLoot.PendingPoints : 0;
        }

        // Cada toque: el botón se hunde (Press) y cuenta como «sigue manteniendo» un rato (ver tapWindow).
        void OnInteractPressed()
        {
            lastPress = Time.time;
            // Solo con el aviso a la vista: si no, el trigger se quedaría pendiente y saltaría luego.
            if (shown) animator.SetTrigger(PressId);
        }

        void OnKindChanged(InputDeviceKind kind)
        {
            if (glyph == null || glyphs == null) return;
            Sprite sprite = glyphs.Get(action, kind);
            glyph.sprite = sprite;
            glyph.enabled = sprite != null;
            if (sprite == null) return;
            float aspect = sprite.rect.width / sprite.rect.height;
            glyph.rectTransform.sizeDelta = new Vector2(glyphHeight * aspect, glyphHeight);
        }

        void LateUpdate()
        {
            if (sellPoint == null)
            {
                Destroy(gameObject);
                return;
            }

            if ((local == null || !local.IsSpawned) && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 1f;
                SetLocal(FindLocalPlayer());
            }

            bool visible = false, holding = false;
            if (local != null && localLoot != null)
            {
                // Solo al alcance de la interacción del jugador (borde de la esfera de interacción).
                float distance = FlatDistanceTo(local.transform.position, sellPoint);
                float reach = local.InteractReach;
                int pending = localLoot.PendingPoints;

                // Acaba de vender aquí: «¡VENDIDO!» (el Animator lo enseña aunque ya no quede nada en la bolsa).
                if (pending == 0 && lastPending > 0 && distance <= reach + 0.5f)
                {
                    animator.SetTrigger(SoldId);
                    SetLabel(soldText);
                    held = 1f;
                }
                lastPending = pending;

                visible = pending > 0 && distance <= reach;
                holding = visible && localInput != null && (localInput.InteractHeld || Time.time - lastPress <= tapWindow);
            }

            // Mismo tiempo que tarda de verdad la venta: el servidor solo la comprueba con cada interacción mantenida
            // (cada InteractRepeatCadence), así que vende en la primera que pasa de HoldTimeFor. El dial dura eso
            // mismo, redondeado al siguiente múltiplo de la cadencia (+ medio frame por repetición): se llena justo
            // cuando vende. Al soltar se vacía rápido.
            float serverHold = sellPoint.HoldTimeFor(localLoot != null ? localLoot.PendingPoints : 0, local);
            float cadence = local != null ? local.InteractRepeatCadence + Time.smoothDeltaTime * 0.5f : 0f;
            float holdTime = cadence > 0.01f ? Mathf.Ceil((serverHold - 0.001f) / cadence) * cadence : serverHold;
            holdTime = Mathf.Max(0.05f, holdTime);
            held = holding ? Mathf.Min(1f, held + Time.deltaTime / holdTime)
                : Mathf.MoveTowards(held, 0f, Time.deltaTime * 4f);
            if (ringFill != null) ringFill.fillAmount = held;

            if (visible) SetLabel(holding ? sellingText : holdText);
            shown = visible;
            animator.SetBool(VisibleId, visible);
            animator.SetBool(HoldingId, holding);

            if (local != null) Place();
        }

        void SetLabel(string text)
        {
            if (label == null || text == shownText) return;
            shownText = text;
            label.SetText(text);
        }

        // Distancia en el suelo desde el jugador hasta el borde del collider más cercano del objeto.
        static float FlatDistanceTo(Vector3 from, Component target)
        {
            float best = float.MaxValue;
            foreach (Collider col in target.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled) continue;
                Vector3 offset = col.ClosestPoint(from) - from;
                offset.y = 0f;
                best = Mathf.Min(best, offset.magnitude);
            }
            if (best == float.MaxValue)
            {
                Vector3 offset = target.transform.position - from;
                offset.y = 0f;
                best = offset.magnitude;
            }
            return best;
        }

        void Place()
        {
            if (content == null || canvas == null) return;
            Camera cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (cam == null) return;
            if (canvas.worldCamera == null) canvas.worldCamera = cam;
            if (!layered)
            {
                CanvasLayering.BringToFront(canvas);
                layered = true;
            }
            Vector3 screen = cam.WorldToScreenPoint(local.transform.position + Vector3.up * headHeight);
            if (screen.z < 0f) return;
            // Directo de píxeles a unidades del canvas (como PlayerStatusUI), sin pasar por su transform.
            float scale = Mathf.Max(canvas.scaleFactor, 0.0001f);
            Rect pixels = cam.pixelRect;
            Vector2 target = (new Vector2(screen.x, screen.y) - pixels.center) / scale + screenOffset;
            Vector2 half = pixels.size * 0.5f / scale;
            content.anchoredPosition = new Vector2(
                Mathf.Clamp(target.x, -half.x + screenMargin, half.x - screenMargin),
                Mathf.Clamp(target.y, -half.y + screenMargin, half.y - screenMargin));
        }

        static PlayerController FindLocalPlayer()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player.IsSpawned && player.IsOwner) return player;
            return null;
        }
    }
}
