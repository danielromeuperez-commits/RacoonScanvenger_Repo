using System.Collections.Generic;
using Racoon.Cameras;
using Racoon.Gameplay;
using UnityEngine;
using UnityEngine.Events;

namespace Racoon.Player
{
    /// <summary>
    /// Efectos de cámara del jugador local (FOV, post-procesado, capas en pantalla y hitstop; los temblores
    /// están en <see cref="PlayerCameraShake"/>). SOLO en la máquina del jugador al que le pasa.
    ///  - Recibir golpe ligero / fuerte: hitstop, punch-in, viñeta del color del rival; el derribo, además,
    ///    un fotograma en blanco y negro, alabeo y distorsión, y mientras estás en el suelo, desaturado.
    ///  - Dar un golpe que conecta: hitstop corto y punch-in mínimo.
    ///  - Correr y dash: FOV abierto y líneas de velocidad. Agotado: desaturado y viñeta que late.
    ///  - Plátano (aturdido por un objeto): alabeo con rebote, congelado y mareo mientras estás en el suelo.
    ///  - Extintor y bebida energética: velocidad y color mientras duran (la bebida, con bajón al acabar).
    /// No hace falta ponerlo en el prefab: se añade solo al jugador local al aparecer (y crea el
    /// <see cref="MatchCameraFX"/> de la partida). Si se pone en el prefab, se usan sus ajustes del Inspector.
    /// </summary>
    public class PlayerCameraFX : MonoBehaviour
    {
        [SerializeField] PlayerController controller;
        [Tooltip("Multiplica todos los efectos de este jugador.")]
        [SerializeField, Range(0f, 2f)] float intensity = 1f;

        // Jerarquía (priority, más alto gana): golpes 80-90 > suelo/mareo 70 > dar golpes 60-65 > extintor 45 >
        // bebida 40 > (partida: racha 30, poco tiempo 50) > agotado 20 > dash 15 > correr 10.
        [Header("Recibir golpes")]
        [SerializeField] CameraFXProfile lightHit = new(0.25f, decayPower: 2.2f)
        {
            priority = 80, duck = 0.5f,
            fov = -0.03f, vignette = 0.28f, chromatic = 0.35f, hitstop = 0.05f, hitstopScale = 0.05f,
        };
        [Tooltip("Cuánto más fuerte es cada golpe ligero seguido del combo (2º = 1 + esto).")]
        [SerializeField] float comboStep = 0.4f;
        [SerializeField] CameraFXProfile heavyHit = new(0.6f, decayPower: 1.6f)
        {
            priority = 90, duck = 0.8f,
            fov = -0.08f, dutch = 6f, dutchWobble = 1.5f, vignette = 0.4f, chromatic = 0.8f, distortion = -0.35f,
            flash = 0.45f, hitstop = 0.12f, hitstopScale = 0.02f,
        };
        [Tooltip("Fotograma de cómic al recibir el derribo: blanco y negro con contraste.")]
        [SerializeField] CameraFXProfile heavyImpactFrame = new(0.12f, decayPower: 6f)
        {
            priority = 90, duck = 1f,
            saturation = -100f, contrast = 45f,
        };
        [Tooltip("Continuo mientras estás derribado.")]
        [SerializeField] CameraFXProfile grounded = new(1f)
        {
            priority = 70, duck = 0.5f, fadeIn = 0.15f, fadeOut = 0.4f,
            saturation = -35f, vignette = 0.3f, fov = -0.02f,
        };
        [Tooltip("Al levantarse (del derribo o del plátano).")]
        [SerializeField] CameraFXProfile getUp = new(0.25f, decayPower: 2f)
        {
            priority = 70, duck = 0.3f,
            fov = 0.03f, chromatic = 0.15f,
        };

        [Header("Dar golpes")]
        [SerializeField] CameraFXProfile hitConfirm = new(0.15f, decayPower: 3f)
        {
            priority = 60, duck = 0.3f,
            fov = -0.015f, hitstop = 0.04f, hitstopScale = 0.1f,
        };
        [Tooltip("El golpe que derriba.")]
        [SerializeField] CameraFXProfile heavyConfirm = new(0.3f, decayPower: 2.5f)
        {
            priority = 65, duck = 0.5f,
            fov = -0.04f, chromatic = 0.25f, flash = 0.15f, hitstop = 0.09f, hitstopScale = 0.03f,
        };

        [Header("Movimiento")]
        [Tooltip("Continuo mientras corre.")]
        [SerializeField] CameraFXProfile run = new(1f)
        {
            priority = 10, duck = 0f, fadeIn = 0.25f, fadeOut = 0.35f,
            fov = 0.05f, speedLines = 0.3f, vignette = 0.08f,
        };
        [Tooltip("Continuo mientras está agotado.")]
        [SerializeField] CameraFXProfile tired = new(1f)
        {
            priority = 20, duck = 0.2f, fadeIn = 0.6f, fadeOut = 0.8f,
            saturation = -30f, vignette = 0.3f, pulseRate = 0.6f, pulseDepth = 0.5f,
        };
        [SerializeField] CameraFXProfile dash = new(0.3f, decayPower: 2f)
        {
            priority = 15, duck = 0.3f,
            fov = 0.07f, speedLines = 1f, chromatic = 0.2f,
        };

        [Header("Plátano (aturdido por un objeto)")]
        [SerializeField] CameraFXProfile slip = new(0.9f, attack: 0.08f, decayPower: 1.4f)
        {
            priority = 80, duck = 0.7f,
            dutch = 12f, dutchWobble = 1.8f, fov = -0.04f, flash = 0.2f, flashColor = new Color(1f, 0.92f, 0.35f),
            chromatic = 0.3f, hitstop = 0.08f, hitstopScale = 0.05f,
        };
        [Tooltip("Continuo mientras está en el suelo por el objeto: mareo.")]
        [SerializeField] CameraFXProfile dizzy = new(1f)
        {
            priority = 70, duck = 0.5f, fadeIn = 0.25f, fadeOut = 0.5f,
            dutch = 2.5f, dutchWobble = 0.35f, distortion = 0.15f, chromatic = 0.25f, saturation = -20f,
            vignette = 0.3f, pulseRate = 0.7f, pulseDepth = 0.6f,
        };

        [Header("Extintor")]
        [Tooltip("Continuo mientras le propulsa el extintor.")]
        [SerializeField] CameraFXProfile extintor = new(1f)
        {
            priority = 45, duck = 0.2f, fadeIn = 0.2f, fadeOut = 0.4f,
            fov = 0.08f, speedLines = 0.6f, edge = 0.4f, edgeColor = new Color(0.9f, 0.95f, 1f), flicker = 0.3f,
        };

        // Colores del HUD: amarillo de acento #FFD36E y crema #FFF8EC (contorno: tinta, en CameraFX.InkColor).
        // Los rayos, en gris casi transparente (el alfa de risingColor), para no competir con el viñeteado.
        [Header("Bebida energética")]
        [Tooltip("Al beber: golpe de FOV, destello, el viñeteado entra de golpe y una ráfaga de rayos.")]
        [SerializeField] CameraFXProfile energyStart = new(0.7f, decayPower: 2.5f)
        {
            priority = 40, duck = 0.5f,
            fov = 0.3f, chromatic = 0.8f, flash = 0.3f, flashColor = new Color(1f, 0.973f, 0.925f), distortion = 0.25f,
            aura = 1f, auraColor = new Color(1f, 0.827f, 0.431f),
            risingBolts = 1f, risingColor = new Color(0.6f, 0.58f, 0.66f, 0.3f),
        };
        [Tooltip("Continuo mientras dura: FOV abierto que respira, colores vivos, viñeteado por capas con ondas y " +
                 "rayos grises translúcidos que suben.")]
        [SerializeField] CameraFXProfile energized = new(1f)
        {
            priority = 40, duck = 0.3f, fadeIn = 0.35f, fadeOut = 0.8f,
            fov = 0.2f, saturation = 40f, hueShift = 12f, pulseRate = 1.6f, pulseDepth = 0.35f,
            aura = 0.8f, auraColor = new Color(1f, 0.827f, 0.431f),
            risingBolts = 0.55f, risingColor = new Color(0.6f, 0.58f, 0.66f, 0.22f),
        };
        [Tooltip("El bajón al acabar.")]
        [SerializeField] CameraFXProfile energyCrash = new(2f, attack: 0.1f, decayPower: 1.5f)
        {
            priority = 40, duck = 0.2f,
            saturation = -45f, vignette = 0.35f, fov = -0.02f,
        };

        readonly object runKey = new();
        readonly object tiredKey = new();
        readonly object groundedKey = new();
        readonly object dizzyKey = new();
        readonly object extintorKey = new();
        readonly object energyKey = new();
        object[] keys;
        object[] Keys => keys ??= new[] { runKey, tiredKey, groundedKey, dizzyKey, extintorKey, energyKey };

        // Golpes entre cualquier jugador (para el hit confirm hay que escuchar también al rival).
        readonly Dictionary<PlayerController, (UnityAction<PlayerController, int> light, UnityAction<PlayerController> heavy)>
            hitListeners = new();
        float nextScan;
        bool wasEnergized;

        bool IsLocal => controller != null && controller.IsSpawned && controller.IsOwner;

        // ---------------- Arranque automático ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            PlayerController.LocalPlayerSpawned -= Attach;
            PlayerController.LocalPlayerSpawned += Attach;
            if (PlayerController.Local != null) Attach(PlayerController.Local);
        }

        static void Attach(PlayerController player)
        {
            if (player == null) return;
            if (player.GetComponentInChildren<PlayerCameraFX>(true) == null) player.gameObject.AddComponent<PlayerCameraFX>();
            MatchCameraFX.Ensure();
        }

        // ---------------- Ciclo de vida ----------------

        void Reset() => controller = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.onDash.AddListener(OnDash);
            controller.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.onDash.RemoveListener(OnDash);
                controller.StateChanged -= OnStateChanged;
            }
            foreach (var pair in hitListeners)
            {
                if (pair.Key == null) continue;
                pair.Key.onLightHit.RemoveListener(pair.Value.light);
                pair.Key.onHeavyHit.RemoveListener(pair.Value.heavy);
            }
            hitListeners.Clear();
            foreach (object key in Keys)
                CameraFX.ClearContinuous(key);
            if (CameraFX.Focus == transform) CameraFX.Focus = null;
        }

        void Update()
        {
            if (!IsLocal)
            {
                foreach (object key in Keys)
                    CameraFX.SetContinuous(key, null, 0f);
                return;
            }

            CameraFX.Focus = controller.transform;
            ScanPlayers();

            PlayerState state = controller.State;
            Continuous(runKey, run, state == PlayerState.Run, 4f);
            Continuous(tiredKey, tired, controller.IsExhausted, 1.5f);
            Continuous(groundedKey, grounded, state == PlayerState.Knockdown, 3f);
            Continuous(dizzyKey, dizzy, state == PlayerState.Downed, 2f);
            Continuous(extintorKey, extintor, controller.IsExtintorActive, 4f);

            bool energizedNow = controller.IsEnergized;
            if (energizedNow && !wasEnergized) Play(energyStart);
            if (!energizedNow && wasEnergized) Play(energyCrash);
            wasEnergized = energizedNow;
            // El último segundo se va apagando.
            float energy = energizedNow ? Mathf.Clamp01(controller.EnergyRemaining) : 0f;
            CameraFX.SetContinuous(energyKey, energized, energy * intensity, 3f);
        }

        void Continuous(object key, CameraFXProfile profile, bool on, float fadeSpeed) =>
            CameraFX.SetContinuous(key, profile, on ? intensity : 0f, fadeSpeed);

        void Play(CameraFXProfile profile, float strength = 1f, Color color = default) =>
            CameraFX.Play(profile, strength * intensity, color);

        // ---------------- Golpes ----------------

        void ScanPlayers()
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            foreach (PlayerController player in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (hitListeners.ContainsKey(player)) continue;
                PlayerController victim = player;
                UnityAction<PlayerController, int> light = (attacker, hitNumber) => OnLightHit(victim, attacker, hitNumber);
                UnityAction<PlayerController> heavy = attacker => OnHeavyHit(victim, attacker);
                victim.onLightHit.AddListener(light);
                victim.onHeavyHit.AddListener(heavy);
                hitListeners[victim] = (light, heavy);
            }
        }

        void OnLightHit(PlayerController victim, PlayerController attacker, int hitNumber)
        {
            if (!IsLocal) return;
            if (victim == controller)
                Play(lightHit, 1f + comboStep * Mathf.Max(0, hitNumber - 1), MatchCameraFX.PlayerColor(attacker));
            else if (attacker == controller)
                Play(hitConfirm, 1f + 0.25f * Mathf.Max(0, hitNumber - 1));
        }

        void OnHeavyHit(PlayerController victim, PlayerController attacker)
        {
            if (!IsLocal) return;
            if (victim == controller)
            {
                Play(heavyHit, 1f, MatchCameraFX.PlayerColor(attacker));
                Play(heavyImpactFrame);
            }
            else if (attacker == controller) Play(heavyConfirm);
        }

        // ---------------- Movimiento y estados ----------------

        void OnDash(Vector3 direction)
        {
            if (IsLocal) Play(dash);
        }

        void OnStateChanged(PlayerState previous, PlayerState current)
        {
            if (!IsLocal) return;
            if (current == PlayerState.Downed && previous != PlayerState.Downed) Play(slip);
            if (previous.IsGrounded() && !current.IsGrounded()) Play(getUp);
        }
    }
}
