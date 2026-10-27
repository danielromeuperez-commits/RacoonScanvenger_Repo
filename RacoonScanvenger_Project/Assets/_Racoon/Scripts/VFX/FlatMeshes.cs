using System.Collections.Generic;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Mallas generadas para los efectos planos (SG_VFX_ToonFlat). Datos en las UV (ver HLSL_ToonFlat):
    /// UV0 = (recorrido 0 base → 1 punta, aleatorio por pieza), UV1 = (1 en el contorno, degradado de color).
    ///  - Gráficos "billboard" (rayos, esquirlas, globos, destellos): plano XY, mirando a -Z (hacia la cámara
    ///    cuando el objeto se orienta con FlatFx.Billboard). El contorno va detrás (+Z) y SE AÑADE ANTES que el
    ///    relleno: en el overlay (sin profundidad) se pinta en ese orden.
    ///  - Cintas (remolinos de las estrellas de mareo): plano XZ, de doble cara.
    ///  - Objetos 3D (estrella, gota): volumen con normales suaves + cáscara de contorno (inverted hull).
    ///  - Con volumen para SG_VFX_ToonVolume (impacto y golpe al aire): pinchos, rayos en zigzag y bola de pinchos
    ///    facetados, con cáscara de contorno que no se abre en las aristas. UV2.x = radio del
    ///    tubo. (El tajo del golpe al aire se rehace cada frame en SwingVFX.)
    /// </summary>
    public static class FlatMeshes
    {
        const float OutlineDepth = 0.01f;

        // ---------------- Gráficos (plano XY) ----------------

        /// <summary>Rayos finos en forma de aguja (diamante alargado) saliendo del centro. Escala 1 = longitud máx.</summary>
        public static Mesh Rays(int seed, int count, float innerRadius, Vector2 length, Vector2 halfWidth, float shoulder = 0.22f)
        {
            System.Random random = new(seed);
            MeshBuilder b = new();
            for (int i = 0; i < count; i++)
            {
                float angle = (i + Range(random, -0.35f, 0.35f)) / count * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f), side = new(-dir.y, dir.x, 0f);
                float len = Range(random, length.x, length.y), w = Range(random, halfWidth.x, halfWidth.y);
                float rnd = (float)random.NextDouble();
                Vector3 mid = dir * (innerRadius + len * shoulder);
                int a = b.Add(dir * innerRadius, Vector3.back, 0f, rnd, 0f, 0f);
                int l = b.Add(mid + side * w, Vector3.back, shoulder, rnd, 0f, shoulder);
                int r = b.Add(mid - side * w, Vector3.back, shoulder, rnd, 0f, shoulder);
                int t = b.Add(dir * (innerRadius + len), Vector3.back, 1f, rnd, 0f, 1f);
                b.Tri(a, l, t, Vector3.back);
                b.Tri(a, t, r, Vector3.back);
            }
            return b.Build("FX_Rays");
        }

        /// <summary>
        /// Esquirlas de cómic (estilo Persona 5): cortes afilados con un quiebro (segments = 2) o dos, anchos en la
        /// base y en punta, con contorno. Escala 1 = longitud máx.
        /// </summary>
        public static Mesh Shards(int seed, int count, float innerRadius, Vector2 length, Vector2 halfWidth, float outline, int segments = 2)
        {
            System.Random random = new(seed);
            List<(Vector3 dir, Vector3 side, Vector3[] points, float[] widths, float rnd)> shards = new();
            float offsetAngle = Range(random, 0f, Mathf.PI * 2f);
            for (int i = 0; i < count; i++)
            {
                float angle = offsetAngle + (i + Range(random, -0.3f, 0.3f)) / count * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f), side = new(-dir.y, dir.x, 0f);
                float len = Range(random, length.x, length.y), w = Range(random, halfWidth.x, halfWidth.y);
                float zig = w * Range(random, 1f, 1.4f) * (random.Next(2) == 0 ? -1f : 1f);
                Vector3[] points = new Vector3[segments + 1];
                float[] widths = new float[segments + 1];
                for (int k = 0; k <= segments; k++)
                {
                    float t = k / (float)segments;
                    float lateral = k == 0 || k == segments ? 0f : (k % 2 == 1 ? zig : -zig) * Range(random, 0.7f, 1f) * (1f - t * 0.3f);
                    points[k] = dir * (innerRadius + len * t) + side * lateral;
                    widths[k] = w * Mathf.Pow(1f - t, 0.6f);
                }
                shards.Add((dir, side, points, widths, (float)random.NextDouble()));
            }

            MeshBuilder b = new();
            foreach (var s in shards) AddStrip(b, s.dir, s.side, s.points, s.widths, s.rnd, outline, true);
            foreach (var s in shards) AddStrip(b, s.dir, s.side, s.points, s.widths, s.rnd, 0f, false);
            return b.Build("FX_Shards");
        }

        static void AddStrip(MeshBuilder b, Vector3 dir, Vector3 side, Vector3[] points, float[] widths, float rnd, float grow, bool isOutline)
        {
            int count = points.Length;
            int[] left = new int[count], right = new int[count];
            Vector3 depth = isOutline ? Vector3.forward * OutlineDepth : Vector3.zero;
            for (int k = 0; k < count; k++)
            {
                float t = k / (count - 1f);
                Vector3 p = points[k];
                // El contorno sobresale también por la base y por la punta.
                if (isOutline && k == 0) p -= dir * grow;
                if (isOutline && k == count - 1) p += dir * grow * 2.2f;
                float w = widths[k] + grow;
                float outline = isOutline ? 1f : 0f;
                left[k] = b.Add(p + side * w + depth, Vector3.back, t, rnd, outline, t);
                right[k] = b.Add(p - side * w + depth, Vector3.back, t, rnd, outline, t);
            }
            for (int k = 0; k < count - 1; k++)
            {
                b.Tri(left[k], right[k], left[k + 1], Vector3.back);
                b.Tri(right[k], right[k + 1], left[k + 1], Vector3.back);
            }
        }

        /// <summary>
        /// Estrella dentada (puntas alternas largas / cortas) con contorno. Con muchas puntas es el globo de
        /// cómic; con 4 y muy estrechas, el destello. secondaryScale > 0 añade otra estrella girada media punta
        /// (destello de 8 puntas). Recorrido = distancia al centro (0-1): Shrink abre un agujero desde el centro.
        /// </summary>
        public static Mesh Burst(int seed, int points, float innerRatio, float jitter, float outline, float secondaryScale = 0f)
        {
            System.Random random = new(seed);
            List<(float angle, float radius)[]> stars = new() { StarOutline(random, points, innerRatio, jitter, 0f, 1f) };
            if (secondaryScale > 0f) stars.Add(StarOutline(random, points, innerRatio, jitter, Mathf.PI / points, secondaryScale));

            MeshBuilder b = new();
            foreach (var star in stars) AddFan(b, star, outline, true);
            foreach (var star in stars) AddFan(b, star, 0f, false);
            return b.Build("FX_Burst");
        }

        static (float angle, float radius)[] StarOutline(System.Random random, int points, float innerRatio, float jitter, float rotation, float scale)
        {
            var outline = new (float, float)[points * 2];
            for (int j = 0; j < outline.Length; j++)
            {
                float angle = rotation + (j + Range(random, -jitter, jitter) * 0.5f) / outline.Length * Mathf.PI * 2f + Mathf.PI * 0.5f;
                float radius = (j % 2 == 0 ? 1f : innerRatio) * Range(random, 1f - jitter, 1f + jitter * 0.3f) * scale;
                outline[j] = (angle, radius);
            }
            return outline;
        }

        static void AddFan(MeshBuilder b, (float angle, float radius)[] star, float grow, bool isOutline)
        {
            Vector3 depth = isOutline ? Vector3.forward * OutlineDepth : Vector3.zero;
            float outline = isOutline ? 1f : 0f;
            int center = b.Add(depth, Vector3.back, 0f, 0.5f, outline, 0f);
            int first = -1, previous = -1;
            foreach (var (angle, radius) in star)
            {
                float r = radius + grow;
                int index = b.Add(new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f) + depth, Vector3.back,
                    Mathf.Clamp01(radius), 0.5f, outline, Mathf.Clamp01(radius));
                if (previous >= 0) b.Tri(center, previous, index, Vector3.back);
                else first = index;
                previous = index;
            }
            b.Tri(center, previous, first, Vector3.back);
        }

        // ---------------- Cintas (plano XZ, doble cara) ----------------

        /// <summary>
        /// Arco (media luna) alrededor del origen, de fromDeg a toDeg (0 = +Z, 90 = +X). Grueso en el centro y
        /// en punta por los extremos (taper 0 = grosor constante). Recorrido = a lo largo del arco; degradado =
        /// del centro de la cinta (BaseColor) a los bordes (TipColor).
        /// </summary>
        public static Mesh Arc(float radius, float fromDeg, float toDeg, float halfWidth, float taper, int segments = 32)
        {
            MeshBuilder b = new();
            int[] inner = new int[segments + 1], middle = new int[segments + 1], outer = new int[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(fromDeg, toDeg, t) * Mathf.Deg2Rad;
                Vector3 dir = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                // sin(π) sale un pelín negativo y Pow daría NaN.
                float w = halfWidth * (taper <= 0f ? 1f : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), taper));
                inner[i] = b.Add(dir * (radius - w), Vector3.up, t, 0f, 0f, 1f);
                middle[i] = b.Add(dir * radius, Vector3.up, t, 0f, 0f, 0f);
                outer[i] = b.Add(dir * (radius + w), Vector3.up, t, 0f, 0f, 1f);
            }
            for (int i = 0; i < segments; i++)
            {
                b.TriBoth(inner[i], middle[i], inner[i + 1]);
                b.TriBoth(middle[i], middle[i + 1], inner[i + 1]);
                b.TriBoth(middle[i], outer[i], middle[i + 1]);
                b.TriBoth(outer[i], outer[i + 1], middle[i + 1]);
            }
            return b.Build("FX_Arc");
        }

        // ---------------- Objetos 3D ----------------

        /// <summary>
        /// Estrella "gordita" de 5 puntas (cojín: abombada por delante y por detrás) mirando a -Z, de diámetro
        /// ~1, con contorno (cáscara). Degradado = del centro (BaseColor) a las puntas (TipColor).
        /// </summary>
        public static Mesh Star(float innerRatio = 0.5f, float depth = 0.17f, float outline = 0.035f, int samples = 60, int rings = 4)
        {
            const float outer = 0.5f;
            List<Vector3> vertices = new();
            List<Vector2> uv0 = new(), uv1 = new();
            List<int> triangles = new();
            List<bool> rim = new();

            for (int side = -1; side <= 1; side += 2)
            {
                int center = vertices.Count;
                AddVertex(new Vector3(0f, 0f, depth * side), 0f, false);
                for (int k = 1; k <= rings; k++)
                {
                    float s = k / (float)rings;
                    for (int j = 0; j < samples; j++)
                    {
                        float theta = j / (float)samples * Mathf.PI * 2f;
                        float r = outer * (innerRatio + (1f - innerRatio) * Mathf.Pow(Mathf.Max(0f, 0.5f + 0.5f * Mathf.Cos(5f * theta)), 1.35f));
                        AddVertex(new Vector3(Mathf.Sin(theta) * r * s, Mathf.Cos(theta) * r * s, depth * side * Mathf.Sqrt(1f - s * s)), s, k == rings);
                    }
                }
                Vector3 facing = new(0f, 0f, side);
                for (int j = 0; j < samples; j++)
                {
                    int j1 = (j + 1) % samples;
                    AddTri(center, center + 1 + j, center + 1 + j1, facing);
                    for (int k = 1; k < rings; k++)
                    {
                        int a = center + 1 + (k - 1) * samples + j, b = center + 1 + (k - 1) * samples + j1;
                        int c = center + 1 + k * samples + j, d = center + 1 + k * samples + j1;
                        AddTri(a, c, b, facing);
                        AddTri(b, c, d, facing);
                    }
                }
            }
            return WithOutline("FX_Star", vertices, uv0, uv1, triangles, rim, outline);

            void AddVertex(Vector3 p, float gradient, bool isRim)
            {
                vertices.Add(p);
                uv0.Add(new Vector2(0.5f, 0f));
                uv1.Add(new Vector2(0f, gradient));
                rim.Add(isRim);
            }

            void AddTri(int a, int b, int c, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) < 0f) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        /// <summary>
        /// Gota de saliva: redonda delante (+Z, hacia donde vuela) y en punta detrás, de radio 0.5, con
        /// contorno. Degradado = de delante (BaseColor) a la cola (TipColor).
        /// </summary>
        public static Mesh Drop(float tail = 1.1f, float outline = 0.08f, int segments = 12)
        {
            const float radius = 0.5f;
            List<(float z, float r)> profile = new();
            for (int i = 0; i <= 5; i++)
            {
                float phi = i / 5f * Mathf.PI * 0.5f;
                profile.Add((radius * Mathf.Cos(phi), radius * Mathf.Sin(phi)));
            }
            for (int i = 1; i <= 5; i++)
            {
                float t = i / 5f;
                profile.Add((-tail * radius * t, radius * Mathf.Pow(1f - t, 1.3f)));
            }

            List<Vector3> vertices = new();
            List<Vector2> uv0 = new(), uv1 = new();
            List<int> triangles = new();
            List<bool> rim = new();
            foreach (var (z, r) in profile)
            {
                for (int s = 0; s < segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                    uv0.Add(new Vector2(0.5f, 0f));
                    uv1.Add(new Vector2(0f, Mathf.InverseLerp(radius, -tail * radius, z)));
                    rim.Add(false);
                }
            }
            for (int i = 0; i < profile.Count - 1; i++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    int a = i * segments + s, b = i * segments + s1, c = (i + 1) * segments + s, d = (i + 1) * segments + s1;
                    AddQuadTri(a, c, b);
                    AddQuadTri(b, c, d);
                }
            }
            return WithOutline("FX_Drop", vertices, uv0, uv1, triangles, rim, outline);

            void AddQuadTri(int a, int b, int c)
            {
                Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                Vector3 facing = new(centroid.x, centroid.y, centroid.z + radius * 0.3f);
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) < 0f) (b, c) = (c, b);
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < 1e-12f) return;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        /// <summary>Salpicadura en el suelo (plano XZ, mirando a +Y): mancha irregular y gotitas, con contorno debajo.</summary>
        public static Mesh Splat(int seed, float outline = 0.07f)
        {
            System.Random random = new(seed);
            List<(Vector2 center, float radius, int points, float bumps)> blobs = new() { (Vector2.zero, 0.38f, 20, 0.22f) };
            int drops = 3 + random.Next(2);
            for (int i = 0; i < drops; i++)
            {
                float angle = Range(random, 0f, Mathf.PI * 2f);
                blobs.Add((new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(random, 0.42f, 0.52f), Range(random, 0.05f, 0.09f), 10, 0f));
            }

            MeshBuilder b = new();
            for (int pass = 0; pass < 2; pass++)
            {
                bool isOutline = pass == 0;
                foreach (var blob in blobs)
                {
                    System.Random shape = new(seed * 31 + blob.points);
                    float y = isOutline ? -0.004f : 0f;
                    int center = b.Add(new Vector3(blob.center.x, y, blob.center.y), Vector3.up, 0.5f, 0f, isOutline ? 1f : 0f, 0f);
                    int first = -1, previous = -1;
                    for (int j = 0; j < blob.points; j++)
                    {
                        float angle = j / (float)blob.points * Mathf.PI * 2f;
                        float r = blob.radius * (1f + blob.bumps * (float)(shape.NextDouble() * 2.0 - 1.0)) + (isOutline ? outline * 0.5f : 0f);
                        int index = b.Add(new Vector3(blob.center.x + Mathf.Cos(angle) * r, y, blob.center.y + Mathf.Sin(angle) * r),
                            Vector3.up, 0.5f, 0f, isOutline ? 1f : 0f, 1f);
                        if (previous >= 0) b.Tri(center, previous, index, Vector3.up);
                        else first = index;
                        previous = index;
                    }
                    b.Tri(center, previous, first, Vector3.up);
                }
            }
            return b.Build("FX_Splat");
        }

        // ---------------- Con volumen (SG_VFX_ToonVolume) ----------------
        // Además de UV0 / UV1, UV2.x = radio del tubo: el shader lo adelgaza con Thickness.

        /// <summary>
        /// Pinchos facetados (<paramref name="sides"/> caras planas) que salen del centro repartidos entre dos conos
        /// alrededor de +Z (de minDeg a maxDeg: 0 = rectos hacia +Z, 90 = tumbados en el plano XY), con contorno.
        /// shoulder = dónde son más anchos (0 = en la base: aguja; 0.3 = rombo); flatten aplasta la sección hacia +Z (1 =
        /// regular, menos = cuchilla plana que se ve de frente). alternate = uno largo y otro corto (estrella de manga).
        /// hollow = radio del hueco del centro: cada pincho nace apartado del eje Z esa distancia (0 = todos desde el centro).
        /// Recorrido = de la base (0) a la punta (1), cada uno a su ritmo (Spread). Degradado = de la base a la punta.
        /// Escala 1 = longitud máx.
        /// </summary>
        public static Mesh Spikes(int seed, int count, float minDeg, float maxDeg, float innerRadius, Vector2 length, Vector2 halfWidth,
            float outline, float shoulder = 0.28f, float flatten = 0.65f, int sides = 5, bool alternate = false, float hollow = 0f)
        {
            System.Random random = new(seed);
            VolumeBuilder b = new();
            float cosMin = Mathf.Cos(minDeg * Mathf.Deg2Rad), cosMax = Mathf.Cos(maxDeg * Mathf.Deg2Rad);
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float spin = Range(random, 0f, Mathf.PI * 2f);
            Vector3[] ring = new Vector3[sides];
            for (int i = 0; i < count; i++)
            {
                // Repartidos por la franja (espiral áurea, área uniforme) con un poco de azar. En la estrella, en orden
                // alrededor para que se alternen de verdad.
                float h = Mathf.Lerp(cosMin, cosMax, (i + Range(random, 0.2f, 0.8f)) / count);
                float azimuth = alternate ? spin + i * Mathf.PI * 2f / count + Range(random, -0.12f, 0.12f) : spin + i * golden + Range(random, -0.3f, 0.3f);
                if (alternate) h = Mathf.Lerp(cosMin, cosMax, Range(random, 0.3f, 0.7f));
                float theta = Mathf.Acos(h);
                Vector3 dir = new(Mathf.Sin(theta) * Mathf.Cos(azimuth), Mathf.Sin(theta) * Mathf.Sin(azimuth), h);
                // side1 tangente alrededor de Z, side2 hacia +Z: flatten aplasta hacia la cara que mira a +Z.
                Vector3 side1 = Vector3.Cross(Vector3.forward, dir);
                side1 = side1.sqrMagnitude > 1e-4f ? side1.normalized : Vector3.right;
                Vector3 side2 = Vector3.Cross(dir, side1);
                float len = Range(random, length.x, length.y) * (alternate && i % 2 == 1 ? 0.55f : 1f);
                float w = Range(random, halfWidth.x, halfWidth.y) * (alternate && i % 2 == 1 ? 0.8f : 1f);
                float rnd = (float)random.NextDouble(), twist = Range(random, 0f, Mathf.PI * 2f) * (flatten < 0.99f ? 0f : 1f);

                Vector3 offset = HollowOffset(dir, azimuth, hollow);
                Vector3 basePoint = offset + dir * innerRadius, tip = offset + dir * (innerRadius + len);
                for (int s = 0; s < sides; s++)
                {
                    float a = twist + s / (float)sides * Mathf.PI * 2f;
                    ring[s] = offset + dir * (innerRadius + len * shoulder) + (side1 * Mathf.Cos(a) + side2 * (Mathf.Sin(a) * flatten)) * w;
                }
                for (int s = 0; s < sides; s++)
                {
                    // Hacia fuera de su propio eje (el del pincho, que con hueco no pasa por el origen).
                    Vector3 r0 = ring[s], r1 = ring[(s + 1) % sides];
                    b.FacetToward(basePoint, r0, r1, 0f, shoulder, shoulder, rnd, AwayFromAxis(offset, dir, basePoint, r0, r1));
                    b.FacetToward(r0, tip, r1, shoulder, 1f, shoulder, rnd, AwayFromAxis(offset, dir, r0, tip, r1));
                }
            }
            return b.Build("FX_Spikes", outline);
        }

        // Cara hacia fuera del eje que pasa por origin en dirección axis.
        static Vector3 AwayFromAxis(Vector3 origin, Vector3 axis, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 centroid = (p0 + p1 + p2) / 3f - origin;
            return centroid - axis * Vector3.Dot(centroid, axis);
        }

        // Con hueco en el centro: hacia fuera del eje Z (por su azimut si sale recto).
        static Vector3 HollowOffset(Vector3 dir, float azimuth, float hollow)
        {
            if (hollow <= 0f) return Vector3.zero;
            Vector3 radial = new(dir.x, dir.y, 0f);
            if (radial.sqrMagnitude < 1e-4f) radial = new Vector3(Mathf.Cos(azimuth), Mathf.Sin(azimuth), 0f);
            return radial.normalized * hollow;
        }

        /// <summary>
        /// Rayos en zigzag facetados (sección de rombo) que salen del centro repartidos por un cono alrededor de +Z, cada
        /// uno de <paramref name="zigs"/> tramos que van y vienen de lado y adelgazan hasta acabar en punta, con contorno.
        /// zigAmount = cuánto se tuercen (respecto a su largo). Recorrido = de la base (0) a la punta (1), cada uno a su
        /// ritmo (Spread). Degradado = de la base a la punta. hollow = radio del hueco del centro (como en Spikes).
        /// Escala 1 = longitud máx.
        /// </summary>
        public static Mesh Bolts(int seed, int count, float coneDeg, float innerRadius, Vector2 length, float halfWidth, int zigs,
            float zigAmount, float outline, float hollow = 0f)
        {
            const int Sides = 4;
            System.Random random = new(seed);
            VolumeBuilder b = new();
            float cosCone = Mathf.Cos(coneDeg * Mathf.Deg2Rad);
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float spin = Range(random, 0f, Mathf.PI * 2f);
            zigs = Mathf.Max(1, zigs);
            Vector3[] spine = new Vector3[zigs + 1];
            Vector3[,] ring = new Vector3[zigs + 1, Sides];
            for (int i = 0; i < count; i++)
            {
                float h = Mathf.Lerp(1f, cosCone, (i + Range(random, 0.2f, 0.8f)) / count);
                float theta = Mathf.Acos(h), azimuth = spin + i * golden + Range(random, -0.3f, 0.3f);
                Vector3 dir = new(Mathf.Sin(theta) * Mathf.Cos(azimuth), Mathf.Sin(theta) * Mathf.Sin(azimuth), h);
                Vector3 side1 = Vector3.Cross(Vector3.forward, dir);
                side1 = side1.sqrMagnitude > 1e-4f ? side1.normalized : Vector3.right;
                Vector3 side2 = Vector3.Cross(dir, side1);
                float len = Range(random, length.x, length.y), rnd = (float)random.NextDouble();
                float zigSign = random.NextDouble() < 0.5 ? -1f : 1f;

                // El zigzag va de lado (side1); la punta vuelve al centro.
                for (int k = 0; k <= zigs; k++)
                {
                    float t = k / (float)zigs;
                    float lateral = k == 0 || k == zigs ? 0f : zigSign * (k % 2 == 1 ? 1f : -1f) * zigAmount * len * Range(random, 0.7f, 1.2f);
                    spine[k] = HollowOffset(dir, azimuth, hollow) + dir * (innerRadius + len * t) + side1 * lateral;
                }
                for (int k = 0; k <= zigs; k++)
                {
                    float t = k / (float)zigs;
                    Vector3 tangent = (spine[Mathf.Min(k + 1, zigs)] - spine[Mathf.Max(k - 1, 0)]).normalized;
                    Vector3 across = Vector3.Cross(side2, tangent).normalized;
                    float w = k == zigs ? 0f : halfWidth * Mathf.Lerp(1f, 0.45f, t);
                    ring[k, 0] = spine[k] + across * w;
                    ring[k, 1] = spine[k] + side2 * (w * 0.6f);
                    ring[k, 2] = spine[k] - across * w;
                    ring[k, 3] = spine[k] - side2 * (w * 0.6f);
                }
                for (int s = 0; s < Sides; s++)
                {
                    int s1 = (s + 1) % Sides;
                    // Tapa de la base.
                    b.FacetToward(spine[0], ring[0, s], ring[0, s1], 0f, 0f, 0f, rnd, -dir);
                    for (int k = 0; k < zigs; k++)
                    {
                        float t0 = k / (float)zigs, t1 = (k + 1) / (float)zigs;
                        Vector3 facing = (ring[k, s] + ring[k, s1] + ring[k + 1, s] + ring[k + 1, s1]) * 0.25f - (spine[k] + spine[k + 1]) * 0.5f;
                        b.FacetToward(ring[k, s], ring[k + 1, s], ring[k, s1], t0, t1, t0, rnd, facing);
                        b.FacetToward(ring[k, s1], ring[k + 1, s], ring[k + 1, s1], t0, t1, t1, rnd, facing);
                    }
                }
            }
            return b.Build("FX_Bolts", outline);
        }

        /// <summary>
        /// Bola de pinchos facetada (icosaedro con sus 12 vértices estirados), de diámetro 1 contando los pinchos, con
        /// contorno. Degradado = del cuerpo (BaseColor) a las puntas (TipColor).
        /// </summary>
        public static Mesh SpikeBall(float body = 0.55f, float outline = 0.04f)
        {
            Mesh sphere = ToonSmoke.CreateSphere(1);
            Vector3[] sphereVertices = sphere.vertices;
            int[] sphereTriangles = sphere.triangles;
            Object.Destroy(sphere);

            VolumeBuilder b = new();
            for (int i = 0; i < sphereTriangles.Length; i += 3)
            {
                // Los 12 primeros vértices son los del icosaedro: las puntas.
                int ia = sphereTriangles[i], ib = sphereTriangles[i + 1], ic = sphereTriangles[i + 2];
                b.Facet(Point(ia), Point(ib), Point(ic), 0.5f, 0.5f, 0.5f, 0f, Vector3.zero, Tip(ia), Tip(ib), Tip(ic));
            }
            return b.Build("FX_SpikeBall", outline);

            Vector3 Point(int i) => sphereVertices[i].normalized * (i < 12 ? 0.5f : 0.5f * body);
            float Tip(int i) => i < 12 ? 1f : 0f;
        }

        // Volumen + cáscara de contorno: la misma malla inflada por las normales con las caras al revés. Con
        // backface culling solo se ve la parte de detrás de la cáscara, asomando alrededor de la silueta.
        static Mesh WithOutline(string name, List<Vector3> vertices, List<Vector2> uv0, List<Vector2> uv1, List<int> triangles, List<bool> rim, float outline)
        {
            Mesh body = new();
            body.SetVertices(vertices);
            body.SetTriangles(triangles, 0);
            body.RecalculateNormals();
            Vector3[] normals = body.normals;
            Object.Destroy(body);

            int count = vertices.Count;
            List<Vector3> allNormals = new(normals);
            for (int i = 0; i < count; i++)
            {
                // En el borde de la estrella delante y detrás no comparten vértices: hacia fuera en el plano para
                // que la cáscara no se abra por la costura.
                Vector3 push = rim[i] ? new Vector3(vertices[i].x, vertices[i].y, 0f).normalized : normals[i];
                vertices.Add(vertices[i] + push * outline);
                allNormals.Add(normals[i]);
                uv0.Add(uv0[i]);
                uv1.Add(new Vector2(1f, uv1[i].y));
            }
            int triangleCount = triangles.Count;
            for (int i = 0; i < triangleCount; i += 3)
            {
                triangles.Add(triangles[i] + count);
                triangles.Add(triangles[i + 2] + count);
                triangles.Add(triangles[i + 1] + count);
            }

            Mesh mesh = new() { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(allNormals);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices = new(), normals = new();
            readonly List<Vector2> uv0 = new(), uv1 = new();
            readonly List<int> triangles = new();

            public int Add(Vector3 position, Vector3 normal, float along, float random, float outline, float gradient)
            {
                vertices.Add(position);
                normals.Add(normal);
                uv0.Add(new Vector2(along, random));
                uv1.Add(new Vector2(outline, gradient));
                return vertices.Count - 1;
            }

            // Cara visible hacia "facing" (en Unity: cross(b-a, c-a)).
            public void Tri(int a, int b, int c, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) < 0f) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public void TriBoth(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
            }

            public Mesh Build(string name)
            {
                Mesh mesh = new() { name = name };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // Mallas con volumen: UV2 (radio del tubo), caras facetadas y cáscara de contorno que no se abre en las aristas.
        sealed class VolumeBuilder
        {
            readonly List<Vector3> vertices = new(), normals = new();
            readonly List<Vector2> uv0 = new(), uv1 = new(), uv2 = new();
            readonly List<int> triangles = new();

            public int Count => vertices.Count;

            public int Add(Vector3 position, Vector3 normal, float along, float random, float gradient, float tube = 0f)
            {
                vertices.Add(position);
                normals.Add(normal);
                uv0.Add(new Vector2(along, random));
                uv1.Add(new Vector2(0f, gradient));
                uv2.Add(new Vector2(tube, 0f));
                return vertices.Count - 1;
            }

            // Cara visible hacia donde miran las normales de sus vértices (superficies suaves).
            public void TriSmooth(int a, int b, int c)
            {
                Vector3 facing = normals[a] + normals[b] + normals[c];
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), facing) < 0f) (b, c) = (c, b);
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            /// <summary>
            /// Cara plana con sus propios vértices y la normal de la cara (facetado), hacia fuera del eje
            /// <paramref name="axis"/> que pasa por el origen (cero = hacia fuera del origen). Degradado = recorrido, o
            /// los que se den.
            /// </summary>
            public void Facet(Vector3 a, Vector3 b, Vector3 c, float alongA, float alongB, float alongC, float random, Vector3 axis,
                float gradientA = -1f, float gradientB = -1f, float gradientC = -1f)
            {
                Vector3 centroid = (a + b + c) / 3f;
                Vector3 facing = axis == Vector3.zero ? centroid : centroid - axis * Vector3.Dot(centroid, axis);
                FacetToward(a, b, c, alongA, alongB, alongC, random, facing, gradientA, gradientB, gradientC);
            }

            /// <summary>Cara plana como <see cref="Facet"/>, visible hacia <paramref name="facing"/>.</summary>
            public void FacetToward(Vector3 a, Vector3 b, Vector3 c, float alongA, float alongB, float alongC, float random, Vector3 facing,
                float gradientA = -1f, float gradientB = -1f, float gradientC = -1f)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-14f) return;
                if (Vector3.Dot(normal, facing) < 0f)
                {
                    (b, c) = (c, b);
                    (alongB, alongC) = (alongC, alongB);
                    (gradientB, gradientC) = (gradientC, gradientB);
                    normal = -normal;
                }
                normal.Normalize();
                triangles.Add(Add(a, normal, alongA, random, gradientA < 0f ? alongA : gradientA));
                triangles.Add(Add(b, normal, alongB, random, gradientB < 0f ? alongB : gradientB));
                triangles.Add(Add(c, normal, alongC, random, gradientC < 0f ? alongC : gradientC));
            }

            /// <summary>
            /// Con outline > 0 añade la cáscara de contorno: la malla inflada por la normal suave (media de las de los
            /// vértices que están en el mismo sitio, así las aristas de las caras planas no la abren), con las caras al
            /// revés. Con backface culling solo asoma alrededor de la silueta.
            /// </summary>
            public Mesh Build(string name, float outline)
            {
                int count = vertices.Count;
                if (outline > 0f)
                {
                    Dictionary<Vector3Int, Vector3> smooth = new();
                    for (int i = 0; i < count; i++)
                    {
                        Vector3Int key = Key(vertices[i]);
                        smooth[key] = (smooth.TryGetValue(key, out Vector3 sum) ? sum : Vector3.zero) + normals[i];
                    }
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 push = smooth[Key(vertices[i])];
                        push = push.sqrMagnitude > 1e-10f ? push.normalized : normals[i];
                        vertices.Add(vertices[i] + push * outline);
                        normals.Add(normals[i]);
                        uv0.Add(uv0[i]);
                        uv1.Add(new Vector2(1f, uv1[i].y));
                        uv2.Add(uv2[i]);
                    }
                    int triangleCount = triangles.Count;
                    for (int i = 0; i < triangleCount; i += 3)
                    {
                        triangles.Add(triangles[i] + count);
                        triangles.Add(triangles[i + 2] + count);
                        triangles.Add(triangles[i + 1] + count);
                    }
                }

                Mesh mesh = new() { name = name };
                if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetUVs(2, uv2);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;

                static Vector3Int Key(Vector3 p) => new(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));
            }
        }
    }
}
