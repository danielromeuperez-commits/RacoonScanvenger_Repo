using UnityEngine;
using System.Collections;

public class PersistentMusic : MonoBehaviour
{
    private static PersistentMusic instance;
    private AudioSource audioSource;

    [Header("Configuración de Cuenta Atrás")]
    [SerializeField] private AudioClip countdownTickSfx;
    [SerializeField] private AudioClip countdownStartSfx;

    [Header("Volumen")]
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.3f; // 0.3 = 30% de volumen (ajústalo a tu gusto)

    public static PersistentMusic Instance => instance;

    private void Awake()
    {
        if (instance == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.volume = musicVolume;
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
        }
        else
        {
            if (instance != this)
            {
                Destroy(gameObject);
                return;
            }
        }
    }

    public void StopMusic()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            StartCoroutine(FadeOutAndStop(1f));
        }
    }

    private IEnumerator FadeOutAndStop(float duration)
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        audioSource.Stop();
        audioSource.volume = startVolume;
    }

    public void PlayTickSound()
    {
        if (audioSource != null && countdownTickSfx != null)
        {
            audioSource.PlayOneShot(countdownTickSfx);
        }
    }

    public void PlayStartSound()
    {
        if (audioSource != null && countdownStartSfx != null)
        {
            audioSource.PlayOneShot(countdownStartSfx);
        }
    }
}