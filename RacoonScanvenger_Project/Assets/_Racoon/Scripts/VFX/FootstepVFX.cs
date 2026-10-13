using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Pasos de un personaje: polvo (puffs cartoon) y huellas en el suelo.
    ///
    /// Los pasos se generan por DISTANCIA recorrida (no hay Animation Events): cada zancada, pie izquierdo
    /// y derecho alternos. Como solo usa el movimiento del transform, funciona igual en todos los clientes
    /// (también en los remotos) y sirve para jugadores y NPCs.
    ///  - Polvo: pequeño al andar, más grande al correr, y una nube extra al frenar en seco o girar
    ///    bruscamente. Si no asignas un ParticleSystem, se crea uno solo con el material de polvo.
    ///  - Huellas: quads planos pegados al suelo (se adaptan a la inclinación), sutiles, que se desvanecen.
    ///
    /// Montaje: hijo del personaje (Z = hacia delante). Quien lo controla puede apagar los pasos con
    /// <see cref="StepsEnabled"/> (p. ej. durante un dash o un golpe).
    /// </summary>
    public class FootstepVFX : MonoBehaviour
    {
        [Header("Pasos")]
        [Tooltip("Velocidad (m/s) de andar y de correr. Entre medias se interpola todo (zancada, polvo...).")]
        [SerializeField] Vector2 walkRunSpeed = new(3.5f, 6.5f);
        [Tooltip("Metros entre paso y paso al andar / al correr.")]
        [SerializeField] Vector2 strideLength = new(0.45f, 0.8f);
        [Tooltip("Por debajo de esta velocidad no hay pasos.")]
        [SerializeField] float minStepSpeed = 0.6f;
        [Tooltip("Separación lateral de cada pie respecto al centro (metros).")]
        [SerializeField] float footSpacing = 0.15f;
        [Tooltip("Desplazamiento de los pies hacia delante (metros, local).")]
        [SerializeField] float footForward = 0.05f;

        [Header("Suelo")]
        [SerializeField] LayerMask groundMask = Physics.DefaultRaycastLayers;
        [Tooltip("Desde qué altura sobre el pivote se lanza el rayo hacia abajo.")]
        [SerializeField] float rayStartHeight = 0.5f;
        [Tooltip("Hasta dónde busca el suelo por debajo del pivote. Si no lo encuentra (en el aire), no hay paso.")]
        [SerializeField] float rayDepth = 1.2f;

        [Header("Polvo")]
        [Tooltip("Vacío = se crea uno automáticamente con dustMaterial.")]
        [SerializeField] ParticleSystem dustParticles;
        [SerializeField] Material dustMaterial;
        [SerializeField] Color dustColor = new(0.86f, 0.8f, 0.7f, 1f);
        [Tooltip("Puffs por paso al andar / al correr.")]
        [SerializeField] Vector2Int dustCount = new(1, 4);
        [Tooltip("Tamaño de cada puff al andar / al correr (metros).")]
        [SerializeField] Vector2 dustSize = new(0.18f, 0.32f);
        [Tooltip("Velocidad con la que salen al andar / al correr.")]
        [SerializeField] Vector2 dustSpeed = new(0.4f, 1.2f);
        [Tooltip("Duración de cada puff (mín, máx).")]
        [SerializeField] Vector2 dustLifetime = new(0.35f, 0.6f);

        [Header("Frenazo / giro brusco")]
        [SerializeField] bool skidDust = true;
        [Tooltip("Puffs extra al frenar en seco o girar bruscamente.")]
        [SerializeField] int skidCount = 6;
        [Tooltip("Ángulo de giro (grados) que cuenta como brusco.")]
        [SerializeField] float skidAngle = 100f;
        [Tooltip("Caída de velocidad (m/s) de golpe que cuenta como frenazo.")]
        [SerializeField] float skidBrake = 4f;
        [SerializeField] float skidCooldown = 0.3f;

        [Header("Huellas")]
        [SerializeField] bool pawPrints = true;
        [SerializeField] Material pawMaterial;
        [SerializeField] Color pawColor = new(0.25f, 0.18f, 0.12f, 1f);
        [Tooltip("Opacidad de las huellas (sutiles).")]
        [SerializeField, Range(0f, 1f)] float pawOpacity = 0.3f;
        [Tooltip("Tamaño de la huella (metros).")]
        [SerializeField] float pawSize = 0.16f;
        [Tooltip("Segundos visibles a opacidad completa + segundos desvaneciéndose.")]
        [SerializeField] Vector2 pawLifetime = new(2.5f, 1.5f);
        [Tooltip("Huellas a la vez como máximo (las más viejas se reutilizan).")]
        [SerializeField] int pawPoolSize = 24;
        [Tooltip("Gira la huella hacia fuera un poco (grados), como los pies de verdad.")]
        [SerializeField] float pawToeOut = 8f;

        /// <summary>Apaga/enciende los pasos (polvo y huellas). El frenazo también se apaga.</summary>
        public bool StepsEnabled { get; set; } = true;

        static readonly RaycastHit[] hits = new RaycastHit[8];
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Mesh quadMesh;

        Transform root;
        Vector3 lastPosition;
        float speed;
        float slowSpeed;
        Vector3 moveDirection;
        Vector3 slowDirection;
        float distanceSinceStep;
        bool rightFoot;
        float skidTimer;

        // Huellas
        Transform pawContainer;
        Transform[] paws;
        MeshRenderer[] pawRenderers;
        float[] pawBorn;
        int nextPaw;
        MaterialPropertyBlock block;

        void Awake()
        {
            // Raíz del personaje (su Rigidbody); sin él, la raíz de la jerarquía.
            Rigidbody body = GetComponentInParent<Rigidbody>();
            root = body != null ? body.transform : transform.root;
            lastPosition = transform.position;
            if (dustParticles == null && dustMaterial != null) dustParticles = CreateDustSystem();
            if (pawPrints && pawMaterial != null) CreatePawPool();
        }

        void OnDestroy()
        {
            if (pawContainer != null) Destroy(pawContainer.gameObject);
        }

        void OnDisable()
        {
            if (paws == null) return;
            foreach (Transform paw in paws) paw.gameObject.SetActive(false);
        }

        // ---------------- Bucle ----------------

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;
            float step = delta.magnitude;
            // Saltos enormes (teleport, respawn): no cuentan.
            if (step > 3f) step = 0f;

            float rawSpeed = step / dt;
            speed = Mathf.Lerp(speed, rawSpeed, 1f - Mathf.Exp(-20f * dt));
            slowSpeed = Mathf.Lerp(slowSpeed, rawSpeed, 1f - Mathf.Exp(-5f * dt));
            if (step > 0.0001f)
            {
                moveDirection = delta / step;
                slowDirection = slowDirection == Vector3.zero ? moveDirection : Vector3.Slerp(slowDirection, moveDirection, 1f - Mathf.Exp(-6f * dt)).normalized;
            }

            UpdatePaws();

            if (!StepsEnabled)
            {
                // Al volver a encenderse no debe saltar un "frenazo" por lo que pasó mientras estaba apagado.
                distanceSinceStep = 0f;
                slowSpeed = speed;
                slowDirection = moveDirection;
                return;
            }

            UpdateSkid(dt);

            if (speed < minStepSpeed)
            {
                // Parado: el próximo paso sale enseguida al arrancar.
                distanceSinceStep = Mathf.Max(distanceSinceStep, CurrentStride() * 0.6f);
                return;
            }

            distanceSinceStep += step;
            if (distanceSinceStep >= CurrentStride())
            {
                distanceSinceStep = 0f;
                rightFoot = !rightFoot;
                Step(rightFoot);
            }
        }

        float Intensity => Mathf.Clamp01(Mathf.InverseLerp(walkRunSpeed.x, walkRunSpeed.y, speed));
        float CurrentStride() => Mathf.Lerp(strideLength.x, strideLength.y, Intensity);

        // ---------------- Paso ----------------

        void Step(bool right)
        {
            Vector3 forward = moveDirection.sqrMagnitude > 0.01f ? moveDirection : FlatForward();
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 foot = transform.position + side * (right ? footSpacing : -footSpacing) + forward * footForward;
            if (!FindGround(foot, out Vector3 point, out Vector3 normal)) return;

            float t = Intensity;
            EmitDust(point, forward, Mathf.RoundToInt(Mathf.Lerp(dustCount.x, dustCount.y, t)), t, 1f);
            if (paws != null) PlacePaw(point, normal, forward, right);
        }

        void UpdateSkid(float dt)
        {
            if (!skidDust || dustParticles == null) return;
            if (skidTimer > 0f)
            {
                skidTimer -= dt;
                return;
            }

            bool brake = slowSpeed - speed > skidBrake;
            bool turn = speed > walkRunSpeed.x && slowDirection != Vector3.zero && Vector3.Angle(moveDirection, slowDirection) > skidAngle;
            if (!brake && !turn) return;

            skidTimer = skidCooldown;
            Vector3 forward = slowDirection != Vector3.zero ? slowDirection : FlatForward();
            if (!FindGround(transform.position, out Vector3 point, out _)) return;
            // Sale hacia donde iba (la inercia), más fuerte que un paso.
            EmitDust(point, -forward, skidCount, 1f, 1.3f);
        }

        Vector3 FlatForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        // Rayo hacia abajo ignorando los colliders del propio personaje.
        bool FindGround(Vector3 position, out Vector3 point, out Vector3 normal)
        {
            point = position;
            normal = Vector3.up;
            Vector3 origin = position + Vector3.up * rayStartHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, rayStartHeight + rayDepth, groundMask, QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.transform.IsChildOf(root)) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    point = hits[i].point;
                    normal = hits[i].normal;
                }
            }
            return best < float.MaxValue;
        }

        // ---------------- Polvo ----------------

        void EmitDust(Vector3 point, Vector3 forward, int count, float intensity, float sizeMultiplier)
        {
            if (dustParticles == null || count <= 0) return;

            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float size = Mathf.Lerp(dustSize.x, dustSize.y, intensity) * sizeMultiplier;
            float velocity = Mathf.Lerp(dustSpeed.x, dustSpeed.y, intensity);
            ParticleSystem.EmitParams emit = new() { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                // Hacia atrás y a los lados (lo que levanta el pie), y un poco hacia arriba.
                Vector3 direction = (-forward * 0.7f + side * Random.Range(-0.8f, 0.8f) + Vector3.up * Random.Range(0.2f, 0.6f)).normalized;
                emit.position = point + side * Random.Range(-0.05f, 0.05f) + Vector3.up * (size * 0.3f);
                emit.velocity = direction * (velocity * Random.Range(0.6f, 1.2f));
                emit.startSize = size * Random.Range(0.75f, 1.15f);
                emit.startLifetime = Random.Range(dustLifetime.x, dustLifetime.y);
                Color color = dustColor;
                float shade = Random.Range(0.92f, 1.05f);
                emit.startColor = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);
                dustParticles.Emit(emit, 1);
            }
        }

        // Puffs cartoon: crecen rápido, frenan y se encogen hasta desaparecer (no se vuelven transparentes).
        ParticleSystem CreateDustSystem()
        {
            GameObject go = new("DustParticles");
            go.transform.SetParent(transform, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(dustLifetime.x, dustLifetime.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f; // Sube un poquito.
            main.maxParticles = 128;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.18f, 1f), new Keyframe(0.6f, 0.85f), new Keyframe(1f, 0f)));

            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = 4f; // Salen rápido y frenan.

            ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = dustMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;

            system.Play();
            return system;
        }

        // ---------------- Huellas ----------------

        void CreatePawPool()
        {
            if (quadMesh == null) quadMesh = CreateQuad();
            block = new MaterialPropertyBlock();

            // Fuera del personaje: las huellas se quedan en el mundo.
            pawContainer = new GameObject($"PawPrints_{root.name}").transform;
            int size = Mathf.Max(1, pawPoolSize);
            paws = new Transform[size];
            pawRenderers = new MeshRenderer[size];
            pawBorn = new float[size];
            for (int i = 0; i < size; i++)
            {
                GameObject go = new("Paw");
                go.transform.SetParent(pawContainer, false);
                go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pawMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                go.SetActive(false);
                paws[i] = go.transform;
                pawRenderers[i] = renderer;
            }
        }

        void PlacePaw(Vector3 point, Vector3 normal, Vector3 forward, bool right)
        {
            int i = nextPaw;
            nextPaw = (nextPaw + 1) % paws.Length;

            // Tumbada en el suelo, mirando hacia donde va, un poco abierta hacia fuera.
            Vector3 along = Vector3.ProjectOnPlane(forward, normal);
            if (along.sqrMagnitude < 0.0001f) along = Vector3.ProjectOnPlane(transform.forward, normal);
            Quaternion rotation = Quaternion.LookRotation(along.normalized, normal) * Quaternion.Euler(0f, right ? pawToeOut : -pawToeOut, 0f);

            Transform paw = paws[i];
            paw.SetPositionAndRotation(point + normal * 0.01f, rotation); // Un pelo por encima: sin parpadeo con el suelo.
            // El pie izquierdo es la huella en espejo.
            paw.localScale = new Vector3(right ? pawSize : -pawSize, 1f, pawSize);
            paw.gameObject.SetActive(true);
            pawBorn[i] = Time.time;
            SetPawAlpha(i, pawOpacity);
        }

        void UpdatePaws()
        {
            if (paws == null) return;
            float visible = pawLifetime.x, fade = Mathf.Max(0.01f, pawLifetime.y);
            for (int i = 0; i < paws.Length; i++)
            {
                if (!paws[i].gameObject.activeSelf) continue;
                float age = Time.time - pawBorn[i];
                if (age >= visible + fade)
                {
                    paws[i].gameObject.SetActive(false);
                    continue;
                }
                if (age > visible) SetPawAlpha(i, pawOpacity * (1f - (age - visible) / fade));
            }
        }

        void SetPawAlpha(int i, float alpha)
        {
            Color color = pawColor;
            color.a *= alpha;
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            pawRenderers[i].SetPropertyBlock(block);
        }

        // Quad 1x1 tumbado (XZ), mirando hacia arriba. UV v = 1 hacia delante (+Z): los dedos de la textura van arriba.
        static Mesh CreateQuad()
        {
            Mesh mesh = new() { name = "PawQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------- Gizmos ----------------

        void OnDrawGizmosSelected()
        {
            // Pies y alcance del rayo.
            Vector3 forward = Application.isPlaying && moveDirection.sqrMagnitude > 0.01f ? moveDirection : FlatForward();
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 foot = transform.position + side * (footSpacing * sign) + forward * footForward;
                Gizmos.color = sign > 0 ? new Color(1f, 0.6f, 0.2f) : new Color(0.2f, 0.8f, 1f);
                Gizmos.DrawLine(foot + Vector3.up * rayStartHeight, foot + Vector3.down * rayDepth);
                Gizmos.DrawWireSphere(foot, 0.04f);
            }
        }
    }
}
