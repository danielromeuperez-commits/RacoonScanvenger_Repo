using System.Collections;
using Racoon.Feedback;
using Racoon.Player;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Cáscara de plátano en el suelo. El SERVIDOR decide cuándo aterriza y a quién tumba
    /// (también a quien la ha lanzado: es a propósito). Los clientes solo ven la posición (NetworkTransform)
    /// y reciben RPCs para los efectos: aplastón al aterrizar y cáscara que sale volando al resbalar.
    ///
    /// Montaje: la malla va en un hijo (<see cref="visual"/>) para poder deformarla sin que el
    /// NetworkTransform (que sincroniza la escala del root) la pise.
    /// </summary>
    public class BananaTrap : NetworkBehaviour
    {
        [Header("Banana")]
        [SerializeField] float stunDuration = 1.5f;
        [SerializeField] float armDelay = 0.4f;
        [SerializeField] float lifeTime = 20f;

        [Header("Suelo")]
        [Tooltip("Normal mínima para considerar que la banana ha tocado el suelo.")]
        [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.5f;

        [Header("Visual")]
        [Tooltip("Hijo con la malla. Es lo que se aplasta al aterrizar y lo que se copia al salir volando.")]
        [SerializeField] Transform visual;

        [Tooltip("Duración del aplastón + botecito al aterrizar.")]
        [SerializeField, Min(0.05f)] float landSquashTime = 0.45f;

        [Tooltip("Altura del botecito al aterrizar.")]
        [SerializeField, Min(0f)] float landHopHeight = 0.08f;

        [Tooltip("Velocidad horizontal con la que sale despedida la cáscara al resbalar alguien.")]
        [SerializeField, Min(0f)] float shellKickSpeed = 4f;

        [Tooltip("Velocidad hacia arriba de la cáscara al salir despedida.")]
        [SerializeField, Min(0f)] float shellKickUpSpeed = 5f;

        [Tooltip("Texto que salta sobre quien resbala (vacío = sin texto).")]
        [SerializeField] string slipText = "¡PLAF!";

        [SerializeField] Color slipTextColor = new(1f, 0.85f, 0.2f);

        [Tooltip("Opcional: fuente del texto. Vacío = la de TMP por defecto.")]
        [SerializeField] TMP_FontAsset slipTextFont;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al CAER la cáscara al suelo: «plaf» blandito y húmedo. Sale de la cáscara.")]
        [SerializeField] SfxCue landSfx = new();

        [Tooltip("SFX al RESBALAR alguien: chirrido de patinazo «fiiiu» + golpe cómico. Sale de quien resbala.")]
        [SerializeField] SfxCue slipSfx = new();

        // Segundos que espera el servidor antes de despawnear tras el resbalón,
        // para que el RPC de efectos llegue antes que la desaparición.
        const float DespawnDelayAfterSlip = 0.25f;

        Rigidbody body;
        MeshRenderer visualRenderer;
        Vector3 visualRestPosition;
        Vector3 visualRestScale;
        Coroutine squashRoutine;

        bool armed;
        bool consumed;
        bool landed;

        void Awake()
        {
            body = GetComponent<Rigidbody>();

            if (visual == null)
                visual = transform;

            visualRenderer = visual.GetComponentInChildren<MeshRenderer>();
            visualRestPosition = visual.localPosition;
            visualRestScale = visual.localScale;
        }

        public override void OnNetworkSpawn()
        {
            // En los clientes la mueve NetworkTransform: su física no debe pelearse con ella.
            if (!IsServer)
            {
                if (body != null) body.isKinematic = true;
                return;
            }

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

            // Llega girando por el aire: se queda tumbada derecha, conservando solo hacia dónde mira.
            Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            body.rotation = upright;
            transform.rotation = upright;

            LandedRpc();
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void LandedRpc()
        {
            landSfx.Play(transform.position);

            if (visual == transform)
                return;

            if (squashRoutine != null)
                StopCoroutine(squashRoutine);

            squashRoutine = StartCoroutine(LandSquash());
        }

        // Aplastón al tocar el suelo, estirón con un botecito y se asienta (oscilación amortiguada).
        IEnumerator LandSquash()
        {
            for (float time = 0f; time < landSquashTime; time += Time.deltaTime)
            {
                float t = time / landSquashTime;
                float squash = Mathf.Exp(-6f * t) * Mathf.Cos(t * 16f);

                visual.localScale = Vector3.Scale(
                    visualRestScale,
                    new Vector3(1f + 0.3f * squash, 1f - 0.45f * squash, 1f + 0.3f * squash)
                );

                float hop = t > 0.12f && t < 0.62f
                    ? Mathf.Sin((t - 0.12f) / 0.5f * Mathf.PI) * landHopHeight
                    : 0f;
                visual.localPosition = visualRestPosition + Vector3.up * hop;

                yield return null;
            }

            visual.localScale = visualRestScale;
            visual.localPosition = visualRestPosition;
            squashRoutine = null;
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

            // Resbalón: patina con su impulso y gira (PlayerController.ServerApplyItemStun).
            player.ServerApplyItemStun(stunDuration, true);

            // La cáscara sale despedida alejándose de quien la ha pisado.
            Vector3 kick = transform.position - player.transform.position;
            kick.y = 0f;
            if (kick.sqrMagnitude < 0.0001f) kick = Random.insideUnitSphere;
            kick.y = 0f;
            kick = kick.normalized;

            SlipRpc(kick, player.transform.position);

            Invoke(nameof(DespawnNow), DespawnDelayAfterSlip);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SlipRpc(Vector3 kickDirection, Vector3 victimPosition)
        {
            FlyAwayDebris.Spawn(
                visualRenderer,
                kickDirection * shellKickSpeed + Vector3.up * shellKickUpSpeed
            );

            // La de verdad se esconde ya; el servidor la despawnea un poco después.
            if (visualRenderer != null)
                visualRenderer.enabled = false;

            if (!string.IsNullOrEmpty(slipText))
                FloatingText.Spawn(slipText, victimPosition + Vector3.up * 2f, slipTextColor, 0.7f, slipTextFont);

            slipSfx.Play(victimPosition);
        }

        void DespawnNow()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        void RemoveBanana()
        {
            if (!IsServer || consumed)
                return;

            consumed = true;

            DespawnNow();
        }
    }
}
