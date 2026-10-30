using Unity.Netcode;
using UnityEngine;
using TMPro;
using Racoon.Items;

public class eventBanana : NetworkBehaviour, IGameEvent
{
    [Header("Configuración del Evento de Instanciación")]
    [Tooltip("El ItemData del objeto que deseas instanciar (ej: ITEM_Banana_New).")]
    [SerializeField] private ItemData itemToSpawnData;

    [Tooltip("Lista de todos los puntos donde quieres que aparezca un plátano.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Configuración de UI")]
    [SerializeField] private GameObject PopUpPanel;
    [SerializeField] private TextMeshProUGUI PopUpText;
    [SerializeField] private float displayDuration = 5f;

    public bool HasTriggered { get; private set; } = false;

    public override void OnNetworkSpawn()
    {
        if (PopUpPanel != null)
            PopUpPanel.SetActive(false);
    }

    public void TriggerEventManually()
    {
        if (HasTriggered || !IsServer)
            return;

        HasTriggered = true;

        if (itemToSpawnData == null)
        {
            Debug.LogError("eventSpawnItem: No se ha asignado ningún ItemData para instanciar.");
            return;
        }

        GameObject prefabToInstantiate = itemToSpawnData.heldVisualPrefab;

        if (prefabToInstantiate == null)
        {
            Debug.LogError($"eventSpawnItem: El ItemData '{itemToSpawnData.displayName}' no tiene un prefab visual asignado.");
            return;
        }

        // Verificamos si hay puntos de spawn asignados
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnedCount = 0;

            // Recorremos CADA UNO de los puntos de la lista para instanciar un plátano en cada posición
            foreach (var sp in spawnPoints)
            {
                if (sp != null)
                {
                    GameObject spawnedInstance = Instantiate(prefabToInstantiate, sp.position, sp.rotation);

                    NetworkObject netObj = spawnedInstance.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Spawn();
                    }

                    spawnedCount++;
                }
            }

            Debug.Log($"¡EVENTO DE ÍTEM! Se han instanciado {spawnedCount} plátanos en el mapa.");
        }
        else
        {
            // Si por alguna razón la lista está vacía, al menos saca uno en la posición del objeto
            GameObject spawnedInstance = Instantiate(prefabToInstantiate, transform.position, transform.rotation);
            NetworkObject netObj = spawnedInstance.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                netObj.Spawn();
            }
            Debug.LogWarning("eventSpawnItem: La lista de SpawnPoints está vacía. Se ha instanciado solo 1 plátano en el origen.");
        }

        TriggerSpawnClientRpc(itemToSpawnData.displayName);
    }

    [ClientRpc]
    void TriggerSpawnClientRpc(string itemName)
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
            PopUpText.text = $"¡APARICIÓN DE ÍTEM!\n¡Han aparecido varios/as {itemName} en el mapa!";
        }
    }

    void HideUI()
    {
        if (PopUpPanel != null)
        {
            PopUpPanel.SetActive(false);
        }
    }

    // Dibuja esferas verdes en TODOS los puntos configurados para que los veas en la escena
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            foreach (var sp in spawnPoints)
            {
                if (sp != null)
                {
                    Gizmos.DrawSphere(sp.position, 0.4f);
                    Gizmos.DrawRay(sp.position, sp.forward * 1f);
                }
            }
        }
        else
        {
            Gizmos.DrawSphere(transform.position, 0.4f);
            Gizmos.DrawRay(transform.position, transform.forward * 1f);
        }
    }
}