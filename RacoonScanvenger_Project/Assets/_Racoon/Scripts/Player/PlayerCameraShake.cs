using Racoon.Cameras;
using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Temblores de cámara del jugador, cada interacción con el suyo. SOLO en la máquina del jugador al que le
    /// pasa (la cámara es local): los eventos llegan a todos los clientes, pero aquí se filtra por IsOwner.
    ///  - Recibir golpe ligero / fuerte: empujón hacia donde sales despedido + vibración (el 2º del combo más
    ///    fuerte que el 1º; el derribo, un golpe gordo con alabeo).
    ///  - Dar un golpe que conecta (hit confirm): un toque seco hacia la víctima, más fuerte si derriba.
    ///  - Golpe al aire, dash, usar objeto, saquear, cambiar de objeto, levantarse del derribo.
    ///  - Aturdido por un objeto (sin golpe): mareo lento.
    ///  - Correr y estar agotado: temblores continuos (balanceo de carrera / respiración) que entran y salen suaves.
    /// Se suscribe solo por código a los eventos del PlayerController (no conectarlos en el Inspector).
    /// </summary>
    public class PlayerCameraShake : MonoBehaviour
    {
        [SerializeField] PlayerController controller;
        [Tooltip("Multiplica todos los temblores de este jugador.")]
        [SerializeField, Range(0f, 2f)] float intensity = 1f;

        [Header("Recibir golpes")]
        [SerializeField] CameraShakeProfile lightHit = new(0.28f, new Vector3(2.2f, 1.2f, 3f), new Vector3(0.06f, 0.05f, 0f), 26f, kick: 0.18f, kickRoll: 3f, decayPower: 2.2f);
        [Tooltip("Cuánto más fuerte es cada golpe ligero seguido del combo (2º = 1 + esto).")]
        [SerializeField] float comboStep = 0.35f;
        [SerializeField] CameraShakeProfile heavyHit = new(0.65f, new Vector3(4f, 2.5f, 6f), new Vector3(0.14f, 0.12f, 0.05f), 20f, kick: 0.35f, kickRoll: 7f, decayPower: 1.6f);
        [Tooltip("Aturdido por un objeto (sin golpe): mareo lento.")]
        [SerializeField] CameraShakeProfile itemStun = new(0.9f, new Vector3(1.5f, 2.5f, 4f), new Vector3(0.03f, 0.02f, 0f), 5f, decayPower: 1.3f, attack: 0.1f);
        [Tooltip("Al levantarse del derribo.")]
        [SerializeField] CameraShakeProfile getUp = new(0.22f, new Vector3(0.8f, 0.3f, 0.6f), Vector3.zero, 12f, kick: 0.05f, decayPower: 2f);

        [Header("Dar golpes")]
        [Tooltip("El golpe conecta (en ligero; en el derribo, x heavyConfirmMultiplier).")]
        [SerializeField] CameraShakeProfile hitConfirm = new(0.15f, new Vector3(1f, 0.6f, 1.2f), new Vector3(0.03f, 0.03f, 0f), 30f, kick: 0.06f, decayPower: 3f);
        [SerializeField] float heavyConfirmMultiplier = 1.8f;
        [Tooltip("Al lanzar el puñetazo (conecte o no).")]
        [SerializeField] CameraShakeProfile punchSwing = new(0.12f, new Vector3(0.4f, 0.3f, 0.6f), Vector3.zero, 22f, kick: 0.04f, decayPower: 3f);

        [Header("Movimiento")]
        [SerializeField] CameraShakeProfile dash = new(0.25f, new Vector3(0.6f, 0.3f, 0f), Vector3.zero, 14f, kick: 0.12f, kickRoll: 1.5f, decayPower: 2f);
        [Tooltip("Continuo mientras corre: balanceo suave de carrera.")]
        [SerializeField] CameraShakeProfile run = new(1f, new Vector3(0.35f, 0.25f, 0.5f), new Vector3(0.02f, 0.03f, 0f), 4.5f);
        [Tooltip("Continuo mientras está agotado: balanceo lento de respiración.")]
        [SerializeField] CameraShakeProfile tired = new(1f, new Vector3(0.7f, 0.2f, 0.45f), new Vector3(0f, 0.02f, 0f), 1.6f);

        [Header("Acciones")]
        [SerializeField] CameraShakeProfile useItem = new(0.18f, new Vector3(0.6f, 0.4f, 0.8f), Vector3.zero, 20f, kick: 0.05f, decayPower: 2.5f);
        [SerializeField] CameraShakeProfile interact = new(0.1f, new Vector3(0.3f, 0.2f, 0.3f), Vector3.zero, 16f, decayPower: 2.5f);
        [SerializeField] CameraShakeProfile switchItem = new(0.08f, new Vector3(0.2f, 0.3f, 0f), Vector3.zero, 14f, decayPower: 2f);

        readonly object runKey = new();
        readonly object tiredKey = new();
        float lastHitTime = float.NegativeInfinity;
        float stunCheckTime = -1f;

        bool IsLocal => controller != null && controller.IsSpawned && controller.IsOwner;

        void Reset() => controller = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.onLightHit.AddListener(OnLightHit);
            controller.onHeavyHit.AddListener(OnHeavyHit);
            controller.onDash.AddListener(OnDash);
            controller.StateChanged += OnStateChanged;
            controller.ActionStarted += OnActionStarted;
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
            CameraShaker.ClearContinuous(runKey);
            CameraShaker.ClearContinuous(tiredKey);
        }

        void Update()
        {
            if (!IsLocal)
            {
                CameraShaker.SetContinuous(runKey, run, 0f);
                CameraShaker.SetContinuous(tiredKey, tired, 0f);
                return;
            }

            CameraShaker.SetContinuous(runKey, run, controller.State == PlayerState.Run ? intensity : 0f, 4f);
            CameraShaker.SetContinuous(tiredKey, tired, controller.IsExhausted ? intensity : 0f, 1.5f);

            // HitStun sin golpe = aturdido por un objeto (el golpe llega justo después del cambio de estado).
            if (stunCheckTime >= 0f && Time.time - stunCheckTime > 0.12f)
            {
                if (lastHitTime < stunCheckTime - 0.15f) Play(itemStun);
                stunCheckTime = -1f;
            }
        }

        void Play(CameraShakeProfile profile, float strength = 1f, Vector3 direction = default) =>
            CameraShaker.Play(profile, strength * intensity, direction);

        // ---------------- Golpes ----------------

        void OnLightHit(PlayerController attacker, int hitNumber)
        {
            lastHitTime = Time.time;
            Vector3 away = AwayFrom(attacker);
            if (IsLocal) Play(lightHit, 1f + comboStep * Mathf.Max(0, hitNumber - 1), away);
            ConfirmForAttacker(attacker, -away, 1f);
        }

        void OnHeavyHit(PlayerController attacker)
        {
            lastHitTime = Time.time;
            Vector3 away = AwayFrom(attacker);
            if (IsLocal) Play(heavyHit, 1f, away);
            ConfirmForAttacker(attacker, -away, heavyConfirmMultiplier);
        }

        // El que pega también lo nota, en su máquina (con sus propios ajustes si los tiene).
        static void ConfirmForAttacker(PlayerController attacker, Vector3 towardVictim, float strength)
        {
            if (attacker == null || !attacker.IsSpawned || !attacker.IsOwner) return;
            PlayerCameraShake shaker = attacker.GetComponentInChildren<PlayerCameraShake>();
            if (shaker != null) shaker.Play(shaker.hitConfirm, strength, towardVictim);
        }

        Vector3 AwayFrom(PlayerController attacker)
        {
            Vector3 away = attacker != null ? controller.transform.position - attacker.transform.position : -controller.transform.forward;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? away.normalized : -controller.transform.forward;
        }

        // ---------------- Movimiento y acciones ----------------

        void OnDash(Vector3 direction)
        {
            if (IsLocal) Play(dash, 1f, direction);
        }

        void OnStateChanged(PlayerState previous, PlayerState current)
        {
            if (!IsLocal) return;
            if (current == PlayerState.HitStun) stunCheckTime = Time.time;
            if (previous == PlayerState.Knockdown && current != PlayerState.Knockdown) Play(getUp, 1f, Vector3.up);
        }

        void OnActionStarted(PlayerState action)
        {
            if (!IsLocal) return;
            Vector3 forward = controller.transform.forward;
            switch (action)
            {
                case PlayerState.Punch: Play(punchSwing, 1f, forward); break;
                case PlayerState.UseItem: Play(useItem, 1f, forward); break;
                case PlayerState.Interact: Play(interact); break;
                case PlayerState.SwitchItem: Play(switchItem); break;
            }
        }
    }
}
