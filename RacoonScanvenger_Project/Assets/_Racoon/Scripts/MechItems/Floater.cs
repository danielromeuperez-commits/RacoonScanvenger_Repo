using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class Floater : NetworkBehaviour
{
    [Header("FLOTADOR")]
    [SerializeField] float floatDuration = 10f;

    [Header("REBOTE")]
    [SerializeField] float bounceForce = 10f;
    [SerializeField] float upwardForce = 2f;
    [SerializeField] float bounceCooldown = 0.25f;

    [Header("LAYERS")]
    [SerializeField] LayerMask wallLayers;
    [SerializeField] LayerMask playerLayers;

    Rigidbody rb;

    bool floatActive;

    float lastBounceTime = -Mathf.Infinity;

    Coroutine floatCoroutine;

    // =========================================================
    // AWAKE
    // =========================================================

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // =========================================================
    // ACTIVAR FLOTADOR
    // =========================================================

    public void ActivateFloat(float duration)
    {
        if (!IsServer)
            return;

        if (duration <= 0f)
            duration = floatDuration;

        if (floatCoroutine != null)
        {
            StopCoroutine(floatCoroutine);
        }

        floatCoroutine =
            StartCoroutine(
                FloatTimer(duration)
            );
    }

    // =========================================================
    // TEMPORIZADOR
    // =========================================================

    IEnumerator FloatTimer(float duration)
    {
        floatActive = true;

        Debug.Log(
            $"[{name}] 🛟 FLOTADOR ACTIVADO"
        );

        yield return new WaitForSeconds(duration);

        floatActive = false;

        floatCoroutine = null;

        Debug.Log(
            $"[{name}] 🛟 FLOTADOR TERMINADO"
        );
    }

    // =========================================================
    // COLISIONES
    // =========================================================

    void OnCollisionEnter(Collision collision)
    {
        // Solo el servidor decide los rebotes.
        if (!IsServer)
            return;

        if (!floatActive)
            return;

        // Evitar rebotes múltiples instantáneos.
        if (Time.time <
            lastBounceTime + bounceCooldown)
            return;

        int otherLayer =
            collision.gameObject.layer;

        // =====================================================
        // PARED
        // =====================================================

        if (IsInLayerMask(
            otherLayer,
            wallLayers))
        {
            BounceFromWall(collision);

            return;
        }

        // =====================================================
        // PLAYER
        // =====================================================

        if (IsInLayerMask(
            otherLayer,
            playerLayers))
        {
            BounceFromPlayer(collision);

            return;
        }
    }

    // =========================================================
    // REBOTE CONTRA PARED
    // =========================================================

    void BounceFromWall(Collision collision)
    {
        if (collision.contactCount == 0)
            return;

        ContactPoint contact =
            collision.GetContact(0);

        // Normal de la pared.
        Vector3 normal =
            contact.normal.normalized;

        lastBounceTime = Time.time;

        Vector3 bounceVelocity =
            normal * bounceForce;

        bounceVelocity +=
            Vector3.up * upwardForce;

        ApplyWallBounceClientRpc(
            bounceVelocity
        );

        Debug.Log(
            $"[{name}] 🛟 REBOTE CONTRA PARED"
        );
    }

    // =========================================================
    // REBOTE CONTRA OTRO PLAYER
    // =========================================================

    void BounceFromPlayer(Collision collision)
    {
        if (collision.contactCount == 0)
            return;

        // Buscar Rigidbody del otro jugador.
        Rigidbody otherRb =
            collision.gameObject
                .GetComponentInParent<Rigidbody>();

        if (otherRb == null)
            return;

        // Buscar NetworkObject del otro jugador.
        NetworkObject otherNetworkObject =
            otherRb.GetComponent<NetworkObject>();

        if (otherNetworkObject == null)
            return;

        // No permitir que se golpee a sí mismo.
        if (otherNetworkObject == NetworkObject)
            return;

        // =====================================================
        // DIRECCIÓN ENTRE LOS DOS JUGADORES
        // =====================================================

        Vector3 direction =
            transform.position -
            otherRb.transform.position;

        // Si están exactamente en la misma posición.
        if (direction.sqrMagnitude < 0.001f)
        {
            ContactPoint contact =
                collision.GetContact(0);

            direction =
                contact.normal;
        }

        direction.Normalize();

        // =====================================================
        // IMPULSO DEL JUGADOR CON FLOTADOR
        // =====================================================

        Vector3 myVelocity =
            direction * bounceForce;

        myVelocity +=
            Vector3.up * upwardForce;

        // =====================================================
        // IMPULSO DEL OTRO JUGADOR
        // =====================================================

        Vector3 otherVelocity =
            -direction * bounceForce;

        otherVelocity +=
            Vector3.up * upwardForce;

        lastBounceTime = Time.time;

        // =====================================================
        // ENVIAR A LOS DOS CLIENTES
        // =====================================================

        ApplyPlayerBounceClientRpc(
            myVelocity,
            otherVelocity,
            otherNetworkObject.NetworkObjectId
        );

        Debug.Log(
            $"[{name}] 🛟💥 CHOQUE CON PLAYER → REBOTAN LOS DOS"
        );
    }

    // =========================================================
    // REBOTE CONTRA PARED - CLIENT RPC
    // =========================================================

    [ClientRpc]
    void ApplyWallBounceClientRpc(
        Vector3 velocity)
    {
        // Solo el propietario mueve su Rigidbody.
        if (!IsOwner)
            return;

        if (rb == null)
            return;

        rb.linearVelocity =
            Vector3.zero;

        rb.AddForce(
            velocity,
            ForceMode.VelocityChange
        );
    }

    // =========================================================
    // REBOTE ENTRE PLAYERS - CLIENT RPC
    // =========================================================

    [ClientRpc]
    void ApplyPlayerBounceClientRpc(
        Vector3 myVelocity,
        Vector3 otherVelocity,
        ulong otherPlayerNetworkId)
    {
        // =====================================================
        // MI PLAYER
        // =====================================================

        if (IsOwner)
        {
            if (rb != null)
            {
                rb.linearVelocity =
                    Vector3.zero;

                rb.AddForce(
                    myVelocity,
                    ForceMode.VelocityChange
                );
            }
        }

        // =====================================================
        // BUSCAR OTRO PLAYER
        // =====================================================

        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton
            .SpawnManager
            .SpawnedObjects
            .TryGetValue(
                otherPlayerNetworkId,
                out NetworkObject otherObject))
        {
            return;
        }

        // Solo el dueño del otro Player
        // aplica su propio impulso.
        if (!otherObject.IsOwner)
            return;

        Rigidbody otherRb =
            otherObject.GetComponent<Rigidbody>();

        if (otherRb == null)
            return;

        otherRb.linearVelocity =
            Vector3.zero;

        otherRb.AddForce(
            otherVelocity,
            ForceMode.VelocityChange
        );
    }

    // =========================================================
    // COMPROBAR LAYER
    // =========================================================

    bool IsInLayerMask(
        int layer,
        LayerMask mask)
    {
        return
            (mask.value &
            (1 << layer)) != 0;
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        Gizmos.color = Color.cyan;

        Gizmos.DrawWireSphere(
            transform.position,
            0.5f
        );
    }
}
