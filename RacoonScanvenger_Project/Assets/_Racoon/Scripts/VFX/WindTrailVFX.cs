using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Racoon.VFX
{
    /// <summary>
    /// Estelas de viento (TrailRenderers hijos) que acompañan a un personaje mientras corre o hace un dash.
    ///
    /// Montaje: hijo PERMANENTE del personaje (raíz con este componente + varios hijos con TrailRenderer).
    /// Z local = hacia delante. Lo enciende/apaga quien lo controla con <see cref="SetMode"/>
    /// (en el jugador, PlayerVFX según el estado).
    ///
    /// Cómo funciona:
    /// - Cada estela es un hijo FIJO en un punto de la superficie de la malla (laterales, espalda,
    ///   cabeza; nunca la cara delantera). La cabeza de la estela siempre está pegada al cuerpo.
    /// - Mientras se ve, nunca deja de emitir. Para apagarla se acorta la cola (TrailRenderer.time)
    ///   desde atrás hasta el cuerpo: no se queda flotando en el mundo. Al apagar (Off) todas se recogen
    ///   a la vez, en fadeOutDuration (con la forma de fadeOutCurve).
    /// - Siempre de cara a la cámara (alineación View) y con normales hacia ella, para que los
    ///   materiales con iluminación (p. ej. FlatKit) no cambien de luz según el ángulo.
    /// - Si va despacio (choques, girar en el sitio) la cola se acorta sola, así no se enrosca.
    /// - Run / Dash: cada modo tiene su perfil. Al cambiar de uno a otro se mantienen los puntos; las que
    ///   sobran se recogen y las que faltan aparecen.
    /// - Cuando todas se han recogido, la próxima vez salen en otros puntos.
    ///
    /// Color final = gradiente × tint × tinte del jugador. Necesita un material que use el color de
    /// vértice (p. ej. URP/Particles/Unlit con Color Mode = Multiply); URP/Lit lo ignora.
    /// </summary>
    public class WindTrailVFX : MonoBehaviour
    {
        public enum WindMode { Off, Run, Dash }

        [Serializable]
        public class Profile
        {
            [Tooltip("Cuántas estelas emiten (mín, máx incluidos). Como mucho, tantas como hijos con TrailRenderer.")]
            public Vector2Int count = new(3, 5);
            [Tooltip("Grosor de cada estela en metros (mín, máx).")]
            public Vector2 width = new(0.05f, 0.12f);
            [Tooltip("Largo de la cola: segundos que dura cada punto (mín, máx).")]
            public Vector2 length = new(0.18f, 0.32f);
            [Range(0f, 1f)] public float opacity = 1f;
            [Tooltip("Ondulación lateral en metros. 0 = rectas.")]
            public float wobble = 0.004f;
        }

        enum TrailState { Dormant, Active, Retracting }

        [Tooltip("Vacío = todos los TrailRenderer hijos.")]
        [SerializeField] TrailRenderer[] trails;

        [Header("Perfiles")]
        [SerializeField] Profile run = new()
        {
            count = new Vector2Int(2, 3),
            width = new Vector2(0.03f, 0.06f),
            length = new Vector2(0.1f, 0.18f),
            opacity = 0.45f,
            wobble = 0.002f,
        };
        [SerializeField] Profile dash = new();
        [Tooltip("Velocidad a la que cambian grosor, largo y opacidad mientras están activas.")]
        [SerializeField] float blendSpeed = 12f;

        [Header("Apagar")]
        [Tooltip("Segundos que tarda la cola en recogerse hasta el cuerpo al acabar el dash / dejar de correr. Todas a la vez.")]
        [SerializeField] float fadeOutDuration = 0.6f;
        [Tooltip("Cuánta cola queda durante el apagado. X: 0 = empieza, 1 = acaba. Y: 1 = cola entera, 0 = recogida.")]
        [SerializeField] AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [Tooltip("Momento del apagado (0-1) en el que empieza a volverse transparente. 1 = nunca, solo se recoge.")]
        [SerializeField, Range(0f, 1f)] float fadeOutOpacityStart = 0.5f;

        [Header("Aspecto (sustituye al del TrailRenderer)")]
        [Tooltip("Izquierda = cabeza (pegada al cuerpo), derecha = punta de la cola.")]
        [SerializeField] Gradient color = DefaultGradient();
        [Tooltip("Grosor a lo largo de la estela. X: 0 = cabeza, 1 = cola. Empezar fino hace que parezca salir de la malla.")]
        [SerializeField] AnimationCurve widthCurve = new(new Keyframe(0f, 0.2f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
        [Tooltip("Multiplica el color (blanco = sin cambios).")]
        [SerializeField] Color tint = Color.white;

        [Header("Nacer de la malla")]
        [Tooltip("Malla del personaje. Vacío = la más grande (Skinned primero) del personaje.")]
        [SerializeField] Renderer bodyRenderer;
        [Tooltip("Separación de la superficie hacia fuera (metros), para que no queden dentro de la malla.")]
        [SerializeField] float surfaceOffset = 0.02f;
        [Tooltip("Descarta caras que miran hacia delante: 1 = permite todas, 0 = solo laterales y espalda.")]
        [SerializeField, Range(-1f, 1f)] float maxForwardFacing = 0.2f;
        [Tooltip("Franja de altura de la malla permitida (0 = pies, 1 = cabeza).")]
        [SerializeField] Vector2 heightRange = new(0.25f, 1f);
        [Tooltip("Distancia mínima entre estelas (metros).")]
        [SerializeField] float minSeparation = 0.2f;

        [Header("Sin malla (respaldo): elipse alrededor del pivote")]
        [SerializeField] float centerHeight;
        [SerializeField] Vector2 ellipseRadius = new(0.55f, 0.45f);
        [SerializeField] Vector2 radiusRange = new(0.55f, 1f);
        [SerializeField] Vector2 depthRange = new(-0.35f, 0.15f);

        [Header("Velocidad")]
        [Tooltip("A partir de esta velocidad (m/s) la cola tiene su largo completo; por debajo se acorta (choques, girar en el sitio).")]
        [SerializeField] float fullLengthSpeed = 4f;
        [Tooltip("Metros recorridos por cada onda de la ondulación.")]
        [SerializeField] float wobbleWavelength = 4f;

        [Header("Test (botones del Inspector, solo en Play)")]
        [SerializeField] float testRunSpeed = 6.5f;
        [SerializeField] float testRunDuration = 1.2f;
        [SerializeField] float testDashDistance = 3f;
        [SerializeField] float testDashDuration = 0.25f;

        public WindMode Mode { get; private set; } = WindMode.Off;

        // Por estela. Puntos y normales en el espacio local de este objeto.
        Vector3[] offsets;
        Vector3[] wobbleAxes;
        float[] phases;
        // Posición de cada estela dentro del rango del perfil (0-1): al cambiar de modo conservan su carácter.
        float[] widthRoll;
        float[] lengthRoll;
        TrailState[] states;
        float[] retractTimer;
        float[] retractFromTime;
        float[] retractFromOpacity;
        // Tiempo que ha pasado quieto durante el apagado: compensa que los puntos envejecen solos.
        float[] retractAging;
        float[] appliedOpacity;

        float countRoll;
        bool needsNewLayout = true;
        float currentOpacity;
        float currentWobble;
        Color playerTint = Color.white;

        Vector3 lastPosition;
        float smoothedSpeed;
        float distanceTravelled;

        // Triángulos de la malla aptos para nacer (en el espacio local de este objeto).
        readonly List<Vector3> surfacePoints = new();
        readonly List<Vector3> surfaceNormals = new();
        readonly List<float> surfaceCumulativeArea = new();
        float surfaceTotalArea;

        Coroutine testRoutine;

        Profile Current => Mode == WindMode.Dash ? dash : run;

        // Mínimo para TrailRenderer.time (0 lo deja en un estado raro).
        const float MinTime = 0.001f;

        void Awake()
        {
            if (trails == null || trails.Length == 0) trails = GetComponentsInChildren<TrailRenderer>(true);

            int n = trails.Length;
            offsets = new Vector3[n];
            wobbleAxes = new Vector3[n];
            phases = new float[n];
            widthRoll = new float[n];
            lengthRoll = new float[n];
            states = new TrailState[n];
            retractTimer = new float[n];
            retractFromTime = new float[n];
            retractFromOpacity = new float[n];
            retractAging = new float[n];
            appliedOpacity = new float[n];

            for (int i = 0; i < n; i++)
            {
                TrailRenderer trail = trails[i];
                trail.gameObject.SetActive(true); // Se apagan con emitting, no desactivándolas.
                trail.widthCurve = widthCurve;
                // Siempre de cara a la cámara, con normales hacia ella (luz uniforme en materiales iluminados).
                trail.alignment = LineAlignment.View;
                trail.generateLightingData = true;
                trail.textureMode = LineTextureMode.Stretch;
                appliedOpacity[i] = -1f;
                SetDormant(i);
            }

            Material material = n > 0 ? trails[0].sharedMaterial : null;
            if (material != null && material.shader.name == "Universal Render Pipeline/Lit")
                Debug.LogWarning($"WindTrailVFX: el material '{material.name}' usa URP/Lit, que ignora el color de las estelas (gradiente, opacidad, tinte). Usa 'Universal Render Pipeline/Particles/Unlit' con Color Mode = Multiply.", this);

            CacheSurface();
            lastPosition = transform.position;
        }

        void OnDisable()
        {
            if (states == null) return;
            Mode = WindMode.Off;
            needsNewLayout = true;
            for (int i = 0; i < trails.Length; i++) SetDormant(i);
        }

        // Cambios en el Inspector durante Play (color, tint, curva) se ven al momento.
        void OnValidate()
        {
            if (!Application.isPlaying || states == null) return;
            foreach (TrailRenderer trail in trails) trail.widthCurve = widthCurve;
            RefreshColors();
        }

        // ---------------- API ----------------

        public void SetMode(WindMode mode)
        {
            if (mode == Mode) return;
            WindMode previous = Mode;
            Mode = mode;

            if (mode == WindMode.Off)
            {
                // Todas (también las que ya se recogían) empiezan en el mismo frame con la misma duración:
                // acaban a la vez.
                for (int i = 0; i < trails.Length; i++)
                    if (states[i] != TrailState.Dormant) StartRetract(i);
                needsNewLayout = true;
                return;
            }

            if (previous == WindMode.Off)
            {
                currentOpacity = Current.opacity;
                currentWobble = Current.wobble;
                // Solo se cambian los puntos si no queda ninguna visible (moverlas mientras emiten dibujaría una raya).
                if (needsNewLayout && AllDormant()) NewLayout();
                needsNewLayout = false;
            }
            UpdateCount();
        }

        /// <summary>Tinte del jugador (se multiplica por el gradiente y el tint del componente).</summary>
        public void SetPlayerTint(Color value)
        {
            playerTint = value;
            if (states != null) RefreshColors();
        }

        // ---------------- Estados de cada estela ----------------

        // Las N primeras del reparto emiten; las demás se recogen.
        void UpdateCount()
        {
            Profile profile = Current;
            int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(profile.count.x, profile.count.y, countRoll)), 0, trails.Length);
            for (int i = 0; i < trails.Length; i++)
            {
                if (i < count) Activate(i);
                else if (states[i] == TrailState.Active) StartRetract(i);
            }
        }

        void Activate(int i)
        {
            TrailRenderer trail = trails[i];
            if (states[i] == TrailState.Dormant)
            {
                trail.Clear();
                trail.transform.localPosition = offsets[i];
                trail.widthMultiplier = TargetWidth(Current, i);
                trail.time = MinTime; // Crece en LateUpdate.
                trail.emitting = true;
                appliedOpacity[i] = -1f;
                SetTrailOpacity(i, currentOpacity);
            }
            // Si se estaba recogiendo, vuelve a crecer desde donde estaba (sigue pegada, sin cortes).
            states[i] = TrailState.Active;
        }

        void StartRetract(int i)
        {
            states[i] = TrailState.Retracting;
            retractTimer[i] = 0f;
            retractFromTime[i] = trails[i].time;
            retractFromOpacity[i] = Mathf.Max(0f, appliedOpacity[i]);
            retractAging[i] = 0f;
        }

        void SetDormant(int i)
        {
            states[i] = TrailState.Dormant;
            TrailRenderer trail = trails[i];
            trail.emitting = false;
            trail.time = MinTime;
            trail.Clear();
        }

        bool AllDormant()
        {
            foreach (TrailState state in states)
                if (state != TrailState.Dormant) return false;
            return true;
        }

        // ---------------- Bucle ----------------

        // LateUpdate: después de que el personaje se haya movido este frame.
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Velocidad real (vale también en los clientes remotos, que solo ven la posición replicada).
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;
            float step = delta.magnitude;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, step / dt, 1f - Mathf.Exp(-20f * dt));
            distanceTravelled = (distanceTravelled + step) % (Mathf.Max(0.01f, wobbleWavelength) * 1000f);

            float speedFactor = fullLengthSpeed > 0f ? Mathf.Clamp01(smoothedSpeed / fullLengthSpeed) : 1f;
            float blend = 1f - Mathf.Exp(-blendSpeed * dt);
            if (Mode != WindMode.Off)
            {
                currentOpacity = Mathf.Lerp(currentOpacity, Current.opacity, blend);
                currentWobble = Mathf.Lerp(currentWobble, Current.wobble, blend);
            }

            float wave = wobbleWavelength > 0.01f ? distanceTravelled / wobbleWavelength * Mathf.PI * 2f : 0f;
            float duration = Mathf.Max(0.01f, fadeOutDuration);
            for (int i = 0; i < trails.Length; i++)
            {
                TrailRenderer trail = trails[i];
                switch (states[i])
                {
                    case TrailState.Active:
                    {
                        Profile profile = Current;
                        trail.widthMultiplier = Mathf.Lerp(trail.widthMultiplier, TargetWidth(profile, i), blend);
                        // Alargar es inmediato (la cola crece sola al moverse); acortar, suave.
                        float targetTime = Mathf.Max(MinTime, TargetLength(profile, i) * speedFactor);
                        trail.time = targetTime > trail.time ? targetTime : Mathf.Lerp(trail.time, targetTime, blend);
                        SetTrailOpacity(i, currentOpacity);
                        trail.transform.localPosition = offsets[i] + wobbleAxes[i] * (Mathf.Sin(wave + phases[i]) * currentWobble);
                        break;
                    }
                    case TrailState.Retracting:
                    {
                        // La cola se recoge hacia el cuerpo (sigue emitiendo, así la cabeza no se separa)
                        // y en la segunda mitad también se vuelve transparente.
                        retractTimer[i] += dt;
                        float k = retractTimer[i] / duration;
                        if (k >= 1f)
                        {
                            SetDormant(i);
                            break;
                        }
                        // Quieto, los puntos envejecen y desaparecerían antes de tiempo: se suma lo que ha
                        // pasado parado para que la cola dure lo que marca la curva. Moviéndose, ese extra
                        // vuelve a 0 (si no, la cola nueva crecería).
                        retractAging[i] += dt * (1f - speedFactor);
                        retractAging[i] = Mathf.Lerp(retractAging[i], 0f, speedFactor * (1f - Mathf.Exp(-10f * dt)));
                        float remaining = retractFromTime[i] * Mathf.Clamp01(fadeOutCurve.Evaluate(k));
                        trail.time = Mathf.Max(MinTime, remaining + retractAging[i]);
                        float x = fadeOutOpacityStart >= 1f ? 0f : Mathf.InverseLerp(fadeOutOpacityStart, 1f, k);
                        float fade = x * x * (3f - 2f * x); // Suave al empezar y al acabar.
                        SetTrailOpacity(i, retractFromOpacity[i] * (1f - fade));
                        break;
                    }
                }
            }
        }

        float TargetWidth(Profile profile, int i) => Mathf.Lerp(profile.width.x, profile.width.y, widthRoll[i]);
        float TargetLength(Profile profile, int i) => Mathf.Lerp(profile.length.x, profile.length.y, lengthRoll[i]);

        // ---------------- Reparto ----------------

        void NewLayout()
        {
            countRoll = Random.value;
            Shuffle();
            for (int i = 0; i < trails.Length; i++)
            {
                PickPoint(i, out Vector3 point, out Vector3 normal);
                offsets[i] = point;

                // Ondula de lado, rodeando el cuerpo (perpendicular a la normal y a la dirección de avance).
                Vector3 axis = Vector3.Cross(Vector3.forward, normal);
                wobbleAxes[i] = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
                phases[i] = Random.Range(0f, Mathf.PI * 2f);
                widthRoll[i] = Random.value;
                lengthRoll[i] = Random.value;
                trails[i].transform.localPosition = point;
            }
        }

        // Así cambian también qué estelas se usan (todas están dormidas cuando se llama).
        void Shuffle()
        {
            for (int i = trails.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (trails[i], trails[j]) = (trails[j], trails[i]);
            }
        }

        // Varios intentos para que queden separadas entre sí; si no se consigue, la más alejada.
        void PickPoint(int placed, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float bestDistance = -1f;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (!SampleSurface(out Vector3 candidate, out Vector3 candidateNormal))
                    SampleEllipse(out candidate, out candidateNormal);

                float nearest = float.MaxValue;
                for (int j = 0; j < placed; j++)
                    nearest = Mathf.Min(nearest, Vector3.Distance(candidate, offsets[j]));

                if (nearest > bestDistance)
                {
                    bestDistance = nearest;
                    point = candidate;
                    normal = candidateNormal;
                }
                if (nearest >= minSeparation) return;
            }
        }

        // ---------------- Malla ----------------

        void CacheSurface()
        {
            surfacePoints.Clear();
            surfaceNormals.Clear();
            surfaceCumulativeArea.Clear();
            surfaceTotalArea = 0f;

            Renderer body = bodyRenderer != null ? bodyRenderer : FindBodyRenderer();
            if (body == null) return;

            Mesh mesh;
            Matrix4x4 toWorld;
            if (body is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true); // Pose actual, con escala; queda en el espacio del renderer sin escala.
                toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (body.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                toWorld = body.transform.localToWorldMatrix;
            }
            else return;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"WindTrailVFX: la malla '{mesh.name}' no tiene Read/Write activado; se usa la elipse. Actívalo en el Import Settings del modelo.", body);
                return;
            }

            // Al espacio local de este objeto: ahí viven las estelas (hijos), así van fijas al cuerpo.
            Matrix4x4 toLocal = transform.worldToLocalMatrix * toWorld;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int v = 0; v < vertices.Length; v++) vertices[v] = toLocal.MultiplyPoint3x4(vertices[v]);

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 vertex in vertices)
            {
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }
            float height = Mathf.Max(0.0001f, maxY - minY);

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-7f) continue;

                Vector3 normal = cross.normalized;
                if (Vector3.Dot(normal, Vector3.forward) > maxForwardFacing) continue;
                if (normal.y < -0.5f) continue; // Caras de abajo: no se ven.
                float h = ((a.y + b.y + c.y) / 3f - minY) / height;
                if (h < heightRange.x || h > heightRange.y) continue;

                surfacePoints.Add(a);
                surfacePoints.Add(b);
                surfacePoints.Add(c);
                surfaceNormals.Add(normal);
                surfaceTotalArea += area;
                surfaceCumulativeArea.Add(surfaceTotalArea);
            }

            if (body is SkinnedMeshRenderer) Destroy(mesh);
        }

        Renderer FindBodyRenderer()
        {
            Transform root = transform.parent != null ? transform.root : transform;
            Renderer best = null;
            float bestSize = 0f;
            foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
            {
                if (candidate.transform.IsChildOf(transform)) continue; // Nuestras propias estelas.
                if (candidate is not (SkinnedMeshRenderer or MeshRenderer)) continue;

                float size = candidate.bounds.size.sqrMagnitude;
                if (candidate is SkinnedMeshRenderer) size *= 1000f; // Prioridad al personaje animado.
                if (size > bestSize)
                {
                    bestSize = size;
                    best = candidate;
                }
            }
            return best;
        }

        // Punto aleatorio de la superficie, ponderado por área (zonas grandes salen más).
        bool SampleSurface(out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (surfaceTotalArea <= 0f) return false;

            float pick = Random.value * surfaceTotalArea;
            int index = surfaceCumulativeArea.BinarySearch(pick);
            if (index < 0) index = Mathf.Min(~index, surfaceCumulativeArea.Count - 1);

            float r1 = Random.value, r2 = Random.value;
            if (r1 + r2 > 1f)
            {
                r1 = 1f - r1;
                r2 = 1f - r2;
            }
            Vector3 a = surfacePoints[index * 3], b = surfacePoints[index * 3 + 1], c = surfacePoints[index * 3 + 2];
            normal = surfaceNormals[index];
            point = a + (b - a) * r1 + (c - a) * r2 + normal * surfaceOffset;
            return true;
        }

        void SampleEllipse(out Vector3 point, out Vector3 normal)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(radiusRange.x, radiusRange.y);
            Vector2 p = new(Mathf.Cos(angle) * ellipseRadius.x * radius, Mathf.Sin(angle) * ellipseRadius.y * radius);
            point = new Vector3(p.x, centerHeight + p.y, Random.Range(depthRange.x, depthRange.y));
            normal = new Vector3(p.x, p.y, 0f).normalized;
        }

        // ---------------- Color ----------------

        void SetTrailOpacity(int i, float opacity)
        {
            opacity = Mathf.Max(0f, opacity);
            if (Mathf.Abs(opacity - appliedOpacity[i]) < 0.005f) return;
            appliedOpacity[i] = opacity;

            Color multiplier = tint * playerTint;
            GradientColorKey[] colors = color.colorKeys;
            GradientAlphaKey[] alphas = color.alphaKeys;
            for (int k = 0; k < colors.Length; k++) colors[k].color *= multiplier;
            for (int k = 0; k < alphas.Length; k++) alphas[k].alpha *= multiplier.a * opacity;

            Gradient gradient = new();
            gradient.SetKeys(colors, alphas);
            trails[i].colorGradient = gradient;
        }

        // Fuerza a reaplicar el color (tint / gradiente cambiados) manteniendo la opacidad de cada una.
        void RefreshColors()
        {
            for (int i = 0; i < trails.Length; i++)
            {
                float opacity = Mathf.Max(0f, appliedOpacity[i]);
                appliedOpacity[i] = -1f;
                SetTrailOpacity(i, opacity);
            }
        }

        static Gradient DefaultGradient()
        {
            Gradient gradient = new();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        // ---------------- Test ----------------

        /// <summary>Simula correr o hacer un dash moviendo el efecto hacia delante. Solo en Play.</summary>
        public void PlayTest(WindMode mode)
        {
            // Una prueba cada vez: hay que esperar a que se recojan antes de devolverlo a su sitio.
            if (!Application.isPlaying || mode == WindMode.Off || testRoutine != null) return;
            testRoutine = StartCoroutine(TestRoutine(mode));
        }

        IEnumerator TestRoutine(WindMode mode)
        {
            Vector3 start = transform.localPosition;
            Vector3 forward = transform.localRotation * Vector3.forward;
            bool isDash = mode == WindMode.Dash;
            float duration = isDash ? testDashDuration : testRunDuration;
            float distance = isDash ? testDashDistance : testRunSpeed * testRunDuration;

            SetMode(mode);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                if (isDash) k = 1f - (1f - k) * (1f - k); // Ease-out, como el dash.
                transform.localPosition = start + forward * (distance * k);
                yield return null;
            }

            // Igual que al volver a andar: se recogen hacia el cuerpo.
            SetMode(WindMode.Off);
            while (!AllDormant()) yield return null;
            transform.localPosition = start;
            lastPosition = transform.position; // Que el salto de vuelta no cuente como velocidad.
            testRoutine = null;
        }

        // ---------------- Gizmos ----------------

        // Con el objeto seleccionado: en Play, los puntos donde nacen; fuera de Play, la elipse de respaldo.
        void OnDrawGizmosSelected()
        {
            if (Application.isPlaying && states != null)
            {
                Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.9f);
                for (int i = 0; i < trails.Length; i++)
                    if (states[i] != TrailState.Dormant) Gizmos.DrawWireSphere(trails[i].transform.position, 0.04f);
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 center = Vector3.up * centerHeight;
            DrawEllipse(center, ellipseRadius, new Color(0.6f, 0.9f, 1f, 0.5f));
            DrawEllipse(center, ellipseRadius * radiusRange.x, new Color(0.6f, 0.9f, 1f, 0.2f));
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
    }
}
