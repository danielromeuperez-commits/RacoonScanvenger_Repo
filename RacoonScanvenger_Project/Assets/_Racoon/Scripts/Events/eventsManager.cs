using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Lanza los eventos de partida (solo servidor). Pasado <c>initialDelay</c>, cada <c>checkInterval</c> segundos tira
/// el dado (<c>eventProbability</c>) y, si sale, lanza un evento de la lista según la secuencia elegida, hasta
/// <c>eventsPerMatch</c> eventos o hasta <c>maxTimeLimit</c>. Tras cada evento espera <c>cooldownAfterEvent</c>.
/// Un evento que sigue en marcha (<see cref="IGameEvent.IsBusy"/>) no se elige.
/// </summary>
public class EventManager : NetworkBehaviour
{
    public enum EventSequence
    {
        [Tooltip("Cualquiera de la lista cada vez (puede repetirse seguido).")]
        Aleatorio,
        [Tooltip("Cualquiera menos el último que salió (si solo hay uno, se repite).")]
        AleatorioSinRepetirAnterior,
        [Tooltip("Cada evento sale como mucho una vez: cuando se acaban, se acaban.")]
        NoRepetirNunca,
    }

    [Header("Configuración de Tiempos y Ciclos (en segundos)")]
    [Tooltip("Segundos de partida que deben pasar antes de empezar a buscar eventos (ej: 60s = Minuto 1).")]
    [SerializeField] float initialDelay = 60f;
    [Tooltip("Cada cuántos segundos se intentará activar un evento una vez pasado el retraso inicial (ej: cada 10s).")]
    [SerializeField] float checkInterval = 10f;
    [Tooltip("Segundo límite de la partida para dejar de intentar activar eventos (ej: 240s = Minuto 4).")]
    [SerializeField] float maxTimeLimit = 240f;
    [Tooltip("Segundos de espera tras un evento antes de volver a intentar otro.")]
    [SerializeField, Min(0f)] float cooldownAfterEvent = 30f;

    [Header("Eventos por partida")]
    [Tooltip("Cuántos eventos pueden salir como máximo en una partida.")]
    [SerializeField, Min(1)] int eventsPerMatch = 1;
    [Tooltip("Cómo se elige el siguiente evento.")]
    [SerializeField] EventSequence sequence = EventSequence.NoRepetirNunca;

    [Header("Configuración de Probabilidad")]
    [Tooltip("Probabilidad (de 0 a 1) de que ocurra un evento en cada intento (ej: 0.4 = 40%).")]
    [Range(0f, 1f)][SerializeField] float eventProbability = 0.4f;

    [Header("Listado de Eventos")]
    [Tooltip("Arrastra aquí los GameObjects o componentes que implementen IGameEvent.")]
    [SerializeField] List<MonoBehaviour> availableEventScripts = new List<MonoBehaviour>();

    MatchTimer matchTimer;
    float nextCheckTimer = 0f;
    bool searchingStopped = false;
    int eventsTriggered = 0;
    IGameEvent lastEvent;
    readonly HashSet<IGameEvent> usedEvents = new HashSet<IGameEvent>();

    public int EventsTriggered => eventsTriggered;

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

    void OnValidate()
    {
        if (sequence == EventSequence.NoRepetirNunca && availableEventScripts != null)
        {
            int count = 0;
            foreach (var script in availableEventScripts)
                if (script != null) count++;
            if (count > 0 && eventsPerMatch > count)
                Debug.LogWarning($"EventManager: con «No repetir nunca» solo pueden salir {count} eventos, " +
                                 $"aunque eventsPerMatch sea {eventsPerMatch}.", this);
        }
    }

    void Update()
    {
        if (!IsServer || matchTimer == null || !matchTimer.IsRunning || searchingStopped)
            return;

        // Tiempo transcurrido de partida
        float timeElapsed = matchTimer.Duration - matchTimer.TimeRemaining;

        // 1. Si aún no llegamos al retraso inicial, esperamos
        if (timeElapsed < initialDelay)
            return;

        // 2. Si superamos el tiempo límite de búsqueda, dejamos de intentar
        if (timeElapsed >= maxTimeLimit)
        {
            Debug.Log($"EventManager: fin del tiempo de eventos ({eventsTriggered}/{eventsPerMatch} lanzados).");
            searchingStopped = true;
            return;
        }

        // 3. Control de intervalos basado en el tiempo transcurrido de la partida
        if (timeElapsed >= nextCheckTimer)
        {
            bool triggered = TryTriggerRandomEvent();
            nextCheckTimer = timeElapsed + (triggered ? Mathf.Max(checkInterval, cooldownAfterEvent) : checkInterval);
        }
    }

    bool TryTriggerRandomEvent()
    {
        if (Random.value > eventProbability)
        {
            Debug.Log("NO SALE EVENTO (Probabilidad fallida)");
            return false;
        }

        List<IGameEvent> allEvents = CollectEvents();
        List<IGameEvent> candidates = new List<IGameEvent>();
        foreach (IGameEvent gameEvent in allEvents)
        {
            if (gameEvent.IsBusy) continue;
            if (sequence == EventSequence.NoRepetirNunca && usedEvents.Contains(gameEvent)) continue;
            candidates.Add(gameEvent);
        }

        if (sequence == EventSequence.NoRepetirNunca && usedEvents.Count >= allEvents.Count)
        {
            Debug.Log("EventManager: ya han salido todos los eventos (no se repiten).");
            searchingStopped = true;
            return false;
        }

        // Sin repetir el anterior: se quita de los candidatos salvo que sea el único posible.
        if (sequence == EventSequence.AleatorioSinRepetirAnterior && lastEvent != null && candidates.Count > 1)
            candidates.Remove(lastEvent);

        if (candidates.Count == 0)
        {
            Debug.Log("EventManager: ahora mismo no hay ningún evento disponible (todos en marcha o la lista está vacía).");
            if (allEvents.Count == 0) searchingStopped = true;
            return false;
        }

        IGameEvent chosen = candidates[Random.Range(0, candidates.Count)];
        chosen.TriggerEventManually();

        lastEvent = chosen;
        usedEvents.Add(chosen);
        eventsTriggered++;
        Debug.Log($"Evento activado con éxito: {(chosen as MonoBehaviour)?.name} ({eventsTriggered}/{eventsPerMatch}).");

        if (eventsTriggered >= eventsPerMatch)
        {
            Debug.Log("EventManager: alcanzado el máximo de eventos por partida.");
            searchingStopped = true;
        }
        return true;
    }

    List<IGameEvent> CollectEvents()
    {
        List<IGameEvent> events = new List<IGameEvent>();
        foreach (var evtScript in availableEventScripts)
        {
            if (evtScript == null) continue;

            // Si se arrastra el GameObject a la lista, Unity guarda su primer MonoBehaviour (normalmente el
            // NetworkObject), no el script del evento: lo buscamos en el mismo objeto.
            IGameEvent gameEvent = evtScript as IGameEvent ?? evtScript.GetComponent<IGameEvent>();
            if (gameEvent == null)
            {
                Debug.LogWarning($"El script {evtScript.name} asignado en el EventManager no implementa IGameEvent.");
                continue;
            }
            if (!events.Contains(gameEvent)) events.Add(gameEvent);
        }
        return events;
    }
}
