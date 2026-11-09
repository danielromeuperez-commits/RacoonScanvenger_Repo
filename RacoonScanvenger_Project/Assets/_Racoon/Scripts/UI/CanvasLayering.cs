using UnityEngine;

namespace Racoon.UI
{
    /// <summary>
    /// Orden entre Canvas Screen Space - Camera. Los avisos que siguen al jugador (machacar, bolsa, bebida) tienen que
    /// verse delante del HUD aunque este se haya montado con otro orden o distancia: se ponen por encima del mayor
    /// orden que haya (sin pasar de <c>ceiling</c>, para que los menús, como la pausa (999), sigan encima) y un pelín
    /// más cerca de la cámara que esos mismos canvas.
    /// </summary>
    public static class CanvasLayering
    {
        /// <summary>
        /// Saca un canvas del objeto en el que va anidado (el jugador): colgado de algo que se mueve y gira, un Screen
        /// Space - Camera hereda ese movimiento hasta que Unity lo vuelve a pegar a la cámara y lo que se coloca en él
        /// tiembla. Lo deja en la misma escena que <paramref name="owner"/> (también si es DontDestroyOnLoad).
        /// </summary>
        public static void DetachFrom(Transform canvas, GameObject owner)
        {
            canvas.SetParent(null, false);
            if (owner.scene.name == "DontDestroyOnLoad") Object.DontDestroyOnLoad(canvas.gameObject);
            else if (owner.scene.IsValid() && owner.scene != canvas.gameObject.scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvas.gameObject, owner.scene);
        }

        const float MinPlaneDistance = 0.5f;

        public static void BringToFront(Canvas canvas, int ceiling = 500)
        {
            if (canvas == null) return;
            int order = canvas.sortingOrder;
            float distance = canvas.planeDistance;
            foreach (Canvas other in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (other == canvas || !other.isRootCanvas || !other.isActiveAndEnabled) continue;
                if (other.renderMode != RenderMode.ScreenSpaceCamera || other.worldCamera != canvas.worldCamera) continue;
                if (other.sortingLayerID != canvas.sortingLayerID || other.sortingOrder >= ceiling) continue;
                order = Mathf.Max(order, other.sortingOrder + 1);
                distance = Mathf.Min(distance, other.planeDistance - 0.01f);
            }
            Camera cam = canvas.worldCamera;
            if (cam != null) distance = Mathf.Max(distance, cam.nearClipPlane + 0.01f);
            // Nunca demasiado pegado a la cámara: a unos centímetros el canvas queda tan pequeño que el shader SDF de
            // TextMeshPro pierde precisión y los textos desaparecen (pasaba con la cinta de eventos, por el overlay de
            // CameraFX junto al plano cercano). Ya queda delante por el orden (sortingOrder), no hace falta acercarlo.
            distance = Mathf.Max(distance, MinPlaneDistance);
            canvas.sortingOrder = Mathf.Min(order, ceiling - 1);
            canvas.planeDistance = distance;
        }
    }
}
