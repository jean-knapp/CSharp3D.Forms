using OpenTK;
using System;

namespace CSharp3D.Forms.Utils
{
    /// <summary>
    /// Projection / unprojection and ray-casting math for cameras, in GL space. Everything here is
    /// matrix-driven, so it works uniformly for both perspective and orthographic cameras — unlike
    /// a picking ray that assumes a single eye point (correct only for perspective).
    ///
    /// These are generic engine primitives (no dependency on a specific camera or control), so they
    /// live in the library for reuse. Inputs are the camera's view and projection matrices plus the
    /// viewport pixel size; screen coordinates use the WinForms convention (origin top-left).
    /// </summary>
    public static class CameraMath
    {
        /// <summary>
        /// Converts a World-space point/vector (X forward, Y left, Z up) to GL space, matching the
        /// engine's (-Y, Z, -X) transform (see IndexedMesh / VectorOrientation). Being public and
        /// raw-vector, it lets callers move rays/points between the space geometry is authored in
        /// and the space cameras work in.
        /// </summary>
        public static Vector3 WorldToGL(Vector3 world)
        {
            return new Vector3(-world.Y, world.Z, -world.X);
        }

        /// <summary> The inverse of <see cref="WorldToGL"/>: GL space back to World space. </summary>
        public static Vector3 GLToWorld(Vector3 gl)
        {
            return new Vector3(-gl.Z, -gl.X, gl.Y);
        }

        /// <summary>
        /// The camera's screen axes as GL-space directions: <paramref name="right"/> is the world
        /// direction that moves +X across the screen, <paramref name="up"/> the one that moves +Y
        /// up it. Extracted from the view matrix's rotation, so it is correct for any orientation —
        /// use it to pan/anchor along what the viewer actually sees rather than fixed world axes.
        ///
        /// Accepts a full view matrix or a rotation-only one: the translation does not affect the
        /// basis (a caller whose view matrix depends on its location can pass just the rotation).
        /// The engine uses row-vector convention (v_eye = v_world · View), so the eye axes in world
        /// space are the columns of the rotation part.
        /// </summary>
        public static void ViewBasis(Matrix4 view, out Vector3 right, out Vector3 up)
        {
            right = new Vector3(view.M11, view.M21, view.M31);
            up = new Vector3(view.M12, view.M22, view.M32);
        }

        /// <summary>
        /// Projects a GL-space world point to a screen pixel. <paramref name="ndcZ"/> receives the
        /// normalized depth (-1 near … +1 far); a point behind the camera has W ≤ 0.
        /// </summary>
        public static Vector2 ProjectToScreen(Matrix4 view, Matrix4 projection, Vector3 world,
            int width, int height, out float ndcZ)
        {
            Vector4 clip = Vector4.Transform(new Vector4(world, 1f), view * projection);

            if (Math.Abs(clip.W) > 1e-9f)
            {
                clip.X /= clip.W;
                clip.Y /= clip.W;
                clip.Z /= clip.W;
            }

            ndcZ = clip.Z;

            float sx = (clip.X * 0.5f + 0.5f) * width;
            float sy = (1f - (clip.Y * 0.5f + 0.5f)) * height; // flip Y: screen origin is top-left
            return new Vector2(sx, sy);
        }

        /// <summary>
        /// Unprojects a screen pixel + NDC depth (-1 near … +1 far) to a GL-space world point.
        /// </summary>
        public static Vector3 Unproject(Matrix4 view, Matrix4 projection,
            float screenX, float screenY, float ndcZ, int width, int height)
        {
            float ndcX = (2f * screenX) / width - 1f;
            float ndcY = 1f - (2f * screenY) / height; // flip Y

            Vector4 clip = new Vector4(ndcX, ndcY, ndcZ, 1f);
            Matrix4 inverse = Matrix4.Invert(view * projection);
            Vector4 world = Vector4.Transform(clip, inverse);

            if (Math.Abs(world.W) > 1e-9f)
            {
                world.X /= world.W;
                world.Y /= world.W;
                world.Z /= world.W;
            }

            return new Vector3(world.X, world.Y, world.Z);
        }

        /// <summary>
        /// The world-space picking ray through a screen pixel, correct for perspective AND
        /// orthographic. Built from two unprojected points (near and far planes): the origin is the
        /// near point and the direction is normalized near→far. For perspective the origin is the
        /// eye; for orthographic each pixel has its own parallel ray — this handles both.
        /// </summary>
        public static void ScreenRay(Matrix4 view, Matrix4 projection,
            float screenX, float screenY, int width, int height,
            out Vector3 origin, out Vector3 direction)
        {
            Vector3 near = Unproject(view, projection, screenX, screenY, -1f, width, height);
            Vector3 far = Unproject(view, projection, screenX, screenY, 1f, width, height);

            origin = near;

            Vector3 delta = far - near;
            float len = delta.Length;
            direction = len > 1e-9f ? delta / len : new Vector3(0f, 0f, -1f);
        }

        /// <summary>
        /// Möller–Trumbore ray/triangle intersection. Returns true and the ray parameter
        /// <paramref name="t"/> (distance along a unit-length <paramref name="direction"/>) if the
        /// ray hits the triangle. Culls nothing — hits front and back faces.
        /// </summary>
        public static bool RayTriangle(Vector3 origin, Vector3 direction,
            Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;

            const float epsilon = 1e-7f;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;

            Vector3 pvec = Vector3.Cross(direction, edge2);
            float det = Vector3.Dot(edge1, pvec);

            if (det > -epsilon && det < epsilon)
                return false; // ray parallel to triangle

            float invDet = 1f / det;

            Vector3 tvec = origin - v0;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(direction, qvec) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(edge2, qvec) * invDet;
            return t > epsilon;
        }
    }
}
