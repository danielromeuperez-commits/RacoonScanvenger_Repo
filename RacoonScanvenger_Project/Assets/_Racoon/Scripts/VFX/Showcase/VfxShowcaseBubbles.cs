using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Showcase: «pompa de visión» (OcclusionBubbles + RacoonBubble.hlsl) sin partida en red. OcclusionBubbles solo
    /// abre pompas a los PlayerController en red; aquí se le pasan al shader las posiciones de los actores, después
    /// de que OcclusionBubbles escriba las suyas (vacías) en el frame. Él sigue preparando los renderers (bit de
    /// rendering layer y copia Lit con pompa); a los actores se les quita el bit para que no se recorten a sí mismos.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class VfxShowcaseBubbles : MonoBehaviour
    {
        const uint BubbleRenderingLayer = 1u << 30;     // igual que RACOON_BUBBLE_LAYER en el shader
        static readonly int BubblesId = Shader.PropertyToID("_RacoonBubbles");
        static readonly int ParamsId = Shader.PropertyToID("_RacoonBubbleParams");

        [SerializeField] VfxShowcaseActor[] actors;

        OcclusionBubbleSettings settings;
        readonly Vector4[] bubbles = new Vector4[4];
        float nextClean;

        void Awake()
        {
            settings = Resources.Load<OcclusionBubbleSettings>("OcclusionBubbleSettings");
            if (settings == null) settings = ScriptableObject.CreateInstance<OcclusionBubbleSettings>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextClean)
            {
                nextClean = Time.unscaledTime + 0.5f;
                foreach (VfxShowcaseActor actor in actors)
                    if (actor != null)
                        foreach (Renderer r in actor.GetComponentsInChildren<Renderer>(true))
                            r.renderingLayerMask &= ~BubbleRenderingLayer;
            }

            int count = 0;
            foreach (VfxShowcaseActor actor in actors)
            {
                if (actor == null || !actor.isActiveAndEnabled || count >= bubbles.Length) continue;
                Vector3 feet = actor.transform.position;
                bubbles[count++] = new Vector4(feet.x, feet.y + settings.chestHeight, feet.z, feet.y);
            }
            Shader.SetGlobalVectorArray(BubblesId, bubbles);
            Shader.SetGlobalVector(ParamsId, new Vector4(count, settings.radius, settings.edge, settings.frontMargin));
        }

        void OnDisable() => Shader.SetGlobalVector(ParamsId, Vector4.zero);
    }
}
