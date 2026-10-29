using System;
using Racoon.Feedback;
using Racoon.Items;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Guarda los puntos del jugador y el peso de los objetos que lleva.
    /// Los puntos pendientes solo pasan a ser definitivos cuando vende el loot.
    /// </summary>
    public class PlayerLoot : NetworkBehaviour
    {
        [Header("Peso")]
        [Tooltip("Peso que consideraremos una bolsa completamente llena visualmente.")]
        [SerializeField, Min(0.1f)] float bagFullWeight = 20f;

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

        public int BankedPoints => bankedPoints.Value;
        public int PendingPoints => pendingPoints.Value;
        public float CarriedWeight => carriedWeight.Value;
        public float BagFullWeight => bagFullWeight;

        /// <summary>
        /// Puntos totales que tendría si vendiera ahora mismo.
        /// </summary>
        public int PotentialPoints =>
            bankedPoints.Value + pendingPoints.Value;

        /// <summary>
        /// 0 = bolsa vacía.
        /// 1 = bolsa visualmente llena.
        /// </summary>
        public float WeightNormalized =>
            bagFullWeight > 0f
                ? Mathf.Clamp01(carriedWeight.Value / bagFullWeight)
                : 0f;

        [Header("Debug")]
        [SerializeField] int debugBankedPoints;
        [SerializeField] int debugPendingPoints;
        [SerializeField] float debugCurrentWeight;

        MatchHUD hud;

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
        /// </summary>
        public bool ServerAddLoot(int points, float weight)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerAddLoot solo se puede llamar en el servidor.", this);
                return false;
            }

            if (points <= 0 && weight <= 0f)
                return false;

            pendingPoints.Value += Mathf.Max(0, points);
            carriedWeight.Value += Mathf.Max(0f, weight);

            return true;
        }

        /// <summary>
        /// Solo servidor. Sonido de haber saqueado un objeto en <paramref name="sourcePosition"/> (la estantería).
        /// Lo oyen todos los clientes.
        /// </summary>
        public void ServerNotifyLootGrabbed(bool doubled, Vector3 sourcePosition)
        {
            if (!IsServer) return;

            LootGrabbedRpc(doubled, sourcePosition);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        void LootGrabbedRpc(bool doubled, Vector3 sourcePosition)
        {
            (doubled && !lootGrabDoubleSfx.IsEmpty ? lootGrabDoubleSfx : lootGrabSfx).Play(sourcePosition);
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

            return true;
        }

        /// <summary>
        /// Solo servidor. Suelta parte del loot sin vender (heavyHitDropFraction) en varias bolsitas
        /// que salen despedidas hacia <paramref name="direction"/> (el sentido del empujón).
        /// Devuelve los puntos soltados.
        /// </summary>
        public int ServerDropLoot(Vector3 direction)
        {
            if (!IsServer)
            {
                Debug.LogWarning("ServerDropLoot solo se puede llamar en el servidor.", this);
                return 0;
            }

            if (droppedLootPrefab == null || heavyHitDropFraction <= 0f || pendingPoints.Value <= 0)
                return 0;

            int points = Mathf.Clamp(Mathf.RoundToInt(pendingPoints.Value * heavyHitDropFraction), 1, pendingPoints.Value);
            // El peso sale en la misma proporción que los puntos.
            float weight = carriedWeight.Value * points / pendingPoints.Value;

            pendingPoints.Value -= points;
            carriedWeight.Value = Mathf.Max(0f, carriedWeight.Value - weight);

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -transform.forward;
            direction.Normalize();

            int pickups = Mathf.Clamp(points / minPointsPerPickup, 1, maxDroppedPickups);
            int pointsLeft = points;
            for (int i = 0; i < pickups; i++)
            {
                // La última se lleva el resto del redondeo.
                int pickupPoints = i == pickups - 1 ? pointsLeft : points / pickups;
                pointsLeft -= pickupPoints;

                // En abanico alrededor de la dirección del empujón.
                float angle = pickups > 1 ? Mathf.Lerp(-0.5f, 0.5f, i / (pickups - 1f)) * dropSpreadAngle : 0f;
                angle += UnityEngine.Random.Range(-10f, 10f);
                Vector3 scatter = Quaternion.AngleAxis(angle, Vector3.up) * direction;

                DroppedLoot pickup = Instantiate(
                    droppedLootPrefab,
                    transform.position + Vector3.up * 1f + scatter * 0.3f,
                    Quaternion.LookRotation(scatter));
                pickup.ServerInitialize(pickupPoints, weight / pickups, this,
                    scatter * dropScatterSpeed + Vector3.up * dropUpwardSpeed);
            }

            return points;
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