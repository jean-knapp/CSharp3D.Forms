using CSharp3D.Forms.Engine;
using OpenTK;
using System;

namespace CSharp3D.Forms.Utils
{
    public class Ray
    {
        public LocationVector Origin { get; }
        public RotationVector Direction { get; } // Must be normalized

        public Ray(LocationVector origin, RotationVector direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public bool RayIntersectsAABB(
            LocationVector rayOrigin,
            RotationVector rayDirection,
            LocationVector boxMin,
            LocationVector boxMax,
            out float tNear)
        {
            // Initialize tNear/tFar for the intersection intervals
            float tmin = float.MinValue;
            float tmax = float.MaxValue;

            // For each axis, find intersection with the slabs [boxMin[i], boxMax[i]]
            // We do this for X, Y, Z. 
            // If the ray is parallel to an axis, we must check that the origin is within the slab.
            // If not, there's no intersection.

            // X-axis
            if (Math.Abs(rayDirection.Roll) < 1e-8)
            {
                // The ray is parallel to X
                if (rayOrigin.X < boxMin.X || rayOrigin.X > boxMax.X)
                {
                    tNear = 0;
                    return false;
                }
            }
            else
            {
                float ood = 1.0f / rayDirection.Roll; // inverse of direction
                float t1 = (boxMin.X - rayOrigin.X) * ood;
                float t2 = (boxMax.X - rayOrigin.X) * ood;
                if (t1 > t2) (t1, t2) = (t2, t1); // swap
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { tNear = 0; return false; }
            }

            // Y-axis
            if (Math.Abs(rayDirection.Pitch) < 1e-8)
            {
                // The ray is parallel to Y
                if (rayOrigin.Y < boxMin.Y || rayOrigin.Y > boxMax.Y)
                {
                    tNear = 0;
                    return false;
                }
            }
            else
            {
                float ood = 1.0f / rayDirection.Pitch;
                float t1 = (boxMin.Y - rayOrigin.Y) * ood;
                float t2 = (boxMax.Y - rayOrigin.Y) * ood;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { tNear = 0; return false; }
            }

            // Z-axis
            if (Math.Abs(rayDirection.Yaw) < 1e-8)
            {
                // The ray is parallel to Z
                if (rayOrigin.Z < boxMin.Z || rayOrigin.Z > boxMax.Z)
                {
                    tNear = 0;
                    return false;
                }
            }
            else
            {
                float ood = 1.0f / rayDirection.Yaw;
                float t1 = (boxMin.Z - rayOrigin.Z) * ood;
                float t2 = (boxMax.Z - rayOrigin.Z) * ood;
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) { tNear = 0; return false; }
            }

            // If we get here, we have an intersection:
            // tmin is near intersection, tmax is far intersection
            // You can return tmin if you want the near intersection along the ray
            tNear = tmin;
            return true;
        }
    }
}
