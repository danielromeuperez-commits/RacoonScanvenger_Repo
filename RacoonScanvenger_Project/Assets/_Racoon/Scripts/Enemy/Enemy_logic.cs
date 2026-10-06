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

    private Rigidbody rb;
    private NavMeshAgent agent;

    private PlayerController targetPlayer;
    private PlayerInputHandler targetInput;
    private Rigidbody targetRigidbody;

    private int currentPoint;

    private bool chasing;
    private bool waitingAtPoint;
    private bool stunned;

    private float lastStunTime = -Mathf.Infinity;

    private Coroutine waitCoroutine;
    private Coroutine stunCoroutine;

    // =========================================================
    // AWAKE
    // =========================================================

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        agent = GetComponent<NavMeshAgent>();

        // El NavMeshAgent controla el movimiento.
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
        base.OnNetworkSpawn();

        // SOLO EL SERVIDOR CONTROLA LA IA.
        if (!IsServer)
        {
            // El cliente NO debe intentar mover el NavMeshAgent.
            agent.isStopped = true;
            agent.enabled = false;

            return;
        }

        Debug.Log($"[{name}] ENEMY SPAWNED EN SERVIDOR");

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

    private void Update()
    {
        if (!IsServer)
            return;

        if (stunned)
            return;

        // Buscar jugador.
        if (targetPlayer == null)
        {
            FindPlayer();
        }

        // Todavía no tenemos jugador.
        if (targetPlayer == null)
        {
            PatrolMovement();
            return;
        }

        float distance = Vector3.Distance(
            transform.position,
            targetPlayer.transform.position
        );

        // =====================================================
        // DETECTAR PLAYER
        // =====================================================

        if (!chasing &&
            distance <= detectionRange)
        {
            StartChasing();
        }

        // =====================================================
        // PERSECUCIÓN TERMINADA
        // =====================================================

        if (chasing &&
            distance > detectionRange)
        {
            StopChasing();

            GoToCurrentPoint();

            return;
        }

        // =====================================================
        // PERSECUCIÓN
        // =====================================================

        if (chasing)
        {
            ChasePlayer(distance);
            return;
        }

        // =====================================================
        // PATRULLA
        // =====================================================

        PatrolMovement();
    }

    // =========================================================
    // PATRULLA
    // =========================================================

    private void PatrolMovement()
    {
        if (stunned)
            return;

        if (chasing)
            return;

        if (waitingAtPoint)
            return;

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
            return;

        if (!agent.enabled)
            return;

        if (!agent.isOnNavMesh)
            return;

        Transform point =
            patrolPoints[currentPoint];

        if (point == null)
        {
            NextPoint();
            return;
        }

        if (!agent.pathPending &&
            agent.remainingDistance <= pointReachDistance)
        {
            OnPointReached();
        }
    }

    private void GoToCurrentPoint()
    {
        if (!IsServer)
            return;

        if (patrolPoints == null ||
            patrolPoints.Length == 0)
            return;

        if (patrolPoints[currentPoint] == null)
            return;

        if (!agent.enabled ||
            !agent.isOnNavMesh)
            return;

        waitingAtPoint = false;

        agent.isStopped = false;
        agent.speed = patrolSpeed;
        agent.stoppingDistance = 0.1f;

        agent.SetDestination(
            patrolPoints[currentPoint].position
        );

        Debug.Log(
            $"[{name}] → PUNTO {currentPoint}"
        );
    }

    private void OnPointReached()
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

    private IEnumerator WaitAtKeyPoint(float waitTime)
    {
        waitingAtPoint = true;

        StopEnemy();

        Debug.Log(
            $"[{name}] PUNTO CLAVE → ESPERANDO {waitTime} SEGUNDOS"
        );

        float timer = 0f;

        while (timer < waitTime)
        {
            if (targetPlayer != null &&
                !stunned)
            {
                float distance =
                    Vector3.Distance(
                        transform.position,
                        targetPlayer.transform.position
                    );

                if (distance <= detectionRange)
                {
                    waitingAtPoint = false;
                    waitCoroutine = null;

                    StartChasing();

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

    private void NextPoint()
    {
        if (!IsServer)
            return;

        currentPoint++;

        if (currentPoint >= patrolPoints.Length)
            currentPoint = 0;

        GoToCurrentPoint();
    }

    // =========================================================
    // PERSECUCIÓN
    // =========================================================

    private void StartChasing()
    {
        if (stunned)
            return;

        if (targetPlayer == null)
            return;

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

    private void StopChasing()
    {
        chasing = false;

        Debug.Log(
            $"[{name}] PLAYER FUERA DE RANGO → PATRULLA"
        );
    }

    private void ChasePlayer(float distance)
    {
        if (!IsServer)
            return;

        if (targetPlayer == null)
            return;

        if (!agent.enabled ||
            !agent.isOnNavMesh)
            return;

        // =====================================================
        // ATAQUE
        // =====================================================

        if (distance <= attackRange)
        {
            StopEnemy();

            TryStun();

            return;
        }

        // =====================================================
        // PERSEGUIR
        // =====================================================

        agent.isStopped = false;
        agent.speed = chaseSpeed;

        agent.SetDestination(
            targetPlayer.transform.position
        );
    }

    // =========================================================
    // MOVIMIENTO
    // =========================================================

    private void StopEnemy()
    {
        if (!agent.enabled)
            return;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    // =========================================================
    // BUSCAR PLAYER
    // =========================================================

    private void FindPlayer()
    {
        if (!IsServer)
            return;

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

    private void TryStun()
    {
        if (!IsServer)
            return;

        if (targetPlayer == null)
            return;

        if (stunned)
            return;

        if (Time.time <
            lastStunTime + stunCooldown)
            return;

        lastStunTime = Time.time;

        if (stunCoroutine != null)
        {
            StopCoroutine(stunCoroutine);
        }

        stunCoroutine =
            StartCoroutine(
                StunPlayer()
            );
    }

    private IEnumerator StunPlayer()
    {
        if (targetPlayer == null)
            yield break;

        stunned = true;
        chasing = false;

        StopEnemy();

        Debug.Log(
            $"[{name}] STUN → {targetPlayer.name}"
        );

        // =====================================================
        // PARAR PLAYER
        // =====================================================

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

        // =====================================================
        // DESACTIVAR INPUT
        // =====================================================

        if (targetInput != null)
        {
            targetInput.SetInputEnabled(false);
        }

        // =====================================================
        // ESPERAR STUN
        // =====================================================

        yield return new WaitForSeconds(
            stunDuration
        );

        // =====================================================
        // DEVOLVER INPUT
        // =====================================================

        if (targetInput != null)
        {
            targetInput.SetInputEnabled(
                targetPlayer != null &&
                targetPlayer.IsOwner
            );
        }

        stunned = false;
        stunCoroutine = null;

        // =====================================================
        // COMPROBAR DISTANCIA
        // =====================================================

        if (targetPlayer != null)
        {
            float distance =
                Vector3.Distance(
                    transform.position,
                    targetPlayer.transform.position
                );

            if (distance <= detectionRange)
            {
                StartChasing();

                Debug.Log(
                    $"[{name}] STUN TERMINADO → SIGUE PERSIGUIENDO"
                );
            }
            else
            {
                StopChasing();

                Debug.Log(
                    $"[{name}] STUN TERMINADO → VUELVE A PATRULLA"
                );

                GoToCurrentPoint();
            }
        }
        else
        {
            StopChasing();

            GoToCurrentPoint();
        }
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    private void OnDrawGizmosSelected()
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