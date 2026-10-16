using Racoon.Items;
using Racoon.Player;
using UnityEngine;

public class SellPoint : MonoBehaviour, IInteractable
{
    /// <summary>
    /// Solo permite interactuar si el jugador lleva
    /// algún objeto pendiente de vender.
    /// </summary>
    public bool CanInteract(PlayerController player)
    {
        if (player == null)
            return false;

        if (!player.TryGetComponent(out PlayerLoot playerLoot))
            return false;

        return playerLoot.PendingPoints > 0;
    }

    /// <summary>
    /// Vende todo el contenido actual de la bolsa.
    /// </summary>
    public void Interact(PlayerController player)
    {
        if (player == null || !player.IsServer)
            return;

        PlayerLoot playerLoot =
            player.GetComponent<PlayerLoot>();

        if (playerLoot == null)
            return;

        playerLoot.ServerSellLoot();
    }
}