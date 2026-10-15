using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Pieza del HUD que se tiñe con el color de un jugador (capas «[TINTE Pn]»: blancas con alfa).
    /// Si <see cref="shadow"/> está asignado, tiñe esa sombra en lugar del gráfico (doble sombra de las fichas).
    /// Los colores los aplica <see cref="HUDPlayerColors"/>.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class HUDTint : MonoBehaviour
    {
        [Range(1, 2)] public int player = 1;

        [Tooltip("Opcional: tiñe esta sombra en vez del color del gráfico.")]
        public Shadow shadow;

        public void Apply(Color color)
        {
            if (shadow != null)
                shadow.effectColor = color;
            else if (TryGetComponent(out Graphic graphic))
                graphic.color = color;
        }
    }
}
