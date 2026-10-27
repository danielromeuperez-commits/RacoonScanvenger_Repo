using System;
using System.Collections;
using System.Collections.Generic;
using Racoon.VFX;
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
    ///  - onLightHit    → OnLightHit: impacto en el punto de la malla donde entra el golpe (ImpactVFX) con sus
    ///                    impact frames (pausa de los dos jugadores + destello), parpadeo rápido mientras está
    ///                    aturdido (golpes 1º y 2º; empieza al acabar la pausa) y polvo del empujón (KnockbackDustVFX).
    ///  - onHeavyHit    → OnHeavyHit: parpadeo rápido en el suelo + lento durante los i-frames al
    ///                    levantarse (se corta si los gasta atacando o con un dash), impacto fuerte, polvo
    ///                    del derribo y estrellas de mareo (DizzyStarsVFX) hasta que se levanta.
    ///  - ActionStarted → Punch: golpe al aire (SwingVFX), conecte o no.
    ///  - Estamina      → cansancio (TiredVFX: aliento y sudor) mientras está agotado.
    ///  - onDash        → OnDash: humo del dash (FootstepVFX.PlayDash: explosión hacia atrás + rastro + colas) y, si hay
    ///                    un efecto "DashDust" en la lista, también ese (uno por cada valor de dashDustDelays).
    ///  - StateChanged  → estelas de viento (WindTrailVFX hijo del jugador): fuertes en Dash, flojas en Run,
    ///                    apagadas en el resto (y al volver a encenderse salen en otras posiciones).
    ///                  → pasos (FootstepVFX hijo del jugador): polvo + huellas al andar/correr; apagados
    ///                    en Dash, HitStun y Knockdown (no son pasos).
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

        [Header("Viento (correr / dash)")]
        [Tooltip("Vacío = el primero que haya en los hijos del jugador.")]
        [SerializeField] WindTrailVFX windTrails;
        [Tooltip("Color del jugador para las estelas: se multiplica por el color/tint del WindTrailVFX (blanco = sin cambios).")]
        [SerializeField] Color windTint = Color.white;

        [Header("Pasos (polvo + huellas)")]
        [Tooltip("Vacío = el primero que haya en los hijos del jugador.")]
        [SerializeField] FootstepVFX footsteps;

        [Header("Golpes (vacíos = los primeros que haya en los hijos del jugador)")]
        [SerializeField] ImpactVFX impact;
        [SerializeField] SwingVFX swing;
        [SerializeField] DizzyStarsVFX dizzyStars;
        [SerializeField] KnockbackDustVFX knockbackDust;

        [Header("Cansancio")]
        [Tooltip("Vacío = el primero que haya en los hijos del jugador.")]
        [SerializeField] TiredVFX tired;
        [Tooltip("En los demás clientes (no saben si está agotado): se cansa con la estamina por debajo de esto...")]
        [SerializeField, Range(0f, 1f)] float tiredBelow = 0.02f;
        [Tooltip("...y se recupera al volver a esto (igual que Exhausted Recover Threshold del PlayerController).")]
        [SerializeField, Range(0f, 1f)] float tiredRecoverAt = 0.3f;

        [Header("Parpadeo al recibir golpes")]
        [Tooltip("Mientras está aturdido / derribado (sin control).")]
        [SerializeField] float blinkInterval = 0.08f;
        [Tooltip("Durante los i-frames al levantarse (ya puede moverse, pero no le pueden golpear).")]
        [SerializeField] float recoveryBlinkInterval = 0.16f;

        /// <summary>Color del jugador (el HUD lo usa para teñir sus piezas).</summary>
        public Color WindTint => windTint;

        readonly Dictionary<string, VfxEntry> lookup = new();
        readonly List<Renderer> blinkRenderers = new();
        Coroutine blinkRoutine;
        bool remoteTired;

        void Reset() => controller = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            // Solo valen los de ESTE jugador: una referencia a otro prefab (p. ej. arrastrada desde el
            // mapache al duplicarlo) apuntaría al asset, no a la instancia.
            if (controller != null)
            {
                FindOwn(ref windTrails);
                FindOwn(ref footsteps);
                FindOwn(ref impact);
                FindOwn(ref swing);
                FindOwn(ref dizzyStars);
                FindOwn(ref knockbackDust);
                FindOwn(ref tired);
            }
            if (windTrails != null) windTrails.SetPlayerTint(windTint);
            if (impact != null) impact.PlayerColor = windTint;
            foreach (VfxEntry entry in effects)
                if (!string.IsNullOrEmpty(entry.id)) lookup[entry.id] = entry;
        }

        void FindOwn<T>(ref T component) where T : Component
        {
            if (component == null || !component.transform.IsChildOf(controller.transform))
                component = controller.GetComponentInChildren<T>(true);
        }

        // Cambiar windTint en el Inspector durante Play se ve al momento.
        void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (windTrails != null) windTrails.SetPlayerTint(windTint);
            if (impact != null) impact.PlayerColor = windTint;
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.onLightHit.AddListener(OnLightHit);
            controller.onHeavyHit.AddListener(OnHeavyHit);
            controller.onDash.AddListener(OnDash);
            controller.StateChanged += OnStateChanged;
            controller.ActionStarted += OnActionStarted;
            UpdateStateVFX(controller.State);
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.onLightHit.RemoveListener(OnLightHit);
                controller.onHeavyHit.RemoveListener(OnHeavyHit);
                controller.onDash.RemoveListener(OnDash);
                controller.StateChanged -= OnStateChanged;
                controller.ActionStarted -= OnActionStarted;
            }
            StopBlink();
            if (tired != null) tired.Tired = false;
        }

        void Update()
        {
            if (tired != null && controller != null) tired.Tired = IsTired();
        }

        // Agotado (estamina a 0 hasta recuperar un poco). Solo el dueño lo sabe seguro; en los demás
        // clientes se deduce de la estamina replicada con la misma histéresis.
        bool IsTired()
        {
            if (!controller.IsSpawned) return false;
            if (controller.IsOwner) return controller.IsExhausted;
            float stamina = controller.StaminaNormalized;
            if (stamina <= tiredBelow) remoteTired = true;
            else if (stamina >= tiredRecoverAt) remoteTired = false;
            return remoteTired;
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
            if (footsteps != null) footsteps.PlayDash(direction, controller.DashDuration);

            // Prefab extra opcional. Sin configurar: no avisar en cada dash.
            if (lookup.ContainsKey(dashDustId))
            {
                foreach (float delay in dashDustDelays)
                {
                    if (delay <= 0f) Spawn(dashDustId, direction);
                    else StartCoroutine(SpawnDelayed(dashDustId, direction, delay));
                }
            }
        }

        IEnumerator SpawnDelayed(string id, Vector3 direction, float delay)
        {
            yield return new WaitForSeconds(delay);
            Spawn(id, direction);
        }

        // ---------------- Según el estado (viento, pasos) ----------------

        void OnStateChanged(PlayerState previous, PlayerState current)
        {
            UpdateStateVFX(current);
            // Las estrellas de mareo se van al levantarse.
            if (previous == PlayerState.Knockdown && current != PlayerState.Knockdown && dizzyStars != null) dizzyStars.Stop();
        }

        void OnActionStarted(PlayerState action)
        {
            if (action == PlayerState.Punch && swing != null) swing.Play();
        }

        void UpdateStateVFX(PlayerState state)
        {
            if (windTrails != null)
            {
                windTrails.SetMode(state switch
                {
                    PlayerState.Dash => WindTrailVFX.WindMode.Dash,
                    PlayerState.Run => WindTrailVFX.WindMode.Run,
                    _ => WindTrailVFX.WindMode.Off,
                });
            }

            // En el dash y al recibir golpes se desliza, no camina.
            if (footsteps != null)
                footsteps.StepsEnabled = state != PlayerState.Dash && !state.IsStun();
        }

        // ---------------- Golpes recibidos ----------------

        /// <summary>Suscrito a PlayerController.onLightHit (golpes 1º y 2º del combo).</summary>
        public void OnLightHit(PlayerController attacker, int hitNumber)
        {
            PlayHitVFX(attacker, false, controller.LightHit.stunDuration);
            StartBlink(controller.LightHit, HitFreeze(false));
        }

        /// <summary>Suscrito a PlayerController.onHeavyHit (golpe 3º: derribo).</summary>
        public void OnHeavyHit(PlayerController attacker)
        {
            PlayHitVFX(attacker, true, controller.HeavyHit.stunDuration);
            StartBlink(controller.HeavyHit, HitFreeze(true));
            if (dizzyStars != null) dizzyStars.Play();
        }

        void PlayHitVFX(PlayerController attacker, bool heavy, float stunDuration)
        {
            // Hacia donde sale despedido: lejos de quien golpea (sin atacante, hacia atrás: tras el golpe mira
            // hacia quien le ha dado).
            Vector3 away = attacker != null ? controller.transform.position - attacker.transform.position : -controller.transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -controller.transform.forward;
            away.Normalize();

            if (impact != null)
            {
                // Impact frames: también se congela quien golpea, y su color vale para el destello.
                Animator attackerAnimator = attacker != null ? attacker.GetComponentInChildren<Animator>() : null;
                PlayerVFX attackerVfx = attacker != null ? attacker.GetComponentInChildren<PlayerVFX>() : null;
                impact.PlayTowards(-away, heavy, attackerAnimator, attackerVfx != null ? attackerVfx.WindTint : windTint);
            }
            if (knockbackDust != null) knockbackDust.Play(away, heavy, stunDuration);
        }

        // Pausa de los impact frames: el parpadeo empieza al acabar (si no, taparía el destello).
        float HitFreeze(bool heavy) => impact != null ? impact.FreezeTime(heavy) : 0f;

        // ---------------- Parpadeo ----------------

        // Un golpe nuevo reinicia el parpadeo.
        void StartBlink(PlayerController.HitReaction reaction, float delay)
        {
            StopBlink();

            // Se recogen al empezar: así entran también los objetos equipados que se hayan añadido.
            // Los efectos (estrellas, impactos...) no: se encienden y apagan solos.
            foreach (Renderer rend in controller.GetComponentsInChildren<Renderer>())
                if ((rend is MeshRenderer || rend is SkinnedMeshRenderer) && rend.enabled && rend.GetComponentInParent<VfxBehaviour>() == null)
                    blinkRenderers.Add(rend);

            blinkRoutine = StartCoroutine(Blink(reaction.stunDuration, reaction.recoveryInvulnerability, delay));
        }

        IEnumerator Blink(float stunDuration, float recoveryDuration, float delay)
        {
            bool visible = true;

            // Sin control: parpadeo rápido. Dura lo mismo que el stun (contando la pausa del golpe).
            float end = Time.time + stunDuration;
            if (delay > 0f) yield return new WaitForSeconds(delay);
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
