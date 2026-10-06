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

    [Header("VUELTA A PATRULLA")]
    [SerializeField] float returnToPatrolDelay = 1.5f;

    [Header("STUN")]
    [SerializeField] float stunDuration = 2f;
    [SerializeField] float stunCooldown = 1f;

    // =========================================================
    // COMPONENTES
    // =========================================================

    Rigidbody rb;
    NavMeshAgent agent;

    // =========================================================
    // PLAYER OBJETIVO
    // =========================================================

    PlayerController targetPlayer;
    PlayerInputHandler targetInput;
    Rigidbody targetRigidbody;

    // =========================================================
    // ESTADO
    // =========================================================

    int currentPoint;

    bool chasing;
    bool waitingAtPoint;
    bool stunned;

    float lastStunTime = -Mathf.Infinity;

    // =========================================================
    // COROUTINES
    // =========================================================

    Coroutine waitCoroutine;
    Coroutine stunCoroutine;
    Coroutine returnPatrolCoroutine;

    // =========================================================
    // AWAKE
    // =========================================================

    void Awake()
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
            // El cliente no mueve el NavMeshAgent.
            agent.isStopped = true;
            agent.enabled = false;

            return;
        }

        Debug.Log(
            $"[{name}] ENEMY SPAWNED EN SERVIDOR"
        );

        // Comprobar puntos de patrulla.
        if (patrolPoints == null ||
            patrolPoints.Length == 0)
        {
            Debug.LogError(
                $"[{name}] NO TIENE PUNTOS DE PATRULLA",
                this
            );

            return;
        }

        // Comprobar NavMesh.
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

        if (stunned)
            return;

        // =====================================================
        // BUSCAR JUGADOR MÁS CERCANO
        // =====================================================

        FindClosestPlayer();

        // =====================================================
        // SI ESTAMOS ESPERANDO PARA VOLVER A PATRULLA
        // =====================================================

        if (returnPatrolCoroutine != null)
        {
            // Si un jugador vuelve a entrar en rango
            // durante la pausa, reanudamos persecución.
            if (targetPlayer != null)
            {
                float distanceToTarget =
                    Vector3.Distance(
                        transform.position,
                        targetPlayer.transform.position
                    );

                if (distanceToTarget <= detectionRange)
                {
                    StopReturnToPatrol();

                    StartChasing();

                    return;
                }
            }

            // Mientras esperamos no hacemos nada más.
            return;
        }

        // =====================================================
        // NO HAY PLAYER CERCA
        // =====================================================

        if (targetPlayer == null)
        {
            if (chasing)
            {
                StartReturnToPatrol();
            }
            else
            {
                PatrolMovement();
            }

            return;
        }

        // =====================================================
        // DISTANCIA AL PLAYER
        // =====================================================

        float distance = Vector3.Distance(
            transform.position,
            targetPlayer.transform.position
        );

        // =====================================================
        // PLAYER DENTRO DEL RANGO
        // =====================================================

        if (!chasing &&
            distance <= detectionRange)
        {
            StartChasing();
        }

        // =====================================================
        // PLAYER FUERA DEL RANGO
        // =====================================================

        if (chasing &&
            distance > detectionRange)
        {
            StartReturnToPatrol();

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

    void PatrolMovement()
    {
        if (stunned)
            return;

        if (chasing)
            return;

        if (returnPatrolCoroutine != null)
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

        // Comprobar si hemos llegado.
        if (!agent.pathPending &&
            agent.remainingDistance <= pointReachDistance)
        {
            OnPointReached();
        }
    }

    // =========================================================
    // IR AL PUNTO ACTUAL
    // =========================================================

    void GoToCurrentPoint()
    {
        if (!IsServer)
            return;

        // Cancelar pausa de vuelta a patrulla.
        if (returnPatrolCoroutine != null)
        {
            StopCoroutine(returnPatrolCoroutine);

            returnPatrolCoroutine = null;
        }

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

    // =========================================================
    // PUNTO ALCANZADO
    // =========================================================

    void OnPointReached()
    {
        if (waitingAtPoint)
            return;

        PatrolPoint patrolPoint =
            patrolPoints[currentPoint]
                .GetComponent<PatrolPoint>();

        // -----------------------------------------------------
        // PUNTO NORMAL
        // -----------------------------------------------------

        if (patrolPoint == null ||
            !patrolPoint.isKeyPoint)
        {
            NextPoint();

            return;
        }

        // -----------------------------------------------------
        // PUNTO CLAVE
        // -----------------------------------------------------

        waitCoroutine =
            StartCoroutine(
                WaitAtKeyPoint(
                    patrolPoint.waitTime
                )
            );
    }

    // =========================================================
    // ESPERAR EN PUNTO CLAVE
    // =========================================================

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
            // Comprobar si hay un jugador cerca.
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

    // =========================================================
    // SIGUIENTE PUNTO
    // =========================================================

    void NextPoint()
    {
        if (!IsServer)
            return;

        currentPoint++;

        if (currentPoint >= patrolPoints.Length)
            currentPoint = 0;

        GoToCurrentPoint();
    }

    // =========================================================
    // INICIAR PERSECUCIÓN
    // =========================================================

    void StartChasing()
    {
        if (!IsServer)
            return;

        if (stunned)
            return;

        if (targetPlayer == null)
            return;

        // Cancelar espera en punto clave.
        if (waitCoroutine != null)
        {
            StopCoroutine(waitCoroutine);

            waitCoroutine = null;
        }

        // Cancelar pausa de vuelta a patrulla.
        if (returnPatrolCoroutine != null)
        {
            StopCoroutine(returnPatrolCoroutine);

            returnPatrolCoroutine = null;
        }

        chasing = true;
        waitingAtPoint = false;

        agent.isStopped = false;
        agent.speed = chaseSpeed;

        Debug.Log(
            $"[{name}] PLAYER DETECTADO → PERSIGUIENDO A {targetPlayer.name}"
        );
    }

    // =========================================================
    // INICIAR VUELTA A PATRULLA
    // =========================================================

    void StartReturnToPatrol()
    {
        if (!IsServer)
            return;

        if (stunned)
            return;

        if (returnPatrolCoroutine != null)
            return;

        chasing = false;

        returnPatrolCoroutine =
            StartCoroutine(
                ReturnToPatrolAfterDelay()
            );
    }

    // =========================================================
    // PAUSA ANTES DE VOLVER A PATRULLA
    // =========================================================

    IEnumerator ReturnToPatrolAfterDelay()
    {
        Debug.Log(
            $"[{name}] PLAYER FUERA DE RANGO → ESPERANDO {returnToPatrolDelay} SEGUNDOS"
        );

        // -----------------------------------------------------
        // PARAR ENEMIGO
        // -----------------------------------------------------

        StopEnemy();

        float timer = 0f;

        while (timer < returnToPatrolDelay)
        {
            if (stunned)
                yield break;

            // Buscar si algún jugador ha vuelto
            // a entrar dentro del rango.
            FindClosestPlayer();

            if (targetPlayer != null)
            {
                float distance =
                    Vector3.Distance(
                        transform.position,
                        targetPlayer.transform.position
                    );

                if (distance <= detectionRange)
                {
                    Debug.Log(
                        $"[{name}] PLAYER VOLVIÓ DURANTE LA PAUSA → PERSIGUIENDO"
                    );

                    returnPatrolCoroutine = null;

                    StartChasing();

                    yield break;
                }
            }

            timer += Time.deltaTime;

            yield return null;
        }

        // -----------------------------------------------------
        // FIN DE LA PAUSA
        // -----------------------------------------------------

        returnPatrolCoroutine = null;

        chasing = false;

        Debug.Log(
            $"[{name}] PAUSA TERMINADA → VUELVE A PATRULLAR"
        );

        GoToCurrentPoint();
    }

    // =========================================================
    // CANCELAR PAUSA
    // =========================================================

    void StopReturnToPatrol()
    {
        if (returnPatrolCoroutine == null)
            return;

        StopCoroutine(
            returnPatrolCoroutine
        );

        returnPatrolCoroutine = null;

        Debug.Log(
            $"[{name}] PAUSA DE VUELTA A PATRULLA CANCELADA"
        );
    }

    // =========================================================
    // PERSECUCIÓN
    // =========================================================

    void ChasePlayer(float distance)
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
    // PARAR ENEMIGO
    // =========================================================

    void StopEnemy()
    {
        if (!agent.enabled)
            return;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    // =========================================================
    // BUSCAR PLAYER MÁS CERCANO
    // =========================================================

    void FindClosestPlayer()
    {
        if (!IsServer)
            return;

        if (NetworkManager.Singleton == null)
            return;

        PlayerController closestPlayer = null;

        float closestDistance = detectionRange;

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

            float distance =
                Vector3.Distance(
                    transform.position,
                    player.transform.position
                );

            // Solo jugadores dentro del rango.
            if (distance > detectionRange)
                continue;

            // Buscar el más cercano.
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = player;
            }
        }

        // =====================================================
        // NO HAY NINGÚN PLAYER CERCA
        // =====================================================

        if (closestPlayer == null)
        {
            targetPlayer = null;
            targetInput = null;
            targetRigidbody = null;

            return;
        }

        // =====================================================
        // CAMBIÓ EL OBJETIVO
        // =====================================================

        if (targetPlayer != closestPlayer)
        {
            targetPlayer = closestPlayer;

            targetInput =
                closestPlayer.GetComponent<PlayerInputHandler>();

            targetRigidbody =
                closestPlayer.GetComponent<Rigidbody>();

            Debug.Log(
                $"[{name}] OBJETIVO → {closestPlayer.name}"
            );
        }
    }

    // =========================================================
    // STUN
    // =========================================================

    void TryStun()
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

    // =========================================================
    // STUN PLAYER
    // =========================================================

    IEnumerator StunPlayer()
    {
        if (targetPlayer == null)
            yield break;

        stunned = true;
        chasing = false;

        // Cancelar pausa de vuelta a patrulla.
        if (returnPatrolCoroutine != null)
        {
            StopCoroutine(
                returnPatrolCoroutine
            );

            returnPatrolCoroutine = null;
        }

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
        // COMPROBAR RANGO
        // =====================================================

        FindClosestPlayer();

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
                StartReturnToPatrol();

                Debug.Log(
                    $"[{name}] STUN TERMINADO → PAUSA → PATRULLA"
                );
            }
        }
        else
        {
            StartReturnToPatrol();
        }
    }

    // =========================================================
    // GIZMOS
    // =========================================================

    void OnDrawGizmosSelected()
    {
        // Rango de detección.
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            detectionRange
        );

        // Rango de ataque.
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            attackRange
        );
    }
}
