using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A dynamic batch of camera-facing textured quads, drawn in one call — built for
    /// particle systems, where thousands of sprites per frame make one SpriteMesh per
    /// particle unusable. The caller refills the vertex buffer each frame with
    /// <see cref="SetQuads"/>; billboard expansion happens in the ParticleSprite vertex
    /// shader (quad center + unit corner + radius + roll per vertex), so no per-particle
    /// matrix work is needed on the CPU.
    ///
    /// Vertex layout (16 floats): center xyz (GL space), corner xy (±1), radius, roll
    /// (radians), uv, uv2 (second sheet frame), frame blend, color rgba.
    /// Use with a Material whose ShaderName is "ParticleSprite".
    /// </summary>
    [ToolboxItem(false)]
    [Description("A dynamic batch of billboarded quads for particle rendering.")]
    public class ParticleBatchMesh : Mesh
    {
        public const int FloatsPerVertex = 16;
        public const int VerticesPerQuad = 4;

        float[] vertexData = new float[0];
        int quadCount;

        /// <summary>GPU quad capacity per context (buffers grow, never shrink).</summary>
        readonly Dictionary<object, int> quadCapacity = new Dictionary<object, int>();

        /// <summary>Contexts whose VBO still holds older data than <see cref="vertexData"/>.</summary>
        int dataVersion;
        readonly Dictionary<object, int> uploadedVersion = new Dictionary<object, int>();

        /// <summary>
        /// Replaces the batch content. <paramref name="vertices"/> holds
        /// <paramref name="quads"/> × 4 vertices × 16 floats (extra tail is ignored);
        /// the array is not copied, so don't mutate it until the next SetQuads.
        /// </summary>
        public void SetQuads(float[] vertices, int quads)
        {
            vertexData = vertices ?? new float[0];
            quadCount = quads;
            dataVersion++;
        }

        public int QuadCount => quadCount;

        public override void SetupMesh(object context)
        {
            vao[context] = GL.GenVertexArray();
            vbo[context] = GL.GenBuffer();
            ebo[context] = GL.GenBuffer();
            quadCapacity[context] = 0;

            GL.BindVertexArray(vao[context]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);

            int stride = FloatsPerVertex * sizeof(float);
            int offset = 0;
            int[] sizes = { 3, 2, 1, 1, 2, 2, 1, 4 };   // center, corner, radius, roll, uv, uv2, blend, color
            for (int attribute = 0; attribute < sizes.Length; attribute++)
            {
                GL.VertexAttribPointer(attribute, sizes[attribute], VertexAttribPointerType.Float,
                    false, stride, offset);
                GL.EnableVertexAttribArray(attribute);
                offset += sizes[attribute] * sizeof(float);
            }

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }

        // Particles are always their own textured sprites: a solid or wireframe view mode has no
        // meaningful reading for them, so the mode is accepted and ignored.
        public override void DrawMesh(object context, Scene scene, Matrix4 projection, Matrix4 view,
            MeshDrawMode drawMode = MeshDrawMode.Textured)
        {
            if (quadCount <= 0)
                return;

            if (!IsVertexDataLoaded(context))
            {
                SetupMesh(context);
            }

            if (Material == null)
            {
                Material = new Material { ShaderName = "ParticleSprite" };
            }

            // Binds the shader, the albedo texture and the blend/cull state.
            Material.Use(context, scene);
            int shaderProgram = Material.Shader.GetShaderId(context, scene);

            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uUseDiffuseTexture"),
                Material.Albedo != null && Material.Albedo.Bitmap != null ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uAddSelf"), Material.AddSelf);
            GL.Uniform1(GL.GetUniformLocation(shaderProgram, "uOverbrightFactor"), Material.OverbrightFactor);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjection"), false, ref projection);
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uView"), false, ref view);

            GL.BindVertexArray(vao[context]);
            UploadData(context);
            GL.DrawElements(PrimitiveType.Triangles, quadCount * 6, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        void UploadData(object context)
        {
            uploadedVersion.TryGetValue(context, out int version);
            if (version == dataVersion)
                return;
            uploadedVersion[context] = dataVersion;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);

            int capacity = quadCapacity[context];
            if (quadCount > capacity)
            {
                // Grow in steps so steady emission doesn't reallocate every frame.
                capacity = System.Math.Max(quadCount, System.Math.Max(256, capacity * 2));
                quadCapacity[context] = capacity;

                GL.BufferData(BufferTarget.ArrayBuffer,
                    capacity * VerticesPerQuad * FloatsPerVertex * sizeof(float),
                    System.IntPtr.Zero, BufferUsageHint.DynamicDraw);

                uint[] indices = new uint[capacity * 6];
                for (int quad = 0; quad < capacity; quad++)
                {
                    uint baseVertex = (uint)(quad * 4);
                    int i = quad * 6;
                    indices[i] = baseVertex;
                    indices[i + 1] = baseVertex + 1;
                    indices[i + 2] = baseVertex + 2;
                    indices[i + 3] = baseVertex;
                    indices[i + 4] = baseVertex + 2;
                    indices[i + 5] = baseVertex + 3;
                }
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo[context]);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint),
                    indices, BufferUsageHint.StaticDraw);
            }

            GL.BufferSubData(BufferTarget.ArrayBuffer, System.IntPtr.Zero,
                quadCount * VerticesPerQuad * FloatsPerVertex * sizeof(float), vertexData);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
    }
}
