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
        /// Unloads the texture data from the context of a RendererControl.
        /// </summary>
        /// <param name="context"></param>
        public void UnloadTexture(object context)
        {
            if (textureId.ContainsKey(context))
            {
                GL.DeleteTexture(textureId[context]);
                textureId.Remove(context);
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
            if (this.Bitmap == null)
            {
                throw new Exception($"Texture with no bitmap.");
            }

            // Generate texture
            textureId[context] = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId[context]);

            // Load the image
            BitmapData data = this.Bitmap.LockBits(
                new Rectangle(0, 0, this.Bitmap.Width, this.Bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

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

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Generate mipmaps
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Disposes the texture data.
        /// </summary>
        /// <param name="context"> The context of the RendererControl. </param>
        public void Dispose(object context)
        {
            if (textureId.ContainsKey(context))
                GL.DeleteTexture(textureId[context]);
        }
    }
}
