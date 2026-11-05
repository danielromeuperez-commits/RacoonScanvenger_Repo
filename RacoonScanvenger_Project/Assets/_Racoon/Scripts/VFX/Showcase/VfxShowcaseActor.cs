using System.Reflection;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Personaje de la escena de showcase de VFX (SCN_VFX_Showcase). Va en un GameObject padre de una instancia de
    /// PF_Player* a la que se le han quitado red, input y control. El clip del director mueve este transform; aquí se
    /// calcula la velocidad para el Animator (Speed) y se ofrecen las acciones que disparan los VFX del jugador.
    /// </summary>
    public class VfxShowcaseActor : MonoBehaviour
    {
        static readonly int StateId = Animator.StringToHash("State");
        static readonly int ActionStartId = Animator.StringToHash("ActionStart");
        static readonly int BodyActionId = Animator.StringToHash("BodyAction");
        static readonly int SpeedId = Animator.StringToHash("Speed");

        [Tooltip("Color del jugador para estelas e impactos (el windTint de su PlayerVFX).")]
        [SerializeField] Color tint = Color.white;
        [Tooltip("Velocidad (m/s) que equivale a Speed = 1 en el Animator.")]
        [SerializeField] float fullSpeed = 6.5f;
        [SerializeField] float speedDampTime = 0.08f;

        public Color Tint => tint;
        public Animator Animator { get; private set; }
        public ImpactVFX Impact { get; private set; }
        public SwingVFX Swing { get; private set; }
        public ChargeVFX Charge { get; private set; }
        public DizzyStarsVFX Dizzy { get; private set; }
        public KnockbackDustVFX Knockback { get; private set; }
        public TiredVFX Tired { get; private set; }
        public FootstepVFX Footsteps { get; private set; }
        public WindTrailVFX Wind { get; private set; }

        Vector3 lastPosition;
        int state;
        float stateUntil = -1f;

        void Awake()
        {
            Animator = GetComponentInChildren<Animator>();
            Impact = GetComponentInChildren<ImpactVFX>(true);
            Swing = GetComponentInChildren<SwingVFX>(true);
            Charge = GetComponentInChildren<ChargeVFX>(true);
            Dizzy = GetComponentInChildren<DizzyStarsVFX>(true);
            Knockback = GetComponentInChildren<KnockbackDustVFX>(true);
            Tired = GetComponentInChildren<TiredVFX>(true);
            Footsteps = GetComponentInChildren<FootstepVFX>(true);
            Wind = GetComponentInChildren<WindTrailVFX>(true);
            lastPosition = transform.position;
        }

        void Start()
        {
            if (Wind != null) Wind.SetPlayerTint(tint);
            if (Impact != null) Impact.PlayerColor = tint;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = delta.magnitude / dt;

            if (stateUntil >= 0f && Time.time >= stateUntil)
            {
                state = 0;
                stateUntil = -1f;
            }

            // Locomoción por velocidad mientras no haya una acción en curso (como hace PlayerController).
            int current = state != 0 ? state : speed > fullSpeed * 0.6f ? 2 : speed > 0.3f ? 1 : 0;
            if (Animator == null) return;
            Animator.SetInteger(StateId, current);
            Animator.SetFloat(SpeedId, speed / fullSpeed, speedDampTime, dt);
        }

        /// <summary>Pone un estado de PlayerState (Punch = 12, HitStun = 14, Dash = 15, Knockdown = 16...) y su trigger.</summary>
        public void Act(int playerState, float duration)
        {
            state = playerState;
            stateUntil = duration > 0f ? Time.time + duration : -1f;
            if (Animator == null) return;
            Animator.SetInteger(StateId, playerState);
            if (playerState >= 10 && playerState <= 13) Animator.SetTrigger(ActionStartId);
            else if (playerState >= 14) Animator.SetTrigger(BodyActionId);
        }

        public void ClearAct()
        {
            state = 0;
            stateUntil = -1f;
        }

        /// <summary>Llama a un método (público o privado, sin parámetros) de uno de los efectos, p. ej. los [VfxTest].</summary>
        public bool Invoke(string component, string method)
        {
            MonoBehaviour target = component switch
            {
                "Impact" => Impact,
                "Swing" => Swing,
                "Charge" => Charge,
                "Dizzy" => Dizzy,
                "Knockback" => Knockback,
                "Tired" => Tired,
                "Footsteps" => Footsteps,
                "Wind" => Wind,
                _ => null,
            };
            if (target == null) return false;
            MethodInfo info = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
            if (info == null) return false;
            info.Invoke(target, null);
            return true;
        }
    }
}
