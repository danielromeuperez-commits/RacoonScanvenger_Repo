using UnityEngine;

namespace Racoon.UI
{
    /// <summary>
    /// Latido suave (escala y transparencia) para indicadores de espera. Usa tiempo sin escalar.
    /// </summary>
    public class UIPulse : MonoBehaviour
    {
        [SerializeField] float speed = 3f;
        [SerializeField] float scaleAmount = 0.12f;
        [SerializeField, Range(0f, 1f)] float minAlpha = 0.35f;

        CanvasGroup group;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
        }

        void OnDisable() => transform.localScale = Vector3.one;

        void Update()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);
            transform.localScale = Vector3.one * (1f + scaleAmount * wave);
            group.alpha = Mathf.Lerp(minAlpha, 1f, wave);
        }
    }
}
