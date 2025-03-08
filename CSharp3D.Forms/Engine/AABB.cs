using OpenTK;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Axis-aligned bounding box.
    /// </summary>
    public class AABB
    {

        /// <summary>
        /// The minimum corner of the bounding box (left, back, bottom).
        /// </summary>
        public Location Min { get; set; } = new Location();

        /// <summary>
        /// The maximum corner of the bounding box (right, front, top).
        /// </summary>
        public Location Max { get; set; } = new Location();

        /// <summary>
        /// Creates a new bounding box with the specified corners.
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        public AABB(Location min, Location max)
        {
            Min = min;
            Max = max;
        }

        public AABB(Dimensions dimensions)
        {
            Min = new Location(-dimensions.Width / 2, -dimensions.Length / 2, -dimensions.Height / 2);
            Max = new Location(dimensions.Width / 2, dimensions.Length / 2, dimensions.Height / 2);
        }

        public AABB(float length, float width, float height)
        {
            Min = new Location(-length / 2, -width / 2, -height / 2);
            Max = new Location(length / 2, width / 2, height / 2);
        }

        public AABB(float size)
        {
            Min = new Location(-size / 2, -size / 2, -size / 2);
            Max = new Location(size / 2, size / 2, size / 2);
        }

        /// <summary>
        /// The center of the bounding box.
        /// </summary>
        public Location Center { get
            {
                return new Location((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2);
            } 
        }

        /// <summary>
        /// The size of the bounding box.
        /// </summary>
        public Dimensions Size
        {
            get
            {
                return Max - Min;
            }
        }
    }
}
