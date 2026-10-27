using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Golpe al aire: un tajo 3D que barre por delante del personaje SIEMPRE de derecha a izquierda y acaba un poco
    /// pasado el centro. Es una estela con volumen que se rehace cada frame: la cabeza (gorda y redondeada) va delante y
    /// la cola adelgaza hasta una punta y la persigue, así se ve fluido y no como una pieza que se recorta. Dos hebras
    /// finas acompañan al tajo (por dentro y por fuera, con un pelín de retraso). Con SG_VFX_ToonVolume: luz y sombra en dos tonos, brillo especular, luz de borde y
    /// contorno; caras blancas y filos y cola dorados.
    ///
    /// Montaje: hijo del personaje a la altura del pecho, Z hacia delante. Lo lanza PlayerVFX al empezar el
    /// puñetazo (en todos los clientes), conecte o no.
    /// </summary>
    public class SwingVFX : VfxBehaviour
    {
        [Header("Materiales")]
        [Tooltip("M_VFX_ToonVolume: tajo con volumen.")]
        [SerializeField] Material volumeMaterial;
        [Tooltip("M_VFX_ToonFlat: solo si falta volumeMaterial.")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [Tooltip("Caras del tajo...")]
        [SerializeField] Color arcColor = Color.white;
        [Tooltip("...y sus filos y su cola. También las hebras.")]
        [SerializeField] Color arcEdgeColor = new(1f, 0.8f, 0.28f);
        [SerializeField] Color outlineColor = new(0.85f, 0.58f, 0.14f);
        [Tooltip("Multiplica el color en la cara en sombra.")]
        [SerializeField] Color shadowTint = new(1f, 0.85f, 0.65f);
        [Tooltip("Brillo especular (alfa = intensidad).")]
        [SerializeField] Color highlightColor = Color.white;
        [Tooltip("Luz de borde en la silueta (alfa = intensidad).")]
        [SerializeField] Color rimColor = new(1f, 1f, 0.9f, 0.8f);

        [Header("Forma")]
        [Tooltip("Radio del barrido (metros).")]
        [SerializeField] float radius = 0.65f;
        [Tooltip("Ancho máximo del tajo, hacia fuera (metros).")]
        [SerializeField] float width = 0.24f;
        [Tooltip("Grosor máximo del tajo, en vertical (metros).")]
        [SerializeField] float thickness = 0.09f;
        [Tooltip("Dónde empieza el barrido (grados; 0 = delante, 90 = a la derecha del personaje).")]
        [SerializeField] float startAngle = 125f;
        [Tooltip("Dónde acaba (grados; negativo = a la izquierda): un poco pasado el centro.")]
        [SerializeField] float endAngle = -25f;
        [Tooltip("Largo máximo de la estela (grados).")]
        [SerializeField] float trail = 105f;
        [Tooltip("Inclinación (grados): empieza más alto por la derecha y acaba más bajo por la izquierda.")]
        [SerializeField] float tilt = 16f;
        [SerializeField] float forwardOffset = 0.05f;
        [Tooltip("Hebras finas por dentro y por fuera del tajo.")]
        [SerializeField] bool strands = true;

        [Header("Ritmo")]
        [Tooltip("Segundos desde que empieza el puñetazo (el golpe se comprueba a los 0.15 s).")]
        [SerializeField] float delay = 0.04f;
        [SerializeField] float duration = 0.2f;

        // Hebras: radio, ancho y grosor (respecto al tajo), altura (metros) y retraso (respecto a la duración).
        static readonly float[] StrandRadius = { 0.8f, 1.14f };
        static readonly float[] StrandWidth = { 0.22f, 0.18f };
        static readonly float[] StrandThickness = { 0.38f, 0.32f };
        static readonly float[] StrandHeight = { 0.05f, -0.04f };
        static readonly float[] StrandLag = { 0.12f, 0.07f };

        Smear blade;
        Smear[] strandSmears;
        float time = -1f;

        void Awake()
        {
            Material material = volumeMaterial != null ? volumeMaterial : flatMaterial;
            if (material == null)
            {
                Debug.LogWarning("SwingVFX: falta volumeMaterial (M_VFX_ToonVolume).", this);
                enabled = false;
                return;
            }

            blade = new Smear("SwingBlade", transform, material, 0.012f, startAngle, endAngle);
            Style(blade.Piece).Colors(arcColor, arcEdgeColor, outlineColor);
            if (strands)
            {
                strandSmears = new Smear[StrandRadius.Length];
                for (int i = 0; i < strandSmears.Length; i++)
                {
                    strandSmears[i] = new Smear($"SwingStrand{i}", transform, material, 0.006f, startAngle, endAngle);
                    Style(strandSmears[i].Piece).Colors(arcEdgeColor, arcEdgeColor, outlineColor);
                }
            }

            FlatPiece Style(FlatPiece piece) => piece.Cel(1f, shadowTint).Shine(highlightColor, rimColor);
        }

        void OnDisable() => Hide();

        void OnDestroy()
        {
            blade?.Dispose();
            if (strandSmears != null) foreach (Smear strand in strandSmears) strand.Dispose();
        }

        // ---------------- API ----------------

        public void Play()
        {
            if (blade == null) return;
            Hide();
            // El plano del barrido, inclinado: la derecha (+X) más alta.
            Quaternion plane = Quaternion.Euler(0f, 0f, tilt);
            blade.Piece.Transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, forwardOffset), plane);
            if (strandSmears != null)
                for (int i = 0; i < strandSmears.Length; i++)
                    strandSmears[i].Piece.Transform.SetLocalPositionAndRotation(new Vector3(0f, StrandHeight[i], forwardOffset), plane);
            time = 0f;
        }

        [VfxTest("Test golpe al aire")]
        void Test() => Play();

        // ---------------- Animación ----------------

        void Update()
        {
            if (time < 0f) return;
            time += Time.deltaTime;
            float t = time - delay, d = Mathf.Max(duration, 0.01f);
            if (t > d * 1.25f)
            {
                Hide();
                return;
            }
            if (t < 0f) return;

            Sweep(blade, t / d, radius, width * 0.5f, thickness * 0.5f);
            if (strandSmears != null)
                for (int i = 0; i < strandSmears.Length; i++)
                    Sweep(strandSmears[i], t / d - StrandLag[i], radius * StrandRadius[i], width * 0.5f * StrandWidth[i], thickness * 0.5f * StrandThickness[i]);
        }

        // x = tiempo normalizado (0 → 1 la cabeza llega al final; la cola llega un poco después y la estela se cierra).
        void Sweep(Smear smear, float x, float smearRadius, float halfWidth, float halfThickness)
        {
            if (x <= 0f || x >= 1.2f)
            {
                smear.Piece.Visible = false;
                return;
            }
            // La cabeza arranca rápido y frena suave; la cola sale después y la alcanza.
            float head = Ease(x / 0.7f);
            float tail = Ease((x - 0.22f) / 0.98f);
            float headDeg = Mathf.Lerp(startAngle, endAngle, head);
            float tailDeg = Mathf.Lerp(startAngle, endAngle, tail);
            float sign = Mathf.Sign(startAngle - endAngle);
            tailDeg = headDeg + sign * Mathf.Min(sign * (tailDeg - headDeg), trail);

            float span = Mathf.Abs(tailDeg - headDeg);
            if (span < 1f)
            {
                smear.Piece.Visible = false;
                return;
            }
            // Engorda al arrancar y adelgaza al cerrarse (si es corta, también más fina).
            float body = FlatFx.OutCubic(Mathf.Clamp01(x / 0.25f)) * Mathf.Sqrt(Mathf.Clamp01(span / 45f));
            smear.Build(tailDeg, headDeg, smearRadius, halfWidth * body, halfThickness * body);
            smear.Piece.Visible = true;
        }

        // Sale disparado y frena suave (quíntica).
        static float Ease(float x)
        {
            x = 1f - Mathf.Clamp01(x);
            return 1f - x * x * x * x * x;
        }

        void Hide()
        {
            time = -1f;
            if (blade != null) blade.Piece.Visible = false;
            if (strandSmears != null) foreach (Smear strand in strandSmears) strand.Piece.Visible = false;
        }

        // ---------------- Estela ----------------

        /// <summary>
        /// Estela con volumen en el plano XZ alrededor del origen (0° = +Z, 90° = +X) que se rehace cada frame de la cola
        /// a la cabeza: sección elíptica aplanada (ancho hacia fuera, grosor en vertical), la cabeza redondeada y lo más
        /// gordo justo detrás de ella, y la cola adelgazando hasta una punta. Con cáscara de contorno y tapas en los
        /// extremos. Degradado = filos y cola (TipColor) → centro de la cabeza (BaseColor).
        /// </summary>
        sealed class Smear
        {
            const int Segments = 48, Sides = 12;
            const int Rings = Segments + 1;
            const int BodyCount = Rings * Sides + 2;
            // Donde empieza la cabeza redondeada (recorrido de cola 0 a cabeza 1).
            const float HeadStart = 0.8f;

            public readonly FlatPiece Piece;
            readonly Mesh mesh;
            readonly float outline;
            readonly Vector3[] vertices = new Vector3[BodyCount * 2];
            readonly Vector3[] normals = new Vector3[BodyCount * 2];

            public Smear(string name, Transform parent, Material material, float outline, float fromDeg, float toDeg)
            {
                this.outline = outline;
                mesh = new Mesh { name = $"FX_{name}" };
                mesh.MarkDynamic();

                Vector2[] uv0 = new Vector2[BodyCount * 2], uv1 = new Vector2[BodyCount * 2], uv2 = new Vector2[BodyCount * 2];
                for (int i = 0; i < Rings; i++)
                {
                    float u = i / (float)Segments;
                    for (int j = 0; j < Sides; j++)
                    {
                        float c = Mathf.Abs(Mathf.Cos(j / (float)Sides * Mathf.PI * 2f));
                        Uv(i * Sides + j, u, Mathf.Clamp01(c * c * 0.85f + (1f - u) * 0.6f));
                    }
                }
                Uv(Rings * Sides, 0f, 1f);
                Uv(Rings * Sides + 1, 1f, 0.4f);

                // Con una forma de prueba para orientar las caras hacia fuera (con la cáscara, que nunca es plana).
                Build(fromDeg, Mathf.Lerp(fromDeg, toDeg, 0.6f), 1f, 0.2f, 0.1f);
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.uv = uv0;
                mesh.uv2 = uv1;
                mesh.uv3 = uv2;
                mesh.triangles = Triangles();
                // Cabe en cualquier barrido: no hace falta recalcularla cada frame.
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);

                Piece = new FlatPiece(name, parent, mesh, material);
                Piece.Window(FlatFx.Full, -1f);

                void Uv(int index, float along, float gradient)
                {
                    uv0[index] = uv0[index + BodyCount] = new Vector2(along, 0.5f);
                    uv1[index] = new Vector2(0f, gradient);
                    uv1[index + BodyCount] = new Vector2(1f, gradient);
                }
            }

            public void Build(float tailDeg, float headDeg, float radius, float halfWidth, float halfThickness)
            {
                halfWidth = Mathf.Max(halfWidth, 1e-4f);
                halfThickness = Mathf.Max(halfThickness, 1e-4f);
                float sign = Mathf.Sign(headDeg - tailDeg);
                Vector3 tailCenter = default, headCenter = default, tailTangent = default, headTangent = default;
                for (int i = 0; i < Rings; i++)
                {
                    float u = i / (float)Segments;
                    float angle = Mathf.Lerp(tailDeg, headDeg, u) * Mathf.Deg2Rad;
                    Vector3 dir = new(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    // Se abre un pelín hacia la cabeza (espiral): más dinámico que un arco perfecto.
                    Vector3 center = dir * (radius * (1f + 0.07f * (u - 0.5f)));
                    float profile = Profile(u);
                    float w = Mathf.Max(halfWidth * profile, 1e-4f), h = Mathf.Max(halfThickness * profile, 1e-4f);
                    for (int j = 0; j < Sides; j++)
                    {
                        float a = j / (float)Sides * Mathf.PI * 2f;
                        float c = Mathf.Cos(a), s = Mathf.Sin(a);
                        Vector3 normal = (dir * (c / halfWidth) + Vector3.up * (s / halfThickness)).normalized;
                        Set(i * Sides + j, center + dir * (c * w) + Vector3.up * (s * h), normal);
                    }
                    Vector3 tangent = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle)) * sign;
                    if (i == 0) (tailCenter, tailTangent) = (center, tangent);
                    if (i == Segments) (headCenter, headTangent) = (center, tangent);
                }
                Set(Rings * Sides, tailCenter, -tailTangent);
                Set(Rings * Sides + 1, headCenter, headTangent);
                if (mesh.vertexCount == 0) return;
                mesh.vertices = vertices;
                mesh.normals = normals;
            }

            // Grosor a lo largo (0 cola → 1 cabeza): crece hasta justo detrás de la cabeza y se cierra en redondo.
            static float Profile(float u)
            {
                if (u <= HeadStart) return Mathf.Pow(u / HeadStart, 0.85f);
                float x = (u - HeadStart) / (1f - HeadStart);
                return Mathf.Sqrt(Mathf.Max(0f, 1f - x * x));
            }

            // Vértice del cuerpo y el de su cáscara de contorno (inflada por la normal).
            void Set(int index, Vector3 position, Vector3 normal)
            {
                vertices[index] = position;
                normals[index] = normal;
                vertices[index + BodyCount] = position + normal * outline;
                normals[index + BodyCount] = normal;
            }

            int[] Triangles()
            {
                int quads = Segments * Sides, caps = Sides * 2;
                int[] triangles = new int[(quads * 2 + caps) * 3 * 2];
                int n = 0;
                for (int i = 0; i < Segments; i++)
                {
                    for (int j = 0; j < Sides; j++)
                    {
                        int j1 = (j + 1) % Sides;
                        int a = i * Sides + j, c = i * Sides + j1, d = (i + 1) * Sides + j, e = (i + 1) * Sides + j1;
                        Tri(a, d, c);
                        Tri(c, d, e);
                    }
                }
                int tail = Rings * Sides, head = tail + 1, last = Segments * Sides;
                for (int j = 0; j < Sides; j++)
                {
                    int j1 = (j + 1) % Sides;
                    Tri(tail, j, j1);
                    Tri(head, last + j, last + j1);
                }
                return triangles;

                // Hacia fuera según la cáscara (los vértices del cuerpo en las puntas coinciden); la cáscara al revés.
                void Tri(int a, int b, int c)
                {
                    Vector3 pa = vertices[a + BodyCount], pb = vertices[b + BodyCount], pc = vertices[c + BodyCount];
                    Vector3 facing = normals[a] + normals[b] + normals[c];
                    if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), facing) < 0f) (b, c) = (c, b);
                    triangles[n++] = a;
                    triangles[n++] = b;
                    triangles[n++] = c;
                    triangles[n++] = a + BodyCount;
                    triangles[n++] = c + BodyCount;
                    triangles[n++] = b + BodyCount;
                }
            }

            public void Dispose()
            {
                if (mesh != null) Object.Destroy(mesh);
            }
        }
    }
}
