using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Cansancio (BreathOnTired + SalivaOnTired), mientras <see cref="Tired"/> esté a true (lo pone PlayerVFX):
    ///  - Aliento: jadeos con el humo cartoon (SG_VFX_ToonSmoke) en forma de cono saliendo de la boca, cada
    ///    uno un resoplido fuerte y otro más flojo justo detrás. Color propio.
    ///  - Saliva: gotas 3D (sombreado de dos tonos y contorno) que salen despedidas de la boca, se estiran al
    ///    caer y dejan una salpicadura en el suelo que se encoge.
    ///
    /// Montaje: hijo del personaje en la boca, Z hacia donde sale el aliento.
    /// </summary>
    public class TiredVFX : VfxBehaviour
    {
        [Header("Aliento")]
        [Tooltip("M_VFX_ToonSmoke.")]
        [SerializeField] Material smokeMaterial;
        [SerializeField] Color breathColor = new(0.95f, 0.97f, 1f, 1f);
        [Tooltip("Segundos entre jadeos (mín, máx).")]
        [SerializeField] Vector2 breathInterval = new(0.38f, 0.55f);
        [Tooltip("Tamaño del resoplido (metros).")]
        [SerializeField] Vector2 breathSize = new(0.24f, 0.32f);
        [SerializeField] float breathSpeed = 1.1f;
        [SerializeField] Vector2 breathLifetime = new(0.5f, 0.7f);
        [Tooltip("Grados hacia abajo desde donde mira la boca.")]
        [SerializeField] float breathDown = 22f;

        [Header("Saliva")]
        [SerializeField] bool saliva = true;
        [Tooltip("M_VFX_ToonFlat.")]
        [SerializeField] Material flatMaterial;
        [SerializeField] Color salivaColor = new(0.82f, 0.96f, 1f);
        [Tooltip("Color hacia la cola de la gota.")]
        [SerializeField] Color salivaTipColor = new(0.55f, 0.84f, 1f);
        [SerializeField] Color salivaShadow = new(0.7f, 0.84f, 1f);
        [SerializeField] Color salivaOutline = new(0.14f, 0.34f, 0.56f);
        [Tooltip("Segundos entre escupitajos (mín, máx).")]
        [SerializeField] Vector2 salivaInterval = new(0.6f, 1.3f);
        [SerializeField] Vector2Int dropsPerSpit = new(1, 2);
        [SerializeField] Vector2 dropSpeed = new(1.4f, 2.4f);
        [Tooltip("Tamaño de la gota (metros).")]
        [SerializeField] float dropSize = 0.06f;
        [SerializeField] float gravity = 9.8f;
        [Tooltip("Tamaño de la salpicadura (metros).")]
        [SerializeField] float splatSize = 0.16f;
        [SerializeField] float splatLifetime = 0.8f;
        [SerializeField] LayerMask groundMask = Physics.DefaultRaycastLayers;

        /// <summary>Encendido / apagado (lo pone PlayerVFX según la estamina).</summary>
        public bool Tired { get; set; }

        const int DropPool = 8;
        const int SplatPool = 6;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        ParticleSystem breath;
        MaterialPropertyBlock colorBlock;
        Transform root;
        float breathTimer;
        float followUpTimer = -1f;
        float salivaTimer;
        float testTimer;

        Transform salivaContainer;
        FlatPiece[] drops;
        Vector3[] dropVelocity;
        float[] dropAge;
        FlatPiece[] splats;
        float[] splatAge;
        int nextDrop, nextSplat;

        void Awake()
        {
            Rigidbody body = GetComponentInParent<Rigidbody>();
            root = body != null ? body.transform : transform.root;

            if (smokeMaterial != null)
            {
                breath = ToonSmoke.CreateSystem("Breath", transform, smokeMaterial, ToonSmoke.ConeMeshes, randomYaw: false, maxParticles: 48);
                ToonSmoke.ApplyColor(breath, ref colorBlock, breathColor);
            }
            else Debug.LogWarning("TiredVFX: falta smokeMaterial (M_VFX_ToonSmoke).", this);

            if (saliva && flatMaterial != null) CreateSaliva();
        }

        void OnValidate()
        {
            if (Application.isPlaying) ToonSmoke.ApplyColor(breath, ref colorBlock, breathColor);
        }

        void OnDisable()
        {
            if (drops != null) foreach (FlatPiece drop in drops) drop.Visible = false;
            if (splats != null) foreach (FlatPiece splat in splats) splat.Visible = false;
            if (dropAge != null) for (int i = 0; i < dropAge.Length; i++) dropAge[i] = -1f;
            if (splatAge != null) for (int i = 0; i < splatAge.Length; i++) splatAge[i] = -1f;
        }

        void OnDestroy()
        {
            if (salivaContainer != null) Destroy(salivaContainer.gameObject);
        }

        [VfxTest("Test cansado (4 s)")]
        void Test() => testTimer = 4f;

        // ---------------- Bucle ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (testTimer > 0f) testTimer -= dt;
            bool tired = Tired || testTimer > 0f;

            if (tired)
            {
                breathTimer -= dt;
                if (breathTimer <= 0f)
                {
                    breathTimer = Random.Range(breathInterval.x, breathInterval.y);
                    Exhale(1f);
                    followUpTimer = 0.09f;
                }
                if (followUpTimer >= 0f)
                {
                    followUpTimer -= dt;
                    if (followUpTimer < 0f) Exhale(0.6f);
                }

                if (drops != null)
                {
                    salivaTimer -= dt;
                    if (salivaTimer <= 0f)
                    {
                        salivaTimer = Random.Range(salivaInterval.x, salivaInterval.y);
                        int count = Random.Range(dropsPerSpit.x, dropsPerSpit.y + 1);
                        for (int i = 0; i < count; i++) Spit();
                    }
                }
            }
            else
            {
                // Al cansarse, el primer jadeo sale enseguida.
                breathTimer = Mathf.Min(breathTimer, 0.1f);
                salivaTimer = Mathf.Max(salivaTimer, 0.35f);
            }

            UpdateDrops(dt);
            UpdateSplats(dt);
        }

        // ---------------- Aliento ----------------

        void Exhale(float strength)
        {
            if (breath == null) return;
            Vector3 forward = Quaternion.AngleAxis(breathDown, transform.right) * transform.forward;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-10f, 10f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-6f, 6f), transform.right) * forward;
            float size = Random.Range(breathSize.x, breathSize.y) * strength;
            ParticleSystem.EmitParams emit = new()
            {
                applyShapeToPosition = false,
                position = transform.position + dir * (size * 0.45f),
                velocity = dir * (breathSpeed * Random.Range(0.8f, 1.2f) * Mathf.Lerp(0.6f, 1f, strength)) + Vector3.up * 0.15f,
                startSize = size,
                startLifetime = Random.Range(breathLifetime.x, breathLifetime.y),
                rotation3D = Quaternion.LookRotation(dir, Vector3.up).eulerAngles,
            };
            breath.Emit(emit, 1);
        }

        // ---------------- Saliva ----------------

        void CreateSaliva()
        {
            // Fuera del personaje: las gotas vuelan por el mundo y las salpicaduras se quedan en el suelo.
            salivaContainer = new GameObject($"Saliva_{root.name}").transform;
            Mesh dropMesh = FlatMeshes.Drop();
            drops = new FlatPiece[DropPool];
            dropVelocity = new Vector3[DropPool];
            dropAge = new float[DropPool];
            for (int i = 0; i < DropPool; i++)
            {
                drops[i] = new FlatPiece($"Drop{i}", salivaContainer, dropMesh, flatMaterial)
                    .Colors(salivaColor, salivaTipColor, salivaOutline).Cel(1f, salivaShadow);
                dropAge[i] = -1f;
            }

            splats = new FlatPiece[SplatPool];
            splatAge = new float[SplatPool];
            for (int i = 0; i < SplatPool; i++)
            {
                splats[i] = new FlatPiece($"Splat{i}", salivaContainer, FlatMeshes.Splat(70 + i), flatMaterial)
                    .Colors(salivaColor, salivaTipColor, salivaOutline);
                splatAge[i] = -1f;
            }
        }

        void Spit()
        {
            int i = nextDrop;
            nextDrop = (nextDrop + 1) % drops.Length;
            Vector3 side = transform.right * Random.Range(-0.6f, 0.6f);
            dropVelocity[i] = (transform.forward * 0.7f + Vector3.up * 0.75f + side).normalized * Random.Range(dropSpeed.x, dropSpeed.y);
            dropAge[i] = 0f;
            drops[i].Transform.position = transform.position + transform.right * Random.Range(-0.05f, 0.05f);
            drops[i].Visible = true;
        }

        void UpdateDrops(float dt)
        {
            if (drops == null) return;
            for (int i = 0; i < drops.Length; i++)
            {
                if (dropAge[i] < 0f) continue;
                dropAge[i] += dt;
                Transform drop = drops[i].Transform;
                Vector3 from = drop.position;
                dropVelocity[i] += Vector3.down * (gravity * dt);
                Vector3 to = from + dropVelocity[i] * dt;

                if (dropAge[i] > 2.5f)
                {
                    StopDrop(i);
                    continue;
                }
                if (dropAge[i] > 0.05f && HitGround(from, to, out RaycastHit hit))
                {
                    StopDrop(i);
                    Splat(hit.point, hit.normal);
                    continue;
                }

                drop.position = to;
                float speed = dropVelocity[i].magnitude;
                if (speed > 0.01f) drop.rotation = Quaternion.LookRotation(dropVelocity[i]);
                float pop = FlatFx.OutBack(FlatFx.Phase(dropAge[i], 0f, 0.08f));
                // Se estira al caer rápido (cartoon).
                drop.localScale = new Vector3(1f, 1f, 1f + speed * 0.12f) * (dropSize * pop);
            }
        }

        void StopDrop(int i)
        {
            dropAge[i] = -1f;
            drops[i].Visible = false;
        }

        void Splat(Vector3 point, Vector3 normal)
        {
            int i = nextSplat;
            nextSplat = (nextSplat + 1) % splats.Length;
            Transform splat = splats[i].Transform;
            splat.SetPositionAndRotation(point + normal * 0.012f,
                Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            splatAge[i] = 0f;
            splats[i].Visible = true;
        }

        void UpdateSplats(float dt)
        {
            if (splats == null) return;
            for (int i = 0; i < splats.Length; i++)
            {
                if (splatAge[i] < 0f) continue;
                splatAge[i] += dt;
                float life = Mathf.Max(splatLifetime, 0.2f);
                if (splatAge[i] >= life)
                {
                    splatAge[i] = -1f;
                    splats[i].Visible = false;
                    continue;
                }
                float appear = FlatFx.OutBack(FlatFx.Phase(splatAge[i], 0f, 0.1f), 2.2f);
                float leave = 1f - FlatFx.InCubic(FlatFx.Phase(splatAge[i], life - 0.3f, life));
                splats[i].Transform.localScale = Vector3.one * (splatSize * appear * leave);
            }
        }

        bool HitGround(Vector3 from, Vector3 to, out RaycastHit result)
        {
            result = default;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-5f) return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(root)) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    result = hits[i];
                }
            }
            return best < float.MaxValue;
        }
    }
}
