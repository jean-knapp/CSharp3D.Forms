using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace CSharp3D.Forms.Cameras
{
    /// <summary>
    /// A free-look camera that can move in 3D space with WASD and mouse.
    /// </summary>
    [ToolboxItem(true)]
    [Description("A free-look camera controlled by WASD and mouse movement.")]
    public class FreeLookCamera : Camera
    {
        /// <summary>
        /// The rotation of the camera, in degrees (Roll, Pitch, Yaw).
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(RotationVectorTypeConverter))]
        [Description("The rotation of the camera, in degrees (Roll, Pitch, Yaw).")]
        public RotationVector Rotation { get; set; } = new RotationVector(0, 0, 0);

        /// <summary>
        /// The distance from the camera to the origin, in World units.
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(LocationVectorTypeConverter))]
        [Description("The location of the camera relative to the origin, in World units.")]
        public LocationVector Location { get; set; } = new LocationVector(0,0,0);

        /// <summary>
        /// Whether to clamp the vertical rotation of the camera to 90 degrees.
        /// </summary>
        [Category("Behavior")]
        [Description("Whether to clamp the vertical rotation of the camera to 90 degrees.")]
        public bool ClampVertically { get; set; } = false;

        [Category("Behavior")]
        [Description("The default number of units per second to move the camera on input.")]
        public int MoveSpeed { get; set; } = 5;

        public bool MouseLook { get; set; } = false;

        public FreeLookCamera()
        {
        }

        public FreeLookCamera(RotationVector direction, LocationVector location)
        {
            Rotation = direction;
            Location = location;
        }

        public override Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            // 1) Convert Rotation (degrees) and Location from World to GL space.
            //    Rotation is (Roll, Pitch, Yaw) in degrees.
            //    Location is (X, Y, Z) in world units.

            Vector3 glRotation = VectorOrientation.ToGL(GetRotation(rendererControl));
            Vector3 glLocation = VectorOrientation.ToGL(Location);

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

        /// <summary>
        /// Gets the rotation of the camera, in degrees (Roll, Pitch, Yaw).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The rotation of the camera, in degrees (Roll, Pitch, Yaw). </returns>
        public RotationVector GetRotation(RendererControl rendererControl)
        {
            var mouseDelta = GetMouseDelta();
            RotationVector rotation = Rotation;

            if (IsMiddleMouseButtonDown || IsRightMouseButtonDown || MouseLook)
            {
                rotation = rotation - new RotationVector(0, 2 * mouseDelta.Y * FOV / rendererControl.Height, 2 * mouseDelta.X * FOV / rendererControl.Width);

                if (!ClampVertically)
                {
                    rotation.Pitch = MathHelper.Clamp(rotation.Pitch, -90, 90);
                }

                rotation.Roll = (rotation.Roll + 180) % 360 - 180;
                rotation.Pitch = (rotation.Pitch + 180) % 360 - 180;
                rotation.Yaw = (rotation.Yaw + 180) % 360 - 180;

                RecenterMouse(rendererControl);
            }

            return rotation;
        }

        /// <summary>
        /// Gets the position of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The position of the camera, in World units (X, Y, Z). </returns>
        public override LocationVector GetLocation(RendererControl rendererControl)
        {
            return Location;
        }

        public override void MouseDown(RendererControl rendererControl, MouseButtons button)
        {
            // Hold the middle OR right mouse button to look around (Hammer uses the right button).
            if ((button == MouseButtons.Middle || button == MouseButtons.Right) && !MouseLook)
            {
                // Get the absolute position of the center of glControl
                Cursor.Position = rendererControl.GetCenterPointInScreen();
                Cursor.Current = MouseHelper.Cursors.Cross;
            }

            base.MouseDown(rendererControl, button);
        }

        public override void MouseUp(RendererControl rendererControl, MouseButtons button)
        {
            if ((button == MouseButtons.Middle || button == MouseButtons.Right) && !MouseLook)
            {
                Cursor.Current = Cursors.Default;
                Rotation = GetRotation(rendererControl);
            }

            base.MouseUp(rendererControl, button);
        }

        /// <summary>
        /// Dollies the camera forward/back along its current look direction — Hammer's 3D-view wheel
        /// behavior. Unlike WASD movement (<see cref="Move"/>), this needs no button held: the wheel
        /// only fires while the cursor is over this view, so that is gate enough on its own.
        /// </summary>
        public override void MouseWheel(RendererControl rendererControl, MouseEventArgs e)
        {
            base.MouseWheel(rendererControl, e);

            RotationVector rotation = GetRotation(rendererControl);
            double yaw = -rotation.Yaw * Math.PI / 180f;
            double pitch = rotation.Pitch * Math.PI / 180f;

            float distance = (e.Delta / 120f) * MoveSpeed;

            Location = new LocationVector(
                Location.X + (float)(distance * Math.Cos(yaw) * Math.Cos(pitch)),
                Location.Y + (float)(distance * Math.Sin(yaw) * Math.Cos(pitch)),
                Location.Z + (float)(distance * Math.Sin(pitch)));
        }

        /// <summary>
        /// Updates the camera each frame. This is where you poll keyboard & mouse, or pass them in
        /// from outside. Movement needs no mouse button held (only look-around, in
        /// <see cref="GetRotation"/>, does) — WASD works as soon as this key state reaches the
        /// camera, so a host that only forwards keys while its view has focus (e.g. on mouse-enter)
        /// gets "hover to fly" for free.
        /// </summary>
        public void Move(RendererControl control, double deltaTime, bool wDown, bool aDown, bool sDown, bool dDown, bool spaceDown, bool shiftDown)
        {
            float speed = MoveSpeed;

            double distance = speed * deltaTime;

            int longitudinalMovement = Math.Abs((wDown ? 1 : 0) - (sDown ? 1 : 0));
            int lateralMovement = Math.Abs((aDown ? 1 : 0) - (dDown ? 1 : 0));

            if (longitudinalMovement + lateralMovement > 1)
            {
                distance /= Math.Sqrt(2);
            }

            float x = Location.X;
            float y = Location.Y;
            float z = Location.Z;

            var rotation = GetRotation(control);

            double yaw = -rotation.Yaw * Math.PI / 180f;
            double pitch = rotation.Pitch * Math.PI / 180f;

            if (wDown)
            {
                x += (float)(distance * Math.Cos(yaw) * Math.Cos(pitch));
                y += (float)(distance * Math.Sin(yaw) * Math.Cos(pitch));
                z += (float)(distance * Math.Sin(pitch));
            }

            if (sDown)
            {
                x -= (float)(distance * Math.Cos(yaw) * Math.Cos(pitch));
                y -= (float)(distance * Math.Sin(yaw) * Math.Cos(pitch));
                z -= (float)(distance * Math.Sin(pitch));
            }

            if (aDown)
            {
                x += (float)(distance * Math.Cos(yaw + Math.PI / 2));
                y += (float)(distance * Math.Sin(yaw + Math.PI / 2));
            }

            if (dDown)
            {
                x -= (float)(distance * Math.Cos(yaw + Math.PI / 2));
                y -= (float)(distance * Math.Sin(yaw + Math.PI / 2));
            }

            Location = new LocationVector(x, y, z);
        }
    }
}
