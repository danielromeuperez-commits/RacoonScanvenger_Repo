using System.Collections.Generic;
using UnityEngine;

namespace Racoon.VFX
{
    /// <summary>
    /// Fuegos artificiales (traído de MonedasVFX). Un programa determinista de carcasas (semilla): el cohete sube con
    /// su estela hasta el apogeo balístico (v0 = √(2gh)) y allí estalla en estrellas que frenan con el aire y caen
    /// (peonía, crisantemo, sauce dorado, anillo, purpurina). Se repite en bucle mientras esté activo.
    ///
    /// Va en tiempo sin escalar (la pantalla de resultado puede tener el juego parado). En Racoon lo usa
    /// MatchResultUI: lo monta lejos del nivel en una capa que solo ve su cámara (PF_VFX_Fireworks).
    /// </summary>
    public class FireworksShow : MonoBehaviour
    {
        public enum ShellType { Peony, Chrysanthemum, Willow, Ring, Glitter }

        struct Shell
        {
            public float launch;
            public Vector3 origin;
            public float height;
            public ShellType type;
            public Color color;
            public float speed;
        }

        public int seed = 7;
        [Min(4f)] public float period = 26f;
        public bool autoPlay = true;
        [Tooltip("Segundos del programa ya transcurridos al activarse: arranca con el cielo lleno.")]
        [Min(0f)] public float lead = 7f;
        [Tooltip("Área de lanzamiento (m, alrededor de este objeto).")]
        public Vector2 launchArea = new Vector2(40f, 12f);
        public Vector2 heightRange = new Vector2(55f, 95f);
        public ParticleSystem rockets;
        public ParticleSystem stars;
        public ParticleSystem willow;
        public ParticleSystem glitter;
        public ParticleSystem smoke;
        public Light[] lights = new Light[0];
        [Min(0f)] public float lightIntensity = 260f;

        static readonly Color[] Palette =
        {
            new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.55f, 1f), new Color(0.35f, 1f, 0.45f), new Color(1f, 0.8f, 0.3f),
            new Color(0.85f, 0.35f, 1f), new Color(1f, 1f, 1f), new Color(0.3f, 1f, 0.95f),
        };

        readonly List<(float time, System.Action action)> events = new();
        readonly List<(float time, Vector3 position, Color color)> bursts = new();
        List<Shell> program;
        float clock;
        float cycleStart;

        ParticleSystem[] Systems => new[] { rockets, stars, willow, glitter, smoke };

        void Awake()
        {
            foreach (ParticleSystem ps in Systems)
                if (ps != null)
                {
                    ParticleSystem.MainModule main = ps.main;
                    main.useUnscaledTime = true;
                }
        }

        void OnEnable()
        {
            events.Clear();
            bursts.Clear();
            clock = 0f;
            cycleStart = 0f;
            if (!autoPlay || !Application.isPlaying)
                return;
            ScheduleCycle(0f);
            Preview(lead);
            foreach (ParticleSystem ps in Systems)
                if (ps != null)
                    ps.Play();
        }

        void Update()
        {
            Advance(Time.unscaledDeltaTime);
            if (autoPlay && clock > cycleStart + period)
                ScheduleCycle(clock);
            UpdateLights(clock);
        }

        // ------------------------------------------------------------------ Reloj
        void Advance(float dt)
        {
            clock += dt;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].time > clock)
                    continue;
                System.Action action = events[i].action;
                events.RemoveAt(i--);
                action?.Invoke();
            }
        }

        // Avance rápido al activarse: pasos de 1/60 s simulando las partículas a la vez.
        void Preview(float seconds)
        {
            const float step = 1f / 60f;
            foreach (ParticleSystem ps in Systems)
                if (ps != null)
                    ps.Clear(true);
            for (float t = 0f; t < seconds; t += step)
            {
                Advance(step);
                foreach (ParticleSystem ps in Systems)
                    if (ps != null)
                        ps.Simulate(step, false, false, false);
            }
        }

        // ------------------------------------------------------------------ Programa
        List<Shell> BuildProgram()
        {
            var random = new System.Random(seed);
            float R() => (float)random.NextDouble();
            var list = new List<Shell>();
            float t = 0f;
            int index = 0;
            // Cuerpo del espectáculo: carcasas sueltas cada vez más seguidas
            while (t < period - 7f)
            {
                list.Add(MakeShell(t, index++, R));
                t += Mathf.Lerp(1.6f, 0.7f, t / period) + R() * 0.5f;
            }
            // Traca final: varias a la vez
            for (int i = 0; i < 7; i++)
                list.Add(MakeShell(period - 6.5f + i * 0.22f, index++, R));
            return list;
        }

        Shell MakeShell(float launch, int index, System.Func<float> R)
        {
            var type = (ShellType)(index % 5 == 4 ? 4 : (int)(R() * 4f));
            Color color = type == ShellType.Willow ? new Color(1f, 0.72f, 0.3f) : Palette[(int)(R() * Palette.Length) % Palette.Length];
            return new Shell
            {
                launch = launch,
                origin = new Vector3((R() - 0.5f) * launchArea.x, 0f, (R() - 0.5f) * launchArea.y),
                height = Mathf.Lerp(heightRange.x, heightRange.y, R()),
                type = type,
                color = color,
                speed = Mathf.Lerp(30f, 42f, R()),
            };
        }

        void ScheduleCycle(float start)
        {
            cycleStart = start;
            program ??= BuildProgram();
            foreach (Shell shell in program)
            {
                Shell s = shell;
                float g = Mathf.Abs(Physics.gravity.y);
                float v0 = Mathf.Sqrt(2f * g * s.height);
                float apex = v0 / g;
                events.Add((start + s.launch, () => Launch(s, v0, apex)));
                events.Add((start + s.launch + apex, () => Burst(s, start + s.launch + apex)));
            }
        }

        // El origen se guarda relativo: se convierte aquí para que el espectáculo siga al objeto donde se monte.
        Vector3 World(Vector3 local) => transform.position + transform.rotation * local;

        void Launch(Shell s, float v0, float apex)
        {
            if (rockets == null)
                return;
            var p = new ParticleSystem.EmitParams
            {
                position = World(s.origin),
                velocity = Vector3.up * v0 + Random.insideUnitSphere * 0.6f,
                startLifetime = apex,
                startColor = Color.Lerp(s.color, new Color(1f, 0.75f, 0.45f), 0.6f),
                applyShapeToPosition = false,
            };
            rockets.Emit(p, 1);
        }

        void Burst(Shell s, float time)
        {
            Vector3 center = World(s.origin) + Vector3.up * s.height;
            bursts.Add((time, center, s.color));
            ParticleSystem target = s.type == ShellType.Willow ? willow : s.type == ShellType.Glitter ? glitter : stars;
            if (target == null)
                return;
            int count = s.type == ShellType.Glitter ? 220 : s.type == ShellType.Ring ? 90 : 150;
            Vector3 ringAxis = Random.onUnitSphere;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir;
                if (s.type == ShellType.Ring)
                {
                    float a = i / (float)count * Mathf.PI * 2f;
                    Vector3 u = Vector3.Cross(ringAxis, Vector3.up).normalized;
                    if (u.sqrMagnitude < 0.01f) u = Vector3.right;
                    Vector3 w = Vector3.Cross(ringAxis, u);
                    dir = u * Mathf.Cos(a) + w * Mathf.Sin(a);
                }
                else
                {
                    // Esfera de Fibonacci: reparto uniforme, sin agujeros ni grumos
                    float y = 1f - 2f * (i + 0.5f) / count;
                    float r = Mathf.Sqrt(1f - y * y);
                    float phi = i * 2.39996323f;
                    dir = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
                }
                float speed = s.speed * Random.Range(0.92f, 1.06f) * (s.type == ShellType.Willow ? 0.75f : 1f);
                Color c = s.type == ShellType.Chrysanthemum && i % 2 == 0 ? Color.Lerp(s.color, Color.white, 0.6f) : s.color;
                var p = new ParticleSystem.EmitParams
                {
                    position = center,
                    velocity = dir * speed,
                    startColor = c,
                    applyShapeToPosition = false,
                };
                target.Emit(p, 1);
            }
            if (smoke != null)
                for (int i = 0; i < 2; i++)
                {
                    var p = new ParticleSystem.EmitParams
                    {
                        position = center + Random.insideUnitSphere * s.speed * 0.4f,
                        velocity = Random.insideUnitSphere * 0.6f,
                        rotation = Random.value * 360f,
                        applyShapeToPosition = false,
                    };
                    smoke.Emit(p, 1);
                }
        }

        void UpdateLights(float now)
        {
            if (lights == null || lights.Length == 0)
                return;
            int slot = 0;
            for (int i = bursts.Count - 1; i >= 0 && slot < lights.Length; i--)
            {
                float age = now - bursts[i].time;
                if (age < 0f || age > 2.5f)
                    continue;
                Light light = lights[slot++];
                light.transform.position = bursts[i].position;
                light.color = bursts[i].color;
                light.intensity = lightIntensity * Mathf.Exp(-age * 1.8f) * (age < 0.05f ? 1.6f : 1f);
                light.enabled = true;
            }
            for (; slot < lights.Length; slot++)
                lights[slot].enabled = false;
        }
    }
}
