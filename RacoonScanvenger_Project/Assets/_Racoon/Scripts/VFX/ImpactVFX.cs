using UnityEngine;
using UnityEngine.Serialization;

namespace Racoon.VFX
{
    /// <summary>
    /// Impacto de un golpe: onda de choque 3D que nace en el punto de la MALLA por donde entra el golpe (se busca con
    /// un rayo desde quien golpea, ver <see cref="CharacterSurface"/>), orientada hacia fuera de la superficie:
    ///  - Núcleo: bola de pinchos facetada que revienta y se cierra.
    ///  - Cristales facetados que salen disparados en abanico y se recogen hacia la punta.
    ///  - Burbuja de choque que se hincha y se va vaciando desde el centro hasta quedar en su silueta.
    ///  - Anillos de choque (tubos) en cono hacia quien golpea: se dibujan dando la vuelta, se abren y el tubo
    ///    adelgaza hasta desaparecer.
    ///  - Golpe fuerte: más grande y lento, un anillo y cristales de acento más y estrellas 3D que salen volando.
    /// Todo tiene volumen (SG_VFX_ToonVolume): luz y sombra en dos tonos, brillo especular, luz de borde en la silueta
    /// y contorno, como el resto de efectos.
    /// La onda acompaña al golpeado cuando sale despedido (followTarget).
    ///
    /// Montaje: hijo del personaje a la altura del pecho (es el centro hacia el que se lanza el rayo). Lo lanza
    /// PlayerVFX con <see cref="PlayTowards"/> en todos los clientes.
    /// </summary>
    public class ImpactVFX : VfxBehaviour
    {
        [Header("Materiales")]
        [Tooltip("M_VFX_ToonVolume: la onda de choque, con volumen (luz, sombra, brillo y luz de borde).")]
        [SerializeField] Material volumeMaterial;
        [Tooltip("M_VFX_ToonFlat: estrellas del golpe fuerte.")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [FormerlySerializedAs("flashColor")]
        [SerializeField] Color coreColor = Color.white;
        [SerializeField] Color coreTipColor = new(1f, 0.85f, 0.35f);
        [FormerlySerializedAs("rayColor")]
        [SerializeField] Color crystalColor = new(1f, 1f, 0.88f);
        [FormerlySerializedAs("rayTipColor")]
        [SerializeField] Color crystalTipColor = new(1f, 0.7f, 0.08f);
        [Tooltip("Anillos (lado de dentro) y burbuja (punta)...")]
        [SerializeField] Color waveColor = new(1f, 1f, 0.92f);
        [Tooltip("...anillos (lado de fuera) y burbuja (base e interior).")]
        [SerializeField] Color waveEdgeColor = new(1f, 0.78f, 0.25f);
        [Tooltip("Golpe fuerte: anillo y cristales de acento.")]
        [SerializeField] Color accentColor = new(0.97f, 0.87f, 0.44f);
        [SerializeField] Color outlineColor = new(0.59f, 0.5f, 0.19f);
        [Tooltip("Multiplica el color en la cara en sombra.")]
        [SerializeField] Color shadowTint = new(1f, 0.82f, 0.62f);
        [Tooltip("Brillo especular (alfa = intensidad).")]
        [SerializeField] Color highlightColor = Color.white;
        [Tooltip("Luz de borde en la silueta (alfa = intensidad).")]
        [SerializeField] Color rimColor = new(1f, 1f, 0.9f, 0.8f);
        [SerializeField] Color starColor = new(1f, 0.88f, 0.28f);
        [SerializeField] Color starTipColor = new(1f, 0.58f, 0.1f);
        [SerializeField] Color starShadow = new(1f, 0.72f, 0.55f);
        [SerializeField] Color starOutline = new(0.28f, 0.11f, 0.03f);

        [Header("Tamaño y ritmo")]
        [Tooltip("Tamaño (metros) de la onda del golpe ligero / fuerte.")]
        [SerializeField] Vector2 size = new(0.55f, 0.85f);
        [Tooltip("Cuánto acompaña la onda al golpeado al salir despedido (0 = se queda donde fue el golpe, 1 = pegada a él).")]
        [SerializeField, Range(0f, 1f)] float followTarget = 0.8f;
        [Tooltip("Variación del punto de entrada entre golpes (metros: a los lados, en altura), para que cada golpe del combo entre por un sitio. El fuerte, más centrado.")]
        [SerializeField] Vector2 hitScatter = new(0.1f, 0.12f);
        [Tooltip("Multiplica la duración de todo (1 = normal; menos = más seco; más = más lento).")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Golpes a la vez como máximo.")]
        [SerializeField] int poolSize = 3;

        [Header("Golpe fuerte: estrellas")]
        [SerializeField] int heavyStars = 3;
        [SerializeField] float starSize = 0.18f;
        [SerializeField] float starGravity = 9f;

        const float LightDuration = 0.36f;
        const float HeavyDuration = 0.62f;
        const float RayStart = 1.5f;
        // Anillos: radio máx. y distancia hacia fuera (por tamaño), retraso (s). El 3º solo en el fuerte.
        static readonly float[] RingRadius = { 0.78f, 0.58f, 0.44f };
        static readonly float[] RingOffset = { 0.03f, 0.16f, 0.3f };
        static readonly float[] RingDelay = { 0f, 0.035f, 0.07f };
        // Grosor del tubo de cada anillo (respecto a su radio).
        static readonly float[] RingTube = { 0.075f, 0.07f, 0.065f };

        sealed class Burst
        {
            public Transform root;
            public FlatPiece core, crystals, accent, bubble;
            public FlatPiece[] rings;
            public Vector3[] ringTilt;
            public FlatPiece[] stars;
            public Vector3[] starVelocity;
            public float[] starSpin;
            public Vector3 point;
            public Vector3 targetStart;
            public Vector3 coreAxis;
            public float time = -1f;
            public float scale;
            public bool heavy;
        }

        Transform character;
        CharacterSurface surface;
        Burst[] bursts;
        int next;

        void Awake()
        {
            if (volumeMaterial == null)
            {
                Debug.LogWarning("ImpactVFX: falta volumeMaterial (M_VFX_ToonVolume).", this);
                enabled = false;
                return;
            }

            Rigidbody body = GetComponentInParent<Rigidbody>();
            character = body != null ? body.transform : transform.root;
            surface = new CharacterSurface(character);

            Mesh core = FlatMeshes.SpikeBall();
            Mesh bubble = FlatMeshes.Bubble();
            Mesh star = heavyStars > 0 && flatMaterial != null ? FlatMeshes.Star() : null;
            Mesh[] rings = new Mesh[RingRadius.Length];
            for (int i = 0; i < rings.Length; i++) rings[i] = FlatMeshes.Torus(310 + i * 17, RingTube[i], 0.02f, 0.035f, 5 + i);

            bursts = new Burst[Mathf.Max(1, poolSize)];
            for (int i = 0; i < bursts.Length; i++) bursts[i] = CreateBurst(i, core, bubble, rings, star);
        }

        void OnDisable()
        {
            if (bursts == null) return;
            foreach (Burst burst in bursts) Hide(burst);
        }

        void OnDestroy()
        {
            surface?.Dispose();
            if (bursts == null) return;
            foreach (Burst burst in bursts)
                if (burst.root != null) Destroy(burst.root.gameObject);
        }

        // ---------------- API ----------------

        /// <summary>
        /// Golpe que llega desde <paramref name="towardAttacker"/> (dirección hacia quien golpea): busca por dónde entra
        /// en la malla y lanza la onda ahí.
        /// </summary>
        public void PlayTowards(Vector3 towardAttacker, bool heavy)
        {
            if (bursts == null) return;
            towardAttacker.y = 0f;
            if (towardAttacker.sqrMagnitude < 1e-4f) towardAttacker = character.forward;
            towardAttacker.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, towardAttacker);
            float scatter = heavy ? 0.35f : 1f;
            Vector3 target = transform.position
                + side * (Random.Range(-1f, 1f) * hitScatter.x * scatter)
                + Vector3.up * (Random.Range(-1f, 1f) * hitScatter.y * scatter);
            surface.FindEntry(target + towardAttacker * RayStart, -towardAttacker, RayStart * 2f, out Vector3 point, out Vector3 normal);
            Play(point, normal, towardAttacker, heavy);
        }

        /// <summary>Onda en <paramref name="point"/> de la superficie, saliendo hacia <paramref name="normal"/>.</summary>
        public void Play(Vector3 point, Vector3 normal, Vector3 towardAttacker, bool heavy)
        {
            if (bursts == null) return;

            Burst burst = bursts[next];
            next = (next + 1) % bursts.Length;
            Hide(burst);

            // Entre la normal de la malla y la dirección del golpe: así no sale torcida si toca una oreja o un brazo.
            Vector3 axis = (normal.normalized + towardAttacker.normalized).normalized;
            if (axis.sqrMagnitude < 0.5f) axis = towardAttacker.sqrMagnitude > 0f ? towardAttacker.normalized : -character.forward;

            burst.time = 0f;
            burst.heavy = heavy;
            burst.scale = heavy ? size.y : size.x;
            burst.point = point + axis * 0.01f;
            burst.targetStart = character.position;
            burst.root.SetPositionAndRotation(burst.point,
                Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));

            burst.core.Transform.localRotation = Random.rotationUniform;
            burst.coreAxis = Random.onUnitSphere;
            burst.crystals.Transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            if (burst.accent != null) burst.accent.Transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            for (int i = 0; i < burst.rings.Length; i++)
                burst.ringTilt[i] = new Vector3(Random.Range(-12f, 12f), Random.Range(-12f, 12f), Random.Range(0f, 360f));

            if (heavy && burst.stars != null)
            {
                for (int i = 0; i < burst.stars.Length; i++)
                {
                    Vector2 flat = Random.insideUnitCircle.normalized;
                    burst.starVelocity[i] = axis * Random.Range(1f, 1.8f) + new Vector3(flat.x, 0f, flat.y) * Random.Range(1.2f, 2.2f) + Vector3.up * Random.Range(2.6f, 3.6f);
                    burst.starSpin[i] = Random.Range(380f, 620f) * (Random.value < 0.5f ? -1f : 1f);
                    burst.stars[i].Transform.position = burst.point;
                }
            }
        }

        [VfxTest("Test golpe ligero")]
        void TestLight() => PlayTowards(TowardCamera(), false);

        [VfxTest("Test golpe fuerte")]
        void TestHeavy() => PlayTowards(TowardCamera(), true);

        // En los tests el golpe entra por el lado que se ve.
        Vector3 TowardCamera()
        {
            Camera cam = FlatFx.Camera;
            return cam != null ? -cam.transform.forward : transform.forward;
        }

        // ---------------- Animación ----------------

        void Update()
        {
            if (bursts == null) return;
            float dt = Time.deltaTime;
            foreach (Burst burst in bursts)
            {
                if (burst.time < 0f) continue;
                burst.time += dt / Mathf.Max(timeScale, 0.01f);
                if (burst.time > (burst.heavy ? HeavyDuration : LightDuration))
                {
                    Hide(burst);
                    continue;
                }
                burst.root.position = burst.point + (character.position - burst.targetStart) * followTarget;
                Animate(burst, dt);
            }
        }

        void Animate(Burst b, float dt)
        {
            float t = b.time;
            float s = b.scale;
            // El fuerte se estira un poco en el tiempo para que pese más.
            float k = b.heavy ? 1.3f : 1f;

            // Núcleo: revienta de golpe, gira y se cierra.
            float coreIn = FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.04f));
            float coreOut = 1f - FlatFx.InCubic(FlatFx.Phase(t, 0.04f, 0.14f * k));
            Show(b.core, coreOut > 0f, Vector3.one * (s * (b.heavy ? 0.4f : 0.34f) * coreIn * coreOut));
            b.core.Transform.localRotation = Quaternion.AngleAxis(900f * dt, b.coreAxis) * b.core.Transform.localRotation;

            // Cristales: salen disparados, aguantan un instante y se recogen hacia la punta.
            float crystalGrow = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, 0f, 0.06f * k));
            float crystalShrink = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, 0.06f * k, 0.25f * k));
            float pop = 0.85f + 0.15f * FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.08f));
            Show(b.crystals, crystalShrink < 1.45f, Vector3.one * (s * 0.78f * pop));
            b.crystals.Window(crystalGrow, crystalShrink);

            // Burbuja de choque: se hincha hacia fuera y se vacía desde el centro hasta quedar en su silueta.
            float bubbleSize = s * 0.42f * FlatFx.OutCubic(FlatFx.Phase(t, 0f, 0.15f * k));
            float hollow = Mathf.SmoothStep(0.25f, 0.98f, FlatFx.Phase(t, 0.01f, 0.24f * k));
            Show(b.bubble, hollow < 0.96f, new Vector3(bubbleSize, bubbleSize, bubbleSize * 0.7f));
            b.bubble.Set(FlatFx.RimCutId, hollow);

            // Anillos (tubos) en cono hacia quien golpea: se dibujan dando la vuelta, se abren y el tubo adelgaza
            // hasta desaparecer.
            for (int i = 0; i < b.rings.Length; i++)
            {
                FlatPiece ring = b.rings[i];
                bool active = i < 2 || b.heavy;
                float start = RingDelay[i] * k;
                float open = FlatFx.OutCubic(FlatFx.Phase(t, start, start + 0.22f * k));
                float sweep = 1.02f * FlatFx.OutCubic(FlatFx.Phase(t, start, start + 0.08f * k));
                float thickness = 1f - Mathf.SmoothStep(0f, 1f, FlatFx.Phase(t, start + 0.04f * k, start + 0.28f * k));
                Show(ring, active && t >= start && thickness > 0.04f, Vector3.one * (s * RingRadius[i] * open));
                ring.Window(sweep, 0f);
                ring.Set(FlatFx.ThicknessId, thickness);
                Vector3 tilt = b.ringTilt[i];
                ring.Transform.localPosition = Vector3.forward * (s * RingOffset[i] * (0.4f + 0.6f * open));
                ring.Transform.localRotation = Quaternion.Euler(tilt.x, tilt.y, tilt.z + 90f * open);
            }

            if (!b.heavy) return;

            // Cristales de acento, un pelín después.
            if (b.accent != null)
            {
                float accentGrow = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, 0.03f, 0.1f));
                float accentShrink = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, 0.12f, 0.36f));
                Show(b.accent, t > 0.03f && accentShrink < 1.55f, Vector3.one * (s * 0.95f));
                b.accent.Window(accentGrow, accentShrink);
            }

            // Estrellas que salen volando, girando, y se encogen al final.
            if (b.stars == null) return;
            for (int i = 0; i < b.stars.Length; i++)
            {
                FlatPiece star = b.stars[i];
                b.starVelocity[i] += Vector3.down * (starGravity * dt);
                star.Transform.position += b.starVelocity[i] * dt;
                star.Transform.rotation = FlatFx.Billboard(b.starSpin[i] * t) * Quaternion.Euler(0f, 30f * Mathf.Sin(t * 12f + i), 0f);
                float grow = FlatFx.OutBack(FlatFx.Phase(t, 0.02f * i, 0.1f + 0.02f * i));
                float fade = 1f - FlatFx.InCubic(FlatFx.Phase(t, HeavyDuration - 0.18f, HeavyDuration));
                Show(star, true, Vector3.one * (starSize * grow * fade));
            }
        }

        static void Show(FlatPiece piece, bool visible, Vector3 scale)
        {
            if (piece == null) return;
            visible &= scale.x > 0.0005f;
            piece.Visible = visible;
            if (visible) piece.Transform.localScale = scale;
        }

        static void Hide(Burst burst)
        {
            burst.time = -1f;
            foreach (FlatPiece piece in new[] { burst.core, burst.crystals, burst.accent, burst.bubble })
                if (piece != null) piece.Visible = false;
            foreach (FlatPiece ring in burst.rings) ring.Visible = false;
            if (burst.stars == null) return;
            foreach (FlatPiece star in burst.stars) star.Visible = false;
        }

        // ---------------- Montaje ----------------

        Burst CreateBurst(int index, Mesh coreMesh, Mesh bubbleMesh, Mesh[] ringMeshes, Mesh starMesh)
        {
            int seed = 101 + index * 37;
            Burst b = new();
            // Fuera del personaje: la onda se mueve por su cuenta (followTarget).
            b.root = new GameObject($"Impact_{character.name}_{index}").transform;

            b.core = Volume("Core", coreMesh).Colors(coreColor, coreTipColor, outlineColor);
            b.crystals = Volume("Crystals",
                    FlatMeshes.Crystals(seed, 9, 62f, 0.1f, new Vector2(0.55f, 1f), new Vector2(0.06f, 0.085f), 0.03f))
                .Colors(crystalColor, crystalTipColor, outlineColor).Spread(0.35f);
            b.accent = Volume("AccentCrystals",
                    FlatMeshes.Crystals(seed + 1, 6, 75f, 0.18f, new Vector2(0.5f, 0.85f), new Vector2(0.055f, 0.075f), 0.03f))
                .Colors(accentColor, accentColor, outlineColor).Spread(0.35f);
            b.bubble = Volume("Bubble", bubbleMesh).Colors(waveColor, waveEdgeColor, outlineColor);

            b.rings = new FlatPiece[ringMeshes.Length];
            b.ringTilt = new Vector3[ringMeshes.Length];
            for (int i = 0; i < ringMeshes.Length; i++)
            {
                // El 3º (solo en el fuerte) con el color de acento.
                Color edge = i == 2 ? accentColor : waveEdgeColor;
                b.rings[i] = Volume($"Ring{i}", ringMeshes[i]).Colors(waveColor, edge, outlineColor);
            }

            if (starMesh != null)
            {
                b.stars = new FlatPiece[heavyStars];
                b.starVelocity = new Vector3[heavyStars];
                b.starSpin = new float[heavyStars];
                for (int i = 0; i < heavyStars; i++)
                {
                    b.stars[i] = new FlatPiece($"Star{i}", b.root, starMesh, flatMaterial)
                        .Colors(starColor, starTipColor, starOutline).Cel(1f, starShadow);
                }
            }
            return b;

            FlatPiece Volume(string pieceName, Mesh mesh) => new FlatPiece(pieceName, b.root, mesh, volumeMaterial)
                .Cel(1f, shadowTint).Shine(highlightColor, rimColor);
        }
    }
}
