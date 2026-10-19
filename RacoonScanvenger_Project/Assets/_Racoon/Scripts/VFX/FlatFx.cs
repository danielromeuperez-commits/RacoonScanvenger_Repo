using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racoon.VFX
{
    /// <summary>
    /// Base de los efectos que se pueden probar desde el Inspector: los métodos con <see cref="VfxTestAttribute"/>
    /// salen como botones en Play (ver VfxBehaviourEditor).
    /// </summary>
    public abstract class VfxBehaviour : MonoBehaviour { }

    /// <summary>Botón de test en el Inspector (solo en Play). El método no puede tener parámetros.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class VfxTestAttribute : Attribute
    {
        public readonly string Label;
        public VfxTestAttribute(string label) => Label = label;
    }

    /// <summary>
    /// Una pieza de efecto (SG_VFX_ToonFlat / SG_VFX_ToonFlatOverlay / SG_VFX_ToonVolume): malla + material compartido
    /// + MaterialPropertyBlock propio (colores, "revelado"...), animada por código.
    /// </summary>
    public sealed class FlatPiece
    {
        public readonly Transform Transform;
        readonly MeshRenderer renderer;
        readonly MeshFilter filter;
        readonly MaterialPropertyBlock block = new();

        public FlatPiece(string name, Transform parent, Mesh mesh, Material material, int sortingOrder = 0)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = sortingOrder; // Orden entre piezas del overlay (se pintan encima unas de otras).
            renderer.enabled = false;
            Transform = go.transform;
            Window(10f, 0f);
        }

        public bool Visible
        {
            get => renderer.enabled;
            set => renderer.enabled = value;
        }

        public Mesh Mesh
        {
            get => filter.sharedMesh;
            set => filter.sharedMesh = value;
        }

        /// <summary>Relleno (base → punta según el degradado de la malla) y contorno.</summary>
        public FlatPiece Colors(Color baseColor, Color tipColor, Color outlineColor)
        {
            block.SetColor(FlatFx.BaseColorId, baseColor);
            block.SetColor(FlatFx.TipColorId, tipColor);
            block.SetColor(FlatFx.OutlineColorId, outlineColor);
            return Apply();
        }

        /// <summary>Sombreado de dos tonos con la luz de cámara (objetos 3D). shadowTint multiplica el color.</summary>
        public FlatPiece Cel(float amount, Color shadowTint, float threshold = 0.55f)
        {
            block.SetFloat(FlatFx.CelAmountId, amount);
            block.SetColor(FlatFx.ShadowColorId, shadowTint);
            block.SetFloat(FlatFx.CelThresholdId, threshold);
            return Apply();
        }

        /// <summary>Cuánto varía el ritmo del revelado entre los rayos / esquirlas de la pieza (0 = todos a la vez).</summary>
        public FlatPiece Spread(float spread)
        {
            block.SetFloat(FlatFx.SpreadId, spread);
            return Apply();
        }

        /// <summary>Brillo especular y luz de borde (SG_VFX_ToonVolume). El alfa de cada color es su intensidad.</summary>
        public FlatPiece Shine(Color highlight, Color rim)
        {
            block.SetColor(FlatFx.HighlightColorId, highlight);
            block.SetColor(FlatFx.RimColorId, rim);
            return Apply();
        }

        /// <summary>Un valor del shader animado por código (p. ej. FlatFx.ThicknessId, FlatFx.RimCutId).</summary>
        public void Set(int id, float value)
        {
            block.SetFloat(id, value);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Parte visible de cada rayo / esquirla: de shrink a grow (0 = base, 1 = punta).</summary>
        public void Window(float grow, float shrink)
        {
            block.SetFloat(FlatFx.GrowId, grow);
            block.SetFloat(FlatFx.ShrinkId, shrink);
            renderer.SetPropertyBlock(block);
        }

        FlatPiece Apply()
        {
            renderer.SetPropertyBlock(block);
            return this;
        }
    }

    /// <summary>Utilidades comunes de los efectos planos: ids del shader, cámara y curvas de animación.</summary>
    public static class FlatFx
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        public static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        public static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        public static readonly int CelAmountId = Shader.PropertyToID("_CelAmount");
        public static readonly int CelThresholdId = Shader.PropertyToID("_CelThreshold");
        public static readonly int GrowId = Shader.PropertyToID("_Grow");
        public static readonly int ShrinkId = Shader.PropertyToID("_Shrink");
        public static readonly int SpreadId = Shader.PropertyToID("_Spread");
        // SG_VFX_ToonVolume
        public static readonly int HighlightColorId = Shader.PropertyToID("_HighlightColor");
        public static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        public static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        public static readonly int RimCutId = Shader.PropertyToID("_RimCut");

        /// <summary>Totalmente visible (por encima de 1 aunque el Spread frene a algunas piezas).</summary>
        public const float Full = 10f;

        static Camera camera;

        public static Camera Camera
        {
            get
            {
                if (camera == null || !camera.isActiveAndEnabled) camera = Camera.main;
                return camera;
            }
        }

        /// <summary>Mirando a cámara (plano XY de la pieza = pantalla), con un giro en pantalla.</summary>
        public static Quaternion Billboard(float rollDegrees = 0f)
        {
            Camera cam = Camera;
            Quaternion face = cam != null ? Quaternion.LookRotation(cam.transform.forward, cam.transform.up) : Quaternion.identity;
            return face * Quaternion.Euler(0f, 0f, rollDegrees);
        }

        // Curvas (t en 0-1).
        public static float OutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        public static float InCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t;
        }

        public static float OutBack(float t, float overshoot = 1.9f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        /// <summary>0 → 1 entre start y end (segundos).</summary>
        public static float Phase(float time, float start, float end) => Mathf.Clamp01((time - start) / Mathf.Max(end - start, 1e-4f));
    }
}
