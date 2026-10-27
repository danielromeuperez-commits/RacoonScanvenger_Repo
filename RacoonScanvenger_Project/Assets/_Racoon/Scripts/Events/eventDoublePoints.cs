using Unity.Netcode;
using UnityEngine;

public class eventDoublePoints : NetworkBehaviour
{
    [Header("Configuración del Evento")]
    [Tooltip("Segundos transcurridos de partida para activar el evento x2.")]
    [SerializeField] float triggerTime = 60f;

    MatchTimer matchTimer;
    bool eventTriggered = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        // Buscamos el MatchTimer en la escena automáticamente
        matchTimer = FindFirstObjectByType<MatchTimer>();

        if (matchTimer == null)
        {
            Debug.LogError("LootEventController: No se encontró ningún MatchTimer en la escena.");
        }
    }

    void Update()
    {
        if (!IsServer || eventTriggered || matchTimer == null)
            return;

        // Verificamos si la partida está corriendo y ha pasado el tiempo estipulado
        if (matchTimer.IsRunning)
        {
            float timeElapsed = matchTimer.Duration - matchTimer.TimeRemaining;

            if (timeElapsed >= triggerTime)
            {
                TriggerDoublePointsEvent();
            }
        }
    }

    void TriggerDoublePointsEvent()
    {
        eventTriggered = true;

        LootPoint[] lootPoints = FindObjectsByType<LootPoint>(FindObjectsSortMode.None);
        if (lootPoints.Length == 0)
        {
            Debug.LogWarning("LootEventController: No se encontraron LootPoints en la escena.");
            return;
        }

        // Seleccionamos uno al azar
        int randomIndex = Random.Range(0, lootPoints.Length);
        LootPoint selectedLoot = lootPoints[randomIndex];

        selectedLoot.SetDoublePoints(true);

        Debug.Log($"¡EVENTO X2! La estantería '{selectedLoot.gameObject.name}' ha sido marcada.");
    }
}