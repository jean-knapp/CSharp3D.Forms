using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// An <see cref="IndexedMesh"/> that can show baked lighting, in one of two ways:
    ///
    /// <list type="bullet">
    /// <item><b>A lightmap over a non-planar surface</b> (a Source displacement). The
    /// shader cannot derive lightmap UVs from the fragment's world position the way it does
    /// for a flat <see cref="QuakeFaceMesh"/> — the surface is sculpted away from the plane
    /// the lightmap is laid on. So each vertex also carries the point of that plane it came
    /// from (<see cref="LightmapPositions"/>), and the shader maps that instead. This is
    /// Source's own rule: a displacement's lightmap coordinates are the base face's,
    /// interpolated over the grid.</item>
    /// <item><b>An ambient cube</b> (a model). Six colours, one per world axis direction,
    /// blended by the surface normal — how Source lights props at run time.</item>
    /// </list>
    ///
    /// Both only apply in <see cref="MeshDrawMode.Textured"/>; without either the mesh draws
    /// exactly like an <see cref="IndexedMesh"/>.
    /// </summary>
    [ToolboxItem(false)]
    public class LitIndexedMesh : IndexedMesh
    {
        public LitIndexedMesh() : base()
        {
        }

        public LitIndexedMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {
        }

        // ---- lightmap over a displaced surface ----

        /// <summary>
        /// Per vertex, the World-space point on the lightmap's plane the vertex belongs to
        /// (a displacement's undisplaced base-face position). Parallel to Positions. Null
        /// for a mesh without a lightmap.
        /// </summary>
        [Browsable(false)]
        public Vector3[] LightmapPositions { get; set; }

        [Browsable(false)]
        public ILightmapTexture Lightmap { get; set; }

        /// <summary>The lightmap's slot in <see cref="Lightmap"/> (see <see cref="QuakeFaceMesh.LightmapRect"/>).</summary>
        [Browsable(false)]
        public LightmapRect LightmapRect { get; set; }

        /// <summary>Same mapping as <see cref="QuakeFaceMesh.LightmapAxisU"/>, applied to <see cref="LightmapPositions"/>.</summary>
        [Browsable(false)]
        public Vector3 LightmapAxisU { get; set; }

        [Browsable(false)]
        public Vector3 LightmapAxisV { get; set; }

        [Browsable(false)]
        public float LightmapOffsetU { get; set; }

        [Browsable(false)]
        public float LightmapOffsetV { get; set; }

        [Browsable(false)]
        public float LightmapWidth { get; set; }

        [Browsable(false)]
        public float LightmapHeight { get; set; }

        // ---- ambient cube ----

        /// <summary>
        /// Light arriving from each World axis direction — +X, −X, +Y, −Y, +Z, −Z — as the
        /// albedo multiplier to apply (1 = as authored). Null for none.
        /// </summary>
        [Browsable(false)]
        public Vector3[] AmbientCube { get; set; }

        private bool HasLightmapPositions => LightmapPositions != null && LightmapPositions.Length == Positions.Length;

        public override void SetupMesh(object context)
        {
            base.SetupMesh(context);

            if (HasLightmapPositions)
                UploadWithLightmapPositions(context);
        }

        protected override void UpdateVertexBuffer(object context)
        {
            base.UpdateVertexBuffer(context);

            if (HasLightmapPositions)
                UploadWithLightmapPositions(context);
        }

        /// <summary>
        /// Re-upload the vertex buffer as the 8-float block, then the blend weights if any
        /// (attribute 3, as <see cref="IndexedMesh"/> lays them out), then three floats of
        /// GL-space lightmap position per vertex (attribute 4). Attributes 0-2 keep the
        /// offsets the base class gave them.
        /// </summary>
        private void UploadWithLightmapPositions(object context)
        {
            if (!vbo.ContainsKey(context) || !vao.ContainsKey(context))
                return;

            float[] block = GetGLVertexArray();
            float[] blend = HasBlendWeights ? BlendWeights : null;
            int blendLength = blend != null ? blend.Length : 0;
            int count = LightmapPositions.Length;

            float[] data = new float[block.Length + blendLength + count * 3];
            System.Array.Copy(block, data, block.Length);

            if (blend != null)
                System.Array.Copy(blend, 0, data, block.Length, blendLength);

            int at = block.Length + blendLength;

            for (int i = 0; i < count; i++)
            {
                // World → GL: (-Y, Z, -X), as IndexedMesh converts positions.
                Vector3 p = LightmapPositions[i];
                data[at + i * 3 + 0] = -p.Y;
                data[at + i * 3 + 1] = p.Z;
                data[at + i * 3 + 2] = -p.X;
            }

            GL.BindVertexArray(vao[context]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);

            if (blend != null)
            {
                GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, sizeof(float), block.Length * sizeof(float));
                GL.EnableVertexAttribArray(3);
            }

            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), at * sizeof(float));
            GL.EnableVertexAttribArray(4);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        }

        protected override void ApplyCustomUniforms(object context, Scene scene, int shaderProgram, MeshDrawMode drawMode)
        {
            base.ApplyCustomUniforms(context, scene, shaderProgram, drawMode);

            if (drawMode != MeshDrawMode.Textured)
                return;

            if (HasLightmapPositions
                && QuakeFaceMesh.BindLightmap(context, shaderProgram, Lightmap, LightmapAxisU, LightmapAxisV,
                                              LightmapOffsetU, LightmapOffsetV, LightmapRect, LightmapWidth, LightmapHeight,
                                              fromVertex: true))
            {
                return;
            }

            Vector3[] cube = AmbientCube;

            if (cube == null || cube.Length != 6)
                return;

            float[] values = new float[18];
            for (int i = 0; i < 6; i++)
            {
                values[i * 3 + 0] = cube[i].X;
                values[i * 3 + 1] = cube[i].Y;
                values[i * 3 + 2] = cube[i].Z;
            }

            Shader.UniformSet u = Shader.GetUniforms(context, shaderProgram);
            GL.Uniform3(u.AmbientCube, 6, values);
            GL.Uniform1(u.UseAmbientCube, 1);
        }
    }

    /// <summary>
    /// Brush faces drawn as one batch with their baked lighting (the lighting preview). Each
    /// vertex carries where its face's lightmap is: the luxel coordinates in the
    /// <see cref="LightmapAtlas"/> (<see cref="LightmapCoords"/>) and the face's slot
    /// (<see cref="LightmapSlots"/>), which every lookup is clamped to. A face with an empty
    /// slot has no bake yet and draws as its plain texture.
    ///
    /// The lighting changes without the mesh: a bake landing only rewrites the atlas. When a
    /// face's slot or mapping moves, the owner updates the two arrays and sets
    /// <see cref="Mesh.NeedsVertexUpdate"/>.
    /// </summary>
    [ToolboxItem(false)]
    public class AtlasLitMesh : IndexedMesh
    {
        [Browsable(false)]
        public LightmapAtlas Lightmap { get; set; }

        /// <summary>Per vertex, luxel coordinates in the atlas (texel centres at +0.5). Parallel to Positions.</summary>
        [Browsable(false)]
        public Vector2[] LightmapCoords { get; set; }

        /// <summary>Per vertex, its face's slot as (x, y, width, height) texels; width 0 for none. Parallel to Positions.</summary>
        [Browsable(false)]
        public Vector4[] LightmapSlots { get; set; }

        private bool HasLightmapData => LightmapCoords != null && LightmapSlots != null
            && LightmapCoords.Length == Positions.Length && LightmapSlots.Length == Positions.Length;

        public override void SetupMesh(object context)
        {
            base.SetupMesh(context);

            if (HasLightmapData)
                UploadWithLightmap(context);
        }

        protected override void UpdateVertexBuffer(object context)
        {
            base.UpdateVertexBuffer(context);

            if (HasLightmapData)
                UploadWithLightmap(context);
        }

        /// <summary>
        /// The 8-float block, then two floats of luxel coordinates (attribute 5) and four of
        /// slot (attribute 6) per vertex.
        /// </summary>
        private void UploadWithLightmap(object context)
        {
            if (!vbo.ContainsKey(context) || !vao.ContainsKey(context))
                return;

            float[] block = GetGLVertexArray();
            int count = Positions.Length;
            float[] data = new float[block.Length + count * 6];
            System.Array.Copy(block, data, block.Length);

            int coords = block.Length;
            int slots = coords + count * 2;

            for (int i = 0; i < count; i++)
            {
                data[coords + i * 2 + 0] = LightmapCoords[i].X;
                data[coords + i * 2 + 1] = LightmapCoords[i].Y;

                Vector4 slot = LightmapSlots[i];
                data[slots + i * 4 + 0] = slot.X;
                data[slots + i * 4 + 1] = slot.Y;
                data[slots + i * 4 + 2] = slot.Z;
                data[slots + i * 4 + 3] = slot.W;
            }

            GL.BindVertexArray(vao[context]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(5, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), coords * sizeof(float));
            GL.EnableVertexAttribArray(5);
            GL.VertexAttribPointer(6, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), slots * sizeof(float));
            GL.EnableVertexAttribArray(6);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        }

        protected override void ApplyCustomUniforms(object context, Scene scene, int shaderProgram, MeshDrawMode drawMode)
        {
            base.ApplyCustomUniforms(context, scene, shaderProgram, drawMode);

            if (drawMode != MeshDrawMode.Textured || Lightmap == null || !HasLightmapData)
                return;

            Shader.UniformSet u = Shader.GetUniforms(context, shaderProgram);
            int textureId = Lightmap.GetTextureId(context);
            int width = Lightmap.Width, height = Lightmap.Height;

            if (textureId == 0 || width <= 0 || height <= 0)
            {
                // Nothing baked anywhere yet: the plain texture, never the unlit dark.
                GL.Uniform1(u.FullBright, 1);
                return;
            }

            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.Uniform1(u.LightmapTexture, 3);
            GL.ActiveTexture(TextureUnit.Texture0);

            GL.Uniform2(u.LightmapInvSize, 1.0f / width, 1.0f / height);
            GL.Uniform1(u.LightmapAtlas, 1);
            GL.Uniform1(u.LightmapFromVertex, 0);
            GL.Uniform1(u.LightmapBicubic, QuakeFaceMesh.LightmapBicubic ? 1 : 0);
            GL.Uniform1(u.UseLightmap, 1);
        }
    }
}