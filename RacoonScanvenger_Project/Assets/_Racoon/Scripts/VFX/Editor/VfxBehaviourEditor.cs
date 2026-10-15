using System.Reflection;
using Racoon.VFX;
using UnityEditor;
using UnityEngine;

namespace Racoon.VFX.EditorTools
{
    /// <summary>Inspector de los efectos: el de siempre + un botón por cada método con [VfxTest] (solo en Play).</summary>
    [CustomEditor(typeof(VfxBehaviour), true)]
    [CanEditMultipleObjects]
    public class VfxBehaviourEditor : Editor
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            MethodInfo[] methods = target.GetType().GetMethods(Flags);
            bool any = false;
            foreach (MethodInfo method in methods)
            {
                VfxTestAttribute test = method.GetCustomAttribute<VfxTestAttribute>();
                if (test == null || method.GetParameters().Length > 0) continue;

                if (!any)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);
                    any = true;
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button(test.Label))
                        foreach (Object t in targets) method.Invoke(t, null);
                }
            }

            if (any && !Application.isPlaying)
                EditorGUILayout.HelpBox("Los botones de test funcionan en Play.", MessageType.None);
        }
    }
}
