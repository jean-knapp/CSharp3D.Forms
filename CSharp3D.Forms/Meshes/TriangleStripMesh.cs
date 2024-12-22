using OpenTK;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A mesh that represents a triangle strip.
    /// </summary>
    [ToolboxItem(false)]
    public class TriangleStripMesh : Mesh
    {
        public Vector3[] Position { get; set; } = { };
        public Vector3[] Normal { get; set; } = { };
        public Vector2[] UV { get; set; } = { };

        public TriangleStripMesh() : base()
        {

        }

        public TriangleStripMesh(Vector3 position, Vector3 rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array of the triangle strip.
        /// </summary>
        /// <returns> The vertex array of the triangle strip. </returns>
        public override float[] GetGLVertexArray()
        {
            // Cuboid vertex data with positions and texture coordinates

            // Positions (-y, z, -x) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[Position.Length * 8];

            // Vertex data should be stored as -y, z, -x, u, v
            for (int i = 0; i < Position.Length; i++)
            {
                result[i * 8] = Position[i].X;
                result[i * 8 + 1] = Position[i].Z;
                result[i * 8 + 2] = Position[i].Y;
                result[i * 8 + 3] = Normal[i].X;
                result[i * 8 + 4] = Normal[i].Y;
                result[i * 8 + 5] = Normal[i].Z;
                result[i * 8 + 6] = UV[i].X;
                result[i * 8 + 7] = UV[i].Y;
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
        /// Get the index array of the triangle strip.
        /// </summary>
        /// <returns> The index array of the triangle strip. </returns>
        public override uint[] GetIndexArray()
        {
            // Cuboid indices
            uint[] indices = new uint[Position.Length];
            for (uint i = 0; i < Position.Length; i++)
            {
                indices[i] = i;
   
            }

            return indices;
        }
    }
}
