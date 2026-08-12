// MetaHuman Bridge - baked rig-evaluation data.
//
// Holds the DNA behaviour layer in a Unity-serialisable shape so the runtime solver never
// has to touch the .dna file. Built by MetaHumanImporter; see RigLogicSolver for the maths.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge
{
    [Serializable]
    public sealed class RigLogicConditionalTable
    {
        public ushort[] inputIndices = Array.Empty<ushort>();
        public ushort[] outputIndices = Array.Empty<ushort>();
        public float[] fromValues = Array.Empty<float>();
        public float[] toValues = Array.Empty<float>();
        public float[] slopeValues = Array.Empty<float>();
        public float[] cutValues = Array.Empty<float>();

        /// <summary>
        /// Rows that share an (input, output) pair form an interval run. Once one row in a run
        /// fires, the rest are skipped; this stores how many rows to jump.
        /// </summary>
        public ushort[] intervalSkip = Array.Empty<ushort>();

        public int outputCount;

        public int RowCount => inputIndices.Length;

        public static RigLogicConditionalTable Build(DnaConditionalTable source, int outputCount)
        {
            var table = new RigLogicConditionalTable
            {
                inputIndices = source.InputIndices,
                outputIndices = source.OutputIndices,
                fromValues = source.FromValues,
                toValues = source.ToValues,
                slopeValues = source.SlopeValues,
                cutValues = source.CutValues,
                outputCount = outputCount
            };
            table.intervalSkip = BuildIntervalSkip(source.InputIndices, source.OutputIndices);
            return table;
        }

        static ushort[] BuildIntervalSkip(ushort[] inputIndices, ushort[] outputIndices)
        {
            var skip = new ushort[inputIndices.Length];
            for (int i = 0; i < inputIndices.Length;)
            {
                ushort count = 1;
                ushort input = inputIndices[i];
                ushort output = outputIndices[i];
                int j = i + 1;
                for (; j < inputIndices.Length && inputIndices[j] == input && outputIndices[j] == output; j++)
                    skip[j] = count++;

                Array.Reverse(skip, i, count);
                i += count;
            }
            return skip;
        }
    }

    /// <summary>One corrective control: the product of its inputs, scaled by a baked weight.</summary>
    [Serializable]
    public sealed class RigLogicPsdSet
    {
        public int[] offsets = Array.Empty<int>();
        public int[] sizes = Array.Empty<int>();
        public float[] weights = Array.Empty<float>();
        public ushort[] inputIndices = Array.Empty<ushort>();
    }

    /// <summary>A dense sub-matrix mapping controls onto joint output attributes.</summary>
    [Serializable]
    public sealed class RigLogicJointGroup
    {
        public ushort[] lodRowCounts = Array.Empty<ushort>();
        public ushort[] inputIndices = Array.Empty<ushort>();
        public ushort[] outputIndices = Array.Empty<ushort>();
        public float[] values = Array.Empty<float>();
    }

    [CreateAssetMenu(menuName = "MetaHuman Bridge/Rig Logic Asset", fileName = "RigLogic")]
    public sealed class RigLogicAsset : ScriptableObject
    {
        [Header("Identity")]
        public string characterName;
        public int lodCount = 1;

        [Header("Controls")]
        public string[] guiControlNames = Array.Empty<string>();
        public string[] rawControlNames = Array.Empty<string>();
        public int psdCount;
        public RigLogicConditionalTable guiToRaw = new RigLogicConditionalTable();
        public RigLogicPsdSet psds = new RigLogicPsdSet();

        [Header("Blend shapes")]
        public string[] blendShapeChannelNames = Array.Empty<string>();
        public ushort[] blendShapeLodRowCounts = Array.Empty<ushort>();
        public ushort[] blendShapeInputIndices = Array.Empty<ushort>();
        public ushort[] blendShapeOutputIndices = Array.Empty<ushort>();

        [Header("Joints")]
        public string[] jointNames = Array.Empty<string>();
        public ushort[] jointParents = Array.Empty<ushort>();
        public float[] neutralTranslations = Array.Empty<float>();  // xyz per joint, DNA space & units
        public float[] neutralRotations = Array.Empty<float>();      // xyz per joint, DNA space & units
        public List<RigLogicJointGroup> jointGroups = new List<RigLogicJointGroup>();
        public int jointAttributeCount;   // jointCount * 9
        [Tooltip("Joints that at least one joint group can move. Everything else keeps its bind pose.")]
        public int[] drivenJointIndices = Array.Empty<int>();

        [Header("Animated maps")]
        public string[] animatedMapNames = Array.Empty<string>();
        public ushort[] animatedMapLodRowCounts = Array.Empty<ushort>();
        public RigLogicConditionalTable animatedMaps = new RigLogicConditionalTable();

        [Header("Coordinate space")]
        public DnaTranslationUnit translationUnit = DnaTranslationUnit.Centimetre;
        public DnaRotationUnit rotationUnit = DnaRotationUnit.Degrees;
        public DnaAxisDirection axisX = DnaAxisDirection.Right;
        public DnaAxisDirection axisY = DnaAxisDirection.Up;
        public DnaAxisDirection axisZ = DnaAxisDirection.Front;
        public DnaRotationSequence rotationSequence = DnaRotationSequence.XYZ;
        public int rotationSignX = 1;
        public int rotationSignY = 1;
        public int rotationSignZ = 1;
        [Tooltip("Extra uniform scale applied on top of the DNA's unit conversion. Must match the value used at import.")]
        public float importScale = 1f;

        [Header("Coverage")]
        [Tooltip("Layers present in the source DNA that this package does not evaluate (RBF, machine-learned and twist/swing correctives).")]
        public string[] unevaluatedLayers = Array.Empty<string>();

        public int JointCount => jointNames.Length;
        public int ControlCount => rawControlNames.Length + psdCount;

        public DnaSpace CreateSpace()
        {
            var descriptor = new DnaDescriptor
            {
                TranslationUnit = translationUnit,
                RotationUnit = rotationUnit,
                CoordinateSystem = new DnaCoordinateSystem { X = axisX, Y = axisY, Z = axisZ },
                RotationSequence = rotationSequence,
                RotationSignX = rotationSignX,
                RotationSignY = rotationSignY,
                RotationSignZ = rotationSignZ
            };
            return DnaSpace.FromDescriptor(descriptor, importScale);
        }

        public int IndexOfGuiControl(string controlName) => Array.IndexOf(guiControlNames, controlName);
        public int IndexOfRawControl(string controlName) => Array.IndexOf(rawControlNames, controlName);
    }
}
