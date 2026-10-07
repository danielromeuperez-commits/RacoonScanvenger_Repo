using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    public class ItemBox : NetworkBehaviour
    {
        [Header("Items")]
        [Tooltip("Objetos que puede entregar esta Item Box.")]
        [SerializeField] ItemData[] possibleItems;

        [Header("Animación")]
        [Tooltip("Objeto visual que gira y sube/baja.")]
        [SerializeField] Transform animatedVisual;

        [Tooltip("Velocidad de giro en grados por segundo.")]
        [SerializeField] float rotationSpeed = 90f;

        [Tooltip("Cuánto sube y baja la caja desde su posición inicial.")]
        [SerializeField] float bobHeight = 0.25f;

        [Tooltip("Velocidad de la animación de subida y bajada, en ciclos por segundo.")]
        [SerializeField] float bobSpeed = 1f;

        [Header("Pickup")]
        [SerializeField] Collider pickupCollider;

        [SerializeField] float messageDuration = 2f;

        NetworkVariable<bool> available = new(
            true,
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

            renderers = GetComponentsInChildren<Renderer>(true);

            if (animatedVisual == null)
                animatedVisual = transform;

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
        }

        public override void OnNetworkDespawn()
        {
            available.OnValueChanged -= OnAvailabilityChanged;
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

            // Giro continuo sobre el eje Y.
            animatedVisual.Rotate(
                Vector3.up,
                rotationSpeed * Time.deltaTime,
                Space.Self
            );

            // Movimiento suave de subida y bajada.
            float offset =
                Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f)
                * bobHeight;

            Vector3 position = initialLocalPosition;
            position.y += offset;

            animatedVisual.localPosition = position;
        }

        void OnTriggerEnter(Collider other)
        {
            // Solo el servidor puede recoger la caja.
            if (!IsServer || !available.Value)
                return;

            PlayerController player =
                other.GetComponentInParent<PlayerController>();

            if (player == null)
                return;

            // Si no hay objetos configurados, no hacemos nada.
            ItemData item = GetRandomItem();

            if (item == null)
            {
                Debug.LogWarning(
                    "ItemBox: no hay ningún ItemData válido en Possible Items.",
                    this
                );

                return;
            }

            // Si el inventario está lleno, la caja tampoco se recoge.
            if (player.Inventory == null || player.Inventory.IsFull)
                return;

            // Intentamos añadir el objeto al inventario.
            if (!player.Inventory.ServerTryAddItem(item))
                return;

            // La caja desaparece para todos.
            available.Value = false;

            // Ocultar inmediatamente en el host.
            ApplyAvailability(false);

            // Mostrar mensaje solamente al jugador que la ha recogido.
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

        ItemData GetRandomItem()
        {
            if (possibleItems == null || possibleItems.Length == 0)
                return null;

            int validCount = 0;

            foreach (ItemData item in possibleItems)
            {
                if (item != null)
                    validCount++;
            }

            if (validCount == 0)
                return null;

            int selectedIndex = Random.Range(0, validCount);

            foreach (ItemData item in possibleItems)
            {
                if (item == null)
                    continue;

                if (selectedIndex == 0)
                    return item;

                selectedIndex--;
            }

            return null;
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