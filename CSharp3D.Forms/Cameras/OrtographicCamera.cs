using CSharp3D.Forms.Controls;
using CSharp3D.Forms.Engine.Helpers;
using CSharp3D.Forms.Utils;
using CSharp3D.Forms.Engine;
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
        [TypeConverter(typeof(RotationVectorTypeConverter))]
        [Description("The rotation of the camera, in degrees (Roll, Pitch, Yaw).")]
        public RotationVector Rotation { get; set; } = new RotationVector(0, 0, 0);

        /// <summary>
        /// The distance from the camera to the origin, in World units.
        /// </summary>
        [Category("Position")]
        [TypeConverter(typeof(LocationVectorTypeConverter))]
        [Description("The location of the camera relative to the origin, in World units.")]
        public LocationVector Location { get; set; } = new LocationVector(0, 0, 0);

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

        public OrtographicCamera(RotationVector direction, LocationVector location)
        {
            Rotation = direction;
            Location = location;

            Projection = Projections.Ortographic;
        }

        // Axis-locked presets for a Hammer-style quad view (World space: X forward, Y left, Z up).
        // Rotation is (Roll, Pitch, Yaw) in degrees.
        //
        // The angles are chosen so each view's SCREEN AXES match Hammer's 2D views
        // (hammer/mapview.h DrawType_t + axes2.h): Top = XY, Front = XZ, Side = YZ, each with its
        // two axes increasing right and up. They were solved numerically against GetViewBasis
        // rather than guessed — if a view ever looks rotated or mirrored, fix it here, in one
        // place, and re-check the basis rather than compensating downstream.

        /// <summary> Top view — looks down the Z axis onto the X/Y plane (right = +X, up = +Y). </summary>
        public static OrtographicCamera CreateTop()
        {
            return new OrtographicCamera(new RotationVector(0, -90, -90), new LocationVector(0, 0, 0));
        }

        /// <summary> Front view — looks along the Y axis onto the X/Z plane (right = +X, up = +Z). </summary>
        public static OrtographicCamera CreateFront()
        {
            return new OrtographicCamera(new RotationVector(0, 0, -90), new LocationVector(0, 0, 0));
        }

        /// <summary> Side view — looks along the X axis onto the Y/Z plane (right = +Y, up = +Z). </summary>
        public static OrtographicCamera CreateSide()
        {
            return new OrtographicCamera(new RotationVector(0, 0, 180), new LocationVector(0, 0, 0));
        }

        /// <summary>
        /// The camera's rotation as a GL-space matrix. Split out of <see cref="GetViewMatrix"/> so
        /// the pan/zoom code can derive the view basis without asking for the view matrix — that
        /// would recurse, since the view matrix depends on <see cref="GetLocation"/>.
        /// </summary>
        private Matrix4 GetRotationMatrix()
        {
            // Rotation is (Roll, Pitch, Yaw) in degrees; convert to GL space, then to radians.
            Vector3 glRotation = VectorOrientation.ToGL(Rotation) * MathHelper.Pi / 180f;

            Quaternion qPitch = Quaternion.FromAxisAngle(Vector3.UnitX, glRotation.X);
            Quaternion qYaw = Quaternion.FromAxisAngle(Vector3.UnitY, glRotation.Y);
            Quaternion qRoll = Quaternion.FromAxisAngle(Vector3.UnitZ, glRotation.Z);

            return Matrix4.CreateFromQuaternion(qRoll * qPitch * qYaw);
        }

        /// <summary>
        /// The camera's screen axes in World units: <paramref name="right"/> is the world direction
        /// that moves right across the view, <paramref name="up"/> the one that moves up it. For the
        /// axis-locked presets these are world axes (e.g. Top: right = +X, up = +Y), but deriving
        /// them from the rotation keeps pan and zoom correct for any orientation.
        /// </summary>
        public void GetViewBasis(out LocationVector right, out LocationVector up)
        {
            Vector3 glRight, glUp;
            CameraMath.ViewBasis(GetRotationMatrix(), out glRight, out glUp);

            right = VectorOrientation.ToWorldLocation(glRight);
            up = VectorOrientation.ToWorldLocation(glUp);
        }

        public override Matrix4 GetViewMatrix(RendererControl rendererControl)
        {
            Vector3 glLocation = VectorOrientation.ToGL(GetLocation(rendererControl));

            // Final view matrix = Rotate^-1 * Translate^-1.
            return Matrix4.CreateTranslation(-glLocation) * GetRotationMatrix();
        }

        /// <summary>
        /// World units per screen pixel. Both axes share one value: the projection is
        /// OrthoScale*aspect wide over `width` pixels and OrthoScale tall over `height`, and
        /// aspect = width/height, so the two ratios are equal.
        /// </summary>
        private float GetUnitsPerPixel(int controlHeight)
        {
            return controlHeight > 0 ? OrthoScale / controlHeight : OrthoScale;
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
        public RotationVector GetRotation(RendererControl rendererControl)
        {
            return Rotation;
        }

        /// <summary>
        /// Gets the position of the camera, in World units (X, Y, Z).
        /// </summary>
        /// <param name="controlWidth"> The width of the control. </param>
        /// <param name="controlHeight"> The height of the control. </param>
        /// <returns> The position of the camera, in World units (X, Y, Z). </returns>
        public override LocationVector GetLocation(RendererControl rendererControl)
        {
            // Start with whatever our camera's "base" location is
            LocationVector location = Location;

            if (IsMiddleMouseButtonDown)
            {
                Vector2 mouseDelta = GetMouseDelta();
                float unitsPerPixel = GetUnitsPerPixel(rendererControl.Height);

                // Pan along the camera's own screen axes, not fixed world X/Y — otherwise every
                // view but a front-facing one drags along the wrong plane (and partly into the
                // view axis, which an orthographic camera cannot even show).
                LocationVector right, up;
                GetViewBasis(out right, out up);

                // GetMouseDelta already negates screen X and keeps screen Y (which grows downward),
                // so adding both moves the camera opposite the cursor: the model follows the mouse.
                location = location
                    + Scaled(right, mouseDelta.X * unitsPerPixel)
                    + Scaled(up, mouseDelta.Y * unitsPerPixel);
            }

            return location;
        }

        private static LocationVector Scaled(LocationVector v, float scale)
        {
            return new LocationVector(v.X * scale, v.Y * scale, v.Z * scale);
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
            // Zoom about the cursor: the world point under it must not move. That point sits at
            // Location + right*(px*unitsPerPixel) + up*(-py*unitsPerPixel), where px/py are the
            // cursor's offset from the view centre — so when unitsPerPixel changes by `duPP`, the
            // camera has to shift by that same expression scaled by duPP to compensate.
            float pxFromCenter = e.X - rendererControl.Width / 2f;
            float pyFromCenter = e.Y - rendererControl.Height / 2f;

            float oldUnitsPerPixel = GetUnitsPerPixel(rendererControl.Height);

            // e.Delta is typically ±120 per notch, so this is a mild exponential.
            float zoomFactor = (float)Math.Pow(1.05, e.Delta / 40.0);
            OrthoScale /= zoomFactor;

            // Clamp so we don't get negative or insane values.
            if (OrthoScale < 0.1f) OrthoScale = 0.1f;
            if (OrthoScale > 100000f) OrthoScale = 100000f;

            float deltaUnitsPerPixel = oldUnitsPerPixel - GetUnitsPerPixel(rendererControl.Height);

            // Along the camera's own axes, so the anchor holds in every view orientation.
            LocationVector right, up;
            GetViewBasis(out right, out up);

            Location = Location
                + Scaled(right, pxFromCenter * deltaUnitsPerPixel)
                + Scaled(up, -pyFromCenter * deltaUnitsPerPixel);

            base.MouseWheel(rendererControl, e);
        }
    }
}
