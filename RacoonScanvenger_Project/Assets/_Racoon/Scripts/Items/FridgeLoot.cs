using System.Collections.Generic;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Nevera saqueable (PF_SM_Fridge). Su LootPoint es exclusivo: mientras alguien la saquea, las puertas se abren
/// (Animator, bool «Open») en todas las instancias. Quién la usa lo replica LootPointSync (LootPoint.CurrentUserClientId).
///
/// Colisión de las puertas (para que nadie se quede encerrado):
/// - Solo tienen collider abiertas del todo. Cerradas las cubre el collider del mueble, y mientras se abren o se
///   cierran no tienen collider, así no arrastran ni meten a nadie dentro.
/// - No chocan con quien saquea, ni con quien esté en el hueco entre las puertas (Pocket) o solapado con una puerta:
///   ese jugador siempre puede salir. Al resto (el rival que llega por los lados) sí lo frenan.
/// </summary>
public class FridgeLoot : MonoBehaviour
{
    static readonly int OpenId = Animator.StringToHash("Open");
    static readonly int OpenStateId = Animator.StringToHash("Abierta");

    [SerializeField] LootPoint lootPoint;
    [SerializeField] Animator animator;
    [Tooltip("Colliders de las puertas: solo existen con la nevera abierta del todo.")]
    [SerializeField] Collider[] doorColliders = new Collider[0];
    [Tooltip("Hueco delante de la nevera entre las puertas abiertas (espacio local de la nevera). Quien está dentro " +
             "atraviesa las puertas para poder salir.")]
    [SerializeField] Bounds pocket = new(new Vector3(0.75f, 1f, -1.2f), new Vector3(1.7f, 2.4f, 0.8f));

    bool doorsSolid = true;
    readonly Dictionary<Collider, bool> ignoring = new();   // collider de jugador → ignora las puertas ahora
    readonly List<Collider> scratch = new();

    void Reset()
    {
        lootPoint = GetComponentInChildren<LootPoint>();
        animator = GetComponent<Animator>();
    }

    void OnDisable()
    {
        SetDoorsSolid(true);
        ignoring.Clear();
    }

    void Update()
    {
        if (lootPoint == null) return;
        int user = lootPoint.CurrentUserClientId;
        if (animator != null) animator.SetBool(OpenId, user >= 0);

        SetDoorsSolid(DoorsFullyOpen());
        if (doorsSolid) UpdateIgnores(user);
    }

    bool DoorsFullyOpen()
    {
        if (animator == null || !animator.isActiveAndEnabled) return false;
        if (animator.IsInTransition(0)) return false;
        return animator.GetCurrentAnimatorStateInfo(0).shortNameHash == OpenStateId;
    }

    void SetDoorsSolid(bool solid)
    {
        if (solid == doorsSolid) return;
        doorsSolid = solid;
        foreach (Collider door in doorColliders)
            if (door != null) door.enabled = solid;
        // Al desactivar un collider Unity olvida sus IgnoreCollision: se recalculan al volver.
        ignoring.Clear();
    }

    void UpdateIgnores(int user)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null) return;

        foreach (NetworkObject netObj in manager.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || !netObj.TryGetComponent(out PlayerController player)) continue;
            bool free = (int)player.OwnerClientId == user || InPocket(player.transform.position);

            scratch.Clear();
            player.GetComponentsInChildren(scratch);
            foreach (Collider playerCollider in scratch)
            {
                if (playerCollider.isTrigger || !Active(playerCollider)) continue;
                // Solapado con una puerta: solo cuenta si ya estaba dentro al aparecer el collider (o ya la atravesaba),
                // así quien empuja la puerta desde fuera no se cuela por el pequeño solape del contacto.
                bool known = ignoring.TryGetValue(playerCollider, out bool current);
                bool ignore = free || ((!known || current) && OverlapsDoor(playerCollider));
                if (known && current == ignore) continue;
                foreach (Collider door in doorColliders)
                    if (Active(door)) Physics.IgnoreCollision(door, playerCollider, ignore);
                ignoring[playerCollider] = ignore;
            }
        }
    }

    bool InPocket(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        local.y = pocket.center.y;   // solo cuenta la planta
        return pocket.Contains(local);
    }

    bool OverlapsDoor(Collider playerCollider)
    {
        foreach (Collider door in doorColliders)
        {
            if (!Active(door) || !door.bounds.Intersects(playerCollider.bounds)) continue;
            if (Physics.ComputePenetration(door, door.transform.position, door.transform.rotation,
                    playerCollider, playerCollider.transform.position, playerCollider.transform.rotation, out _, out _))
                return true;
        }
        return false;
    }

    // IgnoreCollision exige colliders activos (si no, Unity da error).
    static bool Active(Collider c) => c != null && c.enabled && c.gameObject.activeInHierarchy;

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireCube(pocket.center, pocket.size);
    }
}
