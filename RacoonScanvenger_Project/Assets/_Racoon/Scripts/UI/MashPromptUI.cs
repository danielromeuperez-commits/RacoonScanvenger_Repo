using Racoon.Player;
using Racoon.UI.P5;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Aviso de «¡MACHACA!» para levantarse tras pisar un plátano, en el estilo P5 pastel del HUD (P5PastelReactivo):
    /// rótulo de letras recortadas, el glifo del botón de Dash del mando que está usando (PS5, Xbox o teclado), un anillo
    /// que se llena con cada pulsación y un estallido detrás que salta al pulsar.
    ///
    /// Montaje: en la raíz de PF_HUD_AvisoMachacar (un Canvas Screen Space - Camera), que va anidado dentro de cada
    /// prefab de jugador (lo monta Racoon > UI > Montar aviso de machacar). Edita PF_HUD_AvisoMachacar y cambia en los
    /// tres: las piezas se pueden mover, escalar y retocar; la animación es relativa a cómo estén colocadas.
    /// Solo lo ve el jugador local, encima de su cabeza (en los demás clientes no se enciende).
    /// Datos: PlayerController.ItemRecoveryStarted / ItemRecoveryMashed / ItemRecoveryFinished e ItemRecoveryNormalized.
    /// </summary>
    // Sin [RequireComponent(typeof(Canvas))]: al cargar un MashPromptUI que no estaba en un Canvas, Unity convertía esa
    // raíz (la del jugador) en RectTransform + Canvas World Space, y el aviso dejaba de verse.
    public class MashPromptUI : MonoBehaviour
    {
        [Tooltip("Vacío = el del jugador en el que está anidado.")]
        [SerializeField] PlayerController controller;
        [SerializeField] InputGlyphSet glyphs;
        [Tooltip("Acción que hay que machacar (nombre en PlayerInput.inputactions).")]
        [SerializeField] string action = "Dash";

        [Header("Piezas")]
        [Tooltip("Todo el aviso: se mueve encima de la cabeza y se enciende / apaga.")]
        public RectTransform content;
        public CanvasGroup group;
        [Tooltip("Su alto manda; el ancho se ajusta al glifo (las teclas anchas, como ESPACIO, se alargan).")]
        public Image glyph;
        [Tooltip("Filled Radial 360: fillAmount = progreso.")]
        public Image ringFill;
        public Image burst;
        public P5Text labelText;

        [Header("Textos")]
        [SerializeField] string label = "¡MACHACA!";
        [SerializeField] string doneLabel = "¡ARRIBA!";

        [Header("Colores")]
        [Tooltip("Relleno del anillo: al empezar y lleno.")]
        [SerializeField] Color fillStart = new(1f, 0.878f, 0.541f);
        [SerializeField] Color fillFull = new(1f, 0.482f, 0.525f);
        [Tooltip("Lleno pero aún aturdido (parpadea con el de lleno): en cuanto acabe el aturdimiento se levanta.")]
        [SerializeField] Color fillReady = Color.white;
        [Tooltip("Tinte del estallido al pulsar.")]
        [SerializeField] Color burstFlash = new(1f, 0.678f, 0.439f);

        [Header("Animación")]
        [Tooltip("Aplastamiento del glifo al pulsar.")]
        [SerializeField] float pressSquash = 0.2f;
        [Tooltip("Pulsaciones por segundo del glifo cuando lleva un rato sin pulsar (recuerda que hay que machacar). 0 = no.")]
        [SerializeField] float idleHintRate = 3.5f;
        [Tooltip("Grados por segundo que gira el estallido.")]
        [SerializeField] float burstSpin = 25f;
        [Tooltip("Crecimiento del estallido al pulsar.")]
        [SerializeField] float burstKick = 0.22f;
        [Tooltip("Temblor del rótulo al pulsar (px).")]
        [SerializeField] float labelShake = 8f;

        [Header("Posición")]
        [Tooltip("Desplazamiento en pantalla desde la cabeza (px a 2560×1440).")]
        [SerializeField] Vector2 screenOffset = new(0f, 210f);
        [Tooltip("Altura sobre el jugador si no encuentra la cabeza (las estrellas de mareo).")]
        [SerializeField] float fallbackHeadHeight = 1.2f;
        [Tooltip("Margen con los bordes de la pantalla (px).")]
        [SerializeField] float screenMargin = 170f;
        [Tooltip("Zona de arriba en el centro que no se pisa (el panel del tiempo), en px a 2560×1440: ancho y alto.")]
        [SerializeField] Vector2 keepOutTop = new(560f, 320f);
        [Tooltip("Cuánto ocupa el aviso desde su centro (px): medio ancho y lo que sube el rótulo.")]
        [SerializeField] Vector2 promptExtents = new(190f, 280f);
        [Tooltip("Si encima de la cabeza pisaría el tiempo, se pone debajo: desplazamiento desde la cabeza (px).")]
        [SerializeField] Vector2 belowOffset = new(0f, -230f);

        Canvas canvas;
        RectTransform canvasRect, glyphRect, burstRect, labelRect;
        PlayerInputHandler input;
        Transform head;

        // Cómo está colocado en el prefab: la animación va encima.
        Vector3 contentScale, glyphScale, burstScale, labelScale;
        Vector2 glyphPosition, labelPosition;
        Quaternion burstRotation, labelRotation;
        float glyphHeight;
        Color burstColor;

        float showTime = -1f, hideTime = -1f, lastMash = -10f, punch, shownFill;
        bool visible, placedBelow;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null || !(transform is RectTransform))
            {
                Debug.LogError("MashPromptUI va en la raíz de PF_HUD_AvisoMachacar (un Canvas Screen Space - Camera).", this);
                enabled = false;
                return;
            }
            canvasRect = (RectTransform)transform;
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            // Directo (no controller.InputHandler): el Awake del PlayerController puede no haber pasado aún.
            if (controller != null) input = controller.GetComponent<PlayerInputHandler>();

            if (glyph != null)
            {
                glyphRect = glyph.rectTransform;
                glyphScale = glyphRect.localScale;
                glyphPosition = glyphRect.anchoredPosition;
                glyphHeight = glyphRect.sizeDelta.y;
            }
            if (burst != null)
            {
                burstRect = burst.rectTransform;
                burstScale = burstRect.localScale;
                burstRotation = burstRect.localRotation;
                burstColor = burst.color;
            }
            if (labelText != null)
            {
                labelRect = (RectTransform)labelText.transform;
                labelScale = labelRect.localScale;
                labelPosition = labelRect.anchoredPosition;
                labelRotation = labelRect.localRotation;
            }
            if (content != null)
            {
                contentScale = content.localScale;
                // En el prefab está encendido para poder editarlo; en partida, apagado hasta que haga falta.
                content.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            if (controller == null) return;
            // Solo se lanzan en el dueño: en los demás clientes el aviso no se enciende nunca.
            controller.ItemRecoveryStarted += OnStarted;
            controller.ItemRecoveryMashed += OnMashed;
            controller.ItemRecoveryFinished += OnFinished;
            if (input != null) input.ActiveKindChanged += OnKindChanged;
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.ItemRecoveryStarted -= OnStarted;
                controller.ItemRecoveryMashed -= OnMashed;
                controller.ItemRecoveryFinished -= OnFinished;
                if (input != null) input.ActiveKindChanged -= OnKindChanged;
            }
            if (content != null) content.gameObject.SetActive(false);
            visible = false;
        }

        // ---------------- Eventos ----------------

        void OnStarted()
        {
            if (content == null) return;
            if (head == null)
            {
                DizzyStarsVFX stars = controller.GetComponentInChildren<DizzyStarsVFX>(true);
                head = stars != null ? stars.transform : null;
            }
            if (labelText != null)
            {
                // Las fichas «del color del jugador» usan el suyo (el mismo que el HUD).
                PlayerVFX vfx = controller.GetComponentInChildren<PlayerVFX>();
                labelText.player = 0;
                if (vfx != null) labelText.colorJugador = vfx.WindTint;
            }

            // Delante del HUD (el tiempo, los retratos...), lo hayan montado con el orden que sea.
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            CanvasLayering.BringToFront(canvas);

            content.gameObject.SetActive(true);
            visible = true;
            showTime = Time.time;
            hideTime = -1f;
            shownFill = 0f;
            punch = 0f;
            if (labelText != null) labelText.SetText(label);
            OnKindChanged(input != null ? input.ActiveKind : InputDeviceKind.KeyboardMouse);
        }

        void OnMashed(float progress)
        {
            lastMash = Time.time;
            punch = 1f;
        }

        void OnFinished()
        {
            if (!visible) return;
            hideTime = Time.time;
            punch = 1f;
            if (labelText != null) labelText.SetText(doneLabel);
        }

        void OnKindChanged(InputDeviceKind kind)
        {
            if (glyph == null || glyphs == null) return;
            Sprite sprite = glyphs.Get(action, kind);
            glyph.sprite = sprite;
            glyph.enabled = sprite != null;
            if (sprite == null) return;
            // Mismo alto que en el prefab; el ancho, el del glifo.
            float aspect = sprite.rect.width / sprite.rect.height;
            glyphRect.sizeDelta = new Vector2(glyphHeight * aspect, glyphHeight);
        }

        // ---------------- Animación ----------------

        void LateUpdate()
        {
            if (!visible || content == null) return;
            float time = Time.time;
            float dt = Time.deltaTime;

            // Entrada con rebote y salida (al levantarse) creciendo y desvaneciéndose.
            float appear = FlatFx.OutBack(FlatFx.Phase(time, showTime, showTime + 0.3f), 2.2f);
            float leave = hideTime >= 0f ? FlatFx.Phase(time, hideTime, hideTime + 0.35f) : 0f;
            if (leave >= 1f)
            {
                visible = false;
                content.gameObject.SetActive(false);
                return;
            }
            if (group != null) group.alpha = 1f - FlatFx.InCubic(leave);
            content.localScale = contentScale * (appear * (1f + 0.25f * FlatFx.OutCubic(leave)));

            FollowHead();

            punch = Mathf.MoveTowards(punch, 0f, dt / 0.18f);
            float kick = FlatFx.OutCubic(punch);

            // Glifo: aplastado al pulsar; si lleva un rato sin pulsar, se «pulsa solo» para recordar que hay que machacar.
            if (glyphRect != null)
            {
                bool idle = idleHintRate > 0f && time - lastMash > 0.35f && hideTime < 0f;
                float hint = idle ? Mathf.Max(0f, Mathf.Sin(time * Mathf.PI * 2f * idleHintRate)) : 0f;
                float squash = Mathf.Max(kick, hint * 0.6f);
                glyphRect.localScale = Vector3.Scale(glyphScale,
                    new Vector3(1f + pressSquash * 0.9f * squash, 1f - pressSquash * 1.1f * squash, 1f) * (1f + 0.12f * kick));
                glyphRect.anchoredPosition = glyphPosition + new Vector2(0f, -10f * squash);
            }

            // Anillo: sigue al progreso (que baja solo si deja de machacar) y pasa del color inicial al de lleno.
            if (ringFill != null)
            {
                float progress = hideTime >= 0f ? 1f : controller.ItemRecoveryNormalized;
                shownFill = Mathf.Lerp(shownFill, progress, 1f - Mathf.Exp(-18f * dt));
                ringFill.fillAmount = shownFill;
                Color fill = Color.Lerp(fillStart, fillFull, shownFill);
                if (progress >= 1f && hideTime < 0f && Mathf.Repeat(time * 6f, 1f) < 0.5f) fill = fillReady;
                ringFill.color = fill;
            }

            // Estallido: gira despacio, salta y se tiñe al pulsar.
            if (burstRect != null)
            {
                burstRect.localRotation = burstRotation * Quaternion.Euler(0f, 0f, time * burstSpin + 18f * kick);
                burstRect.localScale = burstScale * (1f + burstKick * kick + 0.03f * Mathf.Sin(time * 5f));
                burst.color = Color.Lerp(burstColor, burstFlash, kick);
            }

            // Rótulo: tiembla al pulsar.
            if (labelRect != null)
            {
                labelRect.anchoredPosition = labelPosition +
                    new Vector2(Random.Range(-1f, 1f) * labelShake * kick, 10f * kick);
                labelRect.localRotation = labelRotation * Quaternion.Euler(0f, 0f, Random.Range(-1f, 1f) * 4f * kick);
                labelRect.localScale = labelScale;
            }
        }

        void FollowHead()
        {
            Camera cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (cam == null) return;
            if (canvas.worldCamera == null) canvas.worldCamera = cam;
            Vector3 world = head != null ? head.position : controller.transform.position + Vector3.up * fallbackHeadHeight;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out Vector2 local)) return;
            Vector2 half = canvasRect.rect.size * 0.5f;
            Vector2 target = Clamp(local + screenOffset, half);
            // Encima de la cabeza pisaría el panel del tiempo: debajo de la cabeza. Para volver arriba pide algo de
            // holgura, así no salta de un lado a otro en el borde.
            placedBelow = OverlapsKeepOut(target, half, placedBelow ? 60f : 0f);
            if (placedBelow) target = Clamp(local + belowOffset, half);
            content.anchoredPosition = target;
        }

        // Sin salirse de la pantalla.
        Vector2 Clamp(Vector2 target, Vector2 half) => new(
            Mathf.Clamp(target.x, -half.x + screenMargin, half.x - screenMargin),
            Mathf.Clamp(target.y, -half.y + screenMargin, half.y - screenMargin - 120f));

        bool OverlapsKeepOut(Vector2 position, Vector2 half, float slack) =>
            Mathf.Abs(position.x) < keepOutTop.x * 0.5f + promptExtents.x + slack &&
            position.y + promptExtents.y > half.y - keepOutTop.y - slack;
    }
}
