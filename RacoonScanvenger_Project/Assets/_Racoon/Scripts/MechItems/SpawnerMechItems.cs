using UnityEngine;
using Unity.Netcode;

public class SpawnerMechItem : NetworkBehaviour
{
    [SerializeField] private GameObject[] prefabs;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float segundos = 10f;

    public override void OnNetworkSpawn()
    {
        // SOLO el servidor ejecuta el temporizador y decide qué sale
        if (!IsServer)
            return;

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