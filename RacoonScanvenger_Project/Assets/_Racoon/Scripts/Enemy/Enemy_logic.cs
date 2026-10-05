using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;
using Racoon.Player;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NavMeshAgent))]
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
    NavMeshAgent agent;

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
        agent = GetComponent<NavMeshAgent>();

        // El NavMeshAgent mueve al enemigo.
        rb.isKinematic = true;
        rb.useGravity = false;

        rb.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        agent.updatePosition = true;
        agent.updateRotation = true;

        agent.speed = patrolSpeed;
        agent.stoppingDistance = 0.1f;
    }

    // =========================================================
    // NETWORK
    // =========================================================

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        Debug.Log($"[{name}] ENEMY SPAWNED");

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
        {
            Debug.LogError(
                $"[{name}] NO TIENE PUNTOS DE PATRULLA",
                this
            );

            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError(
                $"[{name}] NO ESTÁ SOBRE EL NAVMESH",
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

            agent.isStopped = false;
            agent.speed = chaseSpeed;

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
    // FIXED UPDATE
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

        // Comprobar si hemos llegado al punto.
        if (!agent.pathPending &&
            agent.remainingDistance <= pointReachDistance)
        {
            OnPointReached();
        }
    }

    void GoToCurrentPoint()
    {
        if (patrolPoints == null ||
            patrolPoints.Length == 0)
            return;

        if (patrolPoints[currentPoint] == null)
            return;

        waitingAtPoint = false;

        agent.isStopped = false;
        agent.speed = patrolSpeed;

        // Dejamos que pointReachDistance
        // controle cuándo hemos llegado.
        agent.stoppingDistance = 0.1f;

        agent.SetDestination(
            patrolPoints[currentPoint].position
        );

        Debug.Log(
            $"[{name}] → PUNTO {currentPoint}"
        );
    }

    void OnPointReached()
    {
        if (waitingAtPoint)
            return;

        PatrolPoint patrolPoint =
            patrolPoints[currentPoint]
                .GetComponent<PatrolPoint>();

        // Punto normal.
        if (patrolPoint == null ||
            !patrolPoint.isKeyPoint)
        {
            NextPoint();
            return;
        }

        // Punto clave.
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
            // Comprobar si detecta al jugador.
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

                    agent.isStopped = false;
                    agent.speed = chaseSpeed;

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

        NextPoint();
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

        // Dentro del rango de ataque.
        if (distance <= attackRange)
        {
            StopEnemy();

            TryStun();

            return;
        }

        agent.isStopped = false;
        agent.speed = chaseSpeed;

        // NavMesh calcula automáticamente
        // cómo llegar hasta el jugador.
        agent.SetDestination(
            targetPlayer.transform.position
        );
    }

    // =========================================================
    // MOVIMIENTO
    // =========================================================

    void StopEnemy()
    {
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
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
                client.PlayerObject
                    .GetComponent<PlayerController>();

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

                agent.isStopped = false;
                agent.speed = chaseSpeed;

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