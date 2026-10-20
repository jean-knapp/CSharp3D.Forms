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

        /// <summary>
        /// Optional, one per vertex: an offset in pixels (X right, Y up; Z unused) applied after
        /// projection, so the vertices it moves draw at the same on-screen size at every zoom.
        /// It is how a mesh carries editor handles - Hammer's centre X on every brush in a 2D view
        /// (mapsolid.cpp Render2D) - beside the geometry they belong to, sharing its colour and
        /// its selection tint. A vertex with a zero offset is an ordinary line vertex. Handles
        /// are drawn only in orthographic views, and only while <see cref="ScreenExtents"/> is at
        /// least twice the handle's size on screen. Null (the default) means no handles at all.
        /// </summary>
        [Browsable(false)]
        public LocationVector[] ScreenOffsets { get; set; }

        /// <summary>
        /// One per vertex alongside <see cref="ScreenOffsets"/>: the world-space size of the shape
        /// a handle vertex marks. A handle whose shape projects smaller than the handle is not
        /// drawn - a cross bigger than the brush it marks would hide the brush.
        /// </summary>
        [Browsable(false)]
        public LocationVector[] ScreenExtents { get; set; }

        /// <summary>The viewport query's buffer; draws happen on the GL thread only.</summary>
        private static readonly int[] viewport = new int[4];

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
        /// Screen offsets are in pixels, so the shader needs the viewport to turn them into clip
        /// space. Set on the program the base draw has just bound, from the size the renderer
        /// recorded for this frame (Mesh.BeginFrame) - asking GL for it (glGetIntegerv) on every
        /// draw stalls a threaded driver.
        /// </summary>
        protected override void ApplyCustomUniforms(object context, Scene scene, int shaderProgram, MeshDrawMode drawMode)
        {
            if (ScreenOffsets == null)
                return;

            int location = Shader.GetUniforms(context, shaderProgram).ViewportSize;

            if (location < 0)
                return;

            int width = ViewportWidth, height = ViewportHeight;

            // Drawn outside a renderer frame: ask GL after all.
            if (width <= 0 || height <= 0)
            {
                GL.GetInteger(GetPName.Viewport, viewport);
                width = viewport[2];
                height = viewport[3];
            }

            GL.Uniform2(location, (float)width, (float)height);
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

                // A handle vertex: its pixel offset in the normal's xy, and the extents of the
                // shape it marks in the remaining slots, in the same (-y, z, -x) GL order as the
                // position (signs dropped - they are sizes).
                if (ScreenOffsets != null && i < ScreenOffsets.Length
                    && (ScreenOffsets[i].X != 0 || ScreenOffsets[i].Y != 0))
                {
                    LocationVector extents = ScreenExtents != null && i < ScreenExtents.Length
                        ? ScreenExtents[i]
                        : new LocationVector(0, 0, 0);

                    result[i * 8 + 3] = ScreenOffsets[i].X;
                    result[i * 8 + 4] = ScreenOffsets[i].Y;
                    result[i * 8 + 5] = System.Math.Abs(extents.X);
                    result[i * 8 + 6] = System.Math.Abs(extents.Y);
                    result[i * 8 + 7] = System.Math.Abs(extents.Z);
                }
            }

            return result;
        }

        /// <summary>
        /// Get the index array of the mesh.
        /// </summary>
        /// <returns> The index array of the mesh. </returns>
        /// <summary>
        /// One index per vertex - counted, not built. The draw path asks for this on every
        /// draw, and the base falls back to building the array: an allocation per outline per
        /// pane per frame, thousands of them on a real map.
        /// </summary>
        public override int GetIndexCount()
        {
            return Vertices != null ? Vertices.Length : 0;
        }

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
