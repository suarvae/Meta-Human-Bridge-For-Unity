// MetaHuman Bridge - .dna binary parser.
//
// The DNA container is a big-endian ("network order") terse archive. Containers are
// serialised as a uint32 element count followed by the elements; strings as a uint32
// byte count followed by raw bytes. File layout (generation 2, version 2+):
//
//   "DNA"                      3 bytes
//   generation, version        2 x uint16
//   index table                uint32 count, then { uint32 id, version, offset, size }
//   layers                     each at its recorded absolute offset
//
// Generation 2 version 1 predates the index table and uses a fixed section lookup
// table instead; both are handled below.
//
// Reference: EpicGames/OpenRigLogic, src/dna/DNA.h and src/terse/archives/binary (MIT).

using System;
using System.Collections.Generic;
using System.IO;

namespace RaveHouse.MetaHumanBridge
{
    public sealed class DnaReadOptions
    {
        /// <summary>Skip the geometry layer entirely. Useful when only the rig is needed.</summary>
        public bool ReadGeometry = true;

        /// <summary>Parse blend shape targets. Turning this off saves a lot of time and memory.</summary>
        public bool ReadBlendShapes = true;

        /// <summary>
        /// When non-null, only meshes whose index appears in this set are decoded. Other meshes
        /// are skipped using their recorded byte size, so this is close to free.
        /// </summary>
        public HashSet<int> MeshIndexFilter;

        /// <summary>Reports progress as (stage, 0..1). May be called from a background thread.</summary>
        public Action<string, float> Progress;
    }

    public sealed class DnaParseException : Exception
    {
        public DnaParseException(string message) : base(message) { }
    }

    public static class DnaBinaryReader
    {
        const uint IdDescriptor = 0x64657363;    // "desc"
        const uint IdDefinition = 0x6465666E;    // "defn"
        const uint IdBehavior = 0x62687672;      // "bhvr"
        const uint IdGeometry = 0x67656F6D;      // "geom"
        const uint IdDescriptorExt = 0x64736365; // "dsce"

        public static DnaFile Load(string path, DnaReadOptions options = null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            DnaFile dna = Parse(bytes, options);
            dna.SourcePath = path;
            return dna;
        }

        public static DnaFile Parse(byte[] bytes, DnaReadOptions options = null)
        {
            options = options ?? new DnaReadOptions();
            var r = new Cursor(bytes);

            if (bytes.Length < 7 || bytes[0] != 'D' || bytes[1] != 'N' || bytes[2] != 'A')
                throw new DnaParseException("Not a DNA file: missing 'DNA' signature.");
            r.Position = 3;

            var dna = new DnaFile
            {
                FileGeneration = r.UInt16(),
                FileVersion = r.UInt16()
            };

            if (dna.FileGeneration != 2)
            {
                throw new DnaParseException(
                    $"Unsupported DNA generation {dna.FileGeneration}.{dna.FileVersion}. " +
                    "This reader implements generation 2 (every MetaHuman release to date).");
            }

            if (dna.FileVersion <= 1)
                ParseLegacySections(r, dna, options);
            else
                ParseIndexedLayers(r, dna, options);

            return dna;
        }

        // ---------------------------------------------------------------- layer dispatch

        static void ParseIndexedLayers(Cursor r, DnaFile dna, DnaReadOptions options)
        {
            int indexCount = checked((int)r.UInt32());
            var entries = new (uint id, uint version, uint offset, uint size)[indexCount];
            for (int i = 0; i < indexCount; i++)
                entries[i] = (r.UInt32(), r.UInt32(), r.UInt32(), r.UInt32());

            foreach (var entry in entries)
            {
                r.Position = checked((int)entry.offset);
                switch (entry.id)
                {
                    case IdDescriptor:
                        options.Progress?.Invoke("Descriptor", 0f);
                        ReadDescriptor(r, dna.Descriptor);
                        break;
                    case IdDefinition:
                        options.Progress?.Invoke("Definition", 0.05f);
                        ReadDefinition(r, dna.Definition);
                        break;
                    case IdBehavior:
                        options.Progress?.Invoke("Behavior", 0.15f);
                        ReadBehavior(r, dna.Behavior);
                        break;
                    case IdGeometry:
                        if (options.ReadGeometry)
                        {
                            options.Progress?.Invoke("Geometry", 0.3f);
                            ReadGeometry(r, dna, options, legacyMeshLayout: false);
                        }
                        break;
                    case IdDescriptorExt:
                        ReadDescriptorExt(r, dna.Descriptor, dna.FileVersion);
                        break;
                    default:
                        dna.UnsupportedLayers.Add(FourCc(entry.id));
                        break;
                }
            }
        }

        static void ParseLegacySections(Cursor r, DnaFile dna, DnaReadOptions options)
        {
            // SectionLookupTable: descriptor, definition, behavior, controls, joints,
            // blendShapeChannels, animatedMaps, geometry.
            uint descriptorOffset = r.UInt32();
            uint definitionOffset = r.UInt32();
            r.UInt32(); // behavior - the four sub-section offsets below are authoritative
            uint controlsOffset = r.UInt32();
            uint jointsOffset = r.UInt32();
            uint blendShapeChannelsOffset = r.UInt32();
            uint animatedMapsOffset = r.UInt32();
            uint geometryOffset = r.UInt32();

            r.Position = checked((int)descriptorOffset);
            ReadDescriptor(r, dna.Descriptor);

            r.Position = checked((int)definitionOffset);
            ReadDefinition(r, dna.Definition);

            r.Position = checked((int)controlsOffset);
            ReadControls(r, dna.Behavior.Controls);
            r.Position = checked((int)jointsOffset);
            ReadJoints(r, dna.Behavior.Joints);
            r.Position = checked((int)blendShapeChannelsOffset);
            ReadBlendShapeChannels(r, dna.Behavior.BlendShapeChannels);
            r.Position = checked((int)animatedMapsOffset);
            ReadAnimatedMaps(r, dna.Behavior.AnimatedMaps);

            if (options.ReadGeometry)
            {
                r.Position = checked((int)geometryOffset);
                ReadGeometry(r, dna, options, legacyMeshLayout: true);
            }
        }

        // ---------------------------------------------------------------- descriptor

        static void ReadDescriptor(Cursor r, DnaDescriptor d)
        {
            d.Name = r.String();
            d.Archetype = r.UInt16();
            d.Gender = r.UInt16();
            d.Age = r.UInt16();

            int metaCount = checked((int)r.UInt32());
            d.Metadata.Clear();
            for (int i = 0; i < metaCount; i++)
            {
                string key = r.String();
                string value = r.String();
                d.Metadata.Add(new KeyValuePair<string, string>(key, value));
            }

            d.TranslationUnit = (DnaTranslationUnit)r.UInt16();
            d.RotationUnit = (DnaRotationUnit)r.UInt16();
            d.CoordinateSystem = new DnaCoordinateSystem
            {
                X = (DnaAxisDirection)r.UInt16(),
                Y = (DnaAxisDirection)r.UInt16(),
                Z = (DnaAxisDirection)r.UInt16()
            };
            d.LodCount = r.UInt16();
            d.MaxLod = r.UInt16();
            d.Complexity = r.String();
            d.DbName = r.String();
        }

        static void ReadDescriptorExt(Cursor r, DnaDescriptor d, int fileVersion)
        {
            d.RotationSequence = (DnaRotationSequence)r.UInt32();
            d.RotationSignX = SignFromRotDir(r.UInt32());
            d.RotationSignY = SignFromRotDir(r.UInt32());
            d.RotationSignZ = SignFromRotDir(r.UInt32());
            // The layer appears in file version 2.7, but faceWindingOrder was only appended in 2.8.
            if (fileVersion >= 8)
                d.FaceWindingOrder = (DnaFaceWindingOrder)r.UInt8();
        }

        static int SignFromRotDir(uint raw)
        {
            // tdm::rot_dir { negative = -1, positive = 1 } serialised through uint32.
            return unchecked((int)raw) < 0 ? -1 : 1;
        }

        // ---------------------------------------------------------------- definition

        static void ReadDefinition(Cursor r, DnaDefinition d)
        {
            d.LodJointMapping = ReadLodMapping(r);
            d.LodBlendShapeMapping = ReadLodMapping(r);
            d.LodAnimatedMapMapping = ReadLodMapping(r);
            d.LodMeshMapping = ReadLodMapping(r);

            d.GuiControlNames = r.StringArray();
            d.RawControlNames = r.StringArray();
            d.JointNames = r.StringArray();
            d.BlendShapeChannelNames = r.StringArray();
            d.AnimatedMapNames = r.StringArray();
            d.MeshNames = r.StringArray();

            d.MeshBlendShapeChannelFrom = r.UInt16Array();
            d.MeshBlendShapeChannelTo = r.UInt16Array();

            d.JointHierarchy = r.UInt16Array();

            d.NeutralJointTranslationX = r.FloatArray();
            d.NeutralJointTranslationY = r.FloatArray();
            d.NeutralJointTranslationZ = r.FloatArray();
            d.NeutralJointRotationX = r.FloatArray();
            d.NeutralJointRotationY = r.FloatArray();
            d.NeutralJointRotationZ = r.FloatArray();
        }

        static DnaLodMapping ReadLodMapping(Cursor r)
        {
            var mapping = new DnaLodMapping { Lods = r.UInt16Array() };
            int rows = checked((int)r.UInt32());
            mapping.Indices = new ushort[rows][];
            for (int i = 0; i < rows; i++)
                mapping.Indices[i] = r.UInt16Array();
            return mapping;
        }

        // ---------------------------------------------------------------- behavior

        static void ReadBehavior(Cursor r, DnaBehavior b)
        {
            ReadControls(r, b.Controls);
            ReadJoints(r, b.Joints);
            ReadBlendShapeChannels(r, b.BlendShapeChannels);
            ReadAnimatedMaps(r, b.AnimatedMaps);
        }

        static void ReadControls(Cursor r, DnaControls c)
        {
            c.PsdCount = r.UInt16();
            c.Conditionals = ReadConditionalTable(r);
            c.Psds = new DnaPsdMatrix
            {
                Rows = r.UInt16Array(),
                Columns = r.UInt16Array(),
                Values = r.FloatArray()
            };
        }

        static DnaConditionalTable ReadConditionalTable(Cursor r)
        {
            return new DnaConditionalTable
            {
                InputIndices = r.UInt16Array(),
                OutputIndices = r.UInt16Array(),
                FromValues = r.FloatArray(),
                ToValues = r.FloatArray(),
                SlopeValues = r.FloatArray(),
                CutValues = r.FloatArray()
            };
        }

        static void ReadJoints(Cursor r, DnaJoints j)
        {
            j.RowCount = r.UInt16();
            j.ColCount = r.UInt16();
            int groupCount = checked((int)r.UInt32());
            j.JointGroups = new DnaJointGroup[groupCount];
            for (int i = 0; i < groupCount; i++)
            {
                j.JointGroups[i] = new DnaJointGroup
                {
                    Lods = r.UInt16Array(),
                    InputIndices = r.UInt16Array(),
                    OutputIndices = r.UInt16Array(),
                    Values = r.FloatArray(),
                    JointIndices = r.UInt16Array()
                };
            }
        }

        static void ReadBlendShapeChannels(Cursor r, DnaBlendShapeChannels b)
        {
            b.Lods = r.UInt16Array();
            b.InputIndices = r.UInt16Array();
            b.OutputIndices = r.UInt16Array();
        }

        static void ReadAnimatedMaps(Cursor r, DnaAnimatedMaps a)
        {
            a.Lods = r.UInt16Array();
            a.Conditionals = ReadConditionalTable(r);
        }

        // ---------------------------------------------------------------- geometry

        static void ReadGeometry(Cursor r, DnaFile dna, DnaReadOptions options, bool legacyMeshLayout)
        {
            int meshCount = checked((int)r.UInt32());
            var meshes = new DnaMesh[meshCount];

            for (int i = 0; i < meshCount; i++)
            {
                bool wanted = options.MeshIndexFilter == null || options.MeshIndexFilter.Contains(i);

                int skipTo;
                if (legacyMeshLayout)
                {
                    // v2.1 stores an absolute stream offset pointing at the next mesh.
                    skipTo = checked((int)r.UInt32());
                }
                else
                {
                    uint size = r.UInt32();
                    skipTo = r.Position + checked((int)size);
                }

                if (!wanted)
                {
                    meshes[i] = new DnaMesh { Name = NameOrDefault(dna.Definition.MeshNames, i) };
                    r.Position = skipTo;
                    continue;
                }

                options.Progress?.Invoke($"Mesh {i + 1}/{meshCount}", 0.3f + 0.6f * (i / (float)Math.Max(1, meshCount)));
                meshes[i] = ReadMesh(r, options);
                meshes[i].Name = NameOrDefault(dna.Definition.MeshNames, i);
                r.Position = skipTo;
            }

            dna.Geometry.Meshes = meshes;
        }

        static string NameOrDefault(string[] names, int index)
        {
            return names != null && index < names.Length ? names[index] : $"mesh_{index}";
        }

        static DnaMesh ReadMesh(Cursor r, DnaReadOptions options)
        {
            var m = new DnaMesh();

            m.PositionX = r.FloatArray();
            m.PositionY = r.FloatArray();
            m.PositionZ = r.FloatArray();

            m.TexCoordU = r.FloatArray();
            m.TexCoordV = r.FloatArray();

            m.NormalX = r.FloatArray();
            m.NormalY = r.FloatArray();
            m.NormalZ = r.FloatArray();

            m.LayoutPosition = r.UInt32Array();
            m.LayoutTexCoord = r.UInt32Array();
            m.LayoutNormal = r.UInt32Array();

            int faceCount = checked((int)r.UInt32());
            m.Faces = new uint[faceCount][];
            for (int i = 0; i < faceCount; i++)
                m.Faces[i] = r.UInt32Array();

            m.MaximumInfluencePerVertex = r.UInt16();

            int skinCount = checked((int)r.UInt32());
            m.SkinWeights = new DnaVertexSkinWeights[skinCount];
            for (int i = 0; i < skinCount; i++)
            {
                m.SkinWeights[i] = new DnaVertexSkinWeights
                {
                    Weights = r.FloatArray(),
                    JointIndices = r.UInt16Array()
                };
            }

            int targetCount = checked((int)r.UInt32());
            if (options.ReadBlendShapes)
            {
                m.BlendShapeTargets = new DnaBlendShapeTarget[targetCount];
                for (int i = 0; i < targetCount; i++)
                {
                    m.BlendShapeTargets[i] = new DnaBlendShapeTarget
                    {
                        DeltaX = r.FloatArray(),
                        DeltaY = r.FloatArray(),
                        DeltaZ = r.FloatArray(),
                        VertexIndices = r.UInt32Array(),
                        BlendShapeChannelIndex = r.UInt16()
                    };
                }
            }
            else
            {
                m.BlendShapeTargets = Array.Empty<DnaBlendShapeTarget>();
                // The caller seeks past the mesh afterwards, so no need to consume the targets.
            }

            return m;
        }

        static string FourCc(uint id)
        {
            return new string(new[]
            {
                (char)((id >> 24) & 0xFF),
                (char)((id >> 16) & 0xFF),
                (char)((id >> 8) & 0xFF),
                (char)(id & 0xFF)
            });
        }

        // ---------------------------------------------------------------- cursor

        /// <summary>Big-endian cursor over the whole file. All DNA scalars are network order.</summary>
        sealed class Cursor
        {
            readonly byte[] _data;
            int _pos;

            public Cursor(byte[] data) { _data = data; }

            public int Position
            {
                get => _pos;
                set
                {
                    if (value < 0 || value > _data.Length)
                        throw new DnaParseException($"DNA seek out of range: {value} (file is {_data.Length} bytes).");
                    _pos = value;
                }
            }

            public int Remaining => _data.Length - _pos;

            void Need(int count)
            {
                if (count < 0 || _pos + count > _data.Length)
                    throw new DnaParseException(
                        $"DNA truncated: wanted {count} bytes at {_pos}, file is {_data.Length} bytes.");
            }

            public byte UInt8()
            {
                Need(1);
                return _data[_pos++];
            }

            public ushort UInt16()
            {
                Need(2);
                ushort v = (ushort)((_data[_pos] << 8) | _data[_pos + 1]);
                _pos += 2;
                return v;
            }

            public uint UInt32()
            {
                Need(4);
                uint v = (uint)((_data[_pos] << 24) | (_data[_pos + 1] << 16) | (_data[_pos + 2] << 8) | _data[_pos + 3]);
                _pos += 4;
                return v;
            }

            public float Single()
            {
                return BitConverter.Int32BitsToSingle(unchecked((int)UInt32()));
            }

            public string String()
            {
                int len = checked((int)UInt32());
                Need(len);
                string s = System.Text.Encoding.UTF8.GetString(_data, _pos, len);
                _pos += len;
                return s;
            }

            public string[] StringArray()
            {
                int count = checked((int)UInt32());
                var result = new string[count];
                for (int i = 0; i < count; i++) result[i] = String();
                return result;
            }

            public ushort[] UInt16Array()
            {
                int count = checked((int)UInt32());
                Need(count * 2);
                var result = new ushort[count];
                int p = _pos;
                for (int i = 0; i < count; i++, p += 2)
                    result[i] = (ushort)((_data[p] << 8) | _data[p + 1]);
                _pos = p;
                return result;
            }

            public uint[] UInt32Array()
            {
                int count = checked((int)UInt32());
                Need(count * 4);
                var result = new uint[count];
                int p = _pos;
                for (int i = 0; i < count; i++, p += 4)
                    result[i] = (uint)((_data[p] << 24) | (_data[p + 1] << 16) | (_data[p + 2] << 8) | _data[p + 3]);
                _pos = p;
                return result;
            }

            public float[] FloatArray()
            {
                int count = checked((int)UInt32());
                Need(count * 4);
                var result = new float[count];
                int p = _pos;
                for (int i = 0; i < count; i++, p += 4)
                {
                    int bits = (_data[p] << 24) | (_data[p + 1] << 16) | (_data[p + 2] << 8) | _data[p + 3];
                    result[i] = BitConverter.Int32BitsToSingle(bits);
                }
                _pos = p;
                return result;
            }
        }
    }
}
