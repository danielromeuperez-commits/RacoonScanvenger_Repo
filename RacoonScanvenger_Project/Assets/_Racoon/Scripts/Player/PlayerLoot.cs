using System;
using System.Collections.Generic;
using Racoon.Feedback;
using Racoon.Items;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Guarda los puntos del jugador y el peso de los objetos que lleva.
    /// Los puntos pendientes solo pasan a ser definitivos cuando vende el loot.
    /// La bolsa tiene un tope de puntos sin vender (BagCapacity); el peso que frena al jugador se mide también en
    /// puntos sin vender y llega a su máximo antes de llenarla (FullWeightPoints).
    /// </summary>
    public class PlayerLoot : NetworkBehaviour
    {
        [Header("Bolsa y peso")]
        [Tooltip("Puntos sin vender que caben en la bolsa. Llena, no se puede saquear más hasta vender.")]
        [SerializeField, Min(1)] int bagCapacity = 100;
        [Tooltip("Puntos sin vender con los que el peso llega a su máximo (lo más lento y lo que más estamina gasta).")]
        [SerializeField, Min(1)] int fullWeightPoints = 80;
        [Tooltip("Segundos entre avisos de «bolsa llena» al intentar saquear (mantener Interact lo intenta seguido).")]
        [SerializeField] float bagFullNoticeInterval = 0.6f;

        [Header("Soltar loot al recibir un golpe fuerte")]
        [Tooltip("Prefab con NetworkObject + DroppedLoot (PF_DroppedLoot). Sin él no se suelta nada.")]
        [SerializeField] DroppedLoot droppedLootPrefab;
        [Tooltip("Fracción del loot sin vender (puntos y peso) que se suelta.")]
        [SerializeField, Range(0f, 1f)] float heavyHitDropFraction = 0.3f;
        [Tooltip("Máximo de bolsitas en las que se reparte lo que se suelta.")]
        [SerializeField, Min(1)] int maxDroppedPickups = 3;
        [Tooltip("Puntos mínimos por bolsita (con poco loot salen menos bolsitas).")]
        [SerializeField, Min(1)] int minPointsPerPickup = 10;
        [Tooltip("Velocidad horizontal con la que salen despedidas.")]
        [SerializeField] float dropScatterSpeed = 3.5f;
        [Tooltip("Velocidad vertical con la que salen despedidas.")]
        [SerializeField] float dropUpwardSpeed = 4f;
        [Tooltip("Apertura del abanico en el que salen (grados), centrado en la dirección del empujón.")]
        [SerializeField] float dropSpreadAngle = 120f;

        [Header("Sonido (pendiente: arrastra los clips cuando los haya)")]
        [Tooltip("SFX al SAQUEAR un objeto de una estantería: «tink» de objeto cogido / bolsa que se llena. " +
                 "Lo oyen todos, sale de la estantería. Al mantener Interact suena seguido: que sea corto.")]
        [SerializeField] SfxCue lootGrabSfx = new();
        [Tooltip("SFX al saquear durante el EVENTO x2: versión «dorada» del anterior (monedas, brillo).")]
        [SerializeField] SfxCue lootGrabDoubleSfx = new();

        // Puntos que el jugador ya ha vendido y tiene asegurados.
        readonly NetworkVariable<int> bankedPoints = new(0);

        // Puntos de los objetos que lleva actualmente en la bolsa.
        readonly NetworkVariable<int> pendingPoints = new(0);

        // Peso actual que lleva el jugador.
        readonly NetworkVariable<float> carriedWeight = new(0f);

        public event Action LootChanged;
        /// <summary>Solo en el dueño. Ha intentado saquear con la bolsa llena.</summary>
        public event Action BagFullRejected;

        /// <summary>
        /// En todos los clientes: alguien ha saqueado una estantería (quién, si era x2, dónde y cuántos puntos).
        /// Lo usan los visuales de los eventos (p. ej. el «+20» del x2).
        /// </summary>
        public static event Action<PlayerLoot, bool, Vector3, int> AnyLootGrabbed;

        public int BankedPoints => bankedPoints.Value;
        public int PendingPoints => pendingPoints.Value;
        public float CarriedWeight => carriedWeight.Value;
        public int BagCapacity => bagCapacity;
        public int FullWeightPoints => fullWeightPoints;
        /// <summary>Cuánto cabe aún en la bolsa (puntos sin vender).</summary>
        public int BagSpace => Mathf.Max(0, bagCapacity - pendingPoints.Value);
        public bool IsBagFull => pendingPoints.Value >= bagCapacity;
        /// <summary>0 = bolsa vacía, 1 = llena (BagCapacity).</summary>
        public float BagFillNormalized => Mathf.Clamp01((float)pendingPoints.Value / bagCapacity);

        /// <summary>
        /// Puntos totales que tendría si vendiera ahora mismo.
        /// </summary>
        public int PotentialPoints =>
            bankedPoints.Value + pendingPoints.Value;

        /// <summary>
        /// 0 = sin peso (bolsa vacía).
        /// 1 = peso máximo (FullWeightPoints puntos sin vender o más).
        /// </summary>
        public float WeightNormalized => Mathf.Clamp01((float)pendingPoints.Value / fullWeightPoints);

        [Header("Debug")]
        [SerializeField] int debugBankedPoints;
        [SerializeField] int debugPendingPoints;
        [SerializeField] float debugCurrentWeight;

        MatchHUD hud;
        float nextBagFullNotice;

        // Solo servidor: qué productos lleva en la bolsa (de qué estantería y cuánto vale cada uno), para soltar
        // esos mismos productos al recibir un golpe. kind = DroppedLoot.Variant (0 = genérico, sin producto).
        struct BagEntry
        {
            public int kind;
            public int points;
        }
        readonly List<BagEntry> bag = new();

        public override void OnNetworkSpawn()
        {
            bankedPoints.OnValueChanged += OnBankedPointsChanged;
            pendingPoints.OnValueChanged += OnPendingPointsChanged;
            carriedWeight.OnValueChanged += OnWeightChanged;

            hud = FindFirstObjectByType<MatchHUD>();

            if (hud != null)
                hud.RegisterPlayer(this);

            LootChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            bankedPoints.OnValueChanged -= OnBankedPointsChanged;
            pendingPoints.OnValueChanged -= OnPendingPointsChanged;
            carriedWeight.OnValueChanged -= OnWeightChanged;

            if (hud != null)
                hud.UnregisterPlayer(this);
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            debugBankedPoints = bankedPoints.Value;
            debugPendingPoints = pendingPoints.Value;
            debugCurrentWeight = carriedWeight.Value;
        }

        void OnBankedPointsChanged(int _, int __)
        {
            LootChanged?.Invoke();
        }

        void OnPendingPointsChanged(int _, int __)
        {
            LootChanged?.Invoke();
        }

        void OnWeightChanged(float _, float __)
        {
            LootChanged?.Invoke();
        }

        /// <summary>
        /// Solo servidor. Añade un objeto a la bolsa.
        /// Sus puntos quedan pendientes hasta venderlo.
        /// Si no cabe entero entra lo que quepa; con la bolsa llena no entra nada (false) y se avisa al dueño.
        /// </summary>
        public bool ServerAddLoot(int points, float weight) => ServerAddLoot(points, weight, 0);

        /// <summary>Igual, apuntando qué producto es (<paramref name="kind"/> = DroppedLoot.Variant) para poder soltarlo luego.</summary>
        public bool ServerAddLoot(int points, float weight, int kind) =>
            ServerAddLootUpTo(points, weight, kind) > 0 || points <= 0 && weight > 0f;

        /// <summary>
        /// Solo servidor. Como ServerAddLoot, pero devuelve los puntos que han entrado (lo que no cabe se queda fuera:
        /// una bolsita del suelo puede quedarse con el resto). El peso entra en la misma proporción.
        /// </summary>
        public int ServerAddLootUpTo(int points, float weight) => ServerAddLootUpTo(points, weight, 0);

        /// <summary>Igual, apuntando qué producto es (<paramref name="kind"/> = DroppedLoot.Variant).</summary>
        public int ServerAddLootUpTo(int points, float weight, int kind)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerAddLoot solo se puede llamar en el servidor.", this);
                return 0;
            }

            points = Mathf.Max(0, points);
            weight = Mathf.Max(0f, weight);
            if (points <= 0)
            {
                carriedWeight.Value += weight;
                return 0;
            }

            int accepted = Mathf.Min(points, BagSpace);
            if (accepted <= 0)
            {
                ServerNotifyBagFull();
                return 0;
            }

            pendingPoints.Value += accepted;
            carriedWeight.Value += weight * accepted / points;
            bag.Add(new BagEntry { kind = kind, points = accepted });
            return accepted;
        }

        void ServerNotifyBagFull()
        {
            if (Time.time < nextBagFullNotice) return;
            nextBagFullNotice = Time.time + bagFullNoticeInterval;
            BagFullRpc();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void BagFullRpc()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(24);
            }

            BagFullRejected?.Invoke();
        }

        /// <summary>
        /// Solo servidor. Sonido de haber saqueado un objeto en <paramref name="sourcePosition"/> (la estantería).
        /// Lo oyen todos los clientes.
        /// </summary>
        public void ServerNotifyLootGrabbed(bool doubled, Vector3 sourcePosition, int points = 0)
        {
            if (!IsServer) return;

            LootGrabbedRpc(doubled, sourcePosition, points);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void LootGrabbedRpc(bool doubled, Vector3 sourcePosition, int points)
        {
            (doubled && !lootGrabDoubleSfx.IsEmpty ? lootGrabDoubleSfx : lootGrabSfx).Play(sourcePosition);
            AnyLootGrabbed?.Invoke(this, doubled, sourcePosition, points);
        }

        /// <summary>
        /// Solo servidor. Vende todo el contenido de la bolsa.
        /// Los puntos pendientes pasan a estar asegurados
        /// y el peso vuelve a cero.
        /// </summary>
        public bool ServerSellLoot()
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerSellLoot solo se puede llamar en el servidor.", this);
                return false;
            }

            if (pendingPoints.Value <= 0)
                return false;

            bankedPoints.Value += pendingPoints.Value;
            pendingPoints.Value = 0;
            carriedWeight.Value = 0f;
            bag.Clear();

            // Se entrega también el objeto mecánico que llevara (extintor, despertador): deja de sonar y se va.
            if (TryGetComponent(out PlayerController controller))
                controller.ServerSetCarriedMech(MechKind.None);

            return true;
        }

        /// <summary>
        /// Solo servidor. Suelta parte del loot sin vender (heavyHitDropFraction) en varias bolsitas
        /// que salen despedidas hacia <paramref name="direction"/> (el sentido del empujón).
        /// Devuelve los puntos soltados.
        /// </summary>
        public int ServerDropLoot(Vector3 direction) => ServerDropLoot(direction, heavyHitDropFraction);

        /// <summary>Igual, soltando <paramref name="fraction"/> del loot sin vender (p. ej. al resbalar en un plátano).</summary>
        public int ServerDropLoot(Vector3 direction, float fraction)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerDropLoot solo se puede llamar en el servidor.", this);
                return 0;
            }

            if (droppedLootPrefab == null || fraction <= 0f || pendingPoints.Value <= 0)
                return 0;

            int target = Mathf.Clamp(Mathf.RoundToInt(pendingPoints.Value * fraction), 1, pendingPoints.Value);
            List<BagEntry> dropped = ServerTakeFromBag(target);
            int points = 0;
            foreach (BagEntry entry in dropped) points += entry.points;
            if (points <= 0) return 0;

            // El peso sale en la misma proporción que los puntos.
            float weight = carriedWeight.Value * points / pendingPoints.Value;

            pendingPoints.Value -= points;
            carriedWeight.Value = Mathf.Max(0f, carriedWeight.Value - weight);

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
            direction.Normalize();

            // Un producto por pieza: cada una vale lo de su estantería (una manzana, más que una lata).
            int pickups = dropped.Count;
            for (int i = 0; i < pickups; i++)
            {
                int pickupPoints = dropped[i].points;

                // En abanico alrededor de la dirección del empujón.
                float angle = pickups > 1 ? Mathf.Lerp(-0.5f, 0.5f, i / (pickups - 1f)) * dropSpreadAngle : 0f;
                angle += UnityEngine.Random.Range(-10f, 10f);
                Vector3 scatter = Quaternion.AngleAxis(angle, Vector3.up) * direction;

                DroppedLoot pickup = Instantiate(
                    droppedLootPrefab,
                    transform.position + Vector3.up * 1f + scatter * 0.3f,
                    Quaternion.LookRotation(scatter));
                pickup.ServerInitialize(pickupPoints, weight * pickupPoints / points, this,
                    scatter * dropScatterSpeed + Vector3.up * dropUpwardSpeed, dropped[i].kind);
            }

            return points;
        }

        // Saca de la bolsa productos enteros al azar hasta unos «target» puntos (como mucho maxDroppedPickups piezas).
        // Si no cabe ninguno entero, parte uno. Lo que no estaba apuntado cuenta como genérico.
        List<BagEntry> ServerTakeFromBag(int target)
        {
            SyncBagWithPending();
            var taken = new List<BagEntry>();
            int sum = 0;

            // Orden al azar.
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            for (int i = bag.Count - 1; i >= 0 && sum < target && taken.Count < maxDroppedPickups; i--)
            {
                BagEntry entry = bag[i];
                // Se acepta pasarse un poco (media pieza), no más.
                if (sum + entry.points - target > entry.points / 2) continue;
                taken.Add(entry);
                sum += entry.points;
                bag.RemoveAt(i);
            }

            if (taken.Count == 0 && bag.Count > 0)
            {
                // Ninguno entra entero: se parte el más pequeño.
                int smallest = 0;
                for (int i = 1; i < bag.Count; i++)
                    if (bag[i].points < bag[smallest].points) smallest = i;
                BagEntry entry = bag[smallest];
                int part = Mathf.Clamp(target, 1, entry.points);
                taken.Add(new BagEntry { kind = entry.kind, points = part });
                entry.points -= part;
                if (entry.points > 0) bag[smallest] = entry;
                else bag.RemoveAt(smallest);
            }

            return taken;
        }

        // La lista y los puntos de la bolsa tienen que cuadrar (por si algo los ha tocado sin pasar por aquí).
        void SyncBagWithPending()
        {
            int sum = 0;
            foreach (BagEntry entry in bag) sum += entry.points;
            int pending = pendingPoints.Value;
            if (sum < pending) bag.Add(new BagEntry { kind = 0, points = pending - sum });
            for (int i = bag.Count - 1; i >= 0 && sum > pending; i--)
            {
                BagEntry entry = bag[i];
                int cut = Mathf.Min(entry.points, sum - pending);
                entry.points -= cut;
                sum -= cut;
                if (entry.points > 0) bag[i] = entry;
                else bag.RemoveAt(i);
            }
        }

        // ---------------- Resultado de partida ----------------

        /// <summary>
        /// Solo servidor. Envía al dueño de este jugador
        /// si ha ganado o perdido la partida.
        /// </summary>
        public void ServerSendMatchResult(bool won)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerSendMatchResult solo se puede llamar en el servidor.", this);
                return;
            }

            MatchResultRpc(won);
        }

        /// <summary>
        /// Se ejecuta únicamente en el cliente dueño de este jugador.
        /// Por ahora solo muestra el resultado en la consola.
        /// </summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void MatchResultRpc(bool won)
        {
            if (won)
                Debug.Log("HAS GANADO");
            else
                Debug.Log("HAS PERDIDO");
        }

        /// <summary>
        /// Solo servidor. Envía al dueño de este jugador
        /// el resultado de empate.
        /// </summary>
        public void ServerSendDrawResult()
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerSendDrawResult solo se puede llamar en el servidor.", this);
                return;
            }

            DrawResultRpc();
        }

        /// <summary>
        /// Se ejecuta únicamente en el cliente dueño de este jugador.
        /// </summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void DrawResultRpc()
        {
            Debug.Log("EMPATE");
        }
    }
}