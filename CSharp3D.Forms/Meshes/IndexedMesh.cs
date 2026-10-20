using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A general-purpose indexed triangle mesh with explicit per-vertex normals.
    ///
    /// Unlike <see cref="TriangleStripMesh"/>, this mesh:
    ///  - uses a real index buffer (no expanded triangle soup),
    ///  - keeps the caller's per-vertex normals instead of recomputing flat face normals,
    ///  - converts World space to OpenGL space with the same (-Y, Z, -X) transform the engine
    ///    uses for object positions (<see cref="VectorOrientation.ToGL(LocationVector)"/>).
    ///    That transform has a positive determinant, so triangle winding is preserved and
    ///    back-face culling behaves correctly.
    ///  - caches its GL arrays, so DrawMesh does not rebuild them every frame.
    ///
    /// Positions and normals supplied to this mesh must be in World space (X forward, Y left,
    /// Z up), which is the same convention Source uses.
    /// </summary>
    [ToolboxItem(false)]
    public class IndexedMesh : Mesh
    {
        /// <summary> Vertex positions, in World space. </summary>
        public Vector3[] Positions { get; set; } = { };

        /// <summary> Vertex normals, in World space. Must be parallel to Positions. </summary>
        public Vector3[] Normals { get; set; } = { };

        /// <summary> Texture coordinates. Must be parallel to Positions. </summary>
        public Vector2[] TextureCoords { get; set; } = { };

        /// <summary> Triangle indices into the vertex arrays. </summary>
        public uint[] Indices { get; set; } = { };

        /// <summary>
        /// Optional per-vertex blend weights, 0..1, parallel to Positions: how much of
        /// <see cref="Material.Albedo2"/> shows over <see cref="Material.Albedo"/> (a Source
        /// displacement's painted alpha on a WorldVertexTransition material). Null for none.
        ///
        /// They ride in the same vertex buffer, after the standard 8-float block rather than
        /// inside it, so <see cref="GetGLVertexArray"/> keeps the layout every other reader of
        /// it (bounds, ray tracing) assumes.
        /// </summary>
        public float[] BlendWeights { get; set; }

        protected override bool HasBlendWeights => BlendWeights != null && BlendWeights.Length == Positions.Length;

        public override void SetupMesh(object context)
        {
            base.SetupMesh(context);

            if (HasBlendWeights)
                UploadWithBlendWeights(context);
        }

        protected override void UpdateVertexBuffer(object context)
        {
            base.UpdateVertexBuffer(context);

            if (HasBlendWeights)
                UploadWithBlendWeights(context);
        }

        /// <summary>
        /// Re-upload the vertex buffer as the 8-float block followed by one weight per vertex,
        /// and point attribute 3 at the weights. The VAO keeps attributes 0-2 as the base class
        /// set them; their offsets into the block do not move.
        /// </summary>
        private void UploadWithBlendWeights(object context)
        {
            if (!vbo.ContainsKey(context) || !vao.ContainsKey(context))
                return;

            float[] block = GetGLVertexArray();
            float[] data = new float[block.Length + BlendWeights.Length];
            System.Array.Copy(block, data, block.Length);
            System.Array.Copy(BlendWeights, 0, data, block.Length, BlendWeights.Length);

            GL.BindVertexArray(vao[context]);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo[context]);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, sizeof(float), block.Length * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
        }

        private float[] _glVertexCache;
        private uint[] _indexCache;

        public IndexedMesh() : base()
        {
        }

        public IndexedMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {
        }

        /// <summary>
        /// Drops the cached GL arrays. Call after changing Positions/Normals/TextureCoords/Indices.
        /// </summary>
        public void InvalidateCache()
        {
            _glVertexCache = null;
            _indexCache = null;
        }

        /// <summary>
        /// Converts a World-space vector to OpenGL space: (-Y, Z, -X).
        /// Matches VectorOrientation.ToGL so geometry and object positions agree.
        /// </summary>
        private static void WorldToGL(Vector3 v, float[] target, int offset)
        {
            target[offset + 0] = -v.Y;
            target[offset + 1] = v.Z;
            target[offset + 2] = -v.X;
        }

        public override float[] GetGLVertexArray()
        {
            if (_glVertexCache != null)
                return _glVertexCache;

            int count = Positions.Length;
            float[] result = new float[count * 8];

            bool hasNormals = Normals != null && Normals.Length == count;
            bool hasUvs = TextureCoords != null && TextureCoords.Length == count;

            for (int i = 0; i < count; i++)
            {
                int o = i * 8;

                WorldToGL(Positions[i], result, o);

                if (hasNormals)
                    WorldToGL(Normals[i], result, o + 3);

                if (hasUvs)
                {
                    result[o + 6] = TextureCoords[i].X;
                    result[o + 7] = TextureCoords[i].Y;
                }
            }

            // Only fall back to generated face normals when the caller supplied none.
            if (!hasNormals)
                result = GenerateFaceNormals(result);

            _glVertexCache = result;
            return result;
        }

        public override uint[] GetIndexArray()
        {
            if (_indexCache != null)
                return _indexCache;

            _indexCache = Indices ?? new uint[0];
            return _indexCache;
        }
    }
}
