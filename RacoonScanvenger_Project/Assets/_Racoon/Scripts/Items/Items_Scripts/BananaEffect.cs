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

        public override void ServerUse(PlayerController user, ItemData item) => ServerUse(user, item, false);

        public override void ServerUse(PlayerController user, ItemData item, bool dropBehind)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (user == null || bananaPrefab == null) return;

            TryGetThrow(user, dropBehind, out Vector3 spawnPosition, out Vector3 velocity);

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
            // Dirección horizontal hacia la que mira el jugador.
            Vector3 forward = Vector3.ProjectOnPlane(
                user.transform.forward,
                Vector3.up
            ).normalized;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

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
    }
}
