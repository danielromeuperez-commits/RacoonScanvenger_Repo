using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Racoon.VFX
{
    /// <summary>
    /// «Pompa de visión»: cuando algo tapa a un jugador (al tuyo o al rival), se abre un círculo transparente a su
    /// alrededor para verle. El recorte lo hace el shader (Visuals/Shaders/RacoonBubble.hlsl, con tramado: sin
    /// transparencias ni ordenar); aquí se le pasan cada frame las posiciones de los jugadores y se preparan los objetos:
    ///  - Solo se recortan los objetos de las capas elegidas en OcclusionBubbleSettings (Settings/Resources): se les
    ///    marca con un bit de rendering layer que el shader comprueba.
    ///  - Sirve con materiales FlatKit Stylized Surface y URP Lit; a los Lit se les pone la copia SH_Racoon_LitPompa
    ///    (se ven igual). Otros shaders no se recortan.
    /// No hace falta montarlo: se crea solo al arrancar el juego.
    /// </summary>
    public class OcclusionBubbles : MonoBehaviour
    {
        const int MaxBubbles = 4;                       // igual que RACOON_MAX_BUBBLES en el shader
        const uint BubbleRenderingLayer = 1u << 30;     // igual que RACOON_BUBBLE_LAYER en el shader
        const string LitShaderName = "Universal Render Pipeline/Lit";

        static readonly int BubblesId = Shader.PropertyToID("_RacoonBubbles");
        static readonly int ParamsId = Shader.PropertyToID("_RacoonBubbleParams");

        OcclusionBubbleSettings settings;
        readonly Vector4[] bubbles = new Vector4[MaxBubbles];
        readonly List<PlayerController> players = new();
        readonly Dictionary<Material, Material> litCopies = new();
        float nextPlayerRefresh, nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<OcclusionBubbles>() != null) return;
            var go = new GameObject("[Pompa de visión]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<OcclusionBubbles>();
        }

        void Awake()
        {
            settings = Resources.Load<OcclusionBubbleSettings>("OcclusionBubbleSettings");
            if (settings == null) settings = ScriptableObject.CreateInstance<OcclusionBubbleSettings>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            nextScan = 0f;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
        }

        void OnDestroy()
        {
            foreach (Material copy in litCopies.Values)
                if (copy != null) Destroy(copy);
            litCopies.Clear();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => nextScan = 0f;

        void LateUpdate()
        {
            float now = Time.unscaledTime;
            if (now >= nextScan)
            {
                nextScan = settings.rescanInterval > 0f ? now + settings.rescanInterval : float.PositiveInfinity;
                PrepareRenderers();
            }

            if (now >= nextPlayerRefresh)
            {
                nextPlayerRefresh = now + 0.5f;
                players.Clear();
                players.AddRange(FindObjectsByType<PlayerController>(FindObjectsSortMode.None));
            }

            int count = 0;
            foreach (PlayerController player in players)
            {
                if (count >= MaxBubbles) break;
                if (player == null || !player.isActiveAndEnabled || !player.IsSpawned) continue;
                Vector3 feet = player.transform.position;
                bubbles[count++] = new Vector4(feet.x, feet.y + settings.chestHeight, feet.z, feet.y);
            }

            Shader.SetGlobalVectorArray(BubblesId, bubbles);
            Shader.SetGlobalVector(ParamsId, new Vector4(count, settings.radius, settings.edge, settings.frontMargin));
        }

        // Marca (o desmarca) cada renderer según su capa y pasa sus materiales Lit a la copia con pompa.
        void PrepareRenderers()
        {
            int mask = settings.includedLayers.value;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
                bool included = (mask & (1 << renderer.gameObject.layer)) != 0;
                // Lo que lleva encima un jugador (objeto a la espalda, extintor, despertador...) nunca se recorta.
                if (included && renderer.GetComponentInParent<PlayerController>(true) != null) included = false;
                uint layers = renderer.renderingLayerMask;
                uint wanted = included ? layers | BubbleRenderingLayer : layers & ~BubbleRenderingLayer;
                if (wanted != layers) renderer.renderingLayerMask = wanted;
                if (included && settings.litBubbleShader != null) SwapLitMaterials(renderer);
            }
        }

        void SwapLitMaterials(Renderer renderer)
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null || source.shader == null || source.shader.name != LitShaderName) continue;
                if (!litCopies.TryGetValue(source, out Material copy) || copy == null)
                {
                    // Mismas propiedades y keywords: se ve igual que el Lit original.
                    copy = new Material(source) { name = source.name + " (pompa)", shader = settings.litBubbleShader };
                    copy.CopyPropertiesFromMaterial(source);
                    copy.shaderKeywords = source.shaderKeywords;
                    copy.renderQueue = source.renderQueue;
                    litCopies[source] = copy;
                }
                materials[i] = copy;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
    }
}
