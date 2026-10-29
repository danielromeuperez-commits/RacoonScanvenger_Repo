using System.Collections.Generic;
using Racoon.Player;
using Racoon.UI.HUD;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Monta las cámaras del retrato en vivo (<see cref="HUDPortraitCamera"/>) en cada prefab de jugador de
    /// Prefabs/Players: un hijo «Cámaras retrato (HUD)» con «Cámara P1» y «Cámara P2» delante del personaje, mirándolo
    /// a tres cuartos hacia el centro de la pantalla (P1 hacia la derecha, P2 hacia la izquierda), apagadas.
    /// Los prefabs que ya tienen las dos no se tocan; la cámara única de antes se sustituye partiendo de su posición.
    /// Luego se colocan y se ajustan a mano en el prefab.
    /// </summary>
    public static class HUDPortraitCameraMenu
    {
        const string PlayersDir = "Assets/_Racoon/Prefabs/Players";
        const string RigName = "Cámaras retrato (HUD)";
        const float FieldOfView = 30f;
        // Encuadre de partida: el cuerpo entero, mirando a media altura desde delante y girada a tres cuartos
        const float LookHeight = .45f, Framing = 1.1f, Yaw = 25f;

        [MenuItem("Racoon/HUD/Montar cámaras de retrato en los jugadores")]
        static void Build()
        {
            var added = new List<string>();
            var skipped = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayersDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset || !asset.GetComponent<PlayerController>()) continue;
                var existing = asset.GetComponentInChildren<HUDPortraitCamera>(true);
                if (existing && existing.cameraP1 && existing.cameraP2)
                {
                    skipped.Add(asset.name);
                    continue;
                }
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    AddCameras(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    added.Add(asset.name);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            string message = added.Count > 0 ? $"Cámaras de retrato montadas en: {string.Join(", ", added)}." : "No he montado ninguna cámara nueva.";
            if (skipped.Count > 0) message += $"\nYa las tenían: {string.Join(", ", skipped)}.";
            message += $"\n\nAbre el prefab y ajusta «{RigName}/Cámara P1» y «Cámara P2» (posición, giro y Field of View): " +
                       "la de P1 se ve en el círculo de la izquierda y la de P2 en el de la derecha.";
            Debug.Log("HUD: " + message.Replace("\n", " "));
            EditorUtility.DisplayDialog("Cámaras de retrato", message, "Vale");
        }

        static void AddCameras(GameObject root)
        {
            // Posición de partida: la de la cámara única de antes (si la habías colocado) o la calculada con la altura
            Vector3 position;
            Quaternion rotation;
            float fov = FieldOfView;
            var old = root.GetComponentInChildren<HUDPortraitCamera>(true);
            var oldCamera = old ? old.GetComponent<Camera>() : null;
            if (oldCamera)
            {
                position = root.transform.InverseTransformPoint(oldCamera.transform.position);
                rotation = Quaternion.Inverse(root.transform.rotation) * oldCamera.transform.rotation;
                fov = oldCamera.fieldOfView;
            }
            else
            {
                float height = Height(root);
                float distance = height * Framing * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);
                position = new Vector3(0f, height * LookHeight, distance);
                rotation = Quaternion.Euler(0f, 180f, 0f); // delante del personaje, mirándolo
            }
            if (old) Object.DestroyImmediate(old.gameObject);

            var rig = new GameObject(RigName);
            rig.transform.SetParent(root.transform, false);
            var portraits = rig.AddComponent<HUDPortraitCamera>();
            // P1 (izquierda) se abre hacia la derecha de la pantalla y P2 hacia la izquierda: orbitan en vertical
            portraits.cameraP1 = AddCamera(rig.transform, "Cámara P1", position, rotation, Yaw, fov);
            portraits.cameraP2 = AddCamera(rig.transform, "Cámara P2", position, rotation, -Yaw, fov);
        }

        static Camera AddCamera(Transform parent, string name, Vector3 position, Quaternion rotation, float yaw, float fov)
        {
            var orbit = Quaternion.Euler(0f, yaw, 0f);
            var pivot = new Vector3(0f, position.y, 0f);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pivot + orbit * (position - pivot);
            go.transform.localRotation = orbit * rotation;
            var camera = go.AddComponent<Camera>();
            camera.enabled = false; // la enciende el HUD con su RenderTexture
            camera.fieldOfView = fov;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 20f;
            camera.depth = -50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 0;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            return camera;
        }

        // Altura del personaje con sus mallas (sin partículas)
        static float Height(GameObject root)
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            var model = animator ? animator.transform : root.transform;
            float top = float.MinValue;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                if (renderer is SkinnedMeshRenderer or MeshRenderer) top = Mathf.Max(top, renderer.bounds.max.y);
            return top > float.MinValue ? Mathf.Max(.3f, top - root.transform.position.y) : 1f;
        }
    }
}
