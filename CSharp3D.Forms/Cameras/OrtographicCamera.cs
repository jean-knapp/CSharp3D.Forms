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
    /// A free-look camera that can move in 3D space with WASD and mouse.
    /// </summary>
    [ToolboxItem(true)]
    [Description("A free-look camera controlled by WASD and mouse movement.")]
    public class OrtographicCamera : Camera
    {
        /// <summary>
        /// The rotation of the camera, in degrees (Roll, Pitch, Yaw).
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The rotation of the camera, in degrees (Roll, Pitch, Yaw).")]
        public Vector3 Rotation { get; set; } = new Vector3(0, 0, 0);

        /// <summary>
        /// The distance from the camera to the origin, in World units.
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The location of the camera relative to the origin, in World units.")]
        public Vector3 Location { get; set; } = new Vector3(0, 0, 0);

        /// <summary>
        /// Whether to clamp the vertical rotation of the camera to 90 degrees.
        /// </summary>
        [Category("Behavior")]
        [Description("Whether to clamp the vertical rotation of the camera to 90 degrees.")]
        public bool ClampVertically { get; set; } = false;

        [Category("Behavior")]
        [Description("The default number of units per second to move the camera on input.")]
        public int MoveSpeed { get; set; } = 5;

        [Category("Position")]
        [Description("The camera zoom.")]
        public float OrthoScale { get; set; } = 64;

        public OrtographicCamera()
        {
            Projection = Projections.Ortographic;
        }

        public OrtographicCamera(Vector3 direction, Vector3 location)
        {
            Rotation = direction;
            Location = location;

            Projection = Projections.Ortographic;
        }

        public override Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            // 1) Convert Rotation (degrees) and Location from World to GL space.
            //    Rotation is (Roll, Pitch, Yaw) in degrees.
            //    Location is (X, Y, Z) in world units.

            Vector3 glRotation = VectorOrientation.ToGL(Rotation);
            Vector3 glLocation = VectorOrientation.ToGL(GetLocation(rendererControl));

            // 2) Convert rotation from degrees to radians
            glRotation *= MathHelper.Pi / 180f;

            Quaternion qPitch = Quaternion.FromAxisAngle(Vector3.UnitX, glRotation.X);
            Quaternion qYaw = Quaternion.FromAxisAngle(Vector3.UnitY, glRotation.Y);
            Quaternion qRoll = Quaternion.FromAxisAngle(Vector3.UnitZ, glRotation.Z);

            Matrix4 rotationMatrix = Matrix4.CreateFromQuaternion(qRoll * qPitch * qYaw);

            // Translate^-1
            Matrix4 translationMatrix = Matrix4.CreateTranslation(-glLocation);

            // Final view matrix = Rotate^-1 * Translate^-1
            //    Usually you multiply rotation * translation in this order
            //    to get "R^-1 * T^-1".
            Matrix4 viewMatrix = translationMatrix * rotationMatrix;

            return viewMatrix;
        }

        public override Matrix4 GetProjectionMatrix(int controlWidth, int controlHeight)
        {
            float aspect = (float)controlWidth / controlHeight;
            Matrix4 projection = Matrix4.CreateOrthographic(OrthoScale * aspect, OrthoScale, NearPlane, FarPlane);
            return projection;
        }

        /// <summary>
        /// Gets the rotation of the camera, in degrees (Roll, Pitch, Yaw).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The rotation of the camera, in degrees (Roll, Pitch, Yaw). </returns>
        public Vector3 GetRotation(RendererControl rendererControl)
        {
            return Rotation;
        }

        /// <summary>
        /// Gets the position of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The position of the camera, in World units (X, Y, Z). </returns>
        public override Vector3 GetLocation(RendererControl rendererControl)
        {
            // Start with whatever our camera's "base" location is
            Vector3 location = Location;

            if (IsMiddleMouseButtonDown)
            {
                Vector2 mouseDelta = GetMouseDelta();

                // Convert pixel movement to world-space movement
                float unitsPerPixel = OrthoScale / rendererControl.Height;

                // Invert X or Y as you like for a “natural” drag feel
                float moveX = mouseDelta.X * unitsPerPixel;
                float moveY = mouseDelta.Y * unitsPerPixel;

                // Adjust location
                location.X += moveX;
                location.Y += moveY;
            }

            return location;
        }

        public override void MouseDown(RendererControl rendererControl, MouseButtons button)
        {
            if (button == MouseButtons.Middle)
            {
                Cursor.Current = Cursors.SizeAll;
            }

            base.MouseDown(rendererControl, button);
        }

        public override void MouseUp(RendererControl rendererControl, MouseButtons button)
        {
            if (button == MouseButtons.Middle)
            {
                Cursor.Current = Cursors.Default;
                Location = GetLocation(rendererControl);
            }

            base.MouseUp(rendererControl, button);
        }

        public override void MouseWheel(RendererControl rendererControl, MouseEventArgs e)
        {
            // 1) The screen position (mouse in control coords)
            Vector2 mouseScreenPos = new Vector2(e.X, e.Y);

            // 2) Convert that to a world position BEFORE zoom changes
            Vector2 oldWorldPos = ScreenToWorld(
                mouseScreenPos,
                new Vector2(Location.X, Location.Y),
                OrthoScale,
                rendererControl.Width,
                rendererControl.Height
            );

            // 3) Apply the zoom
            // e.Delta is typically ±120 per notch, so let's do a mild exponential
            float zoomFactor = (float)Math.Pow(1.05, e.Delta / 40.0);
            OrthoScale /= zoomFactor;

            // Optionally clamp so we don't get negative or insane values
            if (OrthoScale < 0.1f) OrthoScale = 0.1f;
            if (OrthoScale > 100000f) OrthoScale = 100000f;

            // 4) Convert the same mouse screen position to a world position AFTER the new scale
            Vector2 newWorldPos = ScreenToWorld(
                mouseScreenPos,
                new Vector2(Location.X, Location.Y),
                OrthoScale,
                rendererControl.Width,
                rendererControl.Height
            );

            // 5) Adjust the camera Location so that oldWorldPos is still under the mouse
            // That means we translate the camera by the difference:
            Vector2 worldOffset = oldWorldPos - newWorldPos;
            Location = new Vector3(
                Location.X + worldOffset.X,
                Location.Y + worldOffset.Y,
                Location.Z
            );

            // Optionally call base if you want, but be sure base doesn't undo your logic
            base.MouseWheel(rendererControl, e);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="screenPos"></param>
        /// <param name="cameraLoc"></param>
        /// <param name="orthoScale"></param>
        /// <param name="controlWidth"></param>
        /// <param name="controlHeight"></param>
        /// <returns></returns>
        private Vector2 ScreenToWorld(Vector2 screenPos, Vector2 cameraLoc, float orthoScale, int controlWidth, int controlHeight)
        {
            // The camera is typically centered at the middle of the screen in orthographic
            // so let's define (controlWidth/2, controlHeight/2) as the cameraLoc in screen space.

            float aspect = (float)controlWidth / controlHeight;

            // This is how many world units 1 pixel corresponds to (vertically).
            float unitsPerPixelY = orthoScale / controlHeight;
            // For X, scale the same fraction, but multiplied by aspect:
            float unitsPerPixelX = unitsPerPixelY;

            // Offset from the screen center in pixels:
            float pxFromCenter = screenPos.X - (controlWidth / 2f);
            float pyFromCenter = screenPos.Y - (controlHeight / 2f);

            // Convert pixel offset to world offset:
            float dxWorld = pxFromCenter * unitsPerPixelX;
            float dyWorld = -pyFromCenter * unitsPerPixelY;
            // note the minus sign if Y is “inverted” (top-left vs. bottom-left).

            // The final world coords = cameraLoc + offset
            Vector2 worldPos = cameraLoc + new Vector2(dxWorld, dyWorld);

            return worldPos;
        }
    }
}
