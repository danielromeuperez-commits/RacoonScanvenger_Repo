using UnityEngine;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    [Header("Sliders")]
    public Slider musicSlider;
    public Slider sfxSlider;

    private void Start()
    {
        // Inicializar los sliders con los valores actuales guardados en el AudioManager
        if (AudioManager.Instance != null)
        {
            if (musicSlider != null)
                musicSlider.value = AudioManager.Instance.GetMusicVolume();

            if (sfxSlider != null)
                sfxSlider.value = AudioManager.Instance.GetSFXVolume();

        }

        // Escuchar cuando el usuario mueva los sliders
        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);

        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(OnSFXSliderChanged);

    }

    private void OnMusicSliderChanged(float value)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.SetMusicVolume(value);
    }

    private void OnSFXSliderChanged(float value)
    {
        if (AudioManager.Instance != null)
            AudioManager.Instance.SetSFXVolume(value);
    }
}