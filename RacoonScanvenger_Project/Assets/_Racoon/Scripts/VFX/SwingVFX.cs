using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Golpe al aire: un tajo 3D en media luna (sección aplanada, gordo en el centro y en punta por los extremos)
    /// que barre por delante del personaje, y unas líneas de velocidad con volumen hacia delante. Cada golpe barre
    /// hacia el lado contrario que el anterior (izquierda / derecha), como un combo. Con volumen (SG_VFX_ToonVolume):
    /// luz y sombra en dos tonos, brillo especular, luz de borde y contorno; caras blancas y filos dorados.
    ///
    /// Montaje: hijo del personaje a la altura del pecho, Z hacia delante. Lo lanza PlayerVFX al empezar el
    /// puñetazo (en todos los clientes), conecte o no.
    /// </summary>
    public class SwingVFX : VfxBehaviour
    {
        [Header("Materiales")]
        [Tooltip("M_VFX_ToonVolume: tajo y líneas con volumen.")]
        [SerializeField] Material volumeMaterial;
        [Tooltip("M_VFX_ToonFlat: solo si falta volumeMaterial.")]
        [SerializeField] Material flatMaterial;

        [Header("Colores")]
        [Tooltip("Caras del tajo...")]
        [SerializeField] Color arcColor = Color.white;
        [Tooltip("...y sus filos.")]
        [SerializeField] Color arcEdgeColor = new(1f, 0.8f, 0.28f);
        [SerializeField] Color lineColor = Color.white;
        [SerializeField] Color lineTipColor = new(1f, 0.8f, 0.28f);
        [SerializeField] Color outlineColor = new(0.85f, 0.58f, 0.14f);
        [Tooltip("Multiplica el color en la cara en sombra.")]
        [SerializeField] Color shadowTint = new(1f, 0.85f, 0.65f);
        [Tooltip("Brillo especular (alfa = intensidad).")]
        [SerializeField] Color highlightColor = Color.white;
        [Tooltip("Luz de borde en la silueta (alfa = intensidad).")]
        [SerializeField] Color rimColor = new(1f, 1f, 0.9f, 0.8f);

        [Header("Forma")]
        [Tooltip("Radio de la media luna (metros).")]
        [SerializeField] float radius = 0.65f;
        [Tooltip("Ancho máximo de la media luna, hacia fuera (metros).")]
        [SerializeField] float width = 0.24f;
        [Tooltip("Grosor máximo de la media luna, en vertical (metros).")]
        [SerializeField] float thickness = 0.09f;
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
            Material material = volumeMaterial != null ? volumeMaterial : flatMaterial;
            if (material == null)
            {
                Debug.LogWarning("SwingVFX: falta volumeMaterial (M_VFX_ToonVolume).", this);
                enabled = false;
                return;
            }

            // De derecha a izquierda y al revés (los ángulos van de -Z.. a +X; 0 = delante).
            arcRight = Volume("SwingArcR", FlatMeshes.SwingBlade(radius, 115f, -45f, width * 0.5f, thickness * 0.5f, 0.7f, 0.012f))
                .Colors(arcColor, arcEdgeColor, outlineColor);
            arcLeft = Volume("SwingArcL", FlatMeshes.SwingBlade(radius, -115f, 45f, width * 0.5f, thickness * 0.5f, 0.7f, 0.012f))
                .Colors(arcColor, arcEdgeColor, outlineColor);
            if (speedLines)
                lines = Volume("SpeedLines", FlatMeshes.SpeedSpindles(7, 3, 0.16f, new Vector2(0.45f, 0.7f), 0.016f, 0.006f))
                    .Colors(lineColor, lineTipColor, outlineColor).Spread(0.35f);

            FlatPiece Volume(string pieceName, Mesh mesh) => new FlatPiece(pieceName, transform, mesh, material)
                .Cel(1f, shadowTint).Shine(highlightColor, rimColor);
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
