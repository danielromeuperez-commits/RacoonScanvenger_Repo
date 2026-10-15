using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Racoon.VFX
{
    /// <summary>
    /// Pasos de un personaje: polvo (puffs cartoon) y huellas en el suelo.
    ///
    /// Los pasos se generan por DISTANCIA recorrida (no hay Animation Events): cada zancada, pie izquierdo
    /// y derecho alternos. Como solo usa el movimiento del transform, funciona igual en todos los clientes
    /// (también en los remotos) y sirve para jugadores y NPCs.
    ///  - Polvo: puffs cartoon VOLUMÉTRICOS, como el humo dibujado: cada partícula es una nube 3D de
    ///    burbujas solapadas (una grande y varias pequeñas alrededor). Pequeño al andar, más grande al
    ///    correr, y una nube extra al frenar en seco o girar bruscamente. Si no asignas un ParticleSystem,
    ///    se crea uno solo con el material de polvo.
    ///  - Dash: explosión de puffs hacia atrás al arrancar y un rastro mientras dura (<see cref="PlayDash"/>).
    ///  - Colas en punta: estelas finas que salen disparadas al hacer dash o frenar (Trails de partículas).
    ///  - Con el material M_VFX_ToonSmoke (Shader Graph SG_VFX_ToonSmoke) los puffs tienen tonos planos
    ///    (claro hacia la luz, oscuro detrás) y al final se rompen en bolitas; los datos por partícula que
    ///    necesita se configuran solos (vertex streams).
    ///  - Colores por jugador, independientes: humo (<see cref="SmokeColor"/>) y colas (<see cref="WispColor"/>), en el
    ///    Inspector o por código.
    ///  - Huellas: malla con FORMA de pata (almohadilla + 5 dedos, no un quad: nunca se ve el cuadrado),
    ///    pegada al suelo y adaptada a su inclinación, sutil, que se desvanece.
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
        [Tooltip("Mejor opaco e iluminado (p. ej. FlatKit StylizedSurface): los puffs no se vuelven transparentes, se encogen.")]
        [SerializeField] Material dustMaterial;
        [Tooltip("Malla de cada puff. Vacío = nubes de burbujas generadas (3 variantes). Con SG_VFX_ToonSmoke, una malla " +
                 "propia necesita UV0 = (orden de la burbuja 0-1, radio de la burbuja) en cada vértice.")]
        [SerializeField] Mesh dustMesh;
        [Tooltip("Color del humo (los puffs) de este jugador: el tono iluminado. Las sombras salen solas a partir de él " +
                 "con los tintes del material, así que mejor colores claros (uno oscuro deja la sombra casi negra).")]
        [FormerlySerializedAs("dustColor")]
        [SerializeField] Color smokeColor = new(0.83f, 0.85f, 0.9f, 1f);
        [Tooltip("Puffs por paso al andar / al correr.")]
        [SerializeField] Vector2Int dustCount = new(1, 4);
        [Tooltip("Tamaño de cada puff al andar / al correr (metros).")]
        [SerializeField] Vector2 dustSize = new(0.18f, 0.32f);
        [Tooltip("Velocidad con la que salen al andar / al correr.")]
        [SerializeField] Vector2 dustSpeed = new(0.4f, 1.2f);
        [Tooltip("Duración de cada puff (mín, máx).")]
        [SerializeField] Vector2 dustLifetime = new(0.5f, 0.85f);

        [Header("Frenazo / giro brusco")]
        [SerializeField] bool skidDust = true;
        [Tooltip("Puffs extra al frenar en seco o girar bruscamente.")]
        [SerializeField] int skidCount = 6;
        [Tooltip("Ángulo de giro (grados) que cuenta como brusco.")]
        [SerializeField] float skidAngle = 100f;
        [Tooltip("Caída de velocidad (m/s) de golpe que cuenta como frenazo.")]
        [SerializeField] float skidBrake = 4f;
        [SerializeField] float skidCooldown = 0.3f;

        [Header("Dash")]
        [Tooltip("Puffs de la explosión al arrancar el dash.")]
        [SerializeField] int dashBurstCount = 9;
        [Tooltip("Tamaño de esos puffs respecto a los de correr.")]
        [SerializeField] float dashBurstSize = 1.5f;
        [Tooltip("Velocidad de esos puffs respecto a los de correr.")]
        [SerializeField] float dashBurstSpeed = 2f;
        [Tooltip("Cada cuántos segundos deja un puff mientras dura el dash (0 = ninguno).")]
        [SerializeField] float dashTrailInterval = 0.04f;
        [Tooltip("Tamaño de los puffs del recorrido respecto a los de correr.")]
        [SerializeField] float dashTrailSize = 0.9f;

        [Header("Colas en punta (dash / frenazo)")]
        [SerializeField] bool wisps = true;
        [Tooltip("Vacío = una copia del material de polvo en modo Wisp (SG_VFX_ToonSmoke con Wisp = 1).")]
        [SerializeField] Material wispMaterial;
        [Tooltip("Color de las colas, independiente del humo (color plano, sin sombra).")]
        [SerializeField] Color wispColor = new(0.83f, 0.85f, 0.9f, 1f);
        [SerializeField] int dashWispCount = 3;
        [SerializeField] int skidWispCount = 2;
        [Tooltip("Velocidad de salida (mín, máx). Frenan solas.")]
        [SerializeField] Vector2 wispSpeed = new(3f, 5f);
        [Tooltip("Grosor máximo de la cola (metros).")]
        [SerializeField] float wispWidth = 0.1f;
        [Tooltip("Segundos que dura cada cola.")]
        [SerializeField] float wispLifetime = 0.45f;
        [Tooltip("Cuánto se curvan hacia arriba.")]
        [SerializeField] float wispLift = 0.4f;

        [Header("Huellas")]
        [SerializeField] bool pawPrints = true;
        [SerializeField] Material pawMaterial;
        [SerializeField] Color pawColor = new(0.25f, 0.18f, 0.12f, 1f);
        [Tooltip("Opacidad de las huellas (sutiles).")]
        [SerializeField, Range(0f, 1f)] float pawOpacity = 0.3f;
        [Tooltip("Tamaño de la huella (metros).")]
        [SerializeField] float pawSize = 0.28f;
        [Tooltip("Segundos visibles a opacidad completa + segundos desvaneciéndose.")]
        [SerializeField] Vector2 pawLifetime = new(1.2f, 0.8f);
        [Tooltip("Huellas a la vez como máximo (las más viejas se reutilizan).")]
        [SerializeField] int pawPoolSize = 24;
        [Tooltip("Gira la huella hacia fuera un poco (grados), como los pies de verdad.")]
        [SerializeField] float pawToeOut = 8f;

        /// <summary>Apaga/enciende los pasos (polvo y huellas). El frenazo también se apaga.</summary>
        public bool StepsEnabled { get; set; } = true;

        /// <summary>Color del humo (tono iluminado). Cambia también el de los puffs que ya están en el aire.</summary>
        public Color SmokeColor
        {
            get => smokeColor;
            set
            {
                smokeColor = value;
                ApplyColors();
            }
        }

        /// <summary>Color de las colas en punta, independiente del humo.</summary>
        public Color WispColor
        {
            get => wispColor;
            set
            {
                wispColor = value;
                ApplyColors();
            }
        }

        static readonly RaycastHit[] hits = new RaycastHit[8];
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int WispId = Shader.PropertyToID("_Wisp");
        static Mesh pawMesh;

        Transform root;
        Vector3 lastPosition;
        float speed;
        float slowSpeed;
        Vector3 moveDirection;
        Vector3 slowDirection;
        float distanceSinceStep;
        bool rightFoot;
        float skidTimer;
        float dashTimer;
        float dashTrailTimer;
        Vector3 dashDirection;
        ParticleSystem wispParticles;
        Material runtimeWispMaterial;
        MaterialPropertyBlock smokeBlock;
        MaterialPropertyBlock wispBlock;

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
            if (dustParticles == null && dustMaterial != null)
                dustParticles = ToonSmoke.CreateSystem("DustParticles", transform, dustMaterial,
                    dustMesh != null ? new[] { dustMesh } : ToonSmoke.CloudMeshes);
            else if (dustParticles != null)
                ToonSmoke.ConfigureStreams(dustParticles);
            if (wisps) CreateWisps();
            ApplyColors();
            if (pawPrints && pawMaterial != null) CreatePawPool();
        }

        // Cambios de color en el Inspector durante Play se ven al momento.
        void OnValidate()
        {
            if (Application.isPlaying) ApplyColors();
        }

        // Los colores van al renderer de este jugador (sin tocar el asset). Cambian también las partículas que ya existen.
        void ApplyColors()
        {
            ToonSmoke.ApplyColor(dustParticles, ref smokeBlock, smokeColor);
            ToonSmoke.ApplyColor(wispParticles, ref wispBlock, wispColor);
        }

        void OnDestroy()
        {
            if (pawContainer != null) Destroy(pawContainer.gameObject);
            if (runtimeWispMaterial != null) Destroy(runtimeWispMaterial);
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
            UpdateDashTrail(dt);

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
            EmitWisps(point, forward, skidWispCount, 0.7f);
        }

        // ---------------- Dash ----------------

        /// <summary>
        /// Humo del dash: explosión de puffs hacia atrás al arrancar y un rastro mientras dura.
        /// Funciona aunque los pasos estén apagados (durante el dash lo están).
        /// </summary>
        public void PlayDash(Vector3 direction, float duration)
        {
            direction.y = 0f;
            dashDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : FlatForward();
            dashTimer = duration;
            dashTrailTimer = dashTrailInterval;

            if (!FindGround(transform.position, out Vector3 point, out _)) return;
            EmitDust(point, dashDirection, dashBurstCount, 1f, dashBurstSize, dashBurstSpeed, 1.2f);
            EmitWisps(point, -dashDirection, dashWispCount);
        }

        void UpdateDashTrail(float dt)
        {
            if (dashTimer <= 0f) return;
            dashTimer -= dt;
            if (dashTrailInterval <= 0f || dustParticles == null) return;

            dashTrailTimer -= dt;
            if (dashTrailTimer > 0f) return;
            dashTrailTimer += dashTrailInterval;
            if (FindGround(transform.position, out Vector3 point, out _))
                EmitDust(point, dashDirection, 1, 1f, dashTrailSize * Random.Range(0.8f, 1.1f), 0.5f, 0.6f);
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

        // spread: cuánto se abren hacia los lados (1 = normal).
        void EmitDust(Vector3 point, Vector3 forward, int count, float intensity, float sizeMultiplier,
            float speedMultiplier = 1f, float spread = 1f)
        {
            if (dustParticles == null || count <= 0) return;

            Vector3 side = Vector3.Cross(Vector3.up, forward);
            float size = Mathf.Lerp(dustSize.x, dustSize.y, intensity) * sizeMultiplier;
            float velocity = Mathf.Lerp(dustSpeed.x, dustSpeed.y, intensity) * speedMultiplier;
            ParticleSystem.EmitParams emit = new() { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                // Hacia atrás y a los lados (lo que levanta el pie), y un poco hacia arriba.
                Vector3 direction = (-forward * 0.7f + side * (Random.Range(-0.8f, 0.8f) * spread) + Vector3.up * Random.Range(0.2f, 0.6f)).normalized;
                emit.position = point + side * Random.Range(-0.05f, 0.05f) + Vector3.up * (size * 0.4f); // Apoyados en el suelo.
                emit.velocity = direction * (velocity * Random.Range(0.6f, 1.2f));
                emit.startSize = size * Random.Range(0.75f, 1.15f);
                emit.startLifetime = Random.Range(dustLifetime.x, dustLifetime.y);
                dustParticles.Emit(emit, 1);
            }
        }

        // Colas en punta: salen disparadas hacia "direction" en abanico, frenan, se curvan hacia arriba y se
        // retraen hasta desaparecer.
        void EmitWisps(Vector3 point, Vector3 direction, int count, float speedMultiplier = 1f)
        {
            if (wispParticles == null || count <= 0) return;

            Vector3 side = Vector3.Cross(Vector3.up, direction);
            ParticleSystem.EmitParams emit = new() { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float fan = count == 1 ? 0f : i / (count - 1f) * 2f - 1f; // -1..1
                Vector3 heading = (direction + side * (fan * 0.6f + Random.Range(-0.15f, 0.15f)) + Vector3.up * Random.Range(0.05f, 0.25f)).normalized;
                emit.position = point + side * (fan * 0.1f) + Vector3.up * Random.Range(0.08f, 0.2f);
                emit.velocity = heading * (Random.Range(wispSpeed.x, wispSpeed.y) * speedMultiplier);
                emit.startSize = wispWidth * Random.Range(0.8f, 1.2f);
                emit.startLifetime = wispLifetime * Random.Range(0.85f, 1.15f);
                wispParticles.Emit(emit, 1);
            }
        }

        void CreateWisps()
        {
            Material material = wispMaterial;
            if (material == null && dustMaterial != null && dustMaterial.HasProperty(WispId))
            {
                runtimeWispMaterial = new Material(dustMaterial) { name = $"{dustMaterial.name}_Wisp" };
                runtimeWispMaterial.SetFloat(WispId, 1f);
                material = runtimeWispMaterial;
            }
            if (material != null) wispParticles = CreateWispSystem(material);
        }

        // Partículas invisibles que solo dibujan su Trail: una cola en punta por los dos extremos.
        ParticleSystem CreateWispSystem(Material material)
        {
            GameObject go = new("DustWisps");
            go.transform.SetParent(transform, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = wispLifetime;
            main.gravityModifier = -wispLift; // Se curvan hacia arriba.
            main.maxParticles = 32;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = 5f;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));

            ParticleSystem.TrailModule trails = system.trails;
            trails.enabled = true;
            trails.mode = ParticleSystemTrailMode.PerParticle;
            trails.ratio = 1f;
            trails.lifetime = 0.5f; // Fracción de la vida de la partícula: la cola se va retrayendo.
            trails.minVertexDistance = 0.03f;
            trails.worldSpace = true;
            trails.dieWithParticles = false; // Termina de retraerse aunque la partícula ya no exista.
            trails.textureMode = ParticleSystemTrailTextureMode.Stretch;
            trails.sizeAffectsWidth = true;
            trails.sizeAffectsLifetime = false;
            trails.inheritParticleColor = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.15f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.None;
            renderer.trailMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
            return system;
        }

        // ---------------- Huellas ----------------

        void CreatePawPool()
        {
            if (pawMesh == null) pawMesh = CreatePawMesh();
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
                go.AddComponent<MeshFilter>().sharedMesh = pawMesh;
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

        // ---------------- Mallas generadas ----------------

        // Huella con la forma de T_VFX_PawPrint (mismas figuras, en píxeles de la textura de 128):
        // almohadilla (envolvente de dos elipses) + 5 dedos. Tumbada en XZ mirando hacia arriba, 1x1 m,
        // dedos hacia +Z. Las UV coinciden con la textura, así el borde suave de la textura se mantiene.
        static Mesh CreatePawMesh()
        {
            const float texSize = 128f, margin = 1.5f; // Píxeles extra para no cortar el borde difuminado.
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();

            // Almohadilla: una sola figura convexa (sin solapes que oscurezcan la huella).
            List<Vector2> pad = new();
            AddEllipsePoints(pad, new Vector2(64f, 82f), 24f + margin, 20f + margin, 0f, 32);
            AddEllipsePoints(pad, new Vector2(64f, 92f), 18f + margin, 14f + margin, 0f, 32);
            AddFan(ConvexHull(pad), vertices, uvs, triangles, texSize);

            // Dedos: (desplazamiento x, inclinación) como en la textura.
            (float x, float angle)[] toes = { (-30f, -0.55f), (-15f, -0.2f), (0f, 0f), (15f, 0.2f), (30f, 0.55f) };
            for (int i = 0; i < toes.Length; i++)
            {
                float distance = i is >= 1 and <= 3 ? 40f : 33f;
                Vector2 center = new(64f + toes[i].x, 82f - distance + Mathf.Abs(toes[i].x) * 0.25f);
                List<Vector2> toe = new();
                AddEllipsePoints(toe, center, 6.5f + margin, 11f + margin, toes[i].angle, 16);
                AddFan(toe, vertices, uvs, triangles, texSize);
            }

            Mesh mesh = new() { name = "PawPrint" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            Vector3[] normals = new Vector3[vertices.Count];
            Color[] colors = new Color[vertices.Count];
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = Vector3.up;
                colors[i] = Color.white;
            }
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        // Elipse en coordenadas de imagen (y hacia abajo), girada como en el generador de la textura.
        static void AddEllipsePoints(List<Vector2> points, Vector2 center, float rx, float ry, float angle, int segments)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            for (int i = 0; i < segments; i++)
            {
                float t = i * Mathf.PI * 2f / segments;
                float x = rx * Mathf.Cos(t), y = ry * Mathf.Sin(t);
                points.Add(new Vector2(center.x + x * cos - y * sin, center.y + x * sin + y * cos));
            }
        }

        // Abanico desde el centro de una figura convexa. Píxel de imagen → quad 1x1 en XZ (+Z = arriba de la imagen).
        static void AddFan(List<Vector2> outline, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, float texSize)
        {
            Vector2 centroid = Vector2.zero;
            foreach (Vector2 point in outline) centroid += point;
            centroid /= outline.Count;

            int start = vertices.Count;
            AddVertex(centroid);
            foreach (Vector2 point in outline) AddVertex(point);

            for (int i = 0; i < outline.Count; i++)
            {
                int a = start, b = start + 1 + i, c = start + 1 + (i + 1) % outline.Count;
                // Que mire hacia arriba (Unity: cara visible = cross(b-a, c-a)).
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y < 0f) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            void AddVertex(Vector2 pixel)
            {
                Vector2 uv = new(pixel.x / texSize, 1f - pixel.y / texSize);
                uvs.Add(uv);
                vertices.Add(new Vector3(uv.x - 0.5f, 0f, uv.y - 0.5f));
            }
        }

        // Envolvente convexa (cadena monótona), en orden.
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            List<Vector2> hull = new();
            for (int pass = 0; pass < 2; pass++)
            {
                int floor = hull.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[pass == 0 ? i : points.Count - 1 - i];
                    while (hull.Count >= floor + 2 && Cross(hull[^2], hull[^1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;

            static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
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
