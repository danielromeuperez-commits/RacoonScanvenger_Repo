using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Cáscara de plátano. Al usarla, el servidor crea una banana física
    /// y la lanza hacia delante del jugador.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Items/Banana", fileName = "ITEM_Banana")]
    public class BananaItemData : ItemData
    {
        [Header("Banana")]
        [SerializeField] NetworkObject bananaPrefab;

        [Tooltip("Distancia delante del jugador donde aparece la banana.")]
        [SerializeField] float spawnForwardDistance = 0.8f;

        [Tooltip("Altura desde la que sale la banana.")]
        [SerializeField] float spawnHeight = 0.8f;

        [Tooltip("Velocidad hacia delante.")]
        [SerializeField] float throwSpeed = 6f;

        [Tooltip("Impulso vertical al lanzar la banana.")]
        [SerializeField] float upwardSpeed = 2f;

        public override void ServerUse(PlayerController user)
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

            NetworkObject banana = Instantiate(
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
            }
        }
    }
}