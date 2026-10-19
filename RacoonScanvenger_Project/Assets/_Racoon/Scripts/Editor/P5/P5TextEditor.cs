using System.Linq;
using Racoon.UI.P5;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Inspector de <see cref="P5Text"/> (se regenera solo al cambiar cualquier campo; el botón lo fuerza, p. ej. tras
    /// retocar el estilo) y menú GameObject → UI → Racoon → Texto P5.
    /// </summary>
    [CustomEditor(typeof(P5Text)), CanEditMultipleObjects]
    public class P5TextEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerar"))
                foreach (P5Text text in targets)
                    text.Rebuild();
            EditorGUILayout.HelpBox("Las piezas (fichas y letras) se generan solas y no se guardan: para animar, mueve este objeto. " +
                                    "En partida: SetText(\"...\") y SetChip(\"P1\").", MessageType.None);
        }

        [MenuItem("GameObject/UI/Racoon/Texto P5", false, 10)]
        static void Create(MenuCommand command)
        {
            var parent = command.context as GameObject ?? Selection.activeGameObject;
            var go = new GameObject("Texto P5", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            GameObjectUtility.SetParentAndAlign(go, parent);
            var text = go.AddComponent<P5Text>();
            text.style = AssetDatabase.FindAssets("t:P5TextStyle")
                .Select(g => AssetDatabase.LoadAssetAtPath<P5TextStyle>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(s => s.name == "P5_Apodo" ? 0 : 1).FirstOrDefault();
            text.Rebuild();
            Undo.RegisterCreatedObjectUndo(go, "Crear texto P5");
            Selection.activeObject = go;
        }
    }
}
