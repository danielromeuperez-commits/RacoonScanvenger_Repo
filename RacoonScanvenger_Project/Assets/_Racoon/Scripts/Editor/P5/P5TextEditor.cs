using Racoon.UI.P5;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Inspector de <see cref="P5Text"/> (se regenera solo al cambiar cualquier campo; el botón lo fuerza, p. ej. tras
    /// retocar el estilo).
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
    }
}
