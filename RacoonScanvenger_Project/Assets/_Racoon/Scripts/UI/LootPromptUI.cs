using Racoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Aviso de «mantén para saquear» (PF_HUD_AvisoSaquear: arte en Photoshop, animaciones en su Animator). Uno solo
    /// por instancia del juego: lo crea la primera estantería (LootPoint) y va pegado al jugador LOCAL (junto a la cabeza,
    /// como la bolsa); se enciende al acercarse a una estantería con algo que coger. Solo el botón de Interact y su dial: el dial se llena mientras mantiene y vuelve a cero en
    /// cada objeto que coge (con un saltito, el parámetro Grab).
    ///
    /// Este script solo coloca el aviso, pone el glifo, rellena el dial y mueve los parámetros del Animator
    /// (Visible, Holding, Grab).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class LootPromptUI : MonoBehaviour
    {
        static readonly int VisibleId = Animator.StringToHash("Visible");
        static readonly int HoldingId = Animator.StringToHash("Holding");
        static readonly int PressId = Animator.StringToHash("Press");
        static readonly int GrabId = Animator.StringToHash("Grab");

        static LootPromptUI instance;

        [SerializeField] InputGlyphSet glyphs;
        [Tooltip("Acción que hay que mantener (nombre en PlayerInput.inputactions).")]
        [SerializeField] string action = "Interact";

        [Header("Piezas")]
        [Tooltip("Todo el aviso: se coloca sobre la estantería.")]
        public RectTransform content;
        public Animator animator;
        [Tooltip("Su alto manda; el ancho se ajusta al glifo.")]
        public Image glyph;
        [Tooltip("Filled Radial 360: progreso hasta el siguiente objeto.")]
        public Image ringFill;

        // Solo sale si el objeto está al alcance de la interacción del jugador (PlayerController.InteractReach).
        [Tooltip("Segundos que tarda el dial en llenarse entre objeto y objeto (lo que dura el gesto de coger).")]
        [SerializeField] float cycleTime = 0.42f;

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
        PlayerController local;
        PlayerLoot localLoot;
        PlayerInputHandler localInput;
        LootPoint target;
        float nextSearch, held, glyphHeight, lastPress = -10f;
        bool layered, shown;

        /// <summary>Lo llama cada LootPoint al empezar: solo se crea uno.</summary>
        public static void EnsureInstance(LootPromptUI prefab)
        {
            if (instance != null || prefab == null || Application.isBatchMode) return;
            instance = Instantiate(prefab);
        }

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (glyph != null) glyphHeight = glyph.rectTransform.sizeDelta.y;
        }

        void OnEnable() => PlayerLoot.AnyLootGrabbed += OnLootGrabbed;

        void OnDisable()
        {
            PlayerLoot.AnyLootGrabbed -= OnLootGrabbed;
            SetLocal(null);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnLootGrabbed(PlayerLoot loot, bool doubled, Vector3 position, int points)
        {
            if (loot == null || loot != localLoot) return;
            held = 0f;
            animator.SetTrigger(GrabId);
        }

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
            if (localInput == null) return;
            localInput.ActiveKindChanged += OnKindChanged;
            localInput.InteractPressed += OnInteractPressed;
            OnKindChanged(localInput.ActiveKind);
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
            if ((local == null || !local.IsSpawned) && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 1f;
                SetLocal(FindLocalPlayer());
            }

            float distance = float.MaxValue;
            bool visible = false;
            if (local != null && localLoot != null)
            {
                FindTarget(local.transform.position, out distance);
                visible = distance <= local.InteractReach && target != null && !localLoot.IsBagFull;
            }
            bool holding = visible && localInput != null && (localInput.InteractHeld || Time.time - lastPress <= tapWindow);

            // El dial va de objeto a objeto (cycleTime).
            held = holding ? Mathf.Min(1f, held + Time.deltaTime / Mathf.Max(0.05f, cycleTime * (local != null ? local.ActionTimeScale : 1f)))
                : Mathf.MoveTowards(held, 0f, Time.deltaTime * 4f);
            if (ringFill != null) ringFill.fillAmount = held;

            shown = visible;
            animator.SetBool(VisibleId, visible);
            animator.SetBool(HoldingId, holding);
            if (local != null) Place();
        }

        // La estantería con algo que coger más cercana (distancia al borde de su zona de saqueo, en el suelo).
        void FindTarget(Vector3 from, out float bestDistance)
        {
            bestDistance = float.MaxValue;
            LootPoint best = null;
            foreach (LootPoint point in LootPoint.All)
            {
                if (point == null || !point.LooksLootable) continue;
                Collider col = point.GetComponent<Collider>();
                Vector3 closest = col != null ? col.ClosestPoint(from) : point.transform.position;
                Vector3 offset = closest - from;
                offset.y = 0f;
                float d = offset.magnitude;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = point;
            }
            // Se queda con la última mientras desaparece, para que no salte a otra a media animación.
            if (best != null && bestDistance <= (local != null ? local.InteractReach : 0f))
            {
                target = best;
            }
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
