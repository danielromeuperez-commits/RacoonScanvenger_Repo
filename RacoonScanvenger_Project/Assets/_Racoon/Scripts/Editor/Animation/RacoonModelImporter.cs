using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Racoon.EditorTools
{
    /// <summary>
    /// Ajustes de importación del mapache con rig (SK_Mapache_Rig.fbx, exportado desde Blender):
    ///  - Rig Generic con avatar propio, blend shapes del saco y sin extraer materiales (los pone el prefab).
    ///  - Un clip por toma del FBX, con el nombre limpio («rig|Idle» → «Idle»).
    ///  - Los bucles terminan un frame antes: en Blender el último frame es igual al primero y repetido se nota.
    /// Se reaplica en cada reimportación: si cambias algo a mano en el inspector, cámbialo aquí.
    /// </summary>
    public class RacoonModelImporter : AssetPostprocessor
    {
        public const string ModelPath = "Assets/_Racoon/3D/Models/Characters/SK_Mapache_Rig.fbx";

        // Prefijo del clip → (bucle, primer frame, último frame). El orden importa: Knockback_Idle antes que Knockback.
        static readonly (string prefix, bool loop, int first, int last)[] Clips =
        {
            ("Knockback_Idle", true, 0, 89),
            ("Knockback", false, 0, 61),
            ("Idle", true, 0, 60),
            ("Walk", true, 0, 60),
            ("Run", true, 0, 35),
            ("Roll", false, 0, 34),
            ("Hit", false, 0, 27),
            ("GetUp", false, 1, 60),
            ("Punch_Upper", false, 0, 61),
            ("Throw_Upper", false, 0, 61),
            ("Grab_Upper", false, 0, 61),
        };

        void OnPreprocessModel()
        {
            if (assetPath != ModelPath) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.resampleCurves = true;
        }

        void OnPreprocessAnimation()
        {
            if (assetPath != ModelPath) return;
            var importer = (ModelImporter)assetImporter;

            var clips = new List<ModelImporterClipAnimation>();
            foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
            {
                // Blender añade «.001» si ya había una tira NLA con ese nombre (p. ej. los stash de las acciones).
                string name = Regex.Replace(take.takeName.Substring(take.takeName.LastIndexOf('|') + 1), @"\.\d{3}$", "");
                if (!TryGetSettings(name, out bool loop, out int first, out int last))
                {
                    Debug.LogWarning($"[RacoonModelImporter] Toma sin ajustes, se importa tal cual: {take.takeName}");
                    clips.Add(take);
                    continue;
                }
                take.name = name;
                take.loopTime = loop;
                take.loopPose = false;
                take.firstFrame = first;
                take.lastFrame = last;
                clips.Add(take);
            }
            importer.clipAnimations = clips.ToArray();
        }

        static bool TryGetSettings(string clipName, out bool loop, out int first, out int last)
        {
            foreach (var clip in Clips)
            {
                if (!clipName.StartsWith(clip.prefix)) continue;
                loop = clip.loop;
                first = clip.first;
                last = clip.last;
                return true;
            }
            loop = false;
            first = last = 0;
            return false;
        }
    }
}
