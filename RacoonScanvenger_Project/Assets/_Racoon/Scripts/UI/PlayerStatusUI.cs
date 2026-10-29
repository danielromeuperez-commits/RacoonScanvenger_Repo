using Racoon.Feedback;
using Racoon.Player;
using Racoon.UI.P5;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Anillo de la bolsa a la derecha de la cabeza del jugador local (en pantalla, mire hacia donde mire), como la
    /// rueda de estamina de Breath of the Wild, en el estilo P5 pastel: amarillo dorado, se llena con los puntos sin
    /// vender hasta BagCapacity (100) y tiene una marca en FullWeightPoints (80), donde el peso llega a su máximo.
    /// Llena, late y dice «¡LLENA!»; si intenta saquear así, tiembla. Se ve mientras lleva algo en la bolsa.
    ///
    /// Montaje: en la raíz de PF_HUD_EstadoJugador (un Canvas Screen Space - Camera), anidado dentro de cada prefab de
    /// jugador. Edita PF_HUD_EstadoJugador y cambia en los tres; la animación es relativa a cómo estén colocadas las
    /// piezas. Solo lo ve el dueño del jugador. Se coloca después de que la cámara (Cinemachine) se haya movido en el
    /// frame, así va pegado al jugador sin retraso ni tirones.
    /// Datos: PlayerLoot (PendingPoints, BagCapacity, FullWeightPoints, BagFullRejected).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PlayerStatusUI : MonoBehaviour
    {
        [Tooltip("Vacío = el del jugador en el que está anidado.")]
        [SerializeField] PlayerController controller;

        [Header("Bolsa")]
        public RectTransform bagGroup;
        public CanvasGroup bagCanvasGroup;
        [Tooltip("Filled Radial 360: fillAmount = puntos sin vender / capacidad.")]
        public Image bagFill;
        [Tooltip("Gira alrededor del centro del anillo hasta el punto de peso máximo (su hijo es la marca en el borde).")]
        public RectTransform weightMark;
        public Image weightMarkImage;
        public P5Text bagLabel;

        [Header("Textos")]
        [SerializeField] string bagFullText = "¡LLENA!";

        [Header("Colores")]
        [Tooltip("Relleno de la bolsa (amarillo dorado).")]
        [SerializeField] Color bagColor = new(1f, 0.78f, 0.2f);
        [Tooltip("Relleno con la bolsa llena (parpadea con el normal).")]
        [SerializeField] Color bagFullColor = new(1f, 0.93f, 0.62f);
        [Tooltip("Marca de peso máximo: sin llegar y ya pasada.")]
        [SerializeField] Color markColor = new(0.294f, 0.263f, 0.4f);
        [SerializeField] Color markReachedColor = new(1f, 0.482f, 0.525f);

        [Header("Animación")]
        [SerializeField] float fillSmoothing = 14f;

        [Header("Posición")]
        [Tooltip("Altura de la cabeza sobre los pies (m). Fija, para que no se mueva con la animación. El jugador mide 0,7.")]
        [SerializeField] float headHeight = 0.55f;
        [Tooltip("Desplazamiento en pantalla de la bolsa desde la cabeza (px a 2560×1440): a su derecha.")]
        [SerializeField] Vector2 bagOffset = new(80f, 0f);
        [Tooltip("Margen con los bordes de la pantalla (px).")]
        [SerializeField] float screenMargin = 90f;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al intentar saquear con la bolsa llena: «bzzt» corto.")]
        [SerializeField] SfxCue refusedSfx = new();

        Canvas canvas;
        RectTransform canvasRect, bagLabelRect;
        PlayerLoot loot;

        // Cómo está colocado en el prefab: la animación va encima.
        Vector3 bagScale, bagLabelScale;

        float bagShown, bagAlpha, bagShake = -10f, nextColorTry;
        bool layered, colored;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null || !(transform is RectTransform))
            {
                Debug.LogError("PlayerStatusUI va en la raíz de PF_HUD_EstadoJugador (un Canvas Screen Space - Camera).", this);
                enabled = false;
                return;
            }
            canvasRect = (RectTransform)transform;
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            if (controller != null) loot = controller.GetComponent<PlayerLoot>();

            if (bagGroup != null) bagScale = bagGroup.localScale;
            if (bagLabel != null)
            {
                bagLabelRect = (RectTransform)bagLabel.transform;
                bagLabelScale = bagLabelRect.localScale;
                bagLabel.SetText(bagFullText);
            }
            // En el prefab está encendido para poder editarlo; en partida, apagado hasta que haga falta.
            SetVisible(0f);
        }

        void OnEnable()
        {
            if (loot != null) loot.BagFullRejected += OnBagFullRejected;
        }

        void OnDisable()
        {
            if (loot != null) loot.BagFullRejected -= OnBagFullRejected;
        }

        // Solo en el dueño.
        void OnBagFullRejected()
        {
            bagShake = Time.time;
            refusedSfx.Play(controller.transform.position);
        }

        // ---------------- Animación ----------------

        void LateUpdate()
        {
            if (controller == null || loot == null || bagGroup == null || !controller.IsSpawned || !controller.IsOwner)
            {
                SetVisible(0f);
                return;
            }

            float time = Time.time;
            float dt = Time.deltaTime;
            if (!layered && canvas.worldCamera != null)
            {
                // Delante del HUD, lo hayan montado con el orden que sea.
                CanvasLayering.BringToFront(canvas);
                layered = true;
            }
            // Las fichas «del color del jugador» del rótulo: el de su P1 / P2 en el HUD de la partida (hasta que el HUD
            // le dé puesto, se reintenta).
            if (!colored && time >= nextColorTry)
            {
                nextColorTry = time + 0.5f;
                if (PlayerHudColor.TryGet(controller, out Color color)) ApplyLabelColor(color);
            }

            UpdateBag(time, dt);
            Follow();
        }

        void ApplyLabelColor(Color color)
        {
            colored = true;
            if (bagLabel == null) return;
            bagLabel.colorJugador = color;
            bagLabel.Rebuild();
        }

        void UpdateBag(float time, float dt)
        {
            float fill = loot.BagFillNormalized;
            bool full = loot.IsBagFull;
            bagShown = Mathf.Lerp(bagShown, fill, 1f - Mathf.Exp(-fillSmoothing * dt));

            // Se ve mientras lleva algo (y mientras el anillo se vacía al vender).
            bool show = loot.PendingPoints > 0 || bagShown > 0.01f;
            bagAlpha = Mathf.MoveTowards(bagAlpha, show ? 1f : 0f, dt / 0.2f);
            SetVisible(bagAlpha);
            if (bagAlpha <= 0f) return;

            float shake = 1f - FlatFx.Phase(time, bagShake, bagShake + 0.35f);
            float pulse = full ? 0.05f * Mathf.Sin(time * Mathf.PI * 2f * 2.5f) : 0f;
            bagGroup.localScale = bagScale * (FlatFx.OutBack(bagAlpha, 2f) * (1f + pulse + 0.12f * shake));
            bagGroup.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 60f) * 8f * shake);

            if (bagFill != null)
            {
                bagFill.fillAmount = bagShown;
                bagFill.color = full && Mathf.Repeat(time * 3f, 1f) < 0.5f ? bagFullColor : bagColor;
            }

            // Marca del peso máximo: donde está FullWeightPoints en el anillo; pasada, cambia de color.
            float weightAt = Mathf.Clamp01((float)loot.FullWeightPoints / loot.BagCapacity);
            if (weightMark != null) weightMark.localRotation = Quaternion.Euler(0f, 0f, -360f * weightAt);
            if (weightMarkImage != null) weightMarkImage.color = bagShown >= weightAt ? markReachedColor : markColor;

            if (bagLabel != null)
            {
                bool label = full || shake > 0f;
                if (bagLabel.gameObject.activeSelf != label) bagLabel.gameObject.SetActive(label);
                if (label) bagLabelRect.localScale = bagLabelScale * (1f + 0.25f * shake);
            }
        }

        void Follow()
        {
            Camera cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (cam == null) return;
            if (canvas.worldCamera == null) canvas.worldCamera = cam;
            Vector3 screen = cam.WorldToScreenPoint(controller.transform.position + Vector3.up * headHeight);
            if (screen.z < 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local)) return;
            Vector2 half = canvasRect.rect.size * 0.5f;
            Vector2 target = local + bagOffset;
            bagGroup.anchoredPosition = new Vector2(
                Mathf.Clamp(target.x, -half.x + screenMargin, half.x - screenMargin),
                Mathf.Clamp(target.y, -half.y + screenMargin, half.y - screenMargin));
        }

        void SetVisible(float alpha)
        {
            if (bagGroup == null) return;
            bool on = alpha > 0f;
            if (bagGroup.gameObject.activeSelf != on) bagGroup.gameObject.SetActive(on);
            if (bagCanvasGroup != null) bagCanvasGroup.alpha = alpha;
        }
    }
}
