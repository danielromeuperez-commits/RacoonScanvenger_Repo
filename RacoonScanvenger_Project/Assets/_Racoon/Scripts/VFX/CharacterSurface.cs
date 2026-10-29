using System.Collections.Generic;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// La superficie real del personaje (sus mallas) para colocar efectos SOBRE ella: dónde entra un golpe, la
    /// boca para el aliento, la cabeza para el sudor...
    /// Se recogen al crearse los MeshRenderer / SkinnedMeshRenderer activos del personaje, sin los de los efectos
    /// (VfxBehaviour); lo que se añada después (objetos en la mano, huellas) no cuenta.
    /// Las mallas necesitan Read/Write activado en el Import Settings del modelo; sin malla legible se usa el
    /// elipsoide de la caja del personaje.
    /// </summary>
    public sealed class CharacterSurface
    {
        sealed class MeshData
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public int[] triangles;
            public Vector3 min, max;
            // Para elegir puntos al azar ponderados por área (en espacio de la malla).
            public float[] cumulativeArea;
            public float totalArea;
        }

        sealed class Part
        {
            public Renderer renderer;
            public SkinnedMeshRenderer skinned;
            public MeshData data;
            public Mesh baked;
            public int bakedFrame = -1;
        }

        static readonly Dictionary<Mesh, MeshData> meshCache = new();
        static readonly HashSet<Mesh> warned = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            meshCache.Clear();
            warned.Clear();
        }

        readonly Transform root;
        readonly List<Part> parts = new();
        float totalArea;

        public CharacterSurface(Transform root)
        {
            this.root = root;
            foreach (Renderer rend in root.GetComponentsInChildren<Renderer>())
            {
                if (rend is not (MeshRenderer or SkinnedMeshRenderer)) continue;
                if (rend.GetComponentInParent<VfxBehaviour>() != null) continue;

                Part part = new() { renderer = rend };
                if (rend is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null || !Readable(skinned.sharedMesh, rend)) continue;
                    part.skinned = skinned;
                    part.baked = new Mesh { name = "CharacterSurface_Baked" };
                    part.data = new MeshData { triangles = skinned.sharedMesh.triangles };
                    Bake(part);
                }
                else
                {
                    if (!rend.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                    if (!Readable(filter.sharedMesh, rend)) continue;
                    part.data = Load(filter.sharedMesh);
                }
                parts.Add(part);
                totalArea += part.data.totalArea;
            }
        }

        /// <summary>
        /// Un punto pegado a la malla (triángulo + coordenadas dentro de él): sigue a la malla aunque se anime. Ver
        /// <see cref="Evaluate"/>.
        /// </summary>
        public readonly struct Anchor
        {
            internal readonly int part, triangle;
            internal readonly float u, v;
            // La normal de la malla en ese punto mira hacia dentro: darle la vuelta.
            internal readonly bool flip;

            internal Anchor(int part, int triangle, float u, float v, bool flip)
            {
                this.part = part;
                this.triangle = triangle;
                this.u = u;
                this.v = v;
                this.flip = flip;
            }

            public static Anchor None => new(-1, -1, 0f, 0f, false);
            public bool Valid => part >= 0;
        }

        /// <summary>Hay malla de verdad (si no, todo va con el elipsoide de la caja).</summary>
        public bool HasMesh => parts.Count > 0;

        public void Dispose()
        {
            foreach (Part part in parts)
                if (part.baked != null) Object.Destroy(part.baked);
            parts.Clear();
        }

        // ---------------- Consultas ----------------

        /// <summary>
        /// Punto de la malla donde entra un rayo que viene de fuera: el primero que toca o, si no toca nada, el más
        /// cercano al rayo. La normal mira hacia el origen del rayo.
        /// </summary>
        public void FindEntry(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal) =>
            FindEntry(origin, direction, maxDistance, out point, out normal, out _);

        /// <summary>Como el otro FindEntry, y además el punto pegado a la malla (si el rayo la toca).</summary>
        public void FindEntry(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out Anchor anchor)
        {
            if (Raycast(origin, direction, maxDistance, out point, out normal, out anchor)) return;
            anchor = Anchor.None;
            if (NearestToRay(origin, direction, out point, out normal)) return;
            EllipsoidEntry(origin, direction, out point, out normal);
        }

        /// <summary>Primer punto de la malla en el rayo (dirección en mundo). La normal mira hacia el origen.</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal) =>
            Raycast(origin, direction, maxDistance, out point, out normal, out _);

        bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out Vector3 point, out Vector3 normal, out Anchor anchor)
        {
            point = normal = Vector3.zero;
            anchor = Anchor.None;
            if (direction.sqrMagnitude < 1e-10f) return false;
            direction.Normalize();

            float best = maxDistance;
            Part bestPart = null;
            int bestTriangle = -1;
            float bestU = 0f, bestV = 0f;
            foreach (Part part in parts)
            {
                MeshData data = Data(part);
                if (data == null) continue;
                Matrix4x4 toLocal = LocalToWorld(part).inverse;
                // Recta en el espacio de la malla: el parámetro t sigue siendo la distancia en mundo.
                Vector3 o = toLocal.MultiplyPoint3x4(origin), d = toLocal.MultiplyVector(direction);
                if (!RayBox(data.min, data.max, o, d, best)) continue;

                Vector3[] v = data.vertices;
                int[] tris = data.triangles;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    if (!RayTriangle(o, d, v[tris[i]], v[tris[i + 1]], v[tris[i + 2]], best, out float t, out float u, out float w)) continue;
                    best = t;
                    bestPart = part;
                    bestTriangle = i;
                    bestU = u;
                    bestV = w;
                }
            }
            if (bestPart == null) return false;

            point = origin + direction * best;
            normal = TriangleNormal(bestPart, bestTriangle, bestU, bestV);
            bool flip = Vector3.Dot(normal, direction) > 0f;
            if (flip) normal = -normal;
            anchor = new Anchor(parts.IndexOf(bestPart), bestTriangle, bestU, bestV, flip);
            return true;
        }

        /// <summary>Dónde está ahora (en mundo) un punto pegado a la malla, con su normal hacia fuera.</summary>
        public bool Evaluate(Anchor anchor, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (!anchor.Valid || anchor.part >= parts.Count) return false;
            Part part = parts[anchor.part];
            MeshData data = Data(part);
            if (data == null || anchor.triangle + 2 >= data.triangles.Length) return false;
            int[] tris = data.triangles;
            Vector3 a = data.vertices[tris[anchor.triangle]], b = data.vertices[tris[anchor.triangle + 1]], c = data.vertices[tris[anchor.triangle + 2]];
            point = LocalToWorld(part).MultiplyPoint3x4(a * (1f - anchor.u - anchor.v) + b * anchor.u + c * anchor.v);
            normal = TriangleNormal(part, anchor.triangle, anchor.u, anchor.v);
            if (anchor.flip) normal = -normal;
            return true;
        }

        /// <summary>
        /// Punto al azar de la superficie (ponderado por área) por encima de <paramref name="minHeight"/> (0 = pies,
        /// 1 = lo más alto del personaje) y con la normal hacia <paramref name="toward"/> (dot >= minDot). Si ninguno
        /// cumple tras varios intentos, el que más se acerque.
        /// </summary>
        public bool RandomPoint(Vector3 toward, float minDot, float minHeight, out Vector3 point, out Vector3 normal, int tries = 24)
        {
            point = normal = Vector3.zero;
            if (totalArea <= 0f) return EllipsoidPoint(toward, minHeight, out point, out normal);

            Bounds bounds = WorldBounds();
            float bestScore = float.NegativeInfinity;
            toward = toward.sqrMagnitude > 1e-8f ? toward.normalized : Vector3.up;
            for (int attempt = 0; attempt < tries; attempt++)
            {
                if (!Sample(out Vector3 p, out Vector3 n)) continue;
                float height = (p.y - bounds.min.y) / Mathf.Max(bounds.size.y, 1e-4f);
                float dot = Vector3.Dot(n, toward);
                float score = Mathf.Min(dot - minDot, 0f) + Mathf.Min(height - minHeight, 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    point = p;
                    normal = n;
                }
                if (score >= 0f) break;
            }
            return bestScore > float.NegativeInfinity;
        }

        /// <summary>Caja del personaje en mundo (de sus mallas; sin mallas, un metro alrededor de la raíz).</summary>
        public Bounds WorldBounds()
        {
            bool any = false;
            Bounds bounds = default;
            foreach (Part part in parts)
            {
                MeshData data = Data(part);
                if (data == null) continue;
                Matrix4x4 toWorld = LocalToWorld(part);
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = new((corner & 1) == 0 ? data.min.x : data.max.x, (corner & 2) == 0 ? data.min.y : data.max.y, (corner & 4) == 0 ? data.min.z : data.max.z);
                    Vector3 world = toWorld.MultiplyPoint3x4(local);
                    if (!any) bounds = new Bounds(world, Vector3.zero);
                    else bounds.Encapsulate(world);
                    any = true;
                }
            }
            if (any) return bounds;
            if (root.TryGetComponent(out Collider collider)) return collider.bounds;
            return new Bounds(root.position, Vector3.one);
        }

        // ---------------- Malla ----------------

        static bool Readable(Mesh mesh, Object context)
        {
            if (mesh.isReadable) return true;
            if (warned.Add(mesh))
                Debug.LogWarning($"CharacterSurface: la malla '{mesh.name}' no tiene Read/Write activado; los efectos usan una aproximación. Actívalo en el Import Settings del modelo.", context);
            return false;
        }

        static MeshData Load(Mesh mesh)
        {
            if (meshCache.TryGetValue(mesh, out MeshData data)) return data;
            data = new MeshData { triangles = mesh.triangles };
            Fill(data, mesh);
            meshCache[mesh] = data;
            return data;
        }

        static void Fill(MeshData data, Mesh mesh)
        {
            data.vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            data.normals = normals != null && normals.Length == data.vertices.Length ? normals : null;
            Bounds bounds = mesh.bounds;
            data.min = bounds.min;
            data.max = bounds.max;

            int count = data.triangles.Length / 3;
            if (data.cumulativeArea == null || data.cumulativeArea.Length != count) data.cumulativeArea = new float[count];
            float total = 0f;
            Vector3[] v = data.vertices;
            int[] tris = data.triangles;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = v[tris[i * 3]], b = v[tris[i * 3 + 1]], c = v[tris[i * 3 + 2]];
                total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                data.cumulativeArea[i] = total;
            }
            data.totalArea = total;
        }

        static void Bake(Part part)
        {
            part.skinned.BakeMesh(part.baked, true);
            Fill(part.data, part.baked);
            part.bakedFrame = Time.frameCount;
        }

        // Las animadas se hornean una vez por frame como mucho.
        static MeshData Data(Part part)
        {
            if (part.renderer == null) return null;
            if (part.skinned != null && part.bakedFrame != Time.frameCount) Bake(part);
            return part.data;
        }

        // BakeMesh con escala deja la malla en el espacio del renderer sin escala.
        static Matrix4x4 LocalToWorld(Part part) => part.skinned != null
            ? Matrix4x4.TRS(part.skinned.transform.position, part.skinned.transform.rotation, Vector3.one)
            : part.renderer.transform.localToWorldMatrix;

        Vector3 TriangleNormal(Part part, int triangle, float u, float v)
        {
            MeshData data = Data(part);
            int[] tris = data.triangles;
            Vector3 local;
            if (data.normals != null)
            {
                local = data.normals[tris[triangle]] * (1f - u - v) + data.normals[tris[triangle + 1]] * u + data.normals[tris[triangle + 2]] * v;
            }
            else
            {
                Vector3 a = data.vertices[tris[triangle]], b = data.vertices[tris[triangle + 1]], c = data.vertices[tris[triangle + 2]];
                local = Vector3.Cross(b - a, c - a);
            }
            return NormalToWorld(part, local);
        }

        static Vector3 NormalToWorld(Part part, Vector3 local)
        {
            // Inversa traspuesta: correcta también con escala no uniforme.
            Vector3 world = LocalToWorld(part).inverse.transpose.MultiplyVector(local);
            return world.sqrMagnitude > 1e-12f ? world.normalized : Vector3.up;
        }

        bool Sample(out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float pick = Random.value * totalArea;
            foreach (Part part in parts)
            {
                MeshData data = Data(part);
                if (data == null) continue;
                if (pick > data.totalArea)
                {
                    pick -= data.totalArea;
                    continue;
                }

                int triangle = System.Array.BinarySearch(data.cumulativeArea, pick);
                if (triangle < 0) triangle = Mathf.Min(~triangle, data.cumulativeArea.Length - 1);
                float r1 = Mathf.Sqrt(Random.value), r2 = Random.value;
                float u = r1 * (1f - r2), v = r1 * r2;
                int[] tris = data.triangles;
                Vector3 a = data.vertices[tris[triangle * 3]], b = data.vertices[tris[triangle * 3 + 1]], c = data.vertices[tris[triangle * 3 + 2]];
                point = LocalToWorld(part).MultiplyPoint3x4(a * (1f - u - v) + b * u + c * v);
                normal = TriangleNormal(part, triangle * 3, u, v);
                return true;
            }
            return false;
        }

        bool NearestToRay(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            direction.Normalize();
            float bestScore = float.MaxValue;
            foreach (Part part in parts)
            {
                MeshData data = Data(part);
                if (data == null) continue;
                Matrix4x4 toWorld = LocalToWorld(part);
                for (int i = 0; i < data.vertices.Length; i++)
                {
                    Vector3 p = toWorld.MultiplyPoint3x4(data.vertices[i]);
                    Vector3 offset = p - origin;
                    float along = Vector3.Dot(offset, direction);
                    if (along <= 0f) continue;
                    float score = (offset - direction * along).sqrMagnitude;
                    if (score >= bestScore) continue;
                    Vector3 n = data.normals != null ? NormalToWorld(part, data.normals[i]) : -direction;
                    // Las que miran hacia el otro lado (parte de atrás) solo si no hay otra.
                    if (Vector3.Dot(n, direction) > 0.2f) score += 1f;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    point = p;
                    normal = n;
                }
            }
            if (bestScore == float.MaxValue) return false;
            if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
            return true;
        }

        // ---------------- Sin malla: elipsoide de la caja ----------------

        void EllipsoidEntry(Vector3 origin, Vector3 direction, out Vector3 point, out Vector3 normal)
        {
            Bounds bounds = WorldBounds();
            Vector3 extents = Vector3.Max(bounds.extents, Vector3.one * 0.01f);
            direction.Normalize();
            Vector3 o = Divide(origin - bounds.center, extents), d = Divide(direction, extents);
            // |o + t d| = 1
            float a = Vector3.Dot(d, d), b = 2f * Vector3.Dot(o, d), c = Vector3.Dot(o, o) - 1f;
            float discriminant = b * b - 4f * a * c;
            float t = discriminant >= 0f ? (-b - Mathf.Sqrt(discriminant)) / (2f * a) : -Vector3.Dot(o, d) / a;
            point = origin + direction * Mathf.Max(t, 0f);
            normal = Divide(Divide(point - bounds.center, extents), extents).normalized;
            if (normal == Vector3.zero || Vector3.Dot(normal, direction) > 0f) normal = -direction;
        }

        bool EllipsoidPoint(Vector3 toward, float minHeight, out Vector3 point, out Vector3 normal)
        {
            Bounds bounds = WorldBounds();
            Vector3 dir = (toward.normalized + Vector3.up * Mathf.Lerp(-1f, 1f, minHeight)).normalized;
            normal = dir;
            point = bounds.center + Vector3.Scale(dir, bounds.extents);
            return true;
        }

        static Vector3 Divide(Vector3 a, Vector3 b) => new(a.x / b.x, a.y / b.y, a.z / b.z);

        // ---------------- Geometría ----------------

        // Möller–Trumbore, por las dos caras.
        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, float maxT, out float t, out float u, out float v)
        {
            t = u = v = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-12f && det < 1e-12f) return false;
            float inverse = 1f / det;
            Vector3 s = o - a;
            u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(d, q) * inverse;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inverse;
            return t > 0f && t < maxT;
        }

        static bool RayBox(Vector3 min, Vector3 max, Vector3 o, Vector3 d, float maxT)
        {
            float near = 0f, far = maxT;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < 1e-12f)
                {
                    if (o[axis] < min[axis] || o[axis] > max[axis]) return false;
                    continue;
                }
                float t1 = (min[axis] - o[axis]) / d[axis], t2 = (max[axis] - o[axis]) / d[axis];
                if (t1 > t2) (t1, t2) = (t2, t1);
                near = Mathf.Max(near, t1);
                far = Mathf.Min(far, t2);
                if (near > far) return false;
            }
            return true;
        }
    }
}
