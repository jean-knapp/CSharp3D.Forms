using System;

namespace CSharp3D.Forms.Engine
{
    /// <summary>The block-compressed formats a <see cref="CompressedImage"/> can hold (S3TC).</summary>
    public enum CompressedFormat
    {
        /// <summary>DXT1 with its punch-through mode read as transparent black (BC1 RGBA).</summary>
        Dxt1,
        Dxt3,
        Dxt5,
    }

    /// <summary>
    /// A picture as 4x4 compressed blocks with its whole mip chain, ready to upload as it is:
    /// a quarter (DXT3/5) to an eighth (DXT1) of the memory of the same picture as RGBA, on the
    /// GPU and in the driver's copy of it.
    /// </summary>
    public sealed class CompressedImage
    {
        public CompressedFormat Format { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Level 0 first, each half the one before, down to 1x1.</summary>
        public byte[][] Levels { get; }

        /// <param name="levels">
        /// Every level down to 1x1, each at least <see cref="LevelSize"/> bytes long.
        /// </param>
        /// <exception cref="ArgumentException">A level missing or short.</exception>
        public CompressedImage(CompressedFormat format, int width, int height, byte[][] levels)
        {
            if (width <= 0 || height <= 0 || levels == null || levels.Length != LevelCount(width, height))
                throw new ArgumentException("A compressed image needs every mip level down to 1x1.");

            for (int level = 0; level < levels.Length; level++)
            {
                if (levels[level] == null
                    || levels[level].Length < LevelSize(format, Math.Max(1, width >> level), Math.Max(1, height >> level)))
                {
                    throw new ArgumentException("Mip level " + level + " is missing or short.");
                }
            }

            Format = format;
            Width = width;
            Height = height;
            Levels = levels;
        }

        /// <summary>Levels in a full chain: the larger side halved down to 1.</summary>
        public static int LevelCount(int width, int height)
        {
            int count = 1;

            for (int size = Math.Max(width, height); size > 1; size >>= 1)
                count++;

            return count;
        }

        /// <summary>Bytes in one level of this size: 4x4 blocks, partial blocks rounded up.</summary>
        public static int LevelSize(CompressedFormat format, int width, int height)
        {
            int blocks = ((width + 3) / 4) * ((height + 3) / 4);
            return blocks * (format == CompressedFormat.Dxt1 ? 8 : 16);
        }
    }
}
