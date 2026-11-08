using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Ajustes de la «pompa de visión» (OcclusionBubbles). El asset está en
    /// Assets/_Racoon/Settings/Resources/OcclusionBubbleSettings: ahí se eligen las capas que se recortan.
    /// </summary>
    [CreateAssetMenu(menuName = "Racoon/Pompa de visión (ajustes)", fileName = "OcclusionBubbleSettings")]
    public class OcclusionBubbleSettings : ScriptableObject
    {
        [Tooltip("Capas de los objetos que se recortan al tapar a un jugador. Lo que no esté aquí (los jugadores, la UI...) " +
                 "no se recorta nunca. Sirve para materiales FlatKit Stylized Surface y URP Lit (a estos se les pone la " +
                 "copia SH_Racoon_LitPompa al empezar, se ven igual).")]
        public LayerMask includedLayers = 1;

        [Header("Forma")]
        [Tooltip("Radio de la pompa (m, a la distancia del jugador).")]
        [Min(0.1f)] public float radius = 0.95f;
        [Tooltip("Borde suave (m).")]
        [Min(0.01f)] public float edge = 0.35f;
        [Tooltip("Solo recorta lo que está al menos así de delante del jugador (m).")]
        [Min(0f)] public float frontMargin = 0.45f;
        [Tooltip("Centro de la pompa sobre los pies del jugador (m).")]
        public float chestHeight = 0.45f;

        [Header("Objetos")]
        [Tooltip("Cada cuántos segundos se buscan objetos nuevos (los que aparecen en partida). 0 = solo al cargar la escena.")]
        [Min(0f)] public float rescanInterval = 3f;
        [Tooltip("SH_Racoon_LitPompa: la copia de URP Lit con la pompa (no cambiar).")]
        public Shader litBubbleShader;
    }
}
