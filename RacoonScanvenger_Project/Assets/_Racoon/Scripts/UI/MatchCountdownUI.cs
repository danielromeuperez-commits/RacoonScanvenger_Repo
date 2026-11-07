using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class MatchCountdownUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_Text countdownText;

    [Header("Animación")]
    [SerializeField] float numberDuration = 0.75f;
    [SerializeField] float startDuration = 0.9f;

    [SerializeField] float initialScale = 0.15f;
    [SerializeField] float finalScale = 1.5f;

    [SerializeField] float numberRotation = 35f;

    RectTransform textRect;

    void Awake()
    {
        if (countdownText != null)
        {
            textRect = countdownText.rectTransform;

            countdownText.gameObject.SetActive(false);
        }
    }

    // ============================================================
    // CUENTA ATRÁS COMPLETA
    // ============================================================

    public IEnumerator PlayCountdown(Action onStart)
    {
        if (countdownText == null)
        {
            Debug.LogWarning(
                "MatchCountdownUI: no hay Countdown Text asignado."
            );

            onStart?.Invoke();
            yield break;
        }

        countdownText.gameObject.SetActive(true);

        // 3
        PersistentMusic.Instance?.PlayTickSound();
        yield return AnimateText(
            "3",
            numberDuration,
            -numberRotation,
            finalScale
        );

        // 2
        PersistentMusic.Instance?.PlayTickSound();
        yield return AnimateText(
            "2",
            numberDuration,
            numberRotation,
            finalScale
        );

        // 1
        PersistentMusic.Instance?.PlayTickSound();
        yield return AnimateText(
            "1",
            numberDuration,
            -numberRotation,
            finalScale
        );

        // ========================================================
        // AQUÍ EMPIEZA REALMENTE LA PARTIDA
        // ========================================================

        onStart?.Invoke();

        // START!!
        PersistentMusic.Instance?.PlayStartSound();
        yield return AnimateText(
            "START!!",
            startDuration,
            numberRotation * 0.5f,
            finalScale * 1.15f
        );

        countdownText.gameObject.SetActive(false);
    }

    // ============================================================
    // ANIMACIÓN DE CADA TEXTO
    // ============================================================

    IEnumerator AnimateText(
        string text,
        float duration,
        float initialRotation,
        float targetScale)
    {
        countdownText.text = text;

        textRect.localScale =
            Vector3.one * initialScale;

        textRect.localRotation =
            Quaternion.Euler(
                0f,
                0f,
                initialRotation
            );

        SetTextAlpha(0f);

        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;

            float t =
                Mathf.Clamp01(
                    timer / duration
                );

            // Hace que empiece rápido y termine suave.
            float smooth =
                1f - Mathf.Pow(
                    1f - t,
                    3f
                );

            // ====================================================
            // ESCALA
            // ====================================================

            float scale =
                Mathf.Lerp(
                    initialScale,
                    targetScale,
                    smooth
                );

            textRect.localScale =
                Vector3.one * scale;

            // ====================================================
            // ROTACIÓN
            // ====================================================

            float rotation =
                Mathf.Lerp(
                    initialRotation,
                    0f,
                    smooth
                );

            textRect.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    rotation
                );

            // ====================================================
            // ALPHA
            // ====================================================

            float alpha;

            // Aparece rápidamente.
            if (t < 0.15f)
            {
                alpha =
                    Mathf.InverseLerp(
                        0f,
                        0.15f,
                        t
                    );
            }
            // Desaparece al final.
            else if (t > 0.82f)
            {
                alpha =
                    1f -
                    Mathf.InverseLerp(
                        0.82f,
                        1f,
                        t
                    );
            }
            else
            {
                alpha = 1f;
            }

            SetTextAlpha(alpha);

            yield return null;
        }

        SetTextAlpha(0f);
    }

    void SetTextAlpha(float alpha)
    {
        Color color =
            countdownText.color;

        color.a = alpha;

        countdownText.color =
            color;
    }
}