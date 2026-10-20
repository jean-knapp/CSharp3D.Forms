using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using OpenTK.Graphics.OpenGL;
using System.ComponentModel;
using System;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// A texture that can be used by a material.
    /// </summary>
    [Description("A texture that can be used by a material.")]
    public class Texture : Component
    {
        /// <summary>
        /// The texture id for each context.
        /// </summary>
        [Browsable(false)]
        public Dictionary<object, int> textureId { get; private set; } = new Dictionary<object, int>();

        /// <summary>
        /// The bitmap data.
        /// </summary>
        [Category("Texture")]
        [Description("The bitmap data.")]
        public Bitmap Bitmap { get; set; } = null;

        /// <summary>
        /// The same picture as compressed blocks with a full mip chain, when its source had them
        /// (a VTF's DXT data). Uploaded instead of <see cref="Bitmap"/> wherever the driver takes
        /// S3TC, which is a quarter to an eighth of the GPU memory, uses the file's own mips, and
        /// skips building a chain at upload. <see cref="Bitmap"/> stays the picture everything
        /// else reads; the two must be the same image.
        /// </summary>
        [Browsable(false)]
        public CompressedImage Compressed { get; set; } = null;

        public Texture()
        {

        }

        /// <summary>
        /// Checks if the texture data is loaded in the context of a RendererControl.
        /// </summary>
        /// <param name="context">The context of the RendererControl.</param>
        /// <returns> True if the texture data is loaded, false otherwise. </returns>
        public bool IsTextureDataLoaded(object context)
        {
            return textureId.ContainsKey(context);
        }

        /// <summary>
        /// Gets the texture id for the context of a RendererControl.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <param name="scene"> The scene. </param>
        /// <returns></returns>
        public int GetTextureId(object context, Scene scene)
        {
            if (!IsTextureDataLoaded(context))
            {
                LoadTexture(context, scene);
            }

            return textureId[context];
        }

        /// <summary>
        /// Disposes the texture data.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        public void Dispose(object context)
        {
            try
            {
                if (textureId.ContainsKey(context))
                {
                    GL.DeleteTexture(textureId[context]);
                    textureId.Remove(context);
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions, such as logging
                Console.WriteLine($"Failed to delete textures: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the texture data into the context of a RendererControl.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        /// <param name="scene"> The scene. </param>
        /// <exception cref="Exception"></exception>
        private void LoadTexture(object context, Scene scene)
        {
            if (this.Bitmap == null && this.Compressed == null)
            {
                throw new Exception($"Texture with no bitmap.");
            }

            // Generate texture
            textureId[context] = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId[context]);

            if (Compressed != null && UploadCompressed(Compressed))
            {
                SetSampling();
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return;
            }

            if (this.Bitmap == null)
            {
                UploadPlaceholder();
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return;
            }

            // Load the image
            BitmapData data;

            try
            {
                data = this.Bitmap.LockBits(
                    new Rectangle(0, 0, this.Bitmap.Width, this.Bitmap.Height),
                    ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                // A bitmap disposed by its owner ("Parameter is not valid") or held by another
                // thread. This runs inside a view's paint, where an exception takes the whole
                // host down, so one unusable picture draws as the missing-texture magenta
                // instead. The id stays registered: the next frame must not retry and fail
                // again forever.
                Console.WriteLine($"Texture upload failed, drawing a placeholder: {ex.Message}");
                UploadPlaceholder();
                GL.BindTexture(TextureTarget.Texture2D, 0);
                return;
            }

            // Transfer the image data to the GPU
            GL.TexImage2D(TextureTarget.Texture2D,
                          0,
                          PixelInternalFormat.Rgba,
                          data.Width,
                          data.Height,
                          0,
                          OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
                          PixelType.UnsignedByte,
                          data.Scan0);

            this.Bitmap.UnlockBits(data);

            // Generate mipmaps
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            SetSampling();

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Upload every level of a compressed image into the bound texture. False - with the
        /// texture left for the bitmap upload to fill - when the driver has no S3TC or refuses it.
        /// </summary>
        private static bool UploadCompressed(CompressedImage image)
        {
            if (!SupportsS3tc())
                return false;

            // Errors from before are not this upload's; only what it raises decides.
            while (GL.GetError() != ErrorCode.NoError) { }

            InternalFormat format = image.Format == CompressedFormat.Dxt1 ? CompressedRgbaS3tcDxt1
                : image.Format == CompressedFormat.Dxt3 ? CompressedRgbaS3tcDxt3
                : CompressedRgbaS3tcDxt5;

            for (int level = 0; level < image.Levels.Length; level++)
            {
                int width = Math.Max(1, image.Width >> level);
                int height = Math.Max(1, image.Height >> level);

                GL.CompressedTexImage2D(TextureTarget.Texture2D, level, format, width, height, 0,
                    CompressedImage.LevelSize(image.Format, width, height), image.Levels[level]);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, image.Levels.Length - 1);

            return GL.GetError() == ErrorCode.NoError;
        }

        /// <summary>GL_COMPRESSED_RGBA_S3TC_DXT1/3/5_EXT, raw so any binding generation compiles.</summary>
        private const InternalFormat CompressedRgbaS3tcDxt1 = (InternalFormat)0x83F1;
        private const InternalFormat CompressedRgbaS3tcDxt3 = (InternalFormat)0x83F2;
        private const InternalFormat CompressedRgbaS3tcDxt5 = (InternalFormat)0x83F3;

        /// <summary>Whether the driver takes S3TC: null until asked.</summary>
        private static bool? s3tc;

        /// <summary>
        /// EXT_texture_compression_s3tc, asked once: every desktop driver of the last two decades
        /// has it, but it is still an extension, and the bitmap upload is there when it is not.
        /// </summary>
        private static bool SupportsS3tc()
        {
            if (s3tc.HasValue)
                return s3tc.Value;

            bool found = false;

            try
            {
                int count = GL.GetInteger(GetPName.NumExtensions);

                for (int i = 0; i < count && !found; i++)
                    found = GL.GetString(StringNameIndexed.Extensions, i) == "GL_EXT_texture_compression_s3tc";
            }
            catch
            {
                found = false;
            }

            if (!found)
            {
                // A context older than 3.0 has no indexed query; the one string has them all.
                string all = null;

                try { all = GL.GetString(StringName.Extensions); }
                catch { all = null; }

                found = all != null && all.Contains("GL_EXT_texture_compression_s3tc");
            }

            while (GL.GetError() != ErrorCode.NoError) { }

            s3tc = found;
            return found;
        }

        /// <summary>Wrapping and filtering for the bound texture, whichever way its pixels came.</summary>
        private static void SetSampling()
        {
            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Trilinear, not plain Linear. The mipmap chain below was always being generated and
            // never sampled: a non-mipmapped minification filter reads level 0 whatever the
            // surface's scale on screen, so a floor running away from the camera samples a
            // full-resolution texture at well under one texel per pixel. That is what made
            // distant brickwork shimmer and crawl - the classic minification aliasing that
            // mipmaps exist to solve.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                            (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                            (int)TextureMagFilter.Linear);

            ApplyAnisotropy();
        }

        /// <summary>One magenta texel into the bound texture, unfiltered, wrapping.</summary>
        private static void UploadPlaceholder()
        {
            byte[] magenta = { 255, 0, 255, 255 };   // BGRA

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
                          OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, magenta);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        }

        /// <summary>
        /// How many samples anisotropic filtering may take, clamped to what the driver offers.
        /// 8 to match the ray traced view's sampler (GpuScene: MaxAnisotropy = 8), so the two
        /// renderers do not disagree about how a floor looks.
        /// </summary>
        public static float Anisotropy { get; set; } = 8f;

        /// <summary>
        /// GL_TEXTURE_MAX_ANISOTROPY_EXT / GL_MAX_TEXTURE_MAX_ANISOTROPY_EXT. Raw values rather
        /// than OpenTK enum members so this keeps compiling whichever binding generation is in
        /// use - the extension has been effectively universal for two decades, but it is still
        /// an extension.
        /// </summary>
        private const TextureParameterName TextureMaxAnisotropyExt = (TextureParameterName)0x84FE;

        private const GetPName MaxTextureMaxAnisotropyExt = (GetPName)0x84FF;

        /// <summary>Driver ceiling, read once; 0 until asked, -1 when unsupported.</summary>
        private static float maxAnisotropy;

        /// <summary>
        /// Trilinear alone still blurs a surface seen at a grazing angle, because the mip level
        /// has to be chosen for the axis that is minified most - which on a floor stretching to
        /// the horizon is the wrong one for the other axis. Anisotropic filtering is what keeps
        /// that floor sharp, and it is the single most visible setting for a map editor whose
        /// viewport is mostly floors and walls seen edge-on.
        /// </summary>
        private static void ApplyAnisotropy()
        {
            if (maxAnisotropy == 0f)
            {
                // An unsupported pname raises GL_INVALID_ENUM and leaves the value alone, so a
                // driver without the extension reads back 0 and is remembered as unsupported.
                float supported = 0f;

                try { supported = GL.GetFloat(MaxTextureMaxAnisotropyExt); }
                catch { supported = 0f; }

                maxAnisotropy = supported >= 2f ? supported : -1f;
            }

            if (maxAnisotropy < 2f)
                return;

            float wanted = Math.Min(Math.Max(Anisotropy, 1f), maxAnisotropy);

            GL.TexParameter(TextureTarget.Texture2D, TextureMaxAnisotropyExt, wanted);
        }
    }
}
