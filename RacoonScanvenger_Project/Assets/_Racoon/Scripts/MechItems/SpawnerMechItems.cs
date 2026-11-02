using UnityEngine;

public class RandomPrefabSpawner : MonoBehaviour
{
    [SerializeField] private GameObject[] prefabs;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private float segundos = 10f;

    private void Start()
    {
        Invoke(nameof(SpawnRandomPrefab), segundos);
    }

    private void SpawnRandomPrefab()
    {
        if (prefabs.Length == 0 || spawnPoint == null)
            return;

        int randomIndex = Random.Range(0, prefabs.Length);

        Instantiate(
            prefabs[randomIndex],
            spawnPoint.position,
            spawnPoint.rotation
        );
    }
}