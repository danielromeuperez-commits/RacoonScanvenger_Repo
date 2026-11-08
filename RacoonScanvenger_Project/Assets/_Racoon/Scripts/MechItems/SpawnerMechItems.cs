using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components; // Necesario para acceder al NetworkTransform si lo usas
using UnityEngine;

public class SpawnerMechItems : NetworkBehaviour
{
    [Header("Configuración de Objetos")]
    [Tooltip("Lista de GameObjects (deben tener NetworkObject y NetworkTransform).")]
    public List<GameObject> targetObjects;

    [Header("Configuración de Posición")]
    [Tooltip("(Opcional) El punto de la escena desde el cual se moverán.")]
    public Transform sourcePoint;

    [Header("Temporizador y Límites")]
    [Tooltip("Tiempo en segundos para que aparezca el objeto.")]
    public float interval = 3f;

    private float timer;
    private bool hasSpawned = false;

    public override void OnNetworkSpawn()
    {
        // Solo el servidor maneja esta lógica
        if (!IsServer)
        {
            enabled = false;
            return;
        }
    }

    void Update()
    {
        if (hasSpawned) return;

        timer += Time.deltaTime;

        if (timer >= interval)
        {
            TeleportAndShowRandomObject();
            hasSpawned = true;
        }
    }

    void TeleportAndShowRandomObject()
    {
        if (targetObjects == null || targetObjects.Count == 0)
        {
            Debug.LogWarning("La lista de GameObjects está vacía o no asignada.");
            return;
        }

        // Elegir uno al azar de la lista
        int randomIndex = Random.Range(0, targetObjects.Count);
        GameObject selectedObj = targetObjects[randomIndex];

        if (selectedObj != null)
        {
            NetworkObject netObj = selectedObj.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"El objeto {selectedObj.name} necesita un NetworkObject.", selectedObj);
                return;
            }

            // Intentamos obtener el NetworkTransform para evitar que interpole el viaje desde la otra punta del mapa
            NetworkTransform netTransform = selectedObj.GetComponent<NetworkTransform>();

            if (netTransform != null)
            {
                // Desactivamos temporalmente el NetworkTransform para que el teletransporte sea instantáneo en red
                netTransform.enabled = false;
            }

            // 1. Si hay un punto de origen definido, lo movemos ahí primero
            if (sourcePoint != null)
            {
                selectedObj.transform.position = sourcePoint.position;
            }

            // 2. Mover a la posición exactas del Empty (transform.position de este script)
            selectedObj.transform.position = transform.position;

            // Reactivamos el NetworkTransform para que vuelva a sincronizar su posición (ahora ya en el sitio correcto)
            if (netTransform != null)
            {
                netTransform.enabled = true;
            }

            // Asegurarnos de que esté activo
            if (!selectedObj.activeSelf)
            {
                selectedObj.SetActive(true);
            }

            Debug.Log($"[Servidor] Objeto teletransportado instantáneamente al Empty: {selectedObj.name}", selectedObj);
        }
        else
        {
            Debug.LogWarning("El objeto seleccionado de la lista es nulo.");
        }
    }
}