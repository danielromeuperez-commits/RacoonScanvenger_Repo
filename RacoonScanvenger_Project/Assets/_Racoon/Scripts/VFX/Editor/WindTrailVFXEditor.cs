using UnityEditor;
using UnityEngine;

namespace Racoon.VFX
{
    [CustomEditor(typeof(WindTrailVFX))]
    public class WindTrailVFXEditor : Editor
    {
        // Para que "Modo actual" se actualice en Play.
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Test correr", GUILayout.Height(28))) PlayTest(WindTrailVFX.WindMode.Run);
                if (GUILayout.Button("Test dash", GUILayout.Height(28))) PlayTest(WindTrailVFX.WindMode.Dash);
            }
            if (Application.isPlaying)
                EditorGUILayout.LabelField("Modo actual", ((WindTrailVFX)target).Mode.ToString());
            else
                EditorGUILayout.HelpBox("Entra en Play para probarlo. La elipse azul de la Scene es la zona donde salen las estelas; la línea amarilla, hacia dónde va el movimiento.", MessageType.Info);
        }

        void PlayTest(WindTrailVFX.WindMode mode)
        {
            foreach (Object selected in targets)
                ((WindTrailVFX)selected).PlayTest(mode);
        }
    }
}
