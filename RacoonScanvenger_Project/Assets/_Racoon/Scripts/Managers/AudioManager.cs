using UnityEngine;
using UnityEngine.SceneManagement;

public class AudioManager : MonoBehaviour
{
    private static AudioManager instance;

    public static AudioManager Instance
    {
        get
        {
            if (instance == null)
                Debug.LogWarning("No hay AudioManager en la escena!");

            return instance;
        }
    }

    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;

    [Header("Audio Libraries")]
    public AudioClip[] musicLibrary;
    public AudioClip[] sfxLibrary;

    private float masterVolume = 1f;
    private float musicVolume = 1f;
    private float sfxVolume = 1f;

    private float musicFadeMultiplier = 1f;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);

            CreateAudioSourcesIfNeeded();

            LoadAudioSettings();
            ApplyVolumes();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CreateAudioSourcesIfNeeded();
        RefreshAudioVolumes();
    }

    private void CreateAudioSourcesIfNeeded()
    {
        if (musicSource == null)
        {
            GameObject musicObject = new GameObject("MusicSource");
            musicObject.transform.SetParent(transform);

            musicSource = musicObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
        }

        if (sfxSource == null)
        {
            GameObject sfxObject = new GameObject("SFXSource");
            sfxObject.transform.SetParent(transform);

            sfxSource = sfxObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
        }
    }

    public void PlayMusic(int musicToPlay)
    {
        CreateAudioSourcesIfNeeded();

        if (musicSource == null) return;
        if (musicLibrary == null || musicLibrary.Length == 0) return;
        if (musicToPlay < 0 || musicToPlay >= musicLibrary.Length) return;

        AudioClip selectedClip = musicLibrary[musicToPlay];

        LoadAudioSettings();
        ResetMusicFadeMultiplier();

        musicSource.clip = selectedClip;
        musicSource.loop = true;

        ApplyVolumes();

        if (!musicSource.isPlaying)
            musicSource.Play();

        Debug.Log("Playing music: " + selectedClip.name + " | Volume: " + musicSource.volume);
    }

    public void PlaySFX(int sfxToPlay)
    {
        CreateAudioSourcesIfNeeded();

        if (sfxSource == null) return;
        if (sfxLibrary == null || sfxLibrary.Length == 0) return;
        if (sfxToPlay < 0 || sfxToPlay >= sfxLibrary.Length) return;

        LoadAudioSettings();

        sfxSource.PlayOneShot(sfxLibrary[sfxToPlay], masterVolume * sfxVolume);
    }

    public void PlaySFXRandomPitch(int sfxToPlay, float minPitch = 0.95f, float maxPitch = 1.05f)
    {
        CreateAudioSourcesIfNeeded();

        if (sfxSource == null) return;
        if (sfxLibrary == null || sfxLibrary.Length == 0) return;
        if (sfxToPlay < 0 || sfxToPlay >= sfxLibrary.Length) return;

        LoadAudioSettings();

        float originalPitch = sfxSource.pitch;

        sfxSource.pitch = Random.Range(minPitch, maxPitch);
        sfxSource.PlayOneShot(sfxLibrary[sfxToPlay], masterVolume * sfxVolume);
        sfxSource.pitch = originalPitch;
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        PlayerPrefs.Save();

        ApplyVolumes();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("MusicVolume", musicVolume);
        PlayerPrefs.Save();

        ApplyVolumes();
    }

    public void SetSFXVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolume);
        PlayerPrefs.Save();

        ApplyVolumes();
    }

    public float GetMasterVolume()
    {
        LoadAudioSettings();
        return masterVolume;
    }

    public float GetMusicVolume()
    {
        LoadAudioSettings();
        return musicVolume;
    }

    public float GetSFXVolume()
    {
        LoadAudioSettings();
        return sfxVolume;
    }

    public void SetMusicFadeMultiplier(float value)
    {
        musicFadeMultiplier = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void ResetMusicFadeMultiplier()
    {
        musicFadeMultiplier = 1f;
        ApplyVolumes();
    }

    public void RefreshAudioVolumes()
    {
        LoadAudioSettings();
        ApplyVolumes();
    }

    public void ApplyVolumes()
    {
        CreateAudioSourcesIfNeeded();

        if (musicSource != null)
            musicSource.volume = masterVolume * musicVolume * musicFadeMultiplier;

        if (sfxSource != null)
            sfxSource.volume = masterVolume * sfxVolume;
    }

    private void LoadAudioSettings()
    {
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        musicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);
        sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
    }

    public void StopMusic()
    {
        if (musicSource != null)
            musicSource.Stop();
    }

    public void PauseMusic()
    {
        if (musicSource != null)
            musicSource.Pause();
    }

    public void UnPauseMusic()
    {
        if (musicSource != null)
            musicSource.UnPause();
    }
}