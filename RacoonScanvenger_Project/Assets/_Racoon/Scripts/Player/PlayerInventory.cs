using System;
using Racoon.Items;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Inventario estilo Mario Kart. El SERVIDOR es la autoridad: solo él añade, quita o cambia
    /// el objeto equipado. Los clientes leen el estado replicado (NetworkList / NetworkVariable).
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        public const int NoSlot = -1;

        [SerializeField] ItemDatabase database;
        [SerializeField, Min(1)] int maxItems = 3;
        [Tooltip("Si no llevas nada equipado y coges un objeto, se equipa solo.")]
        [SerializeField] bool autoEquipOnPickup = true;
        [Tooltip("Al gastar el objeto equipado, equipa el siguiente automáticamente.")]
        [SerializeField] bool autoEquipNextAfterUse = false;

        // Ids (índices en ItemDatabase) de los objetos que lleva el jugador.
        NetworkList<int> items;
        // Posición en 'items' del objeto equipado, o NoSlot si va con las manos vacías.
        readonly NetworkVariable<int> equippedSlot = new(NoSlot);

        /// <summary>Se lanza en todos los clientes cuando cambia el objeto equipado (null = manos vacías).</summary>
        public event Action<ItemData> EquippedItemChanged;
        /// <summary>Se lanza en todos los clientes cuando cambia el contenido del inventario.</summary>
        public event Action InventoryChanged;

        public ItemDatabase Database => database;
        public int Count => items.Count;
        public int MaxItems => maxItems;
        public bool IsFull => items.Count >= maxItems;
        public int EquippedSlot => equippedSlot.Value;
        public bool HasEquipped => equippedSlot.Value >= 0 && equippedSlot.Value < items.Count;
        public ItemData EquippedItem => HasEquipped ? database.Get(items[equippedSlot.Value]) : null;

        /// <summary>
        /// ¿Tiene efecto pulsar "cambiar objeto"? Equipa si vas con las manos vacías y tienes algo,
        /// o pasa al siguiente si tienes más de uno. Nunca desequipa el único objeto que llevas.
        /// </summary>
        public bool CanSwitch => HasEquipped ? items.Count > 1 : items.Count > 0;

        public ItemData GetItemAt(int slot) => slot >= 0 && slot < items.Count ? database.Get(items[slot]) : null;

        void Awake()
        {
            // Las NetworkList deben crearse en Awake (no en la declaración).
            items = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            items.OnListChanged += OnItemsChanged;
            equippedSlot.OnValueChanged += OnEquippedSlotChanged;
            NotifyChanged();
        }

        public override void OnNetworkDespawn()
        {
            items.OnListChanged -= OnItemsChanged;
            equippedSlot.OnValueChanged -= OnEquippedSlotChanged;
        }

        void OnItemsChanged(NetworkListEvent<int> _) => NotifyChanged();
        void OnEquippedSlotChanged(int _, int __) => NotifyChanged();

        void NotifyChanged()
        {
            // Lista y slot pueden llegar en el mismo tick y disparar dos callbacks seguidos;
            // siempre leemos el estado actual, así el último aviso es el correcto.
            InventoryChanged?.Invoke();
            EquippedItemChanged?.Invoke(EquippedItem);
        }

        // ---------------- Solo servidor ----------------

        public bool ServerTryAddItem(ItemData item)
        {
            if (!IsServer || IsFull) return false;

            int id = database.GetId(item);
            if (id < 0)
            {
                Debug.LogWarning($"{item} no está en la ItemDatabase", this);
                return false;
            }

            items.Add(id);
            if (autoEquipOnPickup && !HasEquipped) equippedSlot.Value = items.Count - 1;
            return true;
        }

        public bool ServerTrySwitch()
        {
            if (!IsServer || !CanSwitch) return false;
            equippedSlot.Value = HasEquipped ? (equippedSlot.Value + 1) % items.Count : 0;
            return true;
        }

        /// <summary>Quita el objeto equipado del inventario y lo devuelve (null si no había).</summary>
        public ItemData ServerConsumeEquipped()
        {
            if (!IsServer || !HasEquipped) return null;

            int slot = equippedSlot.Value;
            ItemData item = database.Get(items[slot]);
            items.RemoveAt(slot);
            equippedSlot.Value = autoEquipNextAfterUse && items.Count > 0 ? slot % items.Count : NoSlot;
            return item;
        }
    }
}
