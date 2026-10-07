using UnityEditor;
using UnityEngine;

namespace Racoon.VFX
{
    [CustomEditor(typeof(WindTrailVFX))]
    public class WindTrailVFXEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Test dash", GUILayout.Height(28)))
                    foreach (Object selected in targets)
                        ((WindTrailVFX)selected).PlayTest();
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Entra en Play para probarlo. La elipse azul de la Scene es la zona donde salen las estelas; la línea amarilla, hacia dónde va el dash.", MessageType.Info);
        }
    }
}
