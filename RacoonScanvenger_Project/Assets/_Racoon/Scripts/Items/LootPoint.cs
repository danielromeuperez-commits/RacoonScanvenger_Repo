using System.Collections;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class LootPoint : MonoBehaviour, IRepeatableInteractable
{
    [Header("Loot por objeto")]
    [Tooltip("Puntos que da cada objeto recogido.")]
    [FormerlySerializedAs("points")]
    [SerializeField, Min(0)] int pointsPerItem = 10;

    [Tooltip("Peso que añade cada objeto recogido.")]
    [FormerlySerializedAs("weight")]
    [SerializeField, Min(0f)] float weightPerItem = 1f;

    [Header("Cantidad de objetos")]
    [Tooltip("Número mínimo de objetos que puede contener esta estantería.")]
    [SerializeField, Min(1)] int minItems = 4;

    [Tooltip("Número máximo de objetos que puede contener esta estantería.")]
    [SerializeField, Min(1)] int maxItems = 7;

    [Header("Respawn")]
    [Tooltip("Segundos hasta rellenarse. 0 = una vez vacía, no reaparece.")]
    [SerializeField, Min(0f)] float respawnTime = 0f;

    int itemsRemaining;
    bool available;
    bool initialized;

    void OnValidate()
    {
        if (maxItems < minItems)
            maxItems = minItems;
    }

    void EnsureInitialized()
    {
        if (initialized)
            return;

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
            return;

        Refill();
        initialized = true;
    }

    void Refill()
    {
        // Random.Range con int no incluye el valor máximo,
        // por eso usamos maxItems + 1.
        itemsRemaining =
            Random.Range(minItems, maxItems + 1);

        available = true;
    }

    /// <summary>
    /// Puede seguir saqueándose mientras queden objetos.
    /// </summary>
    public bool CanInteract(PlayerController player)
    {
        EnsureInitialized();

        if (!available ||
            itemsRemaining <= 0 ||
            player == null)
            return false;

        return player.TryGetComponent(out PlayerLoot _);
    }

    /// <summary>
    /// Cada interacción recoge UN objeto de la estantería.
    /// </summary>
    public void Interact(PlayerController player)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer ||
            !CanInteract(player))
            return;

        PlayerLoot playerLoot =
            player.GetComponent<PlayerLoot>();

        if (!playerLoot.ServerAddLoot(pointsPerItem, weightPerItem))
            return;

        itemsRemaining--;

        if (itemsRemaining > 0)
            return;

        available = false;

        if (respawnTime > 0f)
            StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnTime);

        Refill();
    }
} 
