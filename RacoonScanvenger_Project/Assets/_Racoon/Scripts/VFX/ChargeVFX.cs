using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Carga del puñetazo potente, discreta: un brillo pequeño con pinchos en el puño que crece poco a poco y late,
    /// de crema a naranja, y unos pocos rayos finos que se cierran sobre él cada vez más deprisa. Al completar la carga,
    /// un destello corto con un anillo pequeño; a partir de ahí se queda naranja latiendo rápido hasta que suelta.
    ///
    /// Montaje: VFX_ChargePunch, hijo del hueso de la mano derecha (DEF-hand.R) en cada prefab de jugador (lo monta
    /// Racoon > VFX > Montar carga del puñetazo en la mano). Los tamaños son en metros reales aunque el hueso esté
    /// escalado. PlayerVFX lo enciende en todos los clientes con PlayerController.ChargingPunchChanged y cada cliente
    /// cuenta el tiempo de carga por su cuenta.
    /// </summary>
    public class ChargeVFX : VfxBehaviour
    {
        [Tooltip("M_VFX_ToonFlat.")]
        [SerializeField] Material flatMaterial;

        [Header("Colores (al empezar → con la carga completa)")]
        [SerializeField] Color startColor = new(1f, 0.97f, 0.86f);
        [SerializeField] Color startTipColor = new(1f, 0.86f, 0.5f);
        [SerializeField] Color startOutline = new(0.66f, 0.55f, 0.3f);
        [SerializeField] Color fullColor = new(1f, 0.76f, 0.46f);
        [SerializeField] Color fullTipColor = new(1f, 0.45f, 0.12f);
        [SerializeField] Color fullOutline = new(0.66f, 0.26f, 0.06f);

        [Header("Forma (metros)")]
        [Tooltip("Tamaño del brillo al empezar y con la carga completa.")]
        [SerializeField] Vector2 orbSize = new(0.03f, 0.09f);
        [Tooltip("Radio desde el que llegan los rayos.")]
        [SerializeField] float raysRadius = 0.22f;
        [Tooltip("Segundos que tarda cada oleada de rayos en llegar al puño: al empezar y con la carga completa.")]
        [SerializeField] Vector2 rayPeriod = new(0.8f, 0.35f);
        [Tooltip("Latidos por segundo del brillo: al empezar y con la carga completa.")]
        [SerializeField] Vector2 pulseRate = new(3f, 9f);
        [Tooltip("Cuánto late (fracción del tamaño).")]
        [SerializeField, Range(0f, 0.5f)] float pulseAmount = 0.08f;
        [Tooltip("Tamaño final del anillo del destello.")]
        [SerializeField] float ringSize = 0.3f;

        FlatPiece orb, ring, rays;
        float time = -1f, duration = 1f, leaveTime = -1f, fullTime;
        bool fullFired;

        void Awake()
        {
            if (flatMaterial == null)
            {
                Debug.LogWarning("ChargeVFX: falta flatMaterial (M_VFX_ToonFlat).", this);
                enabled = false;
                return;
            }

            orb = new FlatPiece("ChargeOrb", transform, FlatMeshes.Burst(7, 8, 0.7f, 0.08f, 0.07f), flatMaterial, 2);
            ring = new FlatPiece("ChargeRing", transform, FlatMeshes.Arc(1f, 0f, 360f, 0.05f, 0f, 48), flatMaterial, 1);
            rays = new FlatPiece("ChargeRays", transform,
                FlatMeshes.Rays(31, 6, 0.25f, new Vector2(0.5f, 0.85f), new Vector2(0.012f, 0.02f)), flatMaterial).Spread(0.5f);
        }

        void OnDisable() => Hide();

        // ---------------- API ----------------

        /// <summary>Empieza a cargar: la carga se completa a los <paramref name="chargeDuration"/> segundos.</summary>
        public void Begin(float chargeDuration)
        {
            if (orb == null) return;
            duration = Mathf.Max(chargeDuration, 0.01f);
            time = 0f;
            leaveTime = -1f;
            fullFired = false;
        }

        /// <summary>Deja de cargar (ha soltado, le han golpeado...): se apaga en un momento.</summary>
        public void End()
        {
            if (time >= 0f && leaveTime < 0f) leaveTime = time;
        }

        [VfxTest("Test carga (3 s)")]
        void Test() => Begin(3f);

        [VfxTest("Soltar")]
        void TestEnd() => End();

        // ---------------- Animación ----------------

        // LateUpdate: después de que la animación mueva (y escale) el hueso de la mano.
        void LateUpdate()
        {
            if (time < 0f) return;
            time += Time.deltaTime;

            float leave = leaveTime >= 0f ? FlatFx.Phase(time, leaveTime, leaveTime + 0.1f) : 0f;
            if (leave >= 1f)
            {
                Hide();
                return;
            }

            // Tamaños en metros aunque el hueso venga escalado del FBX.
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(1f / Mathf.Max(parentScale.x, 1e-4f), 1f / Mathf.Max(parentScale.y, 1e-4f),
                1f / Mathf.Max(parentScale.z, 1e-4f));

            float keep = 1f - FlatFx.InCubic(leave);
            float charge = Mathf.Clamp01(time / duration);
            bool full = charge >= 1f;
            if (full && !fullFired)
            {
                fullFired = true;
                fullTime = time;
            }

            Color fill = Color.Lerp(startColor, fullColor, charge);
            Color tip = Color.Lerp(startTipColor, fullTipColor, charge);
            Color outline = Color.Lerp(startOutline, fullOutline, charge);

            // Brillo: aparece suave, crece con la carga y late cada vez más deprisa.
            float appear = FlatFx.OutCubic(FlatFx.Phase(time, 0f, 0.25f));
            float rate = Mathf.Lerp(pulseRate.x, pulseRate.y, charge);
            float pulse = 1f + pulseAmount * Mathf.Sin(time * rate * Mathf.PI * 2f);
            float pop = fullFired ? 1f + 0.35f * (1f - FlatFx.OutCubic(FlatFx.Phase(time, fullTime, fullTime + 0.2f))) : 1f;
            float size = Mathf.Lerp(orbSize.x, orbSize.y, FlatFx.OutCubic(charge)) * appear * pulse * pop * keep;
            orb.Transform.localPosition = Vector3.zero;
            orb.Transform.rotation = FlatFx.Billboard(time * (full ? 240f : 90f));
            orb.Transform.localScale = Vector3.one * size;
            orb.Colors(fill, tip, outline);
            orb.Visible = size > 0.0005f;

            // Rayos finos que se cierran sobre el puño; con la carga, más rápidos. Se apagan al estar cargado.
            float period = Mathf.Lerp(rayPeriod.x, rayPeriod.y, charge);
            float phase = Mathf.Repeat(time / period, 1f);
            float head = 1f - phase * 1.35f;
            rays.Window(head + 0.3f, head);
            rays.Transform.localPosition = Vector3.zero;
            rays.Transform.rotation = FlatFx.Billboard(time * 30f);
            float raysFade = full ? 1f - FlatFx.Phase(time, fullTime, fullTime + 0.3f) : Mathf.Clamp01(time / 0.3f);
            rays.Transform.localScale = Vector3.one * raysRadius * keep * raysFade;
            rays.Colors(fill, tip, tip);
            rays.Visible = keep * raysFade > 0.01f;

            // Destello al completar la carga: un anillo pequeño que se abre y desaparece.
            ring.Visible = false;
            if (fullFired)
            {
                float t = FlatFx.Phase(time, fullTime, fullTime + 0.25f);
                ring.Visible = t < 1f && keep > 0.01f;
                ring.Transform.localPosition = Vector3.zero;
                ring.Transform.rotation = FlatFx.Billboard();
                ring.Transform.localScale = Vector3.one * Mathf.Lerp(0.05f, ringSize, FlatFx.OutCubic(t)) * 0.5f;
                ring.Colors(fullColor, fullTipColor, fullTipColor);
                ring.Window(FlatFx.Full, FlatFx.InCubic(t));
            }
        }

        void Hide()
        {
            time = -1f;
            leaveTime = -1f;
            fullFired = false;
            if (orb != null) orb.Visible = false;
            if (ring != null) ring.Visible = false;
            if (rays != null) rays.Visible = false;
        }
    }
}
