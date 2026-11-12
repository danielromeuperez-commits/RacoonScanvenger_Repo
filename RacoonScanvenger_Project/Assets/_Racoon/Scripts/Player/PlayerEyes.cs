using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Ojos del personaje (mapache / panda rojo). Va en el GameObject "Eyes" del prefab, hijo del hueso de la
    /// cabeza, con dos hijos (Eye_L y Eye_R) que llevan un SpriteRenderer cada uno. Este script NO crea nada:
    /// solo cambia el sprite de cada ojo entre abierto y cerrado. Se ejecuta en TODOS los clientes (usa
    /// StateChanged / ActionStarted, que el PlayerController lanza en todos).
    ///
    ///  - Parpadeo: cada pocos segundos, a veces doble.
    ///  - Ojos cerrados mientras está aturdido (golpe ligero, derribo o aturdido por objeto) y un instante
    ///    más al recuperarse.
    ///  - Cierre rápido al dar un puñetazo y al chocar contra una pared (rebote; solo lo ve el dueño).
    ///
    /// La posición y la forma de cada ojo se ajustan moviendo Eye_L / Eye_R en el prefab.
    /// </summary>
    public class PlayerEyes : MonoBehaviour
    {
        [SerializeField] PlayerController controller;

        [Header("Ojos (SpriteRenderer de Eye_L y Eye_R)")]
        [SerializeField] SpriteRenderer leftEye;
        [SerializeField] SpriteRenderer rightEye;

        [Header("Sprites (de los atlas Open / Close)")]
        [SerializeField] Sprite leftOpen;
        [SerializeField] Sprite rightOpen;
        [SerializeField] Sprite leftClosed;
        [SerializeField] Sprite rightClosed;

        [Header("Parpadeo")]
        [SerializeField] Vector2 blinkInterval = new(2.2f, 5f);
        [SerializeField] float blinkDuration = 0.11f;
        [Range(0f, 1f)] [SerializeField] float doubleBlinkChance = 0.2f;

        [Header("Reacciones")]
        [Tooltip("Ojos cerrados un rato más al salir del aturdimiento.")]
        [SerializeField] float stunExtraClosed = 0.2f;
        [SerializeField] float punchClosedTime = 0.22f;
        [SerializeField] float bumpClosedTime = 0.45f;

        bool open = true;
        bool stunned;
        float closedUntil;      // cerrados por una reacción hasta este momento
        float nextBlinkTime;
        float blinkEndTime;
        int blinksPending;

        void Reset() => controller = GetComponentInParent<PlayerController>();

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<PlayerController>();
            ScheduleBlink();
            Apply(true, force: true);
        }

        void OnEnable()
        {
            if (controller == null) return;
            controller.StateChanged += OnStateChanged;
            controller.ActionStarted += OnActionStarted;
            controller.Bumped += OnBumped;
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.StateChanged -= OnStateChanged;
                controller.ActionStarted -= OnActionStarted;
                controller.Bumped -= OnBumped;
            }
            stunned = false;
            closedUntil = 0f;
        }

        // ---------------- Reacciones ----------------

        void OnStateChanged(PlayerState previous, PlayerState current)
        {
            if (current.IsStun())
            {
                stunned = true;
            }
            else if (stunned)
            {
                stunned = false;
                CloseFor(stunExtraClosed);
            }
        }

        void OnActionStarted(PlayerState action)
        {
            if (action == PlayerState.Punch) CloseFor(punchClosedTime);
        }

        void OnBumped() => CloseFor(bumpClosedTime);

        void CloseFor(float seconds) => closedUntil = Mathf.Max(closedUntil, Time.time + seconds);

        // ---------------- Parpadeo ----------------

        void ScheduleBlink(float from = -1f)
        {
            if (from < 0f) from = Time.time;
            nextBlinkTime = from + Random.Range(blinkInterval.x, blinkInterval.y);
            blinksPending = Random.value < doubleBlinkChance ? 2 : 1;
        }

        void Update()
        {
            bool forcedClosed = stunned || Time.time < closedUntil;
            bool shouldBeOpen = !forcedClosed;

            if (forcedClosed)
            {
                // Una reacción corta el parpadeo en curso.
                blinkEndTime = 0f;
            }
            else
            {
                if (blinksPending > 0 && Time.time >= nextBlinkTime)
                {
                    blinkEndTime = Time.time + blinkDuration;
                    blinksPending--;
                    // En el parpadeo doble, el segundo llega tras un hueco abierto igual de corto.
                    if (blinksPending > 0) nextBlinkTime = blinkEndTime + blinkDuration;
                    else ScheduleBlink(blinkEndTime);
                }
                if (Time.time < blinkEndTime) shouldBeOpen = false;
            }

            Apply(shouldBeOpen);
        }

        void Apply(bool shouldBeOpen, bool force = false)
        {
            if (!force && shouldBeOpen == open) return;
            open = shouldBeOpen;
            if (leftEye != null) leftEye.sprite = open ? leftOpen : leftClosed;
            if (rightEye != null) rightEye.sprite = open ? rightOpen : rightClosed;
        }
    }
}
