using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    public class BananaTrap : NetworkBehaviour
    {
        [Header("Banana")]
        [SerializeField] float stunDuration = 1.5f;
        [SerializeField] float armDelay = 0.4f;
        [SerializeField] float lifeTime = 20f;

        [Header("Suelo")]
        [Tooltip("Normal mínima para considerar que la banana ha tocado el suelo.")]
        [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.5f;

        Rigidbody body;

        bool armed;
        bool consumed;
        bool landed;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            Invoke(nameof(Arm), armDelay);
            Invoke(nameof(RemoveBanana), lifeTime);
        }

        void Arm()
        {
            if (!IsServer) return;

            armed = true;
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || landed || body == null)
                return;

            // Solo se queda quieta cuando toca una superficie
            // suficientemente horizontal, no al chocar contra una pared.
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y < groundNormalThreshold)
                    continue;

                Land();
                return;
            }
        }

        void Land()
        {
            landed = true;

            // Una vez en el suelo deja de tener física:
            // funciona como una trampa estática.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || !armed || !landed || consumed)
            return;

            PlayerController player =
                other.GetComponentInParent<PlayerController>();

            if (player == null)
                return;

            consumed = true;

            // Evitamos que el Invoke del tiempo de vida
            // intente eliminarla otra vez.
            CancelInvoke(nameof(RemoveBanana));

            player.ServerApplyItemStun(stunDuration);

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        void RemoveBanana()
        {
            if (!IsServer || consumed)
                return;

            consumed = true;

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }
}