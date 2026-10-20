using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// Where a string sits relative to its anchor, per axis. Named after Hammer's
    /// CRender::TEXT_JUSTIFY_* flags (Render.h), which say which side of the point the text
    /// grows towards rather than which edge touches it.
    /// </summary>
    public enum TextAnchor
    {
        /// <summary>Text starts at the anchor and grows right (horizontal) or down (vertical).</summary>
        Start,

        /// <summary>Text is centred on the anchor.</summary>
        Center,

        /// <summary>Text ends at the anchor: grows left (horizontal) or up (vertical).</summary>
        End,
    }

    /// <summary>
    /// A batch of strings drawn from a <see cref="GlyphAtlas"/>, all in one draw call.
    ///
    /// Two kinds of string share it:
    /// <list type="bullet">
    /// <item><b>Screen text</b> (<see cref="AddScreenText"/>): pinned to a world point but laid out
    /// in pixels, so it keeps its size at every zoom and in every view — Hammer's 2D labels
    /// (entity names, a selection's size). The vertex shader snaps the anchor to a whole pixel so
    /// each texel lands on exactly one pixel, which is what keeps small text crisp.</item>
    /// <item><b>World text</b> (<see cref="AddWorldText"/>): laid out on a plane in world units,
    /// which perspective then shrinks with distance — point_worldtext's in-world message.</item>
    /// </list>
    ///
    /// Its vertex layout is its own (position, pixel offset, texcoord, colour), so it uploads its
    /// own buffers instead of using <see cref="Mesh"/>'s 8-float layout. Rebuild it by calling
    /// <see cref="Clear"/>, adding strings again and setting <see cref="Mesh.NeedsVertexUpdate"/>
    /// (<see cref="Commit"/> does the last part).
    /// </summary>
    [ToolboxItem(false)]
    public class ScreenTextMesh : Mesh
    {
        /// <summary>Floats per vertex: position 3, pixel offset 2 + screen flag 1, uv 2, colour 4.</summary>
        private const int Stride = 12;

        private readonly List<float> vertexData = new List<float>();

        private float[] uploaded = new float[0];

        private int quadCount;

        /// <summary>The font the strings are drawn in.</summary>
        [Browsable(false)]
        public GlyphAtlas Atlas { get; set; } = GlyphAtlas.Default;

        /// <summary>
        /// Orthographic views hide the screen text while fewer than this many pixels cover a world
        /// unit — Hammer prints entity names only at a zoom of 1 or more (mapentity.cpp
        /// CMapEntity::Render2D, <c>pView-&gt;GetZoom() &gt;= 1</c>), since zoomed further out they
        /// pile into an unreadable smear. 0 (the default) always draws. Perspective views ignore it.
        /// </summary>
        [Browsable(false)]
        public float MinPixelsPerUnit { get; set; }

        /// <summary>Whether the strings added since <see cref="Clear"/> are none at all.</summary>
        public bool IsEmpty
        {
            get { return quadCount == 0; }
        }

        public ScreenTextMesh() : base()
        {
            Material = new Material { ShaderName = "ScreenText", BackCulling = false };
            PrimitiveType = PrimitiveType.Triangles;
            DepthMode = MeshDepthMode.Overlay;
        }

        /// <summary>Drop every string, ready to be filled again.</summary>
        public void Clear()
        {
            vertexData.Clear();
            quadCount = 0;
        }

        /// <summary>
        /// Publish what was added since <see cref="Clear"/> to the GPU on the next draw.
        /// Returns false (and leaves the uploaded copy alone) when nothing changed, so a caller
        /// rebuilding on every frame only pays for an upload when the text actually moved.
        /// </summary>
        public bool Commit()
        {
            float[] next = vertexData.ToArray();

            if (SameData(next, uploaded))
                return false;

            uploaded = next;
            NeedsVertexUpdate = true;
            return true;
        }

        private static bool SameData(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// A string pinned to a world point and laid out in pixels.
        /// </summary>
        /// <param name="anchor">The world point the text hangs from.</param>
        /// <param name="text">The string; characters outside printable ASCII draw as '?'.</param>
        /// <param name="color">The text colour.</param>
        /// <param name="horizontal">Which way the text grows from the anchor across the screen.</param>
        /// <param name="vertical">Which way it grows from the anchor up and down the screen.</param>
        /// <param name="offsetX">Pixels to shift right after anchoring.</param>
        /// <param name="offsetY">Pixels to shift DOWN after anchoring — client-space, as Hammer counts.</param>
        public void AddScreenText(LocationVector anchor, string text, Color color,
            TextAnchor horizontal = TextAnchor.Start, TextAnchor vertical = TextAnchor.Start,
            int offsetX = 0, int offsetY = 0)
        {
            if (string.IsNullOrEmpty(text) || Atlas == null)
                return;

            int width = Atlas.MeasureWidth(text);
            int height = Atlas.LineHeight;

            // Client space: y grows downward. The text's top-left corner relative to the anchor.
            int left = offsetX - (horizontal == TextAnchor.End ? width : horizontal == TextAnchor.Center ? width / 2 : 0);
            int top = offsetY - (vertical == TextAnchor.End ? height : vertical == TextAnchor.Center ? height / 2 : 0);

            Vector3 gl = ToGL(anchor.X, anchor.Y, anchor.Z);
            int pen = left;

            foreach (char c in text)
            {
                GlyphAtlas.Glyph glyph = Atlas.GetGlyph(c);

                if (c != ' ')
                {
                    // Offsets go to the shader with y up, the way clip space counts.
                    float x0 = pen, x1 = pen + glyph.Width;
                    float y0 = -top, y1 = -(top + glyph.Height);

                    AddQuad(gl, gl, gl, gl,
                        new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1),
                        glyph, color, screen: true);
                }

                pen += glyph.Advance;
            }
        }

        /// <summary>
        /// A string laid out on a plane in the world, one atlas pixel being
        /// <paramref name="unitsPerPixel"/> world units.
        /// </summary>
        /// <param name="origin">Where the text's anchor lies.</param>
        /// <param name="right">The world direction the text reads along (unit length).</param>
        /// <param name="up">The world direction of the text's up (unit length).</param>
        public void AddWorldText(System.Numerics.Vector3 origin, System.Numerics.Vector3 right,
            System.Numerics.Vector3 up, float unitsPerPixel, string text, Color color,
            TextAnchor horizontal = TextAnchor.Start, TextAnchor vertical = TextAnchor.End)
        {
            if (string.IsNullOrEmpty(text) || Atlas == null || unitsPerPixel <= 0f)
                return;

            int width = Atlas.MeasureWidth(text);
            int height = Atlas.LineHeight;

            float left = -(horizontal == TextAnchor.End ? width : horizontal == TextAnchor.Center ? width * 0.5f : 0f);

            // "Up" is the positive direction here, so End (grow up) puts the baseline row at the anchor.
            float bottom = vertical == TextAnchor.End ? 0f : vertical == TextAnchor.Center ? -height * 0.5f : -height;

            float pen = left;

            foreach (char c in text)
            {
                GlyphAtlas.Glyph glyph = Atlas.GetGlyph(c);

                if (c != ' ')
                {
                    System.Numerics.Vector3 x0 = right * (pen * unitsPerPixel);
                    System.Numerics.Vector3 x1 = right * ((pen + glyph.Width) * unitsPerPixel);
                    System.Numerics.Vector3 yTop = up * ((bottom + height) * unitsPerPixel);
                    System.Numerics.Vector3 yBottom = up * (bottom * unitsPerPixel);

                    AddQuad(
                        ToGL(origin + x0 + yTop), ToGL(origin + x1 + yTop),
                        ToGL(origin + x1 + yBottom), ToGL(origin + x0 + yBottom),
                        Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero,
                        glyph, color, screen: false);
                }

                pen += glyph.Advance;
            }
        }

        /// <summary>Corners in order top-left, top-right, bottom-right, bottom-left.</summary>
        private void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector2 o0, Vector2 o1, Vector2 o2, Vector2 o3,
            GlyphAtlas.Glyph glyph, Color color, bool screen)
        {
            float u0 = glyph.X / (float)Atlas.Width;
            float u1 = (glyph.X + glyph.Width) / (float)Atlas.Width;
            float v0 = glyph.Y / (float)Atlas.Height;
            float v1 = (glyph.Y + glyph.Height) / (float)Atlas.Height;

            AddVertex(p0, o0, u0, v0, color, screen);
            AddVertex(p1, o1, u1, v0, color, screen);
            AddVertex(p2, o2, u1, v1, color, screen);
            AddVertex(p3, o3, u0, v1, color, screen);

            quadCount++;
        }

        private void AddVertex(Vector3 position, Vector2 offset, float u, float v, Color color, bool screen)
        {
            vertexData.Add(position.X);
            vertexData.Add(position.Y);
            vertexData.Add(position.Z);
            vertexData.Add(offset.X);
            vertexData.Add(offset.Y);
            vertexData.Add(screen ? 1f : 0f);
            vertexData.Add(u);
            vertexData.Add(v);
            vertexData.Add(color.R / 255f);
            vertexData.Add(color.G / 255f);
            vertexData.Add(color.B / 255f);
            vertexData.Add(color.A / 255f);
        }

        /// <summary>World (x, y, z) to the library's GL axes (-y, z, -x), as every mesh stores them.</summary>
        private static Vector3 ToGL(float x, float y, float z)
        {
            return new Vector3(-y, z, -x);
        }

        private static Vector3 ToGL(System.Numerics.Vector3 v)
        {
            return ToGL(v.X, v.Y, v.Z);
        }

        public override float[] GetGLVertexArray()
        {
            return uploaded;
        }

        public override int GetIndexCount()
        {
            return uploaded.Length / Stride / 4 * 6;
        }

        public override uint[] GetIndexArray()
        {
            int quads = uploaded.Length / Stride / 4;
            uint[] indices = new uint[quads * 6];

            for (int q = 0; q < quads; q++)
            {
                uint b = (uint)(q * 4);
                indices[q * 6 + 0] = b;
                indices[q * 6 + 1] = b + 1;
                indices[q * 6 + 2] = b + 2;
                indices[q * 6 + 3] = b;
                indices[q * 6 + 4] = b + 2;
                indices[q * 6 + 5] = b + 3;
            }

            return indices;
        }

        public override void SetupMesh(object context)
        {
            vao[context] = GL.GenVertexArray();
            vbo[context] = GL.GenBuffer();
            ebo[context] = GL.GenBuffer();

            GL.BindVertexArray(vao[context]);

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, uploaded.Length * sizeof(float), uploaded, BufferUsageHint.DynamicDraw);

            uint[] indices = GetIndexArray();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.DynamicDraw);

            int stride = Stride * sizeof(float);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            MarkIndexBufferUploaded(context);
        }

        protected override void UpdateVertexBuffer(object context)
        {
            if (!vbo.ContainsKey(context))
                return;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, uploaded.Length * sizeof(float), uploaded, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

            // The quad count changes with the text, so the element buffer follows it.
            if (IsIndexUploadPending(context))
            {
                uint[] indices = GetIndexArray();

                GL.BindVertexArray(vao[context]);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.DynamicDraw);
                GL.BindVertexArray(0);

                MarkIndexBufferUploaded(context);
            }
        }

        /// <summary>The viewport query's buffer; draws happen on the GL thread only.</summary>
        private static readonly int[] viewport = new int[4];

        public override void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view,
            MeshDrawMode drawMode = MeshDrawMode.Textured)
        {
            if (Atlas == null || scene == null)
                return;

            if (!IsVertexDataLoaded(context))
            {
                SetupMesh(context);
            }
            else if (IsVertexUpdatePending(context))
            {
                UpdateVertexBuffer(context);
                MarkVertexBufferUpdated(context);
            }

            int count = GetIndexCount();

            if (count == 0)
                return;

            Shader shader = scene.GetShader(Material.ShaderName);
            shader.Use(context, scene);
            int program = shader.GetShaderId(context, scene);

            GL.GetInteger(GetPName.Viewport, viewport);

            Matrix4 model = Matrix4.Identity;
            GL.UniformMatrix4(Shader.GetUniformLocation(context, program, "uProjection"), false, ref projection);
            GL.UniformMatrix4(Shader.GetUniformLocation(context, program, "uView"), false, ref view);
            GL.UniformMatrix4(Shader.GetUniformLocation(context, program, "uModel"), false, ref model);
            GL.Uniform2(Shader.GetUniformLocation(context, program, "uViewportSize"), (float)viewport[2], (float)viewport[3]);
            GL.Uniform1(Shader.GetUniformLocation(context, program, "uMinPixelsPerUnit"), MinPixelsPerUnit);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, Atlas.GetTextureId(context));
            GL.Uniform1(Shader.GetUniformLocation(context, program, "uAtlas"), 0);

            // Blended, never culled (a world text plane is seen from both sides), and never
            // writing depth: text is a label on the scene, not part of it. Global state, so
            // whatever was in effect is put back for the next mesh.
            bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool cull = GL.IsEnabled(EnableCap.CullFace);
            bool blend = GL.IsEnabled(EnableCap.Blend);
            bool depthMask;
            GL.GetBoolean(GetPName.DepthWritemask, out depthMask);

            if (DepthMode == MeshDepthMode.Overlay)
                GL.Disable(EnableCap.DepthTest);

            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthMask(false);

            // Line-fill polygon mode would draw the glyph quads as outlines in a wireframe pane.
            int[] polygonMode = new int[2];
            GL.GetInteger(GetPName.PolygonMode, polygonMode);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            GL.BindVertexArray(vao[context]);
            GL.DrawElements(PrimitiveType.Triangles, count, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);

            GL.PolygonMode(MaterialFace.FrontAndBack, (PolygonMode)polygonMode[0]);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.DepthMask(depthMask);

            if (!blend) GL.Disable(EnableCap.Blend);
            if (cull) GL.Enable(EnableCap.CullFace);
            if (depthTest) GL.Enable(EnableCap.DepthTest);
        }
    }
}
