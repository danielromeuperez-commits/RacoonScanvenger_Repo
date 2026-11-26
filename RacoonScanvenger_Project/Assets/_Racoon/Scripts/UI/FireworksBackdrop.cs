using Racoon.VFX;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Fuegos artificiales de fondo para una pantalla de UI (PF_HUD_MatchResult > RAW_Fireworks).
    /// Monta PF_VFX_Fireworks a tamaño real lejos del nivel, en una capa que solo ve su cámara, y lo pinta en este
    /// RawImage con fondo transparente. El RawImage lleva M_UI_FireworksComposite (mezcla premultiplicada): las
    /// partículas son aditivas y con la mezcla normal de UI saldrían apagadas.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class FireworksBackdrop : MonoBehaviour
    {
        [SerializeField] FireworksShow fireworksPrefab;
        [Tooltip("Capa exclusiva (sin nombre en Tags & Layers). 30/31 retratos y visores, 29 iconos, 26/27 visores del resultado.")]
        [SerializeField, Range(8, 31)] int layer = 25;
        [SerializeField] Vector3 stagePosition = new(0f, -1500f, 0f);
        [Tooltip("Cámara respecto a la base del espectáculo: de lejos y algo baja para ver el cielo entero.")]
        [SerializeField] Vector3 cameraOffset = new(0f, 20f, -150f);
        [SerializeField] Vector3 cameraLookOffset = new(0f, 30f, 0f);
        [SerializeField, Range(20f, 90f)] float fieldOfView = 52f;
        [SerializeField, Min(0.1f)] float resolutionScale = 0.6f;
        [Tooltip("Zona de lanzamiento (m) al centrarse detrás de un jugador: más estrecha que la del prefab para que no invada el otro lado.")]
        [SerializeField] Vector2 focusedLaunchArea = new(50f, 12f);

        RawImage image;
        Transform stage;
        Camera stageCamera;
        RenderTexture texture;

        void Awake()
        {
            image = GetComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
        }

        void OnDisable() => Stop();
        void OnDestroy() => Stop();

        /// <summary>Fuegos detrás de un punto de la UI: <paramref name="offsetX"/> en unidades del canvas desde el centro
        /// (p. ej. la X del hueco del ganador). 0 = centrados con la zona de lanzamiento del prefab.</summary>
        public void Play(float offsetX = 0f)
        {
            // La imagen ocupa toda la pantalla y se desplaza en horizontal: el espectáculo queda detrás de ese punto.
            var rect = (RectTransform)transform;
            rect.offsetMin = new Vector2(offsetX, rect.offsetMin.y);
            rect.offsetMax = new Vector2(offsetX, rect.offsetMax.y);
            if (stage != null || fireworksPrefab == null) return;

            stage = new GameObject($"[Fuegos resultado · capa {layer}]").transform;
            stage.position = stagePosition;

            // Se instancia bajo la peana inactiva: capa puesta antes de que arranque (OnEnable ya emite).
            stage.gameObject.SetActive(false);
            FireworksShow show = Instantiate(fireworksPrefab, stage, false);
            foreach (Transform t in show.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            if (!Mathf.Approximately(offsetX, 0f)) show.launchArea = focusedLaunchArea; // antes de que arranque
            show.gameObject.SetActive(true);

            int width = Mathf.Max(1280, Mathf.RoundToInt(Screen.width * resolutionScale));
            int height = Mathf.Max(720, Mathf.RoundToInt(Screen.height * resolutionScale));
            texture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "Fuegos resultado" };

            var camGo = new GameObject("Cámara fuegos");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = cameraOffset;
            camGo.transform.LookAt(stage.position + cameraLookOffset);
            stageCamera = camGo.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Color.clear;
            stageCamera.cullingMask = 1 << layer;
            stageCamera.fieldOfView = fieldOfView;
            stageCamera.nearClipPlane = 1f;
            stageCamera.farClipPlane = 500f;
            stageCamera.depth = -61f;
            stageCamera.targetTexture = texture;

            var data = stageCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; // el fondo se queda transparente
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            stage.gameObject.SetActive(true);
            image.texture = texture;
            image.enabled = true;
        }

        public void Stop()
        {
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
