using System.Collections.Generic;
using System.Linq;
using Racoon.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Monta ACO_Racoon a partir de los clips de SK_Mapache_Rig.fbx y pone el modelo con rig en PF_PlayerRaccoon.
    ///
    /// Capas:
    ///  - Base: locomoción (blend Speed × SackSize) y acciones de cuerpo entero (Dash → Roll, HitStun → Hit,
    ///    Knockdown/Downed → Knockback → Knockback_Idle → GetUp). Todo con variantes de saco pequeño/normal/grande.
    ///  - UpperBody (máscara del pecho hacia arriba): Punch, UseItem (Throw) e Interact (Grab), que así se pueden
    ///    hacer andando o corriendo.
    /// Es idempotente: se puede volver a lanzar tras reexportar desde Blender. Mantiene el GUID del controller.
    /// </summary>
    public static class RacoonAnimatorBuilder
    {
        const string ControllerPath = "Assets/_Racoon/3D/Animations/Racoon/ACO_Racoon.controller";
        const string UpperMaskPath = "Assets/_Racoon/3D/Animations/Racoon/AM_Racoon_UpperBody.mask";
        const string PrefabPath = "Assets/_Racoon/Prefabs/Players/PF_PlayerRaccoon.prefab";
        const string OldModelPath = "Assets/_Racoon/3D/Models/Characters/SK_Mapache.fbx";

        // Hueso a partir del cual todo es tren superior (pecho, brazos, cabeza, orejas, bigotes, saco).
        const string UpperBodyRootBone = "DEF-spine.002";

        // Velocidad de andar / correr del PlayerController: el clip de andar se coloca en walkSpeed / runSpeed.
        const float WalkThreshold = 3.5f / 6.5f;

        static readonly string[] SackSuffixes = { "_SmallSack", "", "_BigSack" };
        static readonly float[] SackThresholds = { 0f, 0.5f, 1f };

        [MenuItem("Racoon/Animación/Montar Animator del mapache")]
        public static void Build()
        {
            AssetDatabase.ImportAsset(RacoonModelImporter.ModelPath, ImportAssetOptions.ForceUpdate);
            Dictionary<string, AnimationClip> clips = LoadClips();
            if (clips == null) return;

            AvatarMask upperMask = BuildUpperMask();
            AnimatorController controller = BuildController(clips, upperMask);
            SetupPrefab(controller);

            AssetDatabase.SaveAssets();
            Debug.Log($"[RacoonAnimatorBuilder] Listo: {clips.Count} clips, controller y prefab actualizados.");
        }

        /// <summary>Para -executeMethod en batchmode.</summary>
        public static void BuildFromCommandLine()
        {
            Build();
            EditorApplication.Exit(0);
        }

        // ---------------- Clips ----------------

        static Dictionary<string, AnimationClip> LoadClips()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(RacoonModelImporter.ModelPath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToDictionary(c => c.name);
            if (clips.Count == 0)
            {
                Debug.LogError($"[RacoonAnimatorBuilder] No hay clips en {RacoonModelImporter.ModelPath}.");
                return null;
            }
            return clips;
        }

        static AnimationClip Clip(Dictionary<string, AnimationClip> clips, string name)
        {
            if (clips.TryGetValue(name, out AnimationClip clip)) return clip;
            Debug.LogError($"[RacoonAnimatorBuilder] Falta el clip «{name}» en el FBX.");
            return null;
        }

        // ---------------- Máscara ----------------

        static AvatarMask BuildUpperMask()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RacoonModelImporter.ModelPath);
            var mask = new AvatarMask();
            mask.AddTransformPath(model.transform, true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                mask.SetTransformActive(i, path.Contains("/" + UpperBodyRootBone));
            }

            AssetDatabase.DeleteAsset(UpperMaskPath);
            AssetDatabase.CreateAsset(mask, UpperMaskPath);
            return mask;
        }

        // ---------------- Controller ----------------

        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, AvatarMask upperMask)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // Vaciar sin borrar el asset (las referencias al controller siguen valiendo).
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != controller) Object.DestroyImmediate(sub, true);
            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];

            controller.AddParameter("State", AnimatorControllerParameterType.Int);
            controller.AddParameter("ActionStart", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("BodyAction", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("SackSize", AnimatorControllerParameterType.Float);

            BuildBaseLayer(controller, clips);
            BuildUpperLayer(controller, clips, upperMask);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void BuildBaseLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips)
        {
            controller.AddLayer("Base Layer");
            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // Locomoción: Speed (quieto / andar / correr) y dentro de cada uno el tamaño del saco.
            BlendTree locomotionTree = NewTree(controller, "Locomotion", "Speed");
            locomotionTree.AddChild(SackTree(controller, clips, "Idle"), 0f);
            locomotionTree.AddChild(SackTree(controller, clips, "Walk"), WalkThreshold);
            locomotionTree.AddChild(SackTree(controller, clips, "Run"), 1f);

            AnimatorState locomotion = AddState(sm, "Locomotion", locomotionTree, new Vector3(300, 0));
            AnimatorState roll = AddState(sm, "Roll", SackTree(controller, clips, "Roll"), new Vector3(300, 120), 1.4f);
            AnimatorState hit = AddState(sm, "Hit", SackTree(controller, clips, "Hit"), new Vector3(300, -120));
            AnimatorState knockback = AddState(sm, "Knockback", SackTree(controller, clips, "Knockback"), new Vector3(600, 120));
            AnimatorState knockbackIdle = AddState(sm, "Knockback_Idle", SackTree(controller, clips, "Knockback_Idle"), new Vector3(850, 120));
            AnimatorState getUp = AddState(sm, "GetUp", SackTree(controller, clips, "GetUp"), new Vector3(850, 0), 1.3f);
            sm.defaultState = locomotion;

            // Entradas desde cualquier estado: trigger BodyAction + el estado que lo ha lanzado.
            AnyStateTo(sm, roll, "BodyAction", PlayerState.Dash, 0.05f);
            AnyStateTo(sm, hit, "BodyAction", PlayerState.HitStun, 0.05f);
            AnyStateTo(sm, knockback, "BodyAction", PlayerState.Knockdown, 0.05f);
            AnyStateTo(sm, knockback, "BodyAction", PlayerState.Downed, 0.05f);

            ExitTimeTo(roll, locomotion, 0.8f, 0.15f);
            ExitTimeTo(hit, locomotion, 0.85f, 0.12f);
            ExitTimeTo(knockback, knockbackIdle, 0.55f, 0.2f);

            // Se levanta en cuanto deja de estar en el suelo (fin del derribo o ha mantenido Dash).
            foreach (AnimatorState from in new[] { knockback, knockbackIdle })
            {
                AnimatorStateTransition t = from.AddTransition(getUp);
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.AddCondition(AnimatorConditionMode.NotEqual, (int)PlayerState.Knockdown, "State");
                t.AddCondition(AnimatorConditionMode.NotEqual, (int)PlayerState.Downed, "State");
            }

            ExitTimeTo(getUp, locomotion, 0.85f, 0.2f);
            // Si ya se está moviendo, sale antes para no patinar.
            AnimatorStateTransition move = getUp.AddTransition(locomotion);
            move.hasExitTime = true;
            move.exitTime = 0.4f;
            move.duration = 0.25f;
            move.AddCondition(AnimatorConditionMode.Greater, 0.3f, "Speed");
        }

        static void BuildUpperLayer(AnimatorController controller, Dictionary<string, AnimationClip> clips, AvatarMask mask)
        {
            var stateMachine = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "UpperBody",
                stateMachine = stateMachine,
                avatarMask = mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                // El peso lo sube PlayerAnimatorDriver solo durante las acciones: con peso 1 el estado vacío
                // (sin Write Defaults) congelaría el torso en la última pose.
                defaultWeight = 0f,
            });

            // Sin motion: mientras está aquí el driver deja la capa a peso 0 y manda la locomoción de la base.
            AnimatorState empty = AddState(stateMachine, "Empty", null, new Vector3(300, 0));
            stateMachine.defaultState = empty;

            (string clip, PlayerState state, float speed, Vector3 position)[] actions =
            {
                ("Punch_Upper", PlayerState.Punch, 1.6f, new Vector3(600, -120)),
                ("Throw_Upper", PlayerState.UseItem, 1.4f, new Vector3(600, 0)),
                ("Grab_Upper", PlayerState.Interact, 1.8f, new Vector3(600, 120)),
            };
            foreach (var action in actions)
            {
                AnimatorState state = AddState(stateMachine, action.clip, Clip(clips, action.clip), action.position, action.speed);
                AnyStateTo(stateMachine, state, "ActionStart", action.state, 0.05f);
                ExitTimeTo(state, empty, 0.9f, 0.15f);
            }

            // Un golpe recibido o un dash cortan la acción: el cuerpo entero lo anima la capa base.
            AnimatorStateTransition cancel = stateMachine.AddAnyStateTransition(empty);
            cancel.hasExitTime = false;
            cancel.duration = 0.1f;
            cancel.canTransitionToSelf = false;
            cancel.AddCondition(AnimatorConditionMode.Greater, (int)PlayerState.UseItem, "State");
        }

        static BlendTree NewTree(AnimatorController controller, string name, string parameter)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = BlendTreeType.Simple1D,
                blendParameter = parameter,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        /// <summary>Saco pequeño (cola natural) / normal / grande (cola apartada), según SackSize.</summary>
        static BlendTree SackTree(AnimatorController controller, Dictionary<string, AnimationClip> clips, string baseName)
        {
            BlendTree tree = NewTree(controller, baseName, "SackSize");
            for (int i = 0; i < SackSuffixes.Length; i++)
                tree.AddChild(Clip(clips, baseName + SackSuffixes[i]), SackThresholds[i]);
            return tree;
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position, float speed = 1f)
        {
            AnimatorState state = sm.AddState(name, position);
            state.motion = motion;
            state.speed = speed;
            // Sin Write Defaults: el estado vacío de la capa superior no debe escribir la pose de reposo.
            state.writeDefaultValues = false;
            return state;
        }

        static void AnyStateTo(AnimatorStateMachine sm, AnimatorState target, string trigger, PlayerState state, float duration)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(target);
            t.hasExitTime = false;
            t.duration = duration;
            t.canTransitionToSelf = true;
            t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            t.AddCondition(AnimatorConditionMode.Equals, (int)state, "State");
        }

        static void ExitTimeTo(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
        }

        // ---------------- Prefab ----------------

        static void SetupPrefab(AnimatorController controller)
        {
            var rigModel = AssetDatabase.LoadAssetAtPath<GameObject>(RacoonModelImporter.ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(RacoonModelImporter.ModelPath).OfType<Avatar>().FirstOrDefault();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform oldModel = FindModelInstance(root.transform, OldModelPath);
                Transform model = FindModelInstance(root.transform, RacoonModelImporter.ModelPath);

                if (model == null)
                {
                    if (oldModel == null)
                    {
                        Debug.LogError("[RacoonAnimatorBuilder] El prefab no tiene ni SK_Mapache ni SK_Mapache_Rig.");
                        return;
                    }
                    model = ((GameObject)PrefabUtility.InstantiatePrefab(rigModel, root.transform)).transform;
                    model.SetSiblingIndex(oldModel.GetSiblingIndex());
                    FitToOldModel(model, oldModel);

                    Material material = oldModel.GetComponentsInChildren<Renderer>(true)
                        .Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
                    if (material != null)
                        foreach (Renderer rend in model.GetComponentsInChildren<Renderer>(true))
                            rend.sharedMaterials = Enumerable.Repeat(material, rend.sharedMaterials.Length).ToArray();

                    Object.DestroyImmediate(oldModel.gameObject);
                }
                else
                {
                    FaceForward(model);
                }
                CenterOnFeet(model);

                if (!model.TryGetComponent(out Animator animator)) animator = model.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.avatar = avatar;
                animator.applyRootMotion = false;
                // En red el rival también tiene que animarse aunque no salga en la cámara de este cliente.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                var driver = root.GetComponentInChildren<PlayerAnimatorDriver>(true);
                if (driver != null)
                {
                    var serialized = new SerializedObject(driver);
                    serialized.FindProperty("animator").objectReferenceValue = animator;
                    serialized.FindProperty("loot").objectReferenceValue = root.GetComponentInChildren<PlayerLoot>(true);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform FindModelInstance(Transform root, string modelPath)
        {
            foreach (Transform child in root)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject);
                if (source != null && AssetDatabase.GetAssetPath(source) == modelPath) return child;
            }
            return null;
        }

        /// <summary>Misma posición, altura y pies que el modelo viejo, y mirando hacia +Z (el forward del jugador).</summary>
        static void FitToOldModel(Transform model, Transform oldModel)
        {
            model.localPosition = oldModel.localPosition;
            model.localScale = Vector3.one;
            FaceForward(model);

            Bounds oldBounds = WorldBounds(oldModel);
            Bounds newBounds = WorldBounds(model);
            if (oldBounds.size.y > 0f && newBounds.size.y > 0f)
            {
                float ratio = oldBounds.size.y / newBounds.size.y;
                if (Mathf.Abs(ratio - 1f) > 0.05f) model.localScale = Vector3.one * ratio;
                newBounds = WorldBounds(model);
                model.position += Vector3.up * (oldBounds.min.y - newBounds.min.y);
            }
        }

        /// <summary>
        /// Gira el modelo para que mire hacia +Z (el forward del jugador). La dirección sale de la cabeza a los bigotes
        /// y se redondea a 90°: el FBX de Blender siempre llega girado un múltiplo de 90°.
        /// </summary>
        static void FaceForward(Transform model)
        {
            model.localRotation = Quaternion.identity;
            Transform head = FindDeep(model, "DEF-spine.006");
            Transform whiskerL = FindDeep(model, "DEF-whisker.001.L");
            Transform whiskerR = FindDeep(model, "DEF-whisker.001.R");
            if (head == null || whiskerL == null || whiskerR == null) return;

            Vector3 facing = (whiskerL.position + whiskerR.position) * 0.5f - head.position;
            facing = model.parent.InverseTransformDirection(facing);
            float yaw = Vector3.SignedAngle(new Vector3(facing.x, 0f, facing.z), Vector3.forward, Vector3.up);
            model.localRotation = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        }

        /// <summary>Desplaza el modelo en horizontal para que el pivote del jugador quede justo entre los dos pies.</summary>
        static void CenterOnFeet(Transform model)
        {
            string[] feet = { "DEF-foot.L", "DEF-foot.R", "DEF-toe.L", "DEF-toe.R" };
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (string name in feet)
            {
                Transform bone = FindDeep(model, name);
                if (bone == null) continue;
                sum += model.parent.InverseTransformPoint(bone.position);
                count++;
            }
            if (count == 0) return;
            Vector3 middle = sum / count;
            model.localPosition -= new Vector3(middle.x, 0f, middle.z);
        }

        static Bounds WorldBounds(Transform t)
        {
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(t.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer rend in renderers) bounds.Encapsulate(rend.bounds);
            return bounds;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
