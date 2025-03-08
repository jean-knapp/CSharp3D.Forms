using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSharp3D.Forms.Engine
{
    public struct Dimensions
    {
        /// <summary>
        /// Width of the size in World units (left to right axis)
        /// </summary>
        public float Width { get; set; }

        /// <summary>
        /// Length of the size in World units (back to front axis)
        /// </summary>
        public float Length { get; set; }

        /// <summary>
        /// Height of the size in World units (bottom to top axis)
        /// </summary>
        public float Height { get; set; }

        /// <summary>
        /// Creates a new size with the specified dimensions.
        /// </summary>
        /// <param name="width"> The width of the size in World units (left to right axis) </param>
        /// <param name="length"> The length of the size in World units (bottom to top axis) </param>
        /// <param name="height"> The height of the size in World units (back to front axis) </param>
        public Dimensions(float width = 0, float length = 0, float height = 0)
        {
            Width = width;
            
            Length = length;
            Height = height;
        }

        /// <summary>
        /// Creates a new size with the specified dimensions.
        /// </summary>
        /// <param name="size"> The size of the size in World units (width, length, height) </param>
        public Dimensions(float size)
        {
            Width = size;
            Length = size;
            Height = size;
        }

        /// <summary>
        /// Adds the right size to the left size.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator +(Dimensions left, Dimensions right)
        {
            left.Width += right.Width;
            left.Length += right.Length;
            left.Height += right.Height;
            return left;
        }
        /// <summary>
        /// Subtracts the right size from the left size.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator -(Dimensions left, Dimensions right)
        {
            left.Width -= right.Width;
            left.Length -= right.Length;
            left.Height -= right.Height;
            return left;
        }

        /// <summary>
        /// Multiplies the Size by an integer.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator *(Dimensions left, int right)
        {
            left.Width *= right;
            left.Length *= right;
            left.Height *= right;
            return left;
        }

        /// <summary>
        /// Multiplies the Size by a float.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator *(Dimensions left, float right)
        {
            left.Width *= right;
            left.Length *= right;
            left.Height *= right;
            return left;
        }

        /// <summary>
        /// Multiplies the Size by a double.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator *(Dimensions left, double right)
        {
            left.Width *= (float)right;
            left.Length *= (float)right;
            left.Height *= (float)right;
            return left;
        }

        /// <summary>
        /// Divides the Size by an integer.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator /(Dimensions left, int right)
        {
            left.Width /= right;
            left.Length /= right;
            left.Height /= right;
            return left;
        }

        /// <summary>
        /// Divides the Size by a float.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator /(Dimensions left, float right)
        {
            left.Width /= right;
            left.Length /= right;
            left.Height /= right;
            return left;
        }

        /// <summary>
        /// Divides the Size by a double.
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Dimensions operator /(Dimensions left, double right)
        {
            left.Width /= (float)right;
            left.Length /= (float)right;
            left.Height /= (float)right;
            return left;
        }
    }
}
