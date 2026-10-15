using System;
using Racoon.UI.P5;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Referencias de todas las piezas animables del HUD. La rellena HUDVariantBuilder al montar cada variante;
    /// los scripts de juego (puntos, tiempo, estamina, ítems, racha) deberían trabajar solo a través de aquí.
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
            [Tooltip("Ilustración del personaje (recortada por la máscara del fondo). Vacía hasta tener los dibujos.")]
            public Image illustration;

            [Header("Racha y liderato (desactivados por defecto)")]
            public GameObject fire;
            [Tooltip("Fotogramas de la llama: alternar uno cada 0,12 s.")]
            public GameObject[] fireFrames = new GameObject[3];
            public GameObject streak;
            [Tooltip("Contador «xN»: streakCounter.SetText(\"x\" + n).")]
            public P5Text streakCounter;
            public GameObject top;

            [Header("Puntos (rodillo)")]
            public RectTransform scorePanel;
            public P5Text scoreHeader;
            [Tooltip("Tiras de dígitos 0-9,0-9 dentro de su máscara: para ver el dígito d, anchoredPosition.y = reelOffset + d * reelPitch.")]
            public RectTransform[] reelStrips = new RectTransform[4];
            public float reelPitch = 100f;
            public float reelOffset = 50f;

            [Header("Ítems")]
            public RectTransform mainItemBox;
            [Tooltip("Icono del ítem principal (desactivado sin ítem). Encaja con ItemUIController.mainItemImage.")]
            public Image mainItemIcon;
            [Tooltip("«?» que se ve sin ítem.")]
            public GameObject mainItemEmpty;
            public RectTransform reserveItemBox;
            [Tooltip("Icono del ítem de reserva. Encaja con ItemUIController.reserveItemImage.")]
            public Image reserveItemIcon;
            public GameObject reserveItemEmpty;

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
            [Tooltip("Filled Horizontal: fillAmount = tiempo restante / duración.")]
            public Image barFill;
            [Tooltip("Losa roja de los últimos 30 s.")]
            public GameObject urgentSlab;
            public P5Text label;
        }

        public HUDPlayerColors colors;
        public PlayerView player1 = new();
        public PlayerView player2 = new();
        public TimerView timer = new();

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
