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
    public class LightmapTexture
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
}
