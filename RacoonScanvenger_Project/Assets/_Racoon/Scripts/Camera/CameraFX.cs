using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Racoon.Cameras
{
    /// <summary>
    /// Qué le hace a la cámara un efecto (ver <see cref="CameraFX"/>). Todos los valores son el máximo: se
    /// multiplican por la envolvente (puntuales) o por la intensidad (continuos). 0 = no toca ese canal.
    /// </summary>
    [Serializable]
    public class CameraFXProfile
    {
        public bool enabled = true;
        [Tooltip("Segundos (en los continuos no se usa).")]
        public float duration = 0.3f;
        [Tooltip("Fracción de la duración en llegar al máximo (0 = de golpe).")]
        [Range(0f, 0.9f)] public float attack;
        [Tooltip("Cómo se apaga: 1 = lineal, más = cae rápido y deja una cola suave.")]
        public float decayPower = 2f;

        [Header("Jerarquía")]
        [Tooltip("De los continuos solo se ve el de más prioridad (a igualdad, el último en empezar); los demás se " +
                 "apartan con su transición y vuelven cuando acaba. Los puntuales bajan (duck) lo que tenga menos.")]
        public int priority;
        [Tooltip("Cuánto baja este efecto, mientras está activo, a los que tienen menos prioridad (1 = los apaga).")]
        [Range(0f, 1f)] public float duck = 0.3f;
        [Tooltip("Continuos: segundos de la transición de entrada (0 = la velocidad que pida quien lo lanza).")]
        public float fadeIn;
        [Tooltip("Continuos: segundos de la transición de salida (0 = la velocidad que pida quien lo lanza).")]
        public float fadeOut;

        [Header("Lente")]
        [Tooltip("Fracción del FOV: + abre (velocidad), - cierra (punch-in). 0.05 = 5 %.")]
        public float fov;
        [Tooltip("Alabeo de cámara (grados, dutch tilt).")]
        public float dutch;
        [Tooltip("Vaivenes por segundo del alabeo (0 = sin vaivén).")]
        public float dutchWobble;

        [Header("Post-procesado")]
        [Range(0f, 1f)] public float vignette;
        public Color vignetteColor = Color.black;
        [Range(0f, 1f)] public float chromatic;
        [Tooltip("Distorsión de lente: - cojín (hacia dentro), + barril (hacia fuera).")]
        [Range(-1f, 1f)] public float distortion;
        [Range(-100f, 100f)] public float saturation;
        [Range(-100f, 100f)] public float contrast;
        [Tooltip("Exposición (EV).")]
        public float exposure;
        [Tooltip("Giro de tono (grados).")]
        public float hueShift;
        [Tooltip("Tinte de la imagen; el alfa es cuánto.")]
        public Color tint = new(1f, 1f, 1f, 0f);

        [Header("Capas en pantalla")]
        [Range(0f, 1f)] public float flash;
        public Color flashColor = Color.white;
        [Tooltip("Líneas de velocidad radiales alrededor del jugador.")]
        [Range(0f, 1f)] public float speedLines;
        [Tooltip("Brillo de color en los bordes de la pantalla.")]
        [Range(0f, 1f)] public float edge;
        public Color edgeColor = Color.white;

        [Header("Zonas de pantalla (esquinas inferiores > centro inferior > laterales)")]
        [Tooltip("Llamas horneadas: cuanto más alto, más zonas se encienden (esquinas, luego centro inferior y por " +
                 "último laterales). Crecen desde su base al entrar y bajan al salir.")]
        [Range(0f, 1.5f)] public float fire;
        [Tooltip("Virutas de fuego que suben desde las mismas zonas, con variación de color.")]
        [Range(0f, 1f)] public float embers;
        [Tooltip("Color base de llamas y virutas (la rampa saca de él los tonos fríos y calientes).")]
        public Color fireColor = new(1f, 0.62f, 0.43f);
        [Tooltip("Aura de energía: viñeteado cartoon por capas con el borde ondulando como ondas de sonido.")]
        [Range(0f, 1f)] public float aura;
        public Color auraColor = new(1f, 0.827f, 0.431f);
        [Tooltip("Líneas que suben desde las zonas y se difuminan al subir.")]
        [Range(0f, 1f)] public float risingLines;
        [Tooltip("Iconos de rayo que suben desde las zonas y se difuminan al subir.")]
        [Range(0f, 1f)] public float risingBolts;
        public Color risingColor = Color.white;

        [Header("Ritmo")]
        [Tooltip("Latidos por segundo (0 = constante).")]
        public float pulseRate;
        [Tooltip("Cuánto baja entre latidos (1 = hasta 0).")]
        [Range(0f, 1f)] public float pulseDepth;
        [Tooltip("Parpadeo irregular.")]
        [Range(0f, 1f)] public float flicker;

        [Header("Tiempo")]
        [Tooltip("Hitstop: segundos (reales) a cámara lenta al lanzarlo. Solo en los puntuales.")]
        public float hitstop;
        [Range(0f, 1f)] public float hitstopScale = 0.05f;

        public CameraFXProfile() { }

        public CameraFXProfile(float duration, float attack = 0f, float decayPower = 2f)
        {
            this.duration = duration;
            this.attack = attack;
            this.decayPower = decayPower;
        }
    }

    /// <summary>
    /// Efectos de cámara por código: FOV y alabeo (extensión de Cinemachine), post-procesado URP (un Volume propio
    /// de máxima prioridad), capas en pantalla (destello, bordes, líneas, llamas horneadas, viñeteado de energía,
    /// virutas, líneas y rayos que suben) y hitstop. Las capas de zona usan sobre todo las esquinas inferiores, después el centro
    /// inferior y por último la parte media-baja de los laterales. Igual que <see cref="CameraShaker"/>, la cámara es local: lo que se lanza aquí solo se ve en esta
    /// máquina.
    ///  - <see cref="Play"/>: un efecto puntual (golpe, venta...).
    ///  - <see cref="SetContinuous"/>: un efecto que dura mientras se mantenga (correr, llamas, poco tiempo).
    ///  - <see cref="Hitstop"/>: congela (o ralentiza) el juego en esta máquina un instante.
    ///
    /// Jerarquía (para que no se solapen): de los continuos solo se ve el de más <see cref="CameraFXProfile.priority"/>;
    /// el resto se aparta con su transición de salida y vuelve con la de entrada cuando el de arriba acaba. Los
    /// puntuales siempre suenan, pero bajan (<see cref="CameraFXProfile.duck"/>) todo lo de menos prioridad mientras
    /// duran, y lo de más prioridad los baja a ellos. Las transiciones van suavizadas (entrada y salida).
    ///
    /// No hace falta montarlo: se crea solo al cargar la escena. <see cref="Focus"/> es el punto de la pantalla hacia
    /// el que se centran viñeta y líneas radiales (el jugador local; el rival comparte la pantalla).
    /// </summary>
    public static class CameraFX
    {
        /// <summary>Multiplicador global (accesibilidad: 0 = sin efectos).</summary>
        public static float GlobalIntensity = 1f;
        /// <summary>Multiplicador de destellos y aberración cromática (accesibilidad: fotosensibilidad).</summary>
        public static float FlashIntensity = 1f;
        public static bool HitstopEnabled = true;
        /// <summary>Contorno de llamas, aura, virutas y rayos: la tinta del HUD (textos y contornos).</summary>
        public static Color InkColor = new(0.294f, 0.263f, 0.4f);   // #4B4366
        /// <summary>Banda de luz: el crema del HUD.</summary>
        public static Color CreamColor = new(1f, 0.973f, 0.925f);    // #FFF8EC
        /// <summary>Hacia dónde se centran viñeta y líneas de velocidad (null = centro de la pantalla).</summary>
        public static Transform Focus;

        const int MaxPulses = 16;

        sealed class Pulse
        {
            public CameraFXProfile profile;
            public float strength;
            public Color color;
            public float age;
            public float amount;   // Envolvente × fuerza de este frame.
        }

        sealed class Layer
        {
            public CameraFXProfile profile;
            public float target;
            public float level;
            public float progress; // 0-1: transición de entrada/salida del propio efecto.
            public float vis;      // 0-1: transición al apartarse por otro de más prioridad.
            public float weight;   // Lo que pesa este frame (level × transiciones).
            public float fadeSpeed;
            public float rateScale = 1f;
            public float seed;
            public float phase;
            public float startedAt;
            public bool on;
            public Color color;
        }

        // Suma de este frame.
        internal struct Sample
        {
            public float fov, dutch, vignette, chromatic, distortion, saturation, contrast, exposure, hueShift;
            public float tintAmount, flash, speedLines, edge, fire, embers, aura, risingLines, risingBolts;
            public Color vignetteColor, tint, flashColor, edgeColor, fireColor, auraColor, risingColor;
        }

        static readonly List<Pulse> pulses = new();
        static readonly Dictionary<object, Layer> layers = new();
        static readonly List<object> finishedLayers = new();
        static float time;
        static float hitstopEnd = -1f;
        static float hitstopScale = 1f;
        static float timeScaleBefore = 1f;
        static CameraFXRunner runner;

        internal static Sample Current;
        internal static int Instances;

        /// <summary>Multiplicador del FOV de este frame.</summary>
        public static float FovMultiplier => Mathf.Clamp(1f + Current.fov, 0.6f, 1.4f);
        public static float Dutch => Current.dutch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pulses.Clear();
            layers.Clear();
            time = 0f;
            hitstopEnd = -1f;
            hitstopScale = 1f;
            timeScaleBefore = 1f;
            runner = null;
            Current = default;
            Instances = 0;
            GlobalIntensity = 1f;
            FlashIntensity = 1f;
            HitstopEnabled = true;
            Focus = null;
        }

        // ---------------- API ----------------

        /// <summary>
        /// Efecto puntual. <paramref name="color"/> (si su alfa &gt; 0) sustituye todos los colores del perfil
        /// (p. ej. el color del rival que te pega).
        /// </summary>
        public static void Play(CameraFXProfile profile, float strength = 1f, Color color = default)
        {
            if (profile == null || !profile.enabled || strength <= 0f || profile.duration <= 0f) return;
            EnsureRunner();
            if (pulses.Count >= MaxPulses) pulses.RemoveAt(0);
            pulses.Add(new Pulse { profile = profile, strength = strength, color = color });
            if (profile.hitstop > 0f) Hitstop(profile.hitstop * Mathf.Min(strength, 1.5f), profile.hitstopScale);
        }

        /// <summary>
        /// Efecto continuo identificado por <paramref name="key"/>, a <paramref name="intensity"/> (0 = apagado).
        /// Entra y sale con las transiciones del perfil (o a <paramref name="fadeSpeed"/> por segundo si no tiene).
        /// <paramref name="rateScale"/> acelera su latido; <paramref name="color"/> (alfa &gt; 0) sustituye sus colores.
        /// </summary>
        public static void SetContinuous(object key, CameraFXProfile profile, float intensity, float fadeSpeed = 3f,
            float rateScale = 1f, Color color = default)
        {
            if (key == null) return;
            if (!layers.TryGetValue(key, out Layer layer))
            {
                if (intensity <= 0f || profile == null || !profile.enabled) return;
                EnsureRunner();
                layer = new Layer { seed = UnityEngine.Random.Range(0f, 100f) };
                layers[key] = layer;
            }
            if (profile != null) layer.profile = profile;
            layer.target = profile != null && profile.enabled ? Mathf.Max(0f, intensity) : 0f;
            layer.fadeSpeed = Mathf.Max(0.01f, fadeSpeed);
            layer.rateScale = Mathf.Max(0f, rateScale);
            layer.color = color;
        }

        /// <summary>Quita de golpe un efecto continuo (p. ej. al destruirse quien lo puso).</summary>
        public static void ClearContinuous(object key)
        {
            if (key != null) layers.Remove(key);
        }

        /// <summary>
        /// Pone el juego de esta máquina a <paramref name="scale"/> durante <paramref name="seconds"/> reales.
        /// Si ya hay uno, se queda el más largo y el más lento. Solo actúa si el juego va a velocidad normal.
        /// </summary>
        public static void Hitstop(float seconds, float scale = 0.05f)
        {
            if (!HitstopEnabled || seconds <= 0f || GlobalIntensity <= 0f) return;
            EnsureRunner();
            bool active = hitstopEnd > 0f;
            if (!active)
            {
                if (!Mathf.Approximately(Time.timeScale, 1f)) return; // Pausa u otro script controlando el tiempo.
                timeScaleBefore = Time.timeScale;
                hitstopScale = scale;
            }
            else hitstopScale = Mathf.Min(hitstopScale, scale);
            hitstopEnd = Mathf.Max(hitstopEnd, Time.unscaledTime + seconds);
            Time.timeScale = Mathf.Clamp01(hitstopScale);
        }

        // Se crea al empezar (y no al primer efecto) para que el canvas se vea en la jerarquía desde el principio.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("[CameraFX]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<CameraFXRunner>();
        }

        internal static void ReleaseRunner(CameraFXRunner which)
        {
            if (runner == which) runner = null;
            if (hitstopEnd > 0f) Time.timeScale = timeScaleBefore;
            hitstopEnd = -1f;
        }

        // ---------------- Cálculo ----------------

        internal static void Advance(float dt)
        {
            time += dt;

            if (hitstopEnd > 0f && Time.unscaledTime >= hitstopEnd)
            {
                hitstopEnd = -1f;
                Time.timeScale = timeScaleBefore;
            }

            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                Pulse pulse = pulses[i];
                pulse.age += dt;
                if (pulse.age >= pulse.profile.duration)
                {
                    pulses.RemoveAt(i);
                    continue;
                }
                CameraFXProfile p = pulse.profile;
                float u = Mathf.Clamp01(pulse.age / p.duration);
                float envelope = u < p.attack
                    ? u / Mathf.Max(p.attack, 1e-4f)
                    : Mathf.Pow(1f - (u - p.attack) / Mathf.Max(1f - p.attack, 1e-4f), Mathf.Max(p.decayPower, 0.1f));
                pulse.amount = envelope * pulse.strength;
            }

            UpdateLayers(dt);

            var s = new Sample();
            var colors = new ColorSums();
            foreach (Pulse pulse in pulses)
            {
                CameraFXProfile p = pulse.profile;
                float amount = pulse.amount * PulseDuck(pulse) * Rhythm(p, pulse.age, 0f);
                float wobble = p.dutchWobble > 0f ? Mathf.Cos(pulse.age * p.dutchWobble * Mathf.PI * 2f) : 1f;
                Accumulate(ref s, ref colors, p, amount, wobble, pulse.color);
            }
            foreach (Layer layer in layers.Values)
            {
                if (layer.weight <= 0.0001f) continue;
                CameraFXProfile p = layer.profile;
                float amount = layer.weight * LayerDuck(layer) * Rhythm(p, layer.phase, layer.seed);
                float wobble = p.dutchWobble > 0f ? Mathf.Sin((time + layer.seed) * p.dutchWobble * Mathf.PI * 2f) : 1f;
                Accumulate(ref s, ref colors, p, amount, wobble, layer.color);
            }

            float global = Mathf.Max(0f, GlobalIntensity);
            float flashGlobal = global * Mathf.Clamp01(FlashIntensity);
            s.fov *= global;
            s.dutch *= global;
            s.vignette = Mathf.Clamp(s.vignette * global, 0f, 0.65f);
            s.chromatic = Mathf.Clamp01(s.chromatic * flashGlobal);
            s.distortion = Mathf.Clamp(s.distortion * global, -0.6f, 0.6f);
            s.saturation = Mathf.Clamp(s.saturation * global, -100f, 100f);
            s.contrast = Mathf.Clamp(s.contrast * global, -100f, 100f);
            s.exposure *= global;
            s.hueShift = Mathf.Clamp(s.hueShift * global, -180f, 180f);
            s.tintAmount = Mathf.Clamp01(s.tintAmount * global);
            s.flash = Mathf.Clamp01(s.flash * flashGlobal);
            s.speedLines = Mathf.Clamp01(s.speedLines * global);
            s.edge = Mathf.Clamp01(s.edge * global);
            s.fire = Mathf.Clamp(s.fire * global, 0f, 1.5f);
            s.embers = Mathf.Clamp01(s.embers * global);
            s.aura = Mathf.Clamp01(s.aura * global);
            s.risingLines = Mathf.Clamp01(s.risingLines * global);
            s.risingBolts = Mathf.Clamp01(s.risingBolts * global);
            s.vignetteColor = colors.Vignette;
            s.tint = colors.Tint;
            s.flashColor = colors.Flash;
            s.edgeColor = colors.Edge;
            s.fireColor = colors.Fire;
            s.auraColor = colors.Aura;
            s.risingColor = colors.Rising;
            Current = s;
        }

        // Transiciones y jerarquía de los continuos.
        static void UpdateLayers(float dt)
        {
            foreach (Layer layer in layers.Values)
            {
                bool on = layer.target > 0f && layer.profile != null;
                if (on && !layer.on) layer.startedAt = time;
                layer.on = on;
            }

            // Solo se ve el de más prioridad (a igualdad, el último en empezar).
            Layer top = null;
            foreach (Layer layer in layers.Values)
            {
                if (!layer.on) continue;
                if (top == null || layer.profile.priority > top.profile.priority ||
                    (layer.profile.priority == top.profile.priority && layer.startedAt > top.startedAt))
                    top = layer;
            }

            finishedLayers.Clear();
            foreach (KeyValuePair<object, Layer> pair in layers)
            {
                Layer layer = pair.Value;
                CameraFXProfile p = layer.profile;
                if (p == null)
                {
                    finishedLayers.Add(pair.Key);
                    continue;
                }
                float fadeIn = p.fadeIn > 0f ? p.fadeIn : 1f / layer.fadeSpeed;
                float fadeOut = p.fadeOut > 0f ? p.fadeOut : 1f / layer.fadeSpeed;

                if (layer.on) layer.level = layer.level <= 0f ? layer.target : Mathf.MoveTowards(layer.level, layer.target, dt * 2f);
                layer.progress = Mathf.MoveTowards(layer.progress, layer.on ? 1f : 0f, dt / (layer.on ? fadeIn : fadeOut));
                bool visible = top == null || top == layer;
                layer.vis = Mathf.MoveTowards(layer.vis, visible ? 1f : 0f, dt / (visible ? fadeIn : fadeOut));
                layer.weight = layer.level * Smooth(layer.progress) * Smooth(layer.vis);
                layer.phase += dt * layer.rateScale;

                if (!layer.on && layer.progress <= 0f) finishedLayers.Add(pair.Key);
            }
            foreach (object key in finishedLayers) layers.Remove(key);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        // Lo bajan los puntuales de más prioridad (entre continuos ya decide la jerarquía).
        static float LayerDuck(Layer layer)
        {
            float k = 1f;
            int priority = layer.profile.priority;
            foreach (Pulse q in pulses)
                if (q.profile.priority > priority) k *= 1f - q.profile.duck * Mathf.Clamp01(q.amount);
            return k;
        }

        // Lo bajan los puntuales y los continuos de más prioridad.
        static float PulseDuck(Pulse pulse)
        {
            float k = 1f;
            int priority = pulse.profile.priority;
            foreach (Pulse q in pulses)
                if (q != pulse && q.profile.priority > priority) k *= 1f - q.profile.duck * Mathf.Clamp01(q.amount);
            foreach (Layer layer in layers.Values)
                if (layer.profile != null && layer.profile.priority > priority)
                    k *= 1f - layer.profile.duck * Mathf.Clamp01(layer.weight);
            return k;
        }

        // Latido y parpadeo.
        static float Rhythm(CameraFXProfile p, float t, float seed)
        {
            float k = 1f;
            if (p.pulseRate > 0f && p.pulseDepth > 0f)
                k *= 1f - p.pulseDepth * (0.5f - 0.5f * Mathf.Cos(t * p.pulseRate * Mathf.PI * 2f));
            if (p.flicker > 0f)
                k *= 1f - p.flicker * Mathf.PerlinNoise(seed + 3.1f, time * 9f);
            return k;
        }

        struct ColorSums
        {
            Vector4 vignette, tint, flash, edge, fire, aura, rising;
            float vignetteW, tintW, flashW, edgeW, fireW, auraW, risingW;

            static void Add(ref Vector4 sum, ref float weight, Color color, float w)
            {
                if (w <= 0f) return;
                sum += (Vector4)color * w;
                weight += w;
            }

            public void AddAll(CameraFXProfile p, float amount, Color over)
            {
                bool useOver = over.a > 0f;
                Add(ref vignette, ref vignetteW, useOver ? over : p.vignetteColor, p.vignette * amount);
                Add(ref tint, ref tintW, p.tint, p.tint.a * amount);
                Add(ref flash, ref flashW, useOver ? over : p.flashColor, p.flash * amount);
                Add(ref edge, ref edgeW, useOver ? over : p.edgeColor, p.edge * amount);
                Add(ref fire, ref fireW, useOver ? over : p.fireColor, (p.fire + p.embers) * amount);
                Add(ref aura, ref auraW, useOver ? over : p.auraColor, p.aura * amount);
                Add(ref rising, ref risingW, useOver ? over : p.risingColor, (p.risingLines + p.risingBolts) * amount);
            }

            static Color Avg(Vector4 sum, float w, Color fallback) => w > 0f ? (Color)(sum / w) : fallback;
            public Color Vignette => Avg(vignette, vignetteW, Color.black);
            public Color Tint => Avg(tint, tintW, Color.white);
            public Color Flash => Avg(flash, flashW, Color.white);
            public Color Edge => Avg(edge, edgeW, Color.white);
            public Color Fire => Avg(fire, fireW, new Color(1f, 0.45f, 0.1f));
            public Color Aura => Avg(aura, auraW, Color.white);
            public Color Rising => Avg(rising, risingW, Color.white);
        }

        static void Accumulate(ref Sample s, ref ColorSums colors, CameraFXProfile p, float amount, float wobble, Color over)
        {
            if (amount <= 0f) return;
            s.fov += p.fov * amount;
            s.dutch += p.dutch * amount * wobble;
            s.vignette += p.vignette * amount;
            s.chromatic += p.chromatic * amount;
            s.distortion += p.distortion * amount;
            s.saturation += p.saturation * amount;
            s.contrast += p.contrast * amount;
            s.exposure += p.exposure * amount;
            s.hueShift += p.hueShift * amount;
            s.tintAmount += p.tint.a * amount;
            s.flash += p.flash * amount;
            s.speedLines += p.speedLines * amount;
            s.edge += p.edge * amount;
            s.fire += p.fire * amount;
            s.embers += p.embers * amount;
            s.aura += p.aura * amount;
            s.risingLines += p.risingLines * amount;
            s.risingBolts += p.risingBolts * amount;
            colors.AddAll(p, amount, over);
        }
    }

    /// <summary>
    /// Aplica cada frame lo que calcula <see cref="CameraFX"/>: Volume URP, capas en pantalla y la extensión de lente.
    /// Lo crea <see cref="CameraFX"/> solo; no se monta a mano.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CameraFXRunner : MonoBehaviour
    {
        const float Eps = 0.001f;
        const int MaxLines = 28;
        const int MaxBolts = 12;
        const int MaxEmbers = 48;
        // Altura (fracción de pantalla, desde donde nace) a la que ya se han difuminado del todo.
        const float LinesFadeHeight = 0.6f;
        const float BoltsFadeHeight = 0.45f;
        // Flipbooks horneados (Tools/CameraFX/bake_camera_fx.py): 4x4 fotogramas.
        const int FlipbookGrid = 4;
        const int FlipbookFrames = FlipbookGrid * FlipbookGrid;
        const float FireFps = 14f;

        static int UILayer => Mathf.Max(0, LayerMask.NameToLayer("UI"));

        // Una llama de la escena: dónde está (fracciones de pantalla), cuánto mide (fracción de la altura) y a partir
        // de qué intensidad de fuego aparece. Así las esquinas encienden primero y los laterales los últimos.
        sealed class Plume
        {
            public RawImage image;
            public RectTransform rect;
            public float x, y, size, threshold, rotation, phase, speed, vis;
            public bool mirror;
        }

        sealed class Mote
        {
            public RawImage image;
            public RectTransform rect;
            public Vector2 position, velocity;
            public float age, life, size, wobble, wobbleRate, seed, alpha, nextFlicker, flicker = 1f;
            public bool active;
        }

        // Zonas de aparición, por peso: esquinas inferiores, centro inferior y parte media-baja de los laterales.
        enum Zone { Corner, BottomCenter, Side }

        Volume volume;
        VolumeProfile profile;
        Vignette vignette;
        ChromaticAberration chromatic;
        LensDistortion distortion;
        ColorAdjustments color;

        Canvas canvas;
        RectTransform canvasRect;
        RawImage flash, edge, lines, waves;
        readonly List<Plume> plumes = new();
        readonly List<Mote> embers = new();
        readonly List<Mote> risingLines = new();
        readonly List<Mote> risingBolts = new();
        Material fireMaterial, boltMaterial, wavesMaterial;
        // Virutas: tres rampas a partir del color del fuego (tal cual, más clara y con el tono girado).
        readonly Material[] emberMaterials = new Material[3];
        Color fireMaterialColor, boltMaterialColor;
        readonly Color[] emberMaterialColors = new Color[3];
        float emberSpawn, lineSpawn, boltSpawn;
        float nextLinesJitter;
        float nextLensScan;
        Camera lastCamera;

        void Awake()
        {
            spawnEmber = SpawnEmber;
            spawnLine = SpawnLine;
            spawnBolt = SpawnBolt;
            BuildVolume();
            BuildOverlay();
        }

        void OnDestroy()
        {
            CameraFX.ReleaseRunner(this);
            if (profile != null) Destroy(profile);
            foreach (Material m in new[] { fireMaterial, boltMaterial, wavesMaterial })
                if (m != null) Destroy(m);
            foreach (Material m in emberMaterials)
                if (m != null) Destroy(m);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            CameraFX.Advance(dt);
            EnsureLensExtension();
            Apply(CameraFX.Current, dt);
        }

        // ---------------- Montaje ----------------

        void BuildVolume()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            vignette = profile.Add<Vignette>();
            chromatic = profile.Add<ChromaticAberration>();
            distortion = profile.Add<LensDistortion>();
            color = profile.Add<ColorAdjustments>();

            var go = new GameObject("Volume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.sharedProfile = profile;
        }

        void BuildOverlay()
        {
            var go = new GameObject("Canvas FX (Screen Space - Camera)", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.layer = UILayer;
            // Screen Space - Camera, como todos los canvas del proyecto. La cámara se pone en EnsureLensExtension.
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.sortingOrder = -100; // Por debajo de la UI del juego.
            canvasRect = (RectTransform)go.transform;

            Shader wavesShader = CameraFXTextures.WavesShader;
            if (wavesShader != null)
            {
                wavesMaterial = new Material(wavesShader) { name = "CameraFX Waves", hideFlags = HideFlags.DontSave };
                wavesMaterial.SetColor("_Ink", CameraFX.InkColor);
                wavesMaterial.SetColor("_Cream", CameraFX.CreamColor);
            }
            Shader ramp = CameraFXTextures.RampShader;
            if (ramp != null)
            {
                fireMaterial = NewRamp(ramp, "CameraFX Fire");
                boltMaterial = NewRamp(ramp, "CameraFX Bolt");
                for (int i = 0; i < emberMaterials.Length; i++) emberMaterials[i] = NewRamp(ramp, "CameraFX Ember " + i);
            }

            // Orden de dibujo: de atrás a delante.
            edge = Layer("Edge", CameraFXTextures.Edge);
            Stretch(edge.rectTransform);
            waves = Layer("AuraWaves", Texture2D.whiteTexture);
            waves.material = wavesMaterial;
            Stretch(waves.rectTransform);
            if (fireMaterial != null && CameraFXTextures.Fire != null) BuildPlumes();
            lines = Layer("SpeedLines", CameraFXTextures.SpeedLines);
            lines.rectTransform.anchorMin = lines.rectTransform.anchorMax = Vector2.zero;
            for (int i = 0; i < MaxEmbers; i++) embers.Add(NewMote("Ember", CameraFXTextures.Ember, new Vector2(0.5f, 0.5f)));
            for (int i = 0; i < MaxLines; i++) risingLines.Add(NewMote("RisingLine", CameraFXTextures.Streak, new Vector2(0.5f, 1f)));
            for (int i = 0; i < MaxBolts; i++) risingBolts.Add(NewMote("RisingBolt", CameraFXTextures.Bolt, new Vector2(0.5f, 0.5f)));
            // Sin el shader de rampa no hay cel-shading: virutas y rayos no se montan.
            if (fireMaterial == null)
            {
                embers.Clear();
                risingBolts.Clear();
            }
            flash = Layer("Flash", Texture2D.whiteTexture);
            Stretch(flash.rectTransform);
        }

        static Material NewRamp(Shader shader, string name)
        {
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            material.SetColor("_Outline", CameraFX.InkColor);
            return material;
        }

        // Llamas: grandes en las esquinas y más pequeñas en el centro inferior (los laterales solo llevan virutas).
        void BuildPlumes()
        {
            //       x      y     tamaño umbral giro
            AddPlumePair(0.03f, 0f, 0.46f, 0f, 0f);
            AddPlumePair(0.12f, 0f, 0.30f, 0.05f, -6f);
            AddPlume(0.38f, -0.01f, 0.15f, 0.35f, 0f, false);
            AddPlume(0.62f, -0.01f, 0.13f, 0.40f, 0f, true);
            AddPlume(0.50f, -0.02f, 0.10f, 0.50f, 0f, false);
        }

        void AddPlumePair(float x, float y, float size, float threshold, float rotation)
        {
            AddPlume(x, y, size, threshold, rotation, false);
            AddPlume(1f - x, y, size * UnityEngine.Random.Range(0.9f, 1.05f), threshold + 0.02f, -rotation, true);
        }

        void AddPlume(float x, float y, float size, float threshold, float rotation, bool mirror)
        {
            RawImage image = Layer("Flame", CameraFXTextures.Fire);
            image.material = fireMaterial;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0f);
            plumes.Add(new Plume
            {
                image = image, rect = rect, x = x, y = y, size = size, threshold = threshold, rotation = rotation,
                mirror = mirror, phase = UnityEngine.Random.Range(0f, FlipbookFrames), speed = UnityEngine.Random.Range(0.85f, 1.15f),
            });
        }

        RawImage Layer(string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasRect, false);
            go.layer = UILayer;
            var image = go.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.color = Color.clear;
            image.enabled = false;
            return image;
        }

        Mote NewMote(string name, Texture texture, Vector2 pivot)
        {
            RawImage image = Layer(name, texture);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            return new Mote { image = image, rect = rect };
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // Como CameraShaker: se añade solo a las CinemachineCamera de la escena (y si se recrean).
        void EnsureLensExtension()
        {
            Camera cam = Camera.main;
            if (cam != null && cam != lastCamera)
            {
                lastCamera = cam;
                // Sin esto URP no dibuja el Volume.
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
                // Pegado al plano cercano para que nada del escenario tape las capas.
                canvas.worldCamera = cam;
                canvas.planeDistance = cam.nearClipPlane + 0.05f;
            }
            if (CameraFX.Instances > 0 || Time.unscaledTime < nextLensScan) return;
            nextLensScan = Time.unscaledTime + 1f;
            foreach (CinemachineCamera vcam in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
                if (!vcam.TryGetComponent(out CameraFXLens _)) vcam.gameObject.AddComponent<CameraFXLens>();
        }

        // ---------------- Aplicar ----------------

        void Apply(in CameraFX.Sample s, float dt)
        {
            Vector2 focus = FocusViewport();

            Set(vignette.intensity, s.vignette, s.vignette > Eps);
            Set(vignette.color, s.vignetteColor, s.vignette > Eps);
            Set(vignette.center, Vector2.Lerp(new Vector2(0.5f, 0.5f), focus, 0.5f), s.vignette > Eps);
            Set(vignette.smoothness, 0.45f, s.vignette > Eps);
            Set(chromatic.intensity, s.chromatic, s.chromatic > Eps);
            Set(distortion.intensity, s.distortion, Mathf.Abs(s.distortion) > Eps);
            Set(distortion.center, focus, Mathf.Abs(s.distortion) > Eps);
            Set(color.saturation, s.saturation, Mathf.Abs(s.saturation) > 0.1f);
            Set(color.contrast, s.contrast, Mathf.Abs(s.contrast) > 0.1f);
            Set(color.postExposure, s.exposure, Mathf.Abs(s.exposure) > Eps);
            Set(color.hueShift, s.hueShift, Mathf.Abs(s.hueShift) > 0.1f);
            Set(color.colorFilter, Color.Lerp(Color.white, s.tint, s.tintAmount), s.tintAmount > Eps);

            Vector2 size = canvasRect.rect.size;
            float scale = size.y / 1080f;
            ApplyImage(flash, s.flashColor, s.flash);
            ApplyImage(edge, s.edgeColor, s.edge);
            ApplyWaves(s, size);
            ApplyFire(s, size);
            ApplyLines(s.speedLines, focus, size);

            // Variación de color de las virutas: el color del fuego, uno más claro y otro con el tono girado.
            Color.RGBToHSV(s.fireColor, out float hue, out float sat, out float val);
            SetRamp(emberMaterials[0], ref emberMaterialColors[0], s.fireColor);
            SetRamp(emberMaterials[1], ref emberMaterialColors[1], Color.Lerp(s.fireColor, CameraFX.CreamColor, 0.4f));
            SetRamp(emberMaterials[2], ref emberMaterialColors[2], Color.HSVToRGB(Mathf.Repeat(hue + 0.05f, 1f), sat, val));
            SetRamp(boltMaterial, ref boltMaterialColor, s.risingColor);
            // Virutas y rayos se colorean con su rampa (el color del vértice solo da el alfa); las líneas, planas.
            UpdateMotes(embers, ref emberSpawn, Mathf.Max(s.embers, Mathf.Clamp01(s.fire) * 0.5f), 22f, size, scale, dt,
                spawnEmber, Color.white, true);
            UpdateMotes(risingLines, ref lineSpawn, s.risingLines, 30f, size, scale, dt, spawnLine, s.risingColor, false);
            // El alfa de risingColor hace los rayos translúcidos (también su contorno).
            UpdateMotes(risingBolts, ref boltSpawn, s.risingBolts, 7f, size, scale, dt, spawnBolt,
                new Color(1f, 1f, 1f, s.risingColor.a), true);
        }

        Action<Mote, float> spawnEmber, spawnLine, spawnBolt;

        static void Set<T>(VolumeParameter<T> parameter, T value, bool active)
        {
            parameter.overrideState = active;
            if (active) parameter.value = value;
        }

        static void ApplyImage(RawImage image, Color tint, float alpha)
        {
            bool on = alpha > Eps;
            if (image.enabled != on) image.enabled = on;
            if (on) image.color = new Color(tint.r, tint.g, tint.b, alpha);
        }

        // Bandas cel-shading a partir del color base (pastel del HUD): sombra un poco más oscura y saturada,
        // base tal cual y luz hacia el crema del HUD. El contorno es siempre la tinta del HUD.
        static void Ramp(Color c, out Color low, out Color mid, out Color high)
        {
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            low = Color.HSVToRGB(h, Mathf.Clamp01(sat * 1.08f), v * 0.8f);
            mid = c;
            high = Color.Lerp(c, CameraFX.CreamColor, 0.6f);
        }

        static void SetRamp(Material material, ref Color cached, Color c)
        {
            if (material == null || cached == c) return;
            cached = c;
            Ramp(c, out Color low, out Color mid, out Color high);
            material.SetColor("_Low", low);
            material.SetColor("_Mid", mid);
            material.SetColor("_High", high);
        }

        static Rect Frame(float time, float fps, float phase)
        {
            int frame = Mathf.FloorToInt(time * fps + phase) % FlipbookFrames;
            if (frame < 0) frame += FlipbookFrames;
            float cell = 1f / FlipbookGrid;
            int col = frame % FlipbookGrid, row = frame / FlipbookGrid; // Fila 0 = arriba.
            return new Rect(col * cell, 1f - (row + 1) * cell, cell, cell);
        }

        // Aura: viñeteado por capas (shader). Entra creciendo desde los bordes con el propio canal.
        void ApplyWaves(in CameraFX.Sample s, Vector2 size)
        {
            if (wavesMaterial == null) return;
            float a = s.aura;
            ApplyImage(waves, s.auraColor, Mathf.Clamp01(a));
            if (!waves.enabled) return;
            wavesMaterial.SetFloat(FXTimeId, Time.unscaledTime);
            wavesMaterial.SetFloat(AspectId, size.x / Mathf.Max(1f, size.y));
            wavesMaterial.SetFloat(ThicknessId, 0.13f * Mathf.Lerp(0.45f, 1f, Smooth(Mathf.Clamp01(a))));
        }

        static readonly int FXTimeId = Shader.PropertyToID("_FXTime");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        // Llamas: cada una aparece cuando el fuego pasa su umbral y crece desde su base.
        void ApplyFire(in CameraFX.Sample s, Vector2 size)
        {
            SetRamp(fireMaterial, ref fireMaterialColor, s.fireColor);
            foreach (Plume p in plumes)
            {
                float target = Mathf.Clamp01((s.fire - p.threshold) / 0.3f);
                p.vis = Mathf.MoveTowards(p.vis, target, Time.unscaledDeltaTime * 3f);
                float v = Smooth(p.vis);
                ApplyImage(p.image, Color.white, v);
                if (!p.image.enabled) continue;
                float side = size.y * p.size;
                p.rect.sizeDelta = new Vector2(side, side);
                p.rect.anchoredPosition = new Vector2(p.x * size.x, p.y * size.y);
                p.rect.localRotation = Quaternion.Euler(0f, 0f, p.rotation);
                p.rect.localScale = new Vector3((p.mirror ? -1f : 1f) * Mathf.Lerp(0.8f, 1f, v), Mathf.Lerp(0.15f, 1f, v), 1f);
                p.image.uvRect = Frame(Time.unscaledTime * p.speed, FireFps, p.phase);
            }
        }

        void ApplyLines(float alpha, Vector2 focus, Vector2 size)
        {
            ApplyImage(lines, Color.white, alpha);
            if (!lines.enabled) return;
            float side = Mathf.Max(size.x, size.y) * 2.2f;
            var rect = lines.rectTransform;
            rect.sizeDelta = new Vector2(side, side);
            rect.anchoredPosition = Vector2.Scale(focus, size);
            // Toon: las líneas «hierven» cambiando a saltos, no girando suave.
            if (Time.unscaledTime >= nextLinesJitter)
            {
                nextLinesJitter = Time.unscaledTime + 0.07f;
                rect.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            }
        }

        // ---------------- Partículas de pantalla ----------------

        // Nacen en las zonas al ritmo del canal; las que ya están terminan su vida aunque el canal se apague.
        static void UpdateMotes(List<Mote> pool, ref float spawn, float amount, float rate, Vector2 size, float scale,
            float dt, Action<Mote, float> spawnFn, Color tint, bool pop)
        {
            spawn += amount * rate * dt;
            while (spawn >= 1f)
            {
                spawn -= 1f;
                Mote free = pool.Find(m => !m.active);
                if (free == null) break;
                free.active = true;
                free.age = 0f;
                free.image.enabled = true;
                spawnFn(free, scale);
            }
            if (amount <= Eps) spawn = 0f;

            float now = Time.unscaledTime;
            foreach (Mote m in pool)
            {
                if (!m.active) continue;
                m.age += dt;
                float u = m.age / m.life;
                if (u >= 1f)
                {
                    m.active = false;
                    m.image.enabled = false;
                    continue;
                }
                m.position += m.velocity * dt;
                float wobble = Mathf.Sin((m.age * m.wobbleRate + m.seed) * Mathf.PI * 2f) * m.wobble;
                m.rect.anchoredPosition = new Vector2((m.position.x + wobble) * size.x, m.position.y * size.y);
                float fade;
                if (pop)
                {
                    // Cartoon: aparece con un «pop» (crece pasándose un poco) y se va encogiendo al final.
                    float grow = Mathf.Clamp01(m.age / 0.15f);
                    float overshoot = 1f + 0.25f * Mathf.Sin(grow * Mathf.PI);
                    float shrink = 1f - Smooth(Mathf.Clamp01((u - 0.65f) / 0.35f));
                    float k = Mathf.Max(0.001f, grow * overshoot * shrink);
                    m.rect.localScale = new Vector3(k, k, 1f);
                    fade = 1f - Smooth(Mathf.Clamp01((u - 0.8f) / 0.2f));
                }
                else
                {
                    if (now >= m.nextFlicker)
                    {
                        m.nextFlicker = now + UnityEngine.Random.Range(0.04f, 0.1f);
                        m.flicker = UnityEngine.Random.Range(0.8f, 1f);
                    }
                    // Entra rápido y se difumina al subir / al acabar su vida.
                    fade = Mathf.Clamp01(u * 10f) * (1f - Smooth(u)) * m.flicker;
                }
                m.image.color = new Color(tint.r, tint.g, tint.b, fade * m.alpha * tint.a);
            }
        }

        // Elige zona por peso y devuelve el punto de nacimiento y hacia dónde «dentro» de la pantalla queda.
        static Vector2 ZonePoint(float corner, float center, float side, out float inward)
        {
            float r = UnityEngine.Random.value * (corner + center + side);
            bool right = UnityEngine.Random.value < 0.5f;
            Vector2 p;
            if (r < corner) p = new Vector2(UnityEngine.Random.Range(0f, 0.14f), UnityEngine.Random.Range(-0.02f, 0.08f));
            else if (r < corner + center) p = new Vector2(UnityEngine.Random.Range(0.3f, 0.5f), UnityEngine.Random.Range(-0.02f, 0.03f));
            else p = new Vector2(UnityEngine.Random.Range(0f, 0.03f), UnityEngine.Random.Range(0.15f, 0.6f));
            inward = right ? -1f : 1f;
            if (right) p.x = 1f - p.x;
            return p;
        }

        void SpawnEmber(Mote m, float scale)
        {
            m.position = ZonePoint(0.6f, 0.2f, 0.2f, out float inward);
            m.velocity = new Vector2(inward * UnityEngine.Random.Range(0f, 0.05f), UnityEngine.Random.Range(0.12f, 0.28f));
            m.life = UnityEngine.Random.Range(1.1f, 2.2f);
            m.wobble = UnityEngine.Random.Range(0.006f, 0.02f);
            m.wobbleRate = UnityEngine.Random.Range(0.8f, 2.2f);
            m.seed = UnityEngine.Random.value;
            m.alpha = 1f;
            // Bolitas y algún destello de cuatro puntas, cada uno con una de las tres rampas de color.
            bool sparkle = UnityEngine.Random.value < 0.3f;
            m.image.texture = sparkle ? CameraFXTextures.Sparkle : CameraFXTextures.Ember;
            m.image.material = emberMaterials[UnityEngine.Random.Range(0, emberMaterials.Length)];
            float s = (sparkle ? UnityEngine.Random.Range(16f, 28f) : UnityEngine.Random.Range(9f, 20f)) * scale;
            m.rect.sizeDelta = new Vector2(s, s);
            m.rect.localRotation = Quaternion.Euler(0f, 0f, sparkle ? UnityEngine.Random.Range(-20f, 20f) : 0f);
        }

        static void SpawnLine(Mote m, float scale)
        {
            m.position = ZonePoint(0.5f, 0.2f, 0.3f, out float inward);
            float speed = UnityEngine.Random.Range(0.9f, 1.6f);
            m.velocity = new Vector2(inward * 0.02f, speed);
            m.life = LinesFadeHeight / speed;
            m.wobble = 0f;
            m.alpha = UnityEngine.Random.Range(0.5f, 1f);
            m.rect.sizeDelta = new Vector2(UnityEngine.Random.Range(3f, 7f) * scale, UnityEngine.Random.Range(110f, 280f) * scale);
            m.rect.localRotation = Quaternion.identity;
        }

        void SpawnBolt(Mote m, float scale)
        {
            m.position = ZonePoint(0.6f, 0.15f, 0.25f, out float inward);
            float speed = UnityEngine.Random.Range(0.22f, 0.4f);
            m.velocity = new Vector2(inward * UnityEngine.Random.Range(0f, 0.04f), speed);
            m.life = BoltsFadeHeight / speed;
            m.wobble = UnityEngine.Random.Range(0.003f, 0.01f);
            m.wobbleRate = UnityEngine.Random.Range(1f, 2f);
            m.seed = UnityEngine.Random.value;
            m.alpha = 1f;
            // Iconos de rayo y algún destello de cuatro puntas entre medias.
            bool sparkle = UnityEngine.Random.value < 0.35f;
            m.image.texture = sparkle ? CameraFXTextures.Sparkle : CameraFXTextures.Bolt;
            m.image.material = boltMaterial;
            float s = (sparkle ? UnityEngine.Random.Range(26f, 44f) : UnityEngine.Random.Range(52f, 92f)) * scale;
            m.rect.sizeDelta = new Vector2(s, s);
            m.rect.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-20f, 20f));
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static Vector2 FocusViewport()
        {
            Camera cam = Camera.main;
            if (CameraFX.Focus == null || cam == null) return new Vector2(0.5f, 0.5f);
            Vector3 p = cam.WorldToViewportPoint(CameraFX.Focus.position + Vector3.up * 0.8f);
            if (p.z <= 0f) return new Vector2(0.5f, 0.5f);
            return new Vector2(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y));
        }
    }

    /// <summary>FOV y alabeo de <see cref="CameraFX"/> (extensión de Cinemachine; se añade sola).</summary>
    public sealed class CameraFXLens : CinemachineExtension
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            CameraFX.Instances++;
        }

        void OnDisable() => CameraFX.Instances--;

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize || !Application.isPlaying) return;
            float fov = CameraFX.FovMultiplier;
            if (state.Lens.Orthographic) state.Lens.OrthographicSize *= fov;
            else state.Lens.FieldOfView = Mathf.Clamp(state.Lens.FieldOfView * fov, 1f, 179f);
            state.Lens.Dutch += CameraFX.Dutch;
        }
    }

    /// <summary>
    /// Texturas de las capas. Llamas, rayo, viruta y destello están horneados (Tools/CameraFX/bake_camera_fx.py) en
    /// Visuals/VFX/CameraFX/Resources/CameraFX; el resto se genera al arrancar (planas, a juego con el estilo toon).
    /// </summary>
    static class CameraFXTextures
    {
        const string Folder = "CameraFX/";

        static Texture2D edge, speedLines, streak, fire, bolt, ember, sparkle;
        static Shader rampShader, wavesShader;
        static bool loaded;

        public static Texture2D Edge => edge != null ? edge : edge = BuildEdge(256);
        public static Texture2D SpeedLines => speedLines != null ? speedLines : speedLines = BuildSpeedLines(512, 96);
        public static Texture2D Streak => streak != null ? streak : streak = BuildStreak(16, 128);

        // Horneadas. Fuego y aura pueden faltar (entonces esas capas no se montan); rayo y viruta tienen respaldo.
        public static Texture2D Fire { get { Load(); return fire; } }
        public static Texture2D Bolt { get { Load(); return bolt != null ? bolt : Streak; } }
        public static Texture2D Ember { get { Load(); return ember != null ? ember : ember = BuildEmber(32); } }
        public static Texture2D Sparkle { get { Load(); return sparkle != null ? sparkle : Ember; } }
        public static Shader RampShader { get { Load(); return rampShader; } }
        public static Shader WavesShader { get { Load(); return wavesShader; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            fire = Resources.Load<Texture2D>(Folder + "T_CamFX_Fire");
            bolt = Resources.Load<Texture2D>(Folder + "T_CamFX_Bolt");
            ember = Resources.Load<Texture2D>(Folder + "T_CamFX_Ember");
            sparkle = Resources.Load<Texture2D>(Folder + "T_CamFX_Sparkle");
            rampShader = Resources.Load<Shader>(Folder + "CameraFXRamp");
            wavesShader = Resources.Load<Shader>(Folder + "CameraFXWaves");
            if (fire == null || bolt == null || ember == null || sparkle == null || rampShader == null || wavesShader == null)
                Debug.LogWarning("CameraFX: faltan texturas horneadas o el shader en Resources/CameraFX " +
                                 "(Tools/CameraFX/bake_camera_fx.py). Las llamas, virutas, rayos o el aura no se verán.");
        }

        // Transparente en el centro, opaco en los bordes (rectángulo redondeado).
        static Texture2D BuildEdge(int size)
        {
            var tex = New(size, size, "CameraFX Edge");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = Mathf.Abs(x / (size - 1f) * 2f - 1f);
                float v = Mathf.Abs(y / (size - 1f) * 2f - 1f);
                float d = Mathf.Pow(Mathf.Pow(u, 4f) + Mathf.Pow(v, 4f), 0.25f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1.05f, d));
                pixels[y * size + x] = White(a);
            }
            return Done(tex, pixels);
        }

        // Rayos radiales que empiezan a distintas distancias del centro, como en el cómic.
        static Texture2D BuildSpeedLines(int size, int rays)
        {
            var tex = New(size, size, "CameraFX SpeedLines");
            var rng = new System.Random(1234);
            var start = new float[rays];
            var width = new float[rays];
            for (int i = 0; i < rays; i++)
            {
                bool on = rng.NextDouble() < 0.6;
                start[i] = on ? 0.3f + (float)rng.NextDouble() * 0.15f : 2f;
                width[i] = 0.15f + (float)rng.NextDouble() * 0.3f;
            }
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 0.5f;
                float t = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f) * rays;
                int ray = Mathf.Clamp((int)t, 0, rays - 1);
                float across = Mathf.Abs(t - ray - 0.5f) * 2f; // 0 = centro del rayo.
                // El rayo se afina hacia dentro.
                float thin = width[ray] * Mathf.InverseLerp(start[ray], 0.5f, r);
                float a = r > start[ray] ? Mathf.Clamp01((thin - across) * 6f) : 0f;
                a *= Mathf.InverseLerp(start[ray], start[ray] + 0.04f, r);
                pixels[y * size + x] = White(a * 0.8f);
            }
            return Done(tex, pixels);
        }

        // Línea vertical cartoon: cápsula plana (sin halo) cuya cola se afina hacia abajo.
        static Texture2D BuildStreak(int width, int height)
        {
            var tex = New(width, height, "CameraFX Streak");
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float across = Mathf.Abs(x / (width - 1f) * 2f - 1f);
                float v = y / (height - 1f);
                float half = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(v * 1.4f)) * Mathf.Sqrt(Mathf.Clamp01((1f - v) * 14f));
                pixels[y * width + x] = White(Mathf.Clamp01((half - across) * width * 0.5f));
            }
            return Done(tex, pixels);
        }

        // Respaldo de la viruta: punto con halo.
        static Texture2D BuildEmber(int size)
        {
            var tex = New(size, size, "CameraFX Ember (respaldo)");
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x / (size - 1f) * 2f - 1f, y / (size - 1f) * 2f - 1f).magnitude;
                pixels[y * size + x] = White(Mathf.Max(Mathf.Clamp01((0.32f - r) / 0.12f), Mathf.Exp(-r * r / 0.3f) * 0.55f));
            }
            return Done(tex, pixels);
        }

        static Color32 White(float a) => new(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));

        static Texture2D New(int width, int height, string name) => new(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

        static Texture2D Done(Texture2D tex, Color32[] pixels)
        {
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
