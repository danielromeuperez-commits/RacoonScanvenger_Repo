using Racoon.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Racoon.Cameras
{
    /// <summary>
    /// Cámaras para grabar el trailer, locales a cada cliente (no se sincronizan por red).
    /// 1 = cámara de juego normal · 2 = al hombro (sigue el giro del jugador) · 3 = de frente (fija al
    /// activarla; pulsa 3 otra vez para recolocarla) · 4 = tercera persona con ratón, estilo shooter.
    /// Las 2-4 son una cámara aparte (sin tag MainCamera) que pinta encima de la principal; el HUD no sale en
    /// ellas. El movimiento del jugador se calcula respecto a <see cref="ActiveCamera"/> si hay una activa.
    /// Se crea sola al arrancar; no hay que poner nada en escena.
    /// </summary>
    public class TrailerCameraSwitcher : MonoBehaviour
    {
        enum Mode { Gameplay, Shoulder, Front, Mouse }

        [System.Serializable]
        struct Rig
        {
            public Vector3 offset;   // en espacio del jugador (x derecha, y arriba, z delante)
            public Vector3 lookAt;   // punto que mira, en espacio del jugador
            public float fov;
        }

        /// <summary>Cámara de trailer que se está viendo, o null con la cámara de juego (1).</summary>
        public static Camera ActiveCamera { get; private set; }

        [SerializeField] Rig shoulder = new() { offset = new(0.7f, 1.6f, -2.2f), lookAt = new(0.3f, 1.1f, 4f), fov = 55f };
        [SerializeField] Rig front    = new() { offset = new(0f, 1.0f, 2.6f),    lookAt = new(0f, 0.8f, 0f),   fov = 45f };
        [SerializeField] float headingSmooth = 0.6f;
        [SerializeField] float positionSmooth = 0.12f;

        [Header("4 · Ratón")]
        [SerializeField] Vector3 mousePivot = new(0f, 1.3f, 0f);      // punto sobre el jugador alrededor del que gira
        [SerializeField] Vector3 mouseShoulder = new(0.75f, 0.2f, -3f); // desde el pivote, en espacio de la cámara
        [SerializeField] float mouseFov = 60f;
        [SerializeField] float mouseSensitivity = 0.12f;               // grados por píxel
        [SerializeField] Vector2 pitchLimits = new(-35f, 65f);
        [SerializeField] float collisionRadius = 0.25f;

        Mode mode = Mode.Gameplay;
        Camera trailerCam;
        float heading, headingVel, yaw, pitch;
        Vector3 posVel;
        CursorLockMode prevLock;
        bool prevVisible;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("[TrailerCameraSwitcher]");
            DontDestroyOnLoad(go);
            go.AddComponent<TrailerCameraSwitcher>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SetMode(Mode.Gameplay);
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SetMode(Mode.Shoulder);
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SetMode(Mode.Front);
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) SetMode(Mode.Mouse);
        }

        void SetMode(Mode next)
        {
            if (mode == Mode.Mouse && next != Mode.Mouse) ReleaseCursor();

            PlayerController player = PlayerController.Local;
            if (next != Mode.Gameplay && (player == null || !EnsureCamera())) next = Mode.Gameplay;

            if (next == Mode.Gameplay)
            {
                mode = next;
                if (trailerCam != null) trailerCam.enabled = false;
                ActiveCamera = null;
                return;
            }

            if (next == Mode.Mouse && mode != Mode.Mouse)
            {
                prevLock = Cursor.lockState;
                prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                // Arranca mirando desde donde se estaba viendo, para no dar un salto.
                Transform from = ActiveCamera != null ? ActiveCamera.transform : Camera.main != null ? Camera.main.transform : player.transform;
                yaw = from.eulerAngles.y;
                pitch = 10f;
            }

            mode = next;
            heading = player.transform.eulerAngles.y;
            headingVel = 0f;
            trailerCam.enabled = true;
            ActiveCamera = trailerCam;
            Place(player.transform, true);
        }

        void ReleaseCursor()
        {
            Cursor.lockState = prevLock;
            Cursor.visible = prevVisible;
        }

        void OnApplicationFocus(bool focus)
        {
            // Al volver a la ventana en modo ratón, se vuelve a bloquear el cursor.
            if (focus && mode == Mode.Mouse) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        bool EnsureCamera()
        {
            if (trailerCam != null) return true;
            Camera main = Camera.main;
            if (main == null) return false;

            var go = new GameObject("TrailerCamera");
            go.transform.SetParent(transform, false);
            trailerCam = go.AddComponent<Camera>();
            trailerCam.CopyFrom(main);
            trailerCam.tag = "Untagged";
            trailerCam.depth = main.depth + 10f;
            trailerCam.targetTexture = null;
            trailerCam.cullingMask &= ~LayerMask.GetMask("UI");

            var mainData = main.GetUniversalAdditionalCameraData();
            var data = trailerCam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = mainData.renderPostProcessing;
            data.antialiasing = mainData.antialiasing;
            data.antialiasingQuality = mainData.antialiasingQuality;
            data.renderShadows = mainData.renderShadows;
            data.volumeLayerMask = mainData.volumeLayerMask;
            data.volumeTrigger = trailerCam.transform;
            return true;
        }

        void LateUpdate()
        {
            if (mode == Mode.Gameplay || trailerCam == null || !trailerCam.enabled) return;
            PlayerController player = PlayerController.Local;
            if (player == null) { SetMode(Mode.Gameplay); return; }
            Place(player.transform, false);
        }

        void Place(Transform target, bool snap)
        {
            Transform t = trailerCam.transform;
            if (mode == Mode.Mouse) { PlaceMouse(target, t); return; }

            // De frente: orientación fija (si siguiera el giro, al andar hacia la cámara el jugador se daría
            // la vuelta, la cámara también, y así en bucle). Al hombro: sigue el giro con suavidad.
            if (mode == Mode.Shoulder && !snap)
                heading = Mathf.SmoothDampAngle(heading, target.eulerAngles.y, ref headingVel, headingSmooth);

            Rig rig = mode == Mode.Shoulder ? shoulder : front;
            Quaternion basis = Quaternion.Euler(0f, heading, 0f);
            Vector3 origin = target.position;
            Vector3 desired = origin + basis * rig.offset;
            Vector3 look = origin + basis * rig.lookAt;

            t.position = snap ? desired : Vector3.SmoothDamp(t.position, desired, ref posVel, positionSmooth);
            if (snap) posVel = Vector3.zero;
            t.rotation = Quaternion.LookRotation(look - t.position, Vector3.up);
            trailerCam.fieldOfView = rig.fov;
        }

        void PlaceMouse(Transform target, Transform t)
        {
            if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
                yaw += delta.x;
                pitch = Mathf.Clamp(pitch - delta.y, pitchLimits.x, pitchLimits.y);
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pivot = target.position + mousePivot;
            Vector3 offset = rot * mouseShoulder;
            float dist = offset.magnitude;

            // Que la cámara no atraviese paredes: se acerca al pivote si choca con algo (ignorando al jugador).
            foreach (RaycastHit hit in Physics.SphereCastAll(pivot, collisionRadius, offset / dist, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(target) || hit.distance <= 0f) continue;
                dist = Mathf.Min(dist, hit.distance);
            }

            t.position = pivot + offset.normalized * dist;
            t.rotation = rot;
            trailerCam.fieldOfView = mouseFov;
        }
    }
}
