using Racoon.Gameplay;
using Racoon.VFX;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Visor 3D de un personaje para la selección: el modelo gira despacio en una «peana» lejos del nivel, haciendo
    /// Idle y de vez en cuando un gesto (puñetazo, lanzar, coger), y una cámara propia lo pinta en el RawImage.
    ///
    /// Va en el RawImage donde se ve (lo añade ConnectionMenu). Solo se instancia el modelo con su Animator: se le
    /// quitan los scripts antes de activarlo, así que no arranca nada de red ni de gameplay.
    /// Mismo truco que HUDLivePortraits: capa propia (solo la ve su cámara) y RenderTexture con fondo transparente.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class CharacterPreviewStage : MonoBehaviour
    {
        static readonly int StateId = Animator.StringToHash("State");
        static readonly int ActionStartId = Animator.StringToHash("ActionStart");
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int SackSizeId = Animator.StringToHash("SackSize");

        // Gestos del Animator del jugador (valores de PlayerState): Interact (coger), Punch, UseItem (lanzar).
        static readonly int[] Gestures = { 12, 13, 10 };

        [Tooltip("Capa exclusiva de este visor (sin nombre en Tags & Layers: no la usa nada más). Cada visor, una distinta.")]
        [SerializeField, Range(8, 31)] int layer = 30;
        [Tooltip("Dónde se monta la peana (lejos del nivel para que no se vea nada más).")]
        [SerializeField] Vector3 stagePosition = new(0f, -500f, 0f);
        [SerializeField, Min(64)] int resolution = 512;
        [SerializeField, Range(10f, 60f)] float fieldOfView = 26f;
        [Tooltip("Cuánto sobra alrededor del modelo (1 = justo).")]
        [SerializeField, Min(1f)] float framing = 1.35f;
        [Tooltip("Grados por segundo que gira el modelo.")]
        [SerializeField] float spinSpeed = 30f;
        [Tooltip("Segundos entre gestos.")]
        [SerializeField, Min(1f)] float gestureInterval = 3.5f;
        [Tooltip("Segundos que dura cada gesto antes de volver a Idle.")]
        [SerializeField, Min(0.1f)] float gestureDuration = 0.7f;
        [Tooltip("Giro inicial (0 = de frente a cámara). Al cambiar de personaje se mantiene el giro que llevaba.")]
        [SerializeField] float startYaw = 0f;

        RawImage image;
        Transform stage;
        Camera stageCamera;
        RenderTexture texture;
        GameObject model;
        Animator animator;
        PlayerCharacterDefinition shown;
        Quaternion modelOffset = Quaternion.identity; // giro del modelo dentro del prefab del jugador
        float yaw;
        bool spinning;
        float nextGesture, gestureEnds;
        int gestureIndex;

        public int Layer { get => layer; set => layer = value; }
        public Vector3 StagePosition { get => stagePosition; set => stagePosition = value; }
        public float SpinSpeed { get => spinSpeed; set => spinSpeed = value; }
        /// <summary>Segundos entre gestos (muy alto = quieto en Idle).</summary>
        public float GestureInterval { get => gestureInterval; set => gestureInterval = Mathf.Max(1f, value); }
        public float StartYaw { get => startYaw; set => startYaw = value; }

        void Awake()
        {
            image = GetComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
        }

        void OnDisable() => Clear();
        void OnDestroy() => Clear();

        /// <summary>Muestra este personaje (null = vacío). Si ya se está viendo, no hace nada.</summary>
        public void Show(PlayerCharacterDefinition character)
        {
            // Con el panel aún oculto no se monta nada (ni ha corrido Awake): se vuelve a pedir cada frame.
            if (!isActiveAndEnabled) return;
            if (character == shown && (character == null || model != null)) return;
            shown = character;

            if (model != null) Destroy(model);
            model = null;
            animator = null;

            if (character == null || character.prefab == null)
            {
                if (image != null) image.enabled = false;
                return;
            }

            EnsureStage();
            model = BuildModel(character.prefab.gameObject);
            if (model == null)
            {
                image.enabled = false;
                return;
            }

            model.transform.SetParent(stage, false);
            model.transform.localPosition = Vector3.zero;
            if (!spinning)
            {
                yaw = startYaw;
                spinning = true;
            }
            ApplyYaw();
            animator = model.GetComponent<Animator>();
            FrameCamera();
            nextGesture = Time.unscaledTime + gestureInterval * 0.5f;
            gestureEnds = 0f;
            image.texture = texture;
            image.enabled = true;
        }

        void EnsureStage()
        {
            if (stage != null) return;
            stage = new GameObject($"[Visor personaje · capa {layer}]").transform;
            stage.position = stagePosition;

            texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = $"Visor personaje {layer}",
                antiAliasing = 4,
            };

            var camGo = new GameObject("Cámara visor");
            camGo.transform.SetParent(stage, false);
            stageCamera = camGo.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Color.clear;
            stageCamera.cullingMask = 1 << layer;
            stageCamera.fieldOfView = fieldOfView;
            stageCamera.nearClipPlane = 0.05f;
            stageCamera.farClipPlane = 30f;
            stageCamera.depth = -60f;
            stageCamera.targetTexture = texture;

            var data = stageCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; // sin efectos: el fondo se queda transparente
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            // Luz propia para que el modelo se vea igual en cualquier escena (solo ilumina esta capa).
            var lightGo = new GameObject("Luz visor");
            lightGo.transform.SetParent(stage, false);
            lightGo.transform.localRotation = Quaternion.Euler(35f, 150f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.cullingMask = 1 << layer;
            light.shadows = LightShadows.None;
        }

        // Copia del modelo (el hijo con Animator del prefab del jugador) sin scripts, colisiones ni efectos.
        GameObject BuildModel(GameObject playerPrefab)
        {
            Animator source = playerPrefab.GetComponentInChildren<Animator>(true);
            if (source == null) return null;
            // El frente del personaje es el del prefab del jugador, no el del modelo (puede venir girado).
            modelOffset = Quaternion.Inverse(playerPrefab.transform.rotation) * source.transform.rotation;

            // Se instancia bajo un padre inactivo: nada se despierta hasta haberlo limpiado.
            var holder = new GameObject("holder");
            holder.SetActive(false);
            GameObject copy = Instantiate(source.gameObject, holder.transform, false);
            copy.name = playerPrefab.name + " (visor)";

            foreach (VfxBehaviour vfx in copy.GetComponentsInChildren<VfxBehaviour>(true))
                if (vfx != null) vfx.gameObject.SetActive(false);
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) DestroyImmediate(behaviour);
            foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
            foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                    skinned.shadowCastingMode = ShadowCastingMode.Off;
                }
                else if (renderer is MeshRenderer mesh) mesh.shadowCastingMode = ShadowCastingMode.Off;
                else if (renderer is SpriteRenderer sprite) sprite.shadowCastingMode = ShadowCastingMode.Off; // los ojos
                else renderer.enabled = false; // partículas, estelas, líneas
            }
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            var anim = copy.GetComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.updateMode = AnimatorUpdateMode.UnscaledTime;

            copy.transform.SetParent(null, false);
            Destroy(holder);
            copy.SetActive(true);
            return copy;
        }

        void FrameCamera()
        {
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            float height = any ? Mathf.Max(0.3f, bounds.max.y - stage.position.y) : 1f;
            float distance = height * framing * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 target = stage.position + Vector3.up * height * 0.5f;
            stageCamera.transform.position = target + new Vector3(0f, height * 0.08f, distance);
            stageCamera.transform.LookAt(target);
        }

        void Update()
        {
            if (model == null) return;
            float time = Time.unscaledTime;
            yaw = Mathf.Repeat(yaw + spinSpeed * Time.unscaledDeltaTime, 360f);
            ApplyYaw();

            if (animator == null || animator.runtimeAnimatorController == null) return;
            animator.SetFloat(SpeedId, 0f);
            animator.SetFloat(SackSizeId, 0f);

            if (gestureEnds > 0f && time >= gestureEnds)
            {
                gestureEnds = 0f;
                animator.SetInteger(StateId, 0);
            }
            if (time >= nextGesture)
            {
                nextGesture = time + gestureInterval;
                gestureEnds = time + gestureDuration;
                // El estado antes que el trigger: las transiciones comprueban los dos a la vez.
                animator.SetInteger(StateId, Gestures[gestureIndex++ % Gestures.Length]);
                animator.SetTrigger(ActionStartId);
            }
        }

        void ApplyYaw() => model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * modelOffset;

        void Clear()
        {
            shown = null;
            spinning = false;
            animator = null;
            if (model != null) Destroy(model);
            model = null;
            if (stage != null) Destroy(stage.gameObject);
            stage = null;
            stageCamera = null;
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
            texture = null;
            if (image != null)
            {
                image.texture = null;
                image.enabled = false;
            }
        }
    }
}
