using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;



public class MenuButtonHover : MonoBehaviour,

    IPointerEnterHandler,
    IPointerExitHandler,
    ISelectHandler,
    IDeselectHandler,
    IPointerDownHandler,
    IPointerUpHandler
{

    [Header("Hover")]
    [SerializeField] float hoverScale = 1.08f;
    [SerializeField] float hoverMoveY = 6f;
    [SerializeField] float hoverRotation = -2f;

    [Header("Click")]
    [SerializeField] float clickScale = 0.96f;

    [Header("Velocidad")]
    [SerializeField] float animationSpeed = 12f;

    RectTransform rectTransform;

    Vector3 originalScale;
    Vector2 originalPosition;
    Quaternion originalRotation;

    Coroutine animationCoroutine;

    bool pointerInside;
    bool selected;
    bool pressed;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        originalScale = rectTransform.localScale;
        originalPosition = rectTransform.anchoredPosition;
        originalRotation = rectTransform.localRotation;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        RefreshAnimation();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        pressed = false;

        RefreshAnimation();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        RefreshAnimation();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        RefreshAnimation();
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        RefreshAnimation();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        pressed = false;

        RefreshAnimation();
    }

    void RefreshAnimation()
    {
        if (animationCoroutine != null)
            StopCoroutine(animationCoroutine);

        bool highlighted =
            pointerInside ||
            selected;

        Vector3 targetScale;
        Vector2 targetPosition;
        Quaternion targetRotation;

        if (pressed)
        {
            targetScale =
                originalScale * clickScale;

            targetPosition =
                originalPosition;

            targetRotation =
                originalRotation;

        }
        else if (highlighted)
        {
            targetScale =
               originalScale * hoverScale;

            targetPosition =
                originalPosition +
                Vector2.up * hoverMoveY;

            targetRotation =
                originalRotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    hoverRotation
                    );
        }
        else
        {
            targetScale =
                originalScale;

            targetPosition =
                originalPosition;

            targetRotation =
                originalRotation;
        }

        animationCoroutine =
            StartCoroutine(
                AnimateTo(
                    targetScale,
                    targetPosition,
                    targetRotation
                    )
                );
    }

    IEnumerator AnimateTo(
        Vector3 targetScale,
        Vector2 targetPosition,
        Quaternion targetRotation)
    {
        while (
            Vector3.Distance(
                rectTransform.localScale,
                targetScale
                ) > 0.001f ||
            Vector2.Distance(
                rectTransform.anchoredPosition,
                targetPosition
                ) > 0.01f ||
            Quaternion.Angle(
                rectTransform.localRotation,
                targetRotation
                ) > 0.1f)
        {
            float t =
                Time.unscaledDeltaTime *
                animationSpeed;

            rectTransform.localScale =
                Vector3.Lerp(
                    rectTransform.localScale,
                    targetScale,
                    t
                    );

            rectTransform.anchoredPosition =
                Vector2.Lerp(
                    rectTransform.anchoredPosition,
                    targetPosition,
                    t
                    );

            rectTransform.localRotation =
                Quaternion.Lerp(
                    rectTransform.localRotation,
                    targetRotation,
                    t
                    );

            yield return null;
        }

        rectTransform.localScale =
            targetScale;

        rectTransform.anchoredPosition =
            targetPosition;

        rectTransform.localRotation =
            targetRotation;

        animationCoroutine = null;
    }

    void OnDisable()
    {
        if (rectTransform == null)
            return;

        rectTransform.localScale =
            originalScale;

        rectTransform.anchoredPosition =
            originalPosition;

        rectTransform.localRotation =
            originalRotation;

        pointerInside = false;
        selected = false;
        pressed = false;
    }
}