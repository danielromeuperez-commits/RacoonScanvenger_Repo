using System.Collections;
using UnityEngine;
using Unity.Netcode;
using Racoon.Player;

[RequireComponent(typeof(Rigidbody))]
public class Enemy_logic : NetworkBehaviour
{
    [Header("PATRULLA")]
    [SerializeField] Transform[] patrolPoints;
    [SerializeField] float patrolSpeed = 2f;
    [SerializeField] float pointReachDistance = 0.5f;

    [Header("PUNTOS CLAVE")]
    [SerializeField] float keyPointWaitTime = 3f;

    [Header("DETECCIÓN")]
    [SerializeField] float detectionRange = 10f;

    [Header("PERSECUCIÓN")]
    [SerializeField] float chaseSpeed = 3f;
    [SerializeField] float attackRange = 1.5f;

    [Header("STUN")]
    [SerializeField] float stunDuration = 2f;
    [SerializeField] float stunCooldown = 1f;

    Rigidbody rb;

    PlayerController targetPlayer;
    PlayerInputHandler targetInput;
    Rigidbody targetRigidbody;

    int currentPoint;

    bool chasing;
    bool waitingAtPoint;
    bool stunned;

    float lastStunTime = -Mathf.Infinity;

    Coroutine waitCoroutine;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // El servidor mueve al enemigo.
        rb.isKinematic = false;
        rb.useGravity = true;

        rb.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }

    // =========================================================
    // NETWORK
    // =========================================================

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Debug.Log(
            $"[{name}] ENEMY SPAWNED"
        );

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
        {
            Debug.LogError(
                $"[{name}] NO TIENE PUNTOS DE PATRULLA",
                this
            );

            return;
        }

        currentPoint = 0;

        GoToCurrentPoint();
    }

    // =========================================================
    // UPDATE
    // =========================================================

    void Update()
    {
        if (!IsServer)
            return;

        // Buscar player si todavía no tenemos uno.
        if (targetPlayer == null)
        {
            FindPlayer();
        }

        if (targetPlayer == null)
            return;

        float distance = Vector3.Distance(
            transform.position,
            targetPlayer.transform.position
        );

        // =====================================================
        // PLAYER DENTRO DEL RANGO
        // =====================================================

        if (!chasing &&
            !stunned &&
            distance <= detectionRange)
        {
            chasing = true;
            waitingAtPoint = false;

            if (waitCoroutine != null)
            {
                StopCoroutine(waitCoroutine);
                waitCoroutine = null;
            }

            Debug.Log(
                $"[{name}] PLAYER DETECTADO → PERSIGUIENDO"
            );
        }

        // =====================================================
        // PLAYER FUERA DEL RANGO
        // =====================================================

        if (chasing &&
            !stunned &&
            distance > detectionRange)
        {
            chasing = false;

            Debug.Log(
                $"[{name}] PLAYER FUERA DE RANGO → VUELVE A PATRULLA"
            );

            GoToCurrentPoint();
        }

        // =====================================================
        // PERSECUCIÓN
        // =====================================================

        if (chasing && !stunned)
        {
            ChasePlayer(distance);
        }
    }

    // =========================================================
    // FÍSICA
    // =========================================================

    void FixedUpdate()
    {
        if (!IsServer)
            return;

        if (stunned)
            return;

        if (chasing)
            return;

        PatrolMovement();
    }

    // =========================================================
    // PATRULLA
    // =========================================================

    void PatrolMovement()
    {
        if (patrolPoints == null ||
            patrolPoints.Length == 0)
            return;

        if (waitingAtPoint)
            return;

        Transform point =
            patrolPoints[currentPoint];

        if (point == null)
        {
            NextPoint();
            return;
        }

        Vector3 direction =
            point.position -
            transform.position;

        direction.y = 0f;

        float distance = direction.magnitude;

        // Llegamos al punto.
        if (distance <= pointReachDistance)
        {
            OnPointReached();

            return;
        }

        direction.Normalize();

        Move(direction, patrolSpeed);
    }

    void GoToCurrentPoint()
    {
        if (patrolPoints == null ||
            patrolPoints.Length == 0)
            return;

        if (patrolPoints[currentPoint] == null)
            return;

        waitingAtPoint = false;

        Debug.Log(
            $"[{name}] → PUNTO {currentPoint}"
        );
    }

    void OnPointReached()
    {
        if (waitingAtPoint)
            return;

        PatrolPoint patrolPoint =
            patrolPoints[currentPoint].GetComponent<PatrolPoint>();

        // Si no tiene PatrolPoint, es un punto normal.
        if (patrolPoint == null || !patrolPoint.isKeyPoint)
        {
            NextPoint();
            return;
        }

        // Es un punto clave.
        waitCoroutine =
            StartCoroutine(
                WaitAtKeyPoint(patrolPoint.waitTime)
            );
    }

    IEnumerator WaitAtKeyPoint(float waitTime)
    {
        waitingAtPoint = true;

        StopEnemy();

        Debug.Log(
            $"[{name}] PUNTO CLAVE → ESPERANDO {waitTime} SEGUNDOS"
        );

        float timer = 0f;

        while (timer < waitTime)
        {
            // Si detecta al player mientras espera,
            // cancela la espera y empieza a perseguir.
            if (targetPlayer != null)
            {
                float distance = Vector3.Distance(
                    transform.position,
                    targetPlayer.transform.position
                );

                if (distance <= detectionRange)
                {
                    waitingAtPoint = false;
                    chasing = true;

                    Debug.Log(
                        $"[{name}] PLAYER DETECTADO EN PUNTO CLAVE"
                    );

                    yield break;
                }
            }

            timer += Time.deltaTime;

            yield return null;
        }

        waitingAtPoint = false;

        waitCoroutine = null;

        // Continuar al siguiente punto.
        NextPoint();
    }

    IEnumerator WaitAtKeyPoint()
    {
        waitingAtPoint = true;

        Debug.Log(
            $"[{name}] PUNTO CLAVE → ESPERA {keyPointWaitTime}s"
        );

        float timer = 0f;

        while (timer < keyPointWaitTime)
        {
            // Si aparece un player durante la espera,
            // se cancela la espera.
            if (targetPlayer != null)
            {
                float distance = Vector3.Distance(
                    transform.position,
                    targetPlayer.transform.position
                );

                if (distance <= detectionRange)
                {
                    waitingAtPoint = false;
                    chasing = true;

                    Debug.Log(
                        $"[{name}] PLAYER DETECTADO EN PUNTO CLAVE"
                    );

                    yield break;
                }
            }

            timer += Time.deltaTime;

            yield return null;
        }

        waitingAtPoint = false;

        NextPoint();

        waitCoroutine = null;
    }

    void NextPoint()
    {
        currentPoint++;

        if (currentPoint >= patrolPoints.Length)
            currentPoint = 0;

        GoToCurrentPoint();
    }

    // =========================================================
    // PERSECUCIÓN
    // =========================================================

    void ChasePlayer(float distance)
    {
        if (targetPlayer == null)
            return;

        if (distance <= attackRange)
        {
            StopEnemy();

            TryStun();

            return;
        }

        Vector3 direction =
            targetPlayer.transform.position -
            transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
            return;

        direction.Normalize();

        Move(direction, chaseSpeed);
    }

    // =========================================================
    // MOVIMIENTO
    // =========================================================

    void Move(
        Vector3 direction,
        float speed)
    {
        if (direction.sqrMagnitude < 0.001f)
            return;

        Vector3 targetVelocity =
            direction * speed;

        Vector3 currentVelocity =
            rb.linearVelocity;

        rb.linearVelocity =
            new Vector3(
                targetVelocity.x,
                currentVelocity.y,
                targetVelocity.z
            );

        Quaternion targetRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );

        rb.MoveRotation(
            Quaternion.RotateTowards(
                rb.rotation,
                targetRotation,
                720f * Time.fixedDeltaTime
            )
        );
    }

    void StopEnemy()
    {
        rb.linearVelocity =
            new Vector3(
                0f,
                rb.linearVelocity.y,
                0f
            );
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
            in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            PlayerController player =
                client.PlayerObject.GetComponent<PlayerController>();

            if (player == null)
                continue;

            targetPlayer = player;

            targetInput =
                player.GetComponent<PlayerInputHandler>();

            targetRigidbody =
                player.GetComponent<Rigidbody>();

            Debug.Log(
                $"[{name}] PLAYER ENCONTRADO: {player.name}"
            );

            return;
        }
    }

    // =========================================================
    // STUN
    // =========================================================

    void TryStun()
    {
        if (targetPlayer == null)
            return;

        if (stunned)
            return;

        if (Time.time <
            lastStunTime + stunCooldown)
            return;

        lastStunTime = Time.time;

        StartCoroutine(
            StunPlayer()
        );
    }

    IEnumerator StunPlayer()
    {
        if (targetPlayer == null)
            yield break;

        stunned = true;
        chasing = false;

        StopEnemy();

        Debug.Log(
            $"[{name}] STUN → {targetPlayer.name}"
        );

        // -----------------------------------------------------
        // PARAR PLAYER
        // -----------------------------------------------------

        if (targetRigidbody != null)
        {
            Vector3 velocity =
                targetRigidbody.linearVelocity;

            targetRigidbody.linearVelocity =
                new Vector3(
                    0f,
                    velocity.y,
                    0f
                );

            targetRigidbody.angularVelocity =
                Vector3.zero;
        }

        // -----------------------------------------------------
        // DESACTIVAR INPUT
        // -----------------------------------------------------

        if (targetInput != null)
        {
            targetInput.SetInputEnabled(false);
        }

        // -----------------------------------------------------
        // ESPERAR STUN
        // -----------------------------------------------------

        yield return new WaitForSeconds(
            stunDuration
        );

        // -----------------------------------------------------
        // DEVOLVER INPUT
        // -----------------------------------------------------

        if (targetInput != null)
        {
            targetInput.SetInputEnabled(
                targetPlayer != null &&
                targetPlayer.IsOwner
            );
        }

        stunned = false;

        // -----------------------------------------------------
        // COMPROBAR RANGO
        // -----------------------------------------------------

        if (targetPlayer != null)
        {
            float distance = Vector3.Distance(
                transform.position,
                targetPlayer.transform.position
            );

            if (distance <= detectionRange)
            {
                chasing = true;

                Debug.Log(
                    $"[{name}] STUN TERMINADO → SIGUE PERSIGUIENDO"
                );
            }
            else
            {
                chasing = false;

                Debug.Log(
                    $"[{name}] STUN TERMINADO → VUELVE A PATRULLA"
                );

                GoToCurrentPoint();
            }
        }
        else
        {
            chasing = false;

            GoToCurrentPoint();
        }
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            detectionRange
        );

        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            attackRange
        );
    }
}