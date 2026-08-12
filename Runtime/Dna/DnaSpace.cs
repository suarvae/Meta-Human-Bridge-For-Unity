// MetaHuman Bridge - coordinate-space conversion between a DNA's declared basis and Unity's.
//
// A DNA descriptor names the direction each of its axes points (tdm::axis_dir), which always
// yields a signed permutation basis. Converting a point is therefore a cheap axis remap, and
// converting a rotation is the similarity transform C * R * C^T, which for quaternions reduces
// to permuting the vector part and multiplying it by det(C).

using UnityEngine;

namespace RaveHouse.MetaHumanBridge
{
    /// <summary>
    /// Immutable conversion from one DNA's coordinate space and units into Unity's
    /// (X right, Y up, Z forward, metres).
    /// </summary>
    public struct DnaSpace
    {
        Vector3 _xAxis;
        Vector3 _yAxis;
        Vector3 _zAxis;
        float _determinant;
        float _unitScale;
        float _angleScale;
        DnaRotationSequence _sequence;
        float _signX, _signY, _signZ;
        bool _reverseWinding;

        /// <summary>Metres per DNA translation unit. Applied to positions and translation deltas.</summary>
        public float UnitScale => _unitScale;

        /// <summary>True when triangle corner order must be reversed for Unity to cull correctly.</summary>
        public bool ReverseWinding => _reverseWinding;

        /// <summary>True when the DNA basis already matches Unity's, so conversion is a no-op.</summary>
        public bool IsIdentity =>
            _determinant > 0f &&
            _xAxis == Vector3.right && _yAxis == Vector3.up && _zAxis == Vector3.forward;

        public static DnaSpace FromDescriptor(DnaDescriptor descriptor, float additionalScale = 1f)
        {
            var space = new DnaSpace
            {
                _xAxis = AxisVector(descriptor.CoordinateSystem.X),
                _yAxis = AxisVector(descriptor.CoordinateSystem.Y),
                _zAxis = AxisVector(descriptor.CoordinateSystem.Z),
                _sequence = descriptor.RotationSequence,
                _signX = descriptor.RotationSignX,
                _signY = descriptor.RotationSignY,
                _signZ = descriptor.RotationSignZ,
                _unitScale = (descriptor.TranslationUnit == DnaTranslationUnit.Metre ? 1f : 0.01f) * additionalScale,
                _angleScale = descriptor.RotationUnit == DnaRotationUnit.Radians ? 1f : Mathf.Deg2Rad
            };

            // Rows of the basis are the DNA axes expressed in Unity's canonical frame.
            space._determinant = Vector3.Dot(space._xAxis, Vector3.Cross(space._yAxis, space._zAxis));

            // A mirrored basis flips the sense of the face normal derived from corner order, and
            // so does a DNA that declares clockwise winding.
            bool mirrored = space._determinant < 0f;
            bool clockwise = descriptor.FaceWindingOrder == DnaFaceWindingOrder.Clockwise;
            space._reverseWinding = mirrored ^ clockwise;

            return space;
        }

        static Vector3 AxisVector(DnaAxisDirection direction)
        {
            switch (direction)
            {
                case DnaAxisDirection.Right: return new Vector3(1f, 0f, 0f);
                case DnaAxisDirection.Left: return new Vector3(-1f, 0f, 0f);
                case DnaAxisDirection.Up: return new Vector3(0f, 1f, 0f);
                case DnaAxisDirection.Down: return new Vector3(0f, -1f, 0f);
                case DnaAxisDirection.Front: return new Vector3(0f, 0f, 1f);
                case DnaAxisDirection.Back: return new Vector3(0f, 0f, -1f);
                default: return Vector3.zero;
            }
        }

        /// <summary>Rotates and mirrors a direction into Unity space without applying unit scale.</summary>
        public Vector3 ConvertDirection(float x, float y, float z)
        {
            return new Vector3(
                x * _xAxis.x + y * _yAxis.x + z * _zAxis.x,
                x * _xAxis.y + y * _yAxis.y + z * _zAxis.y,
                x * _xAxis.z + y * _yAxis.z + z * _zAxis.z);
        }

        /// <summary>Converts a position, including the DNA-unit to metre scale.</summary>
        public Vector3 ConvertPoint(float x, float y, float z)
        {
            return ConvertDirection(x, y, z) * _unitScale;
        }

        /// <summary>
        /// Permutes a scale triple into Unity's axis order. Signs are dropped because a signed
        /// permutation only ever reorders scale magnitudes.
        /// </summary>
        public Vector3 ConvertScale(float x, float y, float z)
        {
            Vector3 v = ConvertDirection(x, y, z);
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        /// <summary>
        /// Builds a quaternion from DNA Euler angles (in the DNA's rotation unit, sequence and
        /// per-axis signs) and expresses it in Unity space.
        /// </summary>
        public Quaternion ConvertEuler(float x, float y, float z)
        {
            return ConvertRotation(EulerToQuaternion(x, y, z));
        }

        /// <summary>Expresses a quaternion authored in DNA space in Unity space.</summary>
        public Quaternion ConvertRotation(Quaternion dnaSpace)
        {
            Vector3 v = ConvertDirection(dnaSpace.x, dnaSpace.y, dnaSpace.z);
            // Conjugating by a mirror maps a rotation about axis a to one about -a by the same
            // angle, so the vector part picks up det(C).
            if (_determinant < 0f) v = -v;
            return new Quaternion(v.x, v.y, v.z, dnaSpace.w);
        }

        /// <summary>Euler angles to a quaternion, still in DNA space.</summary>
        public Quaternion EulerToQuaternion(float x, float y, float z)
        {
            float ax = x * _angleScale * _signX * 0.5f;
            float ay = y * _angleScale * _signY * 0.5f;
            float az = z * _angleScale * _signZ * 0.5f;

            var qx = new Quaternion(Mathf.Sin(ax), 0f, 0f, Mathf.Cos(ax));
            var qy = new Quaternion(0f, Mathf.Sin(ay), 0f, Mathf.Cos(ay));
            var qz = new Quaternion(0f, 0f, Mathf.Sin(az), Mathf.Cos(az));

            // tdm composes an intrinsic sequence as the reversed product of its axis rotations.
            switch (_sequence)
            {
                case DnaRotationSequence.XYZ: return qz * qy * qx;
                case DnaRotationSequence.XZY: return qy * qz * qx;
                case DnaRotationSequence.YXZ: return qz * qx * qy;
                case DnaRotationSequence.YZX: return qx * qz * qy;
                case DnaRotationSequence.ZXY: return qy * qx * qz;
                case DnaRotationSequence.ZYX: return qx * qy * qz;
                default: return qz * qy * qx;
            }
        }
    }
}
