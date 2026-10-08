using Racoon.Items;
using Racoon.Player;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class LootPoint : MonoBehaviour, IInteractable
{
    [Header("Loot")]
    [Tooltip("Puntos que recibe el jugador al saquear este punto.")]
    [SerializeField, Min(0)] int points = 10;

    [Tooltip("Peso que añade este loot a la bolsa del jugador.")]
    [SerializeField, Min(0f)] float weight = 1f;

    [Header("Disponibilidad")]
    [Tooltip("Número de veces que se puede saquear antes de quedarse vacío.")]
    [SerializeField, Min(1)] int usesBeforeEmpty = 1;

    [Tooltip("Segundos que tarda en volver a estar disponible. 0 = no reaparece.")]
    [SerializeField, Min(0f)] float respawnTime = 10f;

    int usesRemaining;
    bool available = true;

    void Awake()
    {
        usesRemaining = usesBeforeEmpty;
    }

    /// <summary>
    /// Devuelve true si este punto puede ser saqueado por el jugador.
    /// </summary>
    public bool CanInteract(PlayerController player)
    {
        if (!available || player == null)
            return false;

        return player.TryGetComponent(out PlayerLoot _);
    }

    /// <summary>
    /// El servidor entrega los puntos y el peso al jugador.
    /// </summary>
    public void Interact(PlayerController player)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer ||
            !CanInteract(player))
            return;

        PlayerLoot playerLoot = player.GetComponent<PlayerLoot>();

        if (!playerLoot.ServerAddLoot(points, weight))
            return;

        usesRemaining--;

        if (usesRemaining <= 0)
        {
            available = false;

            if (respawnTime > 0f)
                StartCoroutine(Respawn());
        }
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnTime);

        usesRemaining = usesBeforeEmpty;
        available = true;
    }
}