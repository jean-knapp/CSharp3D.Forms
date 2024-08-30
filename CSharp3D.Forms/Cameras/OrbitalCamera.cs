using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Utils;
using OpenTK;
using System;
using System.ComponentModel;
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
        [TypeConverter(typeof(Vector3TypeConverter))]
        [Description("The rotation of the camera, in degrees (Roll, Pitch, Yaw).")]
        public Vector3 Rotation { get; set; } = new Vector3(0, 0, 0);

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

        public OrbitalCamera(Vector3 direction, float distance)
        {
            Rotation = direction;
            Distance = distance;
        }

        public override Matrix4 GetViewMatrix(int controlWidth, int controlHeight)
        {
            Vector3 location = VectorOrientation.ToGL(new Vector3(-Distance, 0, 0)) * -1;
            Vector3 rotation = VectorOrientation.ToGL(GetRotation(controlWidth, controlHeight)) * -1 * MathHelper.Pi / 180f;

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
        public Vector3 GetRotation(int controlWidth, int controlHeight)
        {
            var mouseDelta = GetMouseDelta();
            Vector3 rotation = Rotation;
            rotation = rotation + new Vector3(0, 2 * mouseDelta.Y * FOV / controlHeight, 2 * mouseDelta.X * FOV / controlWidth);

            if (!ClampVertically)
            {
                rotation.Y = MathHelper.Clamp(rotation.Y, -90, 90);
            }

            rotation.X = (rotation.X + 180) % 360 - 180;
            rotation.Y = (rotation.Y + 180) % 360 - 180;
            rotation.Z = (rotation.Z + 180) % 360 - 180;          

            return rotation;
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
        /// Gets the distance from the camera to the origin, in World units.
        /// </summary>
        /// <param name="viewMatrix"> The view matrix of the camera. </param>
        /// <returns> The distance from the camera to the origin, in World units. </returns>
        public static float GetDistance(Matrix4 viewMatrix)
        {
            return -viewMatrix.M43;
        }

        /// <summary>
        /// Gets the location of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="viewMatrix"> The view matrix of the camera. </param>
        /// <returns> The location of the camera, in World units (X, Y, Z). </returns>
        public static Vector3 GetLocation(Matrix4 viewMatrix)
        {
            Vector3 rotation = VectorOrientation.ToGL(GetRotation(viewMatrix)) * MathHelper.Pi / 180f;
            float distance = GetDistance(viewMatrix);

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
        public Vector3 GetLocation(int controlWidth, int controlHeight)
        {
            var rot = GetRotation(controlWidth, controlHeight);

            float x = Distance * (float)Math.Cos((rot.Z + 180) * Math.PI / 180f) * (float)Math.Cos((rot.Y) * Math.PI / 180f);
            float y = Distance * (float)Math.Sin((rot.Z + 180) * Math.PI / 180f) * (float)Math.Cos((rot.Y) * Math.PI / 180f);
            float z = Distance * (float)Math.Sin((rot.Y) * Math.PI / 180f);

            // Calculate the camera position
            return new Vector3(x, y, z);
        }

        private static float CopySign(float size, float sign)
        {
            return Math.Sign(sign) == -1 ? -Math.Abs(size) : Math.Abs(size);
        }

        public override void MouseUp(int controlWidth, int controlHeight)
        {
            Rotation = GetRotation(controlWidth, controlHeight);

            base.MouseUp(controlWidth, controlHeight);
        }

        public override void MouseWheel(MouseEventArgs e)
        {
            base.MouseWheel(e);

            Distance = (float)(Distance * Math.Pow(2, -e.Delta / 256f));
        }
    }
}
