using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Racoon.VFX
{
    /// <summary>
    /// Impacto de un golpe: un estallido 3D puntiagudo que nace en el punto de la MALLA por donde entra el golpe (se
    /// busca con un rayo desde quien golpea, ver <see cref="CharacterSurface"/>) y sale hacia fuera de la superficie.
    /// Todo nace del punto del impacto (crece desde casi nada) alrededor de un hueco en el centro (hollow), con una
    /// sacudida seca y, opcional, un fogonazo de pinchos en el centro. Impact frames: al golpear se congelan las
    /// animaciones del golpeado y de quien golpea un instante, el efecto va a cámara lenta y el golpeado destella
    /// (blanco o el color de un jugador). Tres variantes (<see cref="Style"/>):
    ///  - Estallido: erizo de agujas largas que revientan en abanico y salen disparadas por la punta.
    ///  - Estrella: estrella de manga en 3D (corona de cuchillas, una larga y otra corta, casi tumbadas sobre la
    ///    superficie) con lanzas que salen rectas hacia quien golpea.
    ///  - Relámpagos: rayos en zigzag que chispean hacia fuera y esquirlas que saltan con gravedad.
    /// Golpe fuerte: más grande y lento, pinchos de acento y estrellas que salen volando.
    /// Todo tiene volumen (SG_VFX_ToonVolume): luz y sombra en dos tonos, brillo especular, luz de borde y contorno.
    /// Se queda pegado al punto de la malla donde entró (aunque se anime o salga despedido; followTarget).
    ///
    /// Montaje: hijo del personaje a la altura del pecho (es el centro hacia el que se lanza el rayo). Lo lanza
    /// PlayerVFX con <see cref="PlayTowards"/> en todos los clientes.
    /// </summary>
    public class ImpactVFX : VfxBehaviour
    {
        public enum Style
        {
            [InspectorName("A · Estallido de agujas")] Burst,
            [InspectorName("B · Estrella de impacto")] Star,
            [InspectorName("C · Relámpagos")] Bolts,
        }

        public enum FlashColor
        {
            [InspectorName("Blanco")] White,
            [InspectorName("Color del golpeado")] Victim,
            [InspectorName("Color de quien golpea")] Attacker,
        }

        [Header("Variante")]
        [SerializeField] Style style = Style.Burst;
        [Tooltip("Radio del hueco del centro, donde entra el golpe (respecto al tamaño; 0 = sin hueco). Los pinchos nacen alrededor.")]
        [SerializeField, Range(0f, 0.6f)] float hollow = 0.2f;
        [Tooltip("Fogonazo de pinchos en el centro (tapa el hueco mientras dura).")]
        [SerializeField] bool coreFlash = false;

        [Header("Impact frames")]
        [Tooltip("Pausa del golpe: se congelan las animaciones del golpeado (y de quien golpea) y el golpeado destella.")]
        [SerializeField] bool impactFrames = true;
        [Tooltip("Segundos de pausa del golpe ligero / fuerte.")]
        [SerializeField] Vector2 freezeDuration = new(0.08f, 0.14f);
        [Tooltip("Congelar también a quien golpea.")]
        [SerializeField] bool freezeAttacker = true;
        [Tooltip("Velocidad del efecto durante la pausa (0 = congelado, 1 = normal): despacio, para que se vea nacer.")]
        [SerializeField, Range(0f, 1f)] float effectSpeedInFreeze = 0.3f;
        [Tooltip("Color del destello del golpeado.")]
        [SerializeField] FlashColor flashColor = FlashColor.White;
        [Tooltip("Color del destello en blanco (por si se quiere un blanco cálido o crema).")]
        [SerializeField] Color flashWhite = Color.white;
        [Tooltip("Segundos entre parpadeos del destello (destello / normal) durante la pausa. 0 = destello fijo.")]
        [SerializeField] float flashInterval = 0.04f;

        [Header("Materiales")]
        [Tooltip("M_VFX_ToonVolume: el impacto, con volumen (luz, sombra, brillo y luz de borde).")]
        [SerializeField] Material volumeMaterial;
        [Tooltip("M_VFX_ToonFlat: estrellas del golpe fuerte.")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [FormerlySerializedAs("flashColor")]
        [SerializeField] Color coreColor = Color.white;
        [SerializeField] Color coreTipColor = new(1f, 0.85f, 0.35f);
        [Tooltip("Pinchos y rayos: base...")]
        [FormerlySerializedAs("crystalColor"), FormerlySerializedAs("rayColor")]
        [SerializeField] Color spikeColor = new(1f, 1f, 0.88f);
        [Tooltip("...y punta.")]
        [FormerlySerializedAs("crystalTipColor"), FormerlySerializedAs("rayTipColor")]
        [SerializeField] Color spikeTipColor = new(1f, 0.7f, 0.08f);
        [Tooltip("Golpe fuerte: pinchos de acento. También las esquirlas.")]
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

        [Header("Golpe cargado (naranja, como su tajo)")]
        [SerializeField] Color chargedCoreColor = new(1f, 0.86f, 0.62f);
        [SerializeField] Color chargedCoreTipColor = new(1f, 0.5f, 0.1f);
        [SerializeField] Color chargedSpikeColor = new(1f, 0.74f, 0.4f);
        [SerializeField] Color chargedSpikeTipColor = new(1f, 0.38f, 0.05f);
        [SerializeField] Color chargedAccentColor = new(1f, 0.58f, 0.22f);
        [SerializeField] Color chargedOutlineColor = new(0.66f, 0.22f, 0.04f);
        [SerializeField] Color chargedStarColor = new(1f, 0.66f, 0.22f);
        [SerializeField] Color chargedStarTipColor = new(1f, 0.36f, 0.06f);
        [Tooltip("Multiplica el tamaño del impacto cargado.")]
        [SerializeField] float chargedSizeMultiplier = 1.2f;

        [Header("Tamaño y ritmo")]
        [Tooltip("Tamaño (metros) del impacto del golpe ligero / fuerte.")]
        [SerializeField] Vector2 size = new(0.55f, 0.85f);
        [Tooltip("Cuánto se queda pegado al punto de la malla donde entró el golpe, aunque se anime o salga despedido (0 = se queda en el aire donde fue el golpe, 1 = pegado).")]
        [SerializeField, Range(0f, 1f)] float followTarget = 1f;
        [Tooltip("Variación del punto de entrada entre golpes (metros: a los lados, en altura), para que cada golpe del combo entre por un sitio. El fuerte, más centrado.")]
        [SerializeField] Vector2 hitScatter = new(0.1f, 0.12f);
        [Tooltip("Sacudida del impacto al nacer (metros, por tamaño 1).")]
        [SerializeField] float shake = 0.05f;
        [Tooltip("Segundos que tarda cada pieza en crecer desde el punto del impacto hasta su tamaño.")]
        [SerializeField] float birthTime = 0.07f;
        [Tooltip("Distancia (metros) a la superficie a la que nace (0 = pegado; negativo = un poco hundido en la malla).")]
        [SerializeField, Range(-0.05f, 0.1f)] float surfaceOffset = 0f;
        [Tooltip("Multiplica la duración de todo (1 = normal; menos = más seco; más = más lento).")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Golpes a la vez como máximo.")]
        [SerializeField] int poolSize = 3;

        [Header("Esquirlas (Relámpagos y golpe fuerte)")]
        [SerializeField] int shards = 6;
        [SerializeField] float shardSize = 0.07f;
        [SerializeField] float shardGravity = 7f;

        [Header("Golpe fuerte: estrellas")]
        [SerializeField] int heavyStars = 3;
        [SerializeField] float starSize = 0.18f;
        [SerializeField] float starGravity = 9f;

        const float LightDuration = 0.34f;
        const float HeavyDuration = 0.62f;
        const float RayStart = 1.5f;

        sealed class Burst
        {
            public Transform root;
            public readonly List<FlatPiece> pieces = new();
            public FlatPiece core;
            // A · Estallido
            public FlatPiece needles, needlesAccent;
            // B · Estrella
            public FlatPiece crown, crownAccent, lances;
            // C · Relámpagos
            public FlatPiece bolts, boltsAccent;
            public FlatPiece[] shards;
            public Vector3[] shardVelocity, shardSpinAxis;
            public float[] shardLife;
            public FlatPiece[] stars;
            public Vector3[] starVelocity;
            public float[] starSpin;
            public Style style;
            public Vector3 point, coreAxis, shakeDir;
            public Quaternion rotation;
            // Seguir a la malla.
            public CharacterSurface.Anchor anchor;
            public Vector3 normalStart, localPoint;
            public Quaternion localRotation;
            public float time = -1f;
            public float scale, roll;
            public bool heavy;
            // Colores puestos ahora mismo (para no repintar si no cambia).
            public bool charged;
        }

        /// <summary>Color del jugador (lo pone PlayerVFX): destello "Color del golpeado".</summary>
        public Color PlayerColor { get; set; } = Color.white;

        Transform character;
        CharacterSurface surface;
        Burst[] bursts;
        int next;

        // Impact frames.
        Animator victimAnimator;
        Material flashMaterial;
        float freezeStart, freezeEnd = -1f;
        bool flashing;
        readonly List<(Animator animator, float speed)> frozen = new();
        readonly List<(Renderer renderer, Material[] materials)> flashRenderers = new();
        readonly List<Material[]> flashArrays = new();

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
            victimAnimator = character.GetComponentInChildren<Animator>();

            // Destello: el mismo shader con un solo color plano (sin sombra, brillo ni luz de borde).
            flashMaterial = new Material(volumeMaterial) { name = "M_VFX_HitFlash (runtime)" };
            flashMaterial.SetFloat(FlatFx.CelAmountId, 0f);
            flashMaterial.SetColor(FlatFx.HighlightColorId, Color.clear);
            flashMaterial.SetColor(FlatFx.RimColorId, Color.clear);
            flashMaterial.SetFloat(FlatFx.RimCutId, 0f);
            flashMaterial.SetFloat(FlatFx.ThicknessId, 1f);
            flashMaterial.SetFloat(FlatFx.SpreadId, 0f);
            flashMaterial.SetFloat(FlatFx.GrowId, 1e6f);
            flashMaterial.SetFloat(FlatFx.ShrinkId, -1e6f);

            Mesh core = FlatMeshes.SpikeBall(0.5f);
            Mesh star = heavyStars > 0 && flatMaterial != null ? FlatMeshes.Star() : null;
            Mesh[] shardMeshes = new Mesh[Mathf.Max(0, shards)];
            for (int i = 0; i < shardMeshes.Length; i++)
                shardMeshes[i] = FlatMeshes.Spikes(900 + i * 13, 1, 0f, 0f, -0.5f, Vector2.one, new Vector2(0.24f, 0.34f), 0.1f, 0.4f, 0.6f, 4);

            bursts = new Burst[Mathf.Max(1, poolSize)];
            for (int i = 0; i < bursts.Length; i++) bursts[i] = CreateBurst(i, core, shardMeshes, star);
        }

        void OnDisable()
        {
            EndImpactFrames();
            if (bursts == null) return;
            foreach (Burst burst in bursts) Hide(burst);
        }

        void OnDestroy()
        {
            EndImpactFrames();
            if (flashMaterial != null) Destroy(flashMaterial);
            surface?.Dispose();
            if (bursts == null) return;
            foreach (Burst burst in bursts)
                if (burst.root != null) Destroy(burst.root.gameObject);
        }

        // ---------------- API ----------------

        /// <summary>
        /// Golpe que llega desde <paramref name="towardAttacker"/> (dirección hacia quien golpea): busca por dónde entra
        /// en la malla y lanza el impacto ahí, con sus impact frames (<paramref name="attackerAnimator"/> también se
        /// congela; <paramref name="attackerColor"/> es el color del destello "Color de quien golpea").
        /// </summary>
        /// <param name="charged">Puñetazo cargado: el impacto sale naranja (charged*) y algo más grande.</param>
        public void PlayTowards(Vector3 towardAttacker, bool heavy, Animator attackerAnimator = null, Color? attackerColor = null,
                                bool charged = false)
        {
            if (bursts == null) return;
            StartImpactFrames(heavy, attackerAnimator, attackerColor ?? PlayerColor);
            towardAttacker.y = 0f;
            if (towardAttacker.sqrMagnitude < 1e-4f) towardAttacker = character.forward;
            towardAttacker.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, towardAttacker);
            float scatter = heavy ? 0.35f : 1f;
            Vector3 target = transform.position
                + side * (Random.Range(-1f, 1f) * hitScatter.x * scatter)
                + Vector3.up * (Random.Range(-1f, 1f) * hitScatter.y * scatter);
            surface.FindEntry(target + towardAttacker * RayStart, -towardAttacker, RayStart * 2f, out Vector3 point, out Vector3 normal,
                out CharacterSurface.Anchor anchor);
            Play(point, normal, towardAttacker, heavy, anchor, charged);
        }

        /// <summary>
        /// Impacto en <paramref name="point"/> de la superficie, saliendo hacia <paramref name="normal"/>. Con
        /// <paramref name="anchor"/> se queda pegado a ese punto de la malla aunque se anime.
        /// </summary>
        public void Play(Vector3 point, Vector3 normal, Vector3 towardAttacker, bool heavy, CharacterSurface.Anchor? anchor = null,
                         bool charged = false)
        {
            if (bursts == null) return;

            Burst burst = bursts[next];
            next = (next + 1) % bursts.Length;
            Hide(burst);
            Recolor(burst, charged);

            // Entre la normal de la malla y la dirección del golpe: así no sale torcido si toca una oreja o un brazo.
            Vector3 axis = (normal.normalized + towardAttacker.normalized).normalized;
            if (axis.sqrMagnitude < 0.5f) axis = towardAttacker.sqrMagnitude > 0f ? towardAttacker.normalized : -character.forward;

            burst.style = style;
            burst.time = 0f;
            burst.heavy = heavy;
            burst.scale = (heavy ? size.y : size.x) * (charged ? chargedSizeMultiplier : 1f);
            burst.point = point + axis * surfaceOffset;
            burst.roll = Random.Range(0f, 360f);
            burst.rotation = Quaternion.LookRotation(axis, Mathf.Abs(axis.y) > 0.95f ? Vector3.forward : Vector3.up) * Quaternion.Euler(0f, 0f, burst.roll);
            burst.root.SetPositionAndRotation(burst.point, burst.rotation);
            // Para seguir a la malla: el punto de la malla y su normal ahora, o (sin malla) en el espacio del personaje.
            burst.anchor = anchor ?? CharacterSurface.Anchor.None;
            burst.normalStart = normal.normalized;
            burst.localPoint = character.InverseTransformPoint(burst.point);
            burst.localRotation = Quaternion.Inverse(character.rotation) * burst.rotation;
            // La sacudida va en la dirección del golpe (hacia dentro y fuera), con algo de lado.
            burst.shakeDir = (-axis + Random.insideUnitSphere * 0.5f).normalized;

            burst.core.Transform.localRotation = Random.rotationUniform;
            burst.coreAxis = Random.onUnitSphere;

            if (burst.shards != null)
            {
                for (int i = 0; i < burst.shards.Length; i++)
                {
                    Vector3 spray = Random.insideUnitSphere;
                    spray -= axis * Vector3.Dot(spray, axis);
                    burst.shardVelocity[i] = axis * Random.Range(1.6f, 2.6f) + spray.normalized * Random.Range(1.2f, 2.6f) + Vector3.up * Random.Range(0.6f, 1.4f);
                    burst.shardVelocity[i] *= burst.scale / size.x;
                    burst.shardSpinAxis[i] = Random.onUnitSphere;
                    burst.shardLife[i] = Random.Range(0.22f, 0.32f) * (heavy ? 1.3f : 1f);
                    // Del borde del hueco.
                    burst.shards[i].Transform.SetPositionAndRotation(burst.point + spray.normalized * (hollow * burst.scale),
                        Quaternion.LookRotation(burst.shardVelocity[i]));
                }
            }

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

        [VfxTest("Test golpe cargado (naranja)")]
        void TestCharged() => PlayTowards(TowardCamera(), true, null, null, true);

        [VfxTest("Variante A · Estallido")]
        void TestBurst() => TestStyle(Style.Burst);

        [VfxTest("Variante B · Estrella")]
        void TestStar() => TestStyle(Style.Star);

        [VfxTest("Variante C · Relámpagos")]
        void TestBolts() => TestStyle(Style.Bolts);

        // Cambia la variante (se queda puesta) y la prueba con un golpe ligero.
        void TestStyle(Style value)
        {
            style = value;
            TestLight();
        }

        // En los tests el golpe entra por el lado que se ve.
        Vector3 TowardCamera()
        {
            Camera cam = FlatFx.Camera;
            return cam != null ? -cam.transform.forward : transform.forward;
        }

        // ---------------- Animación ----------------

        void Update()
        {
            UpdateImpactFrames();
            if (bursts == null) return;
            // Durante la pausa el efecto va despacio: se ve nacer del punto del impacto.
            float speed = Time.time < freezeEnd ? effectSpeedInFreeze : 1f;
            float dt = Time.deltaTime * speed / Mathf.Max(timeScale, 0.01f);
            foreach (Burst burst in bursts)
            {
                if (burst.time < 0f) continue;
                burst.time += dt;
                if (burst.time > (burst.heavy ? HeavyDuration : LightDuration))
                {
                    Hide(burst);
                    continue;
                }
                // Sacudida seca que se apaga enseguida (va y viene).
                float t = burst.time;
                float jolt = Mathf.Sin(t * 110f) * (1f - FlatFx.Phase(t, 0f, burst.heavy ? 0.14f : 0.09f));
                Follow(burst, out Vector3 position, out Quaternion rotation);
                burst.root.SetPositionAndRotation(position + burst.shakeDir * (shake * burst.scale * jolt), rotation);
                Animate(burst, dt);
            }
        }

        // Pegado al punto de la malla donde entró el golpe (sigue a la malla animada y al golpeado al salir despedido),
        // mezclado con quedarse donde fue el golpe según followTarget.
        void Follow(Burst b, out Vector3 position, out Quaternion rotation)
        {
            if (b.anchor.Valid && surface.Evaluate(b.anchor, out Vector3 point, out Vector3 normal))
            {
                // Gira lo que haya girado la superficie desde el golpe.
                rotation = Quaternion.FromToRotation(b.normalStart, normal) * b.rotation;
                position = point + rotation * Vector3.forward * surfaceOffset;
            }
            else
            {
                position = character.TransformPoint(b.localPoint);
                rotation = character.rotation * b.localRotation;
            }
            position = Vector3.Lerp(b.point, position, followTarget);
            rotation = Quaternion.Slerp(b.rotation, rotation, followTarget);
        }

        void Animate(Burst b, float dt)
        {
            float t = b.time;
            float s = b.scale;
            // El fuerte se estira un poco en el tiempo para que pese más.
            float k = b.heavy ? 1.3f : 1f;

            // Fogonazo (opcional): bola de pinchos que revienta de golpe, gira y se cierra.
            if (coreFlash)
            {
                float coreIn = FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.035f), 2.4f);
                float coreOut = 1f - FlatFx.InCubic(FlatFx.Phase(t, 0.035f, 0.12f * k));
                Show(b.core, coreOut > 0f, Vector3.one * (s * (b.heavy ? 0.42f : 0.34f) * coreIn * coreOut));
                b.core.Transform.localRotation = Quaternion.AngleAxis(1100f * dt, b.coreAxis) * b.core.Transform.localRotation;
            }

            switch (b.style)
            {
                case Style.Burst: AnimateBurst(b, t, s, k); break;
                case Style.Star: AnimateStar(b, t, s, k); break;
                case Style.Bolts: AnimateBolts(b, t, s, k); break;
            }

            if (b.shards != null && (b.style == Style.Bolts || b.heavy)) AnimateShards(b, t, dt);
            if (b.heavy) AnimateStars(b, t, dt);
        }

        // Crece desde el punto del impacto (casi nada) hasta su tamaño, con un pelín de rebote.
        float Born(float t, float start, float k) =>
            Mathf.LerpUnclamped(0.04f, 1f, FlatFx.OutBack(FlatFx.Phase(t, start * k, (start + Mathf.Max(birthTime, 0.005f)) * k), 1.5f));

        // A · Agujas que nacen del impacto, revientan en abanico y salen disparadas por la punta.
        void AnimateBurst(Burst b, float t, float s, float k)
        {
            Needles(b.needles, 0f, 0.09f, 0.26f, s * 1.05f, 25f);
            if (b.heavy) Needles(b.needlesAccent, 0.025f, 0.13f, 0.36f, s * 1.15f, -18f);

            void Needles(FlatPiece piece, float start, float leave, float gone, float size, float spin)
            {
                float grow = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, start * k, (start + birthTime) * k));
                float shrink = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, leave * k, gone * k));
                Show(piece, t >= start * k && shrink < 1.55f, Vector3.one * (size * Born(t, start, k)));
                piece.Window(grow, shrink);
                piece.Transform.localRotation = Quaternion.Euler(0f, 0f, spin * FlatFx.OutCubic(FlatFx.Phase(t, 0f, gone * k)));
            }
        }

        // B · La estrella nace del impacto abriéndose y girando un poco, las lanzas salen rectas; luego todo se recoge
        // hacia las puntas.
        void AnimateStar(Burst b, float t, float s, float k)
        {
            Crown(b.crown, 0f, s * 0.9f, -14f, 10f);
            if (b.heavy) Crown(b.crownAccent, 0.02f, s * 1.12f, 12f, -8f);

            float lanceGrow = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, 0.01f * k, (0.01f + birthTime) * k));
            float lanceShrink = 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, 0.1f * k, 0.25f * k));
            Show(b.lances, t >= 0.01f * k && lanceShrink < 1.55f, Vector3.one * (s * 1f * Born(t, 0.01f, k)));
            b.lances.Window(lanceGrow, lanceShrink);

            void Crown(FlatPiece piece, float start, float size, float fromDeg, float toDeg)
            {
                float open = Born(t, start, k);
                float shrink = 1.5f * FlatFx.OutCubic(FlatFx.Phase(t, (start + 0.12f) * k, (start + 0.3f) * k));
                Show(piece, t >= start * k && shrink < 1.45f, Vector3.one * (size * open));
                piece.Window(FlatFx.Full, shrink);
                piece.Transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(fromDeg, toDeg, open));
            }
        }

        // C · Los rayos nacen del impacto y chispean hacia fuera: se dibujan de la base a la punta, parpadean una vez y
        // se van por la punta.
        void AnimateBolts(Burst b, float t, float s, float k)
        {
            Zap(b.bolts, 0f, s * 1.05f);
            if (b.heavy) Zap(b.boltsAccent, 0.03f, s * 1.2f);

            void Zap(FlatPiece piece, float start, float size)
            {
                float local = t - start * k;
                float grow = 1.6f * FlatFx.OutCubic(FlatFx.Phase(local, 0f, birthTime * k));
                float shrink = 1.6f * FlatFx.OutCubic(FlatFx.Phase(local, 0.11f * k, 0.27f * k));
                // Parpadeo eléctrico: se apaga un instante y vuelve un poco girado.
                bool flicker = local > 0.08f * k && local < 0.095f * k;
                Show(piece, local >= 0f && !flicker && shrink < 1.55f, Vector3.one * (size * Born(t, start, k)));
                piece.Window(grow, shrink);
                piece.Transform.localRotation = Quaternion.Euler(0f, 0f, local > 0.095f * k ? 9f : 0f);
            }
        }

        // ---------------- Impact frames ----------------

        /// <summary>Segundos de pausa de un golpe (0 sin impact frames): PlayerVFX espera esto para empezar a parpadear.</summary>
        public float FreezeTime(bool heavy) => impactFrames && enabled ? Mathf.Max(0f, heavy ? freezeDuration.y : freezeDuration.x) : 0f;

        void StartImpactFrames(bool heavy, Animator attackerAnimator, Color attackerColor)
        {
            EndImpactFrames();
            float duration = FreezeTime(heavy);
            if (duration <= 0f) return;
            freezeStart = Time.time;
            freezeEnd = freezeStart + duration;

            Freeze(victimAnimator);
            if (freezeAttacker) Freeze(attackerAnimator);

            Color color = flashColor switch
            {
                FlashColor.Victim => PlayerColor,
                FlashColor.Attacker => attackerColor,
                _ => flashWhite,
            };
            color.a = 1f;
            flashMaterial.SetColor(FlatFx.BaseColorId, color);
            flashMaterial.SetColor(FlatFx.TipColorId, color);
            flashMaterial.SetColor(FlatFx.OutlineColorId, color);
            flashMaterial.SetColor(FlatFx.ShadowColorId, Color.white);

            // Se recogen al empezar (entran los objetos equipados); los efectos no.
            foreach (Renderer rend in character.GetComponentsInChildren<Renderer>())
            {
                if (!(rend is MeshRenderer || rend is SkinnedMeshRenderer) || rend.GetComponentInParent<VfxBehaviour>() != null) continue;
                Material[] materials = rend.sharedMaterials;
                flashRenderers.Add((rend, materials));
                Material[] flash = new Material[materials.Length];
                for (int i = 0; i < flash.Length; i++) flash[i] = flashMaterial;
                flashArrays.Add(flash);
            }
            SetFlash(true);
        }

        void Freeze(Animator animator)
        {
            if (animator == null) return;
            foreach ((Animator a, float _) in frozen)
                if (a == animator) return;
            // Si ya lo ha congelado otro impacto, al acabar vuelve a velocidad normal.
            frozen.Add((animator, animator.speed > 0f ? animator.speed : 1f));
            animator.speed = 0f;
        }

        void UpdateImpactFrames()
        {
            if (freezeEnd < 0f) return;
            if (Time.time >= freezeEnd)
            {
                EndImpactFrames();
                return;
            }
            // Destello / normal / destello...
            SetFlash(flashInterval <= 0f || (int)((Time.time - freezeStart) / flashInterval) % 2 == 0);
        }

        void EndImpactFrames()
        {
            freezeEnd = -1f;
            SetFlash(false);
            flashRenderers.Clear();
            flashArrays.Clear();
            foreach ((Animator animator, float speed) in frozen)
                if (animator != null) animator.speed = speed;
            frozen.Clear();
        }

        void SetFlash(bool on)
        {
            if (flashing == on) return;
            flashing = on;
            for (int i = 0; i < flashRenderers.Count; i++)
            {
                (Renderer rend, Material[] materials) = flashRenderers[i];
                if (rend != null) rend.sharedMaterials = on ? flashArrays[i] : materials;
            }
        }

        void AnimateShards(Burst b, float t, float dt)
        {
            for (int i = 0; i < b.shards.Length; i++)
            {
                FlatPiece shard = b.shards[i];
                float life = b.shardLife[i];
                if (t > life)
                {
                    shard.Visible = false;
                    continue;
                }
                b.shardVelocity[i] += Vector3.down * (shardGravity * dt);
                b.shardVelocity[i] *= 1f - Mathf.Clamp01(3f * dt);
                Transform tr = shard.Transform;
                tr.position += b.shardVelocity[i] * dt;
                tr.rotation = Quaternion.AngleAxis(1300f * dt, b.shardSpinAxis[i]) * tr.rotation;
                float grow = FlatFx.OutBack(FlatFx.Phase(t, 0f, 0.05f));
                float fade = 1f - FlatFx.InCubic(FlatFx.Phase(t, life * 0.55f, life));
                Show(shard, true, new Vector3(0.75f, 0.75f, 1.4f) * (shardSize * (b.scale / size.x) * grow * fade));
            }
        }

        void AnimateStars(Burst b, float t, float dt)
        {
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

        // Paleta normal o la naranja del golpe cargado (las piezas se comparten entre los dos).
        void Recolor(Burst b, bool charged)
        {
            if (b.charged == charged) return;
            b.charged = charged;
            Color core = charged ? chargedCoreColor : coreColor, coreTip = charged ? chargedCoreTipColor : coreTipColor;
            Color spike = charged ? chargedSpikeColor : spikeColor, tip = charged ? chargedSpikeTipColor : spikeTipColor;
            Color accent = charged ? chargedAccentColor : accentColor, outline = charged ? chargedOutlineColor : outlineColor;

            b.core?.Colors(core, coreTip, outline);
            foreach (FlatPiece piece in new[] { b.needles, b.crown, b.lances, b.bolts }) piece?.Colors(spike, tip, outline);
            foreach (FlatPiece piece in new[] { b.needlesAccent, b.crownAccent, b.boltsAccent }) piece?.Colors(accent, tip, outline);
            if (b.shards != null)
                for (int i = 0; i < b.shards.Length; i++) b.shards[i].Colors(i % 2 == 0 ? spike : accent, tip, outline);
            if (b.stars != null)
                foreach (FlatPiece star in b.stars)
                    star.Colors(charged ? chargedStarColor : starColor, charged ? chargedStarTipColor : starTipColor, starOutline);
        }

        static void Hide(Burst burst)
        {
            burst.time = -1f;
            foreach (FlatPiece piece in burst.pieces) piece.Visible = false;
        }

        // ---------------- Montaje ----------------

        Burst CreateBurst(int index, Mesh coreMesh, Mesh[] shardMeshes, Mesh starMesh)
        {
            int seed = 101 + index * 37;
            Burst b = new();
            // Fuera del personaje: se pega a su malla por código (followTarget).
            b.root = new GameObject($"Impact_{character.name}_{index}").transform;

            b.core = Volume("Core", coreMesh, coreColor, coreTipColor);

            // Todo nace a ras de la superficie (sin distancia hacia fuera), alrededor del hueco.
            // A · Estallido: agujas finas (en punta desde la base) por casi media esfera.
            b.needles = Volume("Needles",
                FlatMeshes.Spikes(seed, 13, 12f, 70f, 0f, new Vector2(0.5f, 1f), new Vector2(0.045f, 0.065f), 0.035f, 0.1f, 1f, 4, false, hollow),
                spikeColor, spikeTipColor).Spread(0.35f);
            b.needlesAccent = Volume("NeedlesAccent",
                FlatMeshes.Spikes(seed + 1, 9, 25f, 85f, 0.01f, new Vector2(0.45f, 0.8f), new Vector2(0.04f, 0.055f), 0.035f, 0.1f, 1f, 4, false, hollow),
                accentColor, spikeTipColor).Spread(0.35f);

            // B · Estrella: corona de cuchillas planas casi tumbadas (una larga, otra corta) y lanzas hacia fuera.
            b.crown = Volume("Crown",
                FlatMeshes.Spikes(seed + 2, 10, 58f, 80f, 0f, new Vector2(0.85f, 1f), new Vector2(0.1f, 0.12f), 0.035f, 0.16f, 0.45f, 4, true, hollow),
                spikeColor, spikeTipColor);
            b.crownAccent = Volume("CrownAccent",
                FlatMeshes.Spikes(seed + 3, 8, 65f, 85f, 0.01f, new Vector2(0.8f, 1f), new Vector2(0.085f, 0.1f), 0.035f, 0.16f, 0.45f, 4, true, hollow),
                accentColor, spikeTipColor);
            b.lances = Volume("Lances",
                FlatMeshes.Spikes(seed + 4, 3, 0f, 18f, 0f, new Vector2(0.95f, 1.2f), new Vector2(0.05f, 0.06f), 0.035f, 0.12f, 1f, 4, false, hollow),
                spikeColor, spikeTipColor).Spread(0.25f);

            // C · Relámpagos: rayos en zigzag.
            b.bolts = Volume("Bolts",
                FlatMeshes.Bolts(seed + 5, 6, 70f, 0f, new Vector2(0.6f, 1f), 0.05f, 3, 0.16f, 0.03f, hollow),
                spikeColor, spikeTipColor).Spread(0.3f);
            b.boltsAccent = Volume("BoltsAccent",
                FlatMeshes.Bolts(seed + 6, 4, 82f, 0.01f, new Vector2(0.5f, 0.85f), 0.045f, 4, 0.2f, 0.03f, hollow),
                accentColor, spikeTipColor).Spread(0.3f);

            if (shardMeshes.Length > 0)
            {
                b.shards = new FlatPiece[shardMeshes.Length];
                b.shardVelocity = new Vector3[shardMeshes.Length];
                b.shardSpinAxis = new Vector3[shardMeshes.Length];
                b.shardLife = new float[shardMeshes.Length];
                for (int i = 0; i < shardMeshes.Length; i++)
                    b.shards[i] = Volume($"Shard{i}", shardMeshes[i], i % 2 == 0 ? spikeColor : accentColor, spikeTipColor);
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
                    b.pieces.Add(b.stars[i]);
                }
            }
            return b;

            FlatPiece Volume(string pieceName, Mesh mesh, Color baseColor, Color tipColor)
            {
                FlatPiece piece = new FlatPiece(pieceName, b.root, mesh, volumeMaterial)
                    .Colors(baseColor, tipColor, outlineColor).Cel(1f, shadowTint).Shine(highlightColor, rimColor);
                b.pieces.Add(piece);
                return piece;
            }
        }
    }
}
