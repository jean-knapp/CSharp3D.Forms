using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace CSharp3D.Forms.Cameras
{
    /// <summary>
    /// A abstract camera class.
    /// </summary>
    [ToolboxItem(false)]
    public class Camera : Component
    {
        public enum Projections { Perspective, Ortographic }

        /// <summary>
        /// The projection type of the camera.
        /// </summary>
        internal Projections Projection { get; set; } = Projections.Perspective;

        /// <summary>
        /// The closest distance to the camera that can be rendered, in GL units.
        /// </summary>
        [Category("Appearance")]
        [Description("The closest distance to the camera that can be rendered, in GL units.")]
        public float NearPlane { get; set; } = 0.1f;

        /// <summary>
        /// The farthest distance from the camera that can be rendered, in GL units.
        /// </summary>
        [Category("Appearance")]
        [Description("The farthest distance from the camera that can be rendered, in GL units.")]
        public float FarPlane { get; set; } = 1000f;

        /// <summary>
        /// The field of view of the camera, in degrees.
        /// </summary>
        [Category("Appearance")]
        [Description("The field of view of the camera, in degrees.")]
        public float FOV { get; set; } = 90;

        internal bool IsMouseDown { get; set; } = false;

        internal bool IsKeyDown { get; set; } = false;

        protected Vector2 MouseInitialLocationReference = Vector2.Zero;

        internal bool IsLeftMouseButtonDown { get; set; } = false;

        internal bool IsMiddleMouseButtonDown { get; set; } = false;
        internal bool IsRightMouseButtonDown { get; set; } = false;

        public Camera()
        {

        }

        public virtual Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            return Matrix4.Identity;
        }

        public virtual Matrix4 GetProjectionMatrix(int controlWidth, int controlHeight)
        {
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(FOV * MathHelper.Pi / 180f, (float)controlWidth / controlHeight, NearPlane, FarPlane);
            return projection;
        }

        public virtual void MouseDown(RendererControl rendererControl, MouseButtons button)
        {
            MouseInitialLocationReference = new Vector2(Cursor.Position.X, Cursor.Position.Y);

            IsMouseDown = true;

            if (button == MouseButtons.Left)
            {
                IsLeftMouseButtonDown = true;
            }
            else if(button == MouseButtons.Middle)
            {
                IsMiddleMouseButtonDown = true;
            }
            else if (button == MouseButtons.Right)
            {
                IsRightMouseButtonDown = true;
            }
        }

        public virtual void MouseUp(RendererControl rendererControl, MouseButtons button)
        {
            if (button == MouseButtons.Left)
            {
                IsLeftMouseButtonDown = false;
            }
            else if (button == MouseButtons.Middle)
            {
                IsMiddleMouseButtonDown = false;
            }
            else if (button == MouseButtons.Right)
            {
                IsRightMouseButtonDown = false;
            }

            if (!IsLeftMouseButtonDown && !IsMiddleMouseButtonDown && !IsRightMouseButtonDown)
            {
                IsMouseDown = false;
            }
        }

        /// <summary>
        /// Get the mouse location relative to the initial reference
        /// </summary>
        /// <returns></returns>
        public Vector2 GetMouseDelta()
        {
            if (!IsMouseDown)
            {
                //return Vector2.Zero;
            }
            Vector2 mouseLocation = new Vector2(Cursor.Position.X, Cursor.Position.Y);
            Vector2 result = new Vector2(-(mouseLocation.X - MouseInitialLocationReference.X), mouseLocation.Y - MouseInitialLocationReference.Y);

            return result;
        }

        public virtual void MouseWheel(RendererControl rendererControl, MouseEventArgs e)
        {

        }

        /// <summary>
        /// Gets the rotation of the camera, in degrees (Roll, Pitch, Yaw).
        /// </summary>
        /// <param name="viewMatrix"> The view matrix of the camera. </param>
        /// <returns> The rotation of the camera, in degrees (Roll, Pitch, Yaw). </returns>
        public static Vector3 GetRotation(Matrix4 viewMatrix)
        {
            // Step 2: Extract the upper-left 3x3 matrix (rotation part)
            Matrix3 rotationMatrix = new Matrix3(
                new Vector3(viewMatrix.M11, viewMatrix.M12, viewMatrix.M13),
                new Vector3(viewMatrix.M21, viewMatrix.M22, viewMatrix.M23),
                new Vector3(viewMatrix.M31, viewMatrix.M32, viewMatrix.M33)
            );

            double theta1 = Math.Asin(viewMatrix.M23);
            double psi1 = 0;
            double phi1 = 0;

            if (Math.Abs(rotationMatrix.M23) == 1)
            {
                psi1 = Math.Atan2(viewMatrix.M31, -CopySign(1, rotationMatrix.M23) * viewMatrix.M32);
            }
            else
            {
                psi1 = -Math.Atan2(viewMatrix.M13 / Math.Cos(theta1), viewMatrix.M33 / Math.Cos(theta1));
                phi1 = -Math.Atan2(viewMatrix.M21 / Math.Cos(theta1), viewMatrix.M22 / Math.Cos(theta1));
            }

            var rotation = new Vector3((float)theta1, (float)psi1, (float)phi1) * new Matrix3(
                new Vector3(0, -1, 0),
                new Vector3(0, 0, 1),
                new Vector3(-1, 0, 0)
            ) * -1 * 180 / MathHelper.Pi;

            return rotation;
        }

        /// <summary>
        /// Gets the location of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="viewMatrix"> The view matrix of the camera. </param>
        /// <returns> The location of the camera, in World units (X, Y, Z). </returns>
        public static Vector3 GetLocation(Matrix4 viewMatrix)
        {
            Vector3 rotation = VectorOrientation.ToGL(GetRotation(viewMatrix)) * MathHelper.Pi / 180f;
            float distance = GetDistanceFromOrigin(viewMatrix);

            Matrix4 yawMatrix = Matrix4.CreateRotationZ(rotation.Z);
            Matrix4 pitchMatrix = Matrix4.CreateRotationX(rotation.X);
            Matrix4 rollMatrix = Matrix4.CreateRotationY(rotation.Y);

            Matrix4 rotationMatrix = pitchMatrix * yawMatrix * rollMatrix;

            Vector3 forward = new Vector3(0, 0, distance);
            Vector3 position = VectorOrientation.ToWorld(Vector3.Transform(forward, new Matrix3(rotationMatrix)));

            return position;
        }

        /// <summary>
        /// Gets the position of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The position of the camera, in World units (X, Y, Z). </returns>
        public virtual Vector3 GetLocation(RendererControl rendererControl)
        {
            return Vector3.Zero;
        }

        /// <summary>
        /// Gets the distance from the camera to the origin, in World units.
        /// </summary>
        /// <param name="viewMatrix"> The view matrix of the camera. </param>
        /// <returns> The distance from the camera to the origin, in World units. </returns>
        public static float GetDistanceFromOrigin(Matrix4 viewMatrix)
        {
            return -viewMatrix.M43;
        }

        private static float CopySign(float size, float sign)
        {
            return Math.Sign(sign) == -1 ? -Math.Abs(size) : Math.Abs(size);
        }

        public Ray GetPickingRay(RendererControl rendererControl, MouseEventArgs mouseEventArgs)
        {
            // 1. Convert 2D mouse coords to Normalized Device Coordinates (NDC)
            float mouseX = mouseEventArgs.X;
            float mouseY = rendererControl.Height - mouseEventArgs.Y; // Because top-left is (0,0) in WinForms
            float ndcX = (2.0f * mouseX) / rendererControl.Width - 1.0f;
            float ndcY = (2.0f * mouseY) / rendererControl.Height - 1.0f;

            // We'll pick from the "near plane" or "clip space" -1 in Z.
            // Or, if you prefer, use +1.0f if you'd like the far plane. 
            // It's a matter of style (some prefer near plane = -1.0f).
            float ndcZ = -1.0f;

            // 2. Get your matrices
            var projectionMatrix = GetProjectionMatrix(rendererControl.Width, rendererControl.Height);
            var viewMatrix = GetViewMatrix(rendererControl);

            // 3. Unproject from Clip Space -> Eye Space
            //    We'll set W=1 for now, then fix it in Eye Space.
            Vector4 rayClip = new Vector4(ndcX, ndcY, ndcZ, 1.0f);

            // Invert the Projection
            Matrix4 invProjection = Matrix4.Invert(projectionMatrix);

            // Transform from Clip Space to Eye Space
            Vector4 rayEye = Vector4.Transform(rayClip, invProjection);

            // In Eye Space, the ray direction has Z=-1, W=0
            // so we keep the X,Y from the transform, but force Z=-1, W=0
            rayEye.Z = -1.0f;
            rayEye.W = 0.0f;

            // 4. Convert from Eye Space -> World Space
            Matrix4 invView = Matrix4.Invert(viewMatrix);
            Vector4 rayWorld4 = Vector4.Transform(rayEye, invView);

            // Make a Vector3 for direction, and normalize
            Vector3 rayDirection = new Vector3(rayWorld4.X, rayWorld4.Y, rayWorld4.Z);
            rayDirection.Normalize();
            rayDirection = VectorOrientation.ToWorld(rayDirection);

            // 5. Get the camera position as the ray origin
            //    (assuming an OrbitalCamera or similar)
            Vector3 cameraPosition = GetLocation(rendererControl);

            // Now we have a "picking ray" in world space:
            Ray pickingRay = new Ray(cameraPosition, rayDirection);

            return pickingRay;
        }

        protected void RecenterMouse(RendererControl rendererControl)
        {
            // Recenter mouse
            var center = rendererControl.GetCenterPointInScreen();
            var diffX = Cursor.Position.X - center.X;
            var diffY = Cursor.Position.Y - center.Y;
            MouseInitialLocationReference = new Vector2(MouseInitialLocationReference.X - diffX, MouseInitialLocationReference.Y - diffY);
            Cursor.Position = center;
        }

        public virtual void KeyDown(RendererControl rendererControl, Keys keyCode)
        {

        }
    }
}
