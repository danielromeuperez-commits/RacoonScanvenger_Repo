using Racoon.Cameras;
using Racoon.Player;
using Racoon.UI.HUD;
using UnityEngine;

namespace Racoon.Gameplay
{
    /// <summary>
    /// Efectos de cámara de la partida, para el jugador local (ver <see cref="CameraFX"/>):
    ///  - Llamas (<see cref="MatchStandings"/>): llamas del color del personaje en la parte baja de la pantalla, que
    ///    suben al encenderse, crecen con la racha y bajan a los <see cref="fireCameraSeconds"/> (la llama del HUD sigue
    ///    mientras dure la racha) o al apagarse (con un «soplido» desaturado).
    ///  - Vender: pulso dorado (más fuerte cuanto más vendes). Perder la bolsa: aberración y desaturado.
    ///  - Cambio de líder: destello del color de quien pasa a ir ganando.
    ///  - Poco tiempo (<see cref="MatchTimer"/>): viñeta roja que late cada vez más rápido, un pulso en cada
    ///    segundo de los últimos 10 (más gordo en los 3 últimos) y cámara lenta al acabar.
    ///  - Tiempo recortado (evento de acelerar el reloj): glitch de «avance rápido».
    /// Es el manager de los efectos de cámara de la partida: ponlo en la escena (Racoon → Efectos → Añadir manager de
    /// efectos de cámara) para ajustarlos y apagarlos («enabled» de cada perfil) desde la jerarquía. Si no hay ninguno,
    /// lo crea <see cref="PlayerCameraFX"/> al aparecer el jugador local, con los valores por defecto.
    /// </summary>
    public class MatchCameraFX : MonoBehaviour
    {
        [SerializeField] MatchStandings standings;
        [SerializeField] MatchTimer matchTimer;
        [SerializeField, Range(0f, 2f)] float intensity = 1f;

        [Header("Llamas")]
        [Tooltip("Al encenderse: las llamas brotan en las esquinas con una ráfaga de virutas. Color del personaje.")]
        [SerializeField] CameraFXProfile ignite = new(0.9f, attack: 0.1f, decayPower: 2f)
        {
            priority = 30, duck = 0.3f,
            fov = -0.03f, flash = 0.1f, fire = 0.9f, embers = 1f,
        };
        [Tooltip("Continuo mientras estás en llamas: llamas y virutas en las esquinas inferiores; con más racha se " +
                 "encienden también el centro inferior y los laterales. Color del personaje.")]
        [SerializeField] CameraFXProfile onFire = new(1f)
        {
            priority = 30, duck = 0.2f, fadeIn = 0.6f, fadeOut = 0.9f,
            fire = 0.55f, embers = 0.6f, saturation = 8f, pulseRate = 0.8f, pulseDepth = 0.1f,
        };
        [Tooltip("Cuánto sube la llama por cada venta de la racha por encima de la primera.")]
        [SerializeField] float fireStreakStep = 0.2f;
        [Tooltip("Segundos que se ven las llamas de cámara desde que te enciendes (0 = toda la racha). La llama del HUD " +
                 "sigue mientras dure la racha.")]
        [SerializeField, Min(0f)] float fireCameraSeconds = 6f;
        [Tooltip("Cada venta que alarga la racha vuelve a encender las llamas de cámara otros «Fire Camera Seconds».")]
        [SerializeField] bool fireRestartsOnSale = true;
        [SerializeField] CameraFXProfile extinguish = new(0.6f, decayPower: 1.5f)
        {
            priority = 30, duck = 0.2f,
            saturation = -40f, fov = 0.015f, embers = 0.5f,
        };

        [Header("Puntos")]
        [SerializeField] CameraFXProfile sell = new(0.45f, decayPower: 2f)
        {
            priority = 35, duck = 0.5f,
            fov = -0.025f, flash = 0.2f, flashColor = new Color(1f, 0.85f, 0.3f), edge = 0.5f,
            edgeColor = new Color(1f, 0.8f, 0.2f), exposure = 0.25f,
        };
        [SerializeField] CameraFXProfile bagLost = new(0.5f, decayPower: 2f)
        {
            priority = 35, duck = 0.5f,
            chromatic = 0.6f, saturation = -40f, vignette = 0.25f,
        };
        [Tooltip("Destello del color del nuevo líder (sale más fuerte si eres tú).")]
        [SerializeField] CameraFXProfile leaderChanged = new(0.5f, decayPower: 2f)
        {
            priority = 35, duck = 0.3f,
            edge = 0.6f, flash = 0.12f,
        };

        [Header("Poco tiempo")]
        [Tooltip("Segundos restantes a partir de los que empieza la viñeta roja.")]
        [SerializeField] float lowTimeStart = 30f;
        [SerializeField] CameraFXProfile lowTime = new(1f)
        {
            priority = 50, duck = 0.3f, fadeIn = 0.8f, fadeOut = 0.6f,
            vignette = 0.35f, vignetteColor = new Color(0.55f, 0f, 0.02f), contrast = 12f,
            pulseRate = 1f, pulseDepth = 0.7f,
        };
        [Tooltip("Latidos por segundo al llegar a 0 (empieza en el del perfil).")]
        [SerializeField] float lowTimeEndRate = 3f;
        [Tooltip("Segundos restantes a partir de los que cada segundo da un pulso.")]
        [SerializeField] int tickFrom = 10;
        [SerializeField] CameraFXProfile tick = new(0.25f, decayPower: 2.5f)
        {
            priority = 50, duck = 0.2f,
            fov = -0.02f, chromatic = 0.15f,
        };
        [Tooltip("Segundos restantes a partir de los que el pulso es el gordo.")]
        [SerializeField] int bigTickFrom = 3;
        [SerializeField] CameraFXProfile bigTick = new(0.4f, decayPower: 2f)
        {
            priority = 55, duck = 0.4f,
            fov = -0.05f, chromatic = 0.35f, flash = 0.1f, flashColor = new Color(1f, 0.2f, 0.15f),
        };
        [SerializeField] CameraFXProfile matchEnd = new(2.5f, attack: 0.05f, decayPower: 1.2f)
        {
            priority = 100, duck = 1f,
            fov = -0.06f, saturation = -70f, flash = 0.5f, vignette = 0.3f,
        };
        [Tooltip("Cámara lenta al acabar: segundos reales y escala de tiempo.")]
        [SerializeField] float endSlowMotion = 0.8f;
        [SerializeField, Range(0f, 1f)] float endSlowMotionScale = 0.25f;

        [Header("Tiempo recortado")]
        [Tooltip("Si el reloj baja de golpe más de esto (s), es que se ha recortado tiempo.")]
        [SerializeField] float timeJumpThreshold = 2f;
        [SerializeField] CameraFXProfile timeCut = new(0.6f, decayPower: 2f)
        {
            priority = 60, duck = 0.6f,
            chromatic = 1f, distortion = 0.3f, speedLines = 0.8f, hueShift = 25f, flash = 0.25f,
            flashColor = new Color(0.7f, 0.9f, 1f),
        };

        static MatchCameraFX instance;
        static HUDPlayerColors colors;

        MatchStandings boundStandings;
        MatchTimer boundTimer;
        readonly object fireKey = new();
        readonly object lowTimeKey = new();
        float nextSearch;
        float lastRemaining = -1f;
        int lastSecond = int.MaxValue;
        bool localOnFire;
        float fireStart = -99f;
        int fireStreak;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            colors = null;
        }

        /// <summary>Crea el de la partida si no hay ninguno.</summary>
        public static void Ensure()
        {
            if (instance != null) return;
            instance = FindFirstObjectByType<MatchCameraFX>();
            if (instance == null) instance = new GameObject("MatchCameraFX").AddComponent<MatchCameraFX>();
        }

        /// <summary>Color del jugador en el HUD (P1/P2). Transparente si no se sabe.</summary>
        public static Color PlayerColor(PlayerController player)
        {
            if (player == null) return default;
            MatchStandings table = instance != null ? instance.standings : null;
            if (table == null) table = FindFirstObjectByType<MatchStandings>();
            if (table == null) return default;
            MatchStandings.Standing standing = table.Players.Count > 0 ? FindStanding(table, player) : null;
            if (standing == null) return default;
            if (colors == null) colors = FindFirstObjectByType<HUDPlayerColors>();
            return colors != null ? colors.Get(standing.Index + 1) : default;
        }

        static MatchStandings.Standing FindStanding(MatchStandings table, PlayerController player)
        {
            foreach (MatchStandings.Standing standing in table.Players)
                if (standing.Controller == player) return standing;
            return null;
        }

        static bool IsLocal(MatchStandings.Standing standing) =>
            standing != null && standing.Controller != null && standing.Controller == PlayerController.Local;

        // ---------------- Ciclo de vida ----------------

        void Awake()
        {
            if (instance == null) instance = this;
            else if (instance != this)
            {
                Destroy(this);
                return;
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Unbind();
            CameraFX.ClearContinuous(fireKey);
            CameraFX.ClearContinuous(lowTimeKey);
        }

        void Update()
        {
            FindSources();
            UpdateFire();
            UpdateTime();
        }

        // HUDController crea el MatchStandings en su Start si no hay ninguno, así que se busca hasta que aparezca.
        void FindSources()
        {
            if (standings != boundStandings || matchTimer != boundTimer) Rebind();
            if ((standings != null && matchTimer != null) || Time.unscaledTime < nextSearch) return;
            nextSearch = Time.unscaledTime + 1f;
            if (matchTimer == null) matchTimer = FindFirstObjectByType<MatchTimer>();
            if (standings == null) standings = FindFirstObjectByType<MatchStandings>();
            Rebind();
        }

        void Rebind()
        {
            Unbind();
            boundStandings = standings;
            boundTimer = matchTimer;
            if (boundStandings != null)
            {
                boundStandings.StreakChanged += OnStreakChanged;
                boundStandings.Scored += OnScored;
                boundStandings.BagLost += OnBagLost;
                boundStandings.LeaderChanged += OnLeaderChanged;
            }
            if (boundTimer != null) boundTimer.MatchEnded += OnMatchEnded;
        }

        void Unbind()
        {
            if (boundStandings != null)
            {
                boundStandings.StreakChanged -= OnStreakChanged;
                boundStandings.Scored -= OnScored;
                boundStandings.BagLost -= OnBagLost;
                boundStandings.LeaderChanged -= OnLeaderChanged;
            }
            if (boundTimer != null) boundTimer.MatchEnded -= OnMatchEnded;
            boundStandings = null;
            boundTimer = null;
        }

        void Play(CameraFXProfile profile, float strength = 1f, Color color = default) =>
            CameraFX.Play(profile, strength * intensity, color);

        // ---------------- Llamas y puntos ----------------

        void UpdateFire()
        {
            MatchStandings.Standing local = standings != null && PlayerController.Local != null
                ? FindStanding(standings, PlayerController.Local)
                : null;
            float fire = local != null && local.OnFire && CameraFireVisible
                ? 1f + fireStreakStep * Mathf.Max(0, local.Streak - 1)
                : 0f;
            Color fireColor = local != null ? PlayerColor(local.Controller) : default;
            CameraFX.SetContinuous(fireKey, onFire, Mathf.Min(fire, 1.5f) * intensity, 2f, 1f, fireColor);
        }

        bool CameraFireVisible => fireCameraSeconds <= 0f || Time.unscaledTime - fireStart < fireCameraSeconds;

        void OnStreakChanged(MatchStandings.Standing standing)
        {
            if (!IsLocal(standing)) return;
            if (standing.OnFire && !localOnFire)
            {
                Play(ignite, 1f, PlayerColor(standing.Controller));
                fireStart = Time.unscaledTime;
            }
            else if (standing.OnFire && fireRestartsOnSale && standing.Streak > fireStreak)
                fireStart = Time.unscaledTime;
            // El «soplido» solo si las llamas de cámara seguían a la vista
            else if (!standing.OnFire && localOnFire && CameraFireVisible) Play(extinguish);
            localOnFire = standing.OnFire;
            fireStreak = standing.Streak;
        }

        void OnScored(MatchStandings.Standing standing, int points)
        {
            if (!IsLocal(standing) || points <= 0) return;
            float big = standings.FirePoints > 0 ? (float)points / standings.FirePoints : 1f;
            Play(sell, Mathf.Clamp(0.6f + big * 0.6f, 0.6f, 1.6f));
        }

        void OnBagLost(MatchStandings.Standing standing, int points)
        {
            if (IsLocal(standing)) Play(bagLost);
        }

        void OnLeaderChanged()
        {
            MatchStandings.Standing leader = standings.Leader;
            if (leader == null) return;
            Play(leaderChanged, IsLocal(leader) ? 1f : 0.6f, PlayerColor(leader.Controller));
        }

        // ---------------- Tiempo ----------------

        void UpdateTime()
        {
            if (matchTimer == null || !matchTimer.IsSpawned || !matchTimer.IsRunning || matchTimer.IsFinished)
            {
                CameraFX.SetContinuous(lowTimeKey, lowTime, 0f, 2f);
                lastRemaining = -1f;
                lastSecond = int.MaxValue;
                return;
            }

            float remaining = matchTimer.TimeRemaining;
            if (lastRemaining >= 0f && lastRemaining - remaining > timeJumpThreshold) Play(timeCut);
            lastRemaining = remaining;

            float k = lowTimeStart > 0f ? 1f - Mathf.Clamp01(remaining / lowTimeStart) : 0f;
            float rate = lowTime.pulseRate > 0f ? Mathf.Lerp(1f, lowTimeEndRate / lowTime.pulseRate, k) : 1f;
            CameraFX.SetContinuous(lowTimeKey, lowTime, remaining <= lowTimeStart ? Mathf.Lerp(0.4f, 1f, k) * intensity : 0f,
                1f, rate);

            int second = Mathf.CeilToInt(remaining);
            if (second < lastSecond && second <= tickFrom && second > 0)
                Play(second <= bigTickFrom ? bigTick : tick);
            lastSecond = second;
        }

        void OnMatchEnded()
        {
            CameraFX.SetContinuous(lowTimeKey, lowTime, 0f, 2f);
            Play(matchEnd);
            CameraFX.Hitstop(endSlowMotion, endSlowMotionScale);
        }
    }
}
