// MetaHuman Bridge - drives an imported MetaHuman from RigLogic control values.
//
// Attach to the root of an imported character. The importer fills in the joint and blend
// shape bindings; at runtime you set control values and the component solves and applies
// them, exactly as the Unreal RigLogic anim node would.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge
{
    /// <summary>Maps rig blend shape channels onto one renderer's Unity blend shape indices.</summary>
    [Serializable]
    public sealed class MetaHumanBlendShapeBinding
    {
        public SkinnedMeshRenderer renderer;
        /// <summary>One entry per rig channel; -1 when this renderer has no matching shape.</summary>
        public int[] channelToShapeIndex = Array.Empty<int>();
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("MetaHuman Bridge/MetaHuman Rig")]
    public sealed class MetaHumanRig : MonoBehaviour
    {
        [SerializeField] RigLogicAsset _asset;
        [SerializeField] Transform[] _joints = Array.Empty<Transform>();
        [SerializeField] List<MetaHumanBlendShapeBinding> _blendShapeBindings = new List<MetaHumanBlendShapeBinding>();

        [Header("Evaluation")]
        [Tooltip("Rig LOD used for the solve. Higher values evaluate fewer rows and cost less.")]
        [SerializeField] int _lod;
        [Tooltip("Solve every LateUpdate. Turn off to drive the rig manually via Solve().")]
        [SerializeField] bool _solveEveryFrame = true;
        [Tooltip("Write solved joint deltas onto the skeleton. Turn off for a blend-shape-only face.")]
        [SerializeField] bool _applyJoints = true;
        [SerializeField] bool _applyBlendShapes = true;

        RigLogicSolver _solver;
        DnaSpace _space;
        bool _spaceReady;

        public RigLogicAsset Asset => _asset;

        /// <summary>Lazily created solver. Read <see cref="RigLogicSolver.GuiControls"/> to drive the rig.</summary>
        public RigLogicSolver Solver
        {
            get
            {
                EnsureSolver();
                return _solver;
            }
        }

        public int Lod
        {
            get => _lod;
            set
            {
                _lod = value;
                if (_solver != null) _solver.Lod = value;
            }
        }

        void OnEnable()
        {
            EnsureSolver();
        }

        void LateUpdate()
        {
            if (_solveEveryFrame) Solve();
        }

        void EnsureSolver()
        {
            if (_asset == null || _solver != null) return;
            _solver = new RigLogicSolver(_asset) { Lod = _lod };
            _space = _asset.CreateSpace();
            _spaceReady = true;
        }

        /// <summary>Solves the rig and pushes the result onto joints and blend shapes.</summary>
        public void Solve()
        {
            EnsureSolver();
            if (_solver == null) return;

            _solver.Lod = _lod;
            _solver.Evaluate();
            Apply();
        }

        /// <summary>Applies the most recent solve without re-running it.</summary>
        public void Apply()
        {
            if (_solver == null || !_spaceReady) return;
            if (_applyJoints) ApplyJoints();
            if (_applyBlendShapes) ApplyBlendShapes();
        }

        public bool SetControl(string guiControlName, float value)
        {
            EnsureSolver();
            return _solver != null && _solver.SetGuiControl(guiControlName, value);
        }

        void ApplyJoints()
        {
            float[] deltas = _solver.JointDeltas;
            float[] neutralT = _asset.neutralTranslations;
            float[] neutralR = _asset.neutralRotations;
            int[] driven = _asset.drivenJointIndices;

            int count = driven != null && driven.Length > 0 ? driven.Length : _joints.Length;

            for (int i = 0; i < count; i++)
            {
                int j = driven != null && driven.Length > 0 ? driven[i] : i;
                if (j < 0 || j >= _joints.Length) continue;

                Transform joint = _joints[j];
                if (joint == null) continue;

                int a = j * 9;
                if (a + 8 >= deltas.Length) continue;

                int t3 = j * 3;
                joint.localPosition = _space.ConvertPoint(
                    neutralT[t3] + deltas[a],
                    neutralT[t3 + 1] + deltas[a + 1],
                    neutralT[t3 + 2] + deltas[a + 2]);

                joint.localRotation = _space.ConvertEuler(
                    neutralR[t3] + deltas[a + 3],
                    neutralR[t3 + 1] + deltas[a + 4],
                    neutralR[t3 + 2] + deltas[a + 5]);

                // Scale outputs are deltas around the bind scale of 1.
                joint.localScale = _space.ConvertScale(
                    1f + deltas[a + 6],
                    1f + deltas[a + 7],
                    1f + deltas[a + 8]);
            }
        }

        void ApplyBlendShapes()
        {
            float[] weights = _solver.BlendShapeWeights;

            for (int b = 0; b < _blendShapeBindings.Count; b++)
            {
                var binding = _blendShapeBindings[b];
                if (binding == null || binding.renderer == null) continue;

                int[] map = binding.channelToShapeIndex;
                int channelCount = Mathf.Min(map.Length, weights.Length);
                for (int channel = 0; channel < channelCount; channel++)
                {
                    int shapeIndex = map[channel];
                    if (shapeIndex < 0) continue;
                    binding.renderer.SetBlendShapeWeight(shapeIndex, weights[channel] * 100f);
                }
            }
        }

        /// <summary>Called by the importer to wire the component up.</summary>
        public void Configure(RigLogicAsset asset, Transform[] joints, List<MetaHumanBlendShapeBinding> bindings)
        {
            _asset = asset;
            _joints = joints ?? Array.Empty<Transform>();
            _blendShapeBindings = bindings ?? new List<MetaHumanBlendShapeBinding>();
            _solver = null;
            _spaceReady = false;
        }
    }
}
