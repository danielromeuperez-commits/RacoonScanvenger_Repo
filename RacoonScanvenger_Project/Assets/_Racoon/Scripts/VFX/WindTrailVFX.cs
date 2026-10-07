using System.Collections;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Estelas de viento (TrailRenderers hijos) que siguen al personaje. Cada vez que se activa el
    /// efecto se reparten en posiciones distintas alrededor de la silueta, con grosor y duración
    /// aleatorios, y ondulan un poco para que parezcan aire y no líneas rectas.
    ///
    /// Montaje: raíz con este componente + varios hijos con TrailRenderer. El efecto mira hacia la
    /// dirección del movimiento (Z local = hacia delante); las estelas se generan detrás.
    /// Para soltarlo, el que lo lanza desemparenta el objeto y pone emitting = false (PlayerVFX lo hace).
    /// </summary>
    public class WindTrailVFX : MonoBehaviour
    {
        [Tooltip("Vacío = todos los TrailRenderer hijos.")]
        [SerializeField] TrailRenderer[] trails;
        [Tooltip("Cuántas estelas se usan en cada dash (mín, máx incluidos).")]
        [SerializeField] Vector2Int activeCount = new(3, 5);

        [Header("Reparto (espacio local)")]
        [Tooltip("Altura del centro de la silueta.")]
        [SerializeField] float centerHeight = 0.6f;
        [Tooltip("Semiejes de la elipse alrededor de la silueta (X = ancho, Y = alto).")]
        [SerializeField] Vector2 ellipseRadius = new(0.55f, 0.45f);
        [Tooltip("Rango de radio normalizado: valores altos = por el borde de la silueta (no tapadas por el cuerpo).")]
        [SerializeField] Vector2 radiusRange = new(0.55f, 1f);
        [Tooltip("Desplazamiento adelante/atrás aleatorio para que no empiecen todas alineadas.")]
        [SerializeField] Vector2 depthRange = new(-0.35f, 0.15f);
        [Tooltip("Distancia mínima entre estelas (en el plano X/Y).")]
        [SerializeField] float minSeparation = 0.25f;

        [Header("Variación por estela")]
        [SerializeField] Vector2 widthRange = new(0.05f, 0.12f);
        [Tooltip("Segundos que tarda en desvanecerse la cola.")]
        [SerializeField] Vector2 timeRange = new(0.18f, 0.32f);

        [Header("Ondulación")]
        [SerializeField] float wobbleAmplitude = 0.06f;
        [SerializeField] float wobbleFrequency = 14f;

        [Header("Color")]
        [Tooltip("Multiplica el Color del TrailRenderer (déjalo blanco y tíñelo con SetTint).")]
        [SerializeField] Color tint = Color.white;

        [Header("Test (botón del Inspector, solo en Play)")]
        [Tooltip("Metros que recorre el efecto hacia delante (Z local) en la prueba.")]
        [SerializeField] float testDistance = 3f;
        [SerializeField] float testDuration = 0.25f;

        Vector3[] basePositions;
        Vector3[] wobbleAxes;
        float[] phases;
        Gradient[] baseGradients;
        Coroutine testRoutine;

        void Awake()
        {
            if (trails == null || trails.Length == 0) trails = GetComponentsInChildren<TrailRenderer>(true);

            basePositions = new Vector3[trails.Length];
            wobbleAxes = new Vector3[trails.Length];
            phases = new float[trails.Length];
            baseGradients = new Gradient[trails.Length];
            for (int i = 0; i < trails.Length; i++)
            {
                baseGradients[i] = new Gradient();
                baseGradients[i].SetKeys(trails[i].colorGradient.colorKeys, trails[i].colorGradient.alphaKeys);
            }
        }

        // Instantiate ya ha colocado el objeto cuando llega aquí, así que se puede repartir directamente.
        void OnEnable()
        {
            Randomize();
            ApplyTint();
        }

        /// <summary>Tiñe todas las estelas (p. ej. con el color del jugador). Llamar justo tras instanciar.</summary>
        public void SetTint(Color color)
        {
            tint = color;
            if (baseGradients != null) ApplyTint();
        }

        /// <summary>
        /// Simula un dash: reparte las estelas, mueve el efecto hacia delante y lo devuelve a su sitio.
        /// Solo en Play (los TrailRenderer necesitan moverse para dibujar algo).
        /// </summary>
        public void PlayTest()
        {
            if (!Application.isPlaying) return;
            if (testRoutine != null) StopCoroutine(testRoutine);
            testRoutine = StartCoroutine(TestDash());
        }

        IEnumerator TestDash()
        {
            Vector3 start = transform.position;
            Vector3 end = start + transform.forward * testDistance;
            Randomize();
            ApplyTint();

            for (float t = 0f; t < testDuration; t += Time.deltaTime)
            {
                float k = t / testDuration;
                transform.position = Vector3.Lerp(start, end, 1f - (1f - k) * (1f - k)); // Ease-out, como el dash.
                yield return null;
            }
            transform.position = end;

            // Igual que al acabar un dash real: dejan de emitir y la cola se desvanece.
            foreach (TrailRenderer trail in trails) trail.emitting = false;
            yield return new WaitForSeconds(timeRange.y + 0.2f);

            transform.position = start;
            foreach (TrailRenderer trail in trails) trail.Clear();
            testRoutine = null;
        }

        void Randomize()
        {
            int count = Mathf.Clamp(Random.Range(activeCount.x, activeCount.y + 1), 0, trails.Length);
            Shuffle();

            int placed = 0;
            for (int i = 0; i < trails.Length; i++)
            {
                TrailRenderer trail = trails[i];
                bool active = i < count;
                trail.gameObject.SetActive(active);
                if (!active) continue;

                Vector2 point = PickPoint(placed);
                basePositions[i] = new Vector3(point.x, centerHeight + point.y, Random.Range(depthRange.x, depthRange.y));
                placed++;

                // Ondula perpendicular a su posición respecto al centro (como aire que rodea el cuerpo).
                Vector2 tangent = point.sqrMagnitude > 0.0001f ? new Vector2(-point.y, point.x).normalized : Vector2.up;
                wobbleAxes[i] = new Vector3(tangent.x, tangent.y, 0f);
                phases[i] = Random.Range(0f, Mathf.PI * 2f);

                trail.transform.localPosition = basePositions[i];
                trail.widthMultiplier = Random.Range(widthRange.x, widthRange.y);
                trail.time = Random.Range(timeRange.x, timeRange.y);
                trail.emitting = true;
                trail.Clear(); // Sin segmentos desde la posición anterior (por si se reutiliza).
            }
        }

        // Punto en un anillo de la elipse, separado de los ya colocados (unos cuantos intentos).
        Vector2 PickPoint(int placed)
        {
            Vector2 best = Vector2.zero;
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float radius = Random.Range(radiusRange.x, radiusRange.y);
                Vector2 candidate = new(Mathf.Cos(angle) * ellipseRadius.x * radius, Mathf.Sin(angle) * ellipseRadius.y * radius);

                float nearest = float.MaxValue;
                for (int j = 0; j < placed; j++)
                    nearest = Mathf.Min(nearest, Vector2.Distance(candidate, (Vector2)basePositions[j] - Vector2.up * centerHeight));

                if (nearest >= minSeparation) return candidate;
                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    best = candidate;
                }
            }
            return best;
        }

        void Update()
        {
            if (wobbleAmplitude <= 0f) return;
            float time = Time.time * wobbleFrequency;
            for (int i = 0; i < trails.Length; i++)
            {
                TrailRenderer trail = trails[i];
                if (!trail.gameObject.activeSelf || !trail.emitting) continue;
                trail.transform.localPosition = basePositions[i] + wobbleAxes[i] * (Mathf.Sin(time + phases[i]) * wobbleAmplitude);
            }
        }

        void ApplyTint()
        {
            for (int i = 0; i < trails.Length; i++)
            {
                GradientColorKey[] colors = baseGradients[i].colorKeys;
                GradientAlphaKey[] alphas = baseGradients[i].alphaKeys;
                for (int k = 0; k < colors.Length; k++) colors[k].color *= tint;
                for (int k = 0; k < alphas.Length; k++) alphas[k].alpha *= tint.a;

                Gradient gradient = new();
                gradient.SetKeys(colors, alphas);
                trails[i].colorGradient = gradient;
            }
        }

        // Zona donde pueden aparecer las estelas (con el objeto seleccionado), para colocarla sobre el personaje.
        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 center = Vector3.up * centerHeight;
            DrawEllipse(center, ellipseRadius, new Color(0.6f, 0.9f, 1f, 0.9f));
            DrawEllipse(center, ellipseRadius * radiusRange.x, new Color(0.6f, 0.9f, 1f, 0.35f));
            // Rango de profundidad y hacia dónde va el dash.
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.5f);
            Gizmos.DrawLine(center + Vector3.forward * depthRange.x, center + Vector3.forward * depthRange.y);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(center, center + Vector3.forward * 0.8f);
        }

        static void DrawEllipse(Vector3 center, Vector2 radius, Color color)
        {
            Gizmos.color = color;
            const int segments = 32;
            Vector3 previous = center + new Vector3(radius.x, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y, 0f);
                Gizmos.DrawLine(previous, point);
                previous = point;
            }
        }

        // Así las que quedan apagadas cambian en cada dash. Los gradientes van con su estela.
        void Shuffle()
        {
            for (int i = trails.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (trails[i], trails[j]) = (trails[j], trails[i]);
                (baseGradients[i], baseGradients[j]) = (baseGradients[j], baseGradients[i]);
            }
        }
    }
}
