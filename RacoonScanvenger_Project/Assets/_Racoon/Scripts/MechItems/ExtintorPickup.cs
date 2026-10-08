
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
    [SerializeField] private float validationDistance = 3f;

    private bool consumed;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (consumed || !IsSpawned)
            return;

        PlayerController player =
            other.GetComponentInParent<PlayerController>();

        if (player == null || !player.IsOwner)
            return;

        player.RequestExtintorPickup(this);
    }

    public void ConsumeOnServer(PlayerController player)
    {
        if (!IsServer || consumed || player == null)
            return;

        if (!player.IsSpawned || !IsSpawned)
            return;

        float distance = Vector3.Distance(
            transform.position,
            player.transform.position
        );

        if (distance > validationDistance)
            return;

        consumed = true;

        player.ActivateExtintorOnOwnerRpc(duration, bounceSpeed);

        NetworkObject.Despawn(true);
    }
}