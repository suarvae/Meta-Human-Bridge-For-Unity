// MetaHuman Bridge - managed port of the RigLogic evaluation pipeline.
//
//   GUI controls -> conditional table -> raw controls
//                -> PSD network       -> corrective controls
//                -> blend shape weights, joint deltas, animated map values
//
// Semantics follow EpicGames/OpenRigLogic (ConditionalTable::calculateForward,
// PSDNetImpl::calculate, BlendShapesImpl::calculate, the CPU joint evaluator).

using System;
using UnityEngine;

namespace RaveHouse.MetaHumanBridge
{
    public sealed class RigLogicSolver
    {
        readonly RigLogicAsset _asset;
        readonly float[] _psdClamp;

        /// <summary>Input: one value per GUI control, normally 0..1 (some are -1..1).</summary>
        public readonly float[] GuiControls;

        /// <summary>Raw controls followed by corrective (PSD) controls.</summary>
        public readonly float[] Controls;

        /// <summary>Output: one weight per blend shape channel, 0..1.</summary>
        public readonly float[] BlendShapeWeights;

        /// <summary>Output: 9 deltas per joint - tx ty tz rx ry rz sx sy sz, in DNA space and units.</summary>
        public readonly float[] JointDeltas;

        /// <summary>Output: one value per animated map (used for wrinkle/mask driving).</summary>
        public readonly float[] AnimatedMapValues;

        int _lod;

        public RigLogicAsset Asset => _asset;

        public int Lod
        {
            get => _lod;
            set => _lod = Mathf.Clamp(value, 0, Mathf.Max(0, _asset.lodCount - 1));
        }

        public RigLogicSolver(RigLogicAsset asset)
        {
            _asset = asset != null ? asset : throw new ArgumentNullException(nameof(asset));

            GuiControls = new float[asset.guiControlNames.Length];
            Controls = new float[asset.ControlCount];
            _psdClamp = new float[asset.ControlCount];
            BlendShapeWeights = new float[asset.blendShapeChannelNames.Length];
            JointDeltas = new float[asset.jointAttributeCount];
            AnimatedMapValues = new float[asset.animatedMapNames.Length];
        }

        public void SetGuiControl(int index, float value)
        {
            if (index >= 0 && index < GuiControls.Length) GuiControls[index] = value;
        }

        public bool SetGuiControl(string controlName, float value)
        {
            int index = _asset.IndexOfGuiControl(controlName);
            if (index < 0) return false;
            GuiControls[index] = value;
            return true;
        }

        public void ResetControls()
        {
            Array.Clear(GuiControls, 0, GuiControls.Length);
        }

        public void Evaluate()
        {
            EvaluateControls();
            EvaluateBlendShapes();
            EvaluateJoints();
            EvaluateAnimatedMaps();
        }

        // -------------------------------------------------------------- controls

        void EvaluateControls()
        {
            Array.Clear(Controls, 0, Controls.Length);

            var table = _asset.guiToRaw;
            ApplyConditionalTable(table, GuiControls, Controls, table.RowCount);

            int rawCount = _asset.rawControlNames.Length;
            var psds = _asset.psds;
            if (psds.offsets.Length == 0) return;

            for (int i = 0; i < Controls.Length; i++)
                _psdClamp[i] = Mathf.Clamp01(Controls[i]);

            for (int p = 0; p < psds.offsets.Length; p++)
            {
                int outIndex = rawCount + p;
                if (outIndex >= Controls.Length) break;

                float value = psds.weights[p];
                int end = psds.offsets[p] + psds.sizes[p];
                for (int i = psds.offsets[p]; i < end; i++)
                {
                    ushort input = psds.inputIndices[i];
                    value *= input < _psdClamp.Length ? _psdClamp[input] : 0f;
                }

                Controls[outIndex] = Mathf.Min(1f, value);
            }
        }

        static void ApplyConditionalTable(RigLogicConditionalTable table, float[] inputs, float[] outputs, int rowCount)
        {
            if (table == null || table.RowCount == 0) return;

            rowCount = Mathf.Min(rowCount, table.RowCount);

            for (int row = 0; row < rowCount; row++)
            {
                ushort inputIndex = table.inputIndices[row];
                if (inputIndex >= inputs.Length) continue;

                float value = inputs[inputIndex];
                if (table.fromValues[row] > value || value > table.toValues[row]) continue;

                ushort outIndex = table.outputIndices[row];
                if (outIndex < outputs.Length)
                    outputs[outIndex] += table.slopeValues[row] * value + table.cutValues[row];

                // Only the first matching interval of a run contributes.
                row += table.intervalSkip[row];
            }

            int clampCount = Mathf.Min(table.outputCount, outputs.Length);
            for (int i = 0; i < clampCount; i++)
                outputs[i] = Mathf.Clamp01(outputs[i]);
        }

        // -------------------------------------------------------------- outputs

        void EvaluateBlendShapes()
        {
            Array.Clear(BlendShapeWeights, 0, BlendShapeWeights.Length);

            var lods = _asset.blendShapeLodRowCounts;
            if (lods.Length == 0) return;

            int rows = lods[Mathf.Clamp(_lod, 0, lods.Length - 1)];
            rows = Mathf.Min(rows, _asset.blendShapeOutputIndices.Length);

            for (int i = 0; i < rows; i++)
            {
                ushort output = _asset.blendShapeOutputIndices[i];
                ushort input = _asset.blendShapeInputIndices[i];
                if (output < BlendShapeWeights.Length && input < Controls.Length)
                    BlendShapeWeights[output] = Controls[input];
            }
        }

        void EvaluateJoints()
        {
            Array.Clear(JointDeltas, 0, JointDeltas.Length);

            var groups = _asset.jointGroups;
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                int cols = group.inputIndices.Length;
                if (cols == 0 || group.lodRowCounts.Length == 0) continue;

                int rows = group.lodRowCounts[Mathf.Clamp(_lod, 0, group.lodRowCounts.Length - 1)];
                rows = Mathf.Min(rows, group.outputIndices.Length);

                for (int r = 0; r < rows; r++)
                {
                    int rowStart = r * cols;
                    float sum = 0f;
                    for (int c = 0; c < cols; c++)
                    {
                        ushort input = group.inputIndices[c];
                        if (input < Controls.Length)
                            sum += group.values[rowStart + c] * Controls[input];
                    }

                    ushort output = group.outputIndices[r];
                    if (output < JointDeltas.Length)
                        JointDeltas[output] += sum;
                }
            }
        }

        void EvaluateAnimatedMaps()
        {
            if (AnimatedMapValues.Length == 0) return;
            Array.Clear(AnimatedMapValues, 0, AnimatedMapValues.Length);

            var lods = _asset.animatedMapLodRowCounts;
            int rows = lods.Length > 0
                ? lods[Mathf.Clamp(_lod, 0, lods.Length - 1)]
                : _asset.animatedMaps.RowCount;

            ApplyConditionalTable(_asset.animatedMaps, Controls, AnimatedMapValues, rows);
        }
    }
}
