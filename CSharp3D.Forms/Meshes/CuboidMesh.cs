using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Engine.Helpers;
using OpenTK;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// Cuboid mesh
    /// </summary>
    [Description("A cuboid mesh")]
    [ToolboxItem(true)]
    public class CuboidMesh : Mesh
    {
        /// <summary>
        /// The cuboid size on the X axis, in World units.
        /// </summary>
        [Category("Cuboid")]
        [Description("The cuboid size on the X axis, in World units.")]
        public float ScaleX { get; set; } = 2;

        /// <summary>
        /// The cuboid size on the Y axis, in World units.
        /// </summary>
        [Category("Cuboid")]
        [Description("The cuboid size on the Y axis, in World units.")]
        public float ScaleY { get; set; } = 2;

        /// <summary>
        /// The cuboid size on the Z axis, in World units.
        /// </summary>
        [Category("Cuboid")]
        [Description("The cuboid size on the Z axis, in World units.")]
        public float ScaleZ { get; set; } = 2;

        private Vertex[] vertices = {
            // Back face (+x)
            new Vertex(new Vector3( 0.5f, -0.5f,  0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f,  0.5f,  0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f,  0.5f, -0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3( 0.5f, -0.5f, -0.5f), new Vector2(0.0f, 1.0f)),

            // Front face (-x)
            new Vertex(new Vector3(-0.5f,  0.5f,  0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f, -0.5f,  0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3(-0.5f,  0.5f, -0.5f), new Vector2(0.0f, 1.0f)),

            // Left face (+y)
            new Vertex(new Vector3( 0.5f,  0.5f,  0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f,  0.5f,  0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f,  0.5f, -0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3( 0.5f,  0.5f, -0.5f), new Vector2(0.0f, 1.0f)),

            // Right face (-y)
            new Vertex(new Vector3(-0.5f, -0.5f,  0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f, -0.5f,  0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f, -0.5f, -0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(0.0f, 1.0f)),

            // Bottom face (-z)
            new Vertex(new Vector3( 0.5f,  0.5f, -0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f,  0.5f, -0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3( 0.5f, -0.5f, -0.5f), new Vector2(0.0f, 1.0f)),

            // Top face (+z)
            new Vertex(new Vector3(-0.5f,  0.5f,  0.5f), new Vector2(0.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f,  0.5f,  0.5f), new Vector2(1.0f, 0.0f)),
            new Vertex(new Vector3( 0.5f, -0.5f,  0.5f), new Vector2(1.0f, 1.0f)),
            new Vertex(new Vector3(-0.5f, -0.5f,  0.5f), new Vector2(0.0f, 1.0f))
        };

        public CuboidMesh() : base()
        {

        }

        public CuboidMesh(Vector3 position, Vector3 rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array for the cuboid mesh
        /// </summary>
        /// <returns> The vertex array </returns>
        public override float[] GetVertexArray()
        {
            // Positions (gl_x,gl_y,gl_z) , Normals (nx, ny, nz), Texture Coords (u, v)
            float[] vertexArray = new float[vertices.Length * 8];

            for (int i = 0; i < vertices.Length; i++)
            {
                // Position
                Vector3 location = VectorOrientation.ToGL(new Vector3(vertices[i].X * ScaleX, vertices[i].Y * ScaleY, vertices[i].Z * ScaleZ));
                vertexArray[i * 8] = location.X;
                vertexArray[i * 8 + 1] = location.Y;
                vertexArray[i * 8 + 2] = location.Z;

                // UV
                vertexArray[i * 8 + 6] = vertices[i].U;
                vertexArray[i * 8 + 7] = vertices[i].V;
            }

            vertexArray = GenerateFaceNormals(vertexArray);

            return vertexArray;
        }

        /// <summary>
        /// Get the index array for the cuboid mesh
        /// </summary>
        /// <returns> The index array </returns>
        public override uint[] GetIndexArray()
        {
            // Cuboid indices
            uint[] indices =
            {
                2,1,0,3,2,0,
                6,5,4,7,6,4,
                10,9,8,11,10,8,
                14,13,12,15,14,12,
                18,17,16,19,18,16,
                22,21,20,23,22,20
            };

            return indices;
        }
    }
}
