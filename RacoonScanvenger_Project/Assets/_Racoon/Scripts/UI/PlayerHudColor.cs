using Racoon.Gameplay;
using Racoon.Player;
using Racoon.UI.HUD;
using UnityEngine;

namespace Racoon.UI
{
    /// <summary>
    /// Color de un jugador tal y como lo pinta el HUD de la partida: el de HUDPlayerColors (P1 / P2, con su tono pastel)
    /// según el puesto que le da MatchStandings. Para que los avisos junto al jugador usen los mismos colores que el HUD.
    /// </summary>
    public static class PlayerHudColor
    {
        /// <summary>false si aún no hay HUD o el jugador aún no tiene puesto (vuelve a intentarlo más tarde).</summary>
        public static bool TryGet(PlayerController player, out Color color)
        {
            color = Color.white;
            if (player == null) return false;
            var colors = Object.FindFirstObjectByType<HUDPlayerColors>();
            var standings = Object.FindFirstObjectByType<MatchStandings>();
            var loot = player.GetComponent<PlayerLoot>();
            if (colors == null || standings == null || loot == null || standings.Players == null) return false;
            // Mismo puesto que usa HUDController: el orden de la lista (P1 = el primero).
            for (int i = 0; i < standings.Players.Count; i++)
            {
                if (standings.Players[i].Loot != loot) continue;
                color = colors.Get(i + 1);
                return true;
            }
            return false;
        }
    }
}
