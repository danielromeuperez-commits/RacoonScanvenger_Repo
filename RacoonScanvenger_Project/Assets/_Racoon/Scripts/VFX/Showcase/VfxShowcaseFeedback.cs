using Racoon.Cameras;
using Racoon.UI.HUD;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Showcase de VFX: lo que en partida hacen PlayerCameraFX (efectos de cámara) y HUDController (HUD) con datos
    /// de red, aquí lo hace a partir de los cues del <see cref="VfxShowcaseDirector"/>. P1 del HUD = actor A,
    /// P2 = actor B. Los perfiles de cámara son los mismos valores por defecto que PlayerCameraFX.
    /// </summary>
    public class VfxShowcaseFeedback : MonoBehaviour
    {
        [SerializeField] HUDController hud;
        [Tooltip("Multiplica todos los efectos de cámara.")]
        [SerializeField, Range(0f, 2f)] float intensity = 1f;
        [Tooltip("Duración del reloj del HUD (cuenta atrás durante el vídeo).")]
        [SerializeField] float matchDuration = 180f;

        [Header("Cámara (valores de PlayerCameraFX)")]
        [SerializeField] CameraFXProfile lightHit = new(0.25f, decayPower: 2.2f)
        {
            priority = 80, duck = 0.5f,
            fov = -0.03f, vignette = 0.28f, chromatic = 0.35f, hitstop = 0.05f, hitstopScale = 0.05f,
        };
        [SerializeField] CameraFXProfile heavyHit = new(0.6f, decayPower: 1.6f)
        {
            priority = 90, duck = 0.8f,
            fov = -0.08f, dutch = 6f, dutchWobble = 1.5f, vignette = 0.4f, chromatic = 0.8f, distortion = -0.35f,
            flash = 0.45f, hitstop = 0.12f, hitstopScale = 0.02f,
        };
        [SerializeField] CameraFXProfile heavyImpactFrame = new(0.12f, decayPower: 6f)
        {
            priority = 90, duck = 1f,
            saturation = -100f, contrast = 45f,
        };
        [SerializeField] CameraFXProfile grounded = new(1f)
        {
            priority = 70, duck = 0.5f, fadeIn = 0.15f, fadeOut = 0.4f,
            saturation = -35f, vignette = 0.3f, fov = -0.02f,
        };
        [SerializeField] CameraFXProfile getUp = new(0.25f, decayPower: 2f)
        {
            priority = 70, duck = 0.3f,
            fov = 0.03f, chromatic = 0.15f,
        };
        [SerializeField] CameraFXProfile hitConfirm = new(0.15f, decayPower: 3f)
        {
            priority = 60, duck = 0.3f,
            fov = -0.015f, hitstop = 0.04f, hitstopScale = 0.1f,
        };
        [SerializeField] CameraFXProfile run = new(1f)
        {
            priority = 10, duck = 0f, fadeIn = 0.25f, fadeOut = 0.35f,
            fov = 0.05f, speedLines = 0.3f, vignette = 0.08f,
        };
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

        [SerializeField] CameraFXProfile energyStart = new(0.7f, decayPower: 2.5f)
        {
            priority = 40, duck = 0.5f,
            aura = 1f, auraColor = new Color(0.25f, 0.75f, 0.7f),
            risingBolts = 1f, risingColor = new Color(0.6f, 0.58f, 0.66f, 0.3f),
        };
        [SerializeField] CameraFXProfile energized = new(1f)
        {
            priority = 40, duck = 0.3f, fadeIn = 0.45f, fadeOut = 0.6f,
            fov = 0.25f,
            aura = 0.8f, auraColor = new Color(0.25f, 0.75f, 0.7f),
            risingBolts = 0.55f, risingColor = new Color(0.6f, 0.58f, 0.66f, 0.22f),
        };

        readonly object energyKey = new();
        readonly object runKey = new(), tiredKey = new(), groundedKey = new();
        readonly System.Collections.Generic.HashSet<string> runners = new();
        float startTime;
        readonly int[] score = new int[2];

        void Start()
        {
            startTime = Time.time;
            if (hud == null) return;
            hud.DemoMode = true;
            for (int i = 1; i <= 2; i++)
            {
                hud.Player(i).SetStamina(1f);
                hud.Player(i).SetScore(0, 0);
            }
        }

        void Update()
        {
            if (hud == null) return;
            float remaining = Mathf.Max(0f, matchDuration - (Time.time - startTime));
            hud.ShowTime(remaining, matchDuration, true, false);
        }

        HUDPlayerPresenter Slot(string actor) =>
            hud == null ? null : actor == "A" ? hud.Player(1) : actor == "B" ? hud.Player(2) : null;

        void Play(CameraFXProfile profile, float strength = 1f, Color color = default) =>
            CameraFX.Play(profile, strength * intensity, color);

        // ---------------- Llamadas del director ----------------

        public void Hit(string victim, string attacker, bool heavy, Color attackerColor)
        {
            if (heavy)
            {
                Play(heavyHit, 1f, attackerColor);
                Play(heavyImpactFrame);
                CameraFX.SetContinuous(groundedKey, grounded, intensity, 6f);
            }
            else
            {
                Play(lightHit, 1f, attackerColor);
                Play(hitConfirm);
            }

            Slot(victim)?.React(heavy ? HUDReaction.HeavyHit : HUDReaction.Hit);
            Slot(victim)?.SetDizzy(heavy);
            HUDPlayerPresenter a = Slot(attacker);
            if (a != null)
            {
                int i = attacker == "A" ? 0 : 1;
                int gained = heavy ? 150 : 50;
                score[i] += gained;
                a.React(HUDReaction.Punch);
                a.SetScore(score[i], gained);
                hud.Player(1).SetTop(score[0] > score[1]);
                hud.Player(2).SetTop(score[1] > score[0]);
            }
        }

        public void Swing(string actor) => Slot(actor)?.React(HUDReaction.Punch);

        public void GetUp(string actor)
        {
            CameraFX.SetContinuous(groundedKey, grounded, 0f, 3f);
            Play(getUp);
            Slot(actor)?.SetDizzy(false);
        }

        public void Dash(string actor)
        {
            Play(dash);
            Slot(actor)?.React(HUDReaction.Roll);
        }

        public void Run(string actor, bool on)
        {
            if (on) runners.Add(actor); else runners.Remove(actor);
            CameraFX.SetContinuous(runKey, run, runners.Count > 0 ? intensity : 0f, 3f);
            Slot(actor)?.SetRunning(on);
        }

        public void Energy(bool on)
        {
            if (on) Play(energyStart);
            CameraFX.SetContinuous(energyKey, energized, on ? intensity : 0f, 3f);
        }

        public void Tired(string actor, bool on)
        {
            CameraFX.SetContinuous(tiredKey, tired, on ? intensity : 0f, 2f);
            Slot(actor)?.SetStamina(on ? 0.05f : 1f);
        }
    }
}
