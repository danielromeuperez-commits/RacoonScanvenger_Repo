using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Racoon.Player;

[RequireComponent(typeof(Rigidbody))]
public class Enemy_logic : NetworkBehaviour
{
    [Header("Detección")]
    [SerializeField] float detectionRange = 10f;
    [SerializeField] float attackRange = 1.5f;

    [Header("Movimiento")]
    [SerializeField] float moveSpeed = 3f;
    [SerializeField] float rotationSpeed = 720f;

    [Header("Stun")]
    [SerializeField] float stunDuration = 2f;
    [SerializeField] float stunCooldown = 1f;

    [Header("Mirar")]
    [Tooltip("Empty colocado delante del enemigo.")]
    [SerializeField] Transform frontPoint;

    Rigidbody body;

    PlayerController targetPlayer;
    PlayerInputHandler targetInput;
    Rigidbody targetBody;

    Vector3 moveDirection;

    bool isStunning;
    float lastStunTime = -Mathf.Infinity;

    void Awake()
    {
        body = GetComponent<Rigidbody>();

        body.interpolation = RigidbodyInterpolation.Interpolate;

        body.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Debug.Log($"[{name}] Enemy iniciado en servidor.");
    }

    void Update()
    {
        if (!IsServer)
            return;

        // Si no tenemos jugador, buscarlo.
        if (targetPlayer == null)
        {
            FindPlayer();
            moveDirection = Vector3.zero;
            return;
        }

        float distance = Vector3.Distance(
            transform.position,
            targetPlayer.transform.position
        );

        // Fuera del rango de detección.
        if (distance > detectionRange)
        {
            moveDirection = Vector3.zero;
            return;
        }

        // Está suficientemente cerca para atacar.
        if (distance <= attackRange)
        {
            moveDirection = Vector3.zero;

            TryStunPlayer();

            return;
        }

        // Perseguir.
        Vector3 direction =
            targetPlayer.transform.position -
            transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            moveDirection = Vector3.zero;
            return;
        }

        moveDirection = direction.normalized;
    }

    void FixedUpdate()
    {
        if (!IsServer)
            return;

        if (isStunning)
            return;

        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        // Movimiento mediante Rigidbody.
        Vector3 movement =
            moveDirection *
            moveSpeed *
            Time.fixedDeltaTime;

        body.MovePosition(
            body.position + movement
        );

        // Girar hacia el jugador.
        Quaternion targetRotation =
            Quaternion.LookRotation(
                moveDirection,
                Vector3.up
            );

        Quaternion newRotation =
            Quaternion.RotateTowards(
                body.rotation,
                targetRotation,
                rotationSpeed * Time.fixedDeltaTime
            );

        body.MoveRotation(newRotation);
    }

    // =========================================================
    // BUSCAR PLAYER
    // =========================================================

    void FindPlayer()
    {
        if (NetworkManager.Singleton == null)
            return;

        foreach (
            var client
            in NetworkManager.Singleton.ConnectedClientsList
        )
        {
            if (client.PlayerObject == null)
                continue;

            PlayerController candidate =
                client.PlayerObject.GetComponent<PlayerController>();

            if (candidate == null)
                continue;

            targetPlayer = candidate;

            targetBody =
                candidate.GetComponent<Rigidbody>();

            targetInput =
                candidate.GetComponent<PlayerInputHandler>();

            Debug.Log(
                $"[{name}] PLAYER DETECTADO: {candidate.name}"
            );

            if (targetInput != null)
            {
                Debug.Log(
                    $"[{name}] PlayerInputHandler encontrado."
                );
            }
            else
            {
                Debug.LogWarning(
                    $"[{name}] El Player no tiene PlayerInputHandler."
                );
            }

            return;
        }
    }

    // =========================================================
    // STUN
    // =========================================================

    void TryStunPlayer()
    {
        if (targetPlayer == null)
            return;

        if (isStunning)
            return;

        if (Time.time < lastStunTime + stunCooldown)
            return;

        lastStunTime = Time.time;

        StartCoroutine(StunPlayer());
    }

    IEnumerator StunPlayer()
    {
        isStunning = true;

        // El enemigo deja de moverse.
        moveDirection = Vector3.zero;

        // Mirar al jugador usando el FrontPoint.
        LookAtPlayer();

        Debug.Log(
            $"[{name}] STUN → {targetPlayer.name} durante {stunDuration}s"
        );

        // -----------------------------------------------------
        // BLOQUEAR INPUT
        // -----------------------------------------------------

        if (targetInput != null)
        {
            targetInput.SetInputEnabled(false);
        }

        // -----------------------------------------------------
        // PARAR PLAYER
        // -----------------------------------------------------

        if (targetBody != null)
        {
            targetBody.linearVelocity = new Vector3(
                0f,
                targetBody.linearVelocity.y,
                0f
            );

            targetBody.angularVelocity = Vector3.zero;
        }

        // -----------------------------------------------------
        // ESPERAR STUN
        // -----------------------------------------------------

        yield return new WaitForSeconds(stunDuration);

        // -----------------------------------------------------
        // DEVOLVER INPUT
        // -----------------------------------------------------

        if (targetInput != null)
        {
            // Solo devolverlo si sigue siendo el jugador local.
            targetInput.SetInputEnabled(
                targetPlayer.IsOwner
            );
        }

        Debug.Log(
            $"[{name}] STUN TERMINADO → {targetPlayer.name}"
        );

        isStunning = false;
    }

    // =========================================================
    // MIRAR AL PLAYER
    // =========================================================

    void LookAtPlayer()
    {
        if (targetPlayer == null)
            return;

        Vector3 direction;

        if (frontPoint != null)
        {
            direction =
                targetPlayer.transform.position -
                frontPoint.position;
        }
        else
        {
            direction =
                targetPlayer.transform.position -
                transform.position;
        }

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
            return;

        direction.Normalize();

        Quaternion targetRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );

        body.MoveRotation(targetRotation);
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    void OnDrawGizmosSelected()
    {
        // Detección.
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            detectionRange
        );

        // Ataque / stun.
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            attackRange
        );

        // FrontPoint.
        if (frontPoint != null)
        {
            Gizmos.color = Color.blue;

            Gizmos.DrawSphere(
                frontPoint.position,
                0.1f
            );

            Gizmos.DrawLine(
                transform.position,
                frontPoint.position
            );
        }
    }
}