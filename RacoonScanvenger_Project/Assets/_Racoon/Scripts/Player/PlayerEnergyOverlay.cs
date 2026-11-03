using System.Collections.Generic;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racoon.Player
{
    /// <summary>
    /// Capa de energía sobre la malla del jugador que lleva la bebida energética: la ven todos los jugadores (lee lo
    /// que replica el <see cref="PlayerController"/>). Cada malla animada se copia con el material de energía, así
    /// cubre todas sus partes sin tocar los materiales del personaje. Entra suave y, al acabarse, parpadea igual que
    /// los efectos de cámara (<see cref="Blink"/>).
    /// No se monta a mano: se añade sola a cada jugador al aparecer.
    /// </summary>
    public sealed class PlayerEnergyOverlay : VfxBehaviour
    {
        /// <summary>Segundos finales en los que todo lo de la bebida parpadea.</summary>
        public const float BlinkWindow = 2f;
        const float BlinkRate = 6f;
        const float FadeTime = 0.5f;
        const float FadeInTime = 0.25f;

        const string ShaderPath = "CameraFX/CameraFXEnergyOverlay";
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int FXTimeId = Shader.PropertyToID("_FXTime");

        readonly List<(SkinnedMeshRenderer source, SkinnedMeshRenderer copy)> copies = new();
        PlayerController controller;
        Material material;
        float level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            PlayerController.AnySpawned -= Attach;
            PlayerController.AnySpawned += Attach;
        }

        static void Attach(PlayerController player)
        {
            if (player == null || player.GetComponentInChildren<PlayerEnergyOverlay>(true) != null) return;
            var go = new GameObject("EnergyOverlay");
            go.transform.SetParent(player.transform, false);
            go.AddComponent<PlayerEnergyOverlay>().controller = player;
        }

        /// <summary>
        /// Multiplicador de los efectos de la bebida según lo que le queda: 1 casi todo el tiempo y, en la ventana
        /// final, un parpadeo ligero que se apaga hasta 0.
        /// </summary>
        public static float Blink(float remaining)
        {
            if (remaining >= BlinkWindow) return 1f;
            float t = 1f - Mathf.Clamp01(remaining / BlinkWindow);
            float wave = 0.5f + 0.5f * Mathf.Sin(remaining * BlinkRate * Mathf.PI * 2f);
            return (1f - Mathf.Lerp(0.15f, 0.5f, t) * wave) * Mathf.Clamp01(remaining / FadeTime);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        void Update()
        {
            if (controller == null || !controller.IsSpawned) return;

            bool visible = controller.EnergyDrinkVisible;
            if (visible && copies.Count == 0) Build();
            level = visible ? Mathf.MoveTowards(level, 1f, Time.deltaTime / FadeInTime) : 0f;
            float intensity = level * (visible ? Blink(controller.EnergyDrinkRemaining) : 0f);

            bool show = intensity > 0.001f;
            foreach (var (source, copy) in copies)
                if (copy != null) copy.enabled = show && source != null && source.enabled;
            if (!show || material == null) return;

            material.SetFloat(IntensityId, intensity);
            material.SetFloat(FXTimeId, Time.time);
            material.SetVector(OriginId, controller.transform.position);
        }

        // Se monta la primera vez que hace falta: el modelo ya está puesto y quien no beba no gasta nada.
        void Build()
        {
            if (material == null)
            {
                var shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    Debug.LogWarning($"PlayerEnergyOverlay: falta el shader Resources/{ShaderPath}.", this);
                    enabled = false;
                    return;
                }
                material = new Material(shader) { name = "CameraFX EnergyOverlay" };
            }

            foreach (SkinnedMeshRenderer source in controller.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (source.sharedMesh == null || source.GetComponentInParent<VfxBehaviour>() != null) continue;
                var go = new GameObject(source.name + "_Energy") { layer = source.gameObject.layer };
                go.transform.SetParent(transform, false);
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = source.sharedMesh;
                copy.bones = source.bones;
                copy.rootBone = source.rootBone;
                copy.localBounds = source.localBounds;
                copy.updateWhenOffscreen = source.updateWhenOffscreen;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                copy.sharedMaterials = materials;
                copies.Add((source, copy));
            }
        }
    }
}
