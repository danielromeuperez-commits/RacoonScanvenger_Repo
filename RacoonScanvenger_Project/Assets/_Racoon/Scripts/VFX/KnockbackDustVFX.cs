using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Polvo del knockback (DustOnKnockback) con el humo cartoon (SG_VFX_ToonSmoke), con su propio color:
    ///  - Al recibir el golpe: puffs saliendo de los pies hacia donde sale despedido; en el golpe fuerte,
    ///    además, una nube de pelea alrededor del cuerpo.
    ///  - Mientras derrapa por el suelo: un rastro de puffs.
    ///  - Al caer al suelo tras el golpe fuerte (sale volando): un anillo de puffs.
    /// Solo usa el movimiento del transform, así que funciona igual en todos los clientes.
    ///
    /// Montaje: en los pies del personaje (p. ej. junto a FootstepVFX). Lo lanza PlayerVFX con <see cref="Play"/>.
    /// </summary>
    public class KnockbackDustVFX : VfxBehaviour
    {
        [Header("Humo")]
        [Tooltip("M_VFX_ToonSmoke.")]
        [SerializeField] Material smokeMaterial;
        [Tooltip("Color del polvo (tono iluminado; las sombras salen solas).")]
        [SerializeField] Color dustColor = new(0.83f, 0.85f, 0.9f, 1f);

        [Header("Suelo")]
        [SerializeField] LayerMask groundMask = Physics.DefaultRaycastLayers;
        [Tooltip("Desde qué altura sobre los pies se busca el suelo.")]
        [SerializeField] float rayStartHeight = 0.4f;
        [Tooltip("Separación del suelo que aún cuenta como apoyado (metros).")]
        [SerializeField] float groundTolerance = 0.2f;

        [Header("Al recibir el golpe")]
        [Tooltip("Puffs del golpe ligero / fuerte.")]
        [SerializeField] Vector2Int burstCount = new(4, 10);
        [Tooltip("Tamaño de esos puffs: ligero / fuerte (metros).")]
        [SerializeField] Vector2 burstSize = new(0.26f, 0.42f);
        [Tooltip("Puffs de la nube de pelea alrededor del cuerpo (golpe fuerte).")]
        [SerializeField] int cloudCount = 5;
        [SerializeField] float cloudSize = 0.55f;
        [Tooltip("Altura del centro del cuerpo sobre los pies.")]
        [SerializeField] float bodyHeight = 0.5f;

        [Header("Derrape")]
        [SerializeField] float slideInterval = 0.045f;
        [Tooltip("Velocidad mínima (m/s) para dejar rastro.")]
        [SerializeField] float minSlideSpeed = 1.2f;
        [SerializeField] float slideSize = 0.3f;

        [Header("Aterrizaje")]
        [SerializeField] int landingCount = 8;
        [SerializeField] float landingSize = 0.36f;
        [Tooltip("Segundos en el aire para que cuente como aterrizaje.")]
        [SerializeField] float minAirTime = 0.1f;

        static readonly RaycastHit[] hits = new RaycastHit[8];

        ParticleSystem dust;
        MaterialPropertyBlock colorBlock;
        Transform root;
        Vector3 lastPosition;
        Vector3 moveDirection;
        float speed;
        float trackTimer;
        float slideTimer;
        float emitTimer;
        float airTime;
        bool heavy;

        void Awake()
        {
            Rigidbody body = GetComponentInParent<Rigidbody>();
            root = body != null ? body.transform : transform.root;
            lastPosition = transform.position;
            if (smokeMaterial == null)
            {
                Debug.LogWarning("KnockbackDustVFX: falta smokeMaterial (M_VFX_ToonSmoke).", this);
                return;
            }
            dust = ToonSmoke.CreateSystem("KnockbackDust", transform, smokeMaterial, ToonSmoke.CloudMeshes);
            ToonSmoke.ApplyColor(dust, ref colorBlock, dustColor);
        }

        void OnValidate()
        {
            if (Application.isPlaying) ToonSmoke.ApplyColor(dust, ref colorBlock, dustColor);
        }

        /// <summary>Color del polvo. Cambia también el de los puffs que ya están en el aire.</summary>
        public Color DustColor
        {
            get => dustColor;
            set
            {
                dustColor = value;
                ToonSmoke.ApplyColor(dust, ref colorBlock, dustColor);
            }
        }

        // ---------------- API ----------------

        /// <summary>
        /// Golpe recibido. <paramref name="direction"/>: hacia donde sale despedido (horizontal).
        /// <paramref name="duration"/>: cuánto dura el aturdimiento (lo que puede durar el derrape).
        /// </summary>
        public void Play(Vector3 direction, bool isHeavy, float duration)
        {
            if (dust == null) return;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : -transform.forward;
            heavy = isHeavy;
            slideTimer = Mathf.Max(duration, 0.2f);
            trackTimer = slideTimer + 0.8f; // Para pillar el aterrizaje aunque llegue después.
            emitTimer = 0f;
            airTime = 0f;

            Vector3 feet = FindGround(out Vector3 point) ? point : transform.position;
            int count = isHeavy ? burstCount.y : burstCount.x;
            float size = isHeavy ? burstSize.y : burstSize.x;
            for (int i = 0; i < count; i++)
            {
                // Sobre todo hacia donde sale despedido, abiertos en abanico.
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-75f, 75f), 0f) * direction;
                if (Random.value < 0.3f) dir = -dir;
                Vector3 velocity = dir * Random.Range(1.4f, isHeavy ? 3.6f : 2.2f) + Vector3.up * Random.Range(0.4f, 1.2f);
                Emit(feet + dir * 0.15f + Vector3.up * (size * 0.4f), velocity, size * Random.Range(0.75f, 1.15f), Random.Range(0.55f, 0.85f));
            }

            if (!isHeavy) return;
            Vector3 body = feet + Vector3.up * bodyHeight;
            for (int i = 0; i < cloudCount; i++)
            {
                Vector3 offset = Random.onUnitSphere;
                offset.y *= 0.6f;
                Emit(body + offset * 0.28f, offset * Random.Range(0.5f, 1.1f), cloudSize * Random.Range(0.8f, 1.15f), Random.Range(0.45f, 0.65f));
            }
        }

        [VfxTest("Test golpe ligero")]
        void TestLight() => Play(-transform.forward, false, 0.45f);

        [VfxTest("Test golpe fuerte")]
        void TestHeavy() => Play(-transform.forward, true, 1f);

        // ---------------- Bucle ----------------

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float step = delta.magnitude;
            if (step > 3f) step = 0f; // Teleport.
            speed = Mathf.Lerp(speed, step / dt, 1f - Mathf.Exp(-20f * dt));
            if (step > 0.0001f) moveDirection = delta / step;

            if (trackTimer <= 0f || dust == null) return;
            trackTimer -= dt;
            slideTimer -= dt;

            bool grounded = FindGround(out Vector3 point);
            if (!grounded)
            {
                airTime += dt;
                return;
            }

            // Cae al suelo tras salir volando: anillo de puffs.
            if (airTime > minAirTime)
            {
                for (int i = 0; i < landingCount; i++)
                {
                    float angle = (i + Random.Range(-0.3f, 0.3f)) / landingCount * Mathf.PI * 2f;
                    Vector3 dir = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Emit(point + dir * 0.2f + Vector3.up * (landingSize * 0.35f), dir * Random.Range(1.8f, 2.8f) + Vector3.up * Random.Range(0.2f, 0.6f),
                        landingSize * Random.Range(0.8f, 1.15f), Random.Range(0.5f, 0.75f));
                }
            }
            airTime = 0f;

            // Derrape: rastro de puffs mientras se desliza.
            if (slideTimer <= 0f || speed < minSlideSpeed) return;
            emitTimer -= dt;
            if (emitTimer > 0f) return;
            emitTimer = slideInterval;
            Vector3 side = Vector3.Cross(Vector3.up, moveDirection);
            float sizeFactor = Mathf.Clamp(speed / 6f, 0.6f, 1.4f) * (heavy ? 1.15f : 1f);
            Emit(point + side * Random.Range(-0.12f, 0.12f) + Vector3.up * (slideSize * 0.35f),
                -moveDirection * Random.Range(0.2f, 0.6f) + side * Random.Range(-0.5f, 0.5f) + Vector3.up * Random.Range(0.4f, 0.9f),
                slideSize * sizeFactor * Random.Range(0.8f, 1.1f), Random.Range(0.45f, 0.7f));
        }

        void Emit(Vector3 position, Vector3 velocity, float size, float lifetime)
        {
            ParticleSystem.EmitParams emit = new()
            {
                applyShapeToPosition = false,
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
            };
            dust.Emit(emit, 1);
        }

        // Rayo hacia abajo desde los pies ignorando los colliders del propio personaje.
        bool FindGround(out Vector3 point)
        {
            point = transform.position;
            Vector3 origin = transform.position + Vector3.up * rayStartHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, rayStartHeight + groundTolerance, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(root)) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    point = hits[i].point;
                }
            }
            return best < float.MaxValue;
        }
    }
}
