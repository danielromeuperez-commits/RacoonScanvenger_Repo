using System;
using System.Collections.Generic;
using UnityEngine;

namespace Racoon.UI.Localization
{
    public enum GameLanguage { English = 0, Spanish = 1 }

    /// <summary>
    /// Textos del juego en inglés y español. Cada idioma es un archivo aparte en Resources/Localization
    /// (en.txt, es.txt) con líneas «clave = texto»; «#» empieza un comentario y «\n» es un salto de línea.
    /// El idioma elegido se guarda en PlayerPrefs; la primera vez se usa el del sistema (inglés si no es español).
    /// </summary>
    public static class Loc
    {
        const string PrefKey = "Racoon.Language";

        static readonly Dictionary<string, string> table = new();
        static readonly Dictionary<string, string> fallback = new();
        static bool loaded;
        static GameLanguage language;

        /// <summary>Se lanza al cambiar de idioma: los textos visibles se vuelven a escribir.</summary>
        public static event Action Changed;

        public static GameLanguage Language
        {
            get { EnsureLoaded(); return language; }
        }

        public static void SetLanguage(GameLanguage value)
        {
            EnsureLoaded();
            if (value == language) return;
            language = value;
            try { PlayerPrefs.SetInt(PrefKey, (int)value); PlayerPrefs.Save(); } catch { }
            Load();
            Changed?.Invoke();
        }

        /// <summary>Texto de <paramref name="key"/> en el idioma actual (en inglés si falta; la clave si no existe).</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureLoaded();
            if (table.TryGetValue(key, out var value)) return value;
            if (fallback.TryGetValue(key, out value)) return value;
            Debug.LogWarning($"Loc: falta la clave «{key}».");
            return key;
        }

        /// <summary><see cref="T"/> con string.Format.</summary>
        public static string F(string key, params object[] args)
        {
            string format = T(key);
            try { return string.Format(format, args); }
            catch (FormatException) { return format; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded = false;
            Changed = null;
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            int saved = -1;
            try { saved = PlayerPrefs.GetInt(PrefKey, -1); } catch { }
            language = saved == 0 || saved == 1
                ? (GameLanguage)saved
                : Application.systemLanguage == SystemLanguage.Spanish ? GameLanguage.Spanish : GameLanguage.English;
            Load();
        }

        static void Load()
        {
            Parse(Code(language), table);
            if (fallback.Count == 0) Parse(Code(GameLanguage.English), fallback);
        }

        static string Code(GameLanguage lang) => lang == GameLanguage.Spanish ? "es" : "en";

        static void Parse(string code, Dictionary<string, string> into)
        {
            into.Clear();
            var asset = Resources.Load<TextAsset>("Localization/" + code);
            if (asset == null)
            {
                Debug.LogError($"Loc: no existe Resources/Localization/{code}.txt");
                return;
            }
            foreach (var raw in asset.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                into[key] = value;
            }
        }
    }
}
