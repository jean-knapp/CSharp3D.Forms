using CSharp3D.Forms.Engine;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A mesh that is similar to the polygon face, but instead of the texture coordinates being calculated from the vertices, they are calculated from the world.
    /// </summary>
    [ToolboxItem(false)]
    public class QuakeFaceMesh : Mesh
    {
        /// <summary>
        /// Optional baked lightmap. When set (and the lightmap has data), the fragment
        /// shader replaces dynamic lighting with albedo × lightmap for this face.
        /// The texture object is shared across scene rebuilds — the owner keys it by
        /// face, not by mesh.
        /// </summary>
        [Browsable(false)]
        public ILightmapTexture Lightmap { get; set; }

        /// <summary>
        /// Where this face's lightmap sits in <see cref="Lightmap"/>, in texels (a slot of a
        /// <see cref="LightmapAtlas"/>). Empty means the whole texture, at
        /// <see cref="LightmapWidth"/> x <see cref="LightmapHeight"/>.
        /// </summary>
        [Browsable(false)]
        public LightmapRect LightmapRect { get; set; }

        /// <summary>
        /// Lightmap UV mapping, world space: u_luxel = dot(P, LightmapAxisU) + LightmapOffsetU
        /// (axis pre-divided by the luxel size, offset = -MinS + 0.5 so texel centers land
        /// on vrad's integer lattice). Same for V. The shader converts to GL orientation.
        /// </summary>
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

        /// <summary>
        /// Bind the baked lightmap for the draw, if there is one. fragPosition in the
        /// shader is GL-oriented world space; a world-space dot product survives the
        /// axis permutation (it's orthonormal), so the axes just get converted.
        /// </summary>
        protected override void ApplyCustomUniforms(object context, Scene scene, int shaderProgram, MeshDrawMode drawMode)
        {
            if (drawMode != MeshDrawMode.Textured)
                return;

            BindLightmap(context, shaderProgram, Lightmap, LightmapAxisU, LightmapAxisV,
                         LightmapOffsetU, LightmapOffsetV, LightmapRect, LightmapWidth, LightmapHeight, fromVertex: false);
        }

        /// <summary>
        /// Smooth baked lightmaps with a bicubic B-spline instead of bilinear filtering — the
        /// editor-side counterpart of Source's <c>r_lightmap_bicubic</c> (Hammer++'s
        /// "LightmapBicubic" option). Read at draw time by every lightmapped mesh.
        /// </summary>
        public static bool LightmapBicubic { get; set; } = true;

        /// <summary>
        /// Bind one baked lightmap for the current draw: texture unit 3, the world→luxel
        /// mapping and the size. Shared by brush faces and displacements, which differ only
        /// in which position the shader maps: the fragment's own (a planar face), or the
        /// undisplaced lattice position carried per vertex (<paramref name="fromVertex"/>).
        /// Returns false — leaving the lightmap off — when there is nothing to bind.
        /// </summary>
        internal static bool BindLightmap(object context, int shaderProgram, ILightmapTexture lightmap,
                                          Vector3 axisU, Vector3 axisV, float offsetU, float offsetV,
                                          LightmapRect rect, float width, float height, bool fromVertex)
        {
            if (lightmap == null)
                return false;

            // No slot: the lightmap is the whole texture.
            if (rect.IsEmpty)
                rect = new LightmapRect { Width = (int)width, Height = (int)height };

            if (rect.IsEmpty)
                return false;

            int textureId = lightmap.GetTextureId(context);
            if (textureId == 0)
                return false;

            int textureWidth = lightmap.Width, textureHeight = lightmap.Height;
            if (textureWidth <= 0 || textureHeight <= 0)
                return false;

            Shader.UniformSet u = Shader.GetUniforms(context, shaderProgram);

            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.Uniform1(u.LightmapTexture, 3);
            GL.ActiveTexture(TextureUnit.Texture0);

            // world (x,y,z) → GL (-y, z, -x); dot products are preserved under this
            // permutation, so converting the axis vector is all that's needed.
            // A slot laid on its side holds luxel (s, t) at texel (t, s): map U onto the
            // texture's rows and V across them.
            if (rect.Rotated)
            {
                Vector3 axis = axisU;
                axisU = axisV;
                axisV = axis;

                float offset = offsetU;
                offsetU = offsetV;
                offsetV = offset;
            }

            GL.Uniform4(u.LightmapMapU, -axisU.Y, axisU.Z, -axisU.X, offsetU);
            GL.Uniform4(u.LightmapMapV, -axisV.Y, axisV.Z, -axisV.X, offsetV);
            GL.Uniform2(u.LightmapInvSize, 1.0f / textureWidth, 1.0f / textureHeight);
            GL.Uniform4(u.LightmapRect, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            GL.Uniform1(u.LightmapAtlas, 0);

            GL.Uniform1(u.LightmapFromVertex, fromVertex ? 1 : 0);
            GL.Uniform1(u.LightmapBicubic, LightmapBicubic ? 1 : 0);
            GL.Uniform1(u.UseLightmap, 1);
            return true;
        }

        /// <summary>
        /// The vertices of the polygon.
        /// </summary>
        [Browsable(false)]
        public Vertex[] Vertices { get; set; } = { };

        /// <summary>
        /// The U map of the material
        /// </summary>
        [Browsable(false)]
        public (float, float, float, float) UMap { get; set; } = (0, 0, 0, 0);

        /// <summary>
        /// The V map of the material
        /// </summary>
        [Browsable(false)]
        public (float, float, float, float) VMap { get; set; } = (0, 0, 0, 0);

        /// <summary>
        /// The scale of the material in the U direction
        /// </summary>
        [Description("The scale of the material in the U direction")]
        public float UScale = 0.25f;

        /// <summary>
        /// The scale of the material in the V direction
        /// </summary>
        [Description("The scale of the material in the V direction")]
        public float VScale = 0.25f;

        public QuakeFaceMesh() : base()
        {

        }

        public QuakeFaceMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array of the polygon.
        /// </summary>
        /// <returns> The vertex array of the polygon. </returns>
        public override float[] GetGLVertexArray()
        {
            // Cuboid vertex data with positions and texture coordinates

            // Positions (-y, z, -x) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[Vertices.Length * 8];

            int width = (int)(Material.Albedo.Bitmap.Width * UScale);
            int height = (int)(Material.Albedo.Bitmap.Height * VScale);

            // Vertex data should be stored as -y, z, -x, u, v
            for (int i = 0; i < Vertices.Length; i++)
            {
                float u = (UMap.Item1 * Vertices[i].X + UMap.Item2 * Vertices[i].Y + UMap.Item3 * Vertices[i].Z + UMap.Item4 * UScale) / width;
                float v = (VMap.Item1 * Vertices[i].X + VMap.Item2 * Vertices[i].Y + VMap.Item3 * Vertices[i].Z + VMap.Item4 * VScale) / height;

                result[i * 8] = -Vertices[i].Y;
                result[i * 8 + 1] = Vertices[i].Z;
                result[i * 8 + 2] = -Vertices[i].X;
                result[i * 8 + 3] = 0;
                result[i * 8 + 4] = 0;
                result[i * 8 + 5] = 0;
                result[i * 8 + 6] = u;
                result[i * 8 + 7] = v;
            }

            bool normalize = true;

            // Calculate normals
            var indices = GetIndexArray();
            for (int i = 0; i < indices.Length / 3; i++)
            {
                Vector3 v1 = new Vector3();
                Vector3 v2 = new Vector3();

                int x = (int)indices[3 * i];
                int y = (int)indices[3 * i + 1];
                int z = (int)indices[3 * i + 2];

                v1.X = result[x * 8 + 0] - result[y * 8 + 0];
                v1.Y = result[x * 8 + 1] - result[y * 8 + 1];
                v1.Z = result[x * 8 + 2] - result[y * 8 + 2];

                v2.X = result[y * 8 + 0] - result[z * 8 + 0];
                v2.Y = result[y * 8 + 1] - result[z * 8 + 1];
                v2.Z = result[y * 8 + 2] - result[z * 8 + 2];

                Vector3 normal = new Vector3();
                normal.X = v1.Y * v2.Z - v1.Z * v2.Y;
                normal.Y = v1.Z * v2.X - v1.X * v2.Z;
                normal.Z = v1.X * v2.Y - v1.Y * v2.X;

                if (normalize)
                {
                    normal = normal.Normalized();
                }

                result[x * 8 + 3] = normal.X;
                result[x * 8 + 4] = normal.Y;
                result[x * 8 + 5] = normal.Z;
                result[y * 8 + 3] = normal.X;
                result[y * 8 + 4] = normal.Y;
                result[y * 8 + 5] = normal.Z;
                result[z * 8 + 3] = normal.X;
                result[z * 8 + 4] = normal.Y;
                result[z * 8 + 5] = normal.Z;
            }

            return result;
        }

        /// <summary>
        /// Get the index array of the polygon.
        /// </summary>
        /// <returns> The index array of the polygon. </returns>
        /// <summary>Triangle fan over the winding — the count needs no array.</summary>
        public override int GetIndexCount()
        {
            return Vertices != null && Vertices.Length >= 3 ? 3 * (Vertices.Length - 2) : 0;
        }

        public override uint[] GetIndexArray()
        {
            // Cuboid indices
            uint[] indices = new uint[3 * (Vertices.Length - 2)];
            for (uint i = 0; i < Vertices.Length - 2; i++)
            {
                // I want 0,1,2,  0,2,3,   0,3,4,    0,4,5, ....
                indices[3 * i] = 0;
                indices[3 * i + 1] = i + 1;
                indices[3 * i + 2] = i + 2;
            }

            return indices;
        }
    }
}
