using UnityEngine;
using UnityEngine.SceneManagement;

namespace Racoon.UI
{
    /// <summary>
    /// Todos los Canvas del proyecto van en Screen Space - Camera. La cámara vive en su propio prefab (PF_CAMERAS),
    /// así que los prefabs de UI no pueden guardarla: este script se la pone en runtime a todo Canvas raíz que no
    /// la tenga (o la tenga destruida), y pasa a Screen Space - Camera cualquier Canvas raíz que siga en Overlay
    /// (p. ej. uno creado por código o por un paquete).
    /// No se monta a mano: arranca solo al cargar la primera escena y repasa los Canvas al cargar cada escena y
    /// cada poco tiempo (para los que se instancian o activan más tarde).
    /// </summary>
    public sealed class CanvasCameraBinder : MonoBehaviour
    {
        const float ScanInterval = 0.25f;
        // Distancia por defecto al convertir un Overlay: cerca para que el escenario no tape la UI.
        const float DefaultPlaneDistance = 1f;

        static CanvasCameraBinder instance;
        float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("[CanvasCameraBinder]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CanvasCameraBinder>();
        }

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Scan();

        void LateUpdate()
        {
            if (Time.unscaledTime < nextScan) return;
            Scan();
        }

        void Scan()
        {
            nextScan = Time.unscaledTime + ScanInterval;
            Camera cam = Camera.main;
            if (cam == null) return;
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.planeDistance = DefaultPlaneDistance;
                }
                if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == cam) continue;
                if (canvas.worldCamera == null || !canvas.worldCamera.isActiveAndEnabled) canvas.worldCamera = cam;
                if (canvas.planeDistance <= cam.nearClipPlane) canvas.planeDistance = cam.nearClipPlane + 0.05f;
            }
        }
    }
}
