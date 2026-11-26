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

        PlayerController player;
        PlayerInputHandler input;

        readonly HashSet<VentilationPoint> nearby = new HashSet<VentilationPoint>();
        VentilationPoint currentVent;
        int lastHorizontalDirection;

        readonly NetworkVariable<bool> insideVent = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        Renderer[] visualRenderers;
        bool[] originalRendererStates;

        public bool IsInsideVent => currentVent != null;

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
            if (player != null && IsOwner) player.SetVentilationLocked(false);
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner || currentVent == null || input == null) return;

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
                ExitVent();
                return true;
            }

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
            player.Teleport(next.InsidePosition, next.InsideRotation);
        }

        void ExitVent()
        {
            if (currentVent == null) return;
            VentilationPoint vent = currentVent;
            currentVent = null;
            lastHorizontalDirection = 0;
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