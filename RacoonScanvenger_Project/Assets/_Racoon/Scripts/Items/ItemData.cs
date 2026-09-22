using Racoon.Player;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Definición de un objeto (estilo Mario Kart). Hereda de esta clase para crear objetos
    /// con efectos propios sobrescribiendo ServerUse.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Items/Item Data", fileName = "ITEM_New")]
    public class ItemData : ScriptableObject
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("Modelo que se ve en la mano cuando está equipado (opcional).")]
        public GameObject heldVisualPrefab;

        /// <summary>
        /// Se ejecuta SOLO en el servidor cuando el jugador usa el objeto.
        /// Aquí va la lógica real del objeto (spawnear un proyectil, aplicar un buff...).
        /// </summary>
        public virtual void ServerUse(PlayerController user)
        {
            Debug.Log($"{user.name} ha usado {displayName}");
        }
    }
}
