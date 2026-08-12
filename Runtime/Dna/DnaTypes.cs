// MetaHuman Bridge - DNA data model.
//
// Mirrors the layout of Epic's open-source DNA format (EpicGames/OpenRigLogic, MIT).
// Field names and ordering deliberately track src/dna/DNA.h so the two can be diffed
// when Epic bumps the format version.

using System;
using System.Collections.Generic;

namespace RaveHouse.MetaHumanBridge
{
    public enum DnaTranslationUnit { Centimetre = 0, Metre = 1 }

    public enum DnaRotationUnit { Degrees = 0, Radians = 1 }

    /// <summary>Matches tdm::axis_dir. "front" points away from the viewer.</summary>
    public enum DnaAxisDirection { Left = 0, Right = 1, Up = 2, Down = 3, Front = 4, Back = 5 }

    /// <summary>Matches tdm::rot_seq. Intrinsic rotation order.</summary>
    public enum DnaRotationSequence { XYZ = 0, XZY = 1, YXZ = 2, YZX = 3, ZXY = 4, ZYX = 5 }

    /// <summary>Matches dna::FaceWindingOrder.</summary>
    public enum DnaFaceWindingOrder { CounterClockwise = 0, Clockwise = 1 }

    public sealed class DnaCoordinateSystem
    {
        public DnaAxisDirection X = DnaAxisDirection.Right;
        public DnaAxisDirection Y = DnaAxisDirection.Up;
        public DnaAxisDirection Z = DnaAxisDirection.Front;
    }

    public sealed class DnaDescriptor
    {
        public string Name = string.Empty;
        public ushort Archetype;
        public ushort Gender;
        public ushort Age;
        public readonly List<KeyValuePair<string, string>> Metadata = new List<KeyValuePair<string, string>>();
        public DnaTranslationUnit TranslationUnit = DnaTranslationUnit.Centimetre;
        public DnaRotationUnit RotationUnit = DnaRotationUnit.Degrees;
        public DnaCoordinateSystem CoordinateSystem = new DnaCoordinateSystem();
        public ushort LodCount;
        public ushort MaxLod;
        public string Complexity = string.Empty;
        public string DbName = string.Empty;

        // From the optional "dsce" layer (file version 2.7+). Defaults match pre-2.7 behaviour.
        public DnaRotationSequence RotationSequence = DnaRotationSequence.XYZ;
        public int RotationSignX = 1;
        public int RotationSignY = 1;
        public int RotationSignZ = 1;
        public DnaFaceWindingOrder FaceWindingOrder = DnaFaceWindingOrder.CounterClockwise;
    }

    /// <summary>
    /// LOD -> index-list mapping. <see cref="Lods"/>[lod] selects a row of <see cref="Indices"/>,
    /// which lets several LODs share one index list.
    /// </summary>
    public sealed class DnaLodMapping
    {
        public ushort[] Lods = Array.Empty<ushort>();
        public ushort[][] Indices = Array.Empty<ushort[]>();

        public ushort[] GetIndices(int lod)
        {
            if (lod < 0 || lod >= Lods.Length) return Array.Empty<ushort>();
            int row = Lods[lod];
            if (row < 0 || row >= Indices.Length) return Array.Empty<ushort>();
            return Indices[row];
        }
    }

    public sealed class DnaDefinition
    {
        public DnaLodMapping LodJointMapping = new DnaLodMapping();
        public DnaLodMapping LodBlendShapeMapping = new DnaLodMapping();
        public DnaLodMapping LodAnimatedMapMapping = new DnaLodMapping();
        public DnaLodMapping LodMeshMapping = new DnaLodMapping();

        public string[] GuiControlNames = Array.Empty<string>();
        public string[] RawControlNames = Array.Empty<string>();
        public string[] JointNames = Array.Empty<string>();
        public string[] BlendShapeChannelNames = Array.Empty<string>();
        public string[] AnimatedMapNames = Array.Empty<string>();
        public string[] MeshNames = Array.Empty<string>();

        /// <summary>Mesh index for each entry of <see cref="MeshBlendShapeChannelTo"/>.</summary>
        public ushort[] MeshBlendShapeChannelFrom = Array.Empty<ushort>();
        /// <summary>Blend shape channel index paired with <see cref="MeshBlendShapeChannelFrom"/>.</summary>
        public ushort[] MeshBlendShapeChannelTo = Array.Empty<ushort>();

        /// <summary>Parent index per joint. The root joint points at itself.</summary>
        public ushort[] JointHierarchy = Array.Empty<ushort>();

        public float[] NeutralJointTranslationX = Array.Empty<float>();
        public float[] NeutralJointTranslationY = Array.Empty<float>();
        public float[] NeutralJointTranslationZ = Array.Empty<float>();
        public float[] NeutralJointRotationX = Array.Empty<float>();
        public float[] NeutralJointRotationY = Array.Empty<float>();
        public float[] NeutralJointRotationZ = Array.Empty<float>();

        public int JointCount => JointNames.Length;
    }

    /// <summary>
    /// Piecewise-linear remap rows. Each row fires when from &lt;= input &lt;= to, contributing
    /// slope * input + cut to its output. Outputs are accumulated then clamped to [0, 1].
    /// </summary>
    public sealed class DnaConditionalTable
    {
        public ushort[] InputIndices = Array.Empty<ushort>();
        public ushort[] OutputIndices = Array.Empty<ushort>();
        public float[] FromValues = Array.Empty<float>();
        public float[] ToValues = Array.Empty<float>();
        public float[] SlopeValues = Array.Empty<float>();
        public float[] CutValues = Array.Empty<float>();

        public int RowCount => InputIndices.Length;
    }

    /// <summary>Sparse matrix of corrective (PSD) controls: output row, input column, weight.</summary>
    public sealed class DnaPsdMatrix
    {
        public ushort[] Rows = Array.Empty<ushort>();
        public ushort[] Columns = Array.Empty<ushort>();
        public float[] Values = Array.Empty<float>();
    }

    public sealed class DnaControls
    {
        public ushort PsdCount;
        public DnaConditionalTable Conditionals = new DnaConditionalTable();
        public DnaPsdMatrix Psds = new DnaPsdMatrix();
    }

    /// <summary>
    /// One dense sub-matrix of the joint delta solve. Row-major:
    /// values[r * InputIndices.Length + c] maps control InputIndices[c] onto output OutputIndices[r].
    /// </summary>
    public sealed class DnaJointGroup
    {
        /// <summary>Row count to evaluate per LOD (rows are ordered most-significant first).</summary>
        public ushort[] Lods = Array.Empty<ushort>();
        public ushort[] InputIndices = Array.Empty<ushort>();
        public ushort[] OutputIndices = Array.Empty<ushort>();
        public float[] Values = Array.Empty<float>();
        public ushort[] JointIndices = Array.Empty<ushort>();
    }

    public sealed class DnaJoints
    {
        /// <summary>Total joint output attributes: jointCount * 9 (tx ty tz rx ry rz sx sy sz).</summary>
        public ushort RowCount;
        /// <summary>Total control inputs.</summary>
        public ushort ColCount;
        public DnaJointGroup[] JointGroups = Array.Empty<DnaJointGroup>();
    }

    public sealed class DnaBlendShapeChannels
    {
        public ushort[] Lods = Array.Empty<ushort>();
        public ushort[] InputIndices = Array.Empty<ushort>();
        public ushort[] OutputIndices = Array.Empty<ushort>();
    }

    public sealed class DnaAnimatedMaps
    {
        public ushort[] Lods = Array.Empty<ushort>();
        public DnaConditionalTable Conditionals = new DnaConditionalTable();
    }

    public sealed class DnaBehavior
    {
        public DnaControls Controls = new DnaControls();
        public DnaJoints Joints = new DnaJoints();
        public DnaBlendShapeChannels BlendShapeChannels = new DnaBlendShapeChannels();
        public DnaAnimatedMaps AnimatedMaps = new DnaAnimatedMaps();
    }

    public sealed class DnaBlendShapeTarget
    {
        public float[] DeltaX = Array.Empty<float>();
        public float[] DeltaY = Array.Empty<float>();
        public float[] DeltaZ = Array.Empty<float>();
        /// <summary>Indices into the mesh's position array.</summary>
        public uint[] VertexIndices = Array.Empty<uint>();
        public ushort BlendShapeChannelIndex;
    }

    public sealed class DnaVertexSkinWeights
    {
        public float[] Weights = Array.Empty<float>();
        public ushort[] JointIndices = Array.Empty<ushort>();
    }

    /// <summary>
    /// DNA meshes are stored de-duplicated: positions, UVs and normals live in independent
    /// arrays and <see cref="LayoutPosition"/>/<see cref="LayoutTexCoord"/>/<see cref="LayoutNormal"/>
    /// describe the unique (position, uv, normal) triples that faces actually index.
    /// </summary>
    public sealed class DnaMesh
    {
        public string Name = string.Empty;

        public float[] PositionX = Array.Empty<float>();
        public float[] PositionY = Array.Empty<float>();
        public float[] PositionZ = Array.Empty<float>();

        public float[] TexCoordU = Array.Empty<float>();
        public float[] TexCoordV = Array.Empty<float>();

        public float[] NormalX = Array.Empty<float>();
        public float[] NormalY = Array.Empty<float>();
        public float[] NormalZ = Array.Empty<float>();

        public uint[] LayoutPosition = Array.Empty<uint>();
        public uint[] LayoutTexCoord = Array.Empty<uint>();
        public uint[] LayoutNormal = Array.Empty<uint>();

        /// <summary>Per face, the layout indices of its corners. Faces may be n-gons.</summary>
        public uint[][] Faces = Array.Empty<uint[]>();

        public ushort MaximumInfluencePerVertex;
        /// <summary>One entry per position, not per layout vertex.</summary>
        public DnaVertexSkinWeights[] SkinWeights = Array.Empty<DnaVertexSkinWeights>();
        public DnaBlendShapeTarget[] BlendShapeTargets = Array.Empty<DnaBlendShapeTarget>();

        public int PositionCount => PositionX.Length;
        public int LayoutCount => LayoutPosition.Length;
    }

    public sealed class DnaGeometry
    {
        public DnaMesh[] Meshes = Array.Empty<DnaMesh>();
    }

    /// <summary>A parsed .dna file. Layers Epic added for body correctives (RBF, ML, twist/swing)
    /// are recorded as present but not decoded; see <see cref="UnsupportedLayers"/>.</summary>
    public sealed class DnaFile
    {
        public int FileGeneration;
        public int FileVersion;
        public DnaDescriptor Descriptor = new DnaDescriptor();
        public DnaDefinition Definition = new DnaDefinition();
        public DnaBehavior Behavior = new DnaBehavior();
        public DnaGeometry Geometry = new DnaGeometry();

        /// <summary>Four-character codes of layers present in the file that this reader skipped.</summary>
        public readonly List<string> UnsupportedLayers = new List<string>();

        public string SourcePath = string.Empty;
    }
}
