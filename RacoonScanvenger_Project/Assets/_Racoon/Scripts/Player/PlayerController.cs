using Racoon.Cameras;
using Racoon.Items;
using System;
using System.Security.Cryptography;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Events;

namespace Racoon.Player
{
    /// <summary>
    /// Controlador del jugador en red (Netcode for GameObjects, Client-Server).
    ///
    /// Quién hace qué:
    ///  - DUEÑO (el cliente que controla este mapache): lee input, mueve el Rigidbody,
    ///    gasta estamina y escribe el estado. NetworkTransform (modo Owner) + NetworkRigidbody
    ///    (Use Rigid Body For Motion) replican la posición.
    ///  - SERVIDOR: valida y ejecuta lo que afecta a la partida (inventario, golpes, interacciones).
    ///  - RESTO DE CLIENTES: solo leen el estado replicado para animar y mostrar la barra.
    ///    Allí NetworkRigidbody pone el Rigidbody en kinematic y lo mueve con lo que llega por red.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(PlayerInputHandler), typeof(PlayerInventory))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("Movimiento")]
        [SerializeField] float walkSpeed = 3.5f;
        [SerializeField] float runSpeed = 6.5f;
        [Tooltip("Aceleración al arrancar o ganar velocidad (m/s²). Más baja = arranque con más inercia.")]
        [SerializeField] float groundAcceleration = 16f;
        [Tooltip("Frenada al soltar el stick o ir más despacio (m/s²). Algo mayor que la aceleración: frena antes de lo que arranca.")]
        [SerializeField] float groundDeceleration = 24f;
        [Tooltip("Frenada al cambiar de sentido de golpe (derrape): primero frena y luego arranca hacia el otro lado.")]
        [SerializeField] float reverseDeceleration = 40f;
        [Tooltip("Por debajo de este coseno entre la velocidad y el input se considera cambio de sentido (-0.5 = más de 120°).")]
        [SerializeField, Range(-1f, 0f)] float reverseThreshold = -0.5f;
        [Tooltip("Lo que gira la velocidad (grados/s) andando y corriendo: corriendo traza curvas más abiertas.")]
        [SerializeField] Vector2 velocityTurnRate = new(900f, 380f);
        [Tooltip("Lo que gira el cuerpo (grados/s) andando y corriendo.")]
        [SerializeField] Vector2 bodyTurnRate = new(900f, 540f);
        [Tooltip("Multiplicador de la aceleración y del giro con la bolsa llena (pesa: arranca y gira peor).")]
        [SerializeField, Range(0.1f, 1f)] float fullBagAccelerationMultiplier = 0.7f;
        [Tooltip("Gravedad extra sobre la del Rigidbody para que caiga con peso (0 = solo la de Unity).")]
        [SerializeField] float extraGravity = 10f;
        [SerializeField, Range(0f, 1f)] float moveDeadzone = 0.15f;
        [Tooltip("Multiplicador de velocidad mientras se hace una acción (golpear, usar...).")]
        [SerializeField, Range(0f, 1f)] float actionMoveMultiplier = 0.3f;

        [Header("Estamina")]
        [SerializeField] float maxStamina = 100f;
        [SerializeField] float staminaDrainPerSecond = 30f;
        [SerializeField] float staminaRegenPerSecond = 20f;
        [Tooltip("Segundos sin correr antes de empezar a regenerar.")]
        [SerializeField] float staminaRegenDelay = 0.75f;
        [Tooltip("Si llegas a 0, no puedes volver a correr hasta recuperar este porcentaje.")]
        [SerializeField, Range(0f, 1f)] float exhaustedRecoverThreshold = 0.3f;

        [Header("Peso del loot")]
        [Tooltip("Multiplicador de velocidad cuando la bolsa ha alcanzado Bag Full Weight.")]
        [SerializeField, Range(0.1f, 1f)] float fullBagSpeedMultiplier = 0.75f;
        [Tooltip("Multiplicador de la velocidad (y por tanto la distancia) del dash con la bolsa llena.")]
        [SerializeField, Range(0.1f, 1f)] float fullBagDashMultiplier = 0.75f;
        [Tooltip("Multiplicador de TODO el gasto de estamina (correr, dash, puñetazo) con la bolsa llena.")]
        [SerializeField, Min(1f)] float fullBagStaminaCostMultiplier = 1.5f;

        [Header("Duración de acciones (s)")]
        [SerializeField] float interactDuration = 0.4f;
        [SerializeField] float switchItemDuration = 0.25f;
        [SerializeField] float punchDuration = 0.45f;
        [SerializeField] float useItemDuration = 0.5f;

        [Header("Interacción (solo por delante y a la altura del golpe: usa Punch Height)")]
        [Tooltip("Distancia hacia delante del centro de la esfera de interacción.")]
        [SerializeField] float frontInteractRange = 0.8f;
        [SerializeField] float frontInteractRadius = 0.55f;
        [SerializeField] LayerMask interactMask = ~0;
        [Tooltip("Tiempo mínimo entre cada objeto recogido al mantener Interact.")]
        [SerializeField] float interactRepeatInterval = 0.25f;
        float interactRepeatTimer;

        [Header("Golpe")]
        [Tooltip("Usar este collider para recibir el golpe en lugar del de este mismo GameObject")]
        [SerializeField] Collider hitCollider;
        [Tooltip("Momento del golpe en el que se comprueba el impacto (frame activo).")]
        [SerializeField] float punchHitDelay = 0.15f;
        [SerializeField] float punchRange = 0.9f;
        [SerializeField] float punchHeight = 1f;
        [SerializeField] float punchRadius = 0.6f;
        [Tooltip("Fuerza base del empujón. Cada tipo de golpe recibido la multiplica (ver Recibir golpe).")]
        [SerializeField] float punchKnockback = 8f;
        [SerializeField] float knockbackDamping = 6f;
        [Tooltip("Espera tras terminar un puñetazo antes de poder dar otro (anti-spam). " +
                 "Junto con el stun ligero deja un pequeño hueco para que la víctima escape con un dash.")]
        [SerializeField] float punchCooldown = 0.2f;
        [Tooltip("Margen extra que acepta el servidor al validar la distancia del golpe (latencia).")]
        [SerializeField] float hitValidationTolerance = 1.5f;
        [SerializeField] LayerMask hitMask = ~0;
        [Tooltip("Estamina que gasta cada puñetazo (fracción de la máxima). Sin bastante estamina no se puede golpear.")]
        [SerializeField, Range(0f, 1f)] float punchStaminaCost = 0.1f;

        [Tooltip("Multiplicador de velocidad durante el puñetazo: va por el tren superior, así que se puede golpear andando o corriendo.")]
        [SerializeField, Range(0f, 1f)] float punchMoveMultiplier = 0.9f;

        [Header("Golpe cargado (mantener Usar con las manos vacías)")]
        [Tooltip("Hasta aquí es un toque: puñetazo normal, sin frenar ni dejar de correr. A partir de aquí empieza a cargar.")]
        [SerializeField] float chargeThreshold = 0.2f;
        [Tooltip("Segundos manteniendo (contando el umbral) para que el puñetazo salga cargado. Si se suelta antes, es normal.")]
        [SerializeField] float punchChargeTime = 3f;
        [Tooltip("Multiplicador extra del knockback del golpe cargado (encima del del golpe fuerte; 1 = como el 3º del combo).")]
        [SerializeField] float chargedKnockbackMultiplier = 1f;
        [Tooltip("Estamina extra (fracción de la máxima) del golpe cargado, sumada a Punch Stamina Cost.")]
        [SerializeField, Range(0f, 1f)] float chargedPunchExtraStaminaCost = 0.15f;
        [Tooltip("Multiplicador de velocidad mientras carga (no puede correr).")]
        [SerializeField, Range(0f, 1f)] float chargeMoveMultiplier = 0.5f;

        [Header("Apuntar objeto (mantener Usar con un objeto)")]
        [Tooltip("Multiplicador de velocidad mientras apunta.")]
        [SerializeField, Range(0f, 1f)] float aimMoveMultiplier = 0.6f;
        [Tooltip("Ángulo (grados) entre el stick y hacia donde mira a partir del cual el objeto se suelta DETRÁS.")]
        [SerializeField, Range(90f, 180f)] float dropBehindAngle = 120f;

        /// <summary>Cómo reacciona el jugador a un tipo de golpe.</summary>
        [Serializable]
        public struct HitReaction
        {
            [Tooltip("Multiplica el knockback que llega (punchKnockback en el puñetazo).")]
            public float knockbackMultiplier;
            [Tooltip("Velocidad vertical al recibirlo (pequeño salto). 0 = ninguno.")]
            public float upwardVelocity;
            [Tooltip("Segundos sin control.")]
            public float stunDuration;
            [Tooltip("No le pueden golpear mientras está aturdido (evita rematar en el suelo).")]
            public bool invulnerableWhileStunned;
            [Tooltip("I-frames al recuperar el control (se cancelan si ataca o hace dash).")]
            public float recoveryInvulnerability;

            /// <summary>Lo que dura el parpadeo: stun + i-frames de recuperación.</summary>
            public float BlinkDuration => stunDuration + recoveryInvulnerability;
        }

        [Header("Recibir golpe (combo)")]
        [Tooltip("Golpe nº X del combo que derriba (golpe fuerte). Los anteriores son ligeros.")]
        [SerializeField, Min(1)] int hitsForHeavy = 3;
        [Tooltip("Si pasa este tiempo sin recibir golpes, el combo vuelve a 0.")]
        [SerializeField] float comboResetTime = 1.5f;
        [Tooltip("Golpes 1º y 2º: stun corto, se puede encadenar (pero hay hueco para escapar con dash).")]
        [SerializeField] HitReaction lightHit = new()
        {
            knockbackMultiplier = 0.6f,
            upwardVelocity = 0f,
            stunDuration = 0.45f,
            invulnerableWhileStunned = false,
            recoveryInvulnerability = 0f,
        };
        [Tooltip("Golpe 3º: derriba, invulnerable en el suelo y al levantarse. Reinicia el combo.")]
        [SerializeField] HitReaction heavyHit = new()
        {
            knockbackMultiplier = 1.8f,
            upwardVelocity = 4f,
            stunDuration = 1f,
            invulnerableWhileStunned = true,
            recoveryInvulnerability = 0.8f,
        };

        [Header("Dash")]
        [SerializeField] float dashSpeed = 14f;
        [SerializeField] float dashDuration = 0.25f;
        [Tooltip("Multiplicador de dashSpeed a lo largo del dash (X: 0-1 tiempo normalizado).")]
        [SerializeField] AnimationCurve dashSpeedCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f);
        [Tooltip("Segundos de espera tras terminar un dash antes de poder hacer otro.")]
        [SerializeField] float dashCooldown = 0.35f;
        [Tooltip("Estamina que gasta cada dash (fracción de la máxima). Sin bastante estamina no se puede hacer.")]
        [SerializeField, Range(0f, 1f)] float dashStaminaCostFraction = 0.33f;
        [Tooltip("Inicio de los invincibility-frames, en segundos desde que empieza el dash.")]
        [SerializeField] float invincibilityStart = 0f;
        [Tooltip("Duración de los invincibility-frames (0 = sin invencibilidad).")]
        [SerializeField] float invincibilityDuration = 0.2f;

        [Header("Debug")]
        [Tooltip("Gizmos en Play: rango del puñetazo, i-frames del dash e interacción mientras ocurren.")]
        [SerializeField] bool drawRuntimeGizmos = true;
        [Tooltip("Segundos que se queda dibujada la comprobación del golpe (frame activo).")]
        [SerializeField] float punchGizmoTime = 0.25f;

        [Header("Cámara (Target Group)")]
        [SerializeField] float cameraWeight = 1f;
        [SerializeField] float cameraRadius = 1.5f;

        [Header("Recuperación tras objetos (machacar Dash)")]
        [Tooltip("Pulsaciones de Dash seguidas para levantarse. Cuentan también durante el aturdimiento, " +
                 "así quien machaca rápido se levanta en cuanto acaba.")]
        [SerializeField, Min(1)] int itemRecoveryPresses = 6;
        [Tooltip("Lo que baja el progreso por segundo sin pulsar (fracción de 0 a 1).")]
        [SerializeField, Min(0f)] float itemRecoveryDecayPerSecond = 0.5f;

        [Tooltip("Velocidad vertical al terminar de recuperarse.")]
        [SerializeField] float itemRecoveryJumpVelocity = 3f;
        [Tooltip("Segundos tras levantarse en los que Dash no hace dash (las pulsaciones de machacar que sobran).")]
        [SerializeField, Min(0f)] float recoveryDashLock = 0.4f;

        [Header("Resbalón (plátano)")]
        [Tooltip("Parte de la velocidad que llevaba que conserva al resbalar (1 = toda, >1 sale disparado).")]
        [SerializeField, Min(0f)] float slipMomentumMultiplier = 1.2f;
        [Tooltip("Velocidad mínima del resbalón aunque fuera despacio (en la dirección en la que iba o miraba).")]
        [SerializeField, Min(0f)] float slipMinSpeed = 3f;
        [Tooltip("Frenado mientras patina (más bajo = patina más lejos). Los golpes normales usan Knockback Damping.")]
        [SerializeField, Min(0.1f)] float slipDamping = 2.5f;
        [Tooltip("Segundos que patina y gira.")]
        [SerializeField, Min(0f)] float slipDuration = 0.6f;
        [Tooltip("Vueltas sobre sí mismo mientras patina (0 = no gira).")]
        [SerializeField, Min(0f)] float slipSpinTurns = 1f;

        [Header("Eventos (se lanzan en TODOS los clientes)")]
        public UnityEvent<PlayerController> onInteract;
        public UnityEvent<ItemData> onItemUsed;
        /// <summary>
        /// Cualquier golpe que ENTRA (no en i-frames), ligero o fuerte. Parámetro: quien golpea (puede ser null).
        /// </summary>
        public UnityEvent<PlayerController> onHitReceived;
        /// <summary>
        /// Golpes ligeros del combo (1º y 2º). Parámetros: quien golpea (puede ser null) y nº de golpe (1, 2...).
        /// PlayerVFX se suscribe solo por código (parpadeo).
        /// </summary>
        public UnityEvent<PlayerController, int> onLightHit;
        /// <summary>
        /// Golpe fuerte (3º del combo): derribo. Parámetro: quien golpea (puede ser null).
        /// PlayerVFX se suscribe solo por código (parpadeo largo).
        /// </summary>
        public UnityEvent<PlayerController> onHeavyHit;
        /// <summary>
        /// Al empezar un dash. Parámetro: dirección del dash (horizontal, normalizada), la misma en todos
        /// los clientes. PlayerVFX se suscribe solo por código; aquí puedes añadir sonido, cámara...
        /// </summary>
        public UnityEvent<Vector3> onDash;

        /// <summary>Estado + contador de acciones en una sola variable, para que lleguen juntos.</summary>
        public struct NetState : INetworkSerializeByMemcpy, IEquatable<NetState>
        {
            public PlayerState State;
            // Se incrementa en cada acción: así se detectan dos golpes seguidos aunque el
            // estado no pase visiblemente por Idle entre ellos.
            public byte ActionSequence;
            // I-frames (dash, derribo, recuperación): el servidor lo consulta para descartar golpes.
            public bool Invulnerable;
            // Mantiene Usar cargando un puñetazo (para animación / VFX de carga en todos los clientes).
            public bool ChargingPunch;
            // El último puñetazo (State == Punch) es cargado: SwingVFX lo pinta naranja.
            public bool ChargedPunch;

            public bool Equals(NetState other) =>
                State == other.State && ActionSequence == other.ActionSequence && Invulnerable == other.Invulnerable &&
                ChargingPunch == other.ChargingPunch && ChargedPunch == other.ChargedPunch;
        }

        // El dueño escribe, todos leen.
        readonly NetworkVariable<NetState> netState = new(
            new NetState { State = PlayerState.Idle },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        readonly NetworkVariable<float> staminaNormalized = new(
            1f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        /// <summary>El jugador controlado en esta máquina (null hasta que spawnea).</summary>
        public static PlayerController Local { get; private set; }
        public static event Action<PlayerController> LocalPlayerSpawned;

        /// <summary>(anterior, nuevo). Todos los clientes.</summary>
        public event Action<PlayerState, PlayerState> StateChanged;
        /// <summary>Se lanza al empezar una acción (Interact, SwitchItem, Punch, UseItem). Todos los clientes.</summary>
        public event Action<PlayerState> ActionStarted;

        public PlayerState State => netState.Value.State;
        /// <summary>Está en invincibility-frames. Replicado: válido en todos los clientes y en el servidor.</summary>
        public bool IsInvulnerable => netState.Value.Invulnerable;
        public float DashDuration => dashDuration;

        // ---- Levantarse machacando Dash (para el indicador del HUD; solo tiene sentido en el dueño) ----

        /// <summary>Tirado por un objeto: tiene que machacar Dash para levantarse (también durante el aturdimiento).</summary>
        public bool IsItemRecoveryRequired => itemRecoveryRequired;
        /// <summary>Ya ha acabado el aturdimiento obligatorio: se levanta en cuanto llene el progreso.</summary>
        public bool IsItemRecoveryActive => itemRecoveryRequired && actionTimer <= 0f;
        /// <summary>Progreso de 0 a 1 (sube con cada pulsación, baja solo si deja de pulsar).</summary>
        public float ItemRecoveryNormalized => itemRecoveryProgress;
        public int ItemRecoveryPresses => itemRecoveryPresses;
        /// <summary>Solo dueño. Al quedar tirado por un objeto (empieza a poder machacar).</summary>
        public event Action ItemRecoveryStarted;
        /// <summary>Solo dueño. Cada pulsación que cuenta. Parámetro: progreso (0-1) tras la pulsación.</summary>
        public event Action<float> ItemRecoveryMashed;
        /// <summary>Solo dueño. Al levantarse.</summary>
        public event Action ItemRecoveryFinished;

        // ---- Golpe cargado ----

        /// <summary>Está cargando un puñetazo. Replicado: válido en todos los clientes.</summary>
        public bool IsChargingPunch => netState.Value.ChargingPunch;
        /// <summary>El puñetazo en curso es cargado. Replicado: válido en todos los clientes.</summary>
        public bool IsChargedPunch => netState.Value.ChargedPunch;
        /// <summary>Carga de 0 a 1 (1 = el puñetazo saldría cargado). Solo tiene sentido en el dueño.</summary>
        public float PunchChargeNormalized => chargingPunch && punchChargeTime > 0f ? Mathf.Clamp01(punchChargeTimer / punchChargeTime) : 0f;
        /// <summary>Segundos manteniendo hasta que el puñetazo sale cargado (los VFX de los demás clientes cuentan con esto).</summary>
        public float PunchChargeTime => punchChargeTime;
        /// <summary>Hasta aquí es un toque (puñetazo normal); el VFX de carga espera a que pase.</summary>
        public float PunchChargeThreshold => chargeThreshold;
        /// <summary>Todos los clientes. Empieza (true) o acaba (false) de cargar un puñetazo.</summary>
        public event Action<bool> ChargingPunchChanged;
        /// <summary>El último golpe recibido fue un puñetazo cargado (para el impacto naranja). Todos los clientes.</summary>
        public bool LastHitWasCharged { get; private set; }
        public HitReaction LightHit => lightHit;
        public HitReaction HeavyHit => heavyHit;
        public int HitsForHeavy => hitsForHeavy;
        public float StaminaNormalized => staminaNormalized.Value;
        /// <summary>Solo tiene sentido en el dueño.</summary>
        public bool IsExhausted => exhausted;
        /// <summary>Bebida energética activa (estamina infinita, dash sin cooldown). Solo tiene sentido en el dueño.</summary>
        public bool IsEnergized => infiniteDashActive;
        /// <summary>Segundos que le quedan a la bebida energética. Solo tiene sentido en el dueño.</summary>
        public float EnergyRemaining => infiniteDashTimer;
        /// <summary>Lo que dura la bebida energética que se ha tomado. Solo tiene sentido en el dueño.</summary>
        public float EnergyDuration => infiniteDashDuration;
        /// <summary>
        /// Bebida energética activa, válido en el dueño y en el servidor (que lleva su propia cuenta para no dejar
        /// tomarse otra a la vez). En los demás clientes, false.
        /// </summary>
        public bool IsEnergyDrinkActive => IsOwner ? infiniteDashActive : IsServer && Time.time < serverEnergyEndTime;
        /// <summary>Solo dueño. Ha intentado usar un objeto que ahora no se puede (p. ej. otra bebida con la primera activa).</summary>
        public event Action<ItemData> ItemUseRefused;
        /// <summary>Propulsado por el extintor. Solo tiene sentido en el dueño.</summary>
        public bool IsExtintorActive => extintorActive;
        /// <summary>Mantiene Usar con un objeto equipado: apuntando, lo usará al soltar. Solo tiene sentido en el dueño.</summary>
        public bool IsAimingItem => aimingItem;
        /// <summary>Apuntando con el stick hacia atrás: al soltar, el objeto cae detrás. Solo tiene sentido en el dueño.</summary>
        public bool IsAimingBehind => aimingItem && aimingBehind;
        public PlayerInventory Inventory => inventory;
        public PlayerInputHandler InputHandler => input;

        static readonly Collider[] overlapBuffer = new Collider[16];

        Rigidbody body;
        CapsuleCollider capsule;
        PlayerInputHandler input;
        PlayerInventory inventory;
        PlayerLoot playerLoot;
        NetworkTransform networkTransform;
        Transform cameraTransform;

        // Lo calcula Update (input, a ritmo de frames) y lo aplica FixedUpdate (física).
        Vector3 desiredVelocity;
        Vector3 desiredDirection;
        Vector3 moveVelocity;
        Vector3 knockbackVelocity;
        float stamina;
        float staminaRegenTimer;
        bool exhausted;
        float actionTimer;
        float punchHitTimer = -1f;
        // Solo para gizmos: última comprobación del golpe (dueño).
        float lastPunchCheckTime = float.NegativeInfinity;
        bool lastPunchConnected;
        Vector3 dashDirection;
        float dashCooldownTimer;
        float punchCooldownTimer;
        bool infiniteDashActive;
        float infiniteDashTimer;
        float infiniteDashDuration;
        // Solo servidor: hasta cuándo dura la bebida energética.
        float serverEnergyEndTime = -1f;

        bool itemRecoveryRequired;
        float itemRecoveryProgress;
        // Tiempo que le queda patinando y girando tras pisar un plátano.
        float slipTimer;
        // Mantiene Usar con un objeto: apunta y lo usa al soltar (dueño).
        bool aimingItem;
        bool aimingBehind;

        // Golpe cargado (dueño).
        bool chargingPunch;
        float punchChargeTimer;
        // El puñetazo en curso salió cargado; lo usa OwnerCheckPunchHit.
        bool releasedPunchCharged;
        // Tras levantarse machacando Dash, las pulsaciones que sobran no hacen dash.
        float recoveryDashLockTimer;

        [Header("Extintor")]
        private bool extintorActive;
        private float extintorTimer;
        private float extintorBounceSpeed = 10f;
        private Vector3 extintorDirection;
        [SerializeField] float extintorTurnSpeed = 120f;

        // Combo recibido (lo lleva el dueño de la víctima, que es quien resuelve los golpes).
        int comboHits;
        float lastHitTime = float.NegativeInfinity;
        bool stunInvulnerable;
        float pendingRecoveryInvulnerability;
        float recoveryInvulnerabilityTimer;

        // Servidor: anti-spam de RPCs de golpe (cliente modificado o ráfagas por latencia).
        float lastServerPunchTime = float.NegativeInfinity;

        // Solo para gizmos: último golpe recibido (todos los clientes).
        int debugComboHits;
        float debugLastHitTime = float.NegativeInfinity;

        bool IsInAction => actionTimer > 0f;
        // Solo en el dueño (el estado lo escribe él, así que su copia siempre está al día).
        bool IsStunned => IsInAction && State.IsStun();
        bool IsDashing => IsInAction && State == PlayerState.Dash;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            input = GetComponent<PlayerInputHandler>();
            inventory = GetComponent<PlayerInventory>();
            playerLoot = GetComponent<PlayerLoot>();
            networkTransform = GetComponent<NetworkTransform>();
        }

        // ---------------- Ciclo de vida de red ----------------

        public override void OnNetworkSpawn()
        {
            netState.OnValueChanged += OnNetStateChanged;
            TargetGroupRegistry.Register(transform, cameraWeight, cameraRadius);

            if (networkTransform != null && networkTransform.AuthorityMode != NetworkTransform.AuthorityModes.Owner)
                Debug.LogWarning("El NetworkTransform del jugador debe estar en Authority Mode = Owner.", this);
            if (!TryGetComponent(out NetworkRigidbody networkRigidbody) || !networkRigidbody.UseRigidBodyForMotion)
                Debug.LogWarning("Falta NetworkRigidbody con 'Use Rigid Body For Motion' activado.", this);

            input.SetInputEnabled(IsOwner);
            if (!IsOwner) return;

            Local = this;
            stamina = maxStamina;
            staminaNormalized.Value = 1f;
            input.InteractPressed += OnInteractPressed;
            input.SwitchItemPressed += OnSwitchItemPressed;
            input.UsePressed += OnUsePressed;
            input.DashPressed += OnDashPressed;
            LocalPlayerSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            TargetGroupRegistry.Unregister(transform);
            input.SetInputEnabled(false);

            if (!IsOwner) return;
            input.InteractPressed -= OnInteractPressed;
            input.SwitchItemPressed -= OnSwitchItemPressed;
            input.UsePressed -= OnUsePressed;
            input.DashPressed -= OnDashPressed;
            if (Local == this) Local = null;
        }

        void OnNetStateChanged(NetState previous, NetState current)
        {
            if (previous.State != current.State)
                StateChanged?.Invoke(previous.State, current.State);
            if (previous.ActionSequence != current.ActionSequence && current.State.IsAction())
                ActionStarted?.Invoke(current.State);
            if (previous.ChargingPunch != current.ChargingPunch)
                ChargingPunchChanged?.Invoke(current.ChargingPunch);
        }

        // ---------------- Bucle del dueño ----------------

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (extintorActive)
            {
                // Antes este bloque estaba duplicado y el tiempo bajaba el doble de rápido.
                extintorTimer -= Time.deltaTime;

                desiredVelocity = Vector3.zero;
                moveVelocity = Vector3.zero;
                knockbackVelocity = Vector3.zero;

                if (extintorTimer <= 0f)
                {
                    extintorTimer = 0f;
                    extintorActive = false;

                    body.linearVelocity = new Vector3(
                        0f,
                        body.linearVelocity.y,
                        0f
                    );
                }
            }
            float dt = Time.deltaTime;
            if (interactRepeatTimer > 0f)
                interactRepeatTimer -= dt;

            // Si mantiene Interact, sigue saqueando automáticamente
            // cuando termina la interacción anterior.
            if (input.InteractHeld &&
                !IsInAction &&
                !itemRecoveryRequired &&
                !chargingPunch &&
                !aimingItem &&
                interactRepeatTimer <= 0f)
            {
                StartAction(PlayerState.Interact, interactDuration);
                interactRepeatTimer = interactRepeatInterval;
                RepeatInteractRpc();
            }
            if (actionTimer > 0f) actionTimer -= dt;
            if (dashCooldownTimer > 0f) dashCooldownTimer -= dt;
            if (infiniteDashTimer > 0f)
            {
                infiniteDashTimer -= dt;

                if (infiniteDashTimer <= 0f)
                {
                    infiniteDashTimer = 0f;
                    infiniteDashActive = false;
                }
            }
            if (punchCooldownTimer > 0f) punchCooldownTimer -= dt;
            if (recoveryDashLockTimer > 0f) recoveryDashLockTimer -= dt;
            if (recoveryInvulnerabilityTimer > 0f) recoveryInvulnerabilityTimer -= dt;
            UpdateItemRecovery(dt);
            // Al recuperar el control tras un golpe empiezan sus i-frames de recuperación.
            if (pendingRecoveryInvulnerability > 0f && !IsInAction)
            {
                recoveryInvulnerabilityTimer = pendingRecoveryInvulnerability;
                pendingRecoveryInvulnerability = 0f;
            }
            if (punchHitTimer >= 0f)
            {
                punchHitTimer -= dt;
                if (punchHitTimer < 0f) OwnerCheckPunchHit();
            }
            UpdatePunchCharge(dt);
            UpdateItemAim();

            UpdateInvulnerability();

            Vector2 move = input.Move;
            bool hasMoveInput = move.sqrMagnitude > moveDeadzone * moveDeadzone;
            // El puñetazo va por el tren superior (capa UpperBody): las piernas siguen andando o corriendo.
            bool upperBodyAction = IsInAction && State == PlayerState.Punch;
            bool isRunning = hasMoveInput && input.RunHeld && (!IsInAction || upperBodyAction) && !IsChargeSlowing &&
                             !exhausted && stamina > 0f;

            UpdateStamina(isRunning, dt);

            // Solo se calcula la intención; la física se aplica en FixedUpdate.
            if (IsStunned || itemRecoveryRequired)
            {
                // Sin control: solo actúa el knockback.
                desiredDirection = desiredVelocity = Vector3.zero;
            }
            else if (IsDashing)
            {
                // La velocidad del dash la pone FixedUpdate.
                desiredDirection = dashDirection;
                desiredVelocity = Vector3.zero;
            }
            else
            {
                if (!hasMoveInput) move = Vector2.zero;
                desiredDirection = GetCameraRelativeDirection(move);
                Vector3 moveDirection = desiredDirection;

                // Apuntando un objeto con el stick muy hacia atrás: lo soltará detrás. No se gira hacia el stick
                // (si se girase, "atrás" pasaría a ser "delante" y se perdería el apuntado).
                aimingBehind = aimingItem && hasMoveInput &&
                    Vector3.Angle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), moveDirection) > dropBehindAngle;
                if (aimingBehind) desiredDirection = Vector3.zero;

                float targetSpeed =
                    (isRunning ? runSpeed : walkSpeed) *
                    move.magnitude *
                    WeightLerp(fullBagSpeedMultiplier);
                if (upperBodyAction) targetSpeed *= punchMoveMultiplier;
                else if (IsInAction) targetSpeed *= actionMoveMultiplier;
                else if (IsChargeSlowing) targetSpeed *= chargeMoveMultiplier;
                else if (aimingItem) targetSpeed *= aimMoveMultiplier;
                desiredVelocity = moveDirection * targetSpeed;
            }

            if (!IsInAction && !itemRecoveryRequired)
                SetLocomotionState(!hasMoveInput ? PlayerState.Idle : isRunning ? PlayerState.Run : PlayerState.Walk);
        }

        void UpdateStamina(bool isRunning, float dt)
        {
            // Bebida energética: estamina infinita, siempre llena.
            if (infiniteDashActive)
            {
                stamina = maxStamina;
                exhausted = false;
                staminaRegenTimer = 0f;
                staminaNormalized.Value = 1f;
                return;
            }

            if (isRunning)
            {
                SpendStamina(staminaDrainPerSecond * dt);
            }
            else if (staminaRegenTimer > 0f)
            {
                staminaRegenTimer -= dt;
            }
            else
            {
                stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSecond * dt);
                if (exhausted && stamina >= maxStamina * exhaustedRecoverThreshold) exhausted = false;
            }

            // La NetworkVariable solo se envía en cada tick de red si cambió, no cada frame.
            staminaNormalized.Value = stamina / maxStamina;
        }

        // 1 con la bolsa vacía → fullBagValue con la bolsa llena.
        float WeightLerp(float fullBagValue) =>
            playerLoot != null ? Mathf.Lerp(1f, fullBagValue, playerLoot.WeightNormalized) : 1f;

        /// <summary>Coste de estamina (fracción de la máxima) ya multiplicado por el peso de la bolsa.</summary>
        float StaminaCost(float fraction) => fraction * maxStamina * WeightLerp(fullBagStaminaCostMultiplier);

        bool HasStaminaFor(float cost) => cost <= 0f || infiniteDashActive || (!exhausted && stamina >= cost);

        /// <summary>Gasta estamina (con el multiplicador del peso), retrasa la regeneración y agota al llegar a 0.</summary>
        void SpendStamina(float amount)
        {
            if (amount <= 0f || infiniteDashActive) return;
            stamina -= amount * WeightLerp(fullBagStaminaCostMultiplier);
            staminaRegenTimer = staminaRegenDelay;
            if (stamina <= 0f)
            {
                stamina = 0f;
                exhausted = true;
            }
            staminaNormalized.Value = stamina / maxStamina;
        }

        void UpdateItemRecovery(float dt)
        {
            if (!itemRecoveryRequired)
                return;

            // Sin pulsar, el progreso se va vaciando: hay que machacar, no basta con pulsar de vez en cuando.
            itemRecoveryProgress = Mathf.Max(0f, itemRecoveryProgress - itemRecoveryDecayPerSecond * dt);

            // Se levanta cuando ha terminado el aturdimiento obligatorio Y ha llenado el progreso.
            if (actionTimer <= 0f && itemRecoveryProgress >= 1f)
                FinishItemRecovery();
        }

        // Cada Dash pulsado mientras está tirado (las pulsaciones durante el aturdimiento también cuentan).
        void OnItemRecoveryMash()
        {
            itemRecoveryProgress = Mathf.Min(1f, itemRecoveryProgress + 1f / itemRecoveryPresses);
            ItemRecoveryMashed?.Invoke(itemRecoveryProgress);
            if (actionTimer <= 0f && itemRecoveryProgress >= 1f)
                FinishItemRecovery();
        }

        void FinishItemRecovery()
        {
            itemRecoveryProgress = 0f;
            itemRecoveryRequired = false;

            // Pequeño salto al levantarse.
            if (!body.isKinematic)
            {
                Vector3 velocity = body.linearVelocity;

                body.linearVelocity = new Vector3(
                    velocity.x,
                    itemRecoveryJumpVelocity,
                    velocity.z
                );
            }

            SetLocomotionState(PlayerState.Idle);
            // Las pulsaciones que sobran al levantarse no deben convertirse en un dash.
            recoveryDashLockTimer = recoveryDashLock;
            ItemRecoveryFinished?.Invoke();
        }

        // ---------------- Golpe cargado ----------------

        // Pasado el toque (chargeThreshold) ya está cargando de verdad: frena y no puede correr.
        bool IsChargeSlowing => chargingPunch && punchChargeTimer >= chargeThreshold;

        void UpdatePunchCharge(float dt)
        {
            if (!chargingPunch) return;

            // Algo le ha quitado el control (golpe, objeto...): se pierde la carga.
            if (IsInAction || itemRecoveryRequired)
            {
                CancelPunchCharge();
                return;
            }

            punchChargeTimer += dt;
            // Solo se replica al pasar el toque: así un puñetazo rápido no enciende el VFX de carga en los demás.
            if (punchChargeTimer >= chargeThreshold) SetChargingPunchReplicated(true);
            if (!input.UseHeld) ReleasePunch();
        }

        void SetChargingPunchReplicated(bool charging)
        {
            NetState value = netState.Value;
            if (value.ChargingPunch == charging) return;
            value.ChargingPunch = charging;
            netState.Value = value;
        }

        void CancelPunchCharge()
        {
            chargingPunch = false;
            punchChargeTimer = 0f;
            SetChargingPunchReplicated(false);
        }

        void ReleasePunch()
        {
            // Todo o nada: con la carga completa es un golpe fuerte (naranja); antes, un puñetazo normal.
            bool charged = punchChargeTimer >= punchChargeTime;
            CancelPunchCharge();

            // Se cobra al soltar: el normal más el extra si va cargado (puede dejarle agotado).
            SpendStamina((punchStaminaCost + (charged ? chargedPunchExtraStaminaCost : 0f)) * maxStamina);

            CancelRecoveryInvulnerability();
            releasedPunchCharged = charged;
            StartAction(PlayerState.Punch, punchDuration, charged);
            punchHitTimer = punchHitDelay;
            punchCooldownTimer = punchDuration + punchCooldown;
        }

        // ---------------- Usar objeto (apuntar mientras se mantiene, usar al soltar) ----------------

        void UpdateItemAim()
        {
            if (!aimingItem) return;

            // Algo le ha quitado el control (golpe, plátano, dash...) o ya no tiene el objeto:
            // se cancela sin gastarlo.
            if (IsInAction || itemRecoveryRequired || !inventory.HasEquipped)
            {
                aimingItem = false;
                aimingBehind = false;
                return;
            }

            // Un toque rápido lo usa al instante (al frame siguiente de soltar).
            if (input.UseHeld) return;

            bool dropBehind = aimingBehind;
            aimingItem = false;
            aimingBehind = false;
            CancelRecoveryInvulnerability();
            StartAction(PlayerState.UseItem, useItemDuration);
            UseItemRpc(dropBehind);
        }

        void UpdateInvulnerability()
        {
            // Derribado (o stun configurado como invulnerable) y al levantarse.
            bool invulnerable = (IsStunned && stunInvulnerable) || recoveryInvulnerabilityTimer > 0f;
            if (!invulnerable && IsDashing && invincibilityDuration > 0f)
            {
                float elapsed = dashDuration - actionTimer;
                invulnerable = elapsed >= invincibilityStart && elapsed < invincibilityStart + invincibilityDuration;
            }
            SetInvulnerable(invulnerable);
        }

        // Atacar o hacer dash al levantarse gasta los i-frames de recuperación: no se puede
        // aprovechar la invulnerabilidad para pegar sin riesgo.
        void CancelRecoveryInvulnerability()
        {
            recoveryInvulnerabilityTimer = 0f;
            pendingRecoveryInvulnerability = 0f;
        }

        void SetInvulnerable(bool invulnerable)
        {
            NetState value = netState.Value;
            if (value.Invulnerable == invulnerable) return;
            value.Invulnerable = invulnerable;
            netState.Value = value;
        }

        void FixedUpdate()
        {
            // En el rival el Rigidbody es kinematic y lo mueve NetworkRigidbody: no tocar.
            if (!IsSpawned || !IsOwner || body.isKinematic)
                return;

            if (extintorActive)
            {
                // Usamos la dirección que ya calcula tu movimiento normal
                Vector3 inputDirection = desiredDirection;

                if (inputDirection.sqrMagnitude > 0.01f)
                {
                    inputDirection.y = 0f;
                    inputDirection.Normalize();

                    // Girar poco a poco hacia donde está pulsando el jugador
                    extintorDirection = Vector3.RotateTowards(
                        extintorDirection,
                        inputDirection,
                        extintorTurnSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime,
                        0f
                    );

                    extintorDirection.y = 0f;
                    extintorDirection.Normalize();
                }

                // Mantener velocidad constante
                body.linearVelocity = new Vector3(
                    extintorDirection.x * extintorBounceSpeed,
                    body.linearVelocity.y,
                    extintorDirection.z * extintorBounceSpeed
                );

                body.angularVelocity = Vector3.zero;

                return;
            }
            float dt = Time.fixedDeltaTime;

            // =========================================================
            // MOVIMIENTO NORMAL
            // =========================================================

            Vector3 velocityNormal = body.linearVelocity;

            if (IsDashing)
            {
                float t = dashDuration > 0f
                    ? 1f - actionTimer / dashDuration
                    : 1f;

                // Con la bolsa cargada el dash es más lento (y por tanto más corto).
                moveVelocity =
                    dashDirection *
                    (dashSpeed * dashSpeedCurve.Evaluate(t) * WeightLerp(fullBagDashMultiplier));
            }
            else
            {
                moveVelocity = SteerVelocity(moveVelocity, desiredVelocity, dt);
            }

            // Resbalando en un plátano frena menos: patina más lejos que con un golpe.
            float damping = slipTimer > 0f ? slipDamping : knockbackDamping;
            knockbackVelocity = Vector3.Lerp(
                knockbackVelocity,
                Vector3.zero,
                1f - Mathf.Exp(-damping * dt)
            );

            Vector3 horizontalNormal =
                moveVelocity + knockbackVelocity;

            body.linearVelocity = new Vector3(
                horizontalNormal.x,
                velocityNormal.y,
                horizontalNormal.z
            );

            if (extraGravity > 0f)
                body.AddForce(
                    Vector3.down * extraGravity,
                    ForceMode.Acceleration
                );

            body.angularVelocity = Vector3.zero;

            // Resbalón: gira sobre sí mismo, rápido al principio y frenando (siempre da slipSpinTurns vueltas).
            if (slipTimer > 0f)
            {
                if (slipSpinTurns > 0f && slipDuration > 0f)
                {
                    float spinSpeed = 2f * 360f * slipSpinTurns / slipDuration * (slipTimer / slipDuration);
                    body.MoveRotation(body.rotation * Quaternion.Euler(0f, spinSpeed * dt, 0f));
                }
                slipTimer -= dt;
            }

            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(
                        desiredDirection,
                        Vector3.up
                    );

                // Cuanto más rápido va, más le cuesta girar el cuerpo (y con la bolsa llena, más).
                float turnRate = Mathf.Lerp(bodyTurnRate.x, bodyTurnRate.y, SpeedBlend(moveVelocity.magnitude)) *
                                 WeightLerp(fullBagAccelerationMultiplier);
                body.MoveRotation(
                    IsDashing
                        ? targetRotation
                        : Quaternion.RotateTowards(
                            body.rotation,
                            targetRotation,
                            turnRate * dt
                        )
                );
            }
        }

        // 0 andando (o más despacio) → 1 corriendo.
        float SpeedBlend(float speed) => Mathf.InverseLerp(walkSpeed, runSpeed, speed);

        /// <summary>
        /// Movimiento con inercia: acelera y frena con ritmos distintos, la dirección de la velocidad gira hacia el
        /// input poco a poco (curvas, más abiertas cuanto más rápido) y, si se pide el sentido contrario, derrapa
        /// frenando antes de arrancar hacia el otro lado. El peso de la bolsa le quita aceleración y giro.
        /// </summary>
        Vector3 SteerVelocity(Vector3 current, Vector3 target, float dt)
        {
            float weight = WeightLerp(fullBagAccelerationMultiplier);
            float speed = current.magnitude;
            float targetSpeed = target.magnitude;

            // Sin input: frena en la dirección en la que iba.
            if (targetSpeed < 0.01f)
                return speed > 0.0001f ? current * (Mathf.MoveTowards(speed, 0f, groundDeceleration * dt) / speed) : Vector3.zero;

            Vector3 targetDirection = target / targetSpeed;

            // Arrancando desde parado: sale directamente hacia el input.
            if (speed < 0.2f)
                return targetDirection * Mathf.MoveTowards(speed, targetSpeed, groundAcceleration * weight * dt);

            Vector3 direction = current / speed;

            // Cambio de sentido: derrapa hasta casi pararse (el cuerpo ya se gira hacia el input en FixedUpdate).
            if (Vector3.Dot(direction, targetDirection) < reverseThreshold)
                return direction * Mathf.MoveTowards(speed, 0f, reverseDeceleration * dt);

            float turnRate = Mathf.Lerp(velocityTurnRate.x, velocityTurnRate.y, SpeedBlend(speed)) * weight;
            direction = Vector3.RotateTowards(direction, targetDirection, turnRate * Mathf.Deg2Rad * dt, 0f);
            float rate = targetSpeed > speed ? groundAcceleration * weight : groundDeceleration;
            return direction * Mathf.MoveTowards(speed, targetSpeed, rate * dt);
        }

        Vector3 GetCameraRelativeDirection(Vector2 move)
        {
            if (move == Vector2.zero) return Vector3.zero;

            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform == null) return new Vector3(move.x, 0f, move.y).normalized;

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            // Cámara mirando justo hacia abajo: usamos su "arriba" como adelante.
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            return (forward * move.y + right * move.x).normalized;
        }

        void SetLocomotionState(PlayerState newState)
        {
            NetState value = netState.Value;
            if (value.State == newState) return;
            value.State = newState;
            netState.Value = value;
        }

        void StartAction(PlayerState action, float duration, bool chargedPunch = false)
        {
            NetState value = netState.Value;
            value.State = action;
            value.ActionSequence++;
            value.ChargedPunch = chargedPunch;
            netState.Value = value;
            actionTimer = duration;
        }

        /// <summary>Solo el dueño. Útil para respawns (el spawn inicial lo hace el servidor en la aprobación).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            // Teleport avisa a los demás de que no interpolen el salto de posición.
            if (networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);

            body.position = position;
            body.rotation = rotation;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            moveVelocity = knockbackVelocity = desiredVelocity = Vector3.zero;
            comboHits = 0;
            slipTimer = 0f;
            aimingItem = false;
            CancelPunchCharge();
        }

        public void RequestExtintorPickup(ExtintorPickup pickup)
        {
            if (!IsSpawned || !IsOwner || pickup == null)
                return;

            RequestExtintorPickupRpc(
                new NetworkObjectReference(pickup.NetworkObject)
            );
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestExtintorPickupRpc(
            NetworkObjectReference pickupReference)
        {
            if (!IsServer)
                return;

            if (!pickupReference.TryGet(out NetworkObject pickupObject))
                return;

            ExtintorPickup pickup =
                pickupObject.GetComponent<ExtintorPickup>();

            if (pickup == null)
                return;

            pickup.ConsumeOnServer(this);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        public void ActivateExtintorOnOwnerRpc(
            float duration,
            float bounceSpeed)
        {
            if (!IsOwner || body == null || body.isKinematic)
                return;

            extintorActive = true;
            extintorTimer = duration;
            extintorBounceSpeed = bounceSpeed;

            actionTimer = 0f;
            punchHitTimer = -1f;

            moveVelocity = Vector3.zero;
            knockbackVelocity = Vector3.zero;
            desiredVelocity = Vector3.zero;
            desiredDirection = Vector3.zero;

            // Dirección aleatoria inicial
            Vector3 direction = UnityEngine.Random.insideUnitSphere;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.forward;

            direction.Normalize();

            // Guardamos la dirección para poder reflejarla al chocar
            extintorDirection = direction;

            // Aplicamos la velocidad inicial
            body.linearVelocity = new Vector3(
                direction.x * extintorBounceSpeed,
                body.linearVelocity.y,
                direction.z * extintorBounceSpeed
            );
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsOwner || !extintorActive)
                return;

            if (collision.contactCount == 0)
                return;

            ContactPoint contact = collision.GetContact(0);

            // Refleja la dirección respecto a la superficie golpeada
            Vector3 reflected = Vector3.Reflect(
                extintorDirection,
                contact.normal
            );

            // Solo queremos movimiento horizontal
            reflected.y = 0f;

            if (reflected.sqrMagnitude < 0.01f)
                return;

            extintorDirection = reflected.normalized;
        }

        // ---------------- Input (solo dueño) ----------------

        void OnInteractPressed()
        {
            if (IsInAction || itemRecoveryRequired || chargingPunch || aimingItem) return;

            StartAction(PlayerState.Interact, interactDuration);
            interactRepeatTimer = interactRepeatInterval;
            InteractRpc();
        }

        void OnSwitchItemPressed()
        {
            // CanSwitch evita desequipar el único objeto que llevas.
            // Mientras apunta no cambia: la flecha es del objeto que tiene en la mano.
            if (IsInAction || itemRecoveryRequired || chargingPunch || aimingItem || !inventory.CanSwitch) return;
            StartAction(PlayerState.SwitchItem, switchItemDuration);
            SwitchItemRpc();
        }

        void OnUsePressed()
        {
            // Tirado por un objeto no se puede golpear ni lanzar hasta levantarse.
            if (IsInAction || itemRecoveryRequired || chargingPunch || aimingItem || State.IsGrounded()) return;

            if (inventory.HasEquipped)
            {
                // Ahora no se puede usar (otra bebida con la primera activa): no se apunta ni se gasta, se avisa.
                ItemData equipped = inventory.EquippedItem;
                if (equipped != null && !equipped.CanUse(this))
                {
                    ItemUseRefused?.Invoke(equipped);
                    return;
                }

                // Mientras mantiene se ve la flecha (PlayerItemHandler); el objeto se usa al soltar (UpdateItemAim).
                aimingItem = true;
            }
            else
            {
                // Con las manos vacías empieza a cargar: el puñetazo sale al soltar (UpdatePunchCharge).
                // Un toque rápido es un puñetazo normal.
                if (punchCooldownTimer > 0f || !HasStaminaFor(StaminaCost(punchStaminaCost))) return;
                punchChargeTimer = 0f;
                chargingPunch = true;
            }
        }

        void OnDashPressed()
        {
            // Tirado por un objeto: Dash se machaca para levantarse.
            if (itemRecoveryRequired)
            {
                OnItemRecoveryMash();
                return;
            }

            // Incluye el stun: no se puede salir del aturdimiento con un dash.
            // Recién levantado del plátano: sigue machacando, no quiere un dash.
            if (IsInAction || recoveryDashLockTimer > 0f) return;

            // Con la bebida energética no hay cooldown ni coste de estamina.
            if (!infiniteDashActive)
            {
                if (dashCooldownTimer > 0f) return;
                if (!HasStaminaFor(StaminaCost(dashStaminaCostFraction))) return;
                SpendStamina(dashStaminaCostFraction * maxStamina);
            }

            // El dash cancela la carga del puñetazo.
            if (chargingPunch) CancelPunchCharge();

            // Hacia donde apunta el stick; sin input, hacia delante.
            Vector2 move = input.Move;
            Vector3 direction = move.sqrMagnitude > moveDeadzone * moveDeadzone
                ? GetCameraRelativeDirection(move)
                : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

            CancelRecoveryInvulnerability(); // El dash ya trae sus propios i-frames.
            dashDirection = direction;
            dashCooldownTimer = infiniteDashActive
            ? 0f
            : dashDuration + dashCooldown;
            StartAction(PlayerState.Dash, dashDuration);
            UpdateInvulnerability(); // Si los i-frames empiezan en 0, ya cuentan desde este frame.
            DashRpc(direction);
        }

        // ---------------- Dash ----------------

        // SendTo.Everyone: los VFX salen al instante en el dueño y con la dirección exacta en el resto.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void DashRpc(Vector3 direction)
        {
            onDash?.Invoke(direction);
        }

        // ---------------- Interactuar ----------------

        // SendTo.Everyone: se ejecuta al instante en el dueño y luego en servidor y rival.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void InteractRpc()
        {
            onInteract?.Invoke(this);

            if (IsServer)
                ServerInteractWithNearest(false);
        }

        /// <summary>
        /// Repite la interacción mientras el jugador mantiene pulsado Interact.
        /// Solo funciona con interactuables marcados como IRepeatableInteractable.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RepeatInteractRpc()
        {
            ServerInteractWithNearest(true);
        }

        // Igual que el golpe: una esfera por delante del personaje (no se interactúa con lo que hay detrás).
        Vector3 InteractForward
        {
            get
            {
                Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                return direction.sqrMagnitude < 0.0001f ? Vector3.forward : direction;
            }
        }

        // A la misma altura que la esfera del golpe (punchHeight), no por encima del personaje.
        Vector3 InteractCenter => transform.position + Vector3.up * punchHeight + InteractForward * frontInteractRange;

        void ServerInteractWithNearest(bool repeatOnly)
        {
            Vector3 origin = transform.position + Vector3.up * punchHeight;

            int count = Physics.OverlapSphereNonAlloc(
                InteractCenter,
                frontInteractRadius,
                overlapBuffer,
                interactMask,
                QueryTriggerInteraction.Collide
            );

            IInteractable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];

                IInteractable interactable =
                    hit.GetComponentInParent<IInteractable>();

                // Al mantener E solo repetimos interactuables que
                // explícitamente permitan interacción continua.
                if (repeatOnly && interactable is not IRepeatableInteractable)
                    continue;

                if (interactable == null ||
                    !interactable.CanInteract(this))
                    continue;

                Vector3 closestPoint =
                    hit.ClosestPoint(origin);

                float distance =
                    (closestPoint - origin).sqrMagnitude;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = interactable;
                }
            }

            best?.Interact(this);
        }

        // ---------------- Inventario ----------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SwitchItemRpc() => inventory.ServerTrySwitch();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void UseItemRpc(bool dropBehind)
        {
            // El dueño ya lo comprueba; esto es por si llega justo al acabar (no se gasta).
            ItemData equipped = inventory.EquippedItem;
            if (equipped != null && !equipped.CanUse(this))
            {
                ItemUseRefusedRpc(inventory.Database.GetId(equipped));
                return;
            }

            ItemData item = inventory.ServerConsumeEquipped();
            if (item == null) return;

            item.ServerUse(this, dropBehind);
            ItemUsedRpc(inventory.Database.GetId(item));
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ItemUseRefusedRpc(int itemId) => ItemUseRefused?.Invoke(inventory.Database.Get(itemId));

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ItemUsedRpc(int itemId)
        {
            ItemData item = inventory.Database.Get(itemId);
            if (item != null) item.useSfx.Play(transform.position);
            onItemUsed?.Invoke(item);
        }

        // ---------------- Golpe ----------------

        // El dueño detecta el impacto (se siente instantáneo) y el servidor lo valida.
        Vector3 PunchCenter => transform.position + Vector3.up * punchHeight + transform.forward * punchRange;

        void OwnerCheckPunchHit()
        {
            lastPunchCheckTime = Time.time;
            lastPunchConnected = false;
            int count = Physics.OverlapSphereNonAlloc(PunchCenter, punchRadius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            bool playerChecked = false, bananaKicked = false;

            for (int i = 0; i < count; i++)
            {
                // Un plátano en el suelo al alcance: sale volando (como mucho uno por golpe).
                if (!bananaKicked)
                {
                    BananaTrap banana = overlapBuffer[i].GetComponentInParent<BananaTrap>();
                    if (banana != null && banana.IsSpawned)
                    {
                        bananaKicked = true;
                        KickBananaRpc(banana.NetworkObject);
                        continue;
                    }
                }

                if (playerChecked) continue;
                PlayerController victim = FindPlayerForHitCollider(overlapBuffer[i]);
                if (victim == null || victim == this) continue;
                playerChecked = true;

                // Lo esquivó con un dash: no gastamos un RPC.
                if (victim.IsInvulnerable) continue;
                lastPunchConnected = true;
                PunchHitRpc(victim.NetworkObject, releasedPunchCharged);
            }
        }

        PlayerController FindPlayerForHitCollider(Collider candidate)
        {
            PlayerController victim = candidate.GetComponentInParent<PlayerController>();
            if (victim != null) return victim;

            if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
                return null;

            foreach (NetworkObject networkObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject.TryGetComponent(out PlayerController player) && player.hitCollider == candidate)
                    return player;
            }

            return null;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void KickBananaRpc(NetworkObjectReference bananaReference)
        {
            if (!bananaReference.TryGet(out NetworkObject bananaObject) ||
                !bananaObject.TryGetComponent(out BananaTrap banana))
                return;

            // Mismo margen que el golpe a un jugador.
            Vector3 toBanana = banana.transform.position - transform.position;
            toBanana.y = 0f;
            float maxDistance = punchRange + punchRadius + hitValidationTolerance;
            if (toBanana.magnitude > maxDistance) return;

            banana.ServerKick(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PunchHitRpc(NetworkObjectReference victimReference, bool charged)
        {
            if (!victimReference.TryGet(out NetworkObject victimObject) ||
                !victimObject.TryGetComponent(out PlayerController victim) ||
                victim == this)
                return;

            // No acepta golpes más rápidos que la animación (con margen por el jitter de red).
            if (Time.time - lastServerPunchTime < punchDuration * 0.75f) return;
            lastServerPunchTime = Time.time;

            Vector3 hitPoint = victim.hitCollider != null
                ? victim.hitCollider.ClosestPoint(transform.position)
                : victim.transform.position;
            Vector3 toVictim = hitPoint - transform.position;
            toVictim.y = 0f;
            float maxDistance = punchRange + punchRadius +
                (victim.hitCollider != null ? 0f : capsule != null ? capsule.radius * 2f : 1f) + hitValidationTolerance;
            if (toVictim.magnitude > maxDistance) return;

            Vector3 direction = toVictim.sqrMagnitude > 0.0001f ? toVictim.normalized : transform.forward;
            // Cargado: derriba como el 3º del combo (golpe fuerte), con su impacto naranja.
            float knockback = punchKnockback * (charged ? chargedKnockbackMultiplier : 1f);
            victim.ServerApplyHit(direction * knockback, this, charged);
        }

        // ---------------- Efectos de objetos ----------------

        /// <summary>
        /// Solo servidor. Aturde temporalmente al jugador sin contar
        /// como un golpe del combo.
        /// </summary>
        /// <param name="slip">Resbalón (plátano): en vez de pararse en seco patina con su impulso y gira.</param>
        public void ServerApplyItemStun(float duration, bool slip = false)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerApplyItemStun solo se puede llamar en el servidor.", this);
                return;
            }

            if (duration <= 0f) return;

            ItemStunRpc(duration, slip);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ItemStunRpc(float duration, bool slip)
        {
            // Cancela cualquier golpe que estuviera preparando.
            punchHitTimer = -1f;

            // Resbalón: la velocidad la conoce bien el dueño (el servidor la ve con retraso).
            Vector3 slide = Vector3.zero;
            if (slip)
            {
                Vector3 velocity = body.isKinematic ? Vector3.zero : body.linearVelocity;
                velocity.y = 0f;
                slide = velocity * slipMomentumMultiplier;

                if (slide.magnitude < slipMinSpeed)
                {
                    Vector3 direction = slide.sqrMagnitude > 0.01f
                        ? slide.normalized
                        : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    slide = direction * slipMinSpeed;
                }
            }

            // Detenemos el movimiento controlado; si resbala, el impulso sigue como knockback (patina).
            moveVelocity = Vector3.zero;
            desiredVelocity = Vector3.zero;
            desiredDirection = Vector3.zero;
            knockbackVelocity = slide;
            slipTimer = slip ? slipDuration : 0f;
            aimingItem = false;

            if (!body.isKinematic)
            {
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(slide.x, velocity.y, slide.z);
            }

            // No cuenta para el combo de puñetazos.
            stunInvulnerable = false;
            recoveryInvulnerabilityTimer = 0f;
            pendingRecoveryInvulnerability = 0f;

            CancelPunchCharge();
            itemRecoveryProgress = 0f;
            itemRecoveryRequired = true;

            // Downed se mantiene hasta levantarse (FinishItemRecovery vuelve a Idle), no solo lo que dura el stun.
            StartAction(PlayerState.Downed, duration);
            ItemRecoveryStarted?.Invoke();
        }

        /// <summary>
        /// Solo servidor. Bebida energética: durante <paramref name="duration"/> segundos la estamina es infinita
        /// (correr, dash y puñetazos no gastan) y el dash no tiene cooldown. No se puede tomar otra mientras dura
        /// (EnergyDrinkEffect.CanUse); si aun así llega, se renueva.
        /// </summary>
        public void ServerApplyEnergyDrink(float duration)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerApplyEnergyDrink solo se puede llamar en el servidor.", this);
                return;
            }

            if (duration <= 0f) return;

            serverEnergyEndTime = Mathf.Max(serverEnergyEndTime, Time.time + duration);
            EnergyDrinkRpc(duration);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void EnergyDrinkRpc(float duration)
        {
            infiniteDashActive = true;
            infiniteDashTimer = Mathf.Max(infiniteDashTimer, duration);
            infiniteDashDuration = Mathf.Max(infiniteDashTimer, 0.01f);
            dashCooldownTimer = 0f;
        }

        // ---------------- Recibir golpe ----------------

        /// <summary>
        /// Solo servidor. Punto de entrada para CUALQUIER cosa que golpee al jugador (puñetazo,
        /// objetos lanzados...). Se ignora si está en invincibility-frames.
        /// <paramref name="charged"/>: puñetazo cargado; derriba aunque no toque por combo y su impacto sale naranja.
        /// </summary>
        public void ServerApplyHit(Vector3 knockback, PlayerController attacker = null, bool charged = false)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerApplyHit solo se puede llamar en el servidor.", this);
                return;
            }
            if (IsInvulnerable) return;

            ReceiveHitRpc(knockback, attacker != null ? attacker.NetworkObjectId : ulong.MaxValue, charged);
        }

        // El golpe lo resuelve el dueño de la víctima: tiene la información más reciente de sus
        // i-frames (el valor que ve el servidor llega con latencia), así el dash se siente justo.
        // También lleva la cuenta del combo, así nunca se desincroniza con lo que ve el jugador.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ReceiveHitRpc(Vector3 knockback, ulong attackerObjectId, bool charged)
        {
            if (IsInvulnerable) return;

            // Combo: vuelve a 0 si ha pasado demasiado desde el último golpe.
            if (Time.time - lastHitTime > comboResetTime) comboHits = 0;
            lastHitTime = Time.time;
            comboHits++;
            int hitNumber = comboHits;
            bool heavy = charged || comboHits >= hitsForHeavy;
            if (heavy) comboHits = 0;
            HitReaction reaction = heavy ? heavyHit : lightHit;

            // Solo el dueño mueve su personaje, así que es él quien aplica el empujón.
            // Se sustituye (no se suma) para que varios golpes seguidos no lo lancen cada vez más lejos.
            knockbackVelocity = knockback * reaction.knockbackMultiplier;
            moveVelocity = Vector3.zero;
            if (reaction.upwardVelocity > 0f && !body.isKinematic)
            {
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(velocity.x, Mathf.Max(velocity.y, reaction.upwardVelocity), velocity.z);
            }

            // Mira hacia quien le ha golpeado (las animaciones de golpe asumen impacto frontal).
            Vector3 facing = -knockback;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f)
            {
                Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
                if (body.isKinematic) transform.rotation = rotation;
                else body.rotation = rotation;
            }

            punchHitTimer = -1f; // Un golpe recibido cancela el tuyo...
            CancelPunchCharge(); // ...y la carga.
            stunInvulnerable = reaction.invulnerableWhileStunned;
            recoveryInvulnerabilityTimer = 0f;
            pendingRecoveryInvulnerability = reaction.recoveryInvulnerability;
            StartAction(heavy ? PlayerState.Knockdown : PlayerState.HitStun, reaction.stunDuration);
            UpdateInvulnerability(); // El derribo es invulnerable desde este mismo frame.
            HitReactedRpc(attackerObjectId, (byte)hitNumber, heavy, charged);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void HitReactedRpc(ulong attackerObjectId, byte hitNumber, bool heavy, bool charged)
        {
            PlayerController attacker = null;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerObjectId, out NetworkObject attackerObject))
                attackerObject.TryGetComponent(out attacker);

            debugComboHits = hitNumber;
            debugLastHitTime = Time.time;
            // Antes de los eventos: PlayerVFX lo lee para pintar el impacto naranja.
            LastHitWasCharged = charged;

            // El golpe fuerte le hace soltar parte del loot que no ha vendido (lo decide el servidor).
            if (heavy && IsServer && playerLoot != null)
            {
                Vector3 away = attacker != null ? transform.position - attacker.transform.position : -transform.forward;
                playerLoot.ServerDropLoot(away);
            }

            onHitReceived?.Invoke(attacker);
            if (heavy) onHeavyHit?.Invoke(attacker);
            else onLightHit?.Invoke(attacker, hitNumber);
        }

        // Rangos configurados (con el objeto seleccionado, también fuera de Play).
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(InteractCenter, frontInteractRadius);
            Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
            Gizmos.DrawWireSphere(PunchCenter, punchRadius);
        }

        // En Play, lo que está pasando ahora mismo. Funciona en todos los clientes porque usa el
        // estado replicado (la comprobación real del golpe solo se ve en el dueño del que golpea).
        // En la Game view hay que activar el botón "Gizmos".
        void OnDrawGizmos()
        {
            if (!drawRuntimeGizmos || !Application.isPlaying || !IsSpawned) return;

            if (State == PlayerState.Interact)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(InteractCenter, frontInteractRadius);
            }

            if (State == PlayerState.Punch || IsChargingPunch)
            {
                Gizmos.color = IsChargingPunch || IsChargedPunch ? new Color(1f, 0.3f, 0f) : new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(PunchCenter, punchRadius);
            }

            // Frame activo: rojo si ha encontrado a alguien, amarillo si ha fallado.
            if (Time.time - lastPunchCheckTime < punchGizmoTime)
            {
                Gizmos.color = lastPunchConnected ? new Color(1f, 0f, 0f, 0.5f) : new Color(1f, 1f, 0f, 0.35f);
                Gizmos.DrawSphere(PunchCenter, punchRadius);
            }

            if (IsInvulnerable)
            {
                // Burbuja azul que envuelve la cápsula mientras duran los i-frames.
                Vector3 center = capsule != null ? transform.TransformPoint(capsule.center) : transform.position;
                float radius = capsule != null ? Mathf.Max(capsule.radius, capsule.height * 0.5f) + 0.1f : 0.8f;
                Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.3f);
                Gizmos.DrawSphere(center, radius);
                Gizmos.color = new Color(0.3f, 0.6f, 1f);
                Gizmos.DrawWireSphere(center, radius);
            }

#if UNITY_EDITOR
            // Contador del combo recibido sobre la cabeza mientras sigue vivo.
            if (Time.time - debugLastHitTime < comboResetTime)
            {
                bool heavy = debugComboHits >= hitsForHeavy;
                var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
                style.normal.textColor = heavy ? Color.red : Color.yellow;
                UnityEditor.Handles.Label(transform.position + Vector3.up * 2f,
                    heavy ? "¡DERRIBO!" : $"Golpes {debugComboHits}/{hitsForHeavy}", style);
            }
#endif
        }
    }
}
