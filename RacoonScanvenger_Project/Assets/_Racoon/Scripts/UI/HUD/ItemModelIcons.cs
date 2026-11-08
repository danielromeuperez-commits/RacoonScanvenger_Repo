using System.Collections.Generic;
using Racoon.Items;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Racoon.UI.HUD
{
    /// <summary>
    /// Iconos del HUD sacados del MODELO 3D de cada objeto (ItemData.heldVisualPrefab, el mismo que se ve en la mano),
    /// en vez del sprite. La primera vez que se pide un objeto se le hace una «foto» de tres cuartos con fondo
    /// transparente y se guarda; si no tiene modelo, se usa su sprite (ItemData.icon) como antes.
    /// </summary>
    public static class ItemModelIcons
    {
        const int Layer = 29;          // capa sin uso: solo la ve la cámara de la foto
        const int Size = 256;
        const int Outline = 9;                                   // grosor del contorno (px del icono)
        static readonly Color32 Ink = new(75, 67, 102, 255);    // tinta del HUD (#4b4366)
        static readonly Vector3 StudioPosition = new(0f, -900f, 0f);
        static readonly Dictionary<ItemData, Sprite> cache = new();

        /// <summary>Icono del objeto (del modelo si lo tiene; si no, su sprite). Null si no tiene ninguno.</summary>
        public static Sprite Get(ItemData item)
        {
            if (item == null) return null;
            if (cache.TryGetValue(item, out Sprite sprite) && sprite != null) return sprite;
            sprite = item.heldVisualPrefab != null ? Render(item.heldVisualPrefab, item.name) : null;
            if (sprite == null) sprite = item.icon;
            cache[item] = sprite;
            return sprite;
        }

        public static bool Has(ItemData item) => item != null && (item.icon != null || item.heldVisualPrefab != null);

        static Sprite Render(GameObject prefab, string name)
        {
            GameObject model = BuildModel(prefab);
            if (model == null) return null;

            var bounds = new Bounds(model.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any)
            {
                Object.DestroyImmediate(model);
                return null;
            }

            var camGo = new GameObject("Foto icono");
            var lightGo = new GameObject("Luz icono");
            var texture = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
            texture.antiAliasing = 4;
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
                cam.cullingMask = 1 << Layer;
                cam.orthographic = true;
                cam.targetTexture = texture;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;

                // Tres cuartos, un poco desde arriba: se lee la forma del objeto.
                Quaternion view = Quaternion.Euler(22f, -35f, 0f);
                float radius = bounds.extents.magnitude;
                cam.transform.rotation = view;
                cam.transform.position = bounds.center - view * Vector3.forward * (radius * 4f);
                cam.orthographicSize = radius * 1.2f;   // sitio para el contorno
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = radius * 8f;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.25f;
                light.cullingMask = 1 << Layer;
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(40f, -60f, 0f);

                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = texture;
                var pixels = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Icono " + name };
                pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                AddOutline(pixels);
                pixels.Apply(true, false);
                RenderTexture.active = previous;
                return Sprite.Create(pixels, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(texture);
                // Inmediato: si no, la cámara de la partida llegaría a ver el modelo en este frame.
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(model);
            }
        }

        // Contorno de tinta alrededor de la silueta, como el resto del HUD: así se ve sobre cualquier color de caja
        // (p. ej. un objeto verde sobre la caja verde del jugador).
        static void AddOutline(Texture2D texture)
        {
            Color32[] src = texture.GetPixels32();
            var dst = (Color32[])src.Clone();
            int r2 = Outline * Outline;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                if (src[i].a >= 200) continue;
                // Opacidad del contorno según lo cerca que quede la silueta (borde suave).
                float best = 0f;
                for (int dy = -Outline; dy <= Outline && best < 1f; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= Size) continue;
                    for (int dx = -Outline; dx <= Outline; dx++)
                    {
                        int xx = x + dx, d2 = dx * dx + dy * dy;
                        if (xx < 0 || xx >= Size || d2 > r2) continue;
                        if (src[yy * Size + xx].a < 128) continue;
                        float edge = Mathf.Clamp01(Outline + 0.5f - Mathf.Sqrt(d2));
                        if (edge > best) best = edge;
                    }
                }
                if (best <= 0f) continue;
                // El objeto (con su borde antialias) va encima de la tinta.
                Color ink = Ink;
                ink.a = best;
                Color over = src[i];
                float a = over.a + ink.a * (1f - over.a);
                Color rgb = a > 0f ? (over * over.a + ink * ink.a * (1f - over.a)) / a : ink;
                rgb.a = a;
                dst[i] = rgb;
            }
            texture.SetPixels32(dst);
        }

        // Solo la malla: sin scripts, red, física ni efectos (se limpia antes de activarlo).
        static GameObject BuildModel(GameObject prefab)
        {
            var holder = new GameObject("holder icono");
            holder.SetActive(false);
            GameObject copy = Object.Instantiate(prefab, holder.transform, false);
            // Al revés: primero los que dependen de otros (los NetworkBehaviour antes que su NetworkObject).
            MonoBehaviour[] behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (behaviours[i] != null) Object.DestroyImmediate(behaviours[i]);
            foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            foreach (Rigidbody body in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) renderer.enabled = false;
            }
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            copy.transform.SetParent(null, false);
            copy.transform.SetPositionAndRotation(StudioPosition, Quaternion.identity);
            Object.DestroyImmediate(holder);
            copy.SetActive(true);
            return copy;
        }
    }
}
