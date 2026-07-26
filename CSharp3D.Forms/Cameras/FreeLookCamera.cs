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
        /// World units the wheel dollies per notch. 0 (the default) keeps the historical
        /// behaviour of stepping by <see cref="MoveSpeed"/>.
        /// Hammer's 3D view uses a fixed 60 (mapview3d.cpp:1754, <c>MoveForward(zDelta / 2)</c>
        /// with zDelta = 120 per notch).
        /// </summary>
        public float WheelStepDistance { get; set; } = 0;

        /// <summary>
        /// Seconds to reach <see cref="MoveSpeed"/> from rest. 0 (the default) snaps
        /// straight to full speed, which is how this camera always behaved.
        /// Hammer's <c>TimeToMaxSpeed</c> default is 500 ms (options.cpp:1022).
        /// </summary>
        public float TimeToMaxSpeed { get; set; } = 0;

        /// <summary>Strafe speed as a fraction of <see cref="MoveSpeed"/> (Hammer: 0.75).</summary>
        public float StrafeSpeedScale { get; set; } = 0.75f;

        private float forwardVelocity;
        private float strafeVelocity;
        private bool wasMoving;

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

            // Per notch: WheelStepDistance when set, else MoveSpeed (the original
            // behaviour, kept as the default so other editors are unaffected).
            // Tying the dolly to MoveSpeed only works while MoveSpeed is modest — a host
            // that scales fly speed to its content ends up teleporting on every notch.
            float perNotch = WheelStepDistance > 0 ? WheelStepDistance : MoveSpeed;

            float distance = (e.Delta / 120f) * perNotch;

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
            bool moving = wDown || aDown || sDown || dDown;

            // Starting a movement burst: throw this frame's elapsed time away.
            //
            // deltaTime measures paint-to-paint, and the view only paints when something
            // invalidates it — so after the camera has been still, the "frame" that
            // notices the first key press is as long as the idle period. Integrating that
            // launched the camera forward in one step before settling into normal speed,
            // which is what the teleport was. Hammer never sees this because it samples
            // input on its own steady timer (mapview3d.cpp ProcessInput), not from
            // rendering, so its clock keeps ticking while the view sits idle.
            if (moving && !wasMoving)
                deltaTime = 0;

            wasMoving = moving;

            // Hammer's speed model (mapview3d.cpp:931 and Accelerate at :1404): each axis
            // has its own velocity that ramps to its maximum over TimeToMaxSpeed, is
            // zeroed the moment the key is released, and is zeroed on direction reversal.
            float forwardMax = MoveSpeed;
            float strafeMax = MoveSpeed * StrafeSpeedScale;

            float accelTime = TimeToMaxSpeed;
            float forwardAccel = accelTime > 0 ? forwardMax / accelTime : 0;
            float strafeAccel = accelTime > 0 ? strafeMax / accelTime : 0;

            int moveForward = (wDown ? 1 : 0) - (sDown ? 1 : 0);
            int moveLeft = (aDown ? 1 : 0) - (dDown ? 1 : 0);

            float forwardBefore = forwardVelocity;
            float strafeBefore = strafeVelocity;

            forwardVelocity = Accelerate(forwardVelocity, forwardAccel, moveForward, (float)deltaTime, forwardMax);
            strafeVelocity = Accelerate(strafeVelocity, strafeAccel, moveLeft, (float)deltaTime, strafeMax);

            // Shift is Hammer's speed boost (mapview3d.cpp:1531).
            float boost = shiftDown ? 2.0f : 1.0f;

            // Integrate over the average of the frame's start and end velocity, which is
            // exact for constant acceleration. Using the end velocity alone (plain Euler)
            // overshoots during the ramp in proportion to the frame time, so the same
            // key press covered measurably more ground at 8 fps than at 240.
            double forwardStep = (forwardBefore + forwardVelocity) * 0.5 * boost * deltaTime;
            double strafeStep = (strafeBefore + strafeVelocity) * 0.5 * boost * deltaTime;

            // A released key zeroes its velocity instantly (Hammer), so the average would
            // otherwise let it coast half a frame further.
            double forwardDistance = moveForward == 0 ? 0 : forwardStep;
            double strafeDistance = moveLeft == 0 ? 0 : strafeStep;

            // Keep the existing diagonal normalisation so moving on both axes is not
            // faster than moving on one.
            if (moveForward != 0 && moveLeft != 0)
            {
                forwardDistance /= Math.Sqrt(2);
                strafeDistance /= Math.Sqrt(2);
            }

            float x = Location.X;
            float y = Location.Y;
            float z = Location.Z;

            var rotation = GetRotation(control);

            double yaw = -rotation.Yaw * Math.PI / 180f;
            double pitch = rotation.Pitch * Math.PI / 180f;

            // Signed velocities, so one term each instead of a branch per key.
            x += (float)(forwardDistance * Math.Cos(yaw) * Math.Cos(pitch));
            y += (float)(forwardDistance * Math.Sin(yaw) * Math.Cos(pitch));
            z += (float)(forwardDistance * Math.Sin(pitch));

            x += (float)(strafeDistance * Math.Cos(yaw + Math.PI / 2));
            y += (float)(strafeDistance * Math.Sin(yaw + Math.PI / 2));

            Location = new LocationVector(x, y, z);
        }

        /// <summary>
        /// mapview3d.cpp:1404. Ramps a velocity toward its maximum, zeroing it when the
        /// input reverses or stops. With no acceleration configured it snaps straight to
        /// full speed, which is the behaviour hosts had before the ramp existed.
        /// </summary>
        private static float Accelerate(float velocity, float acceleration, int scale, float deltaTime, float maxVelocity)
        {
            if (scale == 0)
                return 0;                       // key released: Hammer stops dead

            if (acceleration == 0)
                return maxVelocity * scale;     // infinite acceleration

            // Direction reversal starts from rest rather than decelerating through zero.
            if (scale > 0 && velocity < 0)
                velocity = 0;
            else if (scale < 0 && velocity > 0)
                velocity = 0;

            velocity += acceleration * scale * deltaTime;

            if (velocity > maxVelocity)
                velocity = maxVelocity;
            else if (velocity < -maxVelocity)
                velocity = -maxVelocity;

            return velocity;
        }
    }
}
