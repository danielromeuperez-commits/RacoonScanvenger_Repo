using UnityEngine;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Color de cada jugador en el HUD. Lo aplica a todas las piezas con <see cref="HUDTint"/>.
    /// Por defecto, los windTint de PF_PlayerRacoon (P1) y PF_PlayerRedPanda (P2).
    /// </summary>
    [ExecuteAlways]
    public class HUDPlayerColors : MonoBehaviour
    {
        public Color player1 = new Color32(0x39, 0x5C, 0xF5, 0xFF);
        public Color player2 = new Color32(0xF5, 0x3F, 0x39, 0xFF);
        [Tooltip("Aclara el color de los jugadores hacia el blanco (0 = tal cual): tonos pastel sin cambiar los de los personajes.")]
        [Range(0f, 1f)] public float pastel;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public Color Get(int player) => Color.Lerp(player == 1 ? player1 : player2, Color.white, pastel);

        public void SetPlayerColor(int player, Color color)
        {
            if (player == 1) player1 = color;
            else player2 = color;
            Apply();
        }

        public void Apply()
        {
            foreach (var tint in GetComponentsInChildren<HUDTint>(true))
                tint.Apply(Get(tint.player));
        }
    }
}
