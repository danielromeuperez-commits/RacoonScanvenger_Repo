using System.Collections.Generic;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Humo cartoon (Shader Graph SG_VFX_ToonSmoke) compartido por pasos, dash, knockback y aliento.
    /// Crea el sistema de partículas ya configurado (mallas de nubes de burbujas y los datos por partícula
    /// que necesita el shader) y le pone color por MaterialPropertyBlock (cada sistema el suyo, sin tocar el
    /// material).
    /// </summary>
    public static class ToonSmoke
    {
        /// <summary>Propiedad del shader de humo: si el material la tiene, el humo se rompe en bolitas solo.</summary>
        public const string BreakUpProperty = "_DissolveStart";

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static Mesh[] cloudMeshes;
        static Mesh[] coneMeshes;

        /// <summary>Nubes redondas (3 variantes): una burbuja grande y varias pequeñas alrededor.</summary>
        public static Mesh[] CloudMeshes
        {
            get
            {
                if (cloudMeshes == null || cloudMeshes[0] == null)
                    cloudMeshes = new[] { CreateCloudMesh(11), CreateCloudMesh(23), CreateCloudMesh(37) };
                return cloudMeshes;
            }
        }

        /// <summary>Nubes en cono a lo largo de +Z (3 variantes): estrechas atrás y anchas delante (aliento).</summary>
        public static Mesh[] ConeMeshes
        {
            get
            {
                if (coneMeshes == null || coneMeshes[0] == null)
                    coneMeshes = new[] { CreateConeMesh(5), CreateConeMesh(17), CreateConeMesh(29) };
                return coneMeshes;
            }
        }

        /// <summary>
        /// Sistema de puffs volumétricos: crecen rápido, frenan y se encogen / se rompen hasta desaparecer.
        /// Se emite con EmitParams (sin emisión propia). randomYaw: cada puff girado al azar en vertical y
        /// rotando despacio; si no, la rotación la da quien emite (EmitParams.rotation3D, p. ej. el aliento).
        /// </summary>
        public static ParticleSystem CreateSystem(string name, Transform parent, Material material, Mesh[] meshes,
            bool randomYaw = true, int maxParticles = 128)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 0.7f;
            main.startRotation3D = true;
            if (randomYaw)
            {
                // La burbuja grande siempre queda arriba: solo giro en vertical y una pizca de inclinación.
                main.startRotationX = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            }
            main.gravityModifier = -0.05f; // Sube un poquito.
            main.maxParticles = maxParticles;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            // Con el shader de humo (se rompe solo en bolitas) no hace falta encogerse del todo.
            bool breaksUp = material != null && material.HasProperty(BreakUpProperty);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.18f, 1f), new Keyframe(0.6f, 0.9f), new Keyframe(1f, breaksUp ? 0.8f : 0f)));

            ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = 4f; // Salen rápido y frenan.

            if (randomYaw)
            {
                ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                rotation.x = 0f;
                rotation.y = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
                rotation.z = 0f;
            }

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            if (meshes.Length == 1)
            {
                renderer.mesh = meshes[0];
            }
            else
            {
                renderer.SetMeshes(meshes);
                renderer.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
            }
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            ConfigureStreams(system);

            system.Play();
            return system;
        }

        /// <summary>
        /// Datos por partícula que usa el shader de humo: centro, edad, semilla y tamaño.
        ///   TEXCOORD0 = (uv.x, uv.y, center.x, center.y)   TEXCOORD1 = (center.z, agePercent, random, size)
        /// El color NO va por partícula (el COLOR de las partículas de malla llegaba mal al shader y oscurecía el
        /// humo): va por MaterialPropertyBlock, ver <see cref="ApplyColor"/>. Con otros materiales no molesta.
        /// </summary>
        public static void ConfigureStreams(ParticleSystem system)
        {
            system.GetComponent<ParticleSystemRenderer>().SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Normal,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.Center,
                ParticleSystemVertexStream.AgePercent,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.SizeX,
            });
        }

        /// <summary>
        /// Color del humo de ese sistema (sin tocar el asset): _BaseColor en SG_VFX_ToonSmoke, URP y FlatKit;
        /// _Color en shaders antiguos. Cambia también las partículas que ya existen.
        /// </summary>
        public static void ApplyColor(ParticleSystem system, ref MaterialPropertyBlock block, Color color)
        {
            if (system == null) return;
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            system.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
        }

        // ---------------- Mallas ----------------

        // Nube como las del humo dibujado: una burbuja grande, 3-4 más pequeñas solapadas alrededor (algo
        // más bajas) y una encima.
        static Mesh CreateCloudMesh(int seed)
        {
            System.Random random = new(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            List<(Vector3 center, float radius, float order)> bubbles = new() { (new Vector3(0f, 0.02f, 0f), 0.3f, 1f) };
            int ring = 3 + random.Next(2);
            float start = Range(0f, 360f);
            for (int i = 0; i < ring; i++)
            {
                float angle = (start + i * 360f / ring + Range(-20f, 20f)) * Mathf.Deg2Rad;
                float distance = Range(0.22f, 0.3f);
                Vector3 center = new(Mathf.Cos(angle) * distance, Range(-0.1f, 0.05f), Mathf.Sin(angle) * distance);
                bubbles.Add((center, Range(0.15f, 0.22f), Range(0f, 0.8f)));
            }
            bubbles.Add((new Vector3(Range(-0.08f, 0.08f), Range(0.2f, 0.26f), Range(-0.08f, 0.08f)), Range(0.16f, 0.2f), Range(0.3f, 0.9f)));
            return BuildBubbles($"DustCloud_{seed}", bubbles);
        }

        // Cono a lo largo de +Z: burbujas cada vez más grandes hacia delante (las de atrás, pegadas a la boca,
        // se deshacen antes) y un par a los lados del extremo ancho para que no quede como una salchicha.
        static Mesh CreateConeMesh(int seed)
        {
            System.Random random = new(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            List<(Vector3 center, float radius, float order)> bubbles = new();
            const int count = 5;
            for (int i = 0; i < count; i++)
            {
                float t = i / (count - 1f);
                Vector3 center = new(Range(-0.03f, 0.03f), Range(-0.02f, 0.03f), Mathf.Lerp(-0.42f, 0.3f, t));
                bubbles.Add((center, Mathf.Lerp(0.07f, 0.21f, t) * Range(0.9f, 1.1f), Mathf.Lerp(0.1f, 0.9f, t) + Range(-0.1f, 0.1f)));
            }
            for (int side = -1; side <= 1; side += 2)
                bubbles.Add((new Vector3(side * Range(0.1f, 0.14f), Range(-0.02f, 0.06f), Range(0.12f, 0.24f)), Range(0.11f, 0.15f), Range(0.4f, 0.8f)));
            return BuildBubbles($"BreathCone_{seed}", bubbles);
        }

        // Une las burbujas en una malla que cabe en una esfera de diámetro 1 (startSize = tamaño del puff).
        // UV0 de cada vértice = (orden de la burbuja, radio de la burbuja): el shader de humo usa el orden para
        // decidir cuándo se deshace cada una y el radio para saber dónde está su centro.
        static Mesh BuildBubbles(string name, List<(Vector3 center, float radius, float order)> bubbles)
        {
            float extent = 0f;
            foreach (var bubble in bubbles) extent = Mathf.Max(extent, bubble.center.magnitude + bubble.radius);
            float scale = 0.5f / extent;

            Mesh sphere = CreateSphere(2);
            Vector3[] sphereVertices = sphere.vertices;
            int[] sphereTriangles = sphere.triangles;
            Object.Destroy(sphere);

            List<Vector3> vertices = new(), normals = new();
            List<Vector2> uvs = new();
            List<Color> colors = new();
            List<int> triangles = new();
            foreach (var (center, radius, order) in bubbles)
            {
                int offset = vertices.Count;
                foreach (Vector3 vertex in sphereVertices)
                {
                    Vector3 direction = vertex.normalized;
                    vertices.Add((center + direction * radius) * scale);
                    normals.Add(direction);
                    uvs.Add(new Vector2(Mathf.Clamp01(order), radius * scale));
                    colors.Add(Color.white);
                }
                foreach (int index in sphereTriangles) triangles.Add(offset + index);
            }

            Mesh mesh = new() { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Esfera (icosaedro subdividido) de diámetro 1 con normales suaves.</summary>
        public static Mesh CreateSphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            List<Vector3> vertices = new()
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized * 0.5f;
            List<int> faces = new()
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            Dictionary<long, int> midpoints = new();
            for (int level = 0; level < subdivisions; level++)
            {
                List<int> next = new();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = next;
            }

            // Todas las caras hacia fuera (cara visible en Unity = cross(b-a, c-a)).
            for (int i = 0; i < faces.Count; i += 3)
            {
                Vector3 a = vertices[faces[i]], b = vertices[faces[i + 1]], c = vertices[faces[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) (faces[i + 1], faces[i + 2]) = (faces[i + 2], faces[i + 1]);
            }

            Mesh mesh = new() { name = "Sphere" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(faces, 0);
            Vector3[] normals = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++) normals[i] = vertices[i].normalized;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;

            int Midpoint(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midpoints.TryGetValue(key, out int index)) return index;
                vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized * 0.5f);
                midpoints[key] = vertices.Count - 1;
                return vertices.Count - 1;
            }
        }
    }
}
