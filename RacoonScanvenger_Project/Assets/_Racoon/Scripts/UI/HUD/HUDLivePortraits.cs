using System.Collections.Generic;
using Racoon.Player;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Retratos en vivo: una cámara grúa por jugador que sigue a su personaje y lo graba de frente en una RenderTexture
    /// con fondo transparente, que se ve dentro del círculo del retrato (por encima de la ilustración).
    /// Solo graba el modelo: sus renderers (mallas bajo el Animator, sin partículas, estelas ni lo que coja después)
    /// pasan a una capa propia de cada jugador, que solo ve su cámara; las demás cámaras que lo veían la siguen viendo.
    /// Va en la raíz del HUD (lo pone el montaje; si falta, lo añade <see cref="HUDController"/>). Si el prefab del
    /// jugador trae su <see cref="HUDPortraitCamera"/>, se usa su cámara del perfil (cameraP1/cameraP2) tal cual está colocada; si no, se crea una grúa
    /// automática en la raíz de la escena («Grúa retrato P1/P2») con los ajustes de abajo.
    /// </summary>
    [DisallowMultipleComponent]
    public class HUDLivePortraits : MonoBehaviour
    {
        [Tooltip("Capa de cada jugador (libre, sin colisiones): solo la ve su cámara de retrato.")]
        [SerializeField, Range(8, 31)] int layerP1 = 30;
        [SerializeField, Range(8, 31)] int layerP2 = 31;
        [Tooltip("Lado de la RenderTexture (px).")]
        [SerializeField, Min(64)] int resolution = 512;

        [Header("Grúa automática (jugadores sin HUDPortraitCamera en su prefab)")]
        [SerializeField, Range(10f, 60f)] float fieldOfView = 28f;
        [Tooltip("Altura del punto al que mira, en fracción de la altura del personaje (1 = la coronilla).")]
        [SerializeField, Range(0f, 1.2f)] float lookHeight = .45f;
        [Tooltip("Cuánto del personaje entra en el encuadre (fracción de su altura): menos = más primer plano.")]
        [SerializeField, Range(.2f, 1.5f)] float framing = 1f;
        [Tooltip("Giro de la grúa respecto a la cara (grados): se abre hacia el centro de la pantalla, a tres cuartos.")]
        [SerializeField, Range(0f, 90f)] float yaw = 25f;
        [Tooltip("Grados que sube la grúa por encima del punto al que mira.")]
        [SerializeField, Range(-30f, 60f)] float pitch = -2f;
        [Tooltip("Suavizado de la grúa (más = sigue más pegada).")]
        [SerializeField, Min(.1f)] float follow = 10f;
        [Tooltip("Oculta la ilustración fija mientras se ve el retrato en vivo.")]
        [SerializeField] bool hideIllustration = true;

        class Crane
        {
            public PlayerController player;
            public Transform model;
            public Camera camera;
            public HUDPortraitCamera rig; // la del prefab del jugador (no se destruye al soltarla)
            public RenderTexture texture;
            public RawImage image;
            public readonly List<(GameObject go, int layer)> moved = new();
            public int sourceMask;
            public float height = 1f;
            public Vector3 forward = Vector3.forward;
            public bool placed;
        }

        readonly Crane[] cranes = { new(), new() };
        readonly PlayerController[] wanted = new PlayerController[2];
        HUDView view;
        float nextCameraSync;

        HUDView View => view ? view : view = GetComponent<HUDView>();

        void OnDisable()
        {
            for (int i = 0; i < 2; i++) Release(cranes[i], i + 1);
        }

        /// <summary>Jugador que se ve en el retrato de P1 (1) o P2 (2). Null = sin retrato en vivo. La grúa se monta en el siguiente LateUpdate.</summary>
        public void SetPlayer(int number, PlayerController player)
        {
            wanted[number - 1] = player;
            if (cranes[number - 1].player != player) Release(cranes[number - 1], number);
        }

        void Setup(Crane crane, int number, PlayerController player)
        {
            var animator = player.GetComponentInChildren<Animator>(true);
            crane.player = player;
            crane.model = animator ? animator.transform : player.transform;
            int layer = number == 1 ? layerP1 : layerP2;

            // Solo las mallas del modelo (ni partículas ni estelas ni colisionadores)
            var bounds = new Bounds(crane.model.position, Vector3.zero);
            foreach (var renderer in crane.model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not (SkinnedMeshRenderer or MeshRenderer)) continue;
                if (renderer.GetComponent<Collider>()) continue;
                crane.moved.Add((renderer.gameObject, renderer.gameObject.layer));
                crane.sourceMask |= 1 << renderer.gameObject.layer;
                renderer.gameObject.layer = layer;
                bounds.Encapsulate(renderer.bounds);
            }
            crane.height = Mathf.Max(.3f, bounds.max.y - player.transform.position.y);

            crane.texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { name = $"Retrato P{number}", antiAliasing = 4 };
            // La cámara del prefab que toca a este perfil (cameraP1 o cameraP2); sin ella, la grúa automática
            crane.rig = player.GetComponentInChildren<HUDPortraitCamera>(true);
            crane.camera = crane.rig ? crane.rig.Use(number, crane.texture, 1 << layer) : null;
            if (!crane.camera) crane.rig = null;
            if (!crane.camera)
            {
                var go = new GameObject($"Grúa retrato P{number}");
                crane.camera = go.AddComponent<Camera>();
                crane.camera.clearFlags = CameraClearFlags.SolidColor;
                crane.camera.backgroundColor = Color.clear;
                crane.camera.cullingMask = 1 << layer;
                crane.camera.fieldOfView = fieldOfView;
                crane.camera.nearClipPlane = .05f;
                crane.camera.farClipPlane = 50f;
                crane.camera.depth = -50f;
                crane.camera.targetTexture = crane.texture;
            }
            var data = crane.camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; // sin efectos: el fondo se queda transparente
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            crane.image = Image(number);
            if (crane.image)
            {
                crane.image.texture = crane.texture;
                crane.image.enabled = true;
            }
            var illustration = View ? View.Player(number).illustration : null;
            if (hideIllustration && illustration) illustration.gameObject.SetActive(false);
            crane.placed = false;
            nextCameraSync = 0f;
        }

        void Release(Crane crane, int number)
        {
            foreach (var (go, layer) in crane.moved)
                if (go) go.layer = layer;
            crane.moved.Clear();
            crane.sourceMask = 0;
            if (crane.rig) crane.rig.Stop();
            else if (crane.camera) Destroy(crane.camera.gameObject);
            crane.rig = null;
            if (crane.texture)
            {
                crane.texture.Release();
                Destroy(crane.texture);
            }
            if (crane.image)
            {
                crane.image.texture = null;
                crane.image.enabled = false;
            }
            var illustration = View ? View.Player(number).illustration : null;
            if (illustration) illustration.gameObject.SetActive(true);
            crane.player = null;
            crane.model = null;
            crane.camera = null;
            crane.texture = null;
        }

        // RawImage del retrato dentro de la máscara del círculo, encima de la ilustración (si el prefab no la trae, se crea)
        RawImage Image(int number)
        {
            if (!View) return null;
            var pv = View.Player(number);
            if (pv.liveIllustration) return pv.liveIllustration;
            if (!pv.illustration) return null;
            var go = new GameObject("Retrato en vivo", typeof(RectTransform)) { layer = pv.illustration.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(pv.illustration.transform.parent, false);
            rect.SetSiblingIndex(pv.illustration.transform.GetSiblingIndex() + 1);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            pv.liveIllustration = image;
            return image;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < 2; i++)
            {
                var crane = cranes[i];
                if (crane.camera && (!crane.player || !crane.model)) Release(crane, i + 1); // el jugador ha desaparecido
                if (!crane.camera && wanted[i]) Setup(crane, i + 1, wanted[i]);
                if (!crane.camera || crane.rig) continue; // la del prefab va pegada al jugador tal cual la colocaste
                Place(crane, i == 0 ? 1f : -1f, dt);
            }
            if (Time.unscaledTime >= nextCameraSync) SyncOtherCameras();
        }

        // La grúa sigue al personaje por delante (solo el giro horizontal, suavizado) y mira a su pecho/cabeza
        void Place(Crane crane, float side, float dt)
        {
            var root = crane.player.transform;
            var facing = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            if (facing.sqrMagnitude > 1e-4f) crane.forward = facing.normalized;
            var target = root.position + Vector3.up * (crane.height * lookHeight);
            float distance = crane.height * framing * .5f / Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad);
            // P1 (izquierda) se abre hacia la derecha y P2 hacia la izquierda: los dos miran al centro de la pantalla
            var direction = Quaternion.AngleAxis(side * yaw, Vector3.up) * crane.forward;
            direction = Quaternion.AngleAxis(pitch, Vector3.Cross(direction, Vector3.up)) * direction;
            var position = target + direction * distance;
            var rotation = Quaternion.LookRotation(target - position, Vector3.up);
            var t = crane.camera.transform;
            if (!crane.placed)
            {
                t.SetPositionAndRotation(position, rotation);
                crane.placed = true;
                return;
            }
            float k = 1f - Mathf.Exp(-follow * dt);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, position, k), Quaternion.Slerp(t.rotation, rotation, k));
            crane.camera.fieldOfView = fieldOfView;
        }

        // Las cámaras que veían al personaje en su capa original lo siguen viendo en la del retrato
        void SyncOtherCameras()
        {
            nextCameraSync = Time.unscaledTime + 1f;
            int source = cranes[0].sourceMask | cranes[1].sourceMask;
            if (source == 0) return;
            int portraits = (cranes[0].camera ? 1 << layerP1 : 0) | (cranes[1].camera ? 1 << layerP2 : 0);
            foreach (var camera in Camera.allCameras)
            {
                if (camera == cranes[0].camera || camera == cranes[1].camera) continue;
                if ((camera.cullingMask & source) != 0 && (camera.cullingMask & portraits) != portraits)
                    camera.cullingMask |= portraits;
            }
        }
    }
}
