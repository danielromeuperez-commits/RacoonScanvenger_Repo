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
            // El clip del director mueve al actor: sin root motion, el modelo no se adelanta a su transform.
            if (Animator != null) Animator.applyRootMotion = false;
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
            // Cambio de toma: el actor salta al set nuevo. No es movimiento (no debe ponerse a correr).
            bool teleport = delta.magnitude > 1.5f;
            float speed = teleport ? 0f : delta.magnitude / dt;

            if (stateUntil >= 0f && Time.time >= stateUntil)
            {
                state = 0;
                stateUntil = -1f;
            }

            // Locomoción por velocidad mientras no haya una acción en curso (como hace PlayerController).
            int current = state != 0 ? state : speed > fullSpeed * 0.6f ? 2 : speed > 0.3f ? 1 : 0;
            if (Animator == null) return;
            Animator.SetInteger(StateId, current);
            if (teleport) Animator.SetFloat(SpeedId, 0f);
            else Animator.SetFloat(SpeedId, speed / fullSpeed, speedDampTime, dt);
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

        /// <summary>Variante de ImpactVFX para los siguientes golpes ("Burst", "Star" o "Bolts").</summary>
        public void SetImpactStyle(string style)
        {
            if (Impact == null || !System.Enum.TryParse(style, out ImpactVFX.Style value)) return;
            typeof(ImpactVFX).GetField("style", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(Impact, value);
        }

        // ---------------- Capa de energía (bebida energética) ----------------
        // Lo mismo que PlayerEnergyOverlay (que necesita un PlayerController en red): cada malla animada se copia con
        // el shader CameraFX/CameraFXEnergyOverlay y se le pasa intensidad, origen y tiempo.

        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int FXTimeId = Shader.PropertyToID("_FXTime");
        Material energyMaterial;
        readonly System.Collections.Generic.List<SkinnedMeshRenderer> energyCopies = new();
        float energyLevel, energyTarget;

        public void SetEnergy(bool on)
        {
            energyTarget = on ? 1f : 0f;
            if (on && energyMaterial == null) BuildEnergy();
        }

        void BuildEnergy()
        {
            var shader = Resources.Load<Shader>("CameraFX/CameraFXEnergyOverlay");
            if (shader == null) return;
            energyMaterial = new Material(shader) { name = "Showcase EnergyOverlay" };
            foreach (SkinnedMeshRenderer source in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (source.sharedMesh == null || source.GetComponentInParent<VfxBehaviour>() != null) continue;
                var go = new GameObject(source.name + "_Energy") { layer = source.gameObject.layer };
                go.transform.SetParent(transform, false);
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = source.sharedMesh;
                copy.bones = source.bones;
                copy.rootBone = source.rootBone;
                copy.localBounds = source.localBounds;
                copy.updateWhenOffscreen = true;
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false;
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = energyMaterial;
                copy.sharedMaterials = materials;
                energyCopies.Add(copy);
            }
        }

        void LateUpdate()
        {
            if (energyMaterial == null) return;
            energyLevel = Mathf.MoveTowards(energyLevel, energyTarget, Time.deltaTime / (energyTarget > energyLevel ? 0.25f : 0.5f));
            energyMaterial.SetFloat(IntensityId, energyLevel);
            energyMaterial.SetFloat(FXTimeId, Time.time);
            energyMaterial.SetVector(OriginId, transform.position);
            foreach (var copy in energyCopies) if (copy != null) copy.enabled = energyLevel > 0.001f;
        }

        void OnDestroy()
        {
            if (energyMaterial != null) Destroy(energyMaterial);
        }
    }
}
