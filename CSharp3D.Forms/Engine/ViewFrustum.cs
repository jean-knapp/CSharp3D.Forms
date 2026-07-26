using OpenTK;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// The six clipping planes of a view volume, extracted straight from a
    /// view-projection matrix (Gribb &amp; Hartmann). Works for perspective and
    /// orthographic projections alike, so the 2D panes cull with the same code as the
    /// 3D one.
    ///
    /// This is the role Hammer's <c>CRender3D::IsBoxVisible</c> plays over its cull tree
    /// (render3dms.cpp): the renderer draws nothing it cannot see. Without it, a big map
    /// costs a draw call per face per view every frame no matter where the camera looks,
    /// which is the difference between flying around a large map and not.
    /// </summary>
    public struct ViewFrustum
    {
        // Plane i: dot(normal, p) + d >= 0 means "inside".
        private Vector4 p0, p1, p2, p3, p4, p5;

        public static ViewFrustum FromViewProjection(Matrix4 viewProjection)
        {
            Matrix4 m = viewProjection;

            ViewFrustum f = new ViewFrustum();

            // Rows of the matrix (OpenTK is row-major with row-vector convention, so a
            // clip coordinate is p * M and the planes come from column combinations).
            f.p0 = Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)); // left
            f.p1 = Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)); // right
            f.p2 = Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)); // bottom
            f.p3 = Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)); // top
            f.p4 = Normalize(new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43)); // near
            f.p5 = Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)); // far

            return f;
        }

        private static Vector4 Normalize(Vector4 plane)
        {
            float length = new Vector3(plane.X, plane.Y, plane.Z).Length;
            return length > 1e-12f ? plane / length : plane;
        }

        /// <summary>
        /// Conservative AABB test: false only when the box is wholly outside one plane.
        /// Uses the "positive vertex" of the box per plane, so it is 6 dot products.
        /// </summary>
        public bool Intersects(Vector3 min, Vector3 max)
        {
            return !Outside(ref p0, ref min, ref max)
                && !Outside(ref p1, ref min, ref max)
                && !Outside(ref p2, ref min, ref max)
                && !Outside(ref p3, ref min, ref max)
                && !Outside(ref p4, ref min, ref max)
                && !Outside(ref p5, ref min, ref max);
        }

        private static bool Outside(ref Vector4 plane, ref Vector3 min, ref Vector3 max)
        {
            // The corner furthest along the plane normal: if even that is behind the
            // plane, every corner is.
            float x = plane.X >= 0 ? max.X : min.X;
            float y = plane.Y >= 0 ? max.Y : min.Y;
            float z = plane.Z >= 0 ? max.Z : min.Z;

            return plane.X * x + plane.Y * y + plane.Z * z + plane.W < 0;
        }
    }
}
