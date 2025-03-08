namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Represents the location in World units (X, Y, Z).
    /// X is the left to right axis.
    /// Y is the back to front axis.
    /// Z is the bottom to top axis.
    /// </summary>
    public struct Location
    {
        /// <summary>
        /// The X position in World units (left to right axis)
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// The Y position in World units (back to front axis)
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// The Z position in World units (bottom to top axis)
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Creates a new location with the specified coordinates.
        /// </summary>
        /// <param name="x"> The X position in World units (left to right axis) </param>
        /// <param name="y"> The Y position in World units (back to front axis) </param>
        /// <param name="z"> The Z position in World units (bottom to top axis) </param>
        public Location(float x = 0, float y = 0, float z = 0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Creates a new location with the specified coordinates.
        /// </summary>
        /// <param name="x"> The X position in World units (left to right axis) </param>
        /// <param name="y"> The Y position in World units (back to front axis) </param>
        /// <param name="z"> The Z position in World units (bottom to top axis) </param>
        public Location(decimal x = 0, decimal y = 0, decimal z = 0)
        {
            X = (float)x;
            Y = (float)y;
            Z = (float)z;
        }

        /// <summary>
        /// Subtracts the right location from the left location.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator -(Location left, Location right)
        {
            return new Dimensions(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }
    }
}
