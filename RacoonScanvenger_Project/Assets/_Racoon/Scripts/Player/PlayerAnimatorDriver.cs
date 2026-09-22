using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Traduce el PlayerState replicado a parámetros del Animator. Se ejecuta en TODOS los clientes,
    /// así que no hace falta NetworkAnimator.
    ///
    /// Parámetros esperados en el Animator Controller:
    ///  - "State" (int): valor de PlayerState (0 Idle, 1 Walk, 2 Run, 10 Interact, 11 SwitchItem, 12 Punch, 13 UseItem)
    ///  - "ActionStart" (trigger): se dispara al empezar cualquier acción
    ///  - "Speed" (float, opcional): velocidad horizontal normalizada 0-1 para blend trees
    /// Si falta algún parámetro, simplemente se ignora.
    /// </summary>
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] PlayerController controller;
        [SerializeField] Animator animator;
        [SerializeField] string stateParameter = "State";
        [SerializeField] string actionTriggerParameter = "ActionStart";
        [SerializeField] string speedParameter = "Speed";
        [Tooltip("Velocidad (m/s) que equivale a Speed = 1. Pon aquí la velocidad de correr.")]
        [SerializeField] float fullSpeed = 6.5f;
        [SerializeField] float speedDampTime = 0.1f;

        int stateHash, actionHash, speedHash;
        bool hasState, hasAction, hasSpeed;
        Vector3 lastPosition;

        void Reset()
        {
            controller = GetComponentInParent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            stateHash = Animator.StringToHash(stateParameter);
            actionHash = Animator.StringToHash(actionTriggerParameter);
            speedHash = Animator.StringToHash(speedParameter);

            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == stateHash) hasState = true;
                else if (parameter.nameHash == actionHash) hasAction = true;
                else if (parameter.nameHash == speedHash) hasSpeed = true;
            }
        }

        void OnEnable()
        {
            lastPosition = controller.transform.position;
            controller.ActionStarted += OnActionStarted;
        }

        void OnDisable() => controller.ActionStarted -= OnActionStarted;

        void OnActionStarted(PlayerState action)
        {
            if (hasAction) animator.SetTrigger(actionHash);
        }

        void Update()
        {
            if (hasState) animator.SetInteger(stateHash, (int)controller.State);

            // La velocidad se calcula por desplazamiento, así funciona igual en el dueño y en el rival.
            Vector3 position = controller.transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;
            delta.y = 0f;

            if (hasSpeed && Time.deltaTime > 0f)
                animator.SetFloat(speedHash, delta.magnitude / Time.deltaTime / fullSpeed, speedDampTime, Time.deltaTime);
        }
    }
}
