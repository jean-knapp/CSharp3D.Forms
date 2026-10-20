using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// The printable ASCII range of one font, rasterised once into a texture, with each glyph's
    /// place in it and how far it advances the pen — what <see cref="Meshes.ScreenTextMesh"/>
    /// draws its quads from.
    ///
    /// Text in a viewport is an editor's annotation (an entity's name, a selection's size), so it
    /// is drawn at a fixed size in pixels like Hammer's: its 2D views print through the material
    /// system's font surface in client space (Render.cpp CRender::DrawText), never scaled with the
    /// zoom. One texture per font is all that needs, and every pane shares it.
    ///
    /// The glyphs are drawn white with their coverage in the alpha channel, so a vertex colour can
    /// tint them to anything and blending gives anti-aliased edges.
    /// </summary>
    public sealed class GlyphAtlas
    {
        /// <summary>First and last character rasterised; anything else draws as '?'.</summary>
        private const char FirstChar = ' ';
        private const char LastChar = '~';

        /// <summary>Gap between cells, so linear sampling can never bleed a neighbour in.</summary>
        private const int Padding = 1;

        /// <summary>One glyph: its cell in the atlas (pixels) and its advance.</summary>
        public struct Glyph
        {
            public int X, Y, Width, Height;
            public int Advance;
        }

        private readonly Glyph[] glyphs = new Glyph[LastChar - FirstChar + 1];

        private readonly Dictionary<object, int> textureIds = new Dictionary<object, int>();

        private byte[] pixels;

        /// <summary>Atlas size in pixels.</summary>
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>The height of one line of text, in pixels.</summary>
        public int LineHeight { get; private set; }

        private static GlyphAtlas defaultAtlas;

        /// <summary>
        /// The atlas every editor viewport shares: Tahoma at 8 points, close to the "Default"
        /// vgui scheme font Hammer's 2D views print with (Render.cpp CRender::CRender).
        /// </summary>
        public static GlyphAtlas Default
        {
            get
            {
                if (defaultAtlas == null)
                {
                    using (Font font = new Font("Tahoma", 8f, FontStyle.Regular, GraphicsUnit.Point))
                        defaultAtlas = new GlyphAtlas(font);
                }

                return defaultAtlas;
            }
        }

        public GlyphAtlas(Font font)
        {
            Build(font);
        }

        /// <summary>The glyph for a character, '?' standing in for anything outside the atlas.</summary>
        public Glyph GetGlyph(char c)
        {
            if (c < FirstChar || c > LastChar)
                c = '?';

            return glyphs[c - FirstChar];
        }

        /// <summary>The width a string takes, in pixels.</summary>
        public int MeasureWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int width = 0;

            foreach (char c in text)
                width += GetGlyph(c).Advance;

            return width;
        }

        private void Build(Font font)
        {
            int count = LastChar - FirstChar + 1;
            int[] advances = new int[count];
            int cellHeight;

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                cellHeight = (int)Math.Ceiling(font.GetHeight(g));

                // Typographic measurement: the default StringFormat pads every string by a
                // fraction of an em on both sides, which would put gaps between the letters.
                using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

                    for (int i = 0; i < count; i++)
                    {
                        string s = ((char)(FirstChar + i)).ToString();
                        SizeF size = g.MeasureString(s, font, PointF.Empty, format);
                        advances[i] = Math.Max(1, (int)Math.Round(size.Width));
                    }
                }
            }

            LineHeight = cellHeight;

            // Cells are a little wider than the advance: anti-aliasing and italic overhang reach
            // past it, and clipping them would shave the edge of every glyph.
            const int columns = 16;
            int cellWidth = 0;

            foreach (int advance in advances)
                cellWidth = Math.Max(cellWidth, advance + 2);

            int rows = (count + columns - 1) / columns;

            Width = NextPowerOfTwo(columns * (cellWidth + Padding));
            Height = NextPowerOfTwo(rows * (cellHeight + Padding));

            using (Bitmap bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    // Grayscale anti-aliasing on black: the coverage reads straight back as
                    // brightness. ClearType would smear colour fringes into it.
                    g.Clear(Color.Black);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.SmoothingMode = SmoothingMode.None;

                    for (int i = 0; i < count; i++)
                    {
                        int x = (i % columns) * (cellWidth + Padding);
                        int y = (i / columns) * (cellHeight + Padding);

                        g.DrawString(((char)(FirstChar + i)).ToString(), font, Brushes.White, x, y, format);

                        glyphs[i] = new Glyph
                        {
                            X = x,
                            Y = y,
                            Width = Math.Min(cellWidth, advances[i] + 1),
                            Height = cellHeight,
                            Advance = advances[i],
                        };
                    }
                }

                pixels = ToCoverage(bitmap);
            }
        }

        /// <summary>White texels whose alpha is the rendered coverage, as RGBA bytes.</summary>
        private static byte[] ToCoverage(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            byte[] bgra = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
            bitmap.UnlockBits(data);

            byte[] rgba = new byte[bitmap.Width * bitmap.Height * 4];

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int source = y * data.Stride + x * 4;
                    int target = (y * bitmap.Width + x) * 4;

                    byte coverage = Math.Max(bgra[source], Math.Max(bgra[source + 1], bgra[source + 2]));

                    rgba[target] = 255;
                    rgba[target + 1] = 255;
                    rgba[target + 2] = 255;
                    rgba[target + 3] = coverage;
                }
            }

            return rgba;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;

            while (result < value)
                result <<= 1;

            return result;
        }

        /// <summary>
        /// The atlas texture in a context, uploaded on first use. Linear filtering, no mipmaps:
        /// screen text is snapped to land one texel on one pixel, where linear samples exactly
        /// what nearest would, and text laid out in the world (point_worldtext) is magnified
        /// and wants the smoothing. Must run on the GL thread with <paramref name="context"/>
        /// current.
        /// </summary>
        public int GetTextureId(object context)
        {
            int id;

            if (textureIds.TryGetValue(context, out id))
                return id;

            id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, id);

            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Width, Height, 0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            textureIds[context] = id;
            return id;
        }
    }
}
