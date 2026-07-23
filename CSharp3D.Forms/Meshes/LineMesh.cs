using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A line mesh.
    /// </summary>
    [ToolboxItem(false)]
    public class LineMesh : Mesh
    {
        /// <summary>
        /// The vertices of the line.
        /// </summary>
        [Browsable(false)]
        public LocationVector[] Vertices { get; set; } = { };

        /// <summary>
        /// Line thickness in pixels. Widths above 1 make lines easier to see and to click, which
        /// is what an editor wants for selectable edges. Drivers clamp this to their supported
        /// range (often around 10), so treat it as a request rather than a guarantee.
        /// </summary>
        [Browsable(false)]
        public float LineWidth { get; set; } = 1f;

        /// <summary>
        /// Draw the line dotted in orthographic views only, matching Hammer's tool boxes: Hammer
        /// stipples them in the 2D views (`Box3D::RenderTool2D` uses `RENDER_MODE_DOTTED`) but draws
        /// them solid in 3D (`RenderTool3D` uses `RENDER_MODE_FLAT`). The view is told apart from
        /// the projection matrix, so the same shared mesh is dotted in a 2D pane and solid in the
        /// perspective pane without needing a copy per view.
        /// </summary>
        [Browsable(false)]
        public bool Dotted { get; set; } = false;

        /// <summary>
        /// Whether <see cref="Dotted"/> also applies in a perspective view. Off keeps Hammer's
        /// split (stippled in 2D, solid in 3D); on stipples everywhere, for a line whose whole job
        /// is to read as a guide rather than as geometry no matter which pane it is seen in.
        /// </summary>
        [Browsable(false)]
        public bool DottedInPerspective { get; set; } = false;

        /// <summary>
        /// The GL line-stipple pattern used when <see cref="Dotted"/> is on. 0xAAAA is one bit on,
        /// one off — a fine dotted line once the factor stretches each bit over a couple of pixels.
        /// </summary>
        [Browsable(false)]
        public short StipplePattern { get; set; } = unchecked((short)0xAAAA);

        /// <summary> How many pixels each bit of <see cref="StipplePattern"/> covers. </summary>
        [Browsable(false)]
        public int StippleFactor { get; set; } = 2;

        public LineMesh() : base()
        {
            Material = new Engine.Material();
            Material.ShaderName = "Wireframe";
            PrimitiveType = PrimitiveType.Lines;
        }

        public LineMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {
            Material = new Engine.Material();
            Material.ShaderName = "Wireframe";
            PrimitiveType = PrimitiveType.Lines;
        }

        public override void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view,
            MeshDrawMode drawMode = MeshDrawMode.Textured)
        {
            // Stipple only in orthographic views by default, so a dotted 2D box stays solid in 3D
            // like Hammer — unless the mesh asks for it everywhere.
            // An orthographic projection has M44 == 1; a perspective one has M44 == 0.
            bool orthographic = System.Math.Abs(projection.M44 - 1f) < 1e-4f;
            bool stipple = Dotted && (orthographic || DottedInPerspective);
            bool wide = LineWidth != 1f;

            // Both are global GL state, so restore them for whatever draws next.
            if (stipple)
            {
                GL.Enable(EnableCap.LineStipple);
                GL.LineStipple(StippleFactor, StipplePattern);
            }
            if (wide)
                GL.LineWidth(LineWidth);

            base.DrawMesh(context, scene, projection, view, drawMode);

            if (wide)
                GL.LineWidth(1f);
            if (stipple)
                GL.Disable(EnableCap.LineStipple);
        }

        /// <summary>
        /// Get the vertex array of the mesh.
        /// </summary>
        /// <returns> The vertex array of the mesh. </returns>
        public override float[] GetGLVertexArray()
        {
            // Cuboid vertex data with positions and texture coordinates

            // Positions (-y, z, -x) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[Vertices.Length * 8];

            // Vertex data should be stored as world x,y,z, and u, v
            for (int i = 0; i < Vertices.Length; i++)
            {
                result[i * 8] = -Vertices[i].Y;
                result[i * 8 + 1] = Vertices[i].Z;
                result[i * 8 + 2] = -Vertices[i].X;
                result[i * 8 + 3] = 0;
                result[i * 8 + 4] = 0;
                result[i * 8 + 5] = 0;
                result[i * 8 + 6] = 0;
                result[i * 8 + 7] = 0;
            }

            return result;
        }

        /// <summary>
        /// Get the index array of the mesh.
        /// </summary>
        /// <returns> The index array of the mesh. </returns>
        public override uint[] GetIndexArray()
        {
            // Cuboid indices
            uint[] indices = new uint[Vertices.Length];
            for (uint i = 0; i < Vertices.Length; i++)
            {
                indices[i] = i;

            }

            return indices;
        }
    }
}
