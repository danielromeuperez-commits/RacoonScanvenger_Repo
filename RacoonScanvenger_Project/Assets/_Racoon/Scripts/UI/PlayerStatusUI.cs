using Racoon.Feedback;
using Racoon.Items;
using Racoon.Player;
using Racoon.UI.P5;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Indicadores junto al jugador local, como la rueda de estamina de Breath of the Wild, en el estilo P5 pastel:
    ///  - Bolsa: anillo amarillo dorado que se llena con los puntos sin vender hasta BagCapacity (100), con una marca
    ///    en FullWeightPoints (80), donde el peso llega a su máximo. Llena, late y dice «¡LLENA!»; si intenta saquear
    ///    así, tiembla. Se ve mientras lleva algo en la bolsa.
    ///  - Bebida energética: la lata con un anillo que se vacía con lo que le queda (parpadea al final). Si intenta
    ///    tomarse otra con la primera activa (o usar un objeto que ahora no se puede), la lata tiembla y sale
    ///    «¡YA VAS A TOPE!»; el objeto se queda en la mano.
    ///
    /// Montaje: en la raíz de PF_HUD_EstadoJugador (un Canvas Screen Space - Camera), anidado dentro de cada prefab de
    /// jugador (lo monta Racoon > UI > Montar indicadores del jugador). Edita PF_HUD_EstadoJugador y cambia en los tres;
    /// la animación es relativa a cómo estén colocadas las piezas. Solo lo ve el dueño del jugador.
    /// Datos: PlayerLoot (PendingPoints, BagCapacity, FullWeightPoints, BagFullRejected) y PlayerController
    /// (IsEnergized, EnergyRemaining, EnergyDuration, ItemUseRefused).
    /// </summary>
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

        [Header("Bebida energética")]
        public RectTransform energyGroup;
        public CanvasGroup energyCanvasGroup;
        [Tooltip("Filled Radial 360: fillAmount = lo que le queda.")]
        public Image energyFill;
        public P5Text energyLabel;

        [Header("Textos")]
        [SerializeField] string bagFullText = "¡LLENA!";
        [SerializeField] string energyBusyText = "¡YA VAS A TOPE!";
        [SerializeField] string itemRefusedText = "¡AHORA NO!";

        [Header("Colores")]
        [Tooltip("Relleno de la bolsa (amarillo dorado).")]
        [SerializeField] Color bagColor = new(1f, 0.78f, 0.2f);
        [Tooltip("Relleno con la bolsa llena (parpadea con el normal).")]
        [SerializeField] Color bagFullColor = new(1f, 0.93f, 0.62f);
        [Tooltip("Marca de peso máximo: sin llegar y ya pasada.")]
        [SerializeField] Color markColor = new(0.294f, 0.263f, 0.4f);
        [SerializeField] Color markReachedColor = new(1f, 0.482f, 0.525f);
        [Tooltip("Anillo de la bebida.")]
        [SerializeField] Color energyColor = new(0.35f, 0.9f, 1f);
        [SerializeField] Color energyEndingColor = Color.white;

        [Header("Animación")]
        [SerializeField] float fillSmoothing = 14f;
        [Tooltip("Segundos de bebida en los que el anillo parpadea (se está acabando).")]
        [SerializeField] float energyEndingTime = 2f;
        [Tooltip("Segundos que se ve el aviso de «no se puede».")]
        [SerializeField] float refusedTime = 1.1f;
        [SerializeField] float shakeAmount = 14f;

        [Header("Posición")]
        [Tooltip("Altura sobre los pies del punto de pantalla que siguen (px se suman desde ahí).")]
        [SerializeField] float anchorHeight = 0.9f;
        [Tooltip("Desplazamiento en pantalla de la bolsa desde ese punto (px a 2560×1440).")]
        [SerializeField] Vector2 bagOffset = new(150f, 10f);
        [Tooltip("Desplazamiento en pantalla de la bebida (al otro lado).")]
        [SerializeField] Vector2 energyOffset = new(-150f, 10f);
        [Tooltip("Margen con los bordes de la pantalla (px).")]
        [SerializeField] float screenMargin = 90f;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al intentar usar algo que ahora no se puede (otra bebida) o saquear con la bolsa llena: «bzzt» corto.")]
        [SerializeField] SfxCue refusedSfx = new();

        Canvas canvas;
        RectTransform canvasRect, bagLabelRect, energyLabelRect;
        PlayerLoot loot;

        // Cómo está colocado en el prefab: la animación va encima.
        Vector3 bagScale, energyScale, bagLabelScale, energyLabelScale;

        float bagShown, bagAlpha, energyAlpha, bagShake = -10f, energyShake = -10f, energyRefused = -10f, energyPop;
        Vector2 energyShakeOffset;
        bool layered;

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
            if (energyGroup != null) energyScale = energyGroup.localScale;
            if (bagLabel != null)
            {
                bagLabelRect = (RectTransform)bagLabel.transform;
                bagLabelScale = bagLabelRect.localScale;
                bagLabel.SetText(bagFullText);
            }
            if (energyLabel != null)
            {
                energyLabelRect = (RectTransform)energyLabel.transform;
                energyLabelScale = energyLabelRect.localScale;
            }
            // En el prefab está todo encendido para poder editarlo; en partida, apagado hasta que haga falta.
            SetVisible(bagGroup, bagCanvasGroup, 0f);
            SetVisible(energyGroup, energyCanvasGroup, 0f);
        }

        void OnEnable()
        {
            if (controller != null) controller.ItemUseRefused += OnItemUseRefused;
            if (loot != null) loot.BagFullRejected += OnBagFullRejected;
        }

        void OnDisable()
        {
            if (controller != null) controller.ItemUseRefused -= OnItemUseRefused;
            if (loot != null) loot.BagFullRejected -= OnBagFullRejected;
        }

        // ---------------- Eventos (solo en el dueño) ----------------

        void OnItemUseRefused(ItemData item)
        {
            energyShake = energyRefused = Time.time;
            energyPop = 1f;
            if (energyLabel != null)
                energyLabel.SetText(controller.IsEnergized && item != null && item.Effect is EnergyDrinkEffect ? energyBusyText : itemRefusedText);
            refusedSfx.Play(controller.transform.position);
        }

        void OnBagFullRejected()
        {
            bagShake = Time.time;
            refusedSfx.Play(controller.transform.position);
        }

        // ---------------- Animación ----------------

        void LateUpdate()
        {
            if (controller == null || !controller.IsSpawned || !controller.IsOwner)
            {
                SetVisible(bagGroup, bagCanvasGroup, 0f);
                SetVisible(energyGroup, energyCanvasGroup, 0f);
                return;
            }

            float time = Time.time;
            float dt = Time.deltaTime;
            if (!layered && canvas.worldCamera != null)
            {
                // Delante del HUD, lo hayan montado con el orden que sea.
                CanvasLayering.BringToFront(canvas);
                layered = true;

                // Las fichas «del color del jugador» de los rótulos usan el suyo (el mismo que el HUD).
                PlayerVFX vfx = controller.GetComponentInChildren<PlayerVFX>();
                if (vfx != null)
                {
                    if (bagLabel != null)
                    {
                        bagLabel.colorJugador = vfx.WindTint;
                        bagLabel.Rebuild();
                    }
                    if (energyLabel != null) energyLabel.colorJugador = vfx.WindTint;
                }
            }

            UpdateBag(time, dt);
            UpdateEnergy(time, dt);
            Follow();
        }

        void UpdateBag(float time, float dt)
        {
            if (bagGroup == null || loot == null) return;
            float fill = loot.BagFillNormalized;
            bool full = loot.IsBagFull;
            bagShown = Mathf.Lerp(bagShown, fill, 1f - Mathf.Exp(-fillSmoothing * dt));

            // Se ve mientras lleva algo (y mientras el anillo se vacía al vender).
            bool show = loot.PendingPoints > 0 || bagShown > 0.01f;
            bagAlpha = Mathf.MoveTowards(bagAlpha, show ? 1f : 0f, dt / 0.2f);
            SetVisible(bagGroup, bagCanvasGroup, bagAlpha);
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

        void UpdateEnergy(float time, float dt)
        {
            if (energyGroup == null) return;
            bool energized = controller.IsEnergized;
            bool refused = time - energyRefused < refusedTime;
            energyAlpha = Mathf.MoveTowards(energyAlpha, energized || refused ? 1f : 0f, dt / 0.2f);
            SetVisible(energyGroup, energyCanvasGroup, energyAlpha);
            if (energyAlpha <= 0f) return;

            energyPop = Mathf.MoveTowards(energyPop, 0f, dt / 0.25f);
            float shake = 1f - FlatFx.Phase(time, energyShake, energyShake + 0.4f);
            energyGroup.localScale = energyScale * (FlatFx.OutBack(energyAlpha, 2f) * (1f + 0.15f * FlatFx.OutCubic(energyPop)));
            energyShakeOffset = new Vector2(Mathf.Sin(time * 70f) * shakeAmount * shake, 0f);

            if (energyFill != null)
            {
                float duration = Mathf.Max(controller.EnergyDuration, 0.01f);
                float remaining = energized ? controller.EnergyRemaining : 0f;
                energyFill.fillAmount = Mathf.Clamp01(remaining / duration);
                bool ending = energized && remaining < energyEndingTime && Mathf.Repeat(time * 5f, 1f) < 0.5f;
                energyFill.color = ending ? energyEndingColor : energyColor;
            }

            if (energyLabel != null)
            {
                if (energyLabel.gameObject.activeSelf != refused) energyLabel.gameObject.SetActive(refused);
                if (refused)
                {
                    float t = FlatFx.Phase(time, energyRefused, energyRefused + refusedTime);
                    energyLabelRect.localScale = energyLabelScale * (FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.15f), 2.5f) * (1f - FlatFx.InCubic(FlatFx.Phase(t, 0.8f, 1f))));
                }
            }
        }

        void Follow()
        {
            Camera cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (cam == null) return;
            if (canvas.worldCamera == null) canvas.worldCamera = cam;
            Vector3 screen = cam.WorldToScreenPoint(controller.transform.position + Vector3.up * anchorHeight);
            if (screen.z < 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local)) return;
            Vector2 half = canvasRect.rect.size * 0.5f;
            if (bagGroup != null) bagGroup.anchoredPosition = Clamp(local + bagOffset, half);
            if (energyGroup != null) energyGroup.anchoredPosition = Clamp(local + energyOffset, half) + energyShakeOffset;
        }

        Vector2 Clamp(Vector2 target, Vector2 half) => new(
            Mathf.Clamp(target.x, -half.x + screenMargin, half.x - screenMargin),
            Mathf.Clamp(target.y, -half.y + screenMargin, half.y - screenMargin));

        static void SetVisible(RectTransform group, CanvasGroup canvasGroup, float alpha)
        {
            if (group == null) return;
            bool on = alpha > 0f;
            if (group.gameObject.activeSelf != on) group.gameObject.SetActive(on);
            if (canvasGroup != null) canvasGroup.alpha = alpha;
        }
    }
}
