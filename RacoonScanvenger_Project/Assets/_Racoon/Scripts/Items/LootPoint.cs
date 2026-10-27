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

    [Header("Configuración del Evento X2 y Tiempos")]
    [Tooltip("Número de recolecciones permitidas antes de que expire el evento x2.")]
    [SerializeField, Min(1)] int maxDoublePointsCollections = 10;

    [Tooltip("Tiempo de espera entre recolecciones cuando está en estado normal (segundos).")]
    [SerializeField, Min(0f)] float normalCooldown = 0f;

    [Tooltip("Tiempo de espera entre recolecciones cuando el evento x2 está activo (segundos).")]
    [SerializeField, Min(0f)] float doublePointsCooldown = 2f;

    bool isDoublePoints = false;
    int currentDoublePointsCollections = 0;
    float nextInteractTime = 0f;

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
        itemsRemaining = Random.Range(minItems, maxItems + 1);
        available = true;
    }

    public void SetDoublePoints(bool enabled)
    {
        isDoublePoints = enabled;
        if (enabled)
        {
            currentDoublePointsCollections = 0; // Reiniciar contador al activarse el evento
        }
    }

    public bool CanInteract(PlayerController player)
    {
        EnsureInitialized();

        if (!available ||
            itemsRemaining <= 0 ||
            player == null)
            return false;

        // Comprobar si está en tiempo de espera (cooldown)
        if (Time.time < nextInteractTime)
            return false;

        return player.TryGetComponent(out PlayerLoot _);
    }

    public void Interact(PlayerController player)
    {
        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer ||
            !CanInteract(player))
            return;

        PlayerLoot playerLoot = player.GetComponent<PlayerLoot>();

        // Calcular puntos según si el evento x2 está activo
        int finalPoints = isDoublePoints ? pointsPerItem * 2 : pointsPerItem;

        if (!playerLoot.ServerAddLoot(finalPoints, weightPerItem))
            return;

        // Aplicar el tiempo de espera (cooldown) correspondiente
        float activeCooldown = isDoublePoints ? doublePointsCooldown : normalCooldown;
        nextInteractTime = Time.time + activeCooldown;

        // Si el x2 está activo, sumamos una recolección al contador del evento
        if (isDoublePoints)
        {
            currentDoublePointsCollections++;
            if (currentDoublePointsCollections >= maxDoublePointsCollections)
            {
                SetDoublePoints(false); // Desactiva el x2 al alcanzar el límite de recolecciones
            }
        }

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