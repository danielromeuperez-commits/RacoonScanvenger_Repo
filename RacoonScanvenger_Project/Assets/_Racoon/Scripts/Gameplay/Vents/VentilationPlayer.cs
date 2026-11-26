using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController))]
    public class VentilationPlayer : NetworkBehaviour
    {
        [SerializeField] Transform visualRoot;

        [Header("Movimiento suave entre vents")]
        [SerializeField, Min(0.05f)] float ventMoveDuration = 0.22f;

        [Header("Cooldowns de ventilación (segundos)")]
        [Tooltip("Tiempo de espera tras entrar o salir antes de poder volver a usar una vent. 0 = sin cooldown.")]
        [SerializeField, Min(0f)] public float ventInteractCooldown = 10f;

        [Tooltip("Tiempo máximo que puedes permanecer dentro de una vent antes de salir automáticamente. 0 = sin límite.")]
        [SerializeField, Min(0f)] public float maxInsideVentDuration = 10f;

        PlayerController player;
        PlayerInputHandler input;

        readonly HashSet<VentilationPoint> nearby = new HashSet<VentilationPoint>();
        VentilationPoint currentVent;
        int lastHorizontalDirection;

        float interactCooldownTimer;
        float insideVentTimer;

        bool isMovingBetweenVents;
        float ventMoveTimer;
        Vector3 ventMoveStartPos;
        Vector3 ventMoveTargetPos;
        Quaternion ventMoveStartRot;
        Quaternion ventMoveTargetRot;

        readonly NetworkVariable<bool> insideVent = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        Renderer[] visualRenderers;
        bool[] originalRendererStates;

        public bool IsInsideVent => currentVent != null;
        public bool IsInsideVentSynced => insideVent.Value;
        public float VentInteractCooldownRemaining => Mathf.Max(0f, interactCooldownTimer);
        public float InsideVentRemaining => maxInsideVentDuration > 0f && currentVent != null
            ? Mathf.Max(0f, maxInsideVentDuration - insideVentTimer)
            : 0f;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            input = GetComponent<PlayerInputHandler>();
            if (visualRoot == null) visualRoot = transform;
            CacheRenderers();
        }

        void CacheRenderers()
        {
            visualRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            originalRendererStates = new bool[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++)
                originalRendererStates[i] = visualRenderers[i] != null && visualRenderers[i].enabled;
        }

        public override void OnNetworkSpawn()
        {
            insideVent.OnValueChanged += OnInsideVentChanged;
            ApplyVisibility(insideVent.Value);
        }

        public override void OnNetworkDespawn()
        {
            insideVent.OnValueChanged -= OnInsideVentChanged;
            nearby.Clear();
            currentVent = null;
            isMovingBetweenVents = false;
            interactCooldownTimer = 0f;
            insideVentTimer = 0f;
            if (player != null && IsOwner) player.SetVentilationLocked(false);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            float dt = Time.deltaTime;
            if (interactCooldownTimer > 0f)
                interactCooldownTimer = Mathf.Max(0f, interactCooldownTimer - dt);

            if (currentVent == null) return;

            if (maxInsideVentDuration > 0f)
            {
                insideVentTimer += dt;
                if (insideVentTimer >= maxInsideVentDuration)
                {
                    ExitVent(triggerInteractCooldown: true);
                    return;
                }
            }

            if (isMovingBetweenVents)
            {
                ventMoveTimer += dt;
                float rawT = Mathf.Clamp01(ventMoveTimer / ventMoveDuration);
                float smoothT = rawT * rawT * (3f - 2f * rawT);

                Vector3 pos = Vector3.Lerp(ventMoveStartPos, ventMoveTargetPos, smoothT);
                Quaternion rot = Quaternion.Slerp(ventMoveStartRot, ventMoveTargetRot, smoothT);
                player.Teleport(pos, rot);

                if (rawT >= 1f)
                {
                    isMovingBetweenVents = false;
                    player.Teleport(ventMoveTargetPos, ventMoveTargetRot);
                }
                return;
            }

            if (input == null) return;

            float horizontal = input.Move.x;
            int direction = horizontal > 0.6f ? 1 : (horizontal < -0.6f ? -1 : 0);

            if (direction != 0 && direction != lastHorizontalDirection)
            {
                lastHorizontalDirection = direction;
                MoveVent(direction);
            }
            else if (direction == 0)
            {
                lastHorizontalDirection = 0;
            }
        }

        public void RegisterNearby(VentilationPoint vent) { if (vent != null) nearby.Add(vent); }
        public void UnregisterNearby(VentilationPoint vent) { if (vent != null) nearby.Remove(vent); }

        public bool TryHandleInteract()
        {
            if (!IsSpawned || !IsOwner) return false;

            if (currentVent != null)
            {
                if (interactCooldownTimer > 0f) return true;
                ExitVent(triggerInteractCooldown: true);
                return true;
            }

            if (interactCooldownTimer > 0f) return false;

            VentilationPoint closest = GetClosestVent();
            if (closest == null) return false;

            EnterVent(closest);
            return true;
        }

        VentilationPoint GetClosestVent()
        {
            nearby.RemoveWhere(v => v == null);
            VentilationPoint closest = null;
            float bestDistSqr = float.MaxValue;

            foreach (VentilationPoint vent in nearby)
            {
                float d = (transform.position - vent.transform.position).sqrMagnitude;
                if (d < bestDistSqr) { bestDistSqr = d; closest = vent; }
            }
            if (closest != null) return closest;

            foreach (VentilationPoint vent in FindObjectsByType<VentilationPoint>(FindObjectsSortMode.None))
            {
                if (vent == null) continue;
                float r = Mathf.Max(0.5f, vent.FallbackInteractRadius);
                float d = (transform.position - vent.transform.position).sqrMagnitude;
                if (d <= r * r && d < bestDistSqr) { bestDistSqr = d; closest = vent; }
            }
            return closest;
        }

        void EnterVent(VentilationPoint vent)
        {
            if (vent == null) return;
            currentVent = vent;
            insideVentTimer = 0f;
            interactCooldownTimer = ventInteractCooldown;
            isMovingBetweenVents = false;
            lastHorizontalDirection = 0;
            player.SetVentilationLocked(true);
            player.Teleport(vent.InsidePosition, vent.InsideRotation);
            SetInsideVentServerRpc(true);
        }

        void MoveVent(int direction)
        {
            if (currentVent == null) return;
            VentilationPoint next = direction < 0 ? currentVent.left : currentVent.right;
            if (next == null) return;

            currentVent = next;
            isMovingBetweenVents = true;
            ventMoveTimer = 0f;
            ventMoveStartPos = transform.position;
            ventMoveTargetPos = next.InsidePosition;
            ventMoveStartRot = transform.rotation;
            ventMoveTargetRot = next.InsideRotation;
        }

        void ExitVent(bool triggerInteractCooldown = true)
        {
            VentilationPoint vent = currentVent;
            currentVent = null;
            insideVentTimer = 0f;
            isMovingBetweenVents = false;
            lastHorizontalDirection = 0;

            if (triggerInteractCooldown)
                interactCooldownTimer = ventInteractCooldown;

            if (vent != null)
                player.Teleport(vent.ExitPosition, vent.ExitRotation);

            player.SetVentilationLocked(false);
            SetInsideVentServerRpc(false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SetInsideVentServerRpc(bool value)
        {
            if (IsServer) insideVent.Value = value;
        }

        void OnInsideVentChanged(bool previous, bool current) => ApplyVisibility(current);

        void ApplyVisibility(bool hidden)
        {
            if (visualRenderers == null || visualRenderers.Length == 0) CacheRenderers();
            for (int i = 0; i < visualRenderers.Length; i++)
            {
                if (visualRenderers[i] != null)
                    visualRenderers[i].enabled = !hidden && (i < originalRendererStates.Length ? originalRendererStates[i] : true);
            }
        }
    }
}