using Racoon.Player;
using Racoon.UI.P5;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Aviso de «mantén para vender» sobre el punto de venta (PF_HUD_AvisoVender: arte en Photoshop, animaciones en su
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

        [Header("Distancias (m, en el suelo)")]
        [Tooltip("A esta distancia del punto de venta aparece el aviso (si lleva algo en la bolsa).")]
        [SerializeField] float showDistance = 3.2f;
        [Tooltip("A esta distancia ya puede vender: el anillo se llena si mantiene.")]
        [SerializeField] float sellDistance = 1.9f;

        [Header("Posición")]
        [Tooltip("Altura sobre el punto de venta (m).")]
        [SerializeField] float worldHeight = 1.1f;
        [SerializeField] float screenMargin = 120f;
        [Tooltip("Margen con el borde de arriba (px a 2560×1440): ahí está la franja del HUD (tiempo, retratos, puntos).")]
        [SerializeField] float topMargin = 320f;

        Canvas canvas;
        SellPoint sellPoint;
        PlayerController local;
        PlayerLoot localLoot;
        PlayerInputHandler localInput;
        float nextSearch, held, glyphHeight;
        int lastPending;
        bool layered;
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
            if (localInput != null) localInput.ActiveKindChanged -= OnKindChanged;
            local = player;
            localLoot = player != null ? player.GetComponent<PlayerLoot>() : null;
            localInput = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            if (localInput != null)
            {
                localInput.ActiveKindChanged += OnKindChanged;
                OnKindChanged(localInput.ActiveKind);
            }
            lastPending = localLoot != null ? localLoot.PendingPoints : 0;
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
                Vector3 offset = local.transform.position - sellPoint.transform.position;
                offset.y = 0f;
                float distance = offset.magnitude;
                int pending = localLoot.PendingPoints;

                // Acaba de vender aquí: «¡VENDIDO!» (el Animator lo enseña aunque ya no quede nada en la bolsa).
                if (pending == 0 && lastPending > 0 && distance <= sellDistance)
                {
                    animator.SetTrigger(SoldId);
                    SetLabel(soldText);
                    held = 1f;
                }
                lastPending = pending;

                visible = pending > 0 && distance <= showDistance;
                holding = visible && distance <= sellDistance && localInput != null && localInput.InteractHeld;
            }

            // Mismo tiempo que pide el SellPoint en el servidor; al soltar se vacía rápido.
            float holdTime = Mathf.Max(0.05f, sellPoint.HoldToSellTime);
            held = holding ? Mathf.Min(1f, held + Time.deltaTime / holdTime)
                : Mathf.MoveTowards(held, 0f, Time.deltaTime * 4f);
            if (ringFill != null) ringFill.fillAmount = held;

            if (visible) SetLabel(holding ? sellingText : holdText);
            animator.SetBool(VisibleId, visible);
            animator.SetBool(HoldingId, holding);

            Place();
        }

        void SetLabel(string text)
        {
            if (label == null || text == shownText) return;
            shownText = text;
            label.SetText(text);
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
            Vector3 screen = cam.WorldToScreenPoint(sellPoint.transform.position + Vector3.up * worldHeight);
            if (screen.z < 0f) return;
            // Directo de píxeles a unidades del canvas (como PlayerStatusUI), sin pasar por su transform.
            float scale = Mathf.Max(canvas.scaleFactor, 0.0001f);
            Rect pixels = cam.pixelRect;
            Vector2 local2 = (new Vector2(screen.x, screen.y) - pixels.center) / scale;
            Vector2 half = pixels.size * 0.5f / scale;
            content.anchoredPosition = new Vector2(
                Mathf.Clamp(local2.x, -half.x + screenMargin, half.x - screenMargin),
                Mathf.Clamp(local2.y, -half.y + screenMargin, half.y - Mathf.Max(screenMargin, topMargin)));
        }

        static PlayerController FindLocalPlayer()
        {
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                if (player.IsSpawned && player.IsOwner) return player;
            return null;
        }
    }
}
