// MetaHuman Bridge - bakes a DNA behaviour layer into a RigLogicAsset.
//
// The PSD flattening mirrors PSDNetFactory in OpenRigLogic: all matrix entries sharing an
// output row collapse into one corrective control whose weight is the product of its values.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public static class RigLogicAssetBuilder
    {
        public static RigLogicAsset Build(DnaFile dna, string characterName, float importScale)
        {
            var asset = ScriptableObject.CreateInstance<RigLogicAsset>();
            DnaDefinition definition = dna.Definition;
            DnaBehavior behavior = dna.Behavior;

            asset.name = characterName + " RigLogic";
            asset.characterName = characterName;
            asset.lodCount = Mathf.Max(1, dna.Descriptor.LodCount);

            asset.guiControlNames = definition.GuiControlNames;
            asset.rawControlNames = definition.RawControlNames;
            asset.psdCount = behavior.Controls.PsdCount;
            asset.guiToRaw = RigLogicConditionalTable.Build(
                behavior.Controls.Conditionals,
                definition.RawControlNames.Length + behavior.Controls.PsdCount);
            asset.psds = BuildPsds(behavior.Controls, definition.RawControlNames.Length);

            asset.blendShapeChannelNames = definition.BlendShapeChannelNames;
            asset.blendShapeLodRowCounts = behavior.BlendShapeChannels.Lods;
            asset.blendShapeInputIndices = behavior.BlendShapeChannels.InputIndices;
            asset.blendShapeOutputIndices = behavior.BlendShapeChannels.OutputIndices;

            asset.jointNames = definition.JointNames;
            asset.jointParents = definition.JointHierarchy;
            asset.neutralTranslations = Interleave(
                definition.NeutralJointTranslationX,
                definition.NeutralJointTranslationY,
                definition.NeutralJointTranslationZ);
            asset.neutralRotations = Interleave(
                definition.NeutralJointRotationX,
                definition.NeutralJointRotationY,
                definition.NeutralJointRotationZ);
            asset.jointAttributeCount = behavior.Joints.RowCount != 0
                ? behavior.Joints.RowCount
                : definition.JointCount * 9;
            asset.jointGroups = behavior.Joints.JointGroups
                .Select(group => new RigLogicJointGroup
                {
                    lodRowCounts = group.Lods,
                    inputIndices = group.InputIndices,
                    outputIndices = group.OutputIndices,
                    values = group.Values
                })
                .ToList();
            asset.drivenJointIndices = behavior.Joints.JointGroups
                .SelectMany(group => group.JointIndices)
                .Select(index => (int)index)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();

            asset.animatedMapNames = definition.AnimatedMapNames;
            asset.animatedMapLodRowCounts = behavior.AnimatedMaps.Lods;
            asset.animatedMaps = RigLogicConditionalTable.Build(
                behavior.AnimatedMaps.Conditionals,
                definition.AnimatedMapNames.Length);

            asset.translationUnit = dna.Descriptor.TranslationUnit;
            asset.rotationUnit = dna.Descriptor.RotationUnit;
            asset.axisX = dna.Descriptor.CoordinateSystem.X;
            asset.axisY = dna.Descriptor.CoordinateSystem.Y;
            asset.axisZ = dna.Descriptor.CoordinateSystem.Z;
            asset.rotationSequence = dna.Descriptor.RotationSequence;
            asset.rotationSignX = dna.Descriptor.RotationSignX;
            asset.rotationSignY = dna.Descriptor.RotationSignY;
            asset.rotationSignZ = dna.Descriptor.RotationSignZ;
            asset.importScale = importScale;

            asset.unevaluatedLayers = dna.UnsupportedLayers.Distinct().ToArray();

            return asset;
        }

        static RigLogicPsdSet BuildPsds(DnaControls controls, int rawControlCount)
        {
            int psdCount = controls.PsdCount;
            var set = new RigLogicPsdSet
            {
                offsets = new int[psdCount],
                sizes = new int[psdCount],
                weights = new float[psdCount]
            };

            if (psdCount == 0) return set;

            var inputs = new List<ushort>(controls.Psds.Columns.Length);
            ushort[] rows = controls.Psds.Rows;
            ushort[] columns = controls.Psds.Columns;
            float[] values = controls.Psds.Values;

            for (int start = 0; start < rows.Length; start++)
            {
                int psdIndex = rows[start] - rawControlCount;
                if (psdIndex < 0 || psdIndex >= psdCount) continue;
                if (set.sizes[psdIndex] != 0) continue;

                float weight = 1f;
                int offset = inputs.Count;
                for (int i = start; i < rows.Length; i++)
                {
                    if (rows[i] != rows[start]) continue;
                    if (i < columns.Length) inputs.Add(columns[i]);
                    if (i < values.Length) weight *= values[i];
                }

                set.offsets[psdIndex] = offset;
                set.sizes[psdIndex] = inputs.Count - offset;
                set.weights[psdIndex] = weight;
            }

            set.inputIndices = inputs.ToArray();
            return set;
        }

        static float[] Interleave(float[] x, float[] y, float[] z)
        {
            int count = Mathf.Min(x.Length, Mathf.Min(y.Length, z.Length));
            var result = new float[count * 3];
            for (int i = 0; i < count; i++)
            {
                result[i * 3] = x[i];
                result[i * 3 + 1] = y[i];
                result[i * 3 + 2] = z[i];
            }
            return result;
        }
    }
}
