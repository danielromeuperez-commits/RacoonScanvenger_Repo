using System.Linq;
using Racoon.UI.P5;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Inspector de <see cref="P5Text"/>: regenera las piezas al cambiar cualquier campo (fuera de instancias de
    /// prefab) y añade el menú GameObject → UI → Racoon → Texto P5.
    /// </summary>
    [CustomEditor(typeof(P5Text)), CanEditMultipleObjects]
    public class P5TextEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            bool anyInstance = targets.Any(t => PrefabUtility.IsPartOfPrefabInstance(t));

            EditorGUILayout.Space();
            if (GUILayout.Button("Regenerar") || changed)
                foreach (P5Text text in targets)
                    if (!PrefabUtility.IsPartOfPrefabInstance(text))
                    {
                        Undo.RegisterFullObjectHierarchyUndo(text.gameObject, "Regenerar texto P5");
                        text.Rebuild();
                    }
            if (anyInstance)
                EditorGUILayout.HelpBox("Forma parte de una instancia de prefab: ábrelo en modo prefab para que se regeneren las piezas. " +
                                        "En partida usa SetText().", MessageType.Info);
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
