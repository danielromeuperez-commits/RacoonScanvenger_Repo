using System;
using Racoon.Cameras;
using Racoon.Items;
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
        [SerializeField] float acceleration = 30f;
        [SerializeField] float rotationSpeed = 720f;
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

        [Header("Duración de acciones (s)")]
        [SerializeField] float interactDuration = 0.4f;
        [SerializeField] float switchItemDuration = 0.25f;
        [SerializeField] float punchDuration = 0.45f;
        [SerializeField] float useItemDuration = 0.5f;

        [Header("Interacción")]
        [SerializeField] float interactRadius = 1.5f;
        [SerializeField] LayerMask interactMask = ~0;

        [Header("Golpe")]
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
        [SerializeField] float dashStaminaCost = 15f;
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

        [Header("Recuperación tras objetos")]
        [SerializeField] float itemRecoveryHoldDuration = 1f;

        [Tooltip("Velocidad vertical al terminar de recuperarse.")]
        [SerializeField] float itemRecoveryJumpVelocity = 3f;

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

            public bool Equals(NetState other) =>
                State == other.State && ActionSequence == other.ActionSequence && Invulnerable == other.Invulnerable;
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

        public bool IsItemRecoveryActive => itemRecoveryRequired && actionTimer <= 0f;

        public float ItemRecoveryNormalized =>
            itemRecoveryHoldDuration > 0f
            ? Mathf.Clamp01(itemRecoveryProgress / itemRecoveryHoldDuration)
            : 0f;
        public HitReaction LightHit => lightHit;
        public HitReaction HeavyHit => heavyHit;
        public int HitsForHeavy => hitsForHeavy;
        public float StaminaNormalized => staminaNormalized.Value;
        /// <summary>Solo tiene sentido en el dueño.</summary>
        public bool IsExhausted => exhausted;
        public PlayerInventory Inventory => inventory;
        public PlayerInputHandler InputHandler => input;

        static readonly Collider[] overlapBuffer = new Collider[16];

        Rigidbody body;
        CapsuleCollider capsule;
        PlayerInputHandler input;
        PlayerInventory inventory;
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

        bool itemRecoveryRequired;
        float itemRecoveryProgress;

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
        }

        // ---------------- Bucle del dueño ----------------

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            float dt = Time.deltaTime;
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

            UpdateInvulnerability();

            Vector2 move = input.Move;
            bool hasMoveInput = move.sqrMagnitude > moveDeadzone * moveDeadzone;
            bool isRunning = hasMoveInput && input.RunHeld && !IsInAction && !exhausted && stamina > 0f;

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
                float targetSpeed = (isRunning ? runSpeed : walkSpeed) * move.magnitude;
                if (IsInAction) targetSpeed *= actionMoveMultiplier;
                desiredVelocity = desiredDirection * targetSpeed;
            }

            if (!IsInAction && !itemRecoveryRequired)
                SetLocomotionState(!hasMoveInput ? PlayerState.Idle : isRunning ? PlayerState.Run : PlayerState.Walk);
        }

        void UpdateStamina(bool isRunning, float dt)
        {
            if (isRunning)
            {
                stamina -= staminaDrainPerSecond * dt;
                staminaRegenTimer = staminaRegenDelay;
                if (stamina <= 0f)
                {
                    stamina = 0f;
                    exhausted = true;
                }
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

        void UpdateItemRecovery(float dt)
        {
            if (!itemRecoveryRequired)
                return;

            // Primero tiene que terminar el stun obligatorio.
            if (actionTimer > 0f)
            {
                itemRecoveryProgress = 0f;
                return;
            }

            // Después del stun tiene que mantener pulsado el botón de Dash
            // (Espacio en teclado) para levantarse.
            if (input.DashHeld)
            {
                itemRecoveryProgress += dt;

                if (itemRecoveryProgress >= itemRecoveryHoldDuration)
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
                }
            }
            else
            {
                // Si suelta Espacio antes de tiempo,
                // tiene que volver a empezar.
                itemRecoveryProgress = 0f;
            }
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
            if (!IsSpawned || !IsOwner || body.isKinematic) return;

            float dt = Time.fixedDeltaTime;
            if (IsDashing)
            {
                // Se mueve con velocidad (no atraviesa nada): choca con el entorno y con el otro jugador.
                float t = dashDuration > 0f ? 1f - actionTimer / dashDuration : 1f;
                moveVelocity = dashDirection * (dashSpeed * dashSpeedCurve.Evaluate(t));
            }
            else
            {
                moveVelocity = Vector3.MoveTowards(moveVelocity, desiredVelocity, acceleration * dt);
            }
            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 1f - Mathf.Exp(-knockbackDamping * dt));

            // Controlamos la velocidad horizontal; la vertical la sigue llevando la física (gravedad, rampas).
            Vector3 horizontal = moveVelocity + knockbackVelocity;
            body.linearVelocity = new Vector3(horizontal.x, body.linearVelocity.y, horizontal.z);
            if (extraGravity > 0f) body.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
            // Los choques no deben hacer girar al personaje; la rotación la decidimos nosotros.
            body.angularVelocity = Vector3.zero;

            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
                // En el dash mira directamente hacia donde sale.
                body.MoveRotation(IsDashing ? targetRotation : Quaternion.RotateTowards(body.rotation, targetRotation, rotationSpeed * dt));
            }
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

        void StartAction(PlayerState action, float duration)
        {
            NetState value = netState.Value;
            value.State = action;
            value.ActionSequence++;
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
        }

        // ---------------- Input (solo dueño) ----------------

        void OnInteractPressed()
        {
            if (IsInAction || itemRecoveryRequired) return;
            StartAction(PlayerState.Interact, interactDuration);
            InteractRpc();
        }

        void OnSwitchItemPressed()
        {
            // CanSwitch evita desequipar el único objeto que llevas.
            if (IsInAction || itemRecoveryRequired || !inventory.CanSwitch) return;
            StartAction(PlayerState.SwitchItem, switchItemDuration);
            SwitchItemRpc();
        }

        void OnUsePressed()
        {
            if (IsInAction) return;

            if (inventory.HasEquipped)
            {
                CancelRecoveryInvulnerability();
                StartAction(PlayerState.UseItem, useItemDuration);
                UseItemRpc();
            }
            else
            {
                if (punchCooldownTimer > 0f) return;
                CancelRecoveryInvulnerability();
                StartAction(PlayerState.Punch, punchDuration);
                punchHitTimer = punchHitDelay;
                punchCooldownTimer = punchDuration + punchCooldown;
            }
        }

        void OnDashPressed()
        {
            // Incluye el stun: no se puede salir del aturdimiento con un dash.
            if (IsInAction || itemRecoveryRequired) return;

            // Con la bebida energética no hay cooldown ni coste de estamina.
            if (!infiniteDashActive)
            {
                if (dashCooldownTimer > 0f) return;
                if (dashStaminaCost > 0f && (exhausted || stamina < dashStaminaCost)) return;
            }

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
            if (IsServer) ServerInteractWithNearest();
        }

        void ServerInteractWithNearest()
        {
            Vector3 origin = transform.position;
            int count = Physics.OverlapSphereNonAlloc(origin, interactRadius, overlapBuffer, interactMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                IInteractable interactable = overlapBuffer[i].GetComponentInParent<IInteractable>();
                if (interactable == null || !interactable.CanInteract(this)) continue;

                float distance = (overlapBuffer[i].transform.position - origin).sqrMagnitude;
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
        void UseItemRpc()
        {
            ItemData item = inventory.ServerConsumeEquipped();
            if (item == null) return;

            item.ServerUse(this);
            ItemUsedRpc(inventory.Database.GetId(item));
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ItemUsedRpc(int itemId) => onItemUsed?.Invoke(inventory.Database.Get(itemId));

        // ---------------- Golpe ----------------

        // El dueño detecta el impacto (se siente instantáneo) y el servidor lo valida.
        Vector3 PunchCenter => transform.position + Vector3.up * punchHeight + transform.forward * punchRange;

        void OwnerCheckPunchHit()
        {
            lastPunchCheckTime = Time.time;
            lastPunchConnected = false;
            int count = Physics.OverlapSphereNonAlloc(PunchCenter, punchRadius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerController victim = overlapBuffer[i].GetComponentInParent<PlayerController>();
                if (victim == null || victim == this) continue;

                // Lo esquivó con un dash: no gastamos un RPC.
                if (victim.IsInvulnerable) return;
                lastPunchConnected = true;
                PunchHitRpc(victim.NetworkObject);
                return;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PunchHitRpc(NetworkObjectReference victimReference)
        {
            if (!victimReference.TryGet(out NetworkObject victimObject) ||
                !victimObject.TryGetComponent(out PlayerController victim) ||
                victim == this)
                return;

            // No acepta golpes más rápidos que la animación (con margen por el jitter de red).
            if (Time.time - lastServerPunchTime < punchDuration * 0.75f) return;
            lastServerPunchTime = Time.time;

            Vector3 toVictim = victim.transform.position - transform.position;
            toVictim.y = 0f;
            float maxDistance = punchRange + punchRadius + (capsule != null ? capsule.radius * 2f : 1f) + hitValidationTolerance;
            if (toVictim.magnitude > maxDistance) return;

            Vector3 direction = toVictim.sqrMagnitude > 0.0001f ? toVictim.normalized : transform.forward;
            victim.ServerApplyHit(direction * punchKnockback, this);
        }

        // ---------------- Efectos de objetos ----------------

        /// <summary>
        /// Solo servidor. Aturde temporalmente al jugador sin contar
        /// como un golpe del combo.
        /// </summary>
        public void ServerApplyItemStun(float duration)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerApplyItemStun solo se puede llamar en el servidor.", this);
                return;
            }

            if (duration <= 0f) return;

            ItemStunRpc(duration);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ItemStunRpc(float duration)
        {
            // Cancela cualquier golpe que estuviera preparando.
            punchHitTimer = -1f;

            // Detenemos inmediatamente el movimiento.
            moveVelocity = Vector3.zero;
            desiredVelocity = Vector3.zero;
            desiredDirection = Vector3.zero;
            knockbackVelocity = Vector3.zero;

            if (!body.isKinematic)
            {
                Vector3 velocity = body.linearVelocity;
                body.linearVelocity = new Vector3(0f, velocity.y, 0f);
            }

            // No cuenta para el combo de puñetazos.
            stunInvulnerable = false;
            recoveryInvulnerabilityTimer = 0f;
            pendingRecoveryInvulnerability = 0f;

            itemRecoveryProgress = 0f;
            itemRecoveryRequired = true;

            StartAction(PlayerState.HitStun, duration);
        }

        // ---------------- Recibir golpe ----------------

        /// <summary>
        /// Solo servidor. Punto de entrada para CUALQUIER cosa que golpee al jugador (puñetazo,
        /// objetos lanzados...). Se ignora si está en invincibility-frames.
        /// </summary>
        public void ServerApplyHit(Vector3 knockback, PlayerController attacker = null)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerApplyHit solo se puede llamar en el servidor.", this);
                return;
            }
            if (IsInvulnerable) return;

            ReceiveHitRpc(knockback, attacker != null ? attacker.NetworkObjectId : ulong.MaxValue);
        }

        // El golpe lo resuelve el dueño de la víctima: tiene la información más reciente de sus
        // i-frames (el valor que ve el servidor llega con latencia), así el dash se siente justo.
        // También lleva la cuenta del combo, así nunca se desincroniza con lo que ve el jugador.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void ReceiveHitRpc(Vector3 knockback, ulong attackerObjectId)
        {
            if (IsInvulnerable) return;

            // Combo: vuelve a 0 si ha pasado demasiado desde el último golpe.
            if (Time.time - lastHitTime > comboResetTime) comboHits = 0;
            lastHitTime = Time.time;
            comboHits++;
            int hitNumber = comboHits;
            bool heavy = comboHits >= hitsForHeavy;
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

            punchHitTimer = -1f; // Un golpe recibido cancela el tuyo.
            stunInvulnerable = reaction.invulnerableWhileStunned;
            recoveryInvulnerabilityTimer = 0f;
            pendingRecoveryInvulnerability = reaction.recoveryInvulnerability;
            StartAction(heavy ? PlayerState.Knockdown : PlayerState.HitStun, reaction.stunDuration);
            UpdateInvulnerability(); // El derribo es invulnerable desde este mismo frame.
            HitReactedRpc(attackerObjectId, (byte)hitNumber, heavy);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
        void HitReactedRpc(ulong attackerObjectId, byte hitNumber, bool heavy)
        {
            PlayerController attacker = null;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerObjectId, out NetworkObject attackerObject))
                attackerObject.TryGetComponent(out attacker);

            debugComboHits = hitNumber;
            debugLastHitTime = Time.time;

            onHitReceived?.Invoke(attacker);
            if (heavy) onHeavyHit?.Invoke(attacker);
            else onLightHit?.Invoke(attacker, hitNumber);
        }

        // Rangos configurados (con el objeto seleccionado, también fuera de Play).
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, interactRadius);
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
                Gizmos.DrawWireSphere(transform.position, interactRadius);
            }

            if (State == PlayerState.Punch)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
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
