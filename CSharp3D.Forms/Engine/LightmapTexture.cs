using OpenTK.Graphics.OpenGL;
using System.Collections.Generic;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// A small updatable RGB texture for baked lightmaps. Data is set from any thread
    /// (<see cref="SetData"/> just stores it); the upload happens lazily on the GL
    /// thread when a mesh binds it during draw, one texture object per GL context
    /// (same per-context pattern as <see cref="Texture"/> and mesh VBOs).
    /// </summary>
    public class LightmapTexture : ILightmapTexture
    {
        private readonly object _gate = new object();

        private readonly Dictionary<object, int> _ids = new Dictionary<object, int>();
        private readonly Dictionary<object, int> _uploadedVersion = new Dictionary<object, int>();

        private byte[] _rgb;
        private int _width, _height;
        private int _version;

        public int Width { get { lock (_gate) return _width; } }
        public int Height { get { lock (_gate) return _height; } }

        /// <summary>Whether any data has been supplied yet.</summary>
        public bool HasData { get { lock (_gate) return _rgb != null; } }

        /// <summary>
        /// Replace the texture content (tightly packed RGB, 3 bytes per texel). Safe
        /// from any thread; each GL context re-uploads on its next bind.
        /// </summary>
        public void SetData(byte[] rgb, int width, int height)
        {
            lock (_gate)
            {
                _rgb = rgb;
                _width = width;
                _height = height;
                _version++;
            }
        }

        /// <summary>
        /// Get (and if needed create/update) the texture object for a context. Must run
        /// on that context's GL thread. Returns 0 when no data has been set yet.
        /// </summary>
        public int GetTextureId(object context)
        {
            byte[] rgb;
            int width, height, version;

            lock (_gate)
            {
                rgb = _rgb;
                width = _width;
                height = _height;
                version = _version;
            }

            if (rgb == null)
                return 0;

            int id;
            if (!_ids.TryGetValue(context, out id))
            {
                id = GL.GenTexture();
                _ids[context] = id;

                GL.BindTexture(TextureTarget.Texture2D, id);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }

            int uploaded;
            _uploadedVersion.TryGetValue(context, out uploaded);

            if (uploaded != version)
            {
                GL.BindTexture(TextureTarget.Texture2D, id);

                // Rows are w*3 bytes — not 4-aligned; without this, widths not divisible
                // by 4 shear the image.
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8,
                    width, height, 0, PixelFormat.Rgb, PixelType.UnsignedByte, rgb);
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

                _uploadedVersion[context] = version;
            }

            return id;
        }

        /// <summary>Delete the context's texture object (on that context's GL thread).</summary>
        public void Dispose(object context)
        {
            int id;
            if (_ids.TryGetValue(context, out id))
            {
                GL.DeleteTexture(id);
                _ids.Remove(context);
                _uploadedVersion.Remove(context);
            }
        }
    }

    /// <summary>
    /// What a lightmapped mesh binds: a texture whose size the shader needs to turn texel
    /// coordinates into UVs. A face's lightmap is a rectangle of it - the whole of a
    /// <see cref="LightmapTexture"/>, or one slot of a <see cref="LightmapAtlas"/>.
    /// </summary>
    public interface ILightmapTexture
    {
        /// <summary>The texture object for a context, created or updated as needed; 0 for none yet.</summary>
        int GetTextureId(object context);

        int Width { get; }

        int Height { get; }
    }

    /// <summary>
    /// A rectangle of texels: a lightmap's slot. <see cref="Width"/> and <see cref="Height"/>
    /// are the slot as it lies in the texture; a <see cref="Rotated"/> slot holds the lightmap
    /// transposed (its luxel s runs down the texture, t across), which is how an atlas keeps
    /// tall, narrow lightmaps on short shelves. Whoever maps onto it swaps s and t.
    /// </summary>
    public struct LightmapRect
    {
        public int X, Y, Width, Height;

        public bool Rotated;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        /// <summary>The lightmap's own size, whichever way it lies.</summary>
        public int LightmapWidth => Rotated ? Height : Width;

        public int LightmapHeight => Rotated ? Width : Height;
    }

    /// <summary>
    /// Every face lightmap of a scene in one texture, so faces that share a material can be
    /// drawn as one batch instead of binding a texture per face (Hammer++ and the engine do
    /// the same with lightmap pages).
    ///
    /// <para>
    /// Slots are packed in shelves and never moved: when the texture runs out of room it
    /// doubles, copying what it holds to the same texel positions, so a slot's coordinates
    /// stay valid (only the texture size, a uniform, changes). A freed slot's area is not
    /// reused; when even the largest texture is full the owner calls <see cref="Reset"/> and
    /// lays everything out again.
    /// </para>
    /// <para>
    /// Faces never bleed into each other: the shader clamps every lookup to the face's own
    /// rectangle, which gives exactly what a texture of its own with CLAMP_TO_EDGE gave.
    /// Written and allocated on the UI thread; uploaded lazily, per GL context, by
    /// <see cref="GetTextureId"/> - only the part written since that context's last upload.
    /// </para>
    /// </summary>
    public sealed class LightmapAtlas : ILightmapTexture
    {
        public const int InitialSize = 1024;
        public const int MaxSize = 8192;

        private readonly object _gate = new object();

        private int _size = InitialSize;
        private byte[] _pixels = new byte[InitialSize * InitialSize * 3];
        private int _sizeVersion;

        private sealed class Shelf
        {
            public int Y, Height, X;
        }

        private readonly List<Shelf> _shelves = new List<Shelf>();
        private int _top;

        private sealed class ContextState
        {
            public int Id;
            public int SizeVersion = -1;
            public int DirtyX0 = int.MaxValue, DirtyY0 = int.MaxValue, DirtyX1, DirtyY1;

            public void Dirty(int x0, int y0, int x1, int y1)
            {
                if (x0 < DirtyX0) DirtyX0 = x0;
                if (y0 < DirtyY0) DirtyY0 = y0;
                if (x1 > DirtyX1) DirtyX1 = x1;
                if (y1 > DirtyY1) DirtyY1 = y1;
            }

            public void Clean()
            {
                DirtyX0 = DirtyY0 = int.MaxValue;
                DirtyX1 = DirtyY1 = 0;
            }
        }

        private readonly Dictionary<object, ContextState> _contexts = new Dictionary<object, ContextState>();

        public int Width { get { lock (_gate) return _size; } }

        public int Height { get { lock (_gate) return _size; } }

        /// <summary>Texels handed out, and how many of them were freed since the last <see cref="Reset"/>.</summary>
        public long AllocatedTexels { get; private set; }

        public long FreedTexels { get; private set; }

        /// <summary>
        /// A slot for a <paramref name="width"/> x <paramref name="height"/> lightmap, growing the
        /// texture if it must; one taller than it is wide is laid on its side. False when it does
        /// not fit even at <see cref="MaxSize"/>.
        /// </summary>
        public bool TryAllocate(int width, int height, out LightmapRect rect)
        {
            rect = default(LightmapRect);

            if (width <= 0 || height <= 0 || width > MaxSize || height > MaxSize)
                return false;

            bool rotated = height > width;

            if (rotated)
            {
                int swap = width;
                width = height;
                height = swap;
            }

            lock (_gate)
            {
                while (true)
                {
                    if (TryPlace(width, height, out rect))
                    {
                        rect.Rotated = rotated;
                        AllocatedTexels += (long)width * height;
                        return true;
                    }

                    if (_size >= MaxSize)
                        return false;

                    Grow();
                }
            }
        }

        private bool TryPlace(int width, int height, out LightmapRect rect)
        {
            rect = default(LightmapRect);

            if (width > _size)
                return false;

            // Shelves come in power-of-two heights and a slot goes on a shelf of its own class:
            // lightmaps range from 2 to 1024 texels tall, and a shelf as tall as the first slot
            // put on it wasted most of the texture under the short ones that followed.
            int shelfHeight = 1;
            while (shelfHeight < height)
                shelfHeight <<= 1;

            Shelf best = null;

            foreach (Shelf shelf in _shelves)
            {
                if (shelf.Height == shelfHeight && shelf.X + width <= _size)
                {
                    best = shelf;
                    break;
                }
            }

            if (best == null)
            {
                if (_top + shelfHeight > _size)
                    return false;

                best = new Shelf { Y = _top, Height = shelfHeight, X = 0 };
                _shelves.Add(best);
                _top += shelfHeight;
            }

            rect = new LightmapRect { X = best.X, Y = best.Y, Width = width, Height = height };
            best.X += width;
            return true;
        }

        /// <summary>Double the texture, keeping every slot where it is.</summary>
        private void Grow()
        {
            int size = _size * 2;
            byte[] pixels = new byte[size * size * 3];

            for (int y = 0; y < _size; y++)
                System.Buffer.BlockCopy(_pixels, y * _size * 3, pixels, y * size * 3, _size * 3);

            _pixels = pixels;
            _size = size;
            _sizeVersion++;
        }

        /// <summary>Give a slot back. Its area is only reclaimed by <see cref="Reset"/>.</summary>
        public void Free(LightmapRect rect)
        {
            if (!rect.IsEmpty)
                lock (_gate) FreedTexels += (long)rect.Width * rect.Height;
        }

        /// <summary>Forget every slot (the owner reallocates and rewrites what it still needs).</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _shelves.Clear();
                _top = 0;
                AllocatedTexels = 0;
                FreedTexels = 0;
                System.Array.Clear(_pixels, 0, _pixels.Length);
                _sizeVersion++;
            }
        }

        /// <summary>
        /// Put a lightmap into its slot: tightly packed RGB rows of the lightmap's own width
        /// (<see cref="LightmapRect.LightmapWidth"/>), row 0 first - transposed on the way in
        /// for a rotated slot.
        /// </summary>
        public void Write(LightmapRect rect, byte[] rgb)
        {
            if (rect.IsEmpty || rgb == null || rgb.Length < rect.Width * rect.Height * 3)
                return;

            lock (_gate)
            {
                if (!rect.Rotated)
                {
                    int row = rect.Width * 3;

                    for (int y = 0; y < rect.Height; y++)
                        System.Buffer.BlockCopy(rgb, y * row, _pixels, ((rect.Y + y) * _size + rect.X) * 3, row);
                }
                else
                {
                    // Luxel (s, t) of the lightmap goes to texel (X + t, Y + s).
                    int sourceWidth = rect.LightmapWidth;

                    for (int s = 0; s < rect.Height; s++)
                    {
                        int to = ((rect.Y + s) * _size + rect.X) * 3;

                        for (int t2 = 0; t2 < rect.Width; t2++)
                        {
                            int from = (t2 * sourceWidth + s) * 3;
                            _pixels[to++] = rgb[from];
                            _pixels[to++] = rgb[from + 1];
                            _pixels[to++] = rgb[from + 2];
                        }
                    }
                }

                foreach (ContextState state in _contexts.Values)
                    state.Dirty(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
            }
        }

        public int GetTextureId(object context)
        {
            lock (_gate)
            {
                ContextState state;

                if (!_contexts.TryGetValue(context, out state))
                {
                    state = new ContextState { Id = GL.GenTexture() };
                    _contexts[context] = state;

                    GL.BindTexture(TextureTarget.Texture2D, state.Id);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                }

                if (state.SizeVersion != _sizeVersion)
                {
                    GL.BindTexture(TextureTarget.Texture2D, state.Id);
                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb8,
                        _size, _size, 0, PixelFormat.Rgb, PixelType.UnsignedByte, _pixels);
                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

                    state.SizeVersion = _sizeVersion;
                    state.Clean();
                }
                else if (state.DirtyX1 > state.DirtyX0 && state.DirtyY1 > state.DirtyY0)
                {
                    // Only the rows and columns written since this context last uploaded.
                    GL.BindTexture(TextureTarget.Texture2D, state.Id);
                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                    GL.PixelStore(PixelStoreParameter.UnpackRowLength, _size);
                    GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, state.DirtyX0);
                    GL.PixelStore(PixelStoreParameter.UnpackSkipRows, state.DirtyY0);
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, state.DirtyX0, state.DirtyY0,
                        state.DirtyX1 - state.DirtyX0, state.DirtyY1 - state.DirtyY0,
                        PixelFormat.Rgb, PixelType.UnsignedByte, _pixels);
                    GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                    GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
                    GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
                    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

                    state.Clean();
                }

                return state.Id;
            }
        }

        /// <summary>Delete the context's texture object (on that context's GL thread).</summary>
        public void Dispose(object context)
        {
            lock (_gate)
            {
                ContextState state;

                if (_contexts.TryGetValue(context, out state))
                {
                    GL.DeleteTexture(state.Id);
                    _contexts.Remove(context);
                }
            }
        }
    }
}