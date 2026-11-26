using UnityEngine;
using UnityEngine.UI;

namespace Racoon.UI.Localization
{
    /// <summary>Casillas EN / ES del menú de opciones: solo una marcada a la vez, y marca la del idioma actual.</summary>
    public class LanguageToggles : MonoBehaviour
    {
        [SerializeField] Toggle englishToggle;
        [SerializeField] Toggle spanishToggle;

        bool refreshing;

        void Awake()
        {
            if (englishToggle != null) englishToggle.onValueChanged.AddListener(on => Pick(on, GameLanguage.English));
            if (spanishToggle != null) spanishToggle.onValueChanged.AddListener(on => Pick(on, GameLanguage.Spanish));
        }

        void OnEnable()
        {
            Loc.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Loc.Changed -= Refresh;

        void Pick(bool on, GameLanguage language)
        {
            if (refreshing) return;
            // Desmarcar la casilla activa no deja el juego sin idioma: se vuelve a marcar
            if (!on)
            {
                if (Loc.Language == language) Refresh();
                return;
            }
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(23);
            Loc.SetLanguage(language);
            Refresh();
        }

        void Refresh()
        {
            refreshing = true;
            var current = Loc.Language;
            if (englishToggle != null) englishToggle.SetIsOnWithoutNotify(current == GameLanguage.English);
            if (spanishToggle != null) spanishToggle.SetIsOnWithoutNotify(current == GameLanguage.Spanish);
            refreshing = false;
        }
    }
}
