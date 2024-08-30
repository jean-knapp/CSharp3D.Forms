using OpenTK;

namespace CSharp3D.Forms.Engine.Helpers
{
    /// <summary>
    /// Helper class to convert between OpenGL and World coordinate systems.
    /// </summary>
    internal class VectorOrientation
    {
        /// <summary>
        /// Converts a vertex from the World units to the OpenGL coordinate system.
        /// </summary>
        /// <param name="vertex"> The vertex in World units (X, Y, Z) or (Roll, Pitch, Yaw). </param>
        /// <returns> The vertex in OpenGL units. </returns>
        public static Vector3 ToGL(Vector3 vertex)
        {
            return vertex * new Matrix3(
                new Vector3(0, 0, -1),
                new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0));
        }

        /// <summary>
        /// Converts a vertex from the OpenGL coordinate system to the World units.
        /// </summary>
        /// <param name="vertexGl"> The vertex in OpenGL units. </param>
        /// <returns> The vertex in World units (X, Y, Z) or (Roll, Pitch, Yaw). </returns>
        public static Vector3 ToWorld(Vector3 vertexGl)
        {
            return vertexGl * new Matrix3(
               new Vector3(0, -1, 0),
               new Vector3(0, 0, 1),
               new Vector3(-1, 0, 0)
           );
        }
    }
}
