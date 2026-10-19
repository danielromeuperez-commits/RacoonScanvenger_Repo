using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Cansancio (BreathOnTired + SweatOnTired), mientras <see cref="Tired"/> esté a true (lo pone PlayerVFX):
    ///  - Aliento: jadeos con el humo cartoon (SG_VFX_ToonSmoke) en forma de cono que salen de la superficie del
    ///    morro (se busca en la malla), cada uno un resoplido fuerte y otro más flojo justo detrás. Color propio.
    ///  - Sudor: gotas con volumen (SG_VFX_ToonVolume: luz, sombra, brillo y contorno) que saltan de la cabeza
    ///    (puntos de la malla) a los lados, se estiran al caer y dejan una salpicadura en el suelo; y de vez en cuando
    ///    un goterón de anime que aparece en la sien, resbala un poco y cae.
    /// Todo va con el personaje: el aliento se desplaza con él y las gotas salen con su velocidad, así no se
    /// quedan dentro de la malla al moverse.
    ///
    /// Montaje: hijo del personaje a la altura de la boca, Z hacia donde sale el aliento.
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
        [Tooltip("Cuánto se desplaza el aliento con el personaje (1 = va con él; 0 = se queda en el aire).")]
        [SerializeField, Range(0f, 1f)] float breathFollow = 1f;

        [Header("Sudor")]
        [SerializeField] bool sweat = true;
        [Tooltip("M_VFX_ToonFlat.")]
        [SerializeField] Material flatMaterial;
        [Tooltip("M_VFX_ToonVolume: gotas con volumen y brillo (vacío = flatMaterial).")]
        [SerializeField] Material volumeMaterial;
        [SerializeField] Color sweatColor = new(0.8f, 0.95f, 1f);
        [Tooltip("Color hacia la cola de la gota.")]
        [SerializeField] Color sweatTipColor = new(0.5f, 0.8f, 1f);
        [SerializeField] Color sweatShadow = new(0.68f, 0.82f, 1f);
        [SerializeField] Color sweatOutline = new(0.14f, 0.34f, 0.56f);
        [Tooltip("Brillo de las gotas (alfa = intensidad).")]
        [SerializeField] Color sweatHighlight = Color.white;
        [Tooltip("Luz de borde de las gotas (alfa = intensidad).")]
        [SerializeField] Color sweatRim = new(0.9f, 1f, 1f, 0.7f);
        [Tooltip("Desde qué altura del personaje salen las gotas (0 = pies, 1 = lo más alto): la cabeza.")]
        [SerializeField, Range(0f, 1f)] float headHeight = 0.62f;
        [Tooltip("Segundos entre gotas que saltan (mín, máx).")]
        [SerializeField] Vector2 sweatInterval = new(0.35f, 0.7f);
        [SerializeField] Vector2Int dropsPerFling = new(1, 2);
        [SerializeField] Vector2 dropSpeed = new(1.3f, 2.1f);
        [Tooltip("Tamaño de la gota que salta (metros).")]
        [SerializeField] float dropSize = 0.05f;
        [Tooltip("Segundos entre goterones en la sien (mín, máx). 0 = sin goterones.")]
        [SerializeField] Vector2 beadInterval = new(1.1f, 1.9f);
        [Tooltip("Tamaño del goterón (metros).")]
        [SerializeField] float beadSize = 0.1f;
        [Tooltip("Cuánto resbala el goterón antes de caer (metros) y en cuánto tiempo.")]
        [SerializeField] float beadSlide = 0.06f;
        [SerializeField] float beadHold = 0.55f;
        [SerializeField] float gravity = 9.8f;
        [Tooltip("Tamaño de la salpicadura (metros).")]
        [SerializeField] float splatSize = 0.11f;
        [SerializeField] float splatLifetime = 0.7f;
        [SerializeField] LayerMask groundMask = Physics.DefaultRaycastLayers;

        /// <summary>Encendido / apagado (lo pone PlayerVFX según la estamina).</summary>
        public bool Tired { get; set; }

        const int DropPool = 10;
        const int SplatPool = 6;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        sealed class Drop
        {
            public FlatPiece piece;
            public Vector3 velocity;
            public float age = -1f;
            // Goterón: pegado a la cabeza (en espacio del personaje) hasta que cae.
            public bool bead, big;
            public Vector3 beadPoint, beadNormal, beadSlide;
        }

        ParticleSystem breath;
        ParticleSystem.Particle[] breathParticles;
        MaterialPropertyBlock colorBlock;
        Transform character;
        CharacterSurface surface;
        Vector3 lastPosition;
        Vector3 velocity;
        float breathTimer;
        float followUpTimer = -1f;
        float sweatTimer;
        float beadTimer;
        float testTimer;
        bool nextSide;

        Transform sweatContainer;
        Drop[] drops;
        FlatPiece[] splats;
        float[] splatAge;
        int nextDrop, nextSplat;

        void Awake()
        {
            Rigidbody body = GetComponentInParent<Rigidbody>();
            character = body != null ? body.transform : transform.root;
            surface = new CharacterSurface(character);
            lastPosition = character.position;

            if (smokeMaterial != null)
            {
                breath = ToonSmoke.CreateSystem("Breath", transform, smokeMaterial, ToonSmoke.ConeMeshes, randomYaw: false, maxParticles: 48);
                ToonSmoke.ApplyColor(breath, ref colorBlock, breathColor);
            }
            else Debug.LogWarning("TiredVFX: falta smokeMaterial (M_VFX_ToonSmoke).", this);

            if (sweat && flatMaterial != null) CreateSweat();
        }

        void OnValidate()
        {
            if (Application.isPlaying) ToonSmoke.ApplyColor(breath, ref colorBlock, breathColor);
        }

        void OnEnable()
        {
            if (character != null) lastPosition = character.position;
            velocity = Vector3.zero;
        }

        void OnDisable()
        {
            if (drops != null) foreach (Drop drop in drops) StopDrop(drop);
            if (splats == null) return;
            for (int i = 0; i < splats.Length; i++)
            {
                splats[i].Visible = false;
                splatAge[i] = -1f;
            }
        }

        void OnDestroy()
        {
            surface?.Dispose();
            if (sweatContainer != null) Destroy(sweatContainer.gameObject);
        }

        [VfxTest("Test cansado (4 s)")]
        void Test() => testTimer = 4f;

        // ---------------- Bucle ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            TrackVelocity(dt);
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
                    sweatTimer -= dt;
                    if (sweatTimer <= 0f)
                    {
                        sweatTimer = Random.Range(sweatInterval.x, sweatInterval.y);
                        int count = Random.Range(dropsPerFling.x, dropsPerFling.y + 1);
                        for (int i = 0; i < count; i++) Fling();
                    }
                    if (beadInterval.y > 0f)
                    {
                        beadTimer -= dt;
                        if (beadTimer <= 0f)
                        {
                            beadTimer = Random.Range(beadInterval.x, beadInterval.y);
                            Bead();
                        }
                    }
                }
            }
            else
            {
                // Al cansarse, el primer jadeo sale enseguida.
                breathTimer = Mathf.Min(breathTimer, 0.1f);
                sweatTimer = Mathf.Max(sweatTimer, 0.3f);
                beadTimer = Mathf.Max(beadTimer, 0.5f);
            }

            UpdateDrops(dt);
            UpdateSplats(dt);
        }

        // El aliento va con el personaje: se desplaza lo mismo que él (después de simularse las partículas).
        void LateUpdate()
        {
            Vector3 delta = character.position - lastPosition;
            lastPosition = character.position;
            if (breath == null || breathFollow <= 0f || breath.particleCount == 0) return;
            // Teletransporte (reaparecer): no arrastrar el humo.
            if (delta.sqrMagnitude < 1e-10f || delta.sqrMagnitude > 4f) return;

            breathParticles ??= new ParticleSystem.Particle[breath.main.maxParticles];
            int count = breath.GetParticles(breathParticles);
            Vector3 shift = delta * breathFollow;
            for (int i = 0; i < count; i++) breathParticles[i].position += shift;
            breath.SetParticles(breathParticles, count);
        }

        // Velocidad del personaje (suavizada) a partir de cómo se mueve: vale igual en el dueño que en los demás
        // clientes (donde el Rigidbody es cinemático).
        void TrackVelocity(float dt)
        {
            if (dt <= 0f) return;
            Vector3 current = (character.position - lastPosition) / dt;
            if (current.sqrMagnitude > 400f) current = Vector3.zero;
            velocity = Vector3.Lerp(velocity, current, 1f - Mathf.Exp(-dt * 20f));
        }

        // ---------------- Aliento ----------------

        void Exhale(float strength)
        {
            if (breath == null) return;
            Vector3 facing = character.forward;
            Vector3 forward = Quaternion.AngleAxis(breathDown, character.right) * facing;
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-10f, 10f), Vector3.up) * Quaternion.AngleAxis(Random.Range(-6f, 6f), character.right) * forward;
            float size = Random.Range(breathSize.x, breathSize.y) * strength;

            // La boca: el primer punto de la malla delante de este objeto, viniendo de fuera.
            surface.FindEntry(transform.position + facing, -facing, 2f, out Vector3 mouth, out Vector3 normal);

            ParticleSystem.EmitParams emit = new()
            {
                applyShapeToPosition = false,
                // La parte estrecha del cono justo en la superficie.
                position = mouth + normal * 0.01f + dir * (size * 0.5f),
                velocity = dir * (breathSpeed * Random.Range(0.8f, 1.2f) * Mathf.Lerp(0.6f, 1f, strength)) + Vector3.up * 0.15f,
                startSize = size,
                startLifetime = Random.Range(breathLifetime.x, breathLifetime.y),
                rotation3D = Quaternion.LookRotation(dir, Vector3.up).eulerAngles,
            };
            breath.Emit(emit, 1);
        }

        // ---------------- Sudor ----------------

        void CreateSweat()
        {
            // Fuera del personaje: las gotas vuelan por el mundo y las salpicaduras se quedan en el suelo.
            sweatContainer = new GameObject($"Sweat_{character.name}").transform;
            Mesh dropMesh = FlatMeshes.Drop();
            drops = new Drop[DropPool];
            for (int i = 0; i < DropPool; i++)
            {
                drops[i] = new Drop
                {
                    piece = new FlatPiece($"Drop{i}", sweatContainer, dropMesh, volumeMaterial != null ? volumeMaterial : flatMaterial)
                        .Colors(sweatColor, sweatTipColor, sweatOutline).Cel(1f, sweatShadow).Shine(sweatHighlight, sweatRim),
                };
            }

            splats = new FlatPiece[SplatPool];
            splatAge = new float[SplatPool];
            for (int i = 0; i < SplatPool; i++)
            {
                splats[i] = new FlatPiece($"Splat{i}", sweatContainer, FlatMeshes.Splat(70 + i), flatMaterial)
                    .Colors(sweatColor, sweatTipColor, sweatOutline);
                splatAge[i] = -1f;
            }
        }

        Drop NextDrop()
        {
            Drop drop = drops[nextDrop];
            nextDrop = (nextDrop + 1) % drops.Length;
            StopDrop(drop);
            return drop;
        }

        // Una gota salta de la cabeza hacia un lado (alternando), con la velocidad del personaje.
        void Fling()
        {
            nextSide = !nextSide;
            Vector3 side = character.right * (nextSide ? 1f : -1f);
            Vector3 toward = (side * 0.75f + Vector3.up * 0.65f + character.forward * 0.15f).normalized;
            surface.RandomPoint(toward, 0.35f, headHeight, out Vector3 point, out Vector3 normal);

            Drop drop = NextDrop();
            Vector3 dir = (normal * 0.5f + toward * 0.5f + Vector3.up * 0.35f).normalized;
            drop.velocity = dir * Random.Range(dropSpeed.x, dropSpeed.y) + velocity;
            drop.age = 0f;
            drop.bead = false;
            drop.big = false;
            drop.piece.Transform.position = point + normal * (dropSize * 0.6f);
            drop.piece.Visible = true;
        }

        // Goterón en la sien del lado que ve la cámara: aparece, resbala un poco y cae.
        void Bead()
        {
            Camera cam = FlatFx.Camera;
            Vector3 toCamera = cam != null ? -cam.transform.forward : -character.forward;
            float sign = Vector3.Dot(character.right, toCamera);
            Vector3 side = character.right * (Mathf.Abs(sign) < 0.15f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(sign));
            Vector3 toward = (side * 0.7f + toCamera * 0.5f + Vector3.up * 0.1f).normalized;
            surface.RandomPoint(toward, 0.45f, headHeight + 0.05f, out Vector3 point, out Vector3 normal);

            Drop drop = NextDrop();
            Vector3 slide = Vector3.ProjectOnPlane(Vector3.down, normal);
            slide = slide.sqrMagnitude > 1e-4f ? slide.normalized : Vector3.down;
            drop.bead = true;
            drop.big = true;
            drop.beadPoint = character.InverseTransformPoint(point);
            drop.beadNormal = character.InverseTransformDirection(normal);
            drop.beadSlide = character.InverseTransformDirection(slide);
            drop.age = 0f;
            drop.piece.Visible = true;
            PlaceBead(drop);
        }

        void PlaceBead(Drop drop)
        {
            float slide = beadSlide * FlatFx.OutCubic(FlatFx.Phase(drop.age, beadHold * 0.25f, beadHold));
            Vector3 normal = character.TransformDirection(drop.beadNormal);
            Vector3 down = character.TransformDirection(drop.beadSlide);
            float pop = FlatFx.OutBack(FlatFx.Phase(drop.age, 0f, 0.14f), 2.4f);
            // Tiembla un poco al aparecer (gelatina).
            float jiggle = 1f + 0.18f * Mathf.Sin(drop.age * 38f) * (1f - FlatFx.Phase(drop.age, 0f, 0.35f));
            Transform t = drop.piece.Transform;
            t.SetPositionAndRotation(character.TransformPoint(drop.beadPoint) + down * slide + normal * (beadSize * 0.45f),
                Quaternion.LookRotation(down, normal));
            t.localScale = new Vector3(1f / jiggle, 1f / jiggle, jiggle) * (beadSize * pop);
        }

        void UpdateDrops(float dt)
        {
            if (drops == null) return;
            foreach (Drop drop in drops)
            {
                if (drop.age < 0f) continue;
                drop.age += dt;

                if (drop.bead)
                {
                    if (drop.age < beadHold)
                    {
                        PlaceBead(drop);
                        continue;
                    }
                    // Se suelta: cae con la velocidad del personaje y un empujoncito hacia fuera.
                    drop.bead = false;
                    drop.velocity = velocity + character.TransformDirection(drop.beadNormal) * 0.35f + Vector3.down * 0.3f;
                }

                Transform t = drop.piece.Transform;
                Vector3 from = t.position;
                drop.velocity += Vector3.down * (gravity * dt);
                Vector3 to = from + drop.velocity * dt;

                if (drop.age > 2.5f)
                {
                    StopDrop(drop);
                    continue;
                }
                if (drop.age > 0.05f && HitGround(from, to, out RaycastHit hit))
                {
                    StopDrop(drop);
                    Splat(hit.point, hit.normal);
                    continue;
                }

                t.position = to;
                float speed = drop.velocity.magnitude;
                if (speed > 0.01f) t.rotation = Quaternion.LookRotation(drop.velocity);
                float size = drop.big ? beadSize : dropSize;
                float pop = drop.big ? 1f : FlatFx.OutBack(FlatFx.Phase(drop.age, 0f, 0.08f));
                // Se estira al caer rápido (cartoon).
                t.localScale = new Vector3(1f, 1f, 1f + speed * 0.1f) * (size * pop);
            }
        }

        static void StopDrop(Drop drop)
        {
            drop.age = -1f;
            drop.bead = false;
            drop.piece.Visible = false;
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
                if (hits[i].collider.transform.IsChildOf(character)) continue;
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
