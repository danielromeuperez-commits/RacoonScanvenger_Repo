using System.Collections.Generic;
using UnityEngine;

namespace Racoon.Events
{
    /// <summary>
    /// Mallas planas de los carteles de los eventos, con las UV que espera SG_VFX_ToonFlat (ver HLSL_ToonFlat):
    /// UV0.x = 0,5 (siempre visible con el revelado por defecto), UV1.x = 1 en el contorno, UV1.y = degradado.
    /// Plano XY mirando a -Z, como las de FlatMeshes: el contorno va un pelo detrás (+Z) y se añade antes que el
    /// relleno. Los triángulos van por las dos caras para que se vean giren como giren.
    /// </summary>
    public static class EventMeshes
    {
        const float OutlineDepth = 0.006f;
        const int CornerSegments = 8;

        /// <summary>Rectángulo redondeado (cartel), con contorno de <paramref name="outline"/> alrededor.</summary>
        public static Mesh Board(Vector2 half, float radius, float outline)
        {
            var b = new Builder();
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            if (outline > 0f) b.Fan(half, radius, outline, OutlineDepth, 1f);
            b.Fan(half, radius, 0f, 0f, 0f);
            return b.Build("EventBoard");
        }

        /// <summary>
        /// Marco redondeado alrededor de un rectángulo de <paramref name="half"/>: banda de <paramref name="width"/>
        /// hacia fuera, con contorno de <paramref name="outline"/> por los dos lados. Degradado de dentro (0) a fuera (1).
        /// </summary>
        public static Mesh Frame(Vector2 half, float radius, float width, float outline)
        {
            var b = new Builder();
            radius = Mathf.Min(radius, Mathf.Min(half.x, half.y));
            if (outline > 0f) b.Ring(half, radius, -outline, width + outline, OutlineDepth, 1f);
            b.Ring(half, radius, 0f, width, 0f, 0f);
            return b.Build("EventFrame");
        }

        static List<Vector2> Contour(Vector2 half, float radius, float offset)
        {
            var points = new List<Vector2>((CornerSegments + 1) * 4);
            Vector2[] centers =
            {
                new(half.x - radius, half.y - radius), new(-half.x + radius, half.y - radius),
                new(-half.x + radius, -half.y + radius), new(half.x - radius, -half.y + radius),
            };
            for (int c = 0; c < 4; c++)
            for (int i = 0; i <= CornerSegments; i++)
            {
                float a = (c * 90f + i * 90f / CornerSegments) * Mathf.Deg2Rad;
                points.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Max(0f, radius + offset));
            }
            return points;
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector2> uv0 = new();
            readonly List<Vector2> uv1 = new();
            readonly List<int> triangles = new();

            int Add(Vector2 p, float z, float outline, float gradient)
            {
                vertices.Add(new Vector3(p.x, p.y, z));
                uv0.Add(new Vector2(0.5f, 0.5f));
                uv1.Add(new Vector2(outline, gradient));
                return vertices.Count - 1;
            }

            void Tri(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
            }

            public void Fan(Vector2 half, float radius, float offset, float z, float outline)
            {
                List<Vector2> contour = Contour(half, radius, offset);
                float height = Mathf.Max(half.y + offset, 1e-4f);
                int center = Add(Vector2.zero, z, outline, 0.5f);
                int first = vertices.Count;
                foreach (Vector2 p in contour) Add(p, z, outline, (p.y / height) * 0.5f + 0.5f);
                for (int i = 0; i < contour.Count; i++) Tri(center, first + i, first + (i + 1) % contour.Count);
            }

            public void Ring(Vector2 half, float radius, float inner, float outer, float z, float outline)
            {
                List<Vector2> a = Contour(half, radius, inner);
                List<Vector2> b = Contour(half, radius, outer);
                int first = vertices.Count;
                for (int i = 0; i < a.Count; i++)
                {
                    Add(a[i], z, outline, 0f);
                    Add(b[i], z, outline, 1f);
                }
                for (int i = 0; i < a.Count; i++)
                {
                    int j = (i + 1) % a.Count;
                    int a0 = first + i * 2, b0 = a0 + 1, a1 = first + j * 2, b1 = a1 + 1;
                    Tri(a0, b0, b1);
                    Tri(a0, b1, a1);
                }
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetTriangles(triangles, 0);
                var normals = new Vector3[vertices.Count];
                for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
                mesh.normals = normals;
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
