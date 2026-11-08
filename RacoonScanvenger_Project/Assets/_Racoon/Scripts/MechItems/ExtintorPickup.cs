using UnityEngine;
using Unity.Netcode;
using Racoon.Player;

/// <summary>
/// Extintor del suelo. Se coge MANTENIENDO Interact (como vender). Al cogerlo: suma sus puntos a la bolsa, propulsa
/// al jugador un rato y se lo cuelga a la espalda hasta que lo entregue en el punto de venta (mientras tanto los
/// objetos de las cajas quedan guardados: ver PlayerController.CarriedMech y PlayerMechCarry).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class ExtintorPickup : NetworkBehaviour, IMechPickup
{
    [Header("EFECTO")]
    [SerializeField] private float duration = 5f;
    [SerializeField] private float bounceSpeed = 10f;
    [SerializeField] private float validationDistance = 5f;

    [Header("Coger")]
    [Tooltip("Segundos manteniendo Interact para cogerlo.")]
    [SerializeField, Min(0f)] private float holdTime = 0.8f;

    [Header("Loot / Recompensa")]
    [SerializeField] private int lootPoints = 50;
    [SerializeField] private float lootWeight = 2f;

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
        if (IsOwner)
        {
            AudioManager.Instance.PlaySFX(16);
        }
        consumed = true;

        // Sumar puntos y peso a la bolsa del jugador a través de PlayerLoot
        if (player.TryGetComponent(out PlayerLoot playerLoot))
        {
            playerLoot.ServerAddLoot(lootPoints, lootWeight);
        }

        player.ServerSetCarriedMech(MechKind.Extintor);
        ConsumeClientRpc(player.NetworkObjectId);
        player.ActivateExtintorOnOwnerRpc(duration, bounceSpeed);

        NetworkObject.Despawn(true);
    }

    // Todos los clientes: el modelo pasa a la espalda del jugador (copia visual de este, que desaparece).
    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    private void ConsumeClientRpc(ulong playerObjectId)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject playerObject) &&
            playerObject.TryGetComponent(out PlayerMechCarry carry))
        {
            carry.AttachVisual(MechKind.Extintor, gameObject);
        }
    }
}
