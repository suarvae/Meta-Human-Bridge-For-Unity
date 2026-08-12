// MetaHuman Bridge - material and texture wiring.
//
// A DCC Export ships loose .png maps whose names carry the body part and the map type
// ("head_basecolor", "body_normal", ...). This resolves them by token, creates one material
// per DNA mesh, and configures the importer flags each map needs.
//
// Skin in Unreal uses a bespoke shading model; a URP/Lit or Standard material is a starting
// point, not a match. Treat the result as correctly wired, not final-looking.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public enum MetaHumanMapType
    {
        Unknown,
        BaseColor,
        Normal,
        Roughness,
        Metallic,
        Occlusion,
        Cavity,
        Specular,
        Opacity,
        Displacement
    }

    public static class MetaHumanMaterialBuilder
    {
        static readonly (MetaHumanMapType type, string[] tokens)[] MapTokens =
        {
            (MetaHumanMapType.BaseColor, new[] { "basecolor", "base_color", "albedo", "diffuse", "color", "_c_" }),
            (MetaHumanMapType.Normal, new[] { "normal", "_n_", "nrm" }),
            (MetaHumanMapType.Roughness, new[] { "roughness", "_r_", "rough" }),
            (MetaHumanMapType.Metallic, new[] { "metallic", "metalness" }),
            (MetaHumanMapType.Occlusion, new[] { "occlusion", "ambientocclusion", "_ao", "ao_" }),
            (MetaHumanMapType.Cavity, new[] { "cavity" }),
            (MetaHumanMapType.Specular, new[] { "specular", "_s_" }),
            (MetaHumanMapType.Opacity, new[] { "opacity", "alpha", "mask" }),
            (MetaHumanMapType.Displacement, new[] { "displacement", "height" }),
        };

        /// <summary>Body-part token extracted from a DNA mesh name such as "head_lod0_mesh".</summary>
        public static string MeshToken(string meshName)
        {
            if (string.IsNullOrEmpty(meshName)) return string.Empty;
            int lod = meshName.IndexOf("_lod", StringComparison.OrdinalIgnoreCase);
            return (lod > 0 ? meshName.Substring(0, lod) : meshName).ToLowerInvariant();
        }

        public static MetaHumanMapType Classify(string fileName)
        {
            string lower = Path.GetFileNameWithoutExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
            foreach (var (type, tokens) in MapTokens)
                if (tokens.Any(token => lower.Contains(token)))
                    return type;
            return MetaHumanMapType.Unknown;
        }

        /// <summary>Copies loose textures into the project and returns their project-relative paths.</summary>
        public static List<string> CollectTextures(string sourceFolder, string destinationFolder, bool copy)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder)) return result;

            string[] files = Directory
                .GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                .Where(IsTexture)
                .ToArray();

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

            foreach (string file in files)
            {
                string full = Path.GetFullPath(file);
                if (full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase) && !copy)
                {
                    result.Add(full.Substring(projectRoot.Length + 1).Replace('\\', '/'));
                    continue;
                }

                string target = $"{destinationFolder}/{Path.GetFileName(file)}";
                if (!File.Exists(target))
                    File.Copy(file, target, false);
                result.Add(target);
            }

            AssetDatabase.Refresh();
            return result;
        }

        static bool IsTexture(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".png" || ext == ".tga" || ext == ".jpg" || ext == ".jpeg" || ext == ".exr" || ext == ".tif";
        }

        /// <summary>Marks normal maps as such and everything else as sRGB or linear data.</summary>
        public static void ConfigureTextureImporters(IEnumerable<string> assetPaths)
        {
            foreach (string path in assetPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                MetaHumanMapType type = Classify(path);
                bool changed = false;

                if (type == MetaHumanMapType.Normal && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
                else if (type != MetaHumanMapType.Normal && type != MetaHumanMapType.BaseColor && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    changed = true;
                }

                if (changed) importer.SaveAndReimport();
            }
        }

        public static Material CreateMaterial(string meshName, IReadOnlyList<string> texturePaths, string outputFolder)
        {
            Shader shader = DefaultShader();
            var material = new Material(shader) { name = $"{meshName}" };

            string token = MeshToken(meshName);
            var candidates = texturePaths
                .Where(path => Path.GetFileNameWithoutExtension(path)!.ToLowerInvariant().Contains(token))
                .ToList();

            foreach (string path in candidates)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) continue;

                switch (Classify(path))
                {
                    case MetaHumanMapType.BaseColor:
                        SetTexture(material, texture, "_BaseMap", "_MainTex");
                        break;
                    case MetaHumanMapType.Normal:
                        SetTexture(material, texture, "_BumpMap");
                        if (material.HasProperty("_NormalMapToggle")) material.SetFloat("_NormalMapToggle", 1f);
                        material.EnableKeyword("_NORMALMAP");
                        break;
                    case MetaHumanMapType.Occlusion:
                    case MetaHumanMapType.Cavity:
                        SetTexture(material, texture, "_OcclusionMap");
                        break;
                    case MetaHumanMapType.Metallic:
                    case MetaHumanMapType.Roughness:
                        // URP/Standard pack metallic and smoothness together; a raw roughness map
                        // is inverted relative to smoothness, so it is left for the user to author.
                        SetTexture(material, texture, "_MetallicGlossMap");
                        break;
                }
            }

            string path2 = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{material.name}.mat");
            AssetDatabase.CreateAsset(material, path2);
            return material;
        }

        static void SetTexture(Material material, Texture2D texture, params string[] propertyNames)
        {
            foreach (string property in propertyNames)
            {
                if (!material.HasProperty(property)) continue;
                material.SetTexture(property, texture);
                return;
            }
        }

        public static Shader DefaultShader()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                Shader urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp != null) return urp;

                Shader hdrp = Shader.Find("HDRP/Lit");
                if (hdrp != null) return hdrp;
            }

            return Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
        }
    }
}
