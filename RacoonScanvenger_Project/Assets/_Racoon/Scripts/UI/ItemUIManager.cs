using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;

public class ItemUIManager : MonoBehaviour
{
    [Header("Player UI")]
    [SerializeField] private ItemUIController player1UI;
    [SerializeField] private ItemUIController player2UI;

    // Jugadores que actualmente existen en la partida.
    private readonly List<PlayerInventory> players = new();

    /// <summary>
    /// Registra un jugador cuando aparece en la red.
    /// </summary>
    public void RegisterPlayer(PlayerInventory inventory)
    {
        if (inventory == null || players.Contains(inventory))
            return;

        players.Add(inventory);

        SortPlayers();
        RefreshBindings();
    }

    /// <summary>
    /// Elimina un jugador cuando desaparece de la red.
    /// </summary>
    public void UnregisterPlayer(PlayerInventory inventory)
    {
        if (inventory == null)
            return;

        players.Remove(inventory);

        SortPlayers();
        RefreshBindings();
    }

    /// <summary>
    /// Ordenamos los jugadores para que Player 1 y Player 2
    /// mantengan siempre la misma posición en la interfaz.
    /// </summary>
    private void SortPlayers()
    {
        players.Sort((a, b) =>
        {
            // Primero intentamos ordenar por OwnerClientId.
            int ownerComparison = a.OwnerClientId.CompareTo(b.OwnerClientId);

            if (ownerComparison != 0)
                return ownerComparison;

            // Si ambos pertenecen al mismo cliente,
            // utilizamos el NetworkObjectId como desempate.
            return a.NetworkObjectId.CompareTo(b.NetworkObjectId);
        });
    }

    /// <summary>
    /// Conecta cada inventario con su correspondiente UI.
    /// </summary>
    private void RefreshBindings()
    {
        if (player1UI != null)
        {
            if (players.Count > 0)
                player1UI.Bind(players[0]);
            else
                player1UI.Bind(null);
        }

        if (player2UI != null)
        {
            if (players.Count > 1)
                player2UI.Bind(players[1]);
            else
                player2UI.Bind(null);
        }
    }
}