using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Estrellas de mareo (StarsOnHit): estrellas 3D gorditas (sombreado de dos tonos y contorno) girando
    /// alrededor de la cabeza en una órbita inclinada, cada una dando vueltas sobre sí misma, y unos
    /// remolinos que pasan barriendo. Aparecen con un pequeño rebote y se van encogiendo.
    ///
    /// Montaje: hijo del personaje encima de la cabeza (es el centro de la órbita). PlayerVFX lo lanza con el
    /// golpe fuerte (el 3º, el que derriba) y lo para al levantarse.
    /// </summary>
    public class DizzyStarsVFX : VfxBehaviour
    {
        [Tooltip("M_VFX_ToonFlat.")]
        [SerializeField] Material flatMaterial;
        [Header("Colores")]
        [SerializeField] Color starColor = new(1f, 0.88f, 0.28f);
        [Tooltip("Color hacia las puntas.")]
        [SerializeField] Color starTipColor = new(1f, 0.58f, 0.1f);
        [Tooltip("Tinte de la cara en sombra.")]
        [SerializeField] Color starShadow = new(1f, 0.72f, 0.55f);
        [SerializeField] Color starOutline = new(0.28f, 0.11f, 0.03f);
        [SerializeField] Color swirlColor = new(1f, 0.97f, 0.72f);
        [SerializeField] Color swirlTipColor = new(1f, 0.8f, 0.25f);

        [Header("Órbita")]
        [SerializeField, Range(1, 6)] int starCount = 3;
        [SerializeField] float starSize = 0.2f;
        [SerializeField] float orbitRadius = 0.3f;
        [Tooltip("Grados por segundo.")]
        [SerializeField] float orbitSpeed = 300f;
        [Tooltip("Inclinación de la órbita (grados).")]
        [SerializeField] float tilt = 14f;
        [SerializeField] float bob = 0.03f;
        [SerializeField] bool swirls = true;

        [Header("Duración")]
        [Tooltip("Segundos que siguen tras levantarse.")]
        [SerializeField] float linger = 0.2f;
        [Tooltip("Por si nunca llega el Stop (segundos).")]
        [SerializeField] float maxDuration = 6f;

        FlatPiece[] stars;
        FlatPiece[] swirlPieces;
        Transform orbit;
        float time;
        float stopAt = -1f;
        bool playing;

        void Awake()
        {
            if (flatMaterial == null)
            {
                Debug.LogWarning("DizzyStarsVFX: falta flatMaterial (M_VFX_ToonFlat).", this);
                enabled = false;
                return;
            }

            orbit = new GameObject("Orbit").transform;
            orbit.SetParent(transform, false);

            Mesh starMesh = FlatMeshes.Star();
            stars = new FlatPiece[starCount];
            for (int i = 0; i < starCount; i++)
                stars[i] = new FlatPiece($"Star{i}", orbit, starMesh, flatMaterial)
                    .Colors(starColor, starTipColor, starOutline).Cel(1f, starShadow);

            if (swirls)
            {
                swirlPieces = new FlatPiece[2];
                Mesh swirlMesh = FlatMeshes.Arc(orbitRadius * 1.15f, 0f, 150f, 0.022f, 0.5f);
                for (int i = 0; i < swirlPieces.Length; i++)
                    swirlPieces[i] = new FlatPiece($"Swirl{i}", orbit, swirlMesh, flatMaterial).Colors(swirlColor, swirlTipColor, swirlTipColor);
            }
        }

        void OnDisable() => Hide();

        // ---------------- API ----------------

        /// <summary>Empieza (o sigue, si ya estaba) hasta que llegue <see cref="Stop"/>.</summary>
        public void Play()
        {
            if (stars == null) return;
            if (!playing || stopAt >= 0f) time = playing ? Mathf.Min(time, 0.3f) : 0f;
            playing = true;
            stopAt = -1f;
        }

        /// <summary>Se va (tras <c>linger</c> segundos) encogiéndose.</summary>
        public void Stop()
        {
            if (playing && stopAt < 0f) stopAt = time + linger;
        }

        [VfxTest("Test mareo (2 s)")]
        void Test()
        {
            Play();
            stopAt = time + 2f;
        }

        // ---------------- Animación ----------------

        void Update()
        {
            if (!playing) return;
            time += Time.deltaTime;
            if (time > maxDuration) Stop();

            float leave = stopAt >= 0f ? FlatFx.Phase(time, stopAt, stopAt + 0.18f) : 0f;
            if (leave >= 1f)
            {
                Hide();
                return;
            }
            float keep = 1f - FlatFx.InCubic(leave);

            orbit.localRotation = Quaternion.Euler(tilt + 5f * Mathf.Sin(time * 3f), 0f, 5f * Mathf.Cos(time * 3f));

            for (int i = 0; i < stars.Length; i++)
            {
                float start = i * 0.06f;
                float appear = FlatFx.OutBack(FlatFx.Phase(time, start, start + 0.28f), 2.4f);
                float angle = (time * orbitSpeed + i * 360f / stars.Length) * Mathf.Deg2Rad;
                FlatPiece star = stars[i];
                star.Transform.localPosition = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * orbitRadius
                    + Vector3.up * (bob * Mathf.Sin(time * 7f + i * 2.1f));
                // Mirando a cámara, girando como un molinillo y cabeceando para que se vea gordita.
                star.Transform.rotation = FlatFx.Billboard(time * 240f + i * 50f) * Quaternion.Euler(0f, 35f * Mathf.Sin(time * 8f + i), 0f);
                float scale = starSize * appear * keep;
                star.Visible = scale > 0.001f;
                star.Transform.localScale = Vector3.one * scale;
            }

            if (swirlPieces == null) return;
            for (int i = 0; i < swirlPieces.Length; i++)
            {
                // Pasan barriendo una y otra vez, cada uno a un lado.
                const float period = 0.5f;
                float phase = Mathf.Repeat(time / period + i * 0.5f, 1f);
                FlatPiece swirl = swirlPieces[i];
                swirl.Transform.localRotation = Quaternion.Euler(0f, time * orbitSpeed * 1.4f + i * 180f, 0f);
                swirl.Transform.localPosition = Vector3.up * (i == 0 ? 0.04f : -0.03f);
                swirl.Transform.localScale = Vector3.one * Mathf.Clamp01(time / 0.2f) * keep;
                swirl.Visible = keep > 0.01f;
                swirl.Window(1.3f * FlatFx.OutCubic(phase / 0.6f), 1.3f * FlatFx.OutCubic((phase - 0.35f) / 0.65f));
            }
        }

        void Hide()
        {
            playing = false;
            stopAt = -1f;
            if (stars != null)
                foreach (FlatPiece star in stars) star.Visible = false;
            if (swirlPieces != null)
                foreach (FlatPiece swirl in swirlPieces) swirl.Visible = false;
        }
    }
}
