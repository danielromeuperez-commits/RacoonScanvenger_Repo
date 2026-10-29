using Racoon.Items;
using Racoon.UI;
using Racoon.UI.P5;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Indicadores junto al jugador (PlayerStatusUI: anillo dorado de la bolsa y bebida energética) como prefab
    /// editable: crea PF_HUD_EstadoJugador (Canvas Screen Space - Camera con sus piezas) si no existe y lo anida
    /// dentro de los tres prefabs de jugador. Edita PF_HUD_EstadoJugador y cambia en los tres.
    ///  - Se monta solo al compilar si falta, y desde Racoon > UI > Montar indicadores del jugador.
    ///  - Racoon > UI > Regenerar prefab de los indicadores del jugador lo vuelve a crear desde cero (pierde los cambios).
    /// Es idempotente: no duplica los indicadores en los jugadores ni toca el prefab si ya existe.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerStatusBuilder
    {
        const string PrefabPath = "Assets/_Racoon/Prefabs/UI/HUD/PF_HUD_EstadoJugador.prefab";
        const string LabelStylePath = "Assets/_Racoon/2D/UI/EstilosTextoP5/Pastel_Cabecera.asset";
        const string EnergyDrinkPath = "Assets/_Racoon/Scripts/ScriptableObjects/itemsData/ITEM_EnergyDrink.asset";
        const int UILayer = 5;

        static readonly string[] Players =
        {
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRaccoon.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerOpossum.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRedPanda.prefab",
        };

        static PlayerStatusBuilder() => EditorApplication.delayCall += MountIfMissing;

        static void MountIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool missing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null;
            foreach (string path in Players)
            {
                var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (player != null) missing |= player.GetComponentInChildren<PlayerStatusUI>(true) == null;
            }
            if (missing) Mount();
        }

        [MenuItem("Racoon/UI/Montar indicadores del jugador")]
        public static void Mount()
        {
            var status = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (status == null) status = BuildPrefab();
            if (status == null) return;

            foreach (string path in Players)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool nested = false;
                    foreach (Transform child in root.transform)
                        nested |= PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == status;
                    if (nested) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(status, root.transform);
                    instance.transform.SetAsLastSibling();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[PlayerStatusBuilder] {path}: indicadores del jugador anidados.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [MenuItem("Racoon/UI/Regenerar prefab de los indicadores del jugador (pierde los cambios)")]
        static void Rebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                !EditorUtility.DisplayDialog("Regenerar indicadores del jugador",
                    "Se vuelve a crear PF_HUD_EstadoJugador desde cero y se pierden los cambios hechos a mano.", "Regenerar", "Cancelar"))
                return;
            BuildPrefab();
            Mount();
        }

        // ---------------- Prefab ----------------

        static GameObject BuildPrefab()
        {
            var labelStyle = AssetDatabase.LoadAssetAtPath<P5TextStyle>(LabelStylePath);
            if (labelStyle == null)
            {
                Debug.LogError($"[PlayerStatusBuilder] Falta {LabelStylePath}.");
                return null;
            }

            var go = new GameObject("PF_HUD_EstadoJugador", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)) { layer = UILayer };
            try
            {
                // Screen Space - Camera, como todos los Canvas del proyecto (la cámara la pone CanvasCameraBinder).
                var canvas = go.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = 1f;
                canvas.sortingOrder = 55;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2560f, 1440f);
                scaler.matchWidthOrHeight = 0.5f;

                var ui = go.AddComponent<PlayerStatusUI>();

                // Bolsa: anillo dorado con la marca del peso máximo y el saco en el centro.
                ui.bagGroup = MashPromptBuilder.Rect("Bolsa", go.transform, new Vector2(150f, 10f), new Vector2(150f, 150f));
                ui.bagCanvasGroup = Group(ui.bagGroup);
                MashPromptBuilder.Picture("AnilloFondo", ui.bagGroup, "Aviso_Anillo_Fondo", 150f);
                ui.bagFill = RadialFill(MashPromptBuilder.Picture("AnilloRelleno", ui.bagGroup, "Aviso_Anillo_Relleno", 132f), 0.65f);
                ui.bagFill.color = new Color(1f, 0.78f, 0.2f);
                ui.weightMark = MashPromptBuilder.Rect("MarcaPeso", ui.bagGroup, Vector2.zero, Vector2.zero);
                ui.weightMark.localRotation = Quaternion.Euler(0f, 0f, -288f); // 80 de 100, para verlo al editar
                ui.weightMarkImage = MashPromptBuilder.Picture("Marca", ui.weightMark, "Estado_Marca", 36f);
                ui.weightMarkImage.rectTransform.sizeDelta = new Vector2(18f, 36f);
                ui.weightMarkImage.rectTransform.anchoredPosition = new Vector2(0f, 60f);
                MashPromptBuilder.Picture("Saco", ui.bagGroup, "Estado_Saco", 72f).preserveAspect = true;
                ui.bagLabel = Label("Rotulo", ui.bagGroup, labelStyle, new Vector2(0f, 108f), -4f, "¡LLENA!");

                // Bebida energética: la lata con el anillo de lo que le queda.
                ui.energyGroup = MashPromptBuilder.Rect("Bebida", go.transform, new Vector2(-150f, 10f), new Vector2(130f, 130f));
                ui.energyCanvasGroup = Group(ui.energyGroup);
                MashPromptBuilder.Picture("AnilloFondo", ui.energyGroup, "Aviso_Anillo_Fondo", 130f);
                ui.energyFill = RadialFill(MashPromptBuilder.Picture("AnilloRelleno", ui.energyGroup, "Aviso_Anillo_Relleno", 114f), 0.7f);
                ui.energyFill.color = new Color(0.35f, 0.9f, 1f);
                Image can = MashPromptBuilder.Picture("Lata", ui.energyGroup, "Aviso_Estallido", 66f);
                var drink = AssetDatabase.LoadAssetAtPath<ItemData>(EnergyDrinkPath);
                if (drink != null && drink.icon != null) can.sprite = drink.icon;
                else Debug.LogWarning($"[PlayerStatusBuilder] Sin icono en {EnergyDrinkPath}: pon el de la lata en Bebida/Lata.");
                can.preserveAspect = true;
                ui.energyLabel = Label("Rotulo", ui.energyGroup, labelStyle, new Vector2(0f, 100f), 4f, "¡YA VAS A TOPE!");

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Debug.Log($"[PlayerStatusBuilder] Creado {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static CanvasGroup Group(RectTransform rect)
        {
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            return group;
        }

        static Image RadialFill(Image image, float preview)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = preview; // para verlo al editar; en partida sigue al dato
            return image;
        }

        static P5Text Label(string name, Transform parent, P5TextStyle style, Vector2 position, float angle, string text)
        {
            RectTransform rect = MashPromptBuilder.Rect(name, parent, position, Vector2.zero);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            var label = rect.gameObject.AddComponent<P5Text>();
            label.style = style;
            label.alineacion = P5Text.Alignment.Centro;
            label.escalaTexto = 1.2f;
            label.player = 0;
            label.SetText(text);
            return label;
        }
    }
}
