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
        public Vector3[] Vertices { get; set; } = { };

        public LineMesh() : base()
        {
            Material = new Engine.Material();
            Material.ShaderName = "Wireframe";
            PrimitiveType = PrimitiveType.Lines;
        }

        public LineMesh(Vector3 position, Vector3 rotation) : base(position, rotation)
        {
            Material = new Engine.Material();
            Material.ShaderName = "Wireframe";
            PrimitiveType = PrimitiveType.Lines;
        }

        /// <summary>
        /// Get the vertex array of the mesh.
        /// </summary>
        /// <returns> The vertex array of the mesh. </returns>
        public override float[] GetVertexArray()
        {
            // Cuboid vertex data with positions and texture coordinates

            // Positions (-y, z, -x) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] result = new float[Vertices.Length * 8];

            // Vertex data should be stored as -y, z, -x, u, v
            for (int i = 0; i < Vertices.Length; i++)
            {
                result[i * 8] = Vertices[i].X;
                result[i * 8 + 1] = Vertices[i].Z;
                result[i * 8 + 2] = Vertices[i].Y;
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
