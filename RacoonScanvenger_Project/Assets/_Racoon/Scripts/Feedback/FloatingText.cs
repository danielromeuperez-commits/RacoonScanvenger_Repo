using TMPro;
using UnityEngine;

namespace Racoon.Feedback
{
    /// <summary>
    /// Texto que sale en el mundo, mira a la cámara, hace un pop, sube y se desvanece («+10», «¡PLAF!»...).
    /// Es un TextMeshPro 3D (sin Canvas), así no choca con la regla de canvas en Screen Space - Camera.
    /// Puramente visual y local: cada cliente crea el suyo cuando le llega el RPC correspondiente.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        const float PopTime = 0.18f;

        TextMeshPro text;
        Vector3 start;
        Vector3 drift;
        float rise;
        float lifeTime;
        float age;
        float baseScale;
        Color color;
        Transform cameraTransform;

        /// <summary>
        /// Crea un texto flotante. <paramref name="size"/> en unidades del mundo (alto aproximado de la letra).
        /// <paramref name="drift"/>: desplazamiento horizontal extra durante la vida (p. ej. hacia el jugador).
        /// </summary>
        public static FloatingText Spawn(string value, Vector3 position, Color color, float size = 0.6f,
            TMP_FontAsset font = null, float lifeTime = 0.9f, float rise = 1.2f, Vector3 drift = default)
        {
            var go = new GameObject("FloatingText " + value);
            go.transform.position = position;

            var text = go.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.text = value;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontSize = 10f;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.outlineWidth = 0.25f;
            text.outlineColor = new Color32(30, 20, 40, 255);
            text.rectTransform.sizeDelta = new Vector2(10f, 2f);

            var floating = go.AddComponent<FloatingText>();
            floating.text = text;
            floating.start = position;
            floating.color = color;
            floating.lifeTime = Mathf.Max(0.1f, lifeTime);
            floating.rise = rise;
            floating.drift = drift;
            // fontSize 10 ≈ 1 unidad de alto.
            floating.baseScale = size;
            floating.Tick(0f);
            return floating;
        }

        void Update() => Tick(Time.deltaTime);

        void Tick(float dt)
        {
            age += dt;
            float t = Mathf.Clamp01(age / lifeTime);

            // Pop: crece de golpe un poco por encima de su tamaño y se asienta.
            float pop = age < PopTime
                ? Mathf.Sin(age / PopTime * Mathf.PI * 0.5f) * 1.3f
                : Mathf.Lerp(1.3f, 1f, Mathf.Clamp01((age - PopTime) / 0.12f));
            transform.localScale = Vector3.one * (baseScale * pop);

            // Sube rápido al principio y frena (ease-out).
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = start + Vector3.up * (rise * eased) + drift * eased;

            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;

            // Se desvanece en el último 40%.
            Color c = color;
            c.a *= t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            text.color = c;

            if (age >= lifeTime) Destroy(gameObject);
        }
    }
}
