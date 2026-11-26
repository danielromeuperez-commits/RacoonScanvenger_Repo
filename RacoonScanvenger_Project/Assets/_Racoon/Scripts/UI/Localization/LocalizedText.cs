using Racoon.UI.P5;
using TMPro;
using UnityEngine;

namespace Racoon.UI.Localization
{
    /// <summary>
    /// Pone el texto de <see cref="key"/> en el TMP_Text o P5Text del mismo objeto y lo cambia al cambiar de idioma.
    /// Con <see cref="chipKey"/>, también la «chapa» del P5Text.
    /// </summary>
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] string key;
        [SerializeField] string chipKey;

        public string Key
        {
            get => key;
            set { key = value; Apply(); }
        }

        void OnEnable()
        {
            Loc.Changed += Apply;
            Apply();
        }

        void OnDisable() => Loc.Changed -= Apply;

        public void Apply()
        {
            var p5 = GetComponent<P5Text>();
            if (p5 != null)
            {
                if (!string.IsNullOrEmpty(chipKey)) p5.chapa = Loc.T(chipKey);
                if (!string.IsNullOrEmpty(key)) p5.SetText(Loc.T(key));
                else if (!string.IsNullOrEmpty(chipKey)) p5.Rebuild();
                return;
            }
            if (string.IsNullOrEmpty(key)) return;
            var tmp = GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = Loc.T(key);
        }
    }
}
