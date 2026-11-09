using System.Collections.Generic;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Showcase de programación: las mallas procedurales de <see cref="FlatMeshes"/> que usan todos los VFX (agujas,
    /// relámpagos, estrellas, gotas...), con los materiales reales de los efectos (SG_VFX_ToonFlat / ToonVolume).
    /// Las que tienen semilla se regeneran cada poco con una semilla nueva (misma función, forma distinta) y
    /// SetWire("On") superpone la malla de alambre para ver los triángulos que genera el código.
    /// Cada pieza se anima en bucle como un impacto (aparece con rebote, aguanta y se recoge) y la rejilla se
    /// coloca centrada delante de la cámara del showcase, ocupando un ancho fijo del encuadre.
    /// </summary>
    public class VfxShowcaseMeshGallery : MonoBehaviour
    {
        [SerializeField] Material flatMaterial;
        [SerializeField] Material volumeMaterial;
        [SerializeField] float spacing = 0.7f;
        [SerializeField] int columns = 5;
        [SerializeField] float spinSpeed = 40f;
        [SerializeField] Color wireColor = new(0.29f, 0.26f, 0.4f);
        [Header("Animación (bucle de impacto)")]
        [SerializeField, Min(0.3f)] float cycle = 1.2f;
        [SerializeField] float stagger = 0.09f;
        [Header("Encuadre")]
        [Tooltip("Cámara a la que se pega la rejilla. Vacío: la ShotCamera del director.")]
        [SerializeField] Camera frameCamera;
        [Tooltip("Parte del ancho de pantalla que ocupa la rejilla.")]
        [SerializeField, Range(0.3f, 1f)] float frameWidth = 0.78f;
        [Tooltip("Desplazamiento vertical en pantalla (fracción de la altura; negativo = más abajo).")]
        [SerializeField] float frameOffsetY = -0.06f;
        [Tooltip("Proporción de la imagen final (el vídeo es 16:9; en batchmode camera.aspect no es fiable).")]
        [SerializeField] float frameAspect = 16f / 9f;

        delegate Mesh Generator(int seed);

        sealed class Item
        {
            public FlatPiece piece;
            public MeshFilter filter;
            public MeshFilter wire;
            public Generator generate;
            public bool seeded;
            public bool volume;
            public float phase;
            public float offset;
            public int cycleIndex = -1;
            public float baseScale;
        }

        readonly List<Item> items = new();
        Material wireMaterial;
        int seed = 1;
        bool wireOn;

        void Start()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                wireMaterial = new Material(shader) { name = "Showcase Wire" };
                wireMaterial.SetColor("_BaseColor", wireColor);
            }

            Add(s => FlatMeshes.Rays(s, 9, 0.25f, new Vector2(0.5f, 0.9f), new Vector2(0.03f, 0.06f)), true, false);
            Add(s => FlatMeshes.Shards(s, 7, 0.2f, new Vector2(0.4f, 0.8f), new Vector2(0.06f, 0.1f), 0.04f), true, false);
            Add(s => FlatMeshes.Burst(s, 9, 0.6f, 0.12f, 0.07f, 0.5f), true, false);
            Add(_ => FlatMeshes.Arc(0.55f, 0f, 250f, 0.07f, 0.6f), false, false);
            Add(_ => FlatMeshes.Star(), false, false);
            Add(s => FlatMeshes.Splat(s), true, false);
            Add(_ => FlatMeshes.Drop(), false, true);
            Add(s => FlatMeshes.Spikes(s, 13, 12f, 70f, 0f, new Vector2(0.5f, 1f), new Vector2(0.045f, 0.065f), 0.035f, 0.1f, 1f, 4, false, 0f), true, true);
            Add(s => FlatMeshes.Bolts(s, 6, 70f, 0f, new Vector2(0.6f, 1f), 0.05f, 3, 0.16f, 0.03f, 0f), true, true);
            Add(_ => FlatMeshes.SpikeBall(0.5f), false, true);
        }

        void Add(Generator generate, bool seeded, bool volume)
        {
            int i = items.Count;
            int rows = Mathf.CeilToInt(10f / columns);
            float x = (i % columns - (columns - 1) * 0.5f) * spacing;
            float y = ((rows - 1) * 0.5f - i / columns) * spacing;

            var piece = new FlatPiece($"Gen{i}", transform, generate(seed + i * 17), volume ? volumeMaterial : flatMaterial)
                .Colors(new Color(1f, 0.97f, 0.86f), new Color(1f, 0.62f, 0.12f), new Color(0.45f, 0.3f, 0.1f))
                .Cel(0.5f, new Color(1f, 0.8f, 0.6f))
                .Shine(Color.white, new Color(1f, 1f, 0.9f, 0.8f));
            piece.Visible = true;   // FlatPiece nace apagado (los efectos lo encienden al dispararse)
            piece.Transform.localPosition = new Vector3(x, y, 0f);
            float size = volume ? 0.35f : 0.4f;
            piece.Transform.localScale = Vector3.zero;

            var wireGo = new GameObject("Wire");
            wireGo.transform.SetParent(piece.Transform, false);
            var wire = wireGo.AddComponent<MeshFilter>();
            wireGo.AddComponent<MeshRenderer>().sharedMaterial = wireMaterial;
            wireGo.SetActive(false);

            var item = new Item
            {
                piece = piece, filter = piece.Transform.GetComponent<MeshFilter>(), wire = wire,
                generate = generate, seeded = seeded, volume = volume, phase = i * 37f,
                offset = i * stagger, baseScale = size,
            };
            wire.sharedMesh = WireOf(item.filter.sharedMesh);
            items.Add(item);
        }

        /// <summary>SendMessage desde el director: "On" / "Off".</summary>
        public void SetWire(string value)
        {
            wireOn = value == "On";
            foreach (Item item in items) item.wire.gameObject.SetActive(wireOn);
        }

        void OnEnable() => startTime = Time.time;

        float startTime;

        void Update()
        {
            float t = Time.time - startTime;
            foreach (Item item in items)
            {
                // Bucle de impacto: aparece con rebote, aguanta girando, se recoge; cada vuelta, forma nueva.
                float local = t - item.offset;
                if (local < 0f) { item.piece.Transform.localScale = Vector3.zero; continue; }
                int index = Mathf.FloorToInt(local / cycle);
                float k = (local - index * cycle) / cycle;
                if (index != item.cycleIndex)
                {
                    item.cycleIndex = index;
                    if (index > 0 && item.seeded) Reseed(item, ++seed);
                }
                float pop = k < 0.22f ? FlatFx.OutBack(k / 0.22f, 2.2f)
                          : k < 0.78f ? 1f + 0.05f * Mathf.Sin((k - 0.22f) * 20f) * (0.78f - k)
                          : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.78f) / 0.22f);
                item.piece.Transform.localScale = Vector3.one * item.baseScale * Mathf.Max(0f, pop);

                // Las planas miran a cámara y giran sobre sí mismas; las de volumen giran para que se vea el relieve.
                float spin = item.phase + t * spinSpeed + (1f - Mathf.Min(1f, k / 0.22f)) * 90f;
                item.piece.Transform.localRotation = item.volume
                    ? Quaternion.Euler(20f, spin, 0f)
                    : Quaternion.Euler(0f, 0f, spin * 0.5f);
            }
        }

        void LateUpdate() => FrameToCamera();

        // Centrada delante de la cámara y a la distancia justa para ocupar frameWidth del ancho de pantalla.
        void FrameToCamera()
        {
            if (frameCamera == null)
            {
                var director = FindFirstObjectByType<VfxShowcaseDirector>();
                Transform shot = director != null ? director.transform.Find("ShotCamera") : null;
                frameCamera = shot != null ? shot.GetComponent<Camera>() : null;
                if (frameCamera == null) return;
            }
            float gridWidth = (columns - 1) * spacing + 0.5f;
            float halfV = Mathf.Tan(frameCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * frameAspect;
            float distance = gridWidth * 0.5f / (halfH * frameWidth);
            Transform cam = frameCamera.transform;
            Vector3 center = cam.position + cam.forward * distance + cam.up * (frameOffsetY * 2f * halfV * distance);
            // Que la fila de abajo no se meta en el suelo (y = 0): si hace falta, se sube la rejilla.
            int rows = Mathf.CeilToInt(items.Count / (float)columns);
            float bottom = center.y - (rows - 1) * 0.5f * spacing * cam.up.y - 0.3f;
            if (bottom < 0.05f && cam.up.y > 0.1f) center += cam.up * ((0.05f - bottom) / cam.up.y);
            transform.SetPositionAndRotation(center, Quaternion.LookRotation(cam.forward, cam.up));
        }

        void Reseed(Item item, int value)
        {
            Mesh old = item.filter.sharedMesh;
            item.filter.sharedMesh = item.generate(value * 31 + items.IndexOf(item));
            Mesh oldWire = item.wire.sharedMesh;
            item.wire.sharedMesh = WireOf(item.filter.sharedMesh);
            if (old != null) Destroy(old);
            if (oldWire != null) Destroy(oldWire);
        }

        // Aristas de los triángulos como líneas (sin repetir las compartidas).
        static Mesh WireOf(Mesh source)
        {
            if (source == null) return null;
            int[] tris = source.triangles;
            var edges = new HashSet<long>();
            var indices = new List<int>(tris.Length * 2);
            for (int i = 0; i + 2 < tris.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[i + e], b = tris[i + (e + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!edges.Add(key)) continue;
                    indices.Add(a);
                    indices.Add(b);
                }
            var mesh = new Mesh { name = source.name + "_Wire", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = source.vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = source.bounds;
            return mesh;
        }

        void OnDestroy()
        {
            if (wireMaterial != null) Destroy(wireMaterial);
        }
    }
}
