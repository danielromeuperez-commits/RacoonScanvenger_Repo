using Racoon.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI
{
    /// <summary>
    /// Barra de estamina. Dos usos:
    ///  - HUD en pantalla: deja 'target' vacío y activa 'bindToLocalPlayer'.
    ///  - Barra sobre la cabeza (Canvas World Space dentro del prefab): asigna 'target' y activa 'faceCamera'.
    /// La estamina es una NetworkVariable, así que también puedes ver la del rival.
    /// </summary>
    public class StaminaBarUI : MonoBehaviour
    {
        [SerializeField] PlayerController target;
        [SerializeField] bool bindToLocalPlayer = true;

        [Header("Visual")]
        [Tooltip("Image con Image Type = Filled.")]
        [SerializeField] Image fill;
        [SerializeField] Color fullColor = new(0.35f, 0.85f, 0.35f);
        [SerializeField] Color emptyColor = new(0.9f, 0.3f, 0.2f);
        [Tooltip("Color mientras está agotado (solo visible en tu propia barra).")]
        [SerializeField] Color exhaustedColor = new(0.5f, 0.5f, 0.5f);
        [SerializeField] float fillSmoothing = 12f;

        [Header("Ocultar cuando está llena (opcional)")]
        [SerializeField] CanvasGroup canvasGroup;
        [SerializeField] bool hideWhenFull = false;
        [SerializeField] float fadeSpeed = 4f;

        [Header("World Space")]
        [SerializeField] bool faceCamera = false;

        float displayedValue = 1f;

        void OnEnable()
        {
            if (target == null && bindToLocalPlayer)
            {
                if (PlayerController.Local != null) target = PlayerController.Local;
                else PlayerController.LocalPlayerSpawned += OnLocalPlayerSpawned;
            }
        }

        void OnDisable() => PlayerController.LocalPlayerSpawned -= OnLocalPlayerSpawned;

        void OnLocalPlayerSpawned(PlayerController player)
        {
            target = player;
            PlayerController.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        }

        void Update()
        {
            float value = target != null && target.IsSpawned ? target.StaminaNormalized : 1f;
            displayedValue = Mathf.Lerp(displayedValue, value, 1f - Mathf.Exp(-fillSmoothing * Time.deltaTime));

            if (fill != null)
            {
                fill.fillAmount = displayedValue;
                bool exhausted = target != null && target.IsOwner && target.IsExhausted;
                fill.color = exhausted ? exhaustedColor : Color.Lerp(emptyColor, fullColor, displayedValue);
            }

            if (canvasGroup != null)
            {
                float targetAlpha = target == null || (hideWhenFull && value >= 0.999f) ? 0f : 1f;
                canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);
            }
        }

        void LateUpdate()
        {
            if (faceCamera && Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;
        }
    }
}
