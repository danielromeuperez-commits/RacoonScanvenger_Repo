using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Cáscara de plátano. Al usarla, el servidor crea una banana física
    /// y la lanza hacia delante del jugador.
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

        public override void ServerUse(PlayerController user, ItemData item)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (user == null || bananaPrefab == null) return;

            // Dirección horizontal hacia la que mira el jugador.
            Vector3 forward = Vector3.ProjectOnPlane(
                user.transform.forward,
                Vector3.up
            ).normalized;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            // La creamos un poco delante y por encima del jugador
            // para evitar que choque inmediatamente con él.
            Vector3 spawnPosition =
                user.transform.position +
                forward * spawnForwardDistance +
                Vector3.up * spawnHeight;

            NetworkObject banana = UnityEngine.Object.Instantiate(
                bananaPrefab,
                spawnPosition,
                Quaternion.LookRotation(forward)
            );

            banana.Spawn();

            // El servidor controla la física de la banana.
            if (banana.TryGetComponent(out Rigidbody body))
            {
                body.linearVelocity =
                    forward * throwSpeed +
                    Vector3.up * upwardSpeed;

                // Voltea hacia delante por el aire (al aterrizar BananaTrap la deja derecha).
                if (spinTurnsPerSecond > 0f)
                {
                    float radiansPerSecond = spinTurnsPerSecond * 2f * Mathf.PI;
                    body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, radiansPerSecond);
                    body.angularVelocity = Vector3.Cross(Vector3.up, forward) * radiansPerSecond;
                }
            }
        }
    }
}
