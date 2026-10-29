using System;
using Racoon.Player;
using Unity.Netcode;
using UnityEngine;

namespace Racoon.Items
{
    /// <summary>
    /// Bebida energética: durante unos segundos la estamina es infinita (correr, dash y puñetazos no gastan) y el
    /// dash no tiene cooldown. No se puede tomar otra hasta que se acabe la primera: se queda en la mano y el aviso
    /// lo da PlayerStatusUI. Los efectos de cámara los pone PlayerCameraFX (al empezar, mientras dura y el bajón al acabar).
    /// </summary>
    [Serializable]
    public class EnergyDrinkEffect : ItemEffect
    {
        [Tooltip("Segundos que dura el efecto.")]
        [SerializeField, Min(0.1f)] float duration = 8f;

        public override bool CanUse(PlayerController user) => user != null && !user.IsEnergyDrinkActive;

        public override void ServerUse(PlayerController user, ItemData item)
        {
            if (!NetworkManager.Singleton.IsServer) return;
            if (user == null) return;
            user.ServerApplyEnergyDrink(duration);
        }
    }
}
