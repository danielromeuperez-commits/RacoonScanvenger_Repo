using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Bebida energética: durante unos segundos el dash no tiene cooldown ni gasta estamina.
    /// Los efectos de cámara los pone PlayerCameraFX (al empezar, mientras dura y el bajón al acabar).
    /// </summary>
    [Serializable]
    public class EnergyDrinkEffect : ItemEffect
    {
        [Tooltip("Segundos que dura el efecto.")]
        [SerializeField, Min(0.1f)] float duration = 6f;

        public override void ServerUse(PlayerController user, ItemData item)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (user == null) return;
            user.ServerApplyEnergyDrink(duration);
        }
    }
}
