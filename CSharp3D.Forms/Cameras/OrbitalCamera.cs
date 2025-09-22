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
    /// A camera that moves around a point in space.
    /// </summary>
    [ToolboxItem(true)]
    [Description("A camera that moves around a point in space.")]
    public class OrbitalCamera : Camera
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
        [Description("The distance from the camera to the origin, in World units.")]
        public float Distance { get; set; } = 4;

        /// <summary>
        /// Whether to clamp the vertical rotation of the camera to 90 degrees.
        /// </summary>
        [Category("Behavior")]
        [Description("Whether to clamp the vertical rotation of the camera to 90 degrees.")]
        public bool ClampVertically { get; set; } = false;

        public OrbitalCamera()
        {
        }

        public OrbitalCamera(RotationVector direction, float distance)
        {
            Rotation = direction;
            Distance = distance;
        }

        public override Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            Vector3 location = VectorOrientation.ToGL(new LocationVector(-Distance, 0, 0)) * -1;
            Vector3 rotation = VectorOrientation.ToGL(GetRotation(rendererControl)) * -1 * MathHelper.Pi / 180f;

            Quaternion qPitch = Quaternion.FromAxisAngle(Vector3.UnitX, rotation.X);
            Quaternion qYaw = Quaternion.FromAxisAngle(Vector3.UnitY, rotation.Y);
            Quaternion qRoll = Quaternion.FromAxisAngle(Vector3.UnitZ, rotation.Z);

            return Matrix4.CreateFromQuaternion(qRoll * qPitch * qYaw) * Matrix4.CreateTranslation(location);
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

            if (IsMouseDown)
            {
                rotation = rotation + new RotationVector(0, 2 * mouseDelta.Y * FOV / rendererControl.Height, 2 * mouseDelta.X * FOV / rendererControl.Width);

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
            var rot = GetRotation(rendererControl);
            if (rot == null)
                return new LocationVector(0, 0, 0);

            float x = Distance * (float)Math.Cos((rot.Yaw + 180) * Math.PI / 180f) * (float)Math.Cos((rot.Pitch) * Math.PI / 180f);
            float y = Distance * (float)Math.Sin((rot.Yaw + 180) * Math.PI / 180f) * (float)Math.Cos((rot.Pitch) * Math.PI / 180f);
            float z = Distance * (float)Math.Sin((rot.Pitch) * Math.PI / 180f);

            // Calculate the camera position
            return new LocationVector(x, y, z);
        }

        public override void MouseDown(RendererControl rendererControl, MouseButtons button)
        {
            // Get the absolute position of the center of glControl
            Cursor.Position = rendererControl.GetCenterPointInScreen();
            Cursor.Current = MouseHelper.Cursors.None;

            base.MouseDown(rendererControl, button);
        }

        public override void MouseUp(RendererControl rendererControl, MouseButtons button)
        {
            Cursor.Current = Cursors.Default;
            Rotation = GetRotation(rendererControl);

            base.MouseUp(rendererControl, button);
        }

        public override void MouseWheel(RendererControl rendererControl, MouseEventArgs e)
        {
            base.MouseWheel(rendererControl, e);

            Distance = (float)(Distance * Math.Pow(2, -e.Delta / 256f));
        }
    }
}
