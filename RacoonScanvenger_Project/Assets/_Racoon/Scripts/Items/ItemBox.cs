using Racoon.Player;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Ejemplo de IInteractable para probar el inventario: al interactuar da un objeto aleatorio.
    /// Solo necesita un Collider. No es un objeto de red: toda su lógica corre en el servidor
    /// y el resultado llega a los clientes a través del inventario replicado del jugador.
    /// </summary>
    public class ItemBox : MonoBehaviour, IInteractable
    {
        [SerializeField] ItemData[] possibleItems;
        [SerializeField] float cooldown = 3f;

        float nextAvailableTime;

        public bool CanInteract(PlayerController player) =>
            Time.time >= nextAvailableTime && possibleItems.Length > 0 && !player.Inventory.IsFull;

        public void Interact(PlayerController player)
        {
            ItemData item = possibleItems[Random.Range(0, possibleItems.Length)];
            if (player.Inventory.ServerTryAddItem(item))
                nextAvailableTime = Time.time + cooldown;
        }
    }
}
