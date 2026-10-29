using System;
using System.Collections;
using Racoon.Feedback;
using Racoon.Player;
using Racoon.VFX;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Racoon.Items
{
    /// <summary>
    /// Cáscara de plátano en el suelo. El SERVIDOR decide cuándo aterriza, cuándo está armada, a quién tumba
    /// (también a quien la ha lanzado: es a propósito) y si un puñetazo la manda volando. Los clientes solo ven
    /// la posición (NetworkTransform) y reciben RPCs para los efectos:
    ///  - En el aire: gira y deja un rastro amarillo corto.
    ///  - Al aterrizar: aplastón con botecito. Parpadea hasta que está armada; entonces hace «clic» (pop)
    ///    y aparece un anillo que late a su alrededor para que se vea bien.
    ///  - Últimos segundos de vida: parpadea antes de desaparecer.
    ///  - Al resbalar: la cáscara sale volando, «¡PLAF!» (o «¡AUTOGOL!» si era tuya) y se cae algo de la bolsa.
    ///
    /// Montaje: la malla va en un hijo (<see cref="visual"/>) para poder deformarla sin que el
    /// NetworkTransform (que sincroniza la escala del root) la pise. Rastro y anillo usan M_VFX_ToonFlat.
    /// </summary>
    public class BananaTrap : NetworkBehaviour
    {
        /// <summary>
        /// Alguien ha resbalado (todos los clientes). Parámetros: quien resbala y quien la lanzó o la golpeó
        /// por última vez (null si no se sabe). Para kill-feed y estadísticas.
        /// </summary>
        public static event Action<PlayerController, PlayerController> Slipped;

        [Header("Banana")]
        [SerializeField] float stunDuration = 1.5f;
        [SerializeField] float armDelay = 0.4f;
        [SerializeField] float lifeTime = 20f;

        [Header("Suelo")]
        [Tooltip("Normal mínima para considerar que la banana ha tocado el suelo.")]
        [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.5f;

        [Header("Resbalón")]
        [Tooltip("Parte del loot sin vender que se cae de la bolsa al resbalar (0 = nada).")]
        [SerializeField, Range(0f, 1f)] float slipLootDropFraction = 0.15f;

        [Header("Puñetazo")]
        [Tooltip("Velocidad horizontal con la que sale volando al darle un puñetazo.")]
        [SerializeField, Min(0f)] float kickSpeed = 7f;
        [Tooltip("Velocidad hacia arriba al darle un puñetazo.")]
        [SerializeField, Min(0f)] float kickUpSpeed = 3.5f;

        [Header("Visual")]
        [Tooltip("Hijo con la malla. Es lo que se aplasta al aterrizar y lo que se copia al salir volando.")]
        [SerializeField] Transform visual;

        [Tooltip("M_VFX_ToonFlat. Rastro en el aire y anillo cuando está armada (sin él no salen).")]
        [SerializeField] Material flatMaterial;

        [Tooltip("Duración del aplastón + botecito al aterrizar.")]
        [SerializeField, Min(0.05f)] float landSquashTime = 0.45f;

        [Tooltip("Altura del botecito al aterrizar.")]
        [SerializeField, Min(0f)] float landHopHeight = 0.08f;

        [Tooltip("Segundos del rastro en el aire.")]
        [SerializeField, Min(0f)] float trailTime = 0.25f;

        [Tooltip("Grosor del rastro (metros).")]
        [SerializeField, Min(0f)] float trailWidth = 0.08f;

        [SerializeField] Color trailColor = new(1f, 0.88f, 0.25f);

        [Tooltip("Radio del anillo que late cuando está armada (metros).")]
        [SerializeField, Min(0.05f)] float armedRingRadius = 0.22f;

        [SerializeField] Color armedRingColor = new(1f, 0.85f, 0.2f);

        [Tooltip("Últimos segundos de vida en los que parpadea antes de desaparecer.")]
        [SerializeField, Min(0f)] float expireBlinkTime = 3f;

        [Tooltip("Velocidad horizontal con la que sale despedida la cáscara al resbalar alguien.")]
        [SerializeField, Min(0f)] float shellKickSpeed = 4f;

        [Tooltip("Velocidad hacia arriba de la cáscara al salir despedida.")]
        [SerializeField, Min(0f)] float shellKickUpSpeed = 5f;

        [Tooltip("Texto que salta sobre quien resbala (vacío = sin texto).")]
        [SerializeField] string slipText = "¡PLAF!";

        [Tooltip("Texto si resbala con su propio plátano (vacío = usa Slip Text).")]
        [SerializeField] string selfSlipText = "¡AUTOGOL!";

        [SerializeField] Color slipTextColor = new(1f, 0.85f, 0.2f);

        [Tooltip("Opcional: fuente del texto. Vacío = la de TMP por defecto.")]
        [SerializeField] TMP_FontAsset slipTextFont;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al CAER la cáscara al suelo: «plaf» blandito y húmedo. Sale de la cáscara.")]
        [SerializeField] SfxCue landSfx = new();

        [Tooltip("SFX cuando queda ARMADA (ya tumba): «clic» o «ping» corto y sutil, que avise sin molestar. Sale de la cáscara.")]
        [SerializeField] SfxCue armSfx = new();

        [Tooltip("SFX al RESBALAR alguien: chirrido de patinazo «fiiiu» + golpe cómico. Sale de quien resbala.")]
        [SerializeField] SfxCue slipSfx = new();

        [Tooltip("SFX al darle un PUÑETAZO: «tonk» blando + «fiuu» de salir volando. Sale de la cáscara.")]
        [SerializeField] SfxCue kickSfx = new();

        // Segundos que espera el servidor antes de despawnear tras el resbalón,
        // para que el RPC de efectos llegue antes que la desaparición.
        const float DespawnDelayAfterSlip = 0.25f;
        const float ArmPopTime = 0.2f, BlinkRate = 14f;

        Rigidbody body;
        MeshRenderer visualRenderer;
        Vector3 visualRestPosition;
        Vector3 visualRestScale;
        Coroutine squashRoutine;

        // Servidor
        bool armed;
        bool consumed;
        bool landed;
        bool armedNotified;
        PlayerController thrower;

        // Clientes (visual)
        float spawnTime;
        bool landedVisual;
        bool armedVisual;
        bool hiddenVisual;
        float armedTime = -99f;
        TrailRenderer trail;
        MaterialPropertyBlock trailBlock;
        FlatPiece armedRing;

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
            spawnTime = Time.time;
            BuildFx();

            // En los clientes la mueve NetworkTransform: su física no debe pelearse con ella.
            if (!IsServer)
            {
                if (body != null) body.isKinematic = true;
                return;
            }

            Invoke(nameof(Arm), armDelay);
            Invoke(nameof(RemoveBanana), lifeTime);
        }

        /// <summary>Solo servidor. Quién la ha lanzado (o golpeado por última vez).</summary>
        public void ServerSetThrower(PlayerController player)
        {
            if (IsServer) thrower = player;
        }

        void Arm()
        {
            if (!IsServer) return;

            armed = true;
            TryNotifyArmed();
        }

        // Peligrosa = armada Y en el suelo. Se avisa una vez (otra vez tras un puñetazo).
        void TryNotifyArmed()
        {
            if (!armed || !landed || consumed || armedNotified)
                return;

            armedNotified = true;
            ArmedRpc();
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
            TryNotifyArmed();
        }

        /// <summary>
        /// Solo servidor. Un puñetazo la manda volando hacia <paramref name="direction"/>: vuelve a ser un
        /// proyectil y, al caer, otra trampa (ahora "de" quien la ha golpeado).
        /// </summary>
        public void ServerKick(Vector3 direction, PlayerController kicker)
        {
            if (!IsServer || consumed || body == null)
                return;

            if (kicker != null)
                thrower = kicker;

            landed = false;
            armedNotified = false;
            body.isKinematic = false;
            body.linearVelocity = direction * kickSpeed + Vector3.up * kickUpSpeed;
            body.maxAngularVelocity = Mathf.Max(body.maxAngularVelocity, 20f);
            body.angularVelocity = Vector3.Cross(Vector3.up, direction) * 18f;

            KickedRpc();
        }

        // ---------------- Efectos (todos los clientes) ----------------

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void LandedRpc()
        {
            landedVisual = true;
            if (trail != null) trail.emitting = false;

            landSfx.Play(transform.position);

            if (visual == transform)
                return;

            if (squashRoutine != null)
                StopCoroutine(squashRoutine);

            squashRoutine = StartCoroutine(LandSquash());
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void ArmedRpc()
        {
            armedVisual = true;
            armedTime = Time.time;
            armSfx.Play(transform.position);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void KickedRpc()
        {
            landedVisual = false;
            armedVisual = false;

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            kickSfx.Play(transform.position);
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

        void Update()
        {
            if (!IsSpawned || hiddenVisual)
                return;

            float now = Time.time;

            // Parpadeo: en el suelo hasta que se arma, y en los últimos segundos de vida.
            bool expiring = now - spawnTime > lifeTime - expireBlinkTime;
            bool blink = (landedVisual && !armedVisual) || expiring;
            bool on = !blink || Mathf.Repeat(now * BlinkRate, 1f) < 0.6f;

            if (visualRenderer != null)
                visualRenderer.enabled = on;

            // «Clic» al armarse: pequeño pop (si no está con el aplastón del aterrizaje).
            float pop = now - armedTime;
            if (squashRoutine == null && visual != transform && pop < ArmPopTime)
                visual.localScale = visualRestScale * (1f + 0.25f * Mathf.Sin(pop / ArmPopTime * Mathf.PI));

            if (armedRing != null)
            {
                armedRing.Visible = armedVisual && on;

                if (armedVisual)
                {
                    float appear = FlatFx.OutBack(pop / 0.25f);
                    float beat = 1f + 0.12f * Mathf.Sin(now * 6f);
                    armedRing.Transform.localScale = Vector3.one * armedRingRadius * appear * beat;
                }
            }
        }

        void BuildFx()
        {
            if (flatMaterial == null)
                return;

            // Rastro: hijo del root (sigue a la cáscara pero no se aplasta con ella).
            var trailObject = new GameObject("Trail");
            trailObject.transform.SetParent(transform, false);
            trailObject.transform.localPosition = Vector3.up * 0.06f;
            trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = flatMaterial;
            trail.time = trailTime;
            trail.minVertexDistance = 0.04f;
            trail.widthMultiplier = trailWidth;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            trail.numCapVertices = 2;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = !landed;

            trailBlock = new MaterialPropertyBlock();
            trailBlock.SetColor(FlatFx.BaseColorId, trailColor);
            trailBlock.SetColor(FlatFx.TipColorId, Color.Lerp(trailColor, Color.white, 0.4f));
            trailBlock.SetColor(FlatFx.OutlineColorId, trailColor);
            trailBlock.SetFloat(FlatFx.GrowId, FlatFx.Full);
            trailBlock.SetFloat(FlatFx.ShrinkId, 0f);
            trail.SetPropertyBlock(trailBlock);

            // Anillo en el suelo alrededor de la cáscara cuando ya tumba.
            armedRing = new FlatPiece("ArmedRing", transform, FlatMeshes.Arc(1f, 0f, 360f, 0.1f, 0f, 32), flatMaterial)
                .Colors(armedRingColor, Color.Lerp(armedRingColor, Color.white, 0.5f), armedRingColor);
            armedRing.Transform.localPosition = Vector3.up * 0.01f;
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

            // Se le cae algo de la bolsa, hacia donde iba.
            if (slipLootDropFraction > 0f && player.TryGetComponent(out PlayerLoot loot))
                loot.ServerDropLoot(kick, slipLootDropFraction);

            SlipRpc(kick, player.transform.position, player.NetworkObjectId,
                thrower != null ? thrower.NetworkObjectId : ulong.MaxValue);

            Invoke(nameof(DespawnNow), DespawnDelayAfterSlip);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void SlipRpc(Vector3 kickDirection, Vector3 victimPosition, ulong victimId, ulong throwerId)
        {
            FlyAwayDebris.Spawn(
                visualRenderer,
                kickDirection * shellKickSpeed + Vector3.up * shellKickUpSpeed
            );

            // La de verdad se esconde ya; el servidor la despawnea un poco después.
            hiddenVisual = true;
            if (visualRenderer != null) visualRenderer.enabled = false;
            if (armedRing != null) armedRing.Visible = false;
            if (trail != null) trail.emitting = false;

            PlayerController victim = Find(victimId);
            PlayerController owner = Find(throwerId);
            bool own = victim != null && victim == owner;

            string text = own && !string.IsNullOrEmpty(selfSlipText) ? selfSlipText : slipText;
            if (!string.IsNullOrEmpty(text))
                FloatingText.Spawn(text, victimPosition + Vector3.up * 2f, slipTextColor, 0.7f, slipTextFont);

            slipSfx.Play(victimPosition);
            PlayerRumble.Rumble(victim, 0.6f, 0.4f, 0.25f);

            if (victim != null)
                Slipped?.Invoke(victim, owner);
        }

        PlayerController Find(ulong objectId) =>
            objectId != ulong.MaxValue &&
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out NetworkObject found)
                ? found.GetComponent<PlayerController>()
                : null;

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
