// MetaHuman Bridge - editor front end.
//
// Two tabs: Import turns a DCC Export into a prefab; Inspect reads a .dna and reports what
// is inside it, which is the quickest way to confirm a file parses before committing to a
// full import.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public sealed class MetaHumanBridgeWindow : EditorWindow
    {
        const string PrefsPrefix = "RaveHouse.MetaHumanBridge.";

        enum Tab { Import, Inspect }

        Tab _tab = Tab.Import;
        readonly MetaHumanImportSettings _settings = new MetaHumanImportSettings();
        MetaHumanImportReport _report;
        Vector2 _scroll;

        string _inspectPath = string.Empty;
        DnaFile _inspected;
        string _inspectError;
        Vector2 _inspectScroll;
        string _selfTestReport;
        bool _selfTestPassed;

        [MenuItem("Window/MetaHuman Bridge")]
        public static void Open()
        {
            var window = GetWindow<MetaHumanBridgeWindow>("MetaHuman Bridge");
            window.minSize = new Vector2(460f, 480f);
        }

        void OnEnable()
        {
            _settings.bodyDnaPath = EditorPrefs.GetString(PrefsPrefix + "bodyDna", string.Empty);
            _settings.headDnaPath = EditorPrefs.GetString(PrefsPrefix + "headDna", string.Empty);
            _settings.textureFolder = EditorPrefs.GetString(PrefsPrefix + "textures", string.Empty);
            _settings.outputFolder = EditorPrefs.GetString(PrefsPrefix + "output", "Assets/MetaHumans");
            _settings.characterName = EditorPrefs.GetString(PrefsPrefix + "name", "MetaHuman");
            _inspectPath = EditorPrefs.GetString(PrefsPrefix + "inspect", string.Empty);
        }

        void OnDisable()
        {
            EditorPrefs.SetString(PrefsPrefix + "bodyDna", _settings.bodyDnaPath);
            EditorPrefs.SetString(PrefsPrefix + "headDna", _settings.headDnaPath);
            EditorPrefs.SetString(PrefsPrefix + "textures", _settings.textureFolder);
            EditorPrefs.SetString(PrefsPrefix + "output", _settings.outputFolder);
            EditorPrefs.SetString(PrefsPrefix + "name", _settings.characterName);
            EditorPrefs.SetString(PrefsPrefix + "inspect", _inspectPath);
        }

        void OnGUI()
        {
            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Import", "Inspect DNA" });
            EditorGUILayout.Space();

            if (_tab == Tab.Import) DrawImportTab();
            else DrawInspectTab();
        }

        // ------------------------------------------------------------------ import

        void DrawImportTab()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "In MetaHuman Creator, assemble your character with the DCC Export pipeline and extract the " +
                "archive. Point the fields below at the head and body .dna files and the folder of .png maps.",
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            _settings.headDnaPath = FilePathField("Head DNA", _settings.headDnaPath, "dna");
            _settings.bodyDnaPath = FilePathField("Body DNA", _settings.bodyDnaPath, "dna");
            _settings.textureFolder = FolderPathField("Texture folder", _settings.textureFolder);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            _settings.characterName = EditorGUILayout.TextField("Character name", _settings.characterName);
            _settings.outputFolder = EditorGUILayout.TextField("Output folder", _settings.outputFolder);
            EditorGUILayout.LabelField(" ", _settings.CharacterFolder, EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Geometry", EditorStyles.boldLabel);
            DrawLodSelector();
            _settings.createLodGroup = EditorGUILayout.Toggle("Create LODGroup", _settings.createLodGroup);
            _settings.scale = EditorGUILayout.FloatField(
                new GUIContent("Extra scale", "Applied on top of the DNA's own centimetre-to-metre conversion."),
                _settings.scale);
            _settings.flipV = EditorGUILayout.Toggle(
                new GUIContent("Flip UV V", "Only needed if textures appear vertically mirrored."),
                _settings.flipV);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rig", EditorStyles.boldLabel);
            _settings.blendShapeMode = (BlendShapeImportMode)EditorGUILayout.EnumPopup(
                new GUIContent("Blend shapes",
                    "The MetaHuman face is joint driven; blend shapes are correctives. Importing all of them at " +
                    "LOD 0 can add hundreds of megabytes to the mesh."),
                _settings.blendShapeMode);
            if (_settings.blendShapeMode == BlendShapeImportMode.All)
            {
                EditorGUILayout.HelpBox(
                    "Unity stores blend shape frames densely. A LOD 0 head with ~700 correctives can exceed " +
                    "200 MB. Import LOD 1+ or leave this on None if memory matters.",
                    MessageType.Warning);
            }

            _settings.buildRigLogic = EditorGUILayout.Toggle(
                new GUIContent("Bake RigLogic", "Bakes the head DNA behaviour layer and adds a MetaHumanRig component."),
                _settings.buildRigLogic);

            using (new EditorGUI.DisabledScope(!_settings.buildRigLogic))
            {
                EditorGUI.indentLevel++;
                _settings.faceRigDrivesBodyJoints = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Face rig drives body joints",
                        "Off: body animation keeps the head, neck, clavicles and upper arms. " +
                        "On: the face rig writes to them too, which overwrites the Animator on those joints."),
                    _settings.faceRigDrivesBodyJoints);
                EditorGUI.indentLevel--;
            }
            _settings.buildHumanoidAvatar = EditorGUILayout.Toggle("Build Humanoid avatar", _settings.buildHumanoidAvatar);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Materials", EditorStyles.boldLabel);
            _settings.createMaterials = EditorGUILayout.Toggle("Create materials", _settings.createMaterials);
            using (new EditorGUI.DisabledScope(!_settings.createMaterials))
                _settings.copyTextures = EditorGUILayout.Toggle("Copy textures into project", _settings.copyTextures);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                       string.IsNullOrEmpty(_settings.headDnaPath) && string.IsNullOrEmpty(_settings.bodyDnaPath)))
            {
                if (GUILayout.Button("Import MetaHuman", GUILayout.Height(30f)))
                    _report = MetaHumanImporter.Import(_settings);
            }

            DrawReport();
            EditorGUILayout.EndScrollView();
        }

        void DrawLodSelector()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("LODs");
            for (int lod = 0; lod < 8; lod++)
            {
                bool on = _settings.lods.Contains(lod);
                bool next = GUILayout.Toggle(on, lod.ToString(), EditorStyles.miniButton, GUILayout.Width(26f));
                if (next == on) continue;

                if (next) _settings.lods.Add(lod);
                else _settings.lods.Remove(lod);
                _settings.lods.Sort();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawReport()
        {
            if (_report == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

            foreach (string line in _report.Log)
                EditorGUILayout.LabelField("- " + line, EditorStyles.wordWrappedMiniLabel);

            foreach (string warning in _report.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            if (_report.Succeeded && GUILayout.Button("Select prefab"))
                Selection.activeObject = _report.Prefab;
        }

        // ------------------------------------------------------------------ inspect

        void DrawInspectTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Verify the reader against Epic's reference fixture:", GUILayout.Width(300f));
            if (GUILayout.Button("Run self-test"))
            {
                string result = DnaSelfTest.Run(out bool passed);
                _selfTestPassed = passed;
                _selfTestReport = result;
            }
            EditorGUILayout.EndHorizontal();

            if (_selfTestReport != null)
                EditorGUILayout.HelpBox(_selfTestReport, _selfTestPassed ? MessageType.Info : MessageType.Error);

            EditorGUILayout.Space();
            _inspectPath = FilePathField("DNA file", _inspectPath, "dna");

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_inspectPath)))
            {
                if (GUILayout.Button("Read"))
                {
                    _inspectError = null;
                    _inspected = null;
                    try
                    {
                        _inspected = DnaBinaryReader.Load(_inspectPath);
                    }
                    catch (Exception e)
                    {
                        _inspectError = e.Message;
                    }
                }
            }

            if (_inspectError != null)
            {
                EditorGUILayout.HelpBox(_inspectError, MessageType.Error);
                return;
            }

            if (_inspected == null) return;

            _inspectScroll = EditorGUILayout.BeginScrollView(_inspectScroll);

            DnaDescriptor d = _inspected.Descriptor;
            DnaDefinition def = _inspected.Definition;

            EditorGUILayout.LabelField("Descriptor", EditorStyles.boldLabel);
            Row("Name", d.Name);
            Row("File version", $"{_inspected.FileGeneration}.{_inspected.FileVersion}");
            Row("Units", $"{d.TranslationUnit}, {d.RotationUnit}");
            Row("Axes", $"X {d.CoordinateSystem.X}, Y {d.CoordinateSystem.Y}, Z {d.CoordinateSystem.Z}");
            Row("Rotation", $"{d.RotationSequence}, signs ({d.RotationSignX}, {d.RotationSignY}, {d.RotationSignZ})");
            Row("Winding", d.FaceWindingOrder.ToString());
            Row("LODs", d.LodCount.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Definition", EditorStyles.boldLabel);
            Row("Joints", def.JointCount.ToString());
            Row("GUI controls", def.GuiControlNames.Length.ToString());
            Row("Raw controls", def.RawControlNames.Length.ToString());
            Row("Correctives (PSD)", _inspected.Behavior.Controls.PsdCount.ToString());
            Row("Blend shape channels", def.BlendShapeChannelNames.Length.ToString());
            Row("Animated maps", def.AnimatedMapNames.Length.ToString());
            Row("Joint groups", _inspected.Behavior.Joints.JointGroups.Length.ToString());

            if (_inspected.UnsupportedLayers.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Layers present but not decoded: " + string.Join(", ", _inspected.UnsupportedLayers) +
                    ". These carry RBF, machine-learned and twist/swing correctives.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Meshes", EditorStyles.boldLabel);
            for (int i = 0; i < _inspected.Geometry.Meshes.Length; i++)
            {
                DnaMesh mesh = _inspected.Geometry.Meshes[i];
                string detail = mesh.LayoutCount == 0
                    ? "not decoded"
                    : $"{mesh.PositionCount} positions, {mesh.LayoutCount} vertices, " +
                      $"{mesh.Faces.Length} faces, {mesh.BlendShapeTargets.Length} shapes, " +
                      $"max {mesh.MaximumInfluencePerVertex} influences";
                Row(mesh.Name, detail);
            }

            EditorGUILayout.EndScrollView();
        }

        static void Row(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(180f));
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(16f));
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ fields

        static string FilePathField(string label, string value, string extension)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(value) ? "<none>" : Path.GetFileName(value),
                EditorStyles.textField, GUILayout.Height(18f));

            if (GUILayout.Button("...", GUILayout.Width(28f)))
            {
                string start = string.IsNullOrEmpty(value) ? Application.dataPath : Path.GetDirectoryName(value);
                string picked = EditorUtility.OpenFilePanel(label, start, extension);
                if (!string.IsNullOrEmpty(picked)) value = picked;
            }

            if (!string.IsNullOrEmpty(value) && GUILayout.Button("x", GUILayout.Width(20f)))
                value = string.Empty;

            EditorGUILayout.EndHorizontal();
            return value;
        }

        static string FolderPathField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(value) ? "<none>" : value,
                EditorStyles.textField, GUILayout.Height(18f));

            if (GUILayout.Button("...", GUILayout.Width(28f)))
            {
                string picked = EditorUtility.OpenFolderPanel(label, string.IsNullOrEmpty(value) ? Application.dataPath : value, string.Empty);
                if (!string.IsNullOrEmpty(picked)) value = picked;
            }

            EditorGUILayout.EndHorizontal();
            return value;
        }
    }
}
