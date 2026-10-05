using UnityEngine;
using Unity.Netcode;

namespace Racoon.Player
{
    /// <summary>
    /// Gestiona el uso de los objetos del jugador.
    /// No modifica PlayerController.
    /// </summary>
    public class PlayerItemHandler : NetworkBehaviour
    {
        [Header("Referencias")]
        [SerializeField] PlayerInventory inventory;
        [SerializeField] PlayerInputHandler inputHandler;

        [Header("Puntos del jugador")]
        [Tooltip("Punto donde se verá el objeto equipado.")]
        [SerializeField] Transform itemHoldPoint;

        [Tooltip("Punto desde donde se lanzarán los objetos.")]
        [SerializeField] Transform throwPoint;

        [Header("Apuntado")]
        [Tooltip("Flecha que aparece mientras mantenemos pulsado Use.")]
        [SerializeField] GameObject aimArrow;

        GameObject currentHeldVisual;

        void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventory>();

            if (inputHandler == null)
                inputHandler = GetComponent<PlayerInputHandler>();

            if (aimArrow != null)
                aimArrow.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            inventory.EquippedItemChanged += OnEquippedItemChanged;
            inputHandler.UseStarted += OnUseStarted;
            inputHandler.UseReleased += OnUseReleased;

            OnEquippedItemChanged(inventory.EquippedItem);
        }

        public override void OnNetworkDespawn()
        {
            inventory.EquippedItemChanged -= OnEquippedItemChanged;
            inputHandler.UseStarted -= OnUseStarted;
            inputHandler.UseReleased -= OnUseReleased;
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

        void OnUseStarted()
        {
            if (!IsOwner)
                return;

            if (inventory == null || !inventory.HasEquipped)
                return;

            if (aimArrow != null)
                aimArrow.SetActive(true);
        }

        void OnUseReleased()
        {
            if (!IsOwner)
                return;

            if (!inventory.HasEquipped)
                return;

            if (aimArrow != null)
                aimArrow.SetActive(false);

            // El lanzamiento lo añadiremos en el siguiente paso.
        }

        void Update()
        {
            if (!IsOwner)
                return;

            UpdateAimArrow();
        }

        void UpdateAimArrow()
        {
            if (aimArrow == null || !aimArrow.activeSelf)
                return;

            if (throwPoint == null)
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