using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Impacto de un golpe al estilo Persona 5 (gráfico, seco y muy rápido), sobre quien lo recibe:
    ///  - Destello blanco en estrella con contorno.
    ///  - Rayos dorados que salen disparados del centro y se recogen hacia la punta.
    ///  - Chispas: trazos cortos que viajan hacia fuera.
    ///  - Esquirlas blancas en zigzag con contorno negro (cómic).
    ///  - Golpe fuerte: además, globo de cómic rojo y blanco que estalla desde dentro, esquirlas rojas y
    ///    estrellas 3D que salen disparadas.
    /// Los gráficos miran siempre a cámara y se pintan por encima de todo (M_VFX_ToonFlatOverlay).
    ///
    /// Montaje: hijo del personaje a la altura del pecho (es el centro del impacto). Lo lanza PlayerVFX con
    /// <see cref="PlayTowards"/> en todos los clientes.
    /// </summary>
    public class ImpactVFX : VfxBehaviour
    {
        [Header("Materiales")]
        [Tooltip("M_VFX_ToonFlatOverlay: los gráficos del golpe se pintan por encima de todo.")]
        [SerializeField] Material overlayMaterial;
        [Tooltip("M_VFX_ToonFlat: estrellas 3D del golpe fuerte (con profundidad y sombreado).")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [SerializeField] Color rayColor = new(1f, 1f, 0.88f);
        [SerializeField] Color rayTipColor = new(1f, 0.7f, 0.08f);
        [SerializeField] Color shardColor = Color.white;
        [Tooltip("Rojo Persona 5: globo y esquirlas del golpe fuerte.")]
        [SerializeField] Color accentColor = new(0.9f, 0.04f, 0.12f);
        [SerializeField] Color outlineColor = new(0.04f, 0.03f, 0.05f);
        [SerializeField] Color flashColor = Color.white;
        [SerializeField] Color starColor = new(1f, 0.88f, 0.28f);
        [SerializeField] Color starTipColor = new(1f, 0.58f, 0.1f);
        [SerializeField] Color starShadow = new(1f, 0.72f, 0.55f);
        [SerializeField] Color starOutline = new(0.28f, 0.11f, 0.03f);

        [Header("Tamaño y ritmo")]
        [Tooltip("Tamaño (metros) del golpe ligero / fuerte.")]
        [SerializeField] Vector2 size = new(0.55f, 0.85f);
        [Tooltip("Desde el centro del impacto hacia quien golpea (metros): el efecto sale en la superficie.")]
        [SerializeField] float impactRadius = 0.35f;
        [Tooltip("Multiplica la duración de todo (1 = seco como en Persona; más = más lento).")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Golpes a la vez como máximo.")]
        [SerializeField] int poolSize = 3;

        [Header("Golpe fuerte: estrellas")]
        [SerializeField] int heavyStars = 3;
        [SerializeField] float starSize = 0.18f;
        [SerializeField] float starGravity = 9f;

        const float LightDuration = 0.3f;
        const float HeavyDuration = 0.62f;

        sealed class Burst
        {
            public Transform root;
            public Transform billboard;
            public FlatPiece balloon, balloonInner, rays, streaks, shards, accent, flash;
            public FlatPiece[] stars;
            public Vector3[] starVelocity;
            public float[] starSpin;
            public float time = -1f;
            public float scale;
            public float roll;
            public bool heavy;
        }

        Burst[] bursts;
        int next;

        void Awake()
        {
            if (overlayMaterial == null)
            {
                Debug.LogWarning("ImpactVFX: falta overlayMaterial (M_VFX_ToonFlatOverlay).", this);
                enabled = false;
                return;
            }

            bursts = new Burst[Mathf.Max(1, poolSize)];
            for (int i = 0; i < bursts.Length; i++) bursts[i] = CreateBurst(i);
        }

        void OnDisable()
        {
            if (bursts == null) return;
            foreach (Burst burst in bursts) Hide(burst);
        }

        // ---------------- API ----------------

        /// <summary>Golpe que llega desde <paramref name="towardAttacker"/> (dirección hacia quien golpea).</summary>
        public void PlayTowards(Vector3 towardAttacker, bool heavy)
        {
            towardAttacker.y = 0f;
            Vector3 point = transform.position + (towardAttacker.sqrMagnitude > 0.0001f ? towardAttacker.normalized * impactRadius : Vector3.zero);
            Play(point, heavy);
        }

        public void Play(Vector3 point, bool heavy)
        {
            if (bursts == null) return;

            Burst burst = bursts[next];
            next = (next + 1) % bursts.Length;
            Hide(burst);

            burst.time = 0f;
            burst.heavy = heavy;
            burst.scale = heavy ? size.y : size.x;
            burst.roll = Random.Range(0f, 360f);
            burst.root.position = point;

            if (heavy && burst.stars != null)
            {
                for (int i = 0; i < burst.stars.Length; i++)
                {
                    Vector2 flat = Random.insideUnitCircle.normalized;
                    burst.starVelocity[i] = new Vector3(flat.x, 0f, flat.y) * Random.Range(1.6f, 2.6f) + Vector3.up * Random.Range(2.6f, 3.6f);
                    burst.starSpin[i] = Random.Range(380f, 620f) * (Random.value < 0.5f ? -1f : 1f);
                    burst.stars[i].Transform.position = point;
                }
            }
        }

        [VfxTest("Test golpe ligero")]
        void TestLight() => PlayTowards(transform.forward, false);

        [VfxTest("Test golpe fuerte")]
        void TestHeavy() => PlayTowards(transform.forward, true);

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
                Animate(burst, dt);
            }
        }

        void Animate(Burst b, float dt)
        {
            float t = b.time;
            float s = b.scale;
            // El fuerte se estira un poco en el tiempo para que pese más.
            float k = b.heavy ? 1.3f : 1f;
            b.billboard.rotation = FlatFx.Billboard(b.roll);

            // Destello: aparece de golpe y se cierra.
            float flashIn = FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.035f));
            float flashOut = 1f - FlatFx.InCubic(FlatFx.Phase(t, 0.035f, 0.11f * k));
            Show(b.flash, flashIn * flashOut > 0.001f, s * (b.heavy ? 0.55f : 0.48f) * flashIn * flashOut);

            // Rayos: salen disparados y se recogen hacia la punta.
            float rayGrow = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, 0f, 0.07f * k));
            float rayShrink = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, 0.06f * k, 0.25f * k));
            Show(b.rays, rayShrink < 1.55f, s);
            b.rays.Window(rayGrow, rayShrink);

            // Chispas: un trozo corto que viaja hacia fuera.
            float streakHead = 2.2f * FlatFx.OutCubic(FlatFx.Phase(t, 0.01f, 0.24f * k));
            Show(b.streaks, t < 0.24f * k, s * 1.2f);
            b.streaks.Window(streakHead, streakHead - 0.35f);

            // Esquirlas: estallan, aguantan un instante y se recogen.
            float shardGrow = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, 0f, 0.05f));
            float shardShrink = 1.5f * FlatFx.InCubic(FlatFx.Phase(t, 0.11f * k, 0.27f * k));
            float shardPop = 0.85f + 0.15f * FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.08f));
            Show(b.shards, shardShrink < 1.45f, s * 0.82f * shardPop);
            b.shards.Window(shardGrow, shardShrink);

            if (!b.heavy) return;

            // Globo de cómic: estalla y se abre un agujero desde el centro hasta deshacerse.
            float balloonIn = FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.08f), 1.4f);
            float holeOuter = 1.1f * FlatFx.OutCubic(FlatFx.Phase(t, 0.2f, 0.4f));
            float holeInner = 1.1f * FlatFx.OutCubic(FlatFx.Phase(t, 0.15f, 0.33f));
            Show(b.balloon, holeOuter < 1.05f, s * 0.55f * balloonIn);
            b.balloon.Window(FlatFx.Full, holeOuter);
            Show(b.balloonInner, holeInner < 1.05f, s * 0.55f * 0.64f * balloonIn);
            b.balloonInner.Window(FlatFx.Full, holeInner);

            // Esquirlas rojas, un pelín después de las blancas.
            float accentGrow = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, 0.025f, 0.08f));
            float accentShrink = 1.7f * FlatFx.InCubic(FlatFx.Phase(t, 0.16f, 0.36f));
            Show(b.accent, t > 0.025f && accentShrink < 1.65f, s * 0.95f);
            b.accent.Window(accentGrow, accentShrink);

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
                Show(star, true, starSize * grow * fade);
            }
        }

        static void Show(FlatPiece piece, bool visible, float scale)
        {
            if (piece == null) return;
            visible &= scale > 0.0005f;
            piece.Visible = visible;
            if (visible) piece.Transform.localScale = Vector3.one * scale;
        }

        static void Hide(Burst burst)
        {
            burst.time = -1f;
            foreach (FlatPiece piece in new[] { burst.balloon, burst.balloonInner, burst.rays, burst.streaks, burst.shards, burst.accent, burst.flash })
                if (piece != null) piece.Visible = false;
            if (burst.stars == null) return;
            foreach (FlatPiece star in burst.stars) star.Visible = false;
        }

        // ---------------- Montaje ----------------

        Burst CreateBurst(int index)
        {
            int seed = 101 + index * 37;
            Burst b = new();
            // Fuera del personaje: el impacto se queda donde fue aunque el golpeado salga volando.
            b.root = new GameObject($"Impact_{name}_{index}").transform;
            b.root.SetParent(null, false);
            b.billboard = new GameObject("Billboard").transform;
            b.billboard.SetParent(b.root, false);

            // Orden de pintado (overlay): globo, rayos, chispas, esquirlas, destello.
            b.balloon = Piece("Balloon", FlatMeshes.Burst(seed, 14, 0.7f, 0.18f, 0.07f), 0).Colors(accentColor, accentColor, outlineColor);
            b.balloonInner = Piece("BalloonInner", FlatMeshes.Burst(seed + 1, 12, 0.66f, 0.2f, 0f), 1).Colors(Color.white, Color.white, Color.white);
            b.rays = Piece("Rays", FlatMeshes.Rays(seed + 2, 14, 0.12f, new Vector2(0.5f, 1f), new Vector2(0.025f, 0.045f)), 2)
                .Colors(rayColor, rayTipColor, outlineColor).Spread(0.35f);
            b.streaks = Piece("Streaks", FlatMeshes.Rays(seed + 3, 10, 0.15f, new Vector2(0.6f, 0.95f), new Vector2(0.014f, 0.024f), 0.5f), 3)
                .Colors(Color.white, rayTipColor, outlineColor).Spread(0.3f);
            b.shards = Piece("Shards", FlatMeshes.Shards(seed + 4, 6, 0.22f, new Vector2(0.45f, 0.8f), new Vector2(0.07f, 0.11f), 0.045f, 2), 4)
                .Colors(shardColor, shardColor, outlineColor).Spread(0.3f);
            b.accent = Piece("AccentShards", FlatMeshes.Shards(seed + 5, 4, 0.3f, new Vector2(0.5f, 0.85f), new Vector2(0.05f, 0.075f), 0.045f, 3), 5)
                .Colors(accentColor, accentColor, outlineColor).Spread(0.35f);
            b.flash = Piece("Flash", FlatMeshes.Burst(seed + 6, 4, 0.16f, 0.05f, 0.08f, 0.6f), 6)
                .Colors(flashColor, flashColor, outlineColor);

            if (flatMaterial != null && heavyStars > 0)
            {
                Mesh starMesh = FlatMeshes.Star();
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

            FlatPiece Piece(string pieceName, Mesh mesh, int order) => new(pieceName, b.billboard, mesh, overlayMaterial, order);
        }

        void OnDestroy()
        {
            if (bursts == null) return;
            foreach (Burst burst in bursts)
                if (burst.root != null) Destroy(burst.root.gameObject);
        }
    }
}
