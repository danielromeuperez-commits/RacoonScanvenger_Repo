using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Cáscara de plátano. Al usarla, el servidor crea una banana física
    /// y la lanza hacia delante del jugador (o la suelta detrás si apuntaba hacia atrás).
    /// </summary>
    [Serializable]
    public class BananaEffect : ItemEffect
    {
        [SerializeField] NetworkObject bananaPrefab;

        [Tooltip("Distancia delante del jugador donde aparece la banana.")]
        [SerializeField] float spawnForwardDistance = 0.8f;

        [Tooltip("Altura desde la que sale la banana.")]
        [SerializeField] float spawnHeight = 0.8f;

        [Tooltip("Velocidad hacia delante.")]
        [SerializeField] float throwSpeed = 6f;

        [Tooltip("Impulso vertical al lanzar la banana.")]
        [SerializeField] float upwardSpeed = 2f;

        [Tooltip("Vueltas por segundo que da en el aire (0 = no gira).")]
        [SerializeField, Min(0f)] float spinTurnsPerSecond = 2.5f;

        [Header("Soltar detrás (stick hacia atrás al apuntar)")]
        [Tooltip("Distancia detrás del jugador donde aparece.")]
        [SerializeField] float dropBackDistance = 0.7f;

        [Tooltip("Velocidad hacia atrás al soltarla (pequeña: cae casi a los pies).")]
        [SerializeField] float dropBackSpeed = 1.5f;

        [Tooltip("Impulso vertical al soltarla detrás.")]
        [SerializeField] float dropUpwardSpeed = 1f;

        [Header("Apuntar (gatillo + stick)")]
        [Tooltip("Hasta dónde se puede mandar el punto de caída desde el jugador (radio del círculo, metros).")]
        [SerializeField, Min(1f)] float maxAimRange = 8f;

        [Tooltip("Lo más cerca del jugador que puede caer al apuntar (metros).")]
        [SerializeField, Min(0f)] float minAimRange = 1.3f;

        [Tooltip("Capas contra las que se busca la altura del suelo en el punto de caída.")]
        [SerializeField] LayerMask aimGroundMask = Physics.DefaultRaycastLayers;

        const float AimProbeHeight = 2f, AimProbeDepth = 20f, MinFlightTime = 0.25f;

        public override float MaxAimRange => maxAimRange;

        public override void ServerUse(PlayerController user, ItemData item) => ServerUse(user, item, false);

        public override void ServerUse(PlayerController user, ItemData item, bool dropBehind)
        {
            if (user == null) return;

            TryGetThrow(user, dropBehind, out Vector3 spawnPosition, out Vector3 velocity);
            Launch(user, spawnPosition, velocity);
        }

        public override void ServerUseAt(PlayerController user, ItemData item, Vector3 target)
        {
            if (user == null) return;

            TryGetThrowAt(user, target, out Vector3 spawnPosition, out Vector3 velocity);
            Launch(user, spawnPosition, velocity);
        }

        void Launch(PlayerController user, Vector3 spawnPosition, Vector3 velocity)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (bananaPrefab == null) return;

            Vector3 facing = new Vector3(velocity.x, 0f, velocity.z);
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;

            NetworkObject banana = UnityEngine.Object.Instantiate(
                bananaPrefab,
                spawnPosition,
                Quaternion.LookRotation(facing)
            );

            banana.Spawn();

            // Quién la ha lanzado (para el «¡AUTOGOL!» y, más adelante, kill-feed y estadísticas).
            if (banana.TryGetComponent(out BananaTrap trap))
                trap.ServerSetThrower(user);

            // El servidor controla la física de la banana.
            if (banana.TryGetComponent(out Rigidbody body))
            {
                body.linearVelocity = velocity;

                // Voltea por el aire (al aterrizar BananaTrap la deja derecha).
                if (spinTurnsPerSecond > 0f)
                {
                    float radiansPerSecond = spinTurnsPerSecond * 2f * Mathf.PI;
                    body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, radiansPerSecond);
                    body.angularVelocity = Vector3.Cross(Vector3.up, facing.normalized) * radiansPerSecond;
                }
            }
        }

        // Lo usan el servidor (para lanzarla) y PlayerItemHandler (para dibujar el arco): siempre coinciden.
        public override bool TryGetThrow(PlayerController user, bool dropBehind, out Vector3 spawnPosition, out Vector3 velocity)
        {
            Vector3 forward = Forward(user);

            if (dropBehind)
            {
                spawnPosition = user.transform.position - forward * dropBackDistance + Vector3.up * spawnHeight;
                velocity = -forward * dropBackSpeed + Vector3.up * dropUpwardSpeed;
            }
            else
            {
                // La creamos un poco delante y por encima del jugador
                // para evitar que choque inmediatamente con él.
                spawnPosition = user.transform.position + forward * spawnForwardDistance + Vector3.up * spawnHeight;
                velocity = forward * throwSpeed + Vector3.up * upwardSpeed;
            }

            return true;
        }

        // Mismo ángulo de salida que el lanzamiento normal: lo que cambia con la distancia es la fuerza.
        public override bool TryGetFlightTime(PlayerController user, Vector3 target, out float flightTime)
        {
            Solve(user, target, out _, out _, out _, out _, out flightTime);
            return true;
        }

        public override bool TryGetThrowAt(PlayerController user, Vector3 target, out Vector3 spawnPosition, out Vector3 velocity)
        {
            Solve(user, target, out spawnPosition, out Vector3 direction, out float flat, out float drop, out float flightTime);
            float gravity = -Physics.gravity.y;

            velocity = direction * (flat / flightTime) +
                       Vector3.up * ((0.5f * gravity * flightTime * flightTime - drop) / flightTime);
            return true;
        }

        void Solve(PlayerController user, Vector3 target, out Vector3 spawnPosition, out Vector3 direction,
                   out float flat, out float drop, out float flightTime)
        {
            Vector3 origin = user.transform.position;
            Vector3 offset = Vector3.ProjectOnPlane(target - origin, Vector3.up);
            float distance = offset.magnitude;
            direction = distance > 0.01f ? offset / distance : Forward(user);

            // El servidor también pasa por aquí: un punto fuera del círculo no llega más lejos.
            distance = Mathf.Clamp(distance, minAimRange, maxAimRange);
            spawnPosition = origin + direction * spawnForwardDistance + Vector3.up * spawnHeight;

            float landingY = GroundHeight(origin + direction * distance, origin.y);
            drop = spawnPosition.y - landingY;
            flat = Mathf.Max(0f, distance - spawnForwardDistance);
            float gravity = -Physics.gravity.y;

            flightTime = Mathf.Max(
                MinFlightTime,
                Mathf.Sqrt(Mathf.Max(0f, 2f * (flat * upwardSpeed / throwSpeed + drop) / gravity))
            );
        }

        // Dirección horizontal hacia la que mira el jugador.
        static Vector3 Forward(PlayerController user)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                user.transform.forward,
                Vector3.up
            ).normalized;

            return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward;
        }

        float GroundHeight(Vector3 point, float fallback)
        {
            Vector3 from = new(point.x, fallback + AimProbeHeight, point.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, AimProbeDepth, aimGroundMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : fallback;
        }
    }
}
