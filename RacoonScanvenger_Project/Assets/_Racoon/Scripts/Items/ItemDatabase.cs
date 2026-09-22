using System.Collections.Generic;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Lista de todos los objetos del juego. Por red solo se envía el índice (id) del objeto,
    /// por eso servidor y clientes deben usar exactamente el mismo asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Items/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabase : ScriptableObject
    {
        [SerializeField] List<ItemData> items = new();

        public int Count => items.Count;

        public ItemData Get(int id) => id >= 0 && id < items.Count ? items[id] : null;

        public int GetId(ItemData item) => items.IndexOf(item);
    }
}
