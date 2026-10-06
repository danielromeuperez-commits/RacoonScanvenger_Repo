using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Efectos visuales del jugador. Se ejecuta en TODOS los clientes (los UnityEvents del
    /// PlayerController se lanzan en todos), así que no hace falta sincronizar nada más por red.
    ///
    /// Ponlo en el MISMO GameObject que el Animator (el modelo): los Animation Events solo llaman
    /// a métodos de componentes de ese GameObject.
    ///
    /// Se suscribe solo por código a los eventos del PlayerController (NO conectarlos también en el
    /// Inspector o se ejecutarían dos veces):
    ///  - onLightHit    → OnLightHit: parpadeo rápido mientras está aturdido (golpes 1º y 2º).
    ///  - onHeavyHit    → OnHeavyHit: parpadeo rápido en el suelo + lento durante los i-frames al
    ///                    levantarse (se corta si los gasta atacando o con un dash).
    ///  - onDash        → OnDash: polvo (uno por cada valor de dashDustDelays) + trail de viento,
    ///                    orientados en la dirección del dash.
    /// Animation Events: función "PlayVFX" con parámetro string = id del efecto (p. ej. "HitDust"
    /// en los keyframes de la animación de recibir golpe).
    /// </summary>
    public class PlayerVFX : MonoBehaviour
    {
        [Serializable]
        public class VfxEntry
        {
            public string id;
            public GameObject prefab;
            [Tooltip("Punto de referencia (null = raíz del jugador).")]
            public Transform anchor;
            [Tooltip("Desplazamiento en el espacio del efecto (Z = hacia donde mira).")]
            public Vector3 offset;
            [Tooltip("El efecto sigue al anchor (hijo suyo). Si no, se queda en el mundo.")]
            public bool attachToAnchor;
            [Tooltip("Mirar en sentido contrario a la dirección (p. ej. polvo que sale hacia atrás).")]
            public bool faceOpposite;
            [Tooltip("Segundos hasta destruirlo (0 = lo destruye el propio prefab, p. ej. Stop Action = Destroy).")]
            public float lifetime = 2f;
        }

        [SerializeField] PlayerController controller;
        [SerializeField] List<VfxEntry> effects = new();

        [Header("Dash")]
        [SerializeField] string dashDustId = "DashDust";
        [Tooltip("Un polvo por cada valor: segundos desde el inicio del dash.")]
        [SerializeField] float[] dashDustDelays = { 0f, 0.12f };
        [SerializeField] string dashTrailId = "DashTrail";

        [Header("Parpadeo al recibir golpes")]
        [Tooltip("Mientras está aturdido / derribado (sin control).")]
        [SerializeField] float blinkInterval = 0.08f;
        [Tooltip("Durante los i-frames al levantarse (ya puede moverse, pero no le pueden golpear).")]
        [SerializeField] float recoveryBlinkInterval = 0.16f;

        readonly Dictionary<string, VfxEntry> lookup = new();
        readonly List<Renderer> blinkRenderers = new();
        Coroutine blinkRoutine;

        void Reset() => controller = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            foreach (VfxEntry entry in effects)
                if (!string.IsNullOrEmpty(entry.id)) lookup[entry.id] = entry;
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.onLightHit.AddListener(OnLightHit);
            controller.onHeavyHit.AddListener(OnHeavyHit);
            controller.onDash.AddListener(OnDash);
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.onLightHit.RemoveListener(OnLightHit);
                controller.onHeavyHit.RemoveListener(OnHeavyHit);
                controller.onDash.RemoveListener(OnDash);
            }
            StopBlink();
        }

        // ---------------- API ----------------

        /// <summary>Para Animation Events. Orienta el efecto hacia donde mira el jugador.</summary>
        public void PlayVFX(string id) => Spawn(id, controller.transform.forward);

        /// <summary>Instancia el efecto mirando en <paramref name="direction"/>. Devuelve null si no existe.</summary>
        public GameObject Spawn(string id, Vector3 direction)
        {
            if (!lookup.TryGetValue(id, out VfxEntry entry) || entry.prefab == null)
            {
                Debug.LogWarning($"PlayerVFX: no hay efecto '{id}' configurado.", this);
                return null;
            }

            Transform anchor = entry.anchor != null ? entry.anchor : controller.transform;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = controller.transform.forward;
            if (entry.faceOpposite) direction = -direction;

            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            Vector3 position = anchor.position + rotation * entry.offset;
            GameObject instance = Instantiate(entry.prefab, position, rotation, entry.attachToAnchor ? anchor : null);
            if (entry.lifetime > 0f) Destroy(instance, entry.lifetime);
            return instance;
        }

        // ---------------- Dash ----------------

        /// <summary>Suscrito a PlayerController.onDash.</summary>
        public void OnDash(Vector3 direction)
        {
            // Sin configurar todavía: no avisar en cada dash.
            if (lookup.ContainsKey(dashDustId))
            {
                foreach (float delay in dashDustDelays)
                {
                    if (delay <= 0f) Spawn(dashDustId, direction);
                    else StartCoroutine(SpawnDelayed(dashDustId, direction, delay));
                }
            }

            if (!lookup.ContainsKey(dashTrailId)) return;
            GameObject trail = Spawn(dashTrailId, direction);
            // Al acabar el dash el trail se suelta y deja de emitir, para que se desvanezca solo.
            if (trail != null && trail.transform.parent != null)
                StartCoroutine(ReleaseTrail(trail, controller.DashDuration));
        }

        IEnumerator SpawnDelayed(string id, Vector3 direction, float delay)
        {
            yield return new WaitForSeconds(delay);
            Spawn(id, direction);
        }

        static IEnumerator ReleaseTrail(GameObject trail, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (trail == null) yield break;

            trail.transform.SetParent(null, true);
            foreach (ParticleSystem particles in trail.GetComponentsInChildren<ParticleSystem>())
                particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            foreach (TrailRenderer trailRenderer in trail.GetComponentsInChildren<TrailRenderer>())
                trailRenderer.emitting = false;
        }

        // ---------------- Parpadeo ----------------

        /// <summary>Suscrito a PlayerController.onLightHit (golpes 1º y 2º del combo).</summary>
        public void OnLightHit(PlayerController attacker, int hitNumber) => StartBlink(controller.LightHit);

        /// <summary>Suscrito a PlayerController.onHeavyHit (golpe 3º: derribo).</summary>
        public void OnHeavyHit(PlayerController attacker) => StartBlink(controller.HeavyHit);

        // Un golpe nuevo reinicia el parpadeo.
        void StartBlink(PlayerController.HitReaction reaction)
        {
            StopBlink();

            // Se recogen al empezar: así entran también los objetos equipados que se hayan añadido.
            foreach (Renderer rend in controller.GetComponentsInChildren<Renderer>())
                if ((rend is MeshRenderer || rend is SkinnedMeshRenderer) && rend.enabled)
                    blinkRenderers.Add(rend);

            blinkRoutine = StartCoroutine(Blink(reaction.stunDuration, reaction.recoveryInvulnerability));
        }

        IEnumerator Blink(float stunDuration, float recoveryDuration)
        {
            bool visible = true;

            // Sin control: parpadeo rápido. Dura lo mismo que el stun.
            float end = Time.time + stunDuration;
            WaitForSeconds wait = new(blinkInterval);
            while (Time.time < end)
            {
                visible = !visible;
                SetRenderersVisible(visible);
                yield return wait;
            }

            // I-frames al levantarse: parpadeo lento. IsInvulnerable está replicado, así que se corta
            // en todos los clientes si el jugador los gasta atacando.
            end = Time.time + recoveryDuration;
            wait = new WaitForSeconds(recoveryBlinkInterval);
            while (Time.time < end && controller.IsInvulnerable)
            {
                visible = !visible;
                SetRenderersVisible(visible);
                yield return wait;
            }

            StopBlink();
        }

        void StopBlink()
        {
            if (blinkRoutine != null) StopCoroutine(blinkRoutine);
            blinkRoutine = null;
            SetRenderersVisible(true);
            blinkRenderers.Clear();
        }

        void SetRenderersVisible(bool visible)
        {
            foreach (Renderer rend in blinkRenderers)
                if (rend != null) rend.enabled = visible;
        }
    }
}
