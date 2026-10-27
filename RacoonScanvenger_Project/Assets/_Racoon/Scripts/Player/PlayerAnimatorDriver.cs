using UnityEngine;

namespace Racoon.Player
{
    /// <summary>
    /// Traduce el PlayerState replicado a parámetros del Animator. Se ejecuta en TODOS los clientes,
    /// así que no hace falta NetworkAnimator.
    ///
    /// Parámetros esperados en el Animator Controller (ACO_Racoon):
    ///  - "State" (int): valor de PlayerState (0 Idle, 1 Walk, 2 Run, 10 Interact, 11 SwitchItem, 12 Punch,
    ///    13 UseItem, 14 HitStun (golpe ligero), 15 Dash, 16 Knockdown (golpe fuerte), 17 Downed (aturdido por objeto))
    ///  - "ActionStart" (trigger): acciones de tren superior (Interact, Punch, UseItem), se pueden hacer andando
    ///  - "BodyAction" (trigger): acciones de cuerpo entero (Dash, HitStun, Knockdown, Downed)
    ///  - "Speed" (float): velocidad horizontal normalizada 0-1 (1 = correr)
    ///  - "SackSize" (float): llenado de la bolsa 0-1; elige la variante de la cola (pequeño / normal / grande)
    /// Si falta algún parámetro, simplemente se ignora.
    ///
    /// También ajusta los blend shapes del saco: vacío = Saco_Pequeno, mitad = forma base, lleno = Saco_Grande.
    /// </summary>
    public class PlayerAnimatorDriver : MonoBehaviour
    {
        [SerializeField] PlayerController controller;
        [SerializeField] Animator animator;
        [Tooltip("Origen del peso de la bolsa. Si está vacío se busca en el jugador.")]
        [SerializeField] PlayerLoot loot;
        [SerializeField] string stateParameter = "State";
        [SerializeField] string actionTriggerParameter = "ActionStart";
        [SerializeField] string bodyActionTriggerParameter = "BodyAction";
        [SerializeField] string speedParameter = "Speed";
        [SerializeField] string sackSizeParameter = "SackSize";
        [Tooltip("Velocidad (m/s) que equivale a Speed = 1. Pon aquí la velocidad de correr.")]
        [SerializeField] float fullSpeed = 6.5f;
        [SerializeField] float speedDampTime = 0.1f;

        [Header("Capa de tren superior")]
        [Tooltip("Capa con las acciones de brazos (golpe, lanzar, coger). Solo pesa mientras hay una acción; " +
                 "si pesara siempre, su estado vacío congelaría el torso al andar.")]
        [SerializeField] string upperBodyLayer = "UpperBody";
        [SerializeField] string upperBodyIdleState = "Empty";
        [Tooltip("Segundos que tarda la capa en entrar o salir.")]
        [SerializeField] float upperBodyFadeTime = 0.1f;

        [Header("Saco")]
        [SerializeField] string smallSackBlendShape = "Saco_Pequeno";
        [SerializeField] string bigSackBlendShape = "Saco_Grande";
        [Tooltip("Segundos que tarda el saco (y la cola) en adaptarse al nuevo peso.")]
        [SerializeField] float sackSmoothTime = 0.25f;
        [Tooltip("Cuánto peso de más (sobre el BagFullWeight de PlayerLoot) hace falta para que el saco se vea lleno del todo. " +
                 "Solo es visual: la velocidad sigue usando el peso normal.")]
        [SerializeField, Min(1f)] float sackFullWeightScale = 1.6f;
        [Tooltip("Curva del llenado: >1 hace que el saco crezca despacio al principio y llegue al tope más tarde.")]
        [SerializeField, Min(0.1f)] float sackFillExponent = 1.3f;

        int stateHash, actionHash, bodyActionHash, speedHash, sackHash;
        bool hasState, hasAction, hasBodyAction, hasSpeed, hasSack;
        Vector3 lastPosition;

        int upperLayerIndex = -1, upperIdleHash;
        float upperWeight;

        SkinnedMeshRenderer sackRenderer;
        int smallSackIndex = -1, bigSackIndex = -1;
        float sackSize, sackVelocity;

        void Reset()
        {
            controller = GetComponentInParent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
            loot = GetComponentInParent<PlayerLoot>();
        }

        void Awake()
        {
            stateHash = Animator.StringToHash(stateParameter);
            actionHash = Animator.StringToHash(actionTriggerParameter);
            bodyActionHash = Animator.StringToHash(bodyActionTriggerParameter);
            speedHash = Animator.StringToHash(speedParameter);
            sackHash = Animator.StringToHash(sackSizeParameter);
            if (loot == null) loot = controller.GetComponent<PlayerLoot>();

            if (animator == null || animator.runtimeAnimatorController == null) return;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == stateHash) hasState = true;
                else if (parameter.nameHash == actionHash) hasAction = true;
                else if (parameter.nameHash == bodyActionHash) hasBodyAction = true;
                else if (parameter.nameHash == speedHash) hasSpeed = true;
                else if (parameter.nameHash == sackHash) hasSack = true;
            }

            upperLayerIndex = animator.GetLayerIndex(upperBodyLayer);
            upperIdleHash = Animator.StringToHash(upperBodyIdleState);
            if (upperLayerIndex > 0) animator.SetLayerWeight(upperLayerIndex, 0f);

            foreach (SkinnedMeshRenderer skinned in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null) continue;
                int small = skinned.sharedMesh.GetBlendShapeIndex(smallSackBlendShape);
                int big = skinned.sharedMesh.GetBlendShapeIndex(bigSackBlendShape);
                if (small < 0 && big < 0) continue;
                sackRenderer = skinned;
                smallSackIndex = small;
                bigSackIndex = big;
                break;
            }
        }

        void OnEnable()
        {
            lastPosition = controller.transform.position;
            sackSize = TargetSackSize;
            controller.ActionStarted += OnActionStarted;
        }

        void OnDisable() => controller.ActionStarted -= OnActionStarted;

        float TargetSackSize => loot != null
            ? Mathf.Pow(Mathf.Clamp01(loot.CarriedWeight / (loot.BagFullWeight * sackFullWeightScale)), sackFillExponent)
            : 0f;

        void OnActionStarted(PlayerState action)
        {
            if (animator == null) return;
            // El estado tiene que estar puesto antes que el trigger: las transiciones comprueban los dos a la vez.
            if (hasState) animator.SetInteger(stateHash, (int)action);

            switch (action)
            {
                case PlayerState.Interact:
                case PlayerState.Punch:
                case PlayerState.UseItem:
                    if (hasAction) animator.SetTrigger(actionHash);
                    break;
                case PlayerState.Dash:
                case PlayerState.HitStun:
                case PlayerState.Knockdown:
                case PlayerState.Downed:
                    if (hasBodyAction) animator.SetTrigger(bodyActionHash);
                    break;
            }
        }

        void Update()
        {
            if (animator == null) return;
            if (hasState) animator.SetInteger(stateHash, (int)controller.State);

            // La velocidad se calcula por desplazamiento, así funciona igual en el dueño y en el rival.
            Vector3 position = controller.transform.position;
            Vector3 delta = position - lastPosition;
            lastPosition = position;
            delta.y = 0f;

            if (hasSpeed && Time.deltaTime > 0f)
                animator.SetFloat(speedHash, delta.magnitude / Time.deltaTime / fullSpeed, speedDampTime, Time.deltaTime);

            sackSize = Mathf.SmoothDamp(sackSize, TargetSackSize, ref sackVelocity, sackSmoothTime);
            if (hasSack) animator.SetFloat(sackHash, sackSize);
            UpdateUpperBodyWeight();
        }

        // Los clips del FBX traen curvas de blend shapes a 0 y el Animator las escribe después de Update:
        // el saco se ajusta aquí, una vez aplicada la animación.
        void LateUpdate() => UpdateSackBlendShapes();

        void UpdateUpperBodyWeight()
        {
            if (upperLayerIndex <= 0) return;
            // Pesa mientras la capa está en una acción o entrando en una; al volver al estado vacío se apaga.
            bool inAction = animator.IsInTransition(upperLayerIndex)
                ? animator.GetNextAnimatorStateInfo(upperLayerIndex).shortNameHash != upperIdleHash
                : animator.GetCurrentAnimatorStateInfo(upperLayerIndex).shortNameHash != upperIdleHash;
            float step = upperBodyFadeTime > 0f ? Time.deltaTime / upperBodyFadeTime : 1f;
            upperWeight = Mathf.MoveTowards(upperWeight, inAction ? 1f : 0f, step);
            animator.SetLayerWeight(upperLayerIndex, upperWeight);
        }

        void UpdateSackBlendShapes()
        {
            if (sackRenderer == null) return;
            // 0 → saco pequeño, 0.5 → forma modelada, 1 → saco grande.
            if (smallSackIndex >= 0)
                sackRenderer.SetBlendShapeWeight(smallSackIndex, Mathf.Clamp01(1f - sackSize * 2f) * 100f);
            if (bigSackIndex >= 0)
                sackRenderer.SetBlendShapeWeight(bigSackIndex, Mathf.Clamp01(sackSize * 2f - 1f) * 100f);
        }
    }
}
