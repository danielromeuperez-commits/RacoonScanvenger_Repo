using UnityEngine;
using Unity.Netcode;

namespace Racoon.Player
{
    /// <summary>
    /// Parte visual del uso de objetos: el modelo en la mano y la flecha de apuntado.
    /// Cuándo se usa el objeto lo decide PlayerController (mantener Usar = apuntar, soltar = usar);
    /// aquí solo se lee <see cref="PlayerController.IsAimingItem"/>.
    /// </summary>
    public class PlayerItemHandler : NetworkBehaviour
    {
        [Header("Referencias")]
        [SerializeField] PlayerInventory inventory;
        [SerializeField] PlayerController controller;

        [Header("Puntos del jugador")]
        [Tooltip("Punto donde se verá el objeto equipado.")]
        [SerializeField] Transform itemHoldPoint;

        [Tooltip("Punto desde donde se lanzarán los objetos.")]
        [SerializeField] Transform throwPoint;

        [Header("Apuntado")]
        [Tooltip("Flecha que aparece mientras mantenemos pulsado Use con un objeto.")]
        [SerializeField] GameObject aimArrow;

        [Tooltip("Segundos manteniendo Use antes de enseñar la flecha. Así un toque rápido no la hace parpadear.")]
        [SerializeField, Min(0f)] float aimArrowDelay = 0.12f;

        GameObject currentHeldVisual;
        float aimHeldTime;

        void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventory>();

            if (controller == null)
                controller = GetComponent<PlayerController>();

            if (aimArrow != null)
                aimArrow.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            inventory.EquippedItemChanged += OnEquippedItemChanged;

            OnEquippedItemChanged(inventory.EquippedItem);
        }

        public override void OnNetworkDespawn()
        {
            inventory.EquippedItemChanged -= OnEquippedItemChanged;
        }

        void OnEquippedItemChanged(Items.ItemData item)
        {
            RemoveHeldVisual();

            if (item == null || item.heldVisualPrefab == null)
                return;

            if (itemHoldPoint == null)
                return;

            currentHeldVisual = Instantiate(
                item.heldVisualPrefab,
                itemHoldPoint
            );

            currentHeldVisual.transform.localPosition = Vector3.zero;
            currentHeldVisual.transform.localRotation = Quaternion.identity;
        }

        void Update()
        {
            if (!IsOwner)
                return;

            UpdateAimArrow();
        }

        void UpdateAimArrow()
        {
            if (aimArrow == null)
                return;

            bool aiming = controller != null && controller.IsAimingItem;
            aimHeldTime = aiming ? aimHeldTime + Time.deltaTime : 0f;

            bool show = aiming && aimHeldTime >= aimArrowDelay && throwPoint != null;
            if (aimArrow.activeSelf != show)
                aimArrow.SetActive(show);

            if (!show)
                return;

            aimArrow.transform.position = throwPoint.position;

            Vector3 direction = transform.forward;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                aimArrow.transform.rotation =
                    Quaternion.LookRotation(direction);
            }
        }

        void RemoveHeldVisual()
        {
            if (currentHeldVisual != null)
                Destroy(currentHeldVisual);

            currentHeldVisual = null;
        }
    }
}
