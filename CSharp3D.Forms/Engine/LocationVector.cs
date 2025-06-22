using OpenTK;
using System;

namespace CSharp3D.Forms.Engine
{
    [Serializable]
    public class LocationVector
    {
        public float X;
        public float Y;
        public float Z;
        public static LocationVector Origin => new LocationVector(0, 0, 0);

        public LocationVector(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal Vector3 ToVector3()
        {
            return new Vector3(X, Y, Z);
        }

        internal static LocationVector FromVector3(Vector3 location)
        {
            return new LocationVector(location.X, location.Y, location.Z);
        }

        public static LocationVector operator +(LocationVector a, LocationVector b)
        {
            return new LocationVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        public static LocationVector operator -(LocationVector a, LocationVector b)
        {
            return new LocationVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        public static LocationVector operator *(LocationVector vec, Matrix3 mat)
        {
            TransformRow(ref vec, ref mat, out var result);
            return result;
        }

        private static void TransformRow(ref LocationVector vec, ref Matrix3 mat, out LocationVector result)
        {
            result = new LocationVector(
                x: vec.X * mat.Row0.X + vec.Y * mat.Row1.X + vec.Z * mat.Row2.X,
                y: vec.X * mat.Row0.Y + vec.Y * mat.Row1.Y + vec.Z * mat.Row2.Y,
                z: vec.X * mat.Row0.Z + vec.Y * mat.Row1.Z + vec.Z * mat.Row2.Z);
        }

        public static LocationVector Multiply(LocationVector vec, Vector3 scale)
        {
            return new LocationVector(vec.X * scale.X, vec.Y * scale.Y, vec.Z * scale.Z);
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
    }
}
