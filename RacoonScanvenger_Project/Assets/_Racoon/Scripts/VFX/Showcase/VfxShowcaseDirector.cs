using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Director del showcase de VFX (SCN_VFX_Showcase). Va en el mismo GameObject que el Animator cuyo clip
    /// (ANIM_VFX_Showcase) mueve la cámara y los actores; los efectos se disparan con Animation Events:
    ///
    ///   función "Cue", parámetro string = "Actor:Acción[:arg]". Ejemplos:
    ///     "A:Swing"           golpe al aire (puñetazo + arco)        "A:SwingCharged"   golpe cargado
    ///     "A:Charge:1.2"      empieza a cargar (s hasta carga llena) "A:ChargeEnd"      suelta la carga
    ///     "B:HitLight:A"      golpe ligero recibido de A (impacto + polvo + aturdido)
    ///     "B:HitHeavy:A"      golpe fuerte de A (derribo + estrellas) "B:HitCharged:A"  golpe cargado (naranja)
    ///     "A:Dash"            humo del dash + estela fuerte          "A:Wind:Run|Dash|Off"
    ///     "A:Tired:4"         cansancio durante 4 s                  "A:Dizzy:Stop"     "A:GetUp"
    ///     "C:Test:Impact:TestStar"  cualquier método [VfxTest] (o sin parámetros) de un efecto
    ///     "Arrow:Show" / "Arrow:Hide"  flecha del evento en el suelo
    ///
    /// El clip arranca solo con Play; para grabar, pon la Game view a 1920x1080 y graba (Unity Recorder u OBS).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class VfxShowcaseDirector : MonoBehaviour
    {
        static readonly int VisibleId = Animator.StringToHash("Visible");

        [SerializeField] VfxShowcaseActor actorA;
        [SerializeField] VfxShowcaseActor actorB;
        [SerializeField] VfxShowcaseActor actorC;
        [Tooltip("Animator de la flecha del evento (PF_VFX_EventFloorArrow), opcional.")]
        [SerializeField] Animator eventArrow;
        [Tooltip("Cámara que graba; los golpes sin atacante salen hacia ella.")]
        [SerializeField] Camera shotCamera;
        [Tooltip("Efectos de cámara (CameraFX) y HUD a partir de los cues. Opcional.")]
        [SerializeField] VfxShowcaseFeedback feedback;
        [Tooltip("Escribe en consola cada cue (útil para sacar los tiempos para el montaje en Premiere).")]
        [SerializeField] bool logCues = true;

        [Header("Duraciones (como en PlayerController)")]
        [SerializeField] float punchDuration = 0.35f;
        [SerializeField] float lightStun = 0.45f;
        [SerializeField] float heavyStun = 2.2f;
        [SerializeField] float dashDuration = 0.3f;

        VfxShowcaseActor Actor(string id) => id switch
        {
            "A" => actorA,
            "B" => actorB,
            "C" => actorC,
            _ => null,
        };

        /// <summary>Animation Event. Ver la cabecera de la clase.</summary>
        public void Cue(string cue)
        {
            if (string.IsNullOrEmpty(cue)) return;
            string[] p = cue.Split(':');
            if (logCues) Debug.Log($"[VFX Showcase] {Time.timeSinceLevelLoad:0.00}s  {cue}", this);

            // Cámara lenta para las repeticiones: "Slow:0.3" … "Slow:1". El hitstop de CameraFX también toca
            // timeScale, así que se apaga mientras dura.
            if (p[0] == "Slow")
            {
                float scale = Mathf.Clamp(ParseFloat(p.Length > 1 ? p[1] : null, 1f), 0.05f, 1f);
                Time.timeScale = scale;
                Racoon.Cameras.CameraFX.HitstopEnabled = scale >= 1f;
                return;
            }

            if (p[0] == "Arrow")
            {
                if (eventArrow != null) eventArrow.SetBool(VisibleId, p.Length > 1 && p[1] == "Show");
                return;
            }

            VfxShowcaseActor actor = Actor(p[0]);
            if (actor == null || p.Length < 2)
            {
                Debug.LogWarning($"VfxShowcaseDirector: cue no válido '{cue}'.", this);
                return;
            }
            string arg = p.Length > 2 ? p[2] : null;

            switch (p[1])
            {
                case "Swing":
                case "SwingCharged":
                    actor.Act(12, punchDuration);
                    if (feedback != null) feedback.Swing(p[0]);
                    if (actor.Swing != null) actor.Swing.Play(p[1] == "SwingCharged");
                    break;

                case "Charge":
                    if (actor.Charge != null) actor.Charge.Begin(ParseFloat(arg, 1f));
                    break;
                case "ChargeEnd":
                    if (actor.Charge != null) actor.Charge.End();
                    break;

                case "HitLight":
                    Hit(actor, Actor(arg), false, false, p[0], arg);
                    break;
                case "HitHeavy":
                    Hit(actor, Actor(arg), true, false, p[0], arg);
                    break;
                case "HitCharged":
                    Hit(actor, Actor(arg), true, true, p[0], arg);
                    break;

                case "Dash":
                    actor.Act(15, dashDuration);
                    if (actor.Footsteps != null)
                    {
                        actor.Footsteps.StepsEnabled = false;
                        actor.Footsteps.PlayDash(actor.transform.forward, dashDuration);
                    }
                    if (actor.Wind != null) actor.Wind.SetMode(WindTrailVFX.WindMode.Dash);
                    if (feedback != null) feedback.Dash(p[0]);
                    break;

                case "Wind":
                    if (actor.Wind != null)
                        actor.Wind.SetMode(arg == "Dash" ? WindTrailVFX.WindMode.Dash
                            : arg == "Run" ? WindTrailVFX.WindMode.Run : WindTrailVFX.WindMode.Off);
                    if (feedback != null) feedback.Run(p[0], arg == "Run");
                    break;

                case "Steps":
                    if (actor.Footsteps != null) actor.Footsteps.StepsEnabled = arg != "Off";
                    break;

                case "Tired":
                    if (actor.Tired != null) StartCoroutine(TiredFor(actor.Tired, ParseFloat(arg, 4f), p[0]));
                    break;

                case "Dizzy":
                    if (actor.Dizzy != null)
                    {
                        if (arg == "Stop") actor.Dizzy.Stop();
                        else actor.Dizzy.Play();
                    }
                    break;

                case "GetUp":
                    actor.ClearAct();
                    if (actor.Dizzy != null) actor.Dizzy.Stop();
                    if (actor.Footsteps != null) actor.Footsteps.StepsEnabled = true;
                    if (feedback != null) feedback.GetUp(p[0]);
                    break;

                case "Act":
                    actor.Act(Mathf.RoundToInt(ParseFloat(arg, 0f)), p.Length > 3 ? ParseFloat(p[3], 0.5f) : 0.5f);
                    break;

                case "Test":
                    if (p.Length < 4 || !actor.Invoke(p[2], p[3]))
                        Debug.LogWarning($"VfxShowcaseDirector: no encuentro el test '{cue}'.", this);
                    break;

                default:
                    Debug.LogWarning($"VfxShowcaseDirector: acción desconocida '{cue}'.", this);
                    break;
            }
        }

        // Si se para el Play a mitad de una repetición, que no se quede el tiempo ralentizado.
        void OnDisable()
        {
            Time.timeScale = 1f;
            Racoon.Cameras.CameraFX.HitstopEnabled = true;
        }

        void Hit(VfxShowcaseActor victim, VfxShowcaseActor attacker, bool heavy, bool charged, string victimId, string attackerId)
        {
            Vector3 away;
            if (attacker != null) away = victim.transform.position - attacker.transform.position;
            else if (shotCamera != null) away = victim.transform.position - shotCamera.transform.position;
            else away = -victim.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -victim.transform.forward;
            away.Normalize();

            float stun = heavy ? heavyStun : lightStun;
            victim.Act(heavy ? 16 : 14, stun);
            if (victim.Footsteps != null) victim.Footsteps.StepsEnabled = false;

            if (victim.Impact != null)
                victim.Impact.PlayTowards(-away, heavy, attacker != null ? attacker.Animator : null, null, charged);
            if (victim.Knockback != null) victim.Knockback.Play(away, heavy, stun);
            if (heavy && victim.Dizzy != null) victim.Dizzy.Play();
            if (feedback != null) feedback.Hit(victimId, attackerId, heavy, attacker != null ? attacker.Tint : default);
        }

        System.Collections.IEnumerator TiredFor(TiredVFX tired, float seconds, string id)
        {
            tired.Tired = true;
            if (feedback != null) feedback.Tired(id, true);
            yield return new WaitForSeconds(seconds);
            tired.Tired = false;
            if (feedback != null) feedback.Tired(id, false);
        }

        static float ParseFloat(string s, float fallback) =>
            float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out float v) ? v : fallback;
    }
}
