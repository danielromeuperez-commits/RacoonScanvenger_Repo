using Racoon.Player;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Definición de un objeto (estilo Mario Kart). Todos los objetos usan este mismo SO:
    /// lo que hace cada uno se elige en el desplegable "Effect" (ver ItemEffect).
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Items/Item Data", fileName = "ITEM_New")]
    public class ItemData : ScriptableObject
    {
        public string displayName;
        public Sprite icon;
        [Tooltip("Modelo que se ve en la mano cuando está equipado (opcional).")]
        public GameObject heldVisualPrefab;

        [Tooltip("Qué hace el objeto al usarse.")]
        [SerializeReference, SubclassSelector] ItemEffect effect;

        public ItemEffect Effect => effect;

        /// <summary>
        /// Se ejecuta SOLO en el servidor cuando el jugador usa el objeto.
        /// </summary>
        public void ServerUse(PlayerController user)
        {
            if (effect == null)
            {
                Debug.Log($"{user.name} ha usado {displayName} (sin efecto)");
                return;
            }

            effect.ServerUse(user, this);
        }
    }
}
