using OpenTK;
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
        [Category("Appearance")]
        [Description("The projection type of the camera.")]
        public Projections Projection { get; set; } = Projections.Perspective;

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

        Vector2 mouseStartLocation = Vector2.Zero;

        public Camera()
        {

        }

        public virtual Matrix4 GetViewMatrix(int controlWidth, int controlHeight)
        {
            return Matrix4.Identity;
        }

        public Matrix4 GetProjectionMatrix(int controlWidth, int controlHeight)
        {
            Matrix4 projection;

            switch (Projection)
            {
                case Projections.Ortographic:
                    float scale = 5;
                    projection = Matrix4.CreateOrthographic(scale * (float)controlWidth / controlHeight, scale, NearPlane, FarPlane);
                    break;
                case Projections.Perspective:
                default:
                    projection = Matrix4.CreatePerspectiveFieldOfView(FOV * MathHelper.Pi / 180f, (float)controlWidth / controlHeight, NearPlane, FarPlane);
                    break;
            }

            return projection;
        }

        public void MouseDown(MouseEventArgs e)
        {
            mouseStartLocation = new Vector2(Cursor.Position.X, Cursor.Position.Y);

            IsMouseDown = true;
        }

        public virtual void MouseUp(int controlWidth, int controlHeight)
        {
            IsMouseDown = false;
        }

        public Vector2 GetMouseDelta()
        {
            if (!IsMouseDown)
            {
                return Vector2.Zero;
            }
            Vector2 mouseLocation = new Vector2(Cursor.Position.X, Cursor.Position.Y);
            Vector2 result = new Vector2(-(mouseLocation.X - mouseStartLocation.X), mouseLocation.Y - mouseStartLocation.Y);

            return result;
        }

        public virtual void MouseWheel(MouseEventArgs e)
        {

        }
    }
}
