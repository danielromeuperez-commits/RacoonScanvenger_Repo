using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class EventManager : NetworkBehaviour
{
    [Header("Configuración de Tiempos y Ciclos (en segundos)")]
    [Tooltip("Segundos de partida que deben pasar antes de empezar a buscar eventos (ej: 60s = Minuto 1).")]
    [SerializeField] float initialDelay = 60f;
    [Tooltip("Cada cuántos segundos se intentará activar un evento una vez pasado el retraso inicial (ej: cada 10s).")]
    [SerializeField] float checkInterval = 10f;
    [Tooltip("Segundo límite de la partida para dejar de intentar activar eventos (ej: 240s = Minuto 4).")]
    [SerializeField] float maxTimeLimit = 240f;

    [Header("Configuración de Probabilidad")]
    [Tooltip("Probabilidad (de 0 a 1) de que ocurra un evento en cada intento (ej: 0.4 = 40%).")]
    [Range(0f, 1f)][SerializeField] float eventProbability = 0.4f;

    [Header("Listado de Eventos")]
    [Tooltip("Arrastra aquí los GameObjects o componentes que implementen IGameEvent.")]
    [SerializeField] List<MonoBehaviour> availableEventScripts = new List<MonoBehaviour>();

    MatchTimer matchTimer;
    float nextCheckTimer = 0f;
    bool eventHasTriggeredGlobal = false;
    bool searchingStopped = false;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        matchTimer = FindFirstObjectByType<MatchTimer>();
        if (matchTimer == null)
        {
            Debug.LogError("No se encontró ningún MatchTimer en la escena.");
        }
    }

    void Update()
    {
        if (!IsServer || matchTimer == null || !matchTimer.IsRunning || eventHasTriggeredGlobal || searchingStopped)
            return;

        // Tiempo transcurrido de partida
        float timeElapsed = matchTimer.Duration - matchTimer.TimeRemaining;

        // 1. Si aún no llegamos al retraso inicial, esperamos
        if (timeElapsed < initialDelay)
            return;

        // 2. Si superamos el tiempo límite de búsqueda, dejamos de intentar
        if (timeElapsed >= maxTimeLimit)
        {
            Debug.Log("NO SALIÓ EVENTO EN NINGUN MOMENTO, SE DEJA DE BUSCAR");
            searchingStopped = true;
            return;
        }

        // 3. Control de intervalos basado en el tiempo transcurrido de la partida
        if (timeElapsed >= nextCheckTimer)
        {
            TryTriggerRandomEvent();
            // Programamos el siguiente chequeo sumando el intervalo
            nextCheckTimer = timeElapsed + checkInterval;
        }
    }

    void TryTriggerRandomEvent()
    {
        if (Random.value > eventProbability)
        {
            Debug.Log("NO SALE EVENTO (Probabilidad fallida)");
            return;
        }

        List<IGameEvent> validEvents = new List<IGameEvent>();

        foreach (var evtScript in availableEventScripts)
        {
            if (evtScript == null) continue;

            if (evtScript is IGameEvent gameEvent)
            {
                if (!gameEvent.HasTriggered)
                {
                    validEvents.Add(gameEvent);
                }
            }
            else
            {
                Debug.LogWarning($"El script {evtScript.name} asignado en el EventManager no implementa IGameEvent.");
            }
        }

        if (validEvents.Count == 0)
        {
            Debug.Log("EventManager: No quedan eventos disponibles en la lista o la lista está vacía.");
            searchingStopped = true;
            return;
        }

        int randomIndex = Random.Range(0, validEvents.Count);
        validEvents[randomIndex].TriggerEventManually();

        eventHasTriggeredGlobal = true;
        Debug.Log("Evento activado con éxito");
    }
}