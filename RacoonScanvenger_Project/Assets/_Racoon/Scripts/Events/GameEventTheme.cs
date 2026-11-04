using Racoon.Cameras;
using Racoon.Feedback;
using TMPro;
using UnityEngine;

namespace Racoon.Events
{
    /// <summary>
    /// Aspecto común de los eventos: el prefab de la cinta de anuncios (<see cref="GameEventAnnouncer"/>), su sonido y
    /// su golpe de cámara, y lo que usan los visuales en el mundo (p. ej. <see cref="DoublePointsVFX"/>).
    /// Hay uno solo, en Visuals/VFX/Events/Resources/Events/GameEventTheme.asset: se carga con Resources.Load, así que
    /// no hay que arrastrarlo a ningún prefab ni escena. Todo tiene valores por defecto: si falta algo, se ve igual
    /// (sin prefab no hay cinta, solo sonido y cámara).
    /// </summary>
    [CreateAssetMenu(fileName = "GameEventTheme", menuName = "Racoon/Events/Game Event Theme")]
    public class GameEventTheme : ScriptableObject
    {
        public const string ResourcePath = "Events/GameEventTheme";

        [Header("Texto")]
        [Tooltip("Fuente del HUD (Fredoka SDF).")]
        public TMP_FontAsset font;
        [Tooltip("Material de la fuente con contorno de tinta (Fredoka SDF Contorno Pastel) para los títulos.")]
        public Material outlinedFontMaterial;

        [Header("Cinta de anuncios")]
        [Tooltip("Prefab de la cinta, la ficha y la flecha (PF_UI_EventTicker). Se instancia solo la primera vez.")]
        public GameEventAnnouncer tickerPrefab;
        [Tooltip("Flecha de suelo a los pies del jugador que apunta al objetivo del evento (PF_VFX_EventFloorArrow).")]
        public GameObject floorArrowPrefab;

        [Header("Colores")]
        [Tooltip("Tinta del HUD pastel (#4B4366): contornos y texto oscuro.")]
        public Color ink = new(0.294f, 0.263f, 0.4f);
        [Tooltip("Crema del HUD (#FFF8EC): texto claro y franjas.")]
        public Color cream = new(1f, 0.973f, 0.925f);

        [Header("Mundo")]
        [Tooltip("M_VFX_ToonFlat: carteles, marcos y destellos de los eventos.")]
        public Material flatMaterial;

        [Header("Anuncio")]
        [Tooltip("Sonido «ding-dong» de megafonía. Vacío = se genera uno por código.")]
        public SfxCue chime = new();
        [Range(0f, 1f)] public float generatedChimeVolume = 0.55f;
        [Tooltip("Golpe de cámara al anunciar. Los colores los pone el del evento.")]
        public CameraFXProfile announceFx = new(0.75f, attack: 0.05f, decayPower: 2f)
        {
            priority = 45, duck = 0.3f,
            fov = -0.02f, flash = 0.12f, edge = 0.45f,
        };

        static GameEventTheme cached;
        static bool loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cached = null;
            loaded = false;
        }

        /// <summary>El tema del proyecto, o uno con los valores por defecto si no está el asset.</summary>
        public static GameEventTheme Current
        {
            get
            {
                if (cached != null) return cached;
                if (!loaded)
                {
                    loaded = true;
                    cached = Resources.Load<GameEventTheme>(ResourcePath);
                }
                if (cached == null)
                {
                    cached = CreateInstance<GameEventTheme>();
                    cached.hideFlags = HideFlags.DontSave;
                }
                return cached;
            }
        }
    }
}
