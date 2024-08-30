using CSharp3D.Forms.Engine;
using OpenTK;
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
        /// The vertices of the polygon.
        /// </summary>
        [Browsable(false)]
        public Vector3[] Vertices { get; set; } = { };

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

        public QuakeFaceMesh(Vector3 position, Vector3 rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array of the polygon.
        /// </summary>
        /// <returns> The vertex array of the polygon. </returns>
        public override float[] GetVertexArray()
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
