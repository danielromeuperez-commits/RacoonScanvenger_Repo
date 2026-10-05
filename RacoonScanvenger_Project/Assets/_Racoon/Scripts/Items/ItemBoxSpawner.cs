using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    public class ItemBox : NetworkBehaviour
    {
        [Header("Aparición")]
        [SerializeField] bool startVisible = true;

        [Tooltip("Segundos que tarda en aparecer al empezar la partida.")]
        [SerializeField, Min(0f)] float spawnDelay = 0f;

        [Header("Animación")]
        [SerializeField] Transform animatedVisual;
        [SerializeField] float rotationSpeed = 90f;
        [SerializeField] float bobHeight = 0.25f;
        [SerializeField] float bobSpeed = 1f;

        [Header("Pickup")]
        [SerializeField] Collider pickupCollider;
        [SerializeField] float messageDuration = 2f;

        readonly NetworkVariable<bool> available = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        Renderer[] renderers;
        Vector3 initialLocalPosition;
        float messageTimer;

        void Awake()
        {
            if (pickupCollider == null)
                pickupCollider = GetComponent<Collider>();

            if (animatedVisual == null)
                animatedVisual = transform;

            renderers = GetComponentsInChildren<Renderer>(true);
            initialLocalPosition = animatedVisual.localPosition;
        }

        void Reset()
        {
            pickupCollider = GetComponent<Collider>();

            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }

        public override void OnNetworkSpawn()
        {
            available.OnValueChanged += OnAvailabilityChanged;

            ApplyAvailability(available.Value);

            if (IsServer)
            {
                if (startVisible)
                {
                    available.Value = true;
                }
                else if (spawnDelay <= 0f)
                {
                    available.Value = true;
                }
                else
                {
                    Invoke(nameof(ServerShowBox), spawnDelay);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            available.OnValueChanged -= OnAvailabilityChanged;
            CancelInvoke();
        }

        void Update()
        {
            AnimateBox();

            if (messageTimer > 0f)
                messageTimer -= Time.deltaTime;
        }

        void AnimateBox()
        {
            if (animatedVisual == null || !available.Value)
                return;

            animatedVisual.Rotate(
                Vector3.up,
                rotationSpeed * Time.deltaTime,
                Space.Self
            );

            float offset =
                Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f)
                * bobHeight;

            Vector3 position = initialLocalPosition;
            position.y += offset;

            animatedVisual.localPosition = position;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !available.Value)
                return;

            PlayerController player =
                other.GetComponentInParent<PlayerController>();

            if (player == null)
                return;

            // La caja desaparece para todos.
            available.Value = false;

            ApplyAvailability(false);

            // Mensaje solo para quien la recoge.
            ShowMessageClientRpc(
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { player.OwnerClientId }
                    }
                }
            );
        }

        void ServerShowBox()
        {
            if (!IsServer)
                return;

            available.Value = true;
        }

        [ClientRpc]
        void ShowMessageClientRpc(ClientRpcParams rpcParams = default)
        {
            messageTimer = messageDuration;
        }

        void OnAvailabilityChanged(bool previousValue, bool newValue)
        {
            ApplyAvailability(newValue);
        }

        void ApplyAvailability(bool isAvailable)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = isAvailable;
            }

            if (pickupCollider != null)
                pickupCollider.enabled = isAvailable;
        }

        void OnGUI()
        {
            if (messageTimer <= 0f)
                return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 32,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            GUI.Label(
                new Rect(0, 50, Screen.width, 60),
                "Item box obtenida",
                style
            );
        }
    }
}