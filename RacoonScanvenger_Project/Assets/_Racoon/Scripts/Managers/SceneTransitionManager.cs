using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance;

    [Header("Referencias")]
    [SerializeField] Canvas transitionCanvas;
    [SerializeField] Image blackFade;
    [SerializeField] RectTransform widePanel;

    [Header("Tiempos")]
    [SerializeField, Min(0.05f)] float blackFadeDuration = 0.35f;
    [SerializeField, Min(0.05f)] float wipeDuration = 0.45f;
    [SerializeField, Min(0f)] float postLoadDelay = 0.05f;

    [Header("Ajustes del Wipe")]
    [SerializeField] float wipeMaxScaleX = 3.2f;

    bool isTransitioning;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);

        SetupInitialState();
    }

    void SetupInitialState()
    {
        if (transitionCanvas != null)
            transitionCanvas.enabled = true;

        if (blackFade != null)
            blackFade.raycastTarget = false;

        if (widePanel != null)
        {
            Image wideImage = widePanel.GetComponent<Image>();

            if (wideImage != null)
                wideImage.raycastTarget = false;
        }

        SetBlackFadeAlpha(0f);

        if (widePanel != null)
        {
            Vector3 scale = widePanel.localScale;
            scale.x = 0f;
            scale.y = 1f;
            scale.z = 1f;

            widePanel.localScale = scale;
        }
    }

    // ============================================================
    // LLAMADAS PUBLICAS
    // ============================================================

    public void LoadSceneWithBlackFade(string sceneName)
    {
        if (!CanStartTransition(sceneName))
            return;

        StartCoroutine(CoLoadSceneWithBlackFade(sceneName));
    }

    public void LoadSceneWithDiagonalWipe(string sceneName)
    {
        if (!CanStartTransition(sceneName))
            return;

        StartCoroutine(CoLoadSceneWithDiagonalWipe(sceneName));
    }

    // ============================================================
    // COROUTINES
    // ============================================================

    IEnumerator CoLoadSceneWithBlackFade(string sceneName)
    {
        isTransitioning = true;

        yield return FadeBlack(0f, 1f, blackFadeDuration);

        AsyncOperation loadOperation =
            SceneManager.LoadSceneAsync(sceneName);

        while (!loadOperation.isDone)
            yield return null;

        if (postLoadDelay > 0f)
            yield return new WaitForSecondsRealtime(postLoadDelay);

        yield return FadeBlack(1f, 0f, blackFadeDuration);

        isTransitioning = false;
    }

    IEnumerator CoLoadSceneWithDiagonalWipe(string sceneName)
    {
        isTransitioning = true;

        SetBlackFadeAlpha(0f);

        // Expandimos el panel diagonal hasta cubrir la pantalla.
        yield return ScaleWidePanelX(0f, wipeMaxScaleX, wipeDuration);

        // Dejamos el fondo negro por si durante la carga
        // hay un pequeño parpadeo.
        SetBlackFadeAlpha(1f);

        AsyncOperation loadOperation =
            SceneManager.LoadSceneAsync(sceneName);

        while (!loadOperation.isDone)
            yield return null;

        if (postLoadDelay > 0f)
            yield return new WaitForSecondsRealtime(postLoadDelay);

        // Esperamos un frame para que la nueva escena ya esté estable.
        yield return null;

        // Quitamos el negro y abrimos la transición.
        SetBlackFadeAlpha(0f);

        yield return ScaleWidePanelX(wipeMaxScaleX, 0f, wipeDuration);

        isTransitioning = false;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    bool CanStartTransition(string sceneName)
    {
        if (isTransitioning)
            return false;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("SceneTransitionManager: el nombre de escena está vacío.");
            return false;
        }

        return true;
    }

    IEnumerator FadeBlack(float from, float to, float duration)
    {
        if (blackFade == null)
        {
            Debug.LogWarning("SceneTransitionManager: falta asignar BlackFade.");
            yield break;
        }

        float timer = 0f;

        SetBlackFadeAlpha(from);

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(timer / duration);
            float alpha = Mathf.Lerp(from, to, t);

            SetBlackFadeAlpha(alpha);

            yield return null;
        }

        SetBlackFadeAlpha(to);
    }

    IEnumerator ScaleWidePanelX(float from, float to, float duration)
    {
        if (widePanel == null)
        {
            Debug.LogWarning("SceneTransitionManager: falta asignar WidePanel.");
            yield break;
        }

        float timer = 0f;

        Vector3 scale = widePanel.localScale;
        scale.x = from;
        scale.y = 1f;
        scale.z = 1f;
        widePanel.localScale = scale;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(timer / duration);
            float currentX = Mathf.Lerp(from, to, t);

            scale = widePanel.localScale;
            scale.x = currentX;
            scale.y = 1f;
            scale.z = 1f;
            widePanel.localScale = scale;

            yield return null;
        }

        scale = widePanel.localScale;
        scale.x = to;
        scale.y = 1f;
        scale.z = 1f;
        widePanel.localScale = scale;
    }

    void SetBlackFadeAlpha(float alpha)
    {
        if (blackFade == null)
            return;

        Color color = blackFade.color;
        color.a = alpha;
        blackFade.color = color;
    }
}