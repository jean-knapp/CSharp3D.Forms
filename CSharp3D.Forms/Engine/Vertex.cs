using OpenTK;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Represents the position and UV of a vertex in 3D space.
    /// </summary>
    public class Vertex
    {
        /// <summary>
        /// The X position of the vertex, in World units.
        /// </summary>
        public float X = 0;

        /// <summary>
        /// The Y position of the vertex, in World units.
        /// </summary>
        public float Y = 0;

        /// <summary>
        /// The Z position of the vertex, in World units.
        /// </summary>
        public float Z = 0;

        /// <summary>
        /// The U texture coordinate of the vertex.
        /// </summary>
        public float U = 0;

        /// <summary>
        /// The V texture coordinate of the vertex.
        /// </summary>
        public float V = 0;

        /// <summary>
        /// The position of the vertex, in World units.
        /// </summary>
        public Vector3 Position => new Vector3(X, Y, Z);

        /// <summary>
        /// The texture coordinates of the vertex.
        /// </summary>
        public Vector2 TextureCoords => new Vector2(U, V);

        public Vertex(float x, float y, float z, float u, float v)
        {
            X = x;
            Y = y;
            Z = z;
            U = u;
            V = v;
        }

        public Vertex(Vector3 position, Vector2 textureCoords)
        {
            X = position.X;
            Y = position.Y;
            Z = position.Z;
            U = textureCoords.X;
            V = textureCoords.Y;
        }

        public static Vertex Multiply(Vertex vertex, Vector3 scale)
        {
            return new Vertex(vertex.X * scale.X, vertex.Y * scale.Y, vertex.Z * scale.Z, vertex.U, vertex.V);
        }
    }
}
