namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// A UV map
    /// </summary>
    public class UV
    {
        /// <summary>
        /// The left UV coordinate
        /// </summary>
        public float Left { get; set; } = 0;

        /// <summary>
        /// The top UV coordinate
        /// </summary>
        public float Top { get; set; } = 0;

        /// <summary>
        /// The right UV coordinate
        /// </summary>
        public float Right { get; set; } = 1;


        /// <summary>
        /// The bottom UV coordinate
        /// </summary>
        public float Bottom { get; set; } = 1;

        /// <summary>
        /// Create a new UV map
        /// </summary>
        /// <param name="left"> The left UV coordinate </param>
        /// <param name="top"> The top UV coordinate </param>
        /// <param name="right"> The right UV coordinate </param>
        /// <param name="bottom"> The bottom UV coordinate </param>
        public UV(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public UV()
        {

        }
    }
}
