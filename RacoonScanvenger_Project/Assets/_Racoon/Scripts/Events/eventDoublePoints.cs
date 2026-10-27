using Unity.Netcode;
using UnityEngine;
using TMPro; // Asegúrate de tener instalado TextMeshPro

public class EventDoublePoints : NetworkBehaviour
{
    [Header("Configuración del Evento")]
    [Tooltip("Segundos transcurridos de partida para activar el evento x2.")]
    [SerializeField] float triggerTime = 60f;

    [Header("Configuración de UI")]
    [Tooltip("Panel de UI que se activará cuando comience el evento.")]
    [SerializeField] GameObject PopUpPanel;
    [Tooltip("Texto opcional para mostrar qué estantería tiene el x2.")]
    [SerializeField] TextMeshProUGUI PopUpText;
    [Tooltip("Tiempo en segundos que la UI permanecerá visible (0 para dejarla fija).")]
    [SerializeField] float displayDuration = 5f;

    MatchTimer matchTimer;
    bool eventTriggered = false;

    public override void OnNetworkSpawn()
    {
        // Nos aseguramos de que la UI empiece oculta en todos los clientes
        if (PopUpPanel != null)
            PopUpPanel.SetActive(false);

        if (!IsServer)
            return;

        // Buscamos el MatchTimer en la escena automáticamente
        matchTimer = FindFirstObjectByType<MatchTimer>();

        if (matchTimer == null)
        {
            Debug.LogError("EventDoublePoints: No se encontró ningún MatchTimer en la escena.");
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
            Debug.LogWarning("EventDoublePoints: No se encontraron LootPoints en la escena.");
            return;
        }

        // Seleccionamos uno al azar
        int randomIndex = Random.Range(0, lootPoints.Length);
        LootPoint selectedLoot = lootPoints[randomIndex];

        selectedLoot.SetDoublePoints(true);

        // Notificamos a todos los clientes pasando el nombre del objeto seleccionado
        TriggerDoublePointsClientRpc(selectedLoot.gameObject.name);

        Debug.Log($"¡EVENTO X2! La estantería '{selectedLoot.gameObject.name}' ha sido marcada.");
    }

    [ClientRpc]
    void TriggerDoublePointsClientRpc(string lootPointName)
    {
        // Activamos el panel de UI en cada cliente
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(true);

            // Opcional: Ocultar la UI automáticamente después de unos segundos
            if (displayDuration > 0)
            {
                CancelInvoke(nameof(HideUI));
                Invoke(nameof(HideUI), displayDuration);
            }
        }

        // Actualizamos el texto si está asignado
        if (PopUpText != null)
        {
            PopUpText.text = $"¡EVENTO X2!\nLa zona '{lootPointName}' otorga el doble de puntos.";
        }
    }

    void HideUI()
    {
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(false);
        }
    }
}