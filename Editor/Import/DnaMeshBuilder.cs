// MetaHuman Bridge - turns a DNA mesh into a Unity Mesh.
//
// DNA stores positions, UVs and normals in independent arrays plus a "layout" table of the
// unique (position, uv, normal) triples that faces reference. That layout table is exactly
// Unity's vertex list, so vertices are the layouts and skinning/blend shape data (indexed by
// position) is fanned out through it.

using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public enum BlendShapeImportMode
    {
        None,
        All
    }

    public sealed class DnaMeshBuildResult
    {
        public Mesh Mesh;
        /// <summary>Rig blend shape channel index for each Unity blend shape, in mesh order.</summary>
        public int[] BlendShapeChannelIndices = Array.Empty<int>();
        public int SkippedBlendShapes;
    }

    public static class DnaMeshBuilder
    {
        public static DnaMeshBuildResult Build(
            DnaMesh source,
            DnaFile dna,
            DnaSpace space,
            BlendShapeImportMode blendShapeMode,
            bool flipV,
            Matrix4x4[] bindPoses,
            int[] dnaJointToBoneIndex)
        {
            int vertexCount = source.LayoutCount;
            if (vertexCount == 0)
                throw new InvalidOperationException($"DNA mesh '{source.Name}' has no vertices.");

            var mesh = new Mesh
            {
                name = source.Name,
                indexFormat = vertexCount > ushort.MaxValue
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            var vertices = new Vector3[vertexCount];
            var normals = source.NormalX.Length > 0 ? new Vector3[vertexCount] : null;
            var uvs = source.TexCoordU.Length > 0 ? new Vector2[vertexCount] : null;

            for (int i = 0; i < vertexCount; i++)
            {
                uint p = source.LayoutPosition[i];
                vertices[i] = space.ConvertPoint(source.PositionX[p], source.PositionY[p], source.PositionZ[p]);

                if (normals != null)
                {
                    uint n = source.LayoutNormal[i];
                    if (n < source.NormalX.Length)
                        normals[i] = space.ConvertDirection(source.NormalX[n], source.NormalY[n], source.NormalZ[n]).normalized;
                }

                if (uvs != null)
                {
                    uint t = source.LayoutTexCoord[i];
                    if (t < source.TexCoordU.Length)
                    {
                        float v = source.TexCoordV[t];
                        uvs[i] = new Vector2(source.TexCoordU[t], flipV ? 1f - v : v);
                    }
                }
            }

            mesh.vertices = vertices;
            if (uvs != null) mesh.uv = uvs;
            if (normals != null) mesh.normals = normals;

            mesh.triangles = Triangulate(source, space.ReverseWinding);

            ApplySkinning(mesh, source, bindPoses, dnaJointToBoneIndex);

            var result = new DnaMeshBuildResult { Mesh = mesh };
            if (blendShapeMode != BlendShapeImportMode.None)
                AddBlendShapes(mesh, source, dna, space, result);

            mesh.RecalculateBounds();
            if (normals == null) mesh.RecalculateNormals();
            mesh.RecalculateTangents();

            return result;
        }

        // ------------------------------------------------------------------ topology

        static int[] Triangulate(DnaMesh source, bool reverseWinding)
        {
            int triangleCount = 0;
            foreach (uint[] face in source.Faces)
                if (face.Length >= 3) triangleCount += face.Length - 2;

            var indices = new int[triangleCount * 3];
            int w = 0;

            foreach (uint[] face in source.Faces)
            {
                // MetaHuman meshes are largely quads; fan triangulation is correct for the
                // convex, planar faces DNA stores.
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    int a = (int)face[0];
                    int b = (int)face[i];
                    int c = (int)face[i + 1];

                    if (reverseWinding)
                    {
                        indices[w++] = a;
                        indices[w++] = c;
                        indices[w++] = b;
                    }
                    else
                    {
                        indices[w++] = a;
                        indices[w++] = b;
                        indices[w++] = c;
                    }
                }
            }

            return indices;
        }

        // ------------------------------------------------------------------ skinning

        static void ApplySkinning(Mesh mesh, DnaMesh source, Matrix4x4[] bindPoses, int[] dnaJointToBoneIndex)
        {
            if (source.SkinWeights.Length == 0 || bindPoses == null || bindPoses.Length == 0) return;

            int vertexCount = source.LayoutCount;
            var bonesPerVertex = new byte[vertexCount];
            var flattened = new List<BoneWeight1>(vertexCount * 4);

            for (int i = 0; i < vertexCount; i++)
            {
                uint positionIndex = source.LayoutPosition[i];
                if (positionIndex >= source.SkinWeights.Length)
                {
                    bonesPerVertex[i] = 0;
                    continue;
                }

                DnaVertexSkinWeights sw = source.SkinWeights[positionIndex];
                int influences = Mathf.Min(sw.Weights.Length, sw.JointIndices.Length);

                // Unity requires descending weights within a vertex.
                var ordered = new List<BoneWeight1>(influences);
                for (int k = 0; k < influences; k++)
                {
                    ushort dnaJoint = sw.JointIndices[k];
                    int bone = dnaJoint < dnaJointToBoneIndex.Length ? dnaJointToBoneIndex[dnaJoint] : -1;
                    if (bone < 0 || sw.Weights[k] <= 0f) continue;
                    ordered.Add(new BoneWeight1 { boneIndex = bone, weight = sw.Weights[k] });
                }

                ordered.Sort((x, y) => y.weight.CompareTo(x.weight));
                bonesPerVertex[i] = (byte)Mathf.Min(ordered.Count, byte.MaxValue);
                for (int k = 0; k < bonesPerVertex[i]; k++)
                    flattened.Add(ordered[k]);
            }

            using (var bpv = new NativeArray<byte>(bonesPerVertex, Allocator.Temp))
            using (var weights = new NativeArray<BoneWeight1>(flattened.ToArray(), Allocator.Temp))
            {
                mesh.SetBoneWeights(bpv, weights);
            }

            mesh.bindposes = bindPoses;
        }

        // ------------------------------------------------------------------ blend shapes

        static void AddBlendShapes(Mesh mesh, DnaMesh source, DnaFile dna, DnaSpace space, DnaMeshBuildResult result)
        {
            if (source.BlendShapeTargets.Length == 0) return;

            int vertexCount = source.LayoutCount;
            BuildPositionToLayoutMap(source, out int[] mapStart, out int[] mapEntries);

            var deltas = new Vector3[vertexCount];
            var used = new HashSet<string>();
            var channelIndices = new List<int>(source.BlendShapeTargets.Length);
            string[] channelNames = dna.Definition.BlendShapeChannelNames;

            foreach (DnaBlendShapeTarget target in source.BlendShapeTargets)
            {
                if (target.VertexIndices.Length == 0)
                {
                    result.SkippedBlendShapes++;
                    continue;
                }

                Array.Clear(deltas, 0, deltas.Length);

                int count = Mathf.Min(target.VertexIndices.Length, target.DeltaX.Length);
                for (int k = 0; k < count; k++)
                {
                    uint positionIndex = target.VertexIndices[k];
                    if (positionIndex + 1 >= mapStart.Length) continue;

                    Vector3 delta = space.ConvertPoint(target.DeltaX[k], target.DeltaY[k], target.DeltaZ[k]);
                    for (int e = mapStart[positionIndex]; e < mapStart[positionIndex + 1]; e++)
                        deltas[mapEntries[e]] = delta;
                }

                string name = target.BlendShapeChannelIndex < channelNames.Length
                    ? channelNames[target.BlendShapeChannelIndex]
                    : $"channel_{target.BlendShapeChannelIndex}";

                // Unity keys blend shapes by name, so collisions inside one mesh must be broken.
                string unique = name;
                int suffix = 1;
                while (!used.Add(unique))
                    unique = $"{name}_{suffix++}";

                mesh.AddBlendShapeFrame(unique, 100f, deltas, null, null);
                channelIndices.Add(target.BlendShapeChannelIndex);
            }

            result.BlendShapeChannelIndices = channelIndices.ToArray();
        }

        /// <summary>
        /// Compressed-sparse-row map from a position index to every layout vertex that uses it.
        /// </summary>
        static void BuildPositionToLayoutMap(DnaMesh source, out int[] start, out int[] entries)
        {
            int positionCount = source.PositionCount;
            start = new int[positionCount + 1];

            foreach (uint p in source.LayoutPosition)
                if (p < positionCount) start[p + 1]++;

            for (int i = 0; i < positionCount; i++)
                start[i + 1] += start[i];

            entries = new int[source.LayoutCount];
            var cursor = new int[positionCount];
            for (int i = 0; i < source.LayoutCount; i++)
            {
                uint p = source.LayoutPosition[i];
                if (p >= positionCount) continue;
                entries[start[p] + cursor[p]++] = i;
            }
        }
    }
}
