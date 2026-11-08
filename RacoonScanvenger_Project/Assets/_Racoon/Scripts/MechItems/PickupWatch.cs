using Racoon.Player;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Despertador del suelo. Se coge MANTENIENDO Interact (como vender). Al cogerlo: suma sus puntos a la bolsa y el
/// jugador lo lleva delante, sonando: su mando vibra fuerte y su pantalla tiembla TODO EL RATO hasta que lo vende
/// (PlayerMechCarry). Mientras lo lleva, los objetos de las cajas quedan guardados (PlayerController.CarriedMech).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class PickupWatch : NetworkBehaviour, IMechPickup
{
    [Header("Coger")]
    [Tooltip("Segundos manteniendo Interact para cogerlo.")]
    [SerializeField, Min(0f)] private float holdTime = 0.8f;

    [Header("Efectos Opcionales")]
    public GameObject pickupEffectPrefab;

    [Header("Validación de Red")]
    [SerializeField] private float validationDistance = 5f;

    [Header("Loot / Recompensa")]
    [SerializeField] private int lootPoints = 100;
    [SerializeField] private float lootWeight = 0f;

    private bool consumed;
    private readonly HoldInteraction hold = new();

    public float HoldTime => holdTime;
    public bool IsAvailable => IsSpawned && !consumed;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        // La interacción del jugador solo busca en la capa Interactable.
        MechPickups.UseInteractableLayer(gameObject);
    }

    private void OnEnable() => MechPickups.Register(this);
    private void OnDisable() => MechPickups.Unregister(this);

    // ---------------- Mantener para coger (solo servidor) ----------------

    public bool CanInteract(PlayerController player) =>
        IsAvailable && player != null && !player.IsCarryingMech;

    public void Interact(PlayerController player)
    {
        if (!IsServer || !CanInteract(player))
            return;
        if (hold.Tick(player, holdTime))
            ConsumeOnServer(player);
    }

    public void ConsumeOnServer(PlayerController player)
    {
        if (!IsServer || consumed || player == null || !IsSpawned || player.IsCarryingMech)
            return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance > validationDistance)
            return;

        consumed = true;

        if (player.TryGetComponent(out PlayerLoot playerLoot))
        {
            playerLoot.ServerAddLoot(lootPoints, lootWeight);
        }

        // Lo lleva (y suena) hasta venderlo: PlayerLoot.ServerSellLoot lo quita.
        player.ServerSetCarriedMech(MechKind.Clock);
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

        // El modelo pasa delante del jugador (copia visual de este, que desaparece).
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject) &&
            playerObject.TryGetComponent(out PlayerMechCarry carry))
        {
            carry.AttachVisual(MechKind.Clock, gameObject);
        }

        HideVisualsAndColliders();
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
