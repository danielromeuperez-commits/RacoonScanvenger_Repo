using Unity.Netcode;
using UnityEngine;
using Racoon.Events;
using Racoon.Items;

public class eventSpawnItem : NetworkBehaviour, IGameEvent
{
    [Header("Configuración del Evento de Instanciación")]
    [Tooltip("El ItemData del objeto que deseas instanciar (ej: ITEM_Banana_New).")]
    [SerializeField] private ItemData itemToSpawnData;

    [Tooltip("Lista de todos los puntos donde quieres que aparezca un plátano.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Presentación (anuncio común: GameEventAnnouncer)")]
    [SerializeField] private string title = "¡PASILLO RESBALADIZO!";
    [Tooltip("{0} = nombre del objeto (opcional).")]
    [SerializeField] private string subtitle = "¡Hay plátanos por todo el suelo! Cuidado al correr.";
    [Tooltip("Color del evento (golpe de cámara): amarillo pastel, el de la cinta.")]
    [SerializeField] private Color accent = new(1f, 0.91f, 0.64f);
    [Tooltip("Piel de color de la cinta (Verde, Amarillo o Rojo; sprites del PSD, sin teñir).")]
    [SerializeField] private string colorSkin = "Amarillo";

    [Tooltip("Al repetirse, no se pone otro plátano en un punto que ya tenga uno a menos de esta distancia (m).")]
    [SerializeField, Min(0f)] private float occupiedRadius = 0.6f;

    public bool HasTriggered { get; private set; } = false;

    /// <summary>Se puede repetir en cualquier momento (los puntos ocupados se saltan).</summary>
    public bool IsBusy => false;

    public void TriggerEventManually()
    {
        if (!IsServer || IsBusy)
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
            // Si el evento se repite, los plátanos que siguen en el suelo ocupan su punto.
            Racoon.Items.BananaTrap[] existing = FindObjectsByType<Racoon.Items.BananaTrap>(FindObjectsSortMode.None);

            foreach (var sp in spawnPoints)
            {
                if (sp != null && !IsOccupied(sp.position, existing))
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

    bool IsOccupied(Vector3 point, Racoon.Items.BananaTrap[] existing)
    {
        foreach (var trap in existing)
            if (trap != null && Vector3.Distance(trap.transform.position, point) < occupiedRadius)
                return true;
        return false;
    }

    [ClientRpc]
    void TriggerSpawnClientRpc(string itemName)
    {
        GameEventAnnouncer.Announce(this, title, string.Format(subtitle, itemName.ToLowerInvariant()), accent, colorSkin);
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