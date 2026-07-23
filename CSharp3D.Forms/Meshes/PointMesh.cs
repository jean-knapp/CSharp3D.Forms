using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// Screen-facing square handles at a set of world positions — vertex markers for an editor.
    ///
    /// These are GL points rather than little cubes on purpose. A handle's job is to be seen and
    /// clicked, so it wants a **constant size on screen**: a world-space cube shrinks to nothing
    /// as you zoom out and swells to a blob as you zoom in, exactly when you need it steady. Points
    /// also cost one draw call for the whole set, which is what makes marking every vertex of a
    /// model with tens of thousands of them practical.
    ///
    /// Inherits <see cref="LineMesh"/> for its position-only vertex handling and unlit shader; only
    /// the primitive and the size differ.
    /// </summary>
    [ToolboxItem(false)]
    public class PointMesh : LineMesh
    {
        /// <summary>
        /// Handle size in pixels. Drivers clamp this to their supported range, so treat it as a
        /// request rather than a guarantee.
        /// </summary>
        [Browsable(false)]
        public float PointSize { get; set; } = 5f;

        public PointMesh() : base()
        {
            PrimitiveType = PrimitiveType.Points;
        }

        public PointMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {
            PrimitiveType = PrimitiveType.Points;
        }

        public override void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view,
            MeshDrawMode drawMode = MeshDrawMode.Textured)
        {
            // Size is global GL state, so put it back for whatever draws next. Point smoothing is
            // forced off so handles are crisp squares (Hammer's HANDLE_SQUARE) rather than the round
            // dots some drivers default to when GL_POINT_SMOOTH is enabled.
            bool wasSmooth = GL.IsEnabled(EnableCap.PointSmooth);
            if (wasSmooth)
                GL.Disable(EnableCap.PointSmooth);

            GL.PointSize(PointSize);
            base.DrawMesh(context, scene, projection, view, drawMode);
            GL.PointSize(1f);

            if (wasSmooth)
                GL.Enable(EnableCap.PointSmooth);
        }
    }
}
