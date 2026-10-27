using Unity.Netcode;
using UnityEngine;
using TMPro;

public class eventDoublePoints : NetworkBehaviour, IGameEvent
{
    [Header("Configuración de UI")]
    [Tooltip("Panel de UI que se activará cuando comience el evento.")]
    [SerializeField] GameObject PopUpPanel;
    [Tooltip("Texto opcional para mostrar qué estantería tiene el x2.")]
    [SerializeField] TextMeshProUGUI PopUpText;
    [Tooltip("Tiempo en segundos que la UI permanecerá visible (0 para dejarla fija).")]
    [SerializeField] float displayDuration = 5f;

    // Propiedad requerida por el EventManager para saber si ya se ejecutó
    public bool HasTriggered { get; private set; } = false;

    public override void OnNetworkSpawn()
    {
        if (PopUpPanel != null)
            PopUpPanel.SetActive(false);
    }

    // El Update se elimina de aquí porque el tiempo lo gestiona el EventManager.

    // Método público que llamará el EventManager de forma aleatoria
    public void TriggerEventManually()
    {
        if (HasTriggered || !IsServer)
            return;

        HasTriggered = true;

        LootPoint[] lootPoints = FindObjectsByType<LootPoint>(FindObjectsSortMode.None);
        if (lootPoints.Length == 0)
        {
            Debug.LogWarning("EventDoublePoints: No se encontraron LootPoints en la escena.");
            return;
        }

        int randomIndex = Random.Range(0, lootPoints.Length);
        LootPoint selectedLoot = lootPoints[randomIndex];

        selectedLoot.SetDoublePoints(true);
        TriggerDoublePointsClientRpc(selectedLoot.gameObject.name);

        Debug.Log($"¡EVENTO X2 ALEATORIO! La estantería '{selectedLoot.gameObject.name}' ha sido marcada.");
    }

    [ClientRpc]
    void TriggerDoublePointsClientRpc(string lootPointName)
    {
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(true);

            if (displayDuration > 0)
            {
                CancelInvoke(nameof(HideUI));
                Invoke(nameof(HideUI), displayDuration);
            }
        }

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