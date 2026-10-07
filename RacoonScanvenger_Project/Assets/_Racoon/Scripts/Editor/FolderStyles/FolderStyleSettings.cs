using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Colores e iconos de las carpetas en la ventana Project. Solo editor (no entra en la build).
    ///
    /// Se crea sola la primera vez que se usa el menú, en esta misma carpeta, y va en el repo, así que todo el equipo ve
    /// los mismos colores. Se puede editar desde el Inspector o con clic derecho sobre una carpeta →
    /// "Color de carpeta".
    /// </summary>
    public class FolderStyleSettings : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Carpeta a la que se aplica. Se guarda por referencia, así que sobrevive a renombrados y movimientos.")]
            public DefaultAsset folder;

            public Color color = Color.white;

            [Tooltip("Opcional. En vista de cuadrícula se dibuja como insignia sobre la carpeta; en vista de lista sustituye al icono.")]
            public Texture2D icon;

            [Tooltip("Si está activo, las subcarpetas sin estilo propio heredan este.")]
            public bool applyToSubfolders;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>Se lanza al cambiar algo desde el Inspector para que el drawer limpie su caché.</summary>
        public static event Action Changed;

        void OnValidate() => Changed?.Invoke();

        public static void NotifyChanged() => Changed?.Invoke();

        static FolderStyleSettings instance;

        /// <summary>Busca el asset sin crearlo (puede devolver null). Seguro de llamar desde OnGUI.</summary>
        public static FolderStyleSettings Find()
        {
            if (instance != null) return instance;

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(FolderStyleSettings));
            if (guids.Length > 0)
                instance = AssetDatabase.LoadAssetAtPath<FolderStyleSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return instance;
        }

        /// <summary>Busca el asset y, si no existe, lo crea junto a este script.</summary>
        public static FolderStyleSettings GetOrCreate()
        {
            if (Find() != null) return instance;

            instance = CreateInstance<FolderStyleSettings>();
            var script = MonoScript.FromScriptableObject(instance);
            string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(script)).Replace('\\', '/');
            AssetDatabase.CreateAsset(instance, dir + "/FolderStyleSettings.asset");
            AssetDatabase.SaveAssets();
            return instance;
        }
    }
}
