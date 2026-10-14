using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Entry = Racoon.EditorTools.FolderStyleSettings.Entry;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Pinta las carpetas de la ventana Project con el color/icono definido en FolderStyleSettings.
    ///
    /// Uso: clic derecho sobre una o varias carpetas → "Color de carpeta" → elegir color.
    /// "Personalizar…" abre el asset de ajustes para elegir cualquier color, poner un icono o hacer
    /// que las subcarpetas hereden el estilo.
    /// </summary>
    [InitializeOnLoad]
    public static class FolderStyleDrawer
    {
        // Colores de fondo de la ventana Project, para tapar el icono original en vista de lista.
        static readonly Color BgDark = new Color32(56, 56, 56, 255);
        static readonly Color BgLight = new Color32(200, 200, 200, 255);
        static readonly Color SelectedDark = new Color32(44, 93, 135, 255);
        static readonly Color SelectedLight = new Color32(58, 114, 176, 255);
        static readonly Color SelectedUnfocusedDark = new Color32(77, 77, 77, 255);
        static readonly Color SelectedUnfocusedLight = new Color32(174, 174, 174, 255);

        static Dictionary<string, Entry> entriesByGuid;                  // null = hay que reconstruir
        static readonly Dictionary<string, Entry> resolvedCache = new();  // incluye herencia; valor null = sin estilo
        static readonly Dictionary<string, bool> emptyCache = new();
        static Texture folderIcon, folderEmptyIcon;

        static FolderStyleDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectItemGUI;
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
            FolderStyleSettings.Changed += Invalidate;
        }

        static void Invalidate()
        {
            entriesByGuid = null;
            resolvedCache.Clear();
            emptyCache.Clear();
            EditorApplication.RepaintProjectWindow();
        }

        // ───────────────────────────── Dibujado ─────────────────────────────

        static void OnProjectItemGUI(string guid, Rect rect)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(guid)) return;

            if (entriesByGuid == null) Rebuild();
            if (entriesByGuid.Count == 0) return;

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!AssetDatabase.IsValidFolder(path)) return;

            Entry entry = Resolve(guid, path);
            if (entry == null) return;

            bool grid = rect.height > 20;
            Rect iconRect = grid
                ? new Rect(rect.x, rect.y, rect.width, rect.width)
                : new Rect(rect.x, rect.y, 16, 16);

            // En vista de lista la carpeta puede salir "abierta" (otra forma), así que se tapa primero.
            // En cuadrícula la forma es la misma y basta con pintar encima.
            if (!grid) EditorGUI.DrawRect(iconRect, ListBackground(guid));

            if (!grid && entry.icon != null)
            {
                GUI.DrawTexture(iconRect, entry.icon, ScaleMode.ScaleToFit);
                return;
            }

            Color prev = GUI.color;
            GUI.color = new Color(entry.color.r, entry.color.g, entry.color.b, 1f);
            GUI.DrawTexture(iconRect, GetFolderTexture(IsEmpty(guid, path)), ScaleMode.ScaleToFit);
            GUI.color = prev;

            if (entry.icon != null)
            {
                float size = iconRect.width * 0.45f;
                var badge = new Rect(iconRect.center.x - size * 0.5f, iconRect.y + iconRect.height * 0.38f, size, size);
                GUI.DrawTexture(badge, entry.icon, ScaleMode.ScaleToFit);
            }
        }

        static Color ListBackground(string guid)
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (Array.IndexOf(Selection.assetGUIDs, guid) < 0) return pro ? BgDark : BgLight;

            bool focused = EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType().Name == "ProjectBrowser";
            if (focused) return pro ? SelectedDark : SelectedLight;
            return pro ? SelectedUnfocusedDark : SelectedUnfocusedLight;
        }

        static Texture GetFolderTexture(bool empty)
        {
            if (folderIcon == null) folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            if (folderEmptyIcon == null) folderEmptyIcon = EditorGUIUtility.IconContent("FolderEmpty Icon").image;
            return empty ? folderEmptyIcon : folderIcon;
        }

        static bool IsEmpty(string guid, string path)
        {
            if (emptyCache.TryGetValue(guid, out bool empty)) return empty;
            try
            {
                // Unity ignora los .meta, los ocultos (".algo") y los que acaban en "~".
                empty = !Directory.EnumerateFileSystemEntries(path).Any(p =>
                {
                    string name = Path.GetFileName(p);
                    return !name.EndsWith(".meta") && !name.StartsWith(".") && !name.EndsWith("~");
                });
            }
            catch (Exception)
            {
                empty = false; // p. ej. carpetas de paquetes que no están en disco con esa ruta
            }
            emptyCache[guid] = empty;
            return empty;
        }

        // ───────────────────────────── Datos ─────────────────────────────

        static void Rebuild()
        {
            entriesByGuid = new Dictionary<string, Entry>();
            var settings = FolderStyleSettings.Find();
            if (settings == null) return;

            foreach (var entry in settings.entries)
            {
                if (entry.folder == null) continue;
                string path = AssetDatabase.GetAssetPath(entry.folder);
                if (AssetDatabase.IsValidFolder(path))
                    entriesByGuid[AssetDatabase.AssetPathToGUID(path)] = entry;
            }
        }

        /// <summary>Estilo propio de la carpeta o, si no tiene, el del ancestro más cercano con "applyToSubfolders".</summary>
        static Entry Resolve(string guid, string path)
        {
            if (resolvedCache.TryGetValue(guid, out var cached)) return cached;

            if (!entriesByGuid.TryGetValue(guid, out var result))
            {
                for (string parent = ParentOf(path); !string.IsNullOrEmpty(parent); parent = ParentOf(parent))
                {
                    if (entriesByGuid.TryGetValue(AssetDatabase.AssetPathToGUID(parent), out var e) && e.applyToSubfolders)
                    {
                        result = e;
                        break;
                    }
                }
            }

            resolvedCache[guid] = result;
            return result;
        }

        static string ParentOf(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/');

        // ───────────────────────────── Menú contextual ─────────────────────────────

        const string Menu = "Assets/Color de carpeta/";
        const int Priority = 2000;

        [MenuItem(Menu + "Rojo", false, Priority)]     static void Red()    => SetColor(new Color(1f, 0.35f, 0.35f));
        [MenuItem(Menu + "Naranja", false, Priority)]  static void Orange() => SetColor(new Color(1f, 0.6f, 0.25f));
        [MenuItem(Menu + "Amarillo", false, Priority)] static void Yellow() => SetColor(new Color(1f, 0.88f, 0.3f));
        [MenuItem(Menu + "Verde", false, Priority)]    static void Green()  => SetColor(new Color(0.45f, 0.9f, 0.4f));
        [MenuItem(Menu + "Turquesa", false, Priority)] static void Teal()   => SetColor(new Color(0.3f, 0.9f, 0.85f));
        [MenuItem(Menu + "Azul", false, Priority)]     static void Blue()   => SetColor(new Color(0.4f, 0.65f, 1f));
        [MenuItem(Menu + "Morado", false, Priority)]   static void Purple() => SetColor(new Color(0.7f, 0.5f, 1f));
        [MenuItem(Menu + "Rosa", false, Priority)]     static void Pink()   => SetColor(new Color(1f, 0.5f, 0.8f));
        [MenuItem(Menu + "Gris", false, Priority)]     static void Gray()   => SetColor(new Color(0.55f, 0.55f, 0.55f));

        [MenuItem(Menu + "Personalizar…", false, Priority + 20)]
        static void Customize()
        {
            var settings = FolderStyleSettings.GetOrCreate();
            Undo.RecordObject(settings, "Personalizar carpeta");
            foreach (var folder in SelectedFolders()) GetOrAddEntry(settings, folder);
            Save(settings);

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(Menu + "Quitar estilo", false, Priority + 21)]
        static void Clear()
        {
            var settings = FolderStyleSettings.Find();
            if (settings == null) return;

            var selected = new HashSet<DefaultAsset>(SelectedFolders());
            Undo.RecordObject(settings, "Quitar estilo de carpeta");
            settings.entries.RemoveAll(e => e.folder == null || selected.Contains(e.folder));
            Save(settings);
        }

        [MenuItem(Menu + "Rojo", true)]
        [MenuItem(Menu + "Naranja", true)]
        [MenuItem(Menu + "Amarillo", true)]
        [MenuItem(Menu + "Verde", true)]
        [MenuItem(Menu + "Turquesa", true)]
        [MenuItem(Menu + "Azul", true)]
        [MenuItem(Menu + "Morado", true)]
        [MenuItem(Menu + "Rosa", true)]
        [MenuItem(Menu + "Gris", true)]
        [MenuItem(Menu + "Personalizar…", true)]
        [MenuItem(Menu + "Quitar estilo", true)]
        static bool HasFolderSelected() => SelectedFolders().Any();

        static void SetColor(Color color)
        {
            var settings = FolderStyleSettings.GetOrCreate();
            Undo.RecordObject(settings, "Color de carpeta");
            foreach (var folder in SelectedFolders()) GetOrAddEntry(settings, folder).color = color;
            Save(settings);
        }

        static IEnumerable<DefaultAsset> SelectedFolders() =>
            Selection.GetFiltered<DefaultAsset>(SelectionMode.Assets)
                .Where(a => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(a)));

        static Entry GetOrAddEntry(FolderStyleSettings settings, DefaultAsset folder)
        {
            var entry = settings.entries.FirstOrDefault(e => e.folder == folder);
            if (entry == null)
            {
                entry = new Entry { folder = folder };
                settings.entries.Add(entry);
            }
            return entry;
        }

        static void Save(FolderStyleSettings settings)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            Invalidate();
        }
    }
}
