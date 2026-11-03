using UnityEngine;
using Unity.Netcode;
using Racoon.Player;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class ExtintorPickup : NetworkBehaviour
{
    [Header("EFECTO")]
    [SerializeField] private float duration = 5f;
    [SerializeField] private float bounceSpeed = 10f;
    [SerializeField] private float validationDistance = 5f;

    [Header("Loot / Recompensa")]
    [SerializeField] private int lootPoints = 50;
    [SerializeField] private float lootWeight = 2f;

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

        player.RequestExtintorPickup(this);
    }

    public void ConsumeOnServer(PlayerController player)
    {
        if (!IsServer || consumed || player == null || !IsSpawned)
            return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance > validationDistance)
            return;

        consumed = true;

        // Sumar puntos y peso a la bolsa del jugador a través de PlayerLoot
        if (player.TryGetComponent(out PlayerLoot playerLoot))
        {
            playerLoot.ServerAddLoot(lootPoints, lootWeight);
        }

        player.ActivateExtintorOnOwnerRpc(duration, bounceSpeed);

        NetworkObject.Despawn(true);
    }
}