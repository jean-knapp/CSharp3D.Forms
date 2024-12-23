using CSharp3D.Forms.Cameras;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Utils;
using OpenTK;
using System.ComponentModel;
using System.Windows.Forms;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A sprite mesh
    /// </summary>
    [ToolboxItem(true)]
    [Description("A sprite mesh")]
    public class SpriteMesh : Mesh
    {
        [Category("Position")]
        /// <summary>
        /// The width of the sprite, in World units.
        /// </summary>
        public float Width { get; set; } = 1;

        [Category("Position")]
        /// <summary>
        /// The height of the sprite, in World units.
        /// </summary>

        public float Height { get; set; } = 1;

        /// <summary>
        /// If true, only rotate around the Z axis
        /// </summary>
        [Category("Position")]
        [Description("If true, only rotate around the Z axis")]
        public bool ZAxisRotationOnly { get; set; } = false;

        /// <summary>
        /// Left, Top, Right, Bottom UV map.
        /// </summary>
        [Category("Material")]
        [Description("Left, Top, Right, Bottom UV map.")]
        [TypeConverter(typeof(UVTypeConverter))]
        public UV UV { get; set; } = new UV(0, 0, 1, 1);

        public SpriteMesh() : base()
        {

        }

        public SpriteMesh(Vector3 position, Vector3 rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array for the sprite
        /// </summary>
        /// <returns> The vertex array </returns>
        public override float[] GetGLVertexArray()
        {
            // Face east (x axis
            Vertex[] vertices = new Vertex[]
            {
                new Vertex(new Vector3(0, -Width/2f, -Height/2f), new Vector2(UV.Left, UV.Bottom)),
                new Vertex(new Vector3(0, Width/2f, -Height/2f), new Vector2(UV.Right, UV.Bottom)),
                new Vertex(new Vector3(0, Width/2f, Height/2f), new Vector2(UV.Right, UV.Top)),
                new Vertex(new Vector3(0, -Width/2f, Height/2f), new Vector2(UV.Left, UV.Top))

            };
            // Cuboid vertex data with positions and texture coordinates

            // Positions (-y, z, -x) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[vertices.Length * 8];

            // Vertex data should be stored as -y, z, -x, u, v
            for (int i = 0; i < vertices.Length; i++)
            {
                result[i * 8] = -vertices[i].Y;
                result[i * 8 + 1] = vertices[i].Z;
                result[i * 8 + 2] = -vertices[i].X;
                result[i * 8 + 3] = 0;
                result[i * 8 + 4] = 0;
                result[i * 8 + 5] = 0;
                result[i * 8 + 6] = vertices[i].U;
                result[i * 8 + 7] = vertices[i].V;
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
        /// Get the index array for the sprite
        /// </summary>
        /// <returns></returns>
        public override uint[] GetIndexArray()
        {
            // Rectangle indices
            uint[] indices = new uint[] { 0, 1, 2, 0, 2, 3 };

            return indices;
        }

        /// <summary>
        /// Get the model matrix for the sprite
        /// </summary>
        /// <param name="viewMatrix"> The view matrix </param>
        /// <returns> The model matrix </returns>
        public override Matrix4 GetModelMatrix(Matrix4 viewMatrix)
        {
            var rotation = Camera.GetRotation(viewMatrix);

            Rotation = new Vector3(Rotation.X, (ZAxisRotationOnly ? Rotation.Y : -rotation.Y), rotation.Z + 180);

            return base.GetModelMatrix(viewMatrix);
        }
    }
}
