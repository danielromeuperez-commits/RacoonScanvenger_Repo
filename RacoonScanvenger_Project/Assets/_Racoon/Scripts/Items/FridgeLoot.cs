using System.Collections.Generic;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Nevera saqueable (PF_SM_Fridge). Su LootPoint es exclusivo: mientras alguien la saquea, las puertas se abren
/// (Animator, bool «Open») en todas las instancias y no chocan con ese jugador (sí con el otro, que puede quedarse
/// atrapado o empujado por ellas). Quién la usa lo replica LootPointSync (LootPoint.CurrentUserClientId).
/// </summary>
public class FridgeLoot : MonoBehaviour
{
    static readonly int OpenId = Animator.StringToHash("Open");

    [SerializeField] LootPoint lootPoint;
    [SerializeField] Animator animator;
    [Tooltip("Colliders de las puertas: no chocan con quien está saqueando.")]
    [SerializeField] Collider[] doorColliders = new Collider[0];

    int ignoredClient = -1;
    readonly List<Collider> ignoredPlayerColliders = new();

    void Reset()
    {
        lootPoint = GetComponentInChildren<LootPoint>();
        animator = GetComponent<Animator>();
    }

    void OnDisable() => RestoreCollisions();

    void Update()
    {
        if (lootPoint == null) return;
        int user = lootPoint.CurrentUserClientId;
        if (animator != null) animator.SetBool(OpenId, user >= 0);
        if (user != ignoredClient) SetIgnoredUser(user);
    }

    void SetIgnoredUser(int clientId)
    {
        RestoreCollisions();
        ignoredClient = clientId;
        if (clientId < 0) return;

        PlayerController player = FindPlayer((ulong)clientId);
        if (player == null)
        {
            ignoredClient = -1;   // aún no está: se reintenta el frame siguiente
            return;
        }
        foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>())
        {
            if (playerCollider.isTrigger) continue;
            foreach (Collider door in doorColliders)
                if (door != null) Physics.IgnoreCollision(door, playerCollider, true);
            ignoredPlayerColliders.Add(playerCollider);
        }
    }

    void RestoreCollisions()
    {
        foreach (Collider playerCollider in ignoredPlayerColliders)
        {
            if (playerCollider == null) continue;
            foreach (Collider door in doorColliders)
                if (door != null) Physics.IgnoreCollision(door, playerCollider, false);
        }
        ignoredPlayerColliders.Clear();
        ignoredClient = -1;
    }

    static PlayerController FindPlayer(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null) return null;
        foreach (NetworkObject netObj in manager.SpawnManager.SpawnedObjectsList)
            if (netObj.OwnerClientId == clientId && netObj.TryGetComponent(out PlayerController player)) return player;
        return null;
    }
}
