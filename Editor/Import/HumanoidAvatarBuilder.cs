// MetaHuman Bridge - Humanoid avatar construction.
//
// MetaHuman body skeletons use the Unreal mannequin naming scheme, so the mapping onto
// Unity's Humanoid rig is a fixed table. Optional bones are skipped when absent, which keeps
// the mapping valid across the spine_04/spine_05 and metacarpal variations Epic has shipped.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge.Editor
{
    public static class HumanoidAvatarBuilder
    {
        /// <summary>Candidate joint names per human bone, most preferred first.</summary>
        static readonly Dictionary<HumanBodyBones, string[]> Mapping = new Dictionary<HumanBodyBones, string[]>
        {
            { HumanBodyBones.Hips, new[] { "pelvis" } },
            { HumanBodyBones.Spine, new[] { "spine_01" } },
            { HumanBodyBones.Chest, new[] { "spine_03", "spine_02" } },
            { HumanBodyBones.UpperChest, new[] { "spine_05", "spine_04" } },
            { HumanBodyBones.Neck, new[] { "neck_01" } },
            { HumanBodyBones.Head, new[] { "head" } },

            { HumanBodyBones.LeftShoulder, new[] { "clavicle_l" } },
            { HumanBodyBones.LeftUpperArm, new[] { "upperarm_l" } },
            { HumanBodyBones.LeftLowerArm, new[] { "lowerarm_l" } },
            { HumanBodyBones.LeftHand, new[] { "hand_l" } },
            { HumanBodyBones.RightShoulder, new[] { "clavicle_r" } },
            { HumanBodyBones.RightUpperArm, new[] { "upperarm_r" } },
            { HumanBodyBones.RightLowerArm, new[] { "lowerarm_r" } },
            { HumanBodyBones.RightHand, new[] { "hand_r" } },

            { HumanBodyBones.LeftUpperLeg, new[] { "thigh_l" } },
            { HumanBodyBones.LeftLowerLeg, new[] { "calf_l" } },
            { HumanBodyBones.LeftFoot, new[] { "foot_l" } },
            { HumanBodyBones.LeftToes, new[] { "ball_l" } },
            { HumanBodyBones.RightUpperLeg, new[] { "thigh_r" } },
            { HumanBodyBones.RightLowerLeg, new[] { "calf_r" } },
            { HumanBodyBones.RightFoot, new[] { "foot_r" } },
            { HumanBodyBones.RightToes, new[] { "ball_r" } },

            { HumanBodyBones.LeftThumbProximal, new[] { "thumb_01_l" } },
            { HumanBodyBones.LeftThumbIntermediate, new[] { "thumb_02_l" } },
            { HumanBodyBones.LeftThumbDistal, new[] { "thumb_03_l" } },
            { HumanBodyBones.LeftIndexProximal, new[] { "index_01_l" } },
            { HumanBodyBones.LeftIndexIntermediate, new[] { "index_02_l" } },
            { HumanBodyBones.LeftIndexDistal, new[] { "index_03_l" } },
            { HumanBodyBones.LeftMiddleProximal, new[] { "middle_01_l" } },
            { HumanBodyBones.LeftMiddleIntermediate, new[] { "middle_02_l" } },
            { HumanBodyBones.LeftMiddleDistal, new[] { "middle_03_l" } },
            { HumanBodyBones.LeftRingProximal, new[] { "ring_01_l" } },
            { HumanBodyBones.LeftRingIntermediate, new[] { "ring_02_l" } },
            { HumanBodyBones.LeftRingDistal, new[] { "ring_03_l" } },
            { HumanBodyBones.LeftLittleProximal, new[] { "pinky_01_l" } },
            { HumanBodyBones.LeftLittleIntermediate, new[] { "pinky_02_l" } },
            { HumanBodyBones.LeftLittleDistal, new[] { "pinky_03_l" } },

            { HumanBodyBones.RightThumbProximal, new[] { "thumb_01_r" } },
            { HumanBodyBones.RightThumbIntermediate, new[] { "thumb_02_r" } },
            { HumanBodyBones.RightThumbDistal, new[] { "thumb_03_r" } },
            { HumanBodyBones.RightIndexProximal, new[] { "index_01_r" } },
            { HumanBodyBones.RightIndexIntermediate, new[] { "index_02_r" } },
            { HumanBodyBones.RightIndexDistal, new[] { "index_03_r" } },
            { HumanBodyBones.RightMiddleProximal, new[] { "middle_01_r" } },
            { HumanBodyBones.RightMiddleIntermediate, new[] { "middle_02_r" } },
            { HumanBodyBones.RightMiddleDistal, new[] { "middle_03_r" } },
            { HumanBodyBones.RightRingProximal, new[] { "ring_01_r" } },
            { HumanBodyBones.RightRingIntermediate, new[] { "ring_02_r" } },
            { HumanBodyBones.RightRingDistal, new[] { "ring_03_r" } },
            { HumanBodyBones.RightLittleProximal, new[] { "pinky_01_r" } },
            { HumanBodyBones.RightLittleIntermediate, new[] { "pinky_02_r" } },
            { HumanBodyBones.RightLittleDistal, new[] { "pinky_03_r" } },
        };

        static readonly HumanBodyBones[] Required =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        /// <summary>
        /// Builds a Humanoid avatar for the imported hierarchy, or returns null with a reason
        /// when the skeleton lacks the bones Unity requires.
        /// </summary>
        public static Avatar Build(GameObject root, Dictionary<string, Transform> joints, out string failureReason)
        {
            failureReason = null;

            var missing = Required.Where(bone => Resolve(joints, bone) == null).ToList();
            if (missing.Count > 0)
            {
                failureReason =
                    "Skeleton is missing bones Unity requires for a Humanoid rig: " +
                    string.Join(", ", missing) +
                    ". Import the body DNA as well as the head, or use a Generic avatar.";
                return null;
            }

            var humanBones = new List<HumanBone>();
            foreach (var pair in Mapping)
            {
                Transform t = Resolve(joints, pair.Key);
                if (t == null) continue;

                humanBones.Add(new HumanBone
                {
                    humanName = HumanTrait.BoneName[(int)pair.Key],
                    boneName = t.name,
                    limit = new HumanLimit { useDefaultValues = true }
                });
            }

            var skeleton = root.GetComponentsInChildren<Transform>(true)
                .Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                })
                .ToArray();

            var description = new HumanDescription
            {
                human = humanBones.ToArray(),
                skeleton = skeleton,
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            if (avatar == null || !avatar.isValid)
            {
                failureReason = "Unity rejected the Humanoid mapping. The character will still work as a Generic rig.";
                return null;
            }

            avatar.name = root.name + " Avatar";
            return avatar;
        }

        static Transform Resolve(Dictionary<string, Transform> joints, HumanBodyBones bone)
        {
            if (!Mapping.TryGetValue(bone, out string[] candidates)) return null;
            foreach (string name in candidates)
                if (joints.TryGetValue(name, out Transform t) && t != null) return t;
            return null;
        }
    }
}
