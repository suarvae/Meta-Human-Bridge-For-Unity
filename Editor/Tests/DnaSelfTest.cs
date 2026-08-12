// MetaHuman Bridge - parser self-test.
//
// The blob below is a complete DNA 2.8 file assembled from the byte fixtures in
// EpicGames/OpenRigLogic (tests/dnatests/Fixturesv28.cpp). The expected values come from the
// DecodedV28 struct in the same file, so this checks the reader against Epic's own ground
// truth rather than against itself. Run it from Window > MetaHuman Bridge > Inspect DNA, or
// from the menu item below, whenever Epic bumps the DNA format.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public static class DnaSelfTest
    {
        [MenuItem("Window/MetaHuman Bridge Self Test")]
        public static void RunFromMenu()
        {
            string report = Run(out bool passed);
            if (passed) Debug.Log(report);
            else Debug.LogError(report);
        }

        /// <summary>Parses the embedded fixture and compares against Epic's decoded values.</summary>
        public static string Run(out bool passed)
        {
            var sb = new StringBuilder("MetaHuman Bridge DNA self-test\n");
            var failures = new List<string>();

            void Check(string label, object actual, object expected)
            {
                bool ok = Equals(Convert.ToString(actual), Convert.ToString(expected));
                if (!ok) failures.Add($"{label}: got {actual}, expected {expected}");
                sb.AppendLine($"  {(ok ? "pass" : "FAIL")}  {label}");
            }

            try
            {
                DnaFile dna = DnaBinaryReader.Parse(Convert.FromBase64String(FixtureBase64));

                Check("file version", $"{dna.FileGeneration}.{dna.FileVersion}", "2.8");
                Check("descriptor name", dna.Descriptor.Name, "test");
                Check("age", dna.Descriptor.Age, 42);
                Check("metadata", dna.Descriptor.Metadata.Count, 2);
                Check("translation unit", dna.Descriptor.TranslationUnit, DnaTranslationUnit.Metre);
                Check("rotation unit", dna.Descriptor.RotationUnit, DnaRotationUnit.Radians);
                Check("coordinate system",
                    $"{dna.Descriptor.CoordinateSystem.X}/{dna.Descriptor.CoordinateSystem.Y}/{dna.Descriptor.CoordinateSystem.Z}",
                    "Right/Up/Front");
                Check("rotation sequence", dna.Descriptor.RotationSequence, DnaRotationSequence.YZX);
                Check("rotation signs",
                    $"{dna.Descriptor.RotationSignX},{dna.Descriptor.RotationSignY},{dna.Descriptor.RotationSignZ}",
                    "1,-1,1");
                Check("face winding", dna.Descriptor.FaceWindingOrder, DnaFaceWindingOrder.Clockwise);
                Check("lod count", dna.Descriptor.LodCount, 2);

                DnaDefinition def = dna.Definition;
                Check("gui controls", string.Join(",", def.GuiControlNames), "GA,GB,GC,GD,GE,GF,GG,GH,GI");
                Check("raw controls", string.Join(",", def.RawControlNames), "RA,RB,RC,RD,RE,RF,RG,RH,RI");
                Check("joints", string.Join(",", def.JointNames), "JA,JB,JC,JD,JE,JF,JG,JH,JI");
                Check("meshes", string.Join(",", def.MeshNames), "MA,MB,MC");
                Check("joint hierarchy", string.Join(",", def.JointHierarchy), "0,0,0,1,1,4,2,4,2");
                Check("neutral translation x[8]", Mathf.RoundToInt(def.NeutralJointTranslationX[8]), 9);
                Check("lod 0 meshes", string.Join(",", def.LodMeshMapping.GetIndices(0)), "0,1");
                Check("lod 1 meshes", string.Join(",", def.LodMeshMapping.GetIndices(1)), "2");

                DnaBehavior behavior = dna.Behavior;
                Check("psd count", behavior.Controls.PsdCount, 12);
                Check("conditional rows", behavior.Controls.Conditionals.RowCount, 15);
                Check("conditional inputs",
                    string.Join(",", behavior.Controls.Conditionals.InputIndices),
                    "0,1,1,2,3,3,4,4,4,5,6,7,7,8,8");
                Check("psd rows", behavior.Controls.Psds.Rows.Length, 24);
                Check("joint attribute rows", behavior.Joints.RowCount, 81);
                Check("joint groups", behavior.Joints.JointGroups.Length, 4);
                Check("group 0 inputs", string.Join(",", behavior.Joints.JointGroups[0].InputIndices), "0,1,2,3,6,7,8");
                Check("group 1 joints", string.Join(",", behavior.Joints.JointGroups[1].JointIndices), "2,4");

                foreach (DnaJointGroup group in behavior.Joints.JointGroups)
                {
                    Check($"group matrix is {group.OutputIndices.Length}x{group.InputIndices.Length}",
                        group.Values.Length,
                        group.OutputIndices.Length * group.InputIndices.Length);
                }

                Check("mesh count", dna.Geometry.Meshes.Length, 3);
                Check("mesh MA vertices", dna.Geometry.Meshes[0].LayoutCount, 3);
                Check("mesh MC blend shapes", dna.Geometry.Meshes[2].BlendShapeTargets.Length, 2);

                // Round-trip the coordinate conversion the importer will use.
                DnaSpace space = DnaSpace.FromDescriptor(dna.Descriptor);
                Check("identity basis detected", space.IsIdentity, true);
                Check("metre units keep scale", Mathf.Approximately(space.UnitScale, 1f), true);
                Check("clockwise winding is reversed", space.ReverseWinding, true);
            }
            catch (Exception e)
            {
                failures.Add("threw " + e.GetType().Name + ": " + e.Message);
                sb.AppendLine("  FAIL  exception during parse");
            }

            passed = failures.Count == 0;
            sb.AppendLine();
            sb.AppendLine(passed
                ? "All checks passed - the DNA reader matches Epic's reference fixture."
                : $"{failures.Count} check(s) failed:\n" + string.Join("\n", failures.Select(f => "  - " + f)));

            return sb.ToString();
        }

        const string FixtureBase64 =
            "RE5BAAIACAAAAAtkZXNjAAEAAQAAALsAAABXZGVmbgABAAEAAAESAAADGmJodnIAAQABAAAELAAABUZnZW9tAAEAAQAACXIAAAQ4bWxiaAABAAAAAA2qAAAC" +
            "+nJiZmIAAQAAAAAQpAAAAUdyYmZlAAEAAAAAEesAAADkamJtZAABAAAAABLPAAAAOnR3c3cAAQAAAAATCQAAAMhtbGJlAAEAAAAAE9EAAAHAZHNjZQABAAAA" +
            "ABWRAAAAEQAAAAR0ZXN0AAUAAgAqAAAAAgAAAAVrZXktQQAAAAd2YWx1ZS1BAAAABWtleS1CAAAAB3ZhbHVlLUIAAQABAAEAAgAEAAIAAAAAAAFBAAAABnRl" +
            "c3REQgAAAAIAAAABAAAAAgAAAAkAAAABAAIAAwAEAAUABgAHAAgAAAAGAAAAAQACAAMABgAIAAAAAgAAAAEAAAACAAAACQAAAAEAAgADAAQABQAGAAcACAAA" +
            "AAQAAgAFAAcACAAAAAIAAAABAAAAAgAAAAoAAAABAAIAAwAEAAUABgAHAAgACQAAAAQAAgAFAAcACAAAAAIAAAABAAAAAgAAAAIAAAABAAAAAQACAAAACQAA" +
            "AAJHQQAAAAJHQgAAAAJHQwAAAAJHRAAAAAJHRQAAAAJHRgAAAAJHRwAAAAJHSAAAAAJHSQAAAAkAAAACUkEAAAACUkIAAAACUkMAAAACUkQAAAACUkUAAAAC" +
            "UkYAAAACUkcAAAACUkgAAAACUkkAAAAJAAAAAkpBAAAAAkpCAAAAAkpDAAAAAkpEAAAAAkpFAAAAAkpGAAAAAkpHAAAAAkpIAAAAAkpJAAAACQAAAAJCQQAA" +
            "AAJCQgAAAAJCQwAAAAJCRAAAAAJCRQAAAAJCRgAAAAJCRwAAAAJCSAAAAAJCSQAAAAoAAAACQUEAAAACQUIAAAACQUMAAAACQUQAAAACQUUAAAACQUYAAAAC" +
            "QUcAAAACQUgAAAACQUkAAAACQUoAAAADAAAAAk1BAAAAAk1CAAAAAk1DAAAACQAAAAAAAAABAAEAAQABAAIAAgAAAAkAAAABAAIAAwAEAAUABgAHAAgAAAAJ" +
            "AAAAAAAAAAEAAQAEAAIABAACAAAACT+AAABAAAAAQEAAAECAAABAoAAAQMAAAEDgAABBAAAAQRAAAAAAAAk/gAAAQAAAAEBAAABAgAAAQKAAAEDAAABA4AAA" +
            "QQAAAEEQAAAAAAAJP4AAAEAAAABAQAAAQIAAAECgAABAwAAAQOAAAEEAAABBEAAAAAAACT+AAABAAAAAQEAAAECAAABAoAAAQMAAAEDgAABBAAAAQRAAAAAA" +
            "AAk/gAAAQAAAAEBAAABAgAAAQKAAAEDAAABA4AAAQQAAAEEQAAAAAAAJP4AAAEAAAABAQAAAQIAAAECgAABAwAAAQOAAAEEAAABBEAAAAAwAAAAPAAAAAQAB" +
            "AAIAAwADAAQABAAEAAUABgAHAAcACAAIAAAADwAAAAEAAQACAAMAAwAEAAQABAAFAAYABwAHAAgACAAAAA8AAAAAAAAAAD8ZmZo+zMzNPczMzT8zMzMAAAAA" +
            "PszMzT8zMzM/AAAAAAAAAD3MzM0/GZmaPkzMzQAAAAAAAAAPP4AAAD8ZmZo/gAAAP2ZmZj8zMzM/gAAAPszMzT8zMzM/gAAAP4AAAD+AAAA/GZmaP4AAAD9M" +
            "zM0/gAAAAAAADz+AAAA/ZmZmP2ZmZj9MzM0/MzMzPzMzMz8ZmZo/GZmaPxmZmj8AAAA/GZmaPzMzMz8zMzM/TMzNP2ZmZgAAAA8AAAAAPwAAAD8AAAA+zMzN" +
            "PpmZmj6ZmZo/gAAAP4AAAD+AAAA+TMzNPszMzT9MzM0/TMzNP4AAAD5MzM0AAAAYAAgACAAIAAkACQAKAAoACgALAAwADQANAA0ADgAOAA8AEAASABIAEgAS" +
            "ABMAEwAUAAAAGAAAAAMABgACAAUAAgADAAcAAwACAAAAAQACAAMABgAAAAQAAAADAAQABQAGAAcAAgAAABg/gAAAP2ZmZj9mZmY/GZmaP4AAAD9MzM0/ZmZm" +
            "P0zMzT+AAAA+mZmaP4AAAD9mZmY/gAAAP2ZmZj8AAAA/AAAAP2ZmZj8zMzM/GZmaP4AAAD+AAAA/gAAAPxmZmj+AAAAAUQAKAAAABAAAAAIAAwADAAAABwAA" +
            "AAEAAgADAAYABwAIAAAAAwACAAMABQAAABUAAAAAPUzMzT3MzM0+GZmaPkzMzT6AAAA+mZmaPrMzMz7MzM0+5mZmPwAAAD8MzM0/GZmaPyZmZj8zMzM/QAAA" +
            "P0zMzT9ZmZo/ZmZmP3MzMz+AAAAAAAABAAAAAAACAAQAAgAAAAUAAwAEAAcACAAJAAAABAASABQAJAAmAAAAFDwj1wo8o9cKPPXCjz0j1wo9TMzNPXXCjz2P" +
            "XCk9o9cKPbhR7D3MzM094UeuPfXCjz4FHrg+D1wpPhmZmj4j1wo+LhR7PjhR7D5Cj1w+TMzNAAAAAgACAAQAAAACAAMAAgAAAAQABAAFAAgACQAAAAMANwA4" +
            "AD8AAAAMPp64Uj64Uew+1wo9PvCj1z8HrhQ/FHrhPyPXCj8wo9c/QAAAP0zMzT9cKPY/aPXDAAAAAgAGAAcAAAACAAMAAAAAAAQAAgAFAAYACAAAAAMALQAu" +
            "AEcAAAAMPp64Uj64Uew+1wo9PvCj1z8HrhQ/FHrhPyPXCj8wo9c/QAAAP0zMzT9cKPY/aPXDAAAAAgAFAAcAAAACAAcABAAAAAcAAAABAAIAAwAGAAcACAAA" +
            "AAcAAAABAAIAAwAGAAcACAAAAAIADwAGAAAADwAAAAEAAQACAAMAAwAEAAQABAAFAAYABwAHAAgACAAAAA8AAAABAAEAAgADAAMABAAEAAQABQAGAAcABwAI" +
            "AAgAAAAPAAAAAAAAAAA/GZmaPszMzT3MzM0/MzMzAAAAAD7MzM0/MzMzPwAAAAAAAAA9zMzNPxmZmj5MzM0AAAAAAAAADz+AAAA/GZmaP4AAAD9mZmY/MzMz" +
            "P4AAAD7MzM0/MzMzP4AAAD+AAAA/gAAAPxmZmj+AAAA/TMzNP4AAAAAAAA8/gAAAP2ZmZj9mZmY/TMzNPzMzMz8zMzM/GZmaPxmZmj8ZmZo/AAAAPxmZmj8z" +
            "MzM/MzMzP0zMzT9mZmYAAAAPAAAAAD8AAAA/AAAAPszMzT6ZmZo+mZmaP4AAAD+AAAA/gAAAPkzMzT7MzM0/TMzNP0zMzT+AAAA+TMzNAAAAAwAAAVIAAAAD" +
            "QOAAAEEAAABBEAAAAAAAA0DgAABBAAAAQRAAAAAAAANA4AAAQQAAAEEQAAAAAAADQOAAAEEAAABBEAAAAAAAA0DgAABBAAAAQRAAAAAAAANA4AAAQQAAAEEQ" +
            "AAAAAAADQOAAAEEAAABBEAAAAAAAA0DgAABBAAAAQRAAAAAAAAMAAAAAAAAAAQAAAAIAAAADAAAAAAAAAAEAAAACAAAAAwAAAAAAAAABAAAAAgAAAAEAAAAD" +
            "AAAAAAAAAAEAAAACAAgAAAADAAAAAz8zMzM9zMzNPkzMzQAAAAMAAAABAAIAAAACPwAAAD8AAAAAAAACAAMABAAAAAI+zMzNPxmZmgAAAAIABQAGAAAAAQAA" +
            "AANA4AAAQQAAAEEQAAAAAAADQOAAAEEAAABBEAAAAAAAA0DgAABBAAAAQRAAAAAAAAMAAAAAAAAAAQAAAAIAAgAAAVIAAAADQIAAAECgAABAwAAAAAAAA0CA" +
            "AABAoAAAQMAAAAAAAANAgAAAQKAAAEDAAAAAAAADQIAAAECgAABAwAAAAAAAA0CAAABAoAAAQMAAAAAAAANAgAAAQKAAAEDAAAAAAAADQIAAAECgAABAwAAA" +
            "AAAAA0CAAABAoAAAQMAAAAAAAAMAAAAAAAAAAQAAAAIAAAADAAAAAAAAAAEAAAACAAAAAwAAAAAAAAABAAAAAgAAAAEAAAADAAAAAAAAAAEAAAACAAgAAAAD" +
            "AAAAAz7MzM0+mZmaPpmZmgAAAAMAAAABAAIAAAACP0zMzT5MzM0AAAACAAMABAAAAAI9zMzNP2ZmZgAAAAIABQAGAAAAAQAAAANAgAAAQKAAAEDAAAAAAAAD" +
            "QIAAAECgAABAwAAAAAAAA0CAAABAoAAAQMAAAAAAAAMAAAAAAAAAAQAAAAIAAgAAAYQAAAADP4AAAEAAAABAQAAAAAAAAz+AAABAAAAAQEAAAAAAAAM/gAAA" +
            "QAAAAEBAAAAAAAADP4AAAEAAAABAQAAAAAAAAz+AAABAAAAAQEAAAAAAAAM/gAAAQAAAAEBAAAAAAAADP4AAAEAAAABAQAAAAAAAAz+AAABAAAAAQEAAAAAA" +
            "AAMAAAAAAAAAAQAAAAIAAAADAAAAAAAAAAEAAAACAAAAAwAAAAAAAAABAAAAAgAAAAEAAAADAAAAAAAAAAEAAAACAAgAAAADAAAAAz3MzM0+mZmaPxmZmgAA" +
            "AAMAAAABAAIAAAACPpmZmj8zMzMAAAACAAMABAAAAAI+TMzNP0zMzQAAAAIABQAGAAAAAgAAAAM/gAAAQAAAAEBAAAAAAAADP4AAAEAAAABAQAAAAAAAAz+A" +
            "AABAAAAAQEAAAAAAAAMAAAAAAAAAAQAAAAIAAgAAAAJAgAAAQKAAAAAAAAJAgAAAQKAAAAAAAAJAgAAAQKAAAAAAAAIAAAAAAAAAAgADAAAACQAAAAJNQQAA" +
            "AAJNQgAAAAJNQwAAAAJNRAAAAAJNRQAAAAJNRgAAAAJNRwAAAAJNSAAAAAJNSQAAAAIAAAABAAAAAgAAAAQAAAABAAIAAwAAAAIABAAFAAAAAwAAAAIAAAAC" +
            "UkEAAAACUkIAAAACAAAAAlJDAAAAAlJEAAAAAgAAAAJSRQAAAAJSRgAAAAMAAAACAAAAAQAAAAAAAQABAAAAAgAAAAEAAgAAAAEAAwAAAAIAAAABAAQAAAAB" +
            "AAUAAAAGAAAAWgAAAAEACQAAAAIAAAABAAAAAgAAAAI/gAAAP4AAAAAAAAQ/AAAAPwAAAD8AAAA/AAAAAAEAAAABPwAAAAAAAAE/gAAAAAAAAj8AAAA/AAAA" +
            "AAEAAAABPwAAAAAAAFoAAAABAAoAAAACAAIAAwAAAAIAAAACPwAAAD8AAAAAAAAEP4AAAD+AAAA/gAAAP4AAAAABAAAAAT+AAAAAAAABPwAAAAAAAAI/gAAA" +
            "P4AAAAABAAAAAT+AAAAAAABaAAAAAQALAAAAAgAEAAUAAAACAAAAAj8AAAA/AAAAAAAABD+AAAA/gAAAP4AAAD+AAAAAAQAAAAE/gAAAAAAAAT8AAAAAAAAC" +
            "P4AAAD+AAAAAAQAAAAE/gAAAAAAAWgAAAAEADAAAAAIABgAHAAAAAgAAAAI/gAAAP4AAAAAAAAQ/AAAAPwAAAD8AAAA/AAAAAAEAAAABPwAAAAAAAAE/gAAA" +
            "AAAAAj8AAAA/AAAAAAEAAAABPwAAAAAAAFoAAAABAA0AAAACAAgAAAAAAAIAAAACP4AAAD+AAAAAAAAEPwAAAD8AAAA/AAAAPwAAAAABAAAAAT8AAAAAAAAB" +
            "P4AAAAAAAAI/AAAAPwAAAAABAAAAAT8AAAAAAABaAAAAAQAOAAAAAgAEAAcAAAACAAAAAj8AAAA/AAAAAAAABD+AAAA/gAAAP4AAAD+AAAAAAQAAAAE/gAAA" +
            "AAAAAT8AAAAAAAACP4AAAD+AAAAAAQAAAAE/gAAAAAAAAgAAAAEAAAACAAAAAgAAAAEAAAACAAEAAgAAAAMAAABJAAAAA1JTQQAAAAIACwAMAAAAAwAAAAEA" +
            "AgAAAAZAAAAAAAAAAD+AAAA/gAAAQEAAAMBAAAA/gAAAP4AAAAAAAAAAAQAAAAIAAAAAADUAAAADUlNCAAAAAQADAAAAAgADAAQAAAACAAAAAECAAABAAAAA" +
            "QAAAAAABAAAAAwABAAIAAQAAAEkAAAADUlNDAAAAAgAWABcAAAADAAUABgAHAAAABkAAAAAAAAAAP4AAAD+AAABAQAAAwEAAAD+AAAA/gAAAAAAAAAABAAAA" +
            "AAAAAAAACAAAAAJSQQAAAAAAAAACUkI/gAAAAAAAAlJDQAAAAAAAAAJSREAAAAAAAAACUkU/gAAAAAAAAlJGP4AAAAAAAAJSRz+AAAAAAAACUkg/AAAAAAAA" +
            "CQAAAAJQQQAAAAJQQgAAAAJQQwAAAAJQRAAAAAJQRQAAAAJQRgAAAAJQRwAAAAJQSAAAAAJQSQAAAAgAAAABAAAAAAABAAgAAAABP4AAAAAAAAEAAQAAAAEA" +
            "CQAAAAE/gAAAAAAAAQACAAAAAQAKAAAAAT+AAAAAAAABAAMAAAABAAsAAAABP4AAAAAAAAEABAAAAAEADAAAAAE/gAAAAAAAAQAFAAAAAQANAAAAAT+AAAAA" +
            "AAABAAYAAAABAA4AAAABP4AAAAAAAAEABwAAAAIADwAQAAAAAj8AAAA/AAAAAAAACQAAAAAAAAAAAAAAAAAAAAEAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAEAAAAAAAAAAAAAAAMAAAACP4AAAEAAAAAAAAACAAAAAQAAAAQABQAGAAcACAAAAAAAAsAAAAC/gAAAAAAAAgAEAAYAAAAEAAsADAANAA4AAQAAAAE/" +
            "gAAAAAAAAQAFAAAABAAbABwAHQAeAAIAAAADAAAAAj+AAABAAAAAAAAAAgAAAAEAAAAEAAUABgAHAAgAAAAAAALAAAAAv4AAAAAAAAIABAAGAAAABAALAAwA" +
            "DQAOAAEAAAABP4AAAAAAAAEABQAAAAQAGwAcAB0AHgACAAAAAQAAAAMAAAACAAAAAQAAAAIAAAAEAAAAAQACAAMAAAAAAAAAAgAAAAEAAAACAAAABAAAAAEA" +
            "AgADAAAAAAAAAAIAAAABAAAAAgAAAAQAAAABAAIAAwAAAAAAAAADAAAABAAAAAIAAAABAAAABAAAAAAAAAAAAAEAAAADAAAAAAAAAAIAAAADAAAAAAAAAAAA" +
            "AQAAAAUAAAAFAAAABwAAAAgAAAAJAAAADAAAAAAAAAAAAAEAAAADAAAABgAAAAoAAAALAAAAAAAAAAAAAQAAAAQAAAABAAAAAAAAAAEAAAAAAAEAAAADAAAA" +
            "AQAAAAEAAAABAAAAAAABAAEAAwAAAAEAAAACAAAAAQAAAAAAAQACAAMAAAABAAAAAwAAAAEAAAAAAAEAAwADAAAABAAAAAEAAAANAAAAAQABAAAAAQAAAAIA" +
            "AAADAAAADgAAAA8AAAAQAAAAAQABAAAAAQABAAIAAAAGAAAAEQAAABIAAAATAAAAFAAAABUAAAAWAAAAAQABAAAAAQACAAIAAAAEAAAAFwAAABgAAAAZAAAA" +
            "GgAAAAEAAQAAAAEAAwACAAAAAAAAAAAAAAAAAAAAAAAAAAMAAAAB/////wAAAAEB";
    }
}
