using UnityEngine;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Cámaras grúa del retrato en vivo, montadas en el prefab del jugador (Racoon → HUD → Montar cámaras de retrato en
    /// los jugadores): una por perfil, porque cada círculo mira hacia el centro de la pantalla. Si el jugador es P1 se usa
    /// <see cref="cameraP1"/>; si es P2, <see cref="cameraP2"/>. Colócalas y ajusta su FOV a mano: van pegadas al jugador
    /// y <see cref="HUDLivePortraits"/> solo les pone la RenderTexture, el fondo transparente y la capa del modelo.
    /// Apagadas mientras no las usa el HUD.
    /// </summary>
    public class HUDPortraitCamera : MonoBehaviour
    {
        [Tooltip("Retrato de P1 (izquierda): el personaje mira hacia la derecha.")]
        public Camera cameraP1;
        [Tooltip("Retrato de P2 (derecha): el personaje mira hacia la izquierda.")]
        public Camera cameraP2;

        Camera active;

        public Camera For(int number) => number == 1 ? cameraP1 : cameraP2;

        void Awake()
        {
            // Que no pinten en pantalla
            if (cameraP1 && cameraP1 != active) cameraP1.enabled = false;
            if (cameraP2 && cameraP2 != active) cameraP2.enabled = false;
        }

        /// <summary>Enciende la cámara del perfil <paramref name="number"/> grabando en <paramref name="texture"/>. Null si no la tiene.</summary>
        public Camera Use(int number, RenderTexture texture, int cullingMask)
        {
            Stop();
            var camera = For(number);
            if (!camera) return null;
            active = camera;
            camera.targetTexture = texture;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = cullingMask;
            camera.enabled = true;
            return camera;
        }

        public void Stop()
        {
            if (!active) return;
            active.enabled = false;
            active.targetTexture = null;
            active = null;
        }
    }
}
