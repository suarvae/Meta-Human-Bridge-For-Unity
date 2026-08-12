// MetaHuman Bridge - import configuration.

using System;
using System.Collections.Generic;

namespace RaveHouse.MetaHumanBridge.Editor
{
    [Serializable]
    public sealed class MetaHumanImportSettings
    {
        /// <summary>Absolute path to the body .dna from a DCC Export. Optional.</summary>
        public string bodyDnaPath = string.Empty;

        /// <summary>Absolute path to the head .dna from a DCC Export. Optional.</summary>
        public string headDnaPath = string.Empty;

        /// <summary>Folder containing the exported .png maps. Searched recursively.</summary>
        public string textureFolder = string.Empty;

        /// <summary>Project-relative output folder, e.g. "Assets/MetaHumans/Ada".</summary>
        public string outputFolder = "Assets/MetaHumans";

        /// <summary>Name used for the prefab and sub-assets.</summary>
        public string characterName = "MetaHuman";

        /// <summary>LODs to import. LOD 0 is the highest detail.</summary>
        public List<int> lods = new List<int> { 0 };

        /// <summary>Adds a LODGroup when more than one LOD is imported.</summary>
        public bool createLodGroup = true;

        public BlendShapeImportMode blendShapeMode = BlendShapeImportMode.None;

        /// <summary>Extra uniform scale on top of the DNA's own unit conversion.</summary>
        public float scale = 1f;

        /// <summary>Flip the V texture coordinate. DNA follows the DCC convention, which usually matches Unity.</summary>
        public bool flipV = false;

        public bool buildHumanoidAvatar = true;

        /// <summary>Bake the head DNA behaviour layer into a RigLogicAsset and wire up MetaHumanRig.</summary>
        public bool buildRigLogic = true;

        /// <summary>
        /// Let the face rig write to joints the body skeleton also owns (head, neck, clavicles,
        /// upper arms and their correctives - 27 of them on a stock MetaHuman).
        ///
        /// Off by default, matching how Unreal splits the two rigs: the body animation owns the
        /// body joints and the face rig owns the face. Turning it on lets the face rig contribute
        /// head and neck motion, at the cost of the solver overwriting the Animator on those
        /// joints every LateUpdate - which freezes the arms and head of any body animation.
        /// </summary>
        public bool faceRigDrivesBodyJoints = false;

        public bool createMaterials = true;

        /// <summary>Import textures found next to the DNA into the project if they are outside it.</summary>
        public bool copyTextures = true;

        public string SafeName => string.IsNullOrWhiteSpace(characterName) ? "MetaHuman" : characterName.Trim();

        public string CharacterFolder => $"{outputFolder.TrimEnd('/')}/{SafeName}";
    }
}
