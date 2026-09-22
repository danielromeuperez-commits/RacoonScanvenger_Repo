using System;
using Racoon.Cameras;
using Racoon.Gameplay;
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
    ///  - DUEÑO (el cliente que controla este mapache): lee input, mueve el CharacterController,
    ///    gasta estamina y escribe el estado. Su NetworkTransform (modo Owner) replica la posición.
    ///  - SERVIDOR: valida y ejecuta lo que afecta a la partida (inventario, golpes, interacciones).
    ///  - RESTO DE CLIENTES: solo leen el estado replicado para animar y mostrar la barra.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler), typeof(PlayerInventory))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("Movimiento")]
        [SerializeField] float walkSpeed = 3.5f;
        [SerializeField] float runSpeed = 6.5f;
        [SerializeField] float acceleration = 30f;
        [SerializeField] float rotationSpeed = 720f;
        [SerializeField] float gravity = -25f;
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
        [SerializeField] float punchKnockback = 8f;
        [SerializeField] float knockbackDamping = 6f;
        [Tooltip("Margen extra que acepta el servidor al validar la distancia del golpe (latencia).")]
        [SerializeField] float hitValidationTolerance = 1.5f;
        [SerializeField] LayerMask hitMask = ~0;

        [Header("Cámara (Target Group)")]
        [SerializeField] float cameraWeight = 1f;
        [SerializeField] float cameraRadius = 1.5f;

        [Header("Eventos (se lanzan en TODOS los clientes)")]
        public UnityEvent<PlayerController> onInteract;
        public UnityEvent<ItemData> onItemUsed;
        /// <summary>Parámetro: quien golpea (puede ser null).</summary>
        public UnityEvent<PlayerController> onHitReceived;

        /// <summary>Estado + contador de acciones en una sola variable, para que lleguen juntos.</summary>
        public struct NetState : INetworkSerializeByMemcpy, IEquatable<NetState>
        {
            public PlayerState State;
            // Se incrementa en cada acción: así se detectan dos golpes seguidos aunque el
            // estado no pase visiblemente por Idle entre ellos.
            public byte ActionSequence;

            public bool Equals(NetState other) => State == other.State && ActionSequence == other.ActionSequence;
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
        public float StaminaNormalized => staminaNormalized.Value;
        /// <summary>Solo tiene sentido en el dueño.</summary>
        public bool IsExhausted => exhausted;
        public PlayerInventory Inventory => inventory;

        static readonly Collider[] overlapBuffer = new Collider[16];

        CharacterController characterController;
        PlayerInputHandler input;
        PlayerInventory inventory;
        NetworkTransform networkTransform;
        Transform cameraTransform;

        Vector3 horizontalVelocity;
        Vector3 knockbackVelocity;
        float verticalVelocity;
        float stamina;
        float staminaRegenTimer;
        bool exhausted;
        float actionTimer;
        float punchHitTimer = -1f;

        bool IsInAction => actionTimer > 0f;

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
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

            input.SetInputEnabled(IsOwner);
            if (!IsOwner) return;

            Local = this;
            stamina = maxStamina;
            staminaNormalized.Value = 1f;
            input.InteractPressed += OnInteractPressed;
            input.SwitchItemPressed += OnSwitchItemPressed;
            input.UsePressed += OnUsePressed;
            LocalPlayerSpawned?.Invoke(this);
        }

        protected override void OnNetworkPostSpawn()
        {
            // En PostSpawn el NetworkTransform ya está listo para teletransportar.
            if (IsOwner && PlayerSpawnPoints.TryGetPose(OwnerClientId, out Pose pose))
                Teleport(pose.position, pose.rotation);
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
            if (punchHitTimer >= 0f)
            {
                punchHitTimer -= dt;
                if (punchHitTimer < 0f) OwnerCheckPunchHit();
            }

            Vector2 move = input.Move;
            bool hasMoveInput = move.sqrMagnitude > moveDeadzone * moveDeadzone;
            bool isRunning = hasMoveInput && input.RunHeld && !IsInAction && !exhausted && stamina > 0f;

            UpdateStamina(isRunning, dt);
            UpdateMovement(hasMoveInput ? move : Vector2.zero, isRunning, dt);

            if (!IsInAction)
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

        void UpdateMovement(Vector2 move, bool isRunning, float dt)
        {
            Vector3 direction = GetCameraRelativeDirection(move);

            float targetSpeed = (isRunning ? runSpeed : walkSpeed) * move.magnitude;
            if (IsInAction) targetSpeed *= actionMoveMultiplier;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, direction * targetSpeed, acceleration * dt);

            if (characterController.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += gravity * dt;

            knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 1f - Mathf.Exp(-knockbackDamping * dt));

            Vector3 velocity = horizontalVelocity + knockbackVelocity + Vector3.up * verticalVelocity;
            characterController.Move(velocity * dt);

            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * dt);
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

        void Teleport(Vector3 position, Quaternion rotation)
        {
            // El CharacterController ignora cambios directos de posición mientras está activo.
            characterController.enabled = false;
            if (networkTransform != null && networkTransform.CanCommitToTransform)
                networkTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
            characterController.enabled = true;

            horizontalVelocity = knockbackVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        // ---------------- Input (solo dueño) ----------------

        void OnInteractPressed()
        {
            if (IsInAction) return;
            StartAction(PlayerState.Interact, interactDuration);
            InteractRpc();
        }

        void OnSwitchItemPressed()
        {
            // CanSwitch evita desequipar el único objeto que llevas.
            if (IsInAction || !inventory.CanSwitch) return;
            StartAction(PlayerState.SwitchItem, switchItemDuration);
            SwitchItemRpc();
        }

        void OnUsePressed()
        {
            if (IsInAction) return;

            if (inventory.HasEquipped)
            {
                StartAction(PlayerState.UseItem, useItemDuration);
                UseItemRpc();
            }
            else
            {
                StartAction(PlayerState.Punch, punchDuration);
                punchHitTimer = punchHitDelay;
            }
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
        void OwnerCheckPunchHit()
        {
            Vector3 center = transform.position + Vector3.up * punchHeight + transform.forward * punchRange;
            int count = Physics.OverlapSphereNonAlloc(center, punchRadius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerController victim = overlapBuffer[i].GetComponentInParent<PlayerController>();
                if (victim == null || victim == this) continue;

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

            Vector3 toVictim = victim.transform.position - transform.position;
            toVictim.y = 0f;
            float maxDistance = punchRange + punchRadius + characterController.radius * 2f + hitValidationTolerance;
            if (toVictim.magnitude > maxDistance) return;

            Vector3 direction = toVictim.sqrMagnitude > 0.0001f ? toVictim.normalized : transform.forward;
            victim.ReceiveHitRpc(direction * punchKnockback, NetworkObjectId);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ReceiveHitRpc(Vector3 knockback, ulong attackerObjectId)
        {
            // Solo el dueño mueve su personaje, así que es él quien aplica el empujón.
            if (IsOwner) knockbackVelocity += knockback;

            PlayerController attacker = null;
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(attackerObjectId, out NetworkObject attackerObject))
                attackerObject.TryGetComponent(out attacker);
            onHitReceived?.Invoke(attacker);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * punchHeight + transform.forward * punchRange, punchRadius);
        }
    }
}
