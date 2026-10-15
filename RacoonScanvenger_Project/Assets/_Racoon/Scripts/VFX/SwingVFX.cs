using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Golpe al aire (estilo Persona 5): una media luna blanca con bordes dorados que barre por delante del
    /// personaje y unas líneas de velocidad hacia delante. Cada golpe barre hacia el lado contrario que el
    /// anterior (izquierda / derecha), como un combo.
    ///
    /// Montaje: hijo del personaje a la altura del pecho, Z hacia delante. Lo lanza PlayerVFX al empezar el
    /// puñetazo (en todos los clientes), conecte o no.
    /// </summary>
    public class SwingVFX : VfxBehaviour
    {
        [Tooltip("M_VFX_ToonFlat (con profundidad: puede quedar detrás del personaje u objetos).")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [SerializeField] Color arcColor = Color.white;
        [SerializeField] Color arcEdgeColor = new(1f, 0.8f, 0.28f);
        [SerializeField] Color lineColor = Color.white;
        [SerializeField] Color lineTipColor = new(1f, 0.8f, 0.28f);

        [Header("Forma")]
        [Tooltip("Radio de la media luna (metros).")]
        [SerializeField] float radius = 0.65f;
        [Tooltip("Grosor máximo de la media luna (metros).")]
        [SerializeField] float width = 0.24f;
        [Tooltip("Inclinación (grados) de la media luna: cada golpe hacia un lado.")]
        [SerializeField] float tilt = 16f;
        [SerializeField] float forwardOffset = 0.05f;
        [SerializeField] bool speedLines = true;

        [Header("Ritmo")]
        [Tooltip("Segundos desde que empieza el puñetazo (el golpe se comprueba a los 0.15 s).")]
        [SerializeField] float delay = 0.04f;
        [SerializeField] float duration = 0.2f;

        FlatPiece arcRight, arcLeft, lines;
        FlatPiece arc;
        float time = -1f;
        bool fromRight;

        void Awake()
        {
            if (flatMaterial == null)
            {
                Debug.LogWarning("SwingVFX: falta flatMaterial (M_VFX_ToonFlat).", this);
                enabled = false;
                return;
            }

            // De derecha a izquierda y al revés (los ángulos van de -Z.. a +X; 0 = delante).
            arcRight = new FlatPiece("SwingArcR", transform, FlatMeshes.Arc(radius, 115f, -45f, width * 0.5f, 0.7f), flatMaterial)
                .Colors(arcColor, arcEdgeColor, arcEdgeColor);
            arcLeft = new FlatPiece("SwingArcL", transform, FlatMeshes.Arc(radius, -115f, 45f, width * 0.5f, 0.7f), flatMaterial)
                .Colors(arcColor, arcEdgeColor, arcEdgeColor);
            if (speedLines)
                lines = new FlatPiece("SpeedLines", transform, FlatMeshes.SpeedLines(7, 3, 0.16f, new Vector2(0.45f, 0.7f), 0.018f), flatMaterial)
                    .Colors(lineColor, lineTipColor, lineTipColor).Spread(0.35f);
        }

        void OnDisable() => Hide();

        // ---------------- API ----------------

        public void Play()
        {
            if (arcRight == null) return;
            Hide();
            fromRight = !fromRight;
            arc = fromRight ? arcRight : arcLeft;
            arc.Transform.localPosition = new Vector3(0f, 0f, forwardOffset);
            arc.Transform.localRotation = Quaternion.Euler(0f, 0f, fromRight ? tilt : -tilt);
            if (lines != null)
            {
                lines.Transform.localPosition = new Vector3(0f, 0f, radius * 0.55f);
                lines.Transform.localRotation = Quaternion.Euler(0f, 0f, fromRight ? tilt : -tilt);
            }
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
            if (t > d * 1.1f)
            {
                Hide();
                return;
            }
            if (t < 0f) return;

            // La cabeza barre el arco y la cola la persigue: un tajo que pasa.
            arc.Visible = true;
            arc.Window(1.25f * FlatFx.OutCubic(FlatFx.Phase(t, 0f, d * 0.55f)), 1.25f * FlatFx.OutCubic(FlatFx.Phase(t, d * 0.3f, d)));
            if (lines != null)
            {
                lines.Visible = t > d * 0.15f;
                lines.Window(1.6f * FlatFx.OutCubic(FlatFx.Phase(t, d * 0.15f, d * 0.55f)), 1.6f * FlatFx.OutCubic(FlatFx.Phase(t, d * 0.45f, d * 1.05f)));
            }
        }

        void Hide()
        {
            time = -1f;
            if (arcRight != null) arcRight.Visible = false;
            if (arcLeft != null) arcLeft.Visible = false;
            if (lines != null) lines.Visible = false;
        }
    }
}
