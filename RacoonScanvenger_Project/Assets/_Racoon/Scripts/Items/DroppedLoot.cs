using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Producto que suelta un jugador al recibir un golpe fuerte (PlayerLoot.ServerDropLoot): uno de los que llevaba en
    /// la bolsa (manzana, naranja, pan, patatas, NukiProd, lata...), con el modelo y el valor de su estantería.
    /// Sale despedida con física de verdad (rebota, las latas y los tubos ruedan, la fruta rueda...) con un collider
    /// que se ajusta al modelo del producto, y se queda fija cuando se para en el suelo. La recoge el primero que pase
    /// por encima (trigger), sin pulsar nada, y se ve absorbida hacia él (<see cref="DroppedLootAbsorbFx"/>). Una burbuja
    /// translúcida (hija «Bubble») la destaca. Quien la ha soltado tarda un poco más en poder recuperarla.
    /// El SERVIDOR lo decide todo; los clientes solo ven la posición y el giro (NetworkTransform).
    ///
    /// Montaje (PF_DroppedLoot, en la Network Prefabs List): NetworkObject + NetworkTransform (autoridad del servidor) +
    /// Rigidbody + NetworkRigidbody + un collider sólido para la bolsita genérica (Solid Collider) + un collider trigger
    /// más grande (para recogerla). Los colliders de cada producto se crean solos (servidor) a partir de su modelo.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DroppedLoot : NetworkBehaviour
    {
        [Tooltip("Segundos desde que sale hasta que cualquiera puede recogerla.")]
        [SerializeField] float pickupDelay = 0.4f;
        [Tooltip("Segundos hasta que quien la ha soltado puede recuperarla (para que el rival tenga ventaja).")]
        [SerializeField] float dropperPickupDelay = 2.5f;
        [Tooltip("Segundos hasta desaparecer si nadie la recoge (0 = no desaparece).")]
        [SerializeField] float lifeTime = 20f;
        [Tooltip("Collider sólido de la bolsita genérica (variante 0). Los productos usan uno propio que se ajusta a su " +
                 "modelo. Se desactiva al quedarse fija para no estorbar a los jugadores.")]
        [SerializeField] Collider solidCollider;
        [Tooltip("Normal mínima para considerar que se apoya en el suelo (no en una pared).")]
        [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.5f;

        [Header("Físicas")]
        [Tooltip("Rozamiento con lo que toca. Más alto = las latas ruedan en vez de deslizar.")]
        [SerializeField, Range(0f, 1f)] float friction = 0.55f;
        [SerializeField, Range(0f, 1f)] float bounciness = 0.3f;
        [Tooltip("Lo que frena el giro: con poco, las latas ruedan muy lejos.")]
        [SerializeField, Min(0f)] float angularDamping = 0.9f;
        [SerializeField, Min(0f)] float linearDamping = 0.1f;
        [Tooltip("Velocidad (m/s) y giro (rad/s) por debajo de los que se considera parada.")]
        [SerializeField, Min(0f)] float restSpeed = 0.25f;
        [SerializeField, Min(0f)] float restSpin = 1.2f;
        [Tooltip("Segundos seguidos parada hasta que se queda fija.")]
        [SerializeField, Min(0f)] float restTime = 0.6f;
        [Tooltip("Producto casi redondo (medida mayor / menor por debajo de esto): esfera.")]
        [SerializeField, Min(1f)] float sphereRatio = 1.3f;

        [Header("Absorción y burbuja")]
        [Tooltip("Hija «Bubble»: la burbuja translúcida que la destaca. Se copia al absorberla.")]
        [SerializeField] Transform bubble;
        [Tooltip("Segundos que tarda en verse absorbida por quien la recoge.")]
        [SerializeField, Min(0.05f)] float absorbTime = 0.35f;

        [Header("Resbalar de los props")]
        [Tooltip("Solo se queda fija a esta altura (m) o menos sobre el suelo (los pies de quien lo soltó). " +
                 "Parada encima de una estantería o un mostrador, resbala hasta caer.")]
        [SerializeField, Min(0f)] float floorTolerance = 0.25f;
        [Tooltip("Velocidad (m/s) con la que se aparta del centro del prop en el que ha caído.")]
        [SerializeField, Min(0f)] float slideSpeed = 1.6f;
        [Tooltip("Segundos máximos resbalando: si no llega al suelo, se queda donde esté.")]
        [SerializeField, Min(0.5f)] float maxSlideTime = 5f;

        [Header("Producto")]
        [Tooltip("Modelo genérico (la bolsita): se ve con Variant 0, cuando no se sabe qué producto es.")]
        [SerializeField] Renderer genericVisual;
        [Tooltip("Hijo con el modelo de cada producto, igual que el de la tienda (los PF_Product_*). Variant 1 = el primero " +
                 "(ver LootPoint > Drop Variant); los últimos son sabores extra (ver Randomized Kinds).")]
        [SerializeField] GameObject[] variants = new GameObject[0];
        [Tooltip("Productos que en la estantería salen en varios sabores/colores (patatas, NukiProd, latas): al soltarse " +
                 "sale uno al azar entre Kind y sus Alternatives (números de variante, 1 = el primero de Variants).")]
        [SerializeField] KindAlternatives[] randomizedKinds = new KindAlternatives[0];

        [System.Serializable]
        struct KindAlternatives
        {
            public int kind;
            public int[] alternatives;
        }

        readonly NetworkVariable<byte> variant = new(0);

        /// <summary>Qué producto es (0 = genérico; 1.. = variants).</summary>
        public int Variant => variant.Value;
        public int Points => points;

        Rigidbody body;
        int points;
        float weight;
        PlayerLoot droppedBy;
        float spawnTime;
        bool settled;
        bool consumed;
        float restTimer;
        float supportY = float.PositiveInfinity;
        Collider support;
        float floorY = float.NegativeInfinity;
        GameObject shape;
        static PhysicsMaterial surface;
        static Mesh prism;

        void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>Solo servidor. Sobre una instancia recién creada: guarda el contenido, la spawnea y la lanza.</summary>
        public void ServerInitialize(int points, float weight, PlayerLoot droppedBy, Vector3 velocity, int kind = 0)
        {
            foreach (KindAlternatives group in randomizedKinds)
            {
                if (group.kind != kind || group.alternatives == null || group.alternatives.Length == 0) continue;
                int pick = Random.Range(0, group.alternatives.Length + 1);
                if (pick > 0) kind = group.alternatives[pick - 1];
                break;
            }
            variant.Value = (byte)Mathf.Clamp(kind, 0, variants.Length);
            this.points = points;
            this.weight = weight;
            this.droppedBy = droppedBy;
            spawnTime = Time.time;
            // Altura del suelo: los pies de quien lo suelta.
            if (droppedBy != null) floorY = droppedBy.transform.position.y;

            GetComponent<NetworkObject>().Spawn(true);
            body.linearVelocity = velocity;
            // Un poco de giro al salir: rueda y gira en vez de caer plana.
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }

        public override void OnNetworkSpawn()
        {
            ApplyVariant(0, variant.Value);
            variant.OnValueChanged += ApplyVariant;

            // En los clientes la física la apaga NetworkRigidbody (kinematic) y la mueve NetworkTransform con lo que
            // simula el servidor: así todos la ven en el mismo sitio. El sólido tampoco debe empujar a nadie allí.
            if (!IsServer)
            {
                if (solidCollider != null) solidCollider.enabled = false;
                return;
            }

            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            if (lifeTime > 0f) Invoke(nameof(ServerRemove), lifeTime);
        }

        public override void OnNetworkDespawn() => variant.OnValueChanged -= ApplyVariant;

        PhysicsMaterial Surface
        {
            get
            {
                if (surface == null)
                    surface = new PhysicsMaterial("DroppedLoot_Superficie")
                    {
                        dynamicFriction = friction, staticFriction = friction, bounciness = bounciness,
                        frictionCombine = PhysicsMaterialCombine.Average,
                        bounceCombine = PhysicsMaterialCombine.Average,
                    };
                return surface;
            }
        }

        // Se ve el modelo del producto (o la bolsita si no se sabe cuál es).
        void ApplyVariant(byte _, byte current)
        {
            int index = current - 1;
            bool known = index >= 0 && index < variants.Length && variants[index] != null;
            if (genericVisual != null) genericVisual.enabled = !known;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null) variants[i].SetActive(i == index);

            // Solo el servidor simula: ahí se crea el collider del producto (o vale el de la bolsita genérica).
            if (IsServer && IsSpawned && !settled)
            {
                if (solidCollider != null)
                {
                    solidCollider.enabled = !known;
                    solidCollider.sharedMaterial = Surface;
                }
                BuildShape(known ? variants[index] : null);
            }
        }

        // ---------------- Collider a medida del producto (solo servidor) ----------------

        // Caja envolvente del modelo en el espacio local de este objeto (da igual cómo esté girado ahora).
        static bool TryGetLocalBounds(Transform root, GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        void BuildShape(GameObject model)
        {
            if (shape != null) Destroy(shape);
            shape = null;
            if (model == null || !TryGetLocalBounds(transform, model, out Bounds b)) return;

            shape = new GameObject("~Shape") { layer = gameObject.layer };
            Transform t = shape.transform;
            t.SetParent(transform, false);
            t.localPosition = b.center;

            Vector3 size = b.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float longest = size[axis];
            float a = size[(axis + 1) % 3], c = size[(axis + 2) % 3];
            float shortest = Mathf.Max(1e-4f, Mathf.Min(a, c));

            Collider collider;
            if (longest / shortest <= sphereRatio)
            {
                // Casi redondo (manzana, naranja): esfera, rueda en cualquier dirección.
                var sphere = shape.AddComponent<SphereCollider>();
                sphere.radius = (size.x + size.y + size.z) / 6f;
                collider = sphere;
            }
            else if (Mathf.Abs(a - c) <= 0.25f * Mathf.Max(a, c))
            {
                // Sección redonda y más largo que ancho (lata, tubo): cilindro, rueda de lado y se aguanta de pie.
                t.localRotation = axis == 0 ? Quaternion.Euler(0f, 0f, 90f)
                    : axis == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                // El prisma mide 1 de diámetro y 1 de alto: la medida larga va en su eje Y.
                t.localScale = new Vector3(a, longest, c);
                var mesh = shape.AddComponent<MeshCollider>();
                mesh.convex = true;
                mesh.sharedMesh = Prism;
                collider = mesh;
            }
            else
            {
                var box = shape.AddComponent<BoxCollider>();
                box.size = size;
                collider = box;
            }
            collider.sharedMaterial = Surface;
        }

        // Cilindro de 1 de diámetro y 1 de alto, hecho a mano (los modelos no tienen lectura/escritura activada).
        static Mesh Prism
        {
            get
            {
                if (prism != null) return prism;
                const int sides = 16;
                var vertices = new Vector3[sides * 2];
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    float x = Mathf.Cos(angle) * 0.5f, z = Mathf.Sin(angle) * 0.5f;
                    vertices[i] = new Vector3(x, -0.5f, z);
                    vertices[sides + i] = new Vector3(x, 0.5f, z);
                }
                var triangles = new int[(sides - 2) * 6 + sides * 6];
                int n = 0;
                for (int i = 1; i < sides - 1; i++)
                {
                    triangles[n++] = 0; triangles[n++] = i + 1; triangles[n++] = i;
                    triangles[n++] = sides; triangles[n++] = sides + i; triangles[n++] = sides + i + 1;
                }
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    triangles[n++] = i; triangles[n++] = j; triangles[n++] = sides + i;
                    triangles[n++] = j; triangles[n++] = sides + j; triangles[n++] = sides + i;
                }
                prism = new Mesh { name = "DroppedLoot_Prisma", vertices = vertices, triangles = triangles };
                prism.RecalculateBounds();
                return prism;
            }
        }

        // ---------------- Física y quedarse fija (solo servidor) ----------------

        void OnCollisionEnter(Collision collision) => CheckContacts(collision);

        // Stay: también mientras descansa o resbala por encima de un prop.
        void OnCollisionStay(Collision collision) => CheckContacts(collision);

        // Apunta en qué se apoya (para saber si está en el suelo o encima de un mueble).
        void CheckContacts(Collision collision)
        {
            if (!IsServer || settled) return;
            if (collision.collider.GetComponentInParent<PlayerController>() != null) return;   // rebota en los jugadores

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (contact.normal.y < groundNormalThreshold) continue;   // paredes: rebota y sigue
                supportY = contact.point.y;
                support = collision.collider;
                return;
            }
        }

        // Física normal hasta que se para; entonces, si está en el suelo se queda fija, y si está encima de un prop
        // se aparta de su centro hasta caer por el borde (o hasta que se acaba el tiempo).
        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || settled) return;

            float age = Time.time - spawnTime;
            bool still = body.linearVelocity.sqrMagnitude < restSpeed * restSpeed &&
                         body.angularVelocity.sqrMagnitude < restSpin * restSpin;
            if (age < 0.3f || !still)
            {
                restTimer = 0f;
                if (age > maxSlideTime + 4f) Settle();   // por si algo la mantiene dando vueltas
                return;
            }

            restTimer += Time.fixedDeltaTime;
            if (restTimer < restTime) return;
            restTimer = 0f;

            bool onFloor = supportY <= floorY + floorTolerance;
            if (onFloor || age > maxSlideTime || support == null)
            {
                Settle();
                return;
            }

            // Encima de un prop: empujón hacia fuera de su centro.
            Vector3 away = transform.position - support.bounds.center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere;
            away.y = 0f;
            body.linearVelocity = away.normalized * slideSpeed + Vector3.up * 0.5f;
            supportY = float.PositiveInfinity;
            support = null;
        }

        void Settle()
        {
            settled = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            if (solidCollider != null) solidCollider.enabled = false;
            if (shape != null) Destroy(shape);
            shape = null;
        }

        // ---------------- Recoger ----------------

        // Stay (no Enter): así la recoge también quien ya estaba encima cuando pasa el retraso.
        void OnTriggerStay(Collider other)
        {
            if (!IsServer || consumed) return;

            float age = Time.time - spawnTime;
            if (age < pickupDelay) return;

            PlayerLoot loot = other.GetComponentInParent<PlayerLoot>();
            if (loot == null) return;
            if (loot == droppedBy && age < dropperPickupDelay) return;

            // Lo que no cabe en la bolsa se queda en el suelo para otro (o para cuando venda).
            int accepted = loot.ServerAddLootUpTo(points, weight, variant.Value);
            if (accepted <= 0) return;
            weight -= weight * accepted / points;
            points -= accepted;
            if (points > 0) return;

            consumed = true;
            AbsorbRpc(loot.NetworkObjectId);
            ServerRemove();
        }

        // Todos los clientes: una copia visual de la pieza vuela hacia quien la ha cogido mientras se encoge.
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void AbsorbRpc(ulong playerObjectId)
        {
            if (Application.isBatchMode) return;
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(playerObjectId, out NetworkObject player)) return;

            int index = variant.Value - 1;
            GameObject model = index >= 0 && index < variants.Length ? variants[index] : null;
            DroppedLootAbsorbFx.Play(transform, model != null ? model.transform : null, bubble, player.transform, absorbTime);
        }

        void ServerRemove()
        {
            if (IsServer && IsSpawned) NetworkObject.Despawn();
        }
    }
}
