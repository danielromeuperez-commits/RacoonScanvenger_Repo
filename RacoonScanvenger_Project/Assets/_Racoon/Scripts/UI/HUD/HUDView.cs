using System;
using Racoon.Feedback;
using Racoon.UI.P5;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Referencias de todas las piezas animables del HUD (asignadas en el prefab de cada variante);
    /// <see cref="HUDController"/> las conecta con la partida y las anima.
    /// Medidas en px a escala 2560×1440.
    /// </summary>
    public class HUDView : MonoBehaviour
    {
        [Serializable]
        public class PlayerView
        {
            public RectTransform root;

            [Header("Retrato")]
            [Tooltip("Filled Radial360, origen arriba, sin sentido horario: fillAmount = estamina (0-1).")]
            public Image staminaFill;
            [Tooltip("Copia del aro, detrás, que brilla durante la racha.")]
            public Image staminaGlow;
            [Tooltip("Copia blanca del aro, detrás: estela de la estamina que se acaba de gastar.")]
            public Image staminaTrail;
            [Tooltip("Ilustración del personaje (recortada por la máscara del fondo). Vacía hasta tener los dibujos.")]
            public Image illustration;
            [Tooltip("Retrato en vivo (RenderTexture de la cámara grúa de HUDLivePortraits), encima de la ilustración.")]
            public RawImage liveIllustration;

            [Header("Racha y liderato (desactivados por defecto)")]
            public GameObject fire;
            [Tooltip("Fotogramas de la llama: uno cada 0,12 s.")]
            public GameObject[] fireFrames = new GameObject[3];
            public GameObject streak;
            [Tooltip("Contador «xN».")]
            public P5Text streakCounter;
            [Tooltip("Insignia «TOP» o, si hay corona, la corona.")]
            public GameObject top;
            [Tooltip("Opcional: corona del que va ganando (en lugar de la insignia). Cae sobre el aro del retrato, aterriza y se queda colgando; pivote en su base.")]
            public RectTransform crown;

            [Header("Puntos (rodillo)")]
            public RectTransform scorePanel;
            public P5Text scoreHeader;
            [Tooltip("Ficha de cada rodillo (con su tira de dígitos dentro): se balancea y salta al puntuar.")]
            public RectTransform[] reels = new RectTransform[4];
            [Tooltip("Tiras de dígitos 0-9,0-9,0,1 dentro de su máscara: para ver el dígito d, anchoredPosition.y = reelOffset + d * reelPitch.")]
            public RectTransform[] reelStrips = new RectTransform[4];
            public float reelPitch = 100f;
            public float reelOffset = 50f;
            [Tooltip("Puntos que lleva en la bolsa sin vender (desactivado sin bolsa).")]
            public P5Text bag;

            [Header("Ítems")]
            [Tooltip("Caja principal con su «?» y su icono: flota y hace la entrada.")]
            public RectTransform mainSlot;
            public RectTransform mainItemBox;
            [Tooltip("Icono del ítem principal (desactivado sin ítem).")]
            public Image mainItemIcon;
            [Tooltip("«?» que se ve sin ítem.")]
            public GameObject mainItemEmpty;
            [Tooltip("Caja de reserva con su «?» y su icono.")]
            public RectTransform reserveSlot;
            public RectTransform reserveItemBox;
            public Image reserveItemIcon;
            public GameObject reserveItemEmpty;

            [Header("Reacciones (solo con «Reactions»)")]
            [Tooltip("Rayas de velocidad detrás del retrato: al correr y en la voltereta.")]
            public RectTransform[] speedLines = new RectTransform[0];
            [Tooltip("Estrellas que giran sobre el retrato mientras está aturdido en el suelo.")]
            public RectTransform[] dizzyStars = new RectTransform[0];

            [Header("Etiqueta")]
            [Tooltip("Chapa «Pn» y, si la variante lo muestra, el apodo: tag.SetText(apodo).")]
            public P5Text tag;

            [Header("«+N» al puntuar (plantilla oculta para clonar)")]
            public RectTransform popTemplate;
            public TMP_Text popText;
            public TMP_Text popShadow;
        }

        [Serializable]
        public class TimerView
        {
            [Tooltip("Texto blanco del tiempo. Las dos sombras deben llevar siempre el mismo texto: usa SetTimeText.")]
            public TMP_Text time;
            public TMP_Text shadowP1;
            public TMP_Text shadowP2;
            [Tooltip("Grupo del texto y sus sombras: late en los últimos segundos.")]
            public RectTransform digits;
            [Tooltip("Filled Horizontal: fillAmount = tiempo restante / duración.")]
            public Image barFill;
            [Tooltip("Losa roja de los últimos 30 s.")]
            public GameObject urgentSlab;
            public P5Text label;
            [Tooltip("Color del texto del tiempo.")]
            public Color textColor = Color.white;
            [Tooltip("Color del texto en los últimos segundos (con la losa roja). Transparente = no cambia.")]
            public Color urgentTextColor = Color.clear;
        }

        /// <summary>Animación propia del panel de puntos de cada variante, además del salto de los rodillos.</summary>
        public enum PanelFx
        {
            [Tooltip("Solo saltan los rodillos.")] None,
            [Tooltip("Cartel colgante: se balancea y da un vaivén al puntuar.")] Swing,
            [Tooltip("Tique: da un tirón, como al imprimir, al puntuar.")] Print,
            [Tooltip("Pegatina: se aplasta y rebota al puntuar.")] Squash,
        }

        public HUDPlayerColors colors;
        [Tooltip("Variante sin inclinaciones (Soft limpio): la caja principal flota sin girar.")]
        public bool calm;
        public PanelFx scorePanelFx;
        [Tooltip("El retrato reacciona a lo que le pasa al jugador: golpes, plátanos, voltereta, puñetazos, correr y mareo.")]
        public bool reactions;
        public PlayerView player1 = new();
        public PlayerView player2 = new();
        public TimerView timer = new();

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX en cada cambio de icono de la ruleta de ítems: «tic» seco de ruleta de feria. " +
                 "Suena cada vez más espaciado (la ruleta frena) y un poco más grave. Muy corto (< 0,1 s).")]
        public SfxCue rouletteTickSfx = new();
        [Tooltip("SFX cuando la ruleta se para en el ítem que te toca: «ding» / campanita alegre.")]
        public SfxCue rouletteStopSfx = new();

        public PlayerView Player(int player) => player == 1 ? player1 : player2;

        /// <summary>Escribe el tiempo en el texto blanco y en sus dos sombras.</summary>
        public void SetTimeText(string value)
        {
            if (timer.time) timer.time.text = value;
            if (timer.shadowP1) timer.shadowP1.text = value;
            if (timer.shadowP2) timer.shadowP2.text = value;
        }
    }
}
