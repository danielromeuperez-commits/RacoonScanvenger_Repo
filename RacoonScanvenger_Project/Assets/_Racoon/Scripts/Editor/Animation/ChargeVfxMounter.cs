using Racoon.Player;
using Racoon.VFX;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Cuelga el VFX de carga del puñetazo (ChargeVFX) de la mano derecha (DEF-hand.R) en los prefabs de los tres
    /// jugadores, como hijo del hueso: así sigue a la mano en todas las animaciones y no se crea nada en partida.
    /// PlayerVFX lo encuentra solo (es hijo del jugador) y lo enciende al cargar.
    ///
    /// Se monta solo al compilar si a algún prefab le falta, y se puede relanzar desde el menú (p. ej. tras reexportar
    /// un modelo desde Blender, que vuelve a crear los huesos). Es idempotente: si ya está, solo lo recoloca.
    /// </summary>
    [InitializeOnLoad]
    public static class ChargeVfxMounter
    {
        const string HandBone = "DEF-hand.R";
        const string VfxName = "VFX_ChargePunch";
        const string MaterialPath = "Assets/_Racoon/Visuals/VFX/Player/Combat/M_VFX_ToonFlat.mat";
        // Un poco por delante de los nudillos, en el espacio del hueso (Y = a lo largo de la mano en el rig de Rigify).
        static readonly Vector3 LocalOffset = new(0f, 0.06f, 0f);

        static readonly string[] Prefabs =
        {
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRaccoon.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerOpossum.prefab",
            "Assets/_Racoon/Prefabs/Players/PF_PlayerRedPanda.prefab",
        };

        static ChargeVfxMounter() => EditorApplication.delayCall += MountIfMissing;

        static void MountIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (string path in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponentInChildren<ChargeVFX>(true) == null)
                {
                    Mount();
                    return;
                }
            }
        }

        [MenuItem("Racoon/VFX/Montar carga del puñetazo en la mano")]
        public static void Mount()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError($"[ChargeVfxMounter] No encuentro {MaterialPath}.");
                return;
            }

            foreach (string path in Prefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform hand = FindDeep(root.transform, HandBone);
                    if (hand == null)
                    {
                        Debug.LogError($"[ChargeVfxMounter] {path}: no tiene el hueso {HandBone}.");
                        continue;
                    }

                    // Si estaba en otro sitio (otro hueso, un modelo anterior), se quita y se monta en la mano.
                    foreach (ChargeVFX old in root.GetComponentsInChildren<ChargeVFX>(true))
                        if (old.transform.parent != hand) Object.DestroyImmediate(old.gameObject);

                    Transform vfx = hand.Find(VfxName);
                    if (vfx == null) vfx = new GameObject(VfxName).transform;
                    vfx.SetParent(hand, false);
                    vfx.SetLocalPositionAndRotation(LocalOffset, Quaternion.identity);
                    vfx.localScale = Vector3.one;

                    ChargeVFX charge = vfx.GetComponent<ChargeVFX>();
                    if (charge == null) charge = vfx.gameObject.AddComponent<ChargeVFX>();
                    var serialized = new SerializedObject(charge);
                    serialized.FindProperty("flatMaterial").objectReferenceValue = material;
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    // Referencia directa en PlayerVFX (también lo encontraría solo).
                    PlayerVFX playerVfx = root.GetComponentInChildren<PlayerVFX>(true);
                    if (playerVfx != null)
                    {
                        var vfxSerialized = new SerializedObject(playerVfx);
                        vfxSerialized.FindProperty("charge").objectReferenceValue = charge;
                        vfxSerialized.ApplyModifiedPropertiesWithoutUndo();
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[ChargeVfxMounter] {path}: {VfxName} en {HandBone}.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
