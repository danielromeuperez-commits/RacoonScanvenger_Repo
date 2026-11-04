using Racoon.Cameras;
using Racoon.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.Events
{
    /// <summary>
    /// UI común de los eventos, en el prefab PF_UI_EventTicker (sprites de Photoshop, UI_Eventos_Cinta.psd, y Animator
    /// AC_EventTicker):
    ///  - Cinta tipo rótulo de telediario abajo. Guion: la banda entra de derecha a izquierda sin texto, la placa sube
    ///    desde abajo, el texto entra por la derecha y pasa unas vueltas seguidas; cuando la última sale, ya no vuelve,
    ///    y la placa baja mientras la banda se va hacia la izquierda.
    ///  - Tarjeta de estado pequeña, fija en el lateral derecho de la pantalla (p. ej. «¡OFERTA x2! · QUEDAN 7»).
    ///
    /// Este script solo pone textos y colores, mueve el texto de la cinta (su largo depende del
    /// evento) y da los parámetros del Animator: «Mostrar», «Salir» (al acabar el texto), «Tarjeta» y «TarjetaGolpe».
    /// Los colores van pintados en los sprites (PSD); aquí no se tiñe nada: cada evento elige una «piel» (Verde,
    /// Amarillo, Rojo) y se cambian los sprites de la placa y la franja de la cinta.
    ///
    /// No se monta a mano: la primera vez que un evento lo usa se instancia el prefab de <see cref="GameEventTheme"/>.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class GameEventAnnouncer : MonoBehaviour
    {
        static readonly int ShowId = Animator.StringToHash("Mostrar");
        static readonly int LeaveId = Animator.StringToHash("Salir");
        static readonly int CardId = Animator.StringToHash("Tarjeta");
        static readonly int CardPunchId = Animator.StringToHash("TarjetaGolpe");

        [SerializeField] Animator animator;

        /// <summary>Variante de color de la cinta (sprites exportados de Photoshop).</summary>
        [System.Serializable]
        public struct ColorSkin
        {
            public string id;
            public Sprite label;
            public Sprite stripe;
        }

        [Header("Colores por evento")]
        [Tooltip("Imagen de color de la placa del título.")]
        [SerializeField] Image labelImage;
        [Tooltip("Imagen de color de la franja de la cinta.")]
        [SerializeField] Image stripeImage;
        [Tooltip("Pieles disponibles; el evento elige una por su id (p. ej. «Verde»).")]
        [SerializeField] ColorSkin[] skins;

        [Header("Cinta")]
        [Tooltip("Título del evento, en la placa.")]
        [SerializeField] TMP_Text titleText;
        [Tooltip("Texto que corre por la cinta (hijo de la ventana con máscara).")]
        [SerializeField] TMP_Text tickerText;
        [Tooltip("Vueltas que da el texto antes de que se corte el bucle.")]
        [SerializeField, Min(1)] int tickerLoops = 3;
        [SerializeField] string tickerSeparator = "     •     ";
        [Tooltip("Velocidad del texto (unidades del lienzo de 2560×1440 por segundo).")]
        [SerializeField, Min(10f)] float tickerSpeed = 300f;
        [Tooltip("Segundos desde «Mostrar» hasta que empieza a entrar el texto (cuando la banda y la placa ya están).")]
        [SerializeField, Min(0f)] float tickerDelay = 1.0f;

        [Header("Tarjeta de estado")]
        [SerializeField] RectTransform card;
        [SerializeField] TMP_Text cardTitleText;
        [SerializeField] TMP_Text cardCountText;

        static GameEventAnnouncer instance;
        static AudioClip generatedChime;

        GameEventTheme theme;
        Canvas canvas;
        RectTransform canvasRect;
        bool layered;

        // Cinta
        bool scrolling;
        float scrollStartAt, scrollX, scrollEnd;

        // Tarjeta
        object cardKey;
        bool cardVisible;
        float cardHideAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        // ---------------- API ----------------

        /// <summary>Anuncia un evento: «ding-dong», golpe de cámara y la cinta con el título y el subtítulo.</summary>
        public static void Announce(object key, string title, string subtitle, Color accent, string skin = null)
        {
            GameEventAnnouncer a = Ensure();
            if (a != null) a.DoAnnounce(title, subtitle, accent, skin);
        }

        /// <summary>
        /// Tarjeta de estado en el lateral de la pantalla. Se puede llamar cada frame; late cuando cambia
        /// <paramref name="count"/>.
        /// </summary>
        public static void SetCard(object key, string title, string count, Color accent)
        {
            GameEventAnnouncer a = Ensure();
            if (a != null) a.DoSetCard(key, title, count, accent);
        }

        /// <summary>Quita la tarjeta; con <paramref name="finalText"/>, la enseña un momento antes (p. ej. «¡AGOTADA!»).</summary>
        public static void ClearCard(object key, string finalText = null)
        {
            if (instance != null) instance.DoClearCard(key, finalText);
        }

        static GameEventAnnouncer Ensure()
        {
            if (instance != null) return instance;
            GameEventTheme theme = GameEventTheme.Current;
            if (theme.tickerPrefab == null)
            {
                Debug.LogWarning("GameEventAnnouncer: GameEventTheme no tiene asignado el prefab de la cinta (PF_UI_EventTicker).");
                return null;
            }
            instance = Instantiate(theme.tickerPrefab);
            instance.name = theme.tickerPrefab.name;
            return instance;
        }

        // ---------------- Cinta ----------------

        void Awake()
        {
            theme = GameEventTheme.Current;
            canvas = GetComponent<Canvas>();
            canvasRect = (RectTransform)transform;
            if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = Camera.main;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void DoAnnounce(string title, string subtitle, Color accent, string skin)
        {
            if (titleText != null) titleText.text = title;
            ApplySkin(skin);

            if (tickerText != null)
            {
                string piece = string.IsNullOrEmpty(subtitle) ? title : subtitle;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < tickerLoops; i++)
                {
                    if (i > 0) sb.Append(tickerSeparator);
                    sb.Append(piece);
                }
                tickerText.text = sb.ToString();
                // Empieza justo fuera por la derecha de la ventana y acaba cuando la última letra ha salido por la izquierda.
                var viewport = tickerText.rectTransform.parent as RectTransform;
                scrollX = (viewport != null ? viewport.rect.width : 2140f) + 20f;
                scrollEnd = -tickerText.GetPreferredValues(tickerText.text).x - 20f;
                SetTickerX(scrollX);
                scrolling = true;
                scrollStartAt = Time.unscaledTime + tickerDelay;
            }

            animator.ResetTrigger(LeaveId);
            animator.SetTrigger(ShowId);
            PlayChime();
            CameraFX.Play(theme.announceFx, 1f, accent);
        }

        void ApplySkin(string id)
        {
            if (skins == null || skins.Length == 0 || string.IsNullOrEmpty(id)) return;
            foreach (ColorSkin s in skins)
            {
                if (!string.Equals(s.id, id, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (labelImage != null && s.label != null) labelImage.sprite = s.label;
                if (stripeImage != null && s.stripe != null) stripeImage.sprite = s.stripe;
                return;
            }
            Debug.LogWarning($"GameEventAnnouncer: no hay piel de color «{id}» en el prefab de la cinta.", this);
        }

        void UpdateTicker()
        {
            if (!scrolling || Time.unscaledTime < scrollStartAt) return;
            scrollX -= tickerSpeed * Time.unscaledDeltaTime;
            SetTickerX(scrollX);
            if (scrollX <= scrollEnd)
            {
                scrolling = false;
                animator.SetTrigger(LeaveId);
            }
        }

        void SetTickerX(float x)
        {
            if (tickerText == null) return;
            Vector2 p = tickerText.rectTransform.anchoredPosition;
            p.x = x;
            tickerText.rectTransform.anchoredPosition = p;
        }

        // ---------------- Tarjeta ----------------

        void DoSetCard(object key, string title, string count, Color accent)
        {
            bool changed = cardKey == key && cardCountText != null && cardCountText.text != count;
            cardKey = key;
            cardHideAt = -1f;
            if (cardTitleText != null) cardTitleText.text = title;
            if (cardCountText != null) cardCountText.text = count;
            if (!cardVisible)
            {
                cardVisible = true;
                animator.SetBool(CardId, true);
            }
            else if (changed) animator.SetTrigger(CardPunchId);
        }

        void DoClearCard(object key, string finalText)
        {
            if (cardKey != key || !cardVisible) return;
            if (!string.IsNullOrEmpty(finalText))
            {
                if (cardCountText != null) cardCountText.text = finalText;
                animator.SetTrigger(CardPunchId);
                cardHideAt = Time.unscaledTime + 1.2f;
            }
            else HideCard();
        }

        void HideCard()
        {
            cardVisible = false;
            cardKey = null;
            cardHideAt = -1f;
            animator.SetBool(CardId, false);
        }

        void UpdateCard()
        {
            if (cardHideAt > 0f && Time.unscaledTime >= cardHideAt) HideCard();
        }

        // ---------------- Frame ----------------

        void LateUpdate()
        {
            if (canvas != null)
            {
                if (canvas.worldCamera == null || !canvas.worldCamera.isActiveAndEnabled)
                {
                    canvas.worldCamera = Camera.main;
                    layered = false;
                }
                if (!layered && canvas.worldCamera != null)
                {
                    // Delante del HUD, lo hayan montado con el orden que sea.
                    CanvasLayering.BringToFront(canvas);
                    layered = true;
                }
            }
            UpdateTicker();
            UpdateCard();
        }

        // ---------------- Sonido ----------------

        void PlayChime()
        {
            if (!theme.chime.IsEmpty)
            {
                theme.chime.Play2D();
                return;
            }
            if (generatedChime == null) generatedChime = BuildChime();
            var go = new GameObject("SFX Chime");
            var source = go.AddComponent<AudioSource>();
            source.clip = generatedChime;
            source.volume = theme.generatedChimeVolume;
            source.spatialBlend = 0f;
            source.Play();
            Destroy(go, generatedChime.length + 0.1f);
        }

        /// <summary>«Ding-dong» de megafonía de tienda: dos notas de campana (Mi5 → Do5) que se apagan solas.</summary>
        static AudioClip BuildChime()
        {
            const int rate = 44100;
            const float length = 1.7f;
            int count = Mathf.CeilToInt(rate * length);
            var data = new float[count];
            (float freq, float start)[] notes = { (659.25f, 0f), (523.25f, 0.42f) };
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)rate;
                float sample = 0f;
                foreach ((float freq, float start) in notes)
                {
                    float t = time - start;
                    if (t < 0f) continue;
                    float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 3.2f);
                    float w = 2f * Mathf.PI * freq * t;
                    sample += env * (Mathf.Sin(w) + 0.35f * Mathf.Sin(2f * w) * Mathf.Exp(-t * 4f) + 0.12f * Mathf.Sin(3f * w) * Mathf.Exp(-t * 7f));
                }
                data[i] = sample * 0.3f;
            }
            var clip = AudioClip.Create("Chime_DingDong", count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
