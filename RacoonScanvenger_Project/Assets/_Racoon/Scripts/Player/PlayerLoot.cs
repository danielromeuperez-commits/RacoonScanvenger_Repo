using System;
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
    }
}