using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace Racoon.Cameras
{
    /// <summary>Cómo tiembla la cámara en una interacción (ver <see cref="CameraShaker"/>).</summary>
    [Serializable]
    public class CameraShakeProfile
    {
        public bool enabled = true;
        [Tooltip("Segundos (en los continuos, como correr, no se usa).")]
        public float duration = 0.3f;
        [Tooltip("Grados de giro: cabeceo (X), guiñada (Y) y alabeo (Z).")]
        public Vector3 rotation = new(1f, 1f, 1f);
        [Tooltip("Metros de vaivén: derecha (X), arriba (Y) y adelante (Z), en espacio de cámara.")]
        public Vector3 position = new(0.03f, 0.03f, 0f);
        [Tooltip("Vibraciones por segundo (alto = seco y nervioso, bajo = balanceo).")]
        public float frequency = 18f;
        [Tooltip("Empujón (metros) en la dirección del golpe / movimiento, que vuelve con un rebote.")]
        public float kick;
        [Tooltip("Alabeo (grados) hacia el lado del empujón.")]
        public float kickRoll;
        [Tooltip("Fracción de la duración en llegar al máximo (0 = de golpe).")]
        [Range(0f, 0.9f)] public float attack;
        [Tooltip("Cómo se apaga: 1 = lineal, más = cae rápido y deja una cola suave.")]
        public float decayPower = 2f;

        public CameraShakeProfile() { }

        public CameraShakeProfile(float duration, Vector3 rotation, Vector3 position, float frequency,
            float kick = 0f, float kickRoll = 0f, float decayPower = 2f, float attack = 0f)
        {
            this.duration = duration;
            this.rotation = rotation;
            this.position = position;
            this.frequency = frequency;
            this.kick = kick;
            this.kickRoll = kickRoll;
            this.decayPower = decayPower;
            this.attack = attack;
        }
    }

    /// <summary>
    /// Temblor de cámara por código (extensión de Cinemachine). La cámara no es un objeto de red: cada máquina
    /// tiene la suya, así que lo que se lanza aquí solo se ve en esta máquina (quien lo lanza decide si le toca,
    /// p. ej. solo el jugador local).
    ///  - <see cref="Play"/>: un temblor puntual (golpe, dash...), con empujón opcional en una dirección.
    ///  - <see cref="SetContinuous"/>: un temblor que dura mientras se mantenga (correr, cansancio), con su
    ///    intensidad suavizada.
    /// No hace falta montarlo: se añade solo a las CinemachineCamera de la escena la primera vez que se usa.
    /// </summary>
    public class CameraShaker : CinemachineExtension
    {
        /// <summary>Multiplicador global (opciones de accesibilidad: 0 = sin temblores).</summary>
        public static float GlobalIntensity = 1f;

        const int MaxShakes = 16;

        sealed class Shake
        {
            public CameraShakeProfile profile;
            public float strength;
            public Vector3 direction;
            public float age;
            public float seed;
        }

        sealed class Layer
        {
            public CameraShakeProfile profile;
            public float target;
            public float current;
            public float fadeSpeed;
            public float seed;
        }

        static readonly List<Shake> shakes = new();
        static readonly Dictionary<object, Layer> layers = new();
        static readonly List<object> finishedLayers = new();
        static int instances;
        static int lastFrame = -1;
        static float time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            shakes.Clear();
            layers.Clear();
            instances = 0;
            lastFrame = -1;
            time = 0f;
            GlobalIntensity = 1f;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            instances++;
        }

        void OnDisable() => instances--;

        // ---------------- API ----------------

        /// <summary>
        /// Temblor puntual. <paramref name="direction"/> (mundo): hacia dónde empuja el kick del perfil
        /// (p. ej. hacia donde sale despedido); cero = sin empujón.
        /// </summary>
        public static void Play(CameraShakeProfile profile, float strength = 1f, Vector3 direction = default)
        {
            if (profile == null || !profile.enabled || strength <= 0f || profile.duration <= 0f) return;
            EnsureOnCameras();
            if (shakes.Count >= MaxShakes) shakes.RemoveAt(0);
            shakes.Add(new Shake
            {
                profile = profile,
                strength = strength,
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero,
                seed = UnityEngine.Random.Range(0f, 100f),
            });
        }

        /// <summary>
        /// Temblor continuo identificado por <paramref name="key"/>: va hacia <paramref name="intensity"/>
        /// (0 = apagado) suavemente, a <paramref name="fadeSpeed"/> unidades por segundo.
        /// </summary>
        public static void SetContinuous(object key, CameraShakeProfile profile, float intensity, float fadeSpeed = 3f)
        {
            if (key == null) return;
            if (!layers.TryGetValue(key, out Layer layer))
            {
                if (intensity <= 0f || profile == null || !profile.enabled) return;
                EnsureOnCameras();
                layer = new Layer { seed = UnityEngine.Random.Range(0f, 100f) };
                layers[key] = layer;
            }
            layer.profile = profile;
            layer.target = profile != null && profile.enabled ? Mathf.Max(0f, intensity) : 0f;
            layer.fadeSpeed = Mathf.Max(0.01f, fadeSpeed);
        }

        /// <summary>Quita de golpe un temblor continuo (p. ej. al destruirse quien lo puso).</summary>
        public static void ClearContinuous(object key)
        {
            if (key != null) layers.Remove(key);
        }

        // La primera vez (o si la cámara se ha recreado) se añade a las CinemachineCamera de la escena.
        static void EnsureOnCameras()
        {
            if (instances > 0) return;
            foreach (CinemachineCamera cam in FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None))
                if (!cam.TryGetComponent(out CameraShaker _)) cam.gameObject.AddComponent<CameraShaker>();
        }

        // ---------------- Cinemachine ----------------

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage,
            ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Noise || !Application.isPlaying) return;
            Advance();
            if (shakes.Count == 0 && layers.Count == 0) return;

            Quaternion orientation = state.GetCorrectedOrientation();
            Vector3 localPosition = Vector3.zero, worldPosition = Vector3.zero, euler = Vector3.zero;

            foreach (Shake shake in shakes)
            {
                CameraShakeProfile p = shake.profile;
                float u = Mathf.Clamp01(shake.age / p.duration);
                float envelope = (u < p.attack ? u / Mathf.Max(p.attack, 1e-4f) : Mathf.Pow(1f - (u - p.attack) / Mathf.Max(1f - p.attack, 1e-4f), Mathf.Max(p.decayPower, 0.1f)))
                    * shake.strength;
                AddNoise(p, shake.seed, shake.age, envelope, ref localPosition, ref euler);

                if (shake.direction == Vector3.zero || (p.kick == 0f && p.kickRoll == 0f)) continue;
                // Empujón seco que vuelve con un rebote.
                float kick = (1f - u) * (1f - u) * Mathf.Cos(u * Mathf.PI * 3f) * shake.strength;
                worldPosition += shake.direction * (p.kick * kick);
                float side = Vector3.Dot(shake.direction, orientation * Vector3.right);
                euler.z -= p.kickRoll * kick * Mathf.Sign(side == 0f ? 1f : side);
            }

            foreach (Layer layer in layers.Values)
                if (layer.current > 0.0001f) AddNoise(layer.profile, layer.seed, time, layer.current, ref localPosition, ref euler);

            float global = Mathf.Max(0f, GlobalIntensity);
            state.PositionCorrection += (orientation * localPosition + worldPosition) * global;
            state.OrientationCorrection *= Quaternion.Euler(euler * global);
        }

        static void AddNoise(CameraShakeProfile p, float seed, float t, float amount, ref Vector3 position, ref Vector3 euler)
        {
            float f = t * p.frequency;
            euler += Vector3.Scale(p.rotation, Noise3(seed, f)) * amount;
            position += Vector3.Scale(p.position, Noise3(seed + 37.1f, f)) * amount;
        }

        static Vector3 Noise3(float seed, float t) => new(
            Mathf.PerlinNoise(seed, t) * 2f - 1f,
            Mathf.PerlinNoise(seed + 13.7f, t) * 2f - 1f,
            Mathf.PerlinNoise(seed + 27.3f, t) * 2f - 1f);

        // Una vez por frame aunque haya varias cámaras.
        static void Advance()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            float dt = Time.deltaTime;
            time += dt;

            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                shakes[i].age += dt;
                if (shakes[i].age >= shakes[i].profile.duration) shakes.RemoveAt(i);
            }

            finishedLayers.Clear();
            foreach (KeyValuePair<object, Layer> pair in layers)
            {
                Layer layer = pair.Value;
                layer.current = Mathf.MoveTowards(layer.current, layer.target, layer.fadeSpeed * dt);
                if (layer.current <= 0f && layer.target <= 0f) finishedLayers.Add(pair.Key);
            }
            foreach (object key in finishedLayers) layers.Remove(key);
        }
    }
}
