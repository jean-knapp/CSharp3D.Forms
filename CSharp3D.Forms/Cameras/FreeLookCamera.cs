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
    public class FreeLookCamera : Camera
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
        public Vector3 Location { get; set; } = new Vector3(0,0,0);

        /// <summary>
        /// Whether to clamp the vertical rotation of the camera to 90 degrees.
        /// </summary>
        [Category("Behavior")]
        [Description("Whether to clamp the vertical rotation of the camera to 90 degrees.")]
        public bool ClampVertically { get; set; } = false;

        [Category("Behavior")]
        [Description("The default number of units per second to move the camera on input.")]
        public int MoveSpeed { get; set; } = 5;

        public FreeLookCamera()
        {
        }

        public FreeLookCamera(Vector3 direction, Vector3 location)
        {
            Rotation = direction;
            Location = location;
        }

        public override Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            // 1) Convert Rotation (degrees) and Location from World to GL space.
            //    Rotation is (Roll, Pitch, Yaw) in degrees.
            //    Location is (X, Y, Z) in world units.
            var worldRotation = GetRotation(rendererControl);

            Vector3 glRotation = VectorOrientation.ToGL(worldRotation);
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
        public Vector3 GetRotation(RendererControl rendererControl)
        {
            var mouseDelta = GetMouseDelta();
            Vector3 rotation = Rotation;
            rotation = rotation - new Vector3(0, 2 * mouseDelta.Y * FOV / rendererControl.Height, 2 * mouseDelta.X * FOV / rendererControl.Width);

            if (!ClampVertically)
            {
                rotation.Y = MathHelper.Clamp(rotation.Y, -90, 90);
            }

            rotation.X = (rotation.X + 180) % 360 - 180;
            rotation.Y = (rotation.Y + 180) % 360 - 180;
            rotation.Z = (rotation.Z + 180) % 360 - 180;

            // Recenter mouse
            var center = rendererControl.GetCenterPointInScreen();
            var diffX = Cursor.Position.X - center.X;
            var diffY = Cursor.Position.Y - center.Y;
            mouseStartLocation = new Vector2(mouseStartLocation.X - diffX, mouseStartLocation.Y - diffY);
            Cursor.Position = center;

            return rotation;
        }

        /// <summary>
        /// Gets the position of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The position of the camera, in World units (X, Y, Z). </returns>
        public override Vector3 GetLocation(RendererControl rendererControl)
        {
            return Location;
        }

        public override void MouseUp(RendererControl rendererControl)
        {
            Rotation = GetRotation(rendererControl);

            base.MouseUp(rendererControl);
        }

        public override void MouseWheel(MouseEventArgs e)
        {
            base.MouseWheel(e);
        }

        /// <summary>
        /// Updates the camera each frame.
        /// This is where you poll keyboard & mouse, or pass them in from outside.
        /// </summary>
        /// 
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

            double yaw = -rotation.Z * Math.PI / 180f;
            double pitch = rotation.Y * Math.PI / 180f;

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

            Location = new Vector3(x, y, z);
        }
    }
}
