using Racoon.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Pone en la escena abierta el manager de los efectos de cámara de la partida (<see cref="MatchCameraFX"/>: llamas
    /// de la racha, vender, poco tiempo...) para ajustarlos desde la jerarquía. Sin él, se crea solo en juego con los
    /// valores por defecto.
    /// </summary>
    public static class CameraFXManagerMenu
    {
        public const string ManagerName = "MNG_EfectosCamara";

        [MenuItem("Racoon/Efectos/Añadir manager de efectos de cámara")]
        static void AddToOpenScene()
        {
            var existing = Object.FindFirstObjectByType<MatchCameraFX>(FindObjectsInactive.Include);
            if (existing)
            {
                Selection.activeObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                EditorUtility.DisplayDialog("Efectos de cámara", $"La escena ya tiene el manager: «{existing.name}».", "Vale");
                return;
            }
            Selection.activeObject = Ensure();
        }

        /// <summary>El de la escena abierta o uno nuevo (con deshacer). Devuelve null si ya había uno.</summary>
        public static GameObject Ensure()
        {
            if (Object.FindFirstObjectByType<MatchCameraFX>(FindObjectsInactive.Include)) return null;
            var go = new GameObject(ManagerName, typeof(MatchCameraFX));
            Undo.RegisterCreatedObjectUndo(go, "Añadir manager de efectos de cámara");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"Efectos de cámara: añadido «{ManagerName}» a {go.scene.name}. Guarda la escena para conservarlo.", go);
            return go;
        }
    }
}
