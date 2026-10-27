using System.Linq;
using Racoon.Player;
using Racoon.VFX;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Pone los modelos con rig de la zarigüeya y el panda rojo en sus prefabs de jugador.
    /// Comparten el rig del mapache, así que usan ACO_Racoon tal cual y se colocan igual que SK_Mapache_Rig en
    /// PF_PlayerRaccoon (misma posición, giro y escala; VFX_Tired colgado del mismo hueso).
    /// Es idempotente: se puede volver a lanzar tras reexportar desde Blender.
    /// </summary>
    public static class CharacterSkinBuilder
    {
        const string ControllerPath = "Assets/_Racoon/3D/Animations/Racoon/ACO_Racoon.controller";
        const string RaccoonPrefabPath = "Assets/_Racoon/Prefabs/Players/PF_PlayerRaccoon.prefab";
        const string TiredVfxName = "VFX_Tired";

        // Modelos provisionales que había en los prefabs antes del rig.
        static readonly string[] OldModelNames = { "SM_RedTanuki", "SK_Opossum" };

        static readonly (string prefab, string model, string material)[] Skins =
        {
            ("Assets/_Racoon/Prefabs/Players/PF_PlayerOpossum.prefab", RacoonModelImporter.OpossumModelPath,
                "Assets/_Racoon/3D/Materials/Basics/M_GreenShaderTest.mat"),
            ("Assets/_Racoon/Prefabs/Players/PF_PlayerRedPanda.prefab", RacoonModelImporter.RedPandaModelPath,
                "Assets/_Racoon/3D/Materials/Basics/M_RedShaderTest.mat"),
        };

        [MenuItem("Racoon/Animación/Montar zarigüeya y panda rojo")]
        public static void Build()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[CharacterSkinBuilder] No existe {ControllerPath}: lanza antes «Montar Animator del mapache».");
                return;
            }

            GameObject raccoon = PrefabUtility.LoadPrefabContents(RaccoonPrefabPath);
            try
            {
                Transform reference = RacoonAnimatorBuilder.FindModelInstance(raccoon.transform, RacoonModelImporter.ModelPath);
                if (reference == null)
                {
                    Debug.LogError("[CharacterSkinBuilder] PF_PlayerRaccoon no tiene SK_Mapache_Rig.");
                    return;
                }
                Transform referenceTired = RacoonAnimatorBuilder.FindDeep(raccoon.transform, TiredVfxName);

                foreach (var skin in Skins)
                {
                    AssetDatabase.ImportAsset(skin.model, ImportAssetOptions.ForceUpdate);
                    SetupPrefab(skin.prefab, skin.model, skin.material, controller, reference, referenceTired);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(raccoon);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterSkinBuilder] Listo: {Skins.Length} prefabs actualizados.");
        }

        static void SetupPrefab(string prefabPath, string modelPath, string materialPath, AnimatorController controller,
            Transform reference, Transform referenceTired)
        {
            var rigModel = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (rigModel == null)
            {
                Debug.LogError($"[CharacterSkinBuilder] No se encuentra {modelPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform model = RacoonAnimatorBuilder.FindModelInstance(root.transform, modelPath);
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    if (child != model && OldModelNames.Contains(child.name))
                        Object.DestroyImmediate(child.gameObject);

                if (model == null)
                {
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(rigModel, root.transform)).transform;
                    model.SetSiblingIndex(reference.GetSiblingIndex());
                }
                model.localPosition = reference.localPosition;
                model.localRotation = reference.localRotation;
                model.localScale = reference.localScale;

                if (material != null)
                    foreach (Renderer rend in model.GetComponentsInChildren<Renderer>(true))
                        rend.sharedMaterials = Enumerable.Repeat(material, rend.sharedMaterials.Length).ToArray();

                if (!model.TryGetComponent(out Animator animator)) animator = model.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var driver = root.GetComponentInChildren<PlayerAnimatorDriver>(true);
                if (driver != null)
                {
                    var serialized = new SerializedObject(driver);
                    serialized.FindProperty("animator").objectReferenceValue = animator;
                    serialized.FindProperty("loot").objectReferenceValue = root.GetComponentInChildren<PlayerLoot>(true);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                // La estela del dash se recorta con la silueta del cuerpo.
                var body = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                foreach (WindTrailVFX wind in root.GetComponentsInChildren<WindTrailVFX>(true))
                {
                    var serialized = new SerializedObject(wind);
                    serialized.FindProperty("bodyRenderer").objectReferenceValue = body;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                AttachLikeReference(root.transform, model, referenceTired);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Cuelga el VFX del mismo hueso y con la misma pose local que en el mapache.</summary>
        static void AttachLikeReference(Transform root, Transform model, Transform referenceVfx)
        {
            if (referenceVfx == null || referenceVfx.parent == null) return;
            Transform vfx = RacoonAnimatorBuilder.FindDeep(root, referenceVfx.name);
            Transform bone = RacoonAnimatorBuilder.FindDeep(model, referenceVfx.parent.name);
            if (vfx == null || bone == null) return;

            vfx.SetParent(bone, false);
            vfx.localPosition = referenceVfx.localPosition;
            vfx.localRotation = referenceVfx.localRotation;
            vfx.localScale = referenceVfx.localScale;
        }
    }
}
