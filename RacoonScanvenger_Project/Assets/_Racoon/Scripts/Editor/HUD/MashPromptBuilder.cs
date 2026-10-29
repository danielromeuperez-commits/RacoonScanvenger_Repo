using Racoon.UI;
using Racoon.UI.P5;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Aviso de «¡MACHACA!» (MashPromptUI) como prefab editable: crea PF_HUD_AvisoMachacar (Canvas Screen Space - Camera
    /// con sus piezas) si no existe y lo anida dentro de los tres prefabs de jugador. Edita PF_HUD_AvisoMachacar y
    /// cambia en los tres.
    ///  - Se monta solo al compilar si falta algo, y desde Racoon > UI > Montar aviso de machacar.
    ///  - Racoon > UI > Regenerar prefab del aviso de machacar lo vuelve a crear desde cero (pierde los cambios).
    /// Es idempotente: no duplica el aviso en los jugadores ni toca el prefab si ya existe.
    /// </summary>
    [InitializeOnLoad]
    public static class MashPromptBuilder
    {
        const string PrefabPath = "Assets/_Racoon/Prefabs/UI/HUD/PF_HUD_AvisoMachacar.prefab";
        const string GlyphsPath = "Assets/_Racoon/2D/UI/SO_InputGlyphs.asset";
        const string LabelStylePath = "Assets/_Racoon/2D/UI/EstilosTextoP5/Pastel_Cabecera.asset";
        const string SpritesDir = "Assets/_Racoon/2D/Sprites/UI/HUD/_Glifos/";
        const int UILayer = 5;

        static readonly string[] Players =
        {
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRaccoon.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerOpossum.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRedPanda.prefab",
        };

        static MashPromptBuilder() => EditorApplication.delayCall += MountIfMissing;

        static void MountIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var prompt = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            bool missing = prompt == null;
            foreach (string path in Players)
            {
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (player == null) continue;
                // Falta el aviso anidado, o queda el componente antiguo en la raíz (sin Canvas).
                missing |= player.GetComponentInChildren<MashPromptUI>(true) == null || player.GetComponent<MashPromptUI>() != null;
            }
            if (missing) Mount();
        }

        [MenuItem("Racoon/UI/Montar aviso de machacar")]
        public static void Mount()
        {
            var prompt = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prompt == null) prompt = BuildPrefab();
            if (prompt == null) return;

            foreach (string path in Players)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    // El de la sesión anterior iba en la raíz del jugador y se montaba por código: fuera.
                    foreach (MashPromptUI old in root.GetComponents<MashPromptUI>())
                    {
                        Object.DestroyImmediate(old);
                        changed = true;
                    }

                    bool nested = false;
                    foreach (Transform child in root.transform)
                        nested |= PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == prompt;
                    if (!nested)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prompt, root.transform);
                        instance.transform.SetAsLastSibling();
                        changed = true;
                    }

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Debug.Log($"[MashPromptBuilder] {path}: aviso de machacar anidado.");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [MenuItem("Racoon/UI/Regenerar prefab del aviso de machacar (pierde los cambios)")]
        static void Rebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Regenerar aviso de machacar",
                    "Se vuelve a crear PF_HUD_AvisoMachacar desde cero y se pierden los cambios hechos a mano.", "Regenerar", "Cancelar"))
                return;
            BuildPrefab();
            Mount();
        }

        // ---------------- Prefab del aviso ----------------

        static GameObject BuildPrefab()
        {
            var glyphs = AssetDatabase.LoadAssetAtPath<InputGlyphSet>(GlyphsPath);
            var labelStyle = AssetDatabase.LoadAssetAtPath<P5TextStyle>(LabelStylePath);
            if (glyphs == null || labelStyle == null)
            {
                Debug.LogError($"[MashPromptBuilder] Faltan {GlyphsPath} o {LabelStylePath}.");
                return null;
            }

            var go = new GameObject("PF_HUD_AvisoMachacar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)) { layer = UILayer };
            try
            {
                // Screen Space - Camera, como todos los Canvas del proyecto (la cámara la pone CanvasCameraBinder).
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 60;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2560f, 1440f);
                scaler.matchWidthOrHeight = 0.5f;

                var ui = go.AddComponent<MashPromptUI>();
                ui.content = Rect("Aviso", go.transform, Vector2.zero, Vector2.zero);
                ui.group = ui.content.gameObject.AddComponent<CanvasGroup>();
                ui.group.interactable = ui.group.blocksRaycasts = false;

                ui.burst = Picture("Estallido", ui.content, "Aviso_Estallido", 330f);
                Picture("AnilloFondo", ui.content, "Aviso_Anillo_Fondo", 240f);
                ui.ringFill = Picture("AnilloRelleno", ui.content, "Aviso_Anillo_Relleno", 211f);
                ui.ringFill.type = Image.Type.Filled;
                ui.ringFill.fillMethod = Image.FillMethod.Radial360;
                ui.ringFill.fillOrigin = (int)Image.Origin360.Top;
                ui.ringFill.fillClockwise = true;
                ui.ringFill.fillAmount = 0.6f; // para verlo al editar; en partida sigue al progreso
                ui.ringFill.color = new Color(1f, 0.878f, 0.541f);
                // Glifo de ejemplo (en partida, el del mando que use): manda su alto.
                ui.glyph = Picture("Glifo", ui.content, "Glifo_PS_Circulo", 130f);
                ui.glyph.preserveAspect = true;

                RectTransform labelRect = Rect("Rotulo", ui.content, new Vector2(0f, 190f), Vector2.zero);
                labelRect.localRotation = Quaternion.Euler(0f, 0f, -5f);
                ui.labelText = labelRect.gameObject.AddComponent<P5Text>();
                ui.labelText.style = labelStyle;
                ui.labelText.alineacion = P5Text.Alignment.Centro;
                ui.labelText.escalaTexto = 2.2f;
                ui.labelText.player = 0;
                ui.labelText.SetText("¡MACHACA!");

                var serialized = new SerializedObject(ui);
                serialized.FindProperty("glyphs").objectReferenceValue = glyphs;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Debug.Log($"[MashPromptBuilder] Creado {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UILayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image Picture(string name, Transform parent, string sprite, float size)
        {
            RectTransform rect = Rect(name, parent, Vector2.zero, new Vector2(size, size));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritesDir + sprite + ".png");
            image.raycastTarget = false;
            if (image.sprite == null) Debug.LogWarning($"[MashPromptBuilder] Falta el sprite {sprite}.");
            return image;
        }
    }
}
