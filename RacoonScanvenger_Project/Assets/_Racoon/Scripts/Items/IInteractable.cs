using Racoon.Player;

namespace Racoon.Items
{
    /// <summary>
    /// Implementa esto en cualquier objeto con el que se pueda interactuar (botón sur).
    /// Ambos métodos se llaman SOLO en el servidor.
    /// </summary>
    public interface IInteractable
    {
        bool CanInteract(PlayerController player);
        void Interact(PlayerController player);
    }
}
