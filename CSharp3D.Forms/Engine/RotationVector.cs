using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharp3D.Forms.Engine
{
    [Serializable]
    public class RotationVector
    {
        public float Roll; // X
        public float Pitch; // Y
        public float Yaw; // Z
        

        public RotationVector(float roll, float pitch, float yaw)
        {
            Pitch = pitch;
            Yaw = yaw;
            Roll = roll;
        }

        internal Vector3 ToVector3()
        {
            return new Vector3(Roll, Pitch, Yaw);
        }

        internal static RotationVector FromVector3(Vector3 rotation)
        {
            return new RotationVector(rotation.X, rotation.Y, rotation.Z);
        }

        public static RotationVector operator +(RotationVector a, RotationVector b)
        {
            return new RotationVector(a.Roll + b.Roll, a.Pitch + b.Pitch, a.Yaw + b.Yaw);
        }

        public static RotationVector operator -(RotationVector a, RotationVector b)
        {
            return new RotationVector(a.Roll - b.Roll, a.Pitch - b.Pitch, a.Yaw - b.Yaw);
        }

        public static RotationVector operator *(RotationVector vec, Matrix3 mat)
        {
            TransformRow(ref vec, ref mat, out var result);
            return result;
        }

        private static void TransformRow(ref RotationVector vec, ref Matrix3 mat, out RotationVector result)
        {
            result = new RotationVector(
                roll: vec.Roll * mat.Row0.X + vec.Pitch * mat.Row1.X + vec.Yaw * mat.Row2.X,
                pitch: vec.Roll * mat.Row0.Y + vec.Pitch * mat.Row1.Y + vec.Yaw * mat.Row2.Y,
                yaw: vec.Roll * mat.Row0.Z + vec.Pitch * mat.Row1.Z + vec.Yaw * mat.Row2.Z);
        }

        public static RotationVector Multiply(RotationVector vec, Vector3 scale)
        {
            return new RotationVector(vec.Roll * scale.X, vec.Pitch * scale.Y, vec.Yaw * scale.Z);
        }
    }
}
