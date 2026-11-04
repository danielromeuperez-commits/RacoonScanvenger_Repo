using UnityEngine;
using Unity.Netcode;

public class SpawnerMechItem : NetworkBehaviour, IGameEvent
{
    [SerializeField] private GameObject[] prefabs;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float segundos = 10f;

    public bool HasTriggered { get; private set; } = false;

    /// <summary>Ocupado mientras espera para sacar el objeto.</summary>
    public bool IsBusy => IsInvoking(nameof(SpawnRandomPrefab));

    // Lo lanza el EventManager (solo en el servidor): sale el objeto pasados "segundos".
    public void TriggerEventManually()
    {
        if (!IsServer || IsBusy)
            return;

        HasTriggered = true;
        Invoke(nameof(SpawnRandomPrefab), segundos);
    }

    private void SpawnRandomPrefab()
    {
        if (!IsServer)
            return;

        if (prefabs.Length == 0 || spawnPoint == null)
            return;

        int randomIndex = Random.Range(0, prefabs.Length);
        GameObject selectedPrefab = prefabs[randomIndex];

        // Instanciamos el objeto en el mundo del servidor
        GameObject spawnedInstance = Instantiate(
            selectedPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        // Si el prefab tiene un NetworkObject, debemos hacer que la red lo reconozca y lo spawnee en todos los clientes
        NetworkObject netObj = spawnedInstance.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
        else
        {
            Debug.LogWarning("El prefab seleccionado en el SpawnerMechItem no tiene componente NetworkObject.");
        }
    }
}