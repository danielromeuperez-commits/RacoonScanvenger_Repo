using Racoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Aviso de «mantén para saquear» (PF_HUD_AvisoSaquear: arte en Photoshop, animaciones en su Animator). Uno solo
    /// por instancia del juego: lo crea la primera estantería (LootPoint) y sigue a la más cercana del jugador LOCAL que
    /// tenga algo que coger. Solo el botón de Interact y su dial: el dial se llena mientras mantiene y vuelve a cero en
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

        [Header("Distancias (m, en el suelo, hasta el borde de la estantería)")]
        [SerializeField] float showDistance = 1.8f;
        [Tooltip("A esta distancia ya está saqueando si mantiene.")]
        [SerializeField] float lootDistance = 1.1f;
        [Tooltip("Segundos que tarda el dial en llenarse entre objeto y objeto (lo que dura el gesto de coger).")]
        [SerializeField] float cycleTime = 0.42f;

        [Header("Posición")]
        [Tooltip("Altura por encima de lo alto de la estantería (m).")]
        [SerializeField] float heightAboveShelf = 0.25f;
        [SerializeField] float screenMargin = 120f;
        [Tooltip("Margen con el borde de arriba (px a 2560×1440): ahí está la franja del HUD.")]
        [SerializeField] float topMargin = 320f;

        Canvas canvas;
        PlayerController local;
        PlayerLoot localLoot;
        PlayerInputHandler localInput;
        LootPoint target;
        Collider targetCollider;
        float nextSearch, held, glyphHeight;
        bool layered;

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
            if (localInput != null) localInput.ActiveKindChanged -= OnKindChanged;
            local = player;
            localLoot = player != null ? player.GetComponent<PlayerLoot>() : null;
            localInput = player != null ? player.GetComponent<PlayerInputHandler>() : null;
            if (localInput == null) return;
            localInput.ActiveKindChanged += OnKindChanged;
            OnKindChanged(localInput.ActiveKind);
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
            if (local != null && localLoot != null && !localLoot.IsBagFull)
            {
                FindTarget(local.transform.position, out distance);
                visible = target != null && distance <= showDistance;
            }
            bool holding = visible && distance <= lootDistance && localInput != null && localInput.InteractHeld;

            held = holding ? Mathf.Min(1f, held + Time.deltaTime / Mathf.Max(0.05f, cycleTime))
                : Mathf.MoveTowards(held, 0f, Time.deltaTime * 4f);
            if (ringFill != null) ringFill.fillAmount = held;

            animator.SetBool(VisibleId, visible);
            animator.SetBool(HoldingId, holding);
            if (target != null) Place();
        }

        // La estantería con algo que coger más cercana (distancia al borde de su zona de saqueo, en el suelo).
        void FindTarget(Vector3 from, out float bestDistance)
        {
            bestDistance = float.MaxValue;
            LootPoint best = null;
            Collider bestCollider = null;
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
                bestCollider = col;
            }
            // Se queda con la última mientras desaparece, para que no salte a otra a media animación.
            if (best != null && bestDistance <= showDistance)
            {
                target = best;
                targetCollider = bestCollider;
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
            Bounds bounds = targetCollider != null ? targetCollider.bounds : new Bounds(target.transform.position, Vector3.zero);
            Vector3 world = new Vector3(bounds.center.x, bounds.max.y + heightAboveShelf, bounds.center.z);
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;
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
