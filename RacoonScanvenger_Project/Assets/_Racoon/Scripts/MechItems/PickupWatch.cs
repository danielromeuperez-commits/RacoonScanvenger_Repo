using Racoon.Cameras;
using Racoon.Player;
using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class PickupWatch : NetworkBehaviour
{
    [Header("Configuración de Vibración Larga")]
    [Range(0f, 1f)] public float lowFrequency = 0.5f;
    [Range(0f, 1f)] public float highFrequency = 0.5f;
    [Tooltip("Duración total de la vibración del mando en segundos.")]
    public float totalVibrationDuration = 2.0f;

    [Header("Configuración de Temblor de Cámara")]
    [SerializeField] private CameraShakeProfile shakeProfile;
    [SerializeField] private float shakeIntensity = 1f;

    [Header("Efectos Opcionales")]
    public GameObject pickupEffectPrefab;

    [Header("Validación de Red")]
    [SerializeField] private float validationDistance = 5f;

    [Header("Loot / Recompensa")]
    [SerializeField] private int lootPoints = 100;
    [SerializeField] private float lootWeight = 0f;

    private bool consumed;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (consumed || !IsSpawned)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || !player.IsOwner)
            return;

        player.RequestPickupWatch(this);
    }

    public void ConsumeOnServer(PlayerController player)
    {
        if (!IsServer || consumed || player == null || !IsSpawned)
            return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance > validationDistance)
            return;

        consumed = true;

        if (player.TryGetComponent(out PlayerLoot playerLoot))
        {
            playerLoot.ServerAddLoot(lootPoints, lootWeight);
        }

        ConsumeClientRpc(player.NetworkObjectId);

        NetworkObject.Despawn(true);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ConsumeClientRpc(ulong playerObjectId)
    {
        if (pickupEffectPrefab != null)
        {
            Instantiate(pickupEffectPrefab, transform.position, transform.rotation);
        }

        HideVisualsAndColliders();

        // Si somos el cliente dueño que lo ha recogido, disparamos los efectos locales
        if (NetworkManager.Singleton.LocalClient.PlayerObject != null &&
            NetworkManager.Singleton.LocalClient.PlayerObject.NetworkObjectId == playerObjectId)
        {
            StartCoroutine(LongPickupRoutine());
        }
    }

    private IEnumerator LongPickupRoutine()
    {
        // 1. Inicia el temblor de cámara respetando la duración configurada en el perfil del Inspector
        if (shakeProfile != null)
        {
            CameraShaker.Play(shakeProfile, shakeIntensity);
        }

        // 2. Inicia la vibración del mando de golpe por el tiempo total establecido
        if (VibrationManager.Instance != null)
        {
            VibrationManager.Instance.Rumble(lowFrequency, highFrequency, totalVibrationDuration);
        }

        // Espera a que termine el tiempo de forma natural
        yield return new WaitForSeconds(totalVibrationDuration);
    }

    private void HideVisualsAndColliders()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            renderer.enabled = false;
        }
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }
    }
}