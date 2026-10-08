using System;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Guarda los puntos de loot y el peso que lleva actualmente el jugador.
    /// El servidor es quien modifica estos valores.
    /// </summary>
    public class PlayerLoot : NetworkBehaviour
    {
        [Header("Peso")]
        [Tooltip("Peso que consideraremos una bolsa completamente llena visualmente.")]
        [SerializeField, Min(0.1f)] float bagFullWeight = 20f;

        readonly NetworkVariable<int> lootPoints = new(0);
        readonly NetworkVariable<float> carriedWeight = new(0f);

        public event Action LootChanged;

        public int LootPoints => lootPoints.Value;
        public float CarriedWeight => carriedWeight.Value;
        public float BagFullWeight => bagFullWeight;

        /// <summary>
        /// 0 = bolsa vacía.
        /// 1 = bolsa visualmente llena.
        /// Más adelante lo utilizaremos para cambiar el tamaño de la bolsa.
        /// </summary>
        public float WeightNormalized =>
            bagFullWeight > 0f
                ? Mathf.Clamp01(carriedWeight.Value / bagFullWeight)
                : 0f;

        MatchHUD hud;

        public override void OnNetworkSpawn()
        {
            lootPoints.OnValueChanged += OnLootPointsChanged;
            carriedWeight.OnValueChanged += OnWeightChanged;

            hud = FindFirstObjectByType<MatchHUD>();

            if (hud != null)
                hud.RegisterPlayer(this);

            LootChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            lootPoints.OnValueChanged -= OnLootPointsChanged;
            carriedWeight.OnValueChanged -= OnWeightChanged;

            if (hud != null)
                hud.UnregisterPlayer(this);
        }

        void OnLootPointsChanged(int _, int __)
        {
            LootChanged?.Invoke();
        }

        void OnWeightChanged(float _, float __)
        {
            LootChanged?.Invoke();
        }

        /// <summary>
        /// Solo servidor. Añade puntos y peso al jugador.
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

            lootPoints.Value += Mathf.Max(0, points);
            carriedWeight.Value += Mathf.Max(0f, weight);

            return true;
        }
    }
}