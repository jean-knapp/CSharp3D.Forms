using CSharp3D.Forms.Engine;
using OpenTK;
using System;
using System.ComponentModel;

namespace CSharp3D.Forms.Meshes
{
    /// <summary>
    /// A grid mesh
    /// </summary>
    [ToolboxItem(true)]
    [Description("A grid mesh")]
    public class GridMesh : LineMesh
    {
        /// <summary>
        /// The width of the grid, in World units.
        /// </summary>
        [Category("Grid")]
        public float Size { get; set; } = 4;

        /// <summary>
        /// The spacing between lines, in World units.
        /// </summary>
        [Category("Grid")]
        public float LineInterval { get; set; } = 1;

        public GridMesh() : base()
        {

        }
        public GridMesh(LocationVector position, RotationVector rotation) : base(position, rotation)
        {

        }

        /// <summary>
        /// Get the vertex array for the grid
        /// </summary>
        /// <returns> The vertex array </returns>
        public override float[] GetGLVertexArray()
        {
            int length = (int)Math.Max(Size / LineInterval, 2);
            Vertices = new LocationVector[4 * (length + 1)];

            for (int i = -length / 2; i < length / 2; i += 2)
            {
                Vertices[2 * (length / 2 + i) + 0] = new LocationVector(i * LineInterval, -length / 2 * LineInterval, 0);
                Vertices[2 * (length / 2 + i) + 1] = new LocationVector(i * LineInterval, length / 2 * LineInterval, 0);
                Vertices[2 * (length / 2 + i) + 2] = new LocationVector((i + 1) * LineInterval, length / 2 * LineInterval, 0);
                Vertices[2 * (length / 2 + i) + 3] = new LocationVector((i + 1) * LineInterval, -length / 2 * LineInterval, 0);
            }

            // Add last line
            Vertices[2 * (2 * length / 2) + 0] = new LocationVector(length / 2 * LineInterval, -length / 2 * LineInterval, 0);
            Vertices[2 * (2 * length / 2) + 1] = new LocationVector(length / 2 * LineInterval, length / 2 * LineInterval, 0);

            // Now add the other axis

            for (int i = -length / 2; i < length / 2; i += 2)
            {
                Vertices[2 * (2 * length / 2 + 1 + length / 2 + i) + 0] = new LocationVector(-length / 2 * LineInterval, i * LineInterval, 0);
                Vertices[2 * (2 * length / 2 + 1 + length / 2 + i) + 1] = new LocationVector(length / 2 * LineInterval, i * LineInterval, 0);
                Vertices[2 * (2 * length / 2 + 1 + length / 2 + i) + 2] = new LocationVector(length / 2 * LineInterval, (i + 1) * LineInterval, 0);
                Vertices[2 * (2 * length / 2 + 1 + length / 2 + i) + 3] = new LocationVector(-length / 2 * LineInterval, (i + 1) * LineInterval, 0);
            }

            // Add last line
            Vertices[2 * (2 * length / 2 + 1 + 2 * length / 2) + 0] = new LocationVector(-length / 2 * LineInterval, length / 2 * LineInterval, 0);
            Vertices[2 * (2 * length / 2 + 1 + 2 * length / 2) + 1] = new LocationVector(length / 2 * LineInterval, length / 2 * LineInterval, 0);

            return base.GetGLVertexArray();
        }
    }
}
