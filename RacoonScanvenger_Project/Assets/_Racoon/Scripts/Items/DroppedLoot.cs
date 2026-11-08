using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Producto que suelta un jugador al recibir un golpe fuerte (PlayerLoot.ServerDropLoot): uno de los que llevaba en
    /// la bolsa (manzana, naranja, pan, patatas, NukiProd, lata), con el modelo y el valor de su estantería.
    /// Sale despedida con física, se queda quieta al tocar el suelo y la recoge el primero que pase por
    /// encima (trigger), sin pulsar nada. Quien la ha soltado tarda un poco más en poder recuperarla.
    /// El SERVIDOR lo decide todo; los clientes solo ven la posición (NetworkTransform).
    ///
    /// Montaje (PF_DroppedLoot, en la Network Prefabs List): NetworkObject + NetworkTransform (autoridad del servidor) +
    /// Rigidbody + NetworkRigidbody + un collider sólido (Solid Collider, para rebotar en el suelo) + un collider trigger
    /// más grande (para recogerla).
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
        [Tooltip("Collider sólido: se desactiva al quedarse en el suelo para no estorbar a los jugadores.")]
        [SerializeField] Collider solidCollider;
        [Tooltip("Normal mínima para considerar que ha tocado el suelo (no una pared).")]
        [SerializeField, Range(0f, 1f)] float groundNormalThreshold = 0.5f;

        [Header("Resbalar de los props")]
        [Tooltip("Solo se queda quieto a esta altura (m) o menos sobre el suelo (los pies de quien lo soltó). " +
                 "Encima de una estantería o un mostrador resbala hasta caer.")]
        [SerializeField, Min(0f)] float floorTolerance = 0.25f;
        [Tooltip("Velocidad (m/s) con la que se aparta del centro del prop en el que ha caído.")]
        [SerializeField, Min(0f)] float slideSpeed = 1.6f;
        [Tooltip("Segundos máximos resbalando: si no llega al suelo, se queda donde esté.")]
        [SerializeField, Min(0.5f)] float maxSlideTime = 5f;

        [Header("Producto")]
        [Tooltip("Modelo genérico (la bolsita): se ve con Variant 0, cuando no se sabe qué producto es.")]
        [SerializeField] Renderer genericVisual;
        [Tooltip("Hijo con el modelo de cada producto. Variant 1 = el primero (ver LootPoint > Drop Variant).")]
        [SerializeField] GameObject[] variants = new GameObject[0];

        readonly NetworkVariable<byte> variant = new(0);

        /// <summary>Qué producto es (0 = genérico; 1.. = variants).</summary>
        public int Variant => variant.Value;
        public int Points => points;

        Rigidbody body;
        int points;
        float weight;
        PlayerLoot droppedBy;
        float spawnTime;
        bool landed;
        bool consumed;
        float floorY = float.NegativeInfinity;
        static PhysicsMaterial slippery;

        void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>Solo servidor. Sobre una instancia recién creada: guarda el contenido, la spawnea y la lanza.</summary>
        public void ServerInitialize(int points, float weight, PlayerLoot droppedBy, Vector3 velocity, int kind = 0)
        {
            variant.Value = (byte)Mathf.Clamp(kind, 0, variants.Length);
            this.points = points;
            this.weight = weight;
            this.droppedBy = droppedBy;
            spawnTime = Time.time;
            // Altura del suelo: los pies de quien lo suelta.
            if (droppedBy != null) floorY = droppedBy.transform.position.y;
            // Casi sin rozamiento: resbala de los bordes en vez de quedarse enganchado.
            if (solidCollider != null)
            {
                if (slippery == null)
                    slippery = new PhysicsMaterial("DroppedLoot_Resbala")
                    {
                        dynamicFriction = 0.05f, staticFriction = 0.05f, bounciness = 0.15f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                    };
                solidCollider.sharedMaterial = slippery;
            }

            GetComponent<NetworkObject>().Spawn(true);
            body.linearVelocity = velocity;
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

            if (lifeTime > 0f) Invoke(nameof(ServerRemove), lifeTime);
        }

        public override void OnNetworkDespawn() => variant.OnValueChanged -= ApplyVariant;

        // Se ve el modelo del producto (o la bolsita si no se sabe cuál es).
        void ApplyVariant(byte _, byte current)
        {
            int index = current - 1;
            bool known = index >= 0 && index < variants.Length && variants[index] != null;
            if (genericVisual != null) genericVisual.enabled = !known;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null) variants[i].SetActive(i == index);
        }

        void OnCollisionEnter(Collision collision) => CheckContacts(collision);

        // Stay: también mientras resbala por encima de un prop (pocas bolsitas y solo hasta que se paran: barato).
        void OnCollisionStay(Collision collision) => CheckContacts(collision);

        void CheckContacts(Collision collision)
        {
            if (!IsServer || landed) return;
            if (collision.collider.GetComponentInParent<PlayerController>() != null) return;   // rebota en los jugadores

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                // Paredes: rebota y sigue.
                if (contact.normal.y < groundNormalThreshold) continue;

                // En el suelo (o se le acabó el tiempo): se queda quieta.
                bool onFloor = contact.point.y <= floorY + floorTolerance;
                if (onFloor || Time.time - spawnTime > maxSlideTime)
                {
                    Land();
                    return;
                }

                // Encima de un prop: se aparta de su centro hasta caer por el borde.
                Vector3 away = transform.position - collision.collider.bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere;
                away.y = 0f;
                Vector3 velocity = body.linearVelocity;
                Vector3 push = away.normalized * slideSpeed;
                if (new Vector2(velocity.x, velocity.z).sqrMagnitude < slideSpeed * slideSpeed)
                    body.linearVelocity = new Vector3(push.x, velocity.y, push.z);
                return;
            }
        }

        void Land()
        {
            landed = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            if (solidCollider != null) solidCollider.enabled = false;
        }

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
            ServerRemove();
        }

        void ServerRemove()
        {
            if (IsServer && IsSpawned) NetworkObject.Despawn();
        }
    }
}
