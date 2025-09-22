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
        /// <param name="vertex"> The vertex in World units (Roll, Pitch, Yaw). </param>
        /// <returns> The vertex in OpenGL units. </returns>
        public static Vector3 ToGL(RotationVector vertex)
        {
            if (vertex == null)
                return new Vector3(0, 0, 0);

            return vertex.ToVector3() * new Matrix3(
                new Vector3(0, 0, -1),
                new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0));
        }

        /// <summary>
        /// Converts a vertex from the World units to the OpenGL coordinate system.
        /// </summary>
        /// <param name="vertex"> The vertex in World units (X, Y, Z). </param>
        /// <returns> The vertex in OpenGL units. </returns>
        public static Vector3 ToGL(LocationVector vertex)
        {
            return vertex.ToVector3() * new Matrix3(
                new Vector3(0, 0, -1),
                new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0));
        }

        /// <summary>
        /// Converts a vertex from the OpenGL coordinate system to the World units.
        /// </summary>
        /// <param name="vertexGl"> The vertex in OpenGL units. </param>
        /// <returns> The vertex in World units (Roll, Pitch, Yaw). </returns>
        public static RotationVector ToWorldRotation(Vector3 vertexGl)
        {
            return RotationVector.FromVector3(vertexGl * new Matrix3(
               new Vector3(0, -1, 0),
               new Vector3(0, 0, 1),
               new Vector3(-1, 0, 0)
           ));
        }

        /// <summary>
        /// Converts a vertex from the OpenGL coordinate system to the World units.
        /// </summary>
        /// <param name="vertexGl"> The vertex in OpenGL units. </param>
        /// <returns> The vertex in World units (X, Y, Z). </returns>
        public static LocationVector ToWorldLocation(Vector3 vertexGl)
        {
            return LocationVector.FromVector3(vertexGl * new Matrix3(
               new Vector3(0, -1, 0),
               new Vector3(0, 0, 1),
               new Vector3(-1, 0, 0)
           ));
        }
    }
}
