// MetaHuman Bridge - the import pipeline.
//
// Takes the .dna files and textures from a MetaHuman Creator "DCC Export" and produces a
// Unity prefab: one SkinnedMeshRenderer per DNA mesh, a shared skeleton, optional Humanoid
// avatar, optional LODGroup, and (for the head) a baked RigLogicAsset driving MetaHumanRig.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public sealed class MetaHumanImportReport
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public GameObject Prefab;
        public string PrefabPath;
        public bool Succeeded => Prefab != null;

        public void Info(string message) => Log.Add(message);
        public void Warn(string message) => Warnings.Add(message);
    }

    public static class MetaHumanImporter
    {
        public static MetaHumanImportReport Import(MetaHumanImportSettings settings)
        {
            var report = new MetaHumanImportReport();
            GameObject root = null;

            try
            {
                Validate(settings);

                string characterFolder = EnsureFolder(settings.CharacterFolder);
                string meshFolder = EnsureFolder(characterFolder + "/Meshes");
                string materialFolder = EnsureFolder(characterFolder + "/Materials");
                string textureFolder = EnsureFolder(characterFolder + "/Textures");

                root = new GameObject(settings.SafeName);
                var sharedJoints = new Dictionary<string, Transform>(StringComparer.Ordinal);

                List<string> texturePaths = settings.createMaterials
                    ? MetaHumanMaterialBuilder.CollectTextures(settings.textureFolder, textureFolder, settings.copyTextures)
                    : new List<string>();
                if (texturePaths.Count > 0)
                {
                    MetaHumanMaterialBuilder.ConfigureTextureImporters(texturePaths);
                    report.Info($"Found {texturePaths.Count} textures.");
                }
                else if (settings.createMaterials)
                {
                    report.Warn("No textures found - materials will be created untextured.");
                }

                var lodRenderers = new Dictionary<int, List<Renderer>>();
                RigLogicAsset rigLogicAsset = null;
                var blendShapeBindings = new List<MetaHumanBlendShapeBinding>();
                Transform[] headJoints = null;

                // Body first: it owns the shared skeleton the head hangs off.
                var bodyOwnedJoints = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(settings.bodyDnaPath))
                {
                    DnaFile body = LoadDna(settings.bodyDnaPath, settings, report, "body");
                    ImportPart(body, settings, root, sharedJoints, texturePaths, meshFolder, materialFolder,
                        lodRenderers, report, out _, out _);

                    // Everything the body created is body-owned: the Animator drives these, so the
                    // face solver must not write to them. See faceRigDrivesBodyJoints.
                    bodyOwnedJoints.UnionWith(sharedJoints.Keys);
                }

                if (!string.IsNullOrEmpty(settings.headDnaPath))
                {
                    DnaFile head = LoadDna(settings.headDnaPath, settings, report, "head");
                    ImportPart(head, settings, root, sharedJoints, texturePaths, meshFolder, materialFolder,
                        lodRenderers, report, out headJoints, out blendShapeBindings);

                    if (settings.buildRigLogic)
                    {
                        rigLogicAsset = RigLogicAssetBuilder.Build(head, settings.SafeName, settings.scale);
                        AssetDatabase.CreateAsset(rigLogicAsset,
                            AssetDatabase.GenerateUniqueAssetPath($"{characterFolder}/{settings.SafeName}_RigLogic.asset"));

                        report.Info(
                            $"Baked RigLogic: {rigLogicAsset.guiControlNames.Length} GUI controls, " +
                            $"{rigLogicAsset.rawControlNames.Length} raw controls, {rigLogicAsset.psdCount} correctives, " +
                            $"{rigLogicAsset.jointGroups.Count} joint groups.");

                        if (rigLogicAsset.unevaluatedLayers.Length > 0)
                        {
                            report.Warn(
                                "This DNA carries behaviour layers the solver does not evaluate: " +
                                string.Join(", ", rigLogicAsset.unevaluatedLayers) +
                                ". Expect slightly softer correctives than Unreal.");
                        }
                    }
                }

                if (settings.createLodGroup && lodRenderers.Count > 1)
                    BuildLodGroup(root, lodRenderers, report);

                if (settings.buildHumanoidAvatar)
                {
                    Avatar avatar = HumanoidAvatarBuilder.Build(root, sharedJoints, out string failureReason);
                    if (avatar != null)
                    {
                        AssetDatabase.CreateAsset(avatar,
                            AssetDatabase.GenerateUniqueAssetPath($"{characterFolder}/{settings.SafeName}_Avatar.asset"));
                        var animator = root.AddComponent<Animator>();
                        animator.avatar = avatar;
                        report.Info("Built a Humanoid avatar.");
                    }
                    else
                    {
                        report.Warn(failureReason);
                    }
                }

                if (rigLogicAsset != null && headJoints != null)
                {
                    var rig = root.AddComponent<MetaHumanRig>();

                    // A stock MetaHuman face DNA nominally drives every one of its joints, including
                    // the 27 it shares with the body. Left connected, the solver would stamp the
                    // neutral pose onto the arms, neck and head after the Animator has run.
                    string[] rigJointNames = rigLogicAsset.jointNames;
                    var ordered = new Transform[rigJointNames.Length];
                    int suppressed = 0;
                    int unresolved = 0;

                    for (int i = 0; i < rigJointNames.Length; i++)
                    {
                        string name = rigJointNames[i];

                        if (!settings.faceRigDrivesBodyJoints && bodyOwnedJoints.Contains(name))
                        {
                            suppressed++;
                            continue;
                        }

                        if (sharedJoints.TryGetValue(name, out Transform t)) ordered[i] = t;
                        else unresolved++;
                    }

                    rig.Configure(rigLogicAsset, ordered, blendShapeBindings);

                    if (suppressed > 0)
                        report.Info(
                            $"The face rig will not write to {suppressed} body-owned joints " +
                            "(head, neck, clavicles, upper arms and their correctives), so body " +
                            "animation keeps control of them. Enable 'Face rig drives body joints' to change that.");

                    if (unresolved > 0)
                        report.Warn($"{unresolved} rig joints had no matching transform and will not animate.");
                }

                string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{characterFolder}/{settings.SafeName}.prefab");
                report.Prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                report.PrefabPath = prefabPath;

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                report.Info($"Saved prefab to {prefabPath}.");
            }
            catch (Exception e)
            {
                report.Warn($"Import failed: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }

            return report;
        }

        // ------------------------------------------------------------------ setup

        static void Validate(MetaHumanImportSettings settings)
        {
            if (string.IsNullOrEmpty(settings.bodyDnaPath) && string.IsNullOrEmpty(settings.headDnaPath))
                throw new InvalidOperationException("Select at least one .dna file (head, body, or both).");

            foreach (string path in new[] { settings.bodyDnaPath, settings.headDnaPath })
                if (!string.IsNullOrEmpty(path) && !File.Exists(path))
                    throw new FileNotFoundException($"DNA file not found: {path}");

            if (!settings.outputFolder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The output folder must be inside Assets/.");

            if (settings.lods == null || settings.lods.Count == 0)
                settings.lods = new List<int> { 0 };
        }

        static string EnsureFolder(string projectRelativePath)
        {
            string[] parts = projectRelativePath.Replace('\\', '/').TrimEnd('/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
            return current;
        }

        static DnaFile LoadDna(string path, MetaHumanImportSettings settings, MetaHumanImportReport report, string label)
        {
            EditorUtility.DisplayProgressBar("MetaHuman Bridge", $"Reading {label} DNA...", 0.05f);
            byte[] bytes = File.ReadAllBytes(path);

            // First pass without geometry so the LOD -> mesh mapping is known before decoding meshes.
            DnaFile probe = DnaBinaryReader.Parse(bytes, new DnaReadOptions { ReadGeometry = false });

            var filter = new HashSet<int>();
            foreach (int lod in settings.lods)
                foreach (ushort index in probe.Definition.LodMeshMapping.GetIndices(lod))
                    filter.Add(index);

            if (filter.Count == 0)
            {
                report.Warn($"The {label} DNA has no meshes for the requested LODs; importing every mesh instead.");
                filter = null;
            }

            var options = new DnaReadOptions
            {
                MeshIndexFilter = filter,
                ReadBlendShapes = settings.blendShapeMode != BlendShapeImportMode.None,
                Progress = (stage, t) => EditorUtility.DisplayProgressBar("MetaHuman Bridge", $"{label}: {stage}", t)
            };

            DnaFile dna = DnaBinaryReader.Parse(bytes, options);
            dna.SourcePath = path;

            report.Info(
                $"{label} DNA {dna.FileGeneration}.{dna.FileVersion} - " +
                $"{dna.Definition.JointCount} joints, {dna.Definition.MeshNames.Length} meshes, " +
                $"{dna.Descriptor.LodCount} LODs, {dna.Definition.BlendShapeChannelNames.Length} blend shape channels.");

            return dna;
        }

        // ------------------------------------------------------------------ per-DNA import

        static void ImportPart(
            DnaFile dna,
            MetaHumanImportSettings settings,
            GameObject root,
            Dictionary<string, Transform> sharedJoints,
            List<string> texturePaths,
            string meshFolder,
            string materialFolder,
            Dictionary<int, List<Renderer>> lodRenderers,
            MetaHumanImportReport report,
            out Transform[] joints,
            out List<MetaHumanBlendShapeBinding> bindings)
        {
            DnaSpace space = DnaSpace.FromDescriptor(dna.Descriptor, settings.scale);
            joints = BuildSkeleton(dna, root.transform, space, sharedJoints);
            bindings = new List<MetaHumanBlendShapeBinding>();

            var bindPoses = new Matrix4x4[joints.Length];
            Matrix4x4 rootToWorld = root.transform.localToWorldMatrix;
            for (int i = 0; i < joints.Length; i++)
                bindPoses[i] = joints[i] != null
                    ? joints[i].worldToLocalMatrix * rootToWorld
                    : Matrix4x4.identity;

            var identityMap = new int[joints.Length];
            for (int i = 0; i < identityMap.Length; i++) identityMap[i] = i;

            Dictionary<int, int> meshToLod = BuildMeshLodMap(dna, settings.lods);
            int channelCount = dna.Definition.BlendShapeChannelNames.Length;

            for (int meshIndex = 0; meshIndex < dna.Geometry.Meshes.Length; meshIndex++)
            {
                DnaMesh source = dna.Geometry.Meshes[meshIndex];
                if (source.LayoutCount == 0) continue;   // skipped by the LOD filter

                EditorUtility.DisplayProgressBar("MetaHuman Bridge", $"Building {source.Name}...",
                    0.6f + 0.35f * (meshIndex / (float)Math.Max(1, dna.Geometry.Meshes.Length)));

                DnaMeshBuildResult built;
                try
                {
                    built = DnaMeshBuilder.Build(source, dna, space, settings.blendShapeMode, settings.flipV,
                        bindPoses, identityMap);
                }
                catch (Exception e)
                {
                    report.Warn($"Skipped mesh '{source.Name}': {e.Message}");
                    continue;
                }

                AssetDatabase.CreateAsset(built.Mesh,
                    AssetDatabase.GenerateUniqueAssetPath($"{meshFolder}/{Sanitize(source.Name)}.asset"));

                var go = new GameObject(source.Name);
                go.transform.SetParent(root.transform, false);

                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = built.Mesh;
                renderer.bones = joints;
                renderer.rootBone = joints.Length > 0 ? joints[0] : root.transform;
                renderer.localBounds = built.Mesh.bounds;
                renderer.updateWhenOffscreen = false;

                if (settings.createMaterials)
                    renderer.sharedMaterial = MetaHumanMaterialBuilder.CreateMaterial(
                        Sanitize(source.Name), texturePaths, materialFolder);

                int lod = meshToLod.TryGetValue(meshIndex, out int value) ? value : 0;
                if (!lodRenderers.TryGetValue(lod, out List<Renderer> list))
                    lodRenderers[lod] = list = new List<Renderer>();
                list.Add(renderer);

                if (built.BlendShapeChannelIndices.Length > 0)
                {
                    var map = new int[channelCount];
                    for (int i = 0; i < map.Length; i++) map[i] = -1;
                    for (int shape = 0; shape < built.BlendShapeChannelIndices.Length; shape++)
                    {
                        int channel = built.BlendShapeChannelIndices[shape];
                        if (channel >= 0 && channel < map.Length) map[channel] = shape;
                    }

                    bindings.Add(new MetaHumanBlendShapeBinding { renderer = renderer, channelToShapeIndex = map });
                }

                if (built.SkippedBlendShapes > 0)
                    report.Warn($"'{source.Name}': {built.SkippedBlendShapes} empty blend shape targets were skipped.");
            }
        }

        static Dictionary<int, int> BuildMeshLodMap(DnaFile dna, List<int> lods)
        {
            var map = new Dictionary<int, int>();
            // Walk high detail first so a mesh shared between LODs is attributed to its best one.
            foreach (int lod in lods.OrderBy(l => l))
                foreach (ushort meshIndex in dna.Definition.LodMeshMapping.GetIndices(lod))
                    if (!map.ContainsKey(meshIndex))
                        map[meshIndex] = lod;
            return map;
        }

        static Transform[] BuildSkeleton(DnaFile dna, Transform root, DnaSpace space, Dictionary<string, Transform> shared)
        {
            DnaDefinition definition = dna.Definition;
            int count = definition.JointCount;

            var transforms = new Transform[count];
            var created = new bool[count];

            for (int i = 0; i < count; i++)
            {
                string name = definition.JointNames[i];
                if (shared.TryGetValue(name, out Transform existing))
                {
                    transforms[i] = existing;
                    continue;
                }

                var go = new GameObject(name);
                transforms[i] = go.transform;
                created[i] = true;
                shared[name] = go.transform;
            }

            for (int i = 0; i < count; i++)
            {
                if (!created[i]) continue;

                int parent = i < definition.JointHierarchy.Length ? definition.JointHierarchy[i] : i;
                Transform parentTransform = parent == i || parent < 0 || parent >= count
                    ? root
                    : transforms[parent];

                transforms[i].SetParent(parentTransform, false);

                if (i < definition.NeutralJointTranslationX.Length && i < definition.NeutralJointRotationX.Length)
                {
                    transforms[i].localPosition = space.ConvertPoint(
                        definition.NeutralJointTranslationX[i],
                        definition.NeutralJointTranslationY[i],
                        definition.NeutralJointTranslationZ[i]);
                    transforms[i].localRotation = space.ConvertEuler(
                        definition.NeutralJointRotationX[i],
                        definition.NeutralJointRotationY[i],
                        definition.NeutralJointRotationZ[i]);
                }

                transforms[i].localScale = Vector3.one;
            }

            return transforms;
        }

        static void BuildLodGroup(GameObject root, Dictionary<int, List<Renderer>> lodRenderers, MetaHumanImportReport report)
        {
            var group = root.AddComponent<LODGroup>();
            int[] levels = lodRenderers.Keys.OrderBy(l => l).ToArray();

            var lods = new LOD[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                // Geometric falloff from 60% screen height down to the culling threshold.
                float height = Mathf.Max(0.01f, 0.6f / Mathf.Pow(2.2f, i + 1));
                lods[i] = new LOD(height, lodRenderers[levels[i]].ToArray());
            }

            group.SetLODs(lods);
            group.RecalculateBounds();
            report.Info($"Created a LODGroup with {levels.Length} levels.");
        }

        static string Sanitize(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }
    }
}
