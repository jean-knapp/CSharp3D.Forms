using System;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// One face of a <see cref="SkyboxCube"/>: linear light, three floats a pixel, row by row
    /// from the top.
    /// </summary>
    public sealed class SkyboxCubeFace
    {
        public SkyboxCubeFace(int width, int height, float[] rgb)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (rgb == null || rgb.Length < width * height * 3)
                throw new ArgumentException("A face needs three floats for every pixel.", nameof(rgb));

            Width = width;
            Height = height;
            Rgb = rgb;
        }

        public int Width { get; }
        public int Height { get; }
        public float[] Rgb { get; }
    }

    /// <summary>
    /// A sky as six pictures seen from the middle of a cube, ready for any of the renderers to
    /// draw: the GL sky pass, the Vulkan ray tracer, or a CPU preview.
    ///
    /// <para>
    /// The faces are addressed by the cube's own axes, not by any engine's names for them:
    /// <see cref="PositiveX"/> through <see cref="NegativeZ"/>, in a right-handed frame with +Y up.
    /// Each face is the picture a viewer standing at the middle sees when facing that axis —
    /// upright, with its right-hand edge on the viewer's right. The top face (+Y) is seen with the
    /// viewer facing -Z, so its bottom edge joins the top of the -Z face; the bottom face (-Y) the
    /// same way round, its top edge joining the bottom of -Z.
    /// </para>
    /// <para>
    /// Which picture goes on which axis is the host's knowledge — for Source it is not what the
    /// names suggest — and so is how its world is oriented against the cube, which it gives as
    /// <see cref="CubeFromWorld"/>.
    /// </para>
    /// </summary>
    public sealed class SkyboxCube
    {
        /// <summary>The faces, in axis order: +X, -X, +Y, -Y, +Z, -Z. Any may be null.</summary>
        public SkyboxCubeFace[] Faces { get; } = new SkyboxCubeFace[6];

        public SkyboxCubeFace PositiveX { get { return Faces[0]; } set { Faces[0] = value; } }
        public SkyboxCubeFace NegativeX { get { return Faces[1]; } set { Faces[1] = value; } }
        public SkyboxCubeFace PositiveY { get { return Faces[2]; } set { Faces[2] = value; } }
        public SkyboxCubeFace NegativeY { get { return Faces[3]; } set { Faces[3] = value; } }
        public SkyboxCubeFace PositiveZ { get { return Faces[4]; } set { Faces[4] = value; } }
        public SkyboxCubeFace NegativeZ { get { return Faces[5]; } set { Faces[5] = value; } }

        /// <summary>
        /// Turns a direction in the renderer's world into the cube's frame. Row-major: a direction
        /// d maps to (Row0·d, Row1·d, Row2·d). Identity when the world already is the cube's frame.
        /// </summary>
        public OpenTK.Matrix3 CubeFromWorld { get; set; } = OpenTK.Matrix3.Identity;

        /// <summary>
        /// Side faces stored twice as wide as they are tall hold only the sky above the horizon,
        /// and are drawn over the upper half of the face with their last row carried down to the
        /// floor — Source's <c>$basetexturetransform "... scale 1 2"</c> with the face clamped.
        /// On by default; an engine that stretches such a face over the whole side turns it off.
        /// </summary>
        public bool HalfHeightSides { get; set; } = true;

        /// <summary>Bumped by every change the renderers must re-upload for.</summary>
        public int Version { get; private set; }

        public void Touch()
        {
            Version++;
        }

        /// <summary>Whether a face index is one of the four around the horizon.</summary>
        public static bool IsSide(int index)
        {
            return index != 2 && index != 3;
        }

        /// <summary>Whether a face holds only the sky above the horizon.</summary>
        public bool IsHalfHeight(int index)
        {
            SkyboxCubeFace face = Faces[index];
            return HalfHeightSides && face != null && IsSide(index) && face.Width == 2 * face.Height;
        }

        /// <summary>The mean linear radiance over the faces present, for an ambient term.</summary>
        public OpenTK.Vector3 MeanRadiance()
        {
            double r = 0, g = 0, b = 0;
            long count = 0;

            foreach (SkyboxCubeFace face in Faces)
            {
                if (face == null)
                    continue;

                int step = Math.Max(1, face.Width * face.Height / 65536);

                for (int p = 0; p < face.Width * face.Height; p += step)
                {
                    r += face.Rgb[p * 3];
                    g += face.Rgb[p * 3 + 1];
                    b += face.Rgb[p * 3 + 2];
                    count++;
                }
            }

            if (count == 0)
                return OpenTK.Vector3.Zero;

            return new OpenTK.Vector3((float)(r / count), (float)(g / count), (float)(b / count));
        }
    }
}
