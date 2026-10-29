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
            canvas.sortingOrder = Mathf.Min(order, ceiling - 1);
            canvas.planeDistance = distance;
        }
    }
}
