using System;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace CSharp3D.Forms.Engine
{
    /// <summary>
    /// Runs a <see cref="SkyboxPass"/> into a bitmap, without a window on screen: a hidden GL
    /// context, a framebuffer the size asked for, and a read-back.
    ///
    /// <para>
    /// For hosts that paint the sky themselves and draw on top of it with GDI — a GL window is a
    /// separate surface, and nothing a WinForms control paints can sit over part of it. The sky
    /// that comes back is the same shader, the same face table and the same tone curve the map
    /// editor's views draw with.
    /// </para>
    /// </summary>
    public sealed class SkyboxOffscreen : IDisposable
    {
        private NativeWindow _window;
        private GraphicsContext _context;

        private int _framebuffer;
        private int _colour;
        private int _width;
        private int _height;

        private SkyboxOffscreen()
        {
        }

        /// <summary>A renderer, or null when this machine cannot give it a GL 3.3 context.</summary>
        public static SkyboxOffscreen TryCreate()
        {
            var renderer = new SkyboxOffscreen();

            try
            {
                renderer._window = new NativeWindow(16, 16, string.Empty, GameWindowFlags.Default,
                    GraphicsMode.Default, DisplayDevice.Default)
                {
                    Visible = false,
                };

                renderer._context = new GraphicsContext(GraphicsMode.Default, renderer._window.WindowInfo,
                    3, 3, GraphicsContextFlags.Default);

                renderer._context.MakeCurrent(renderer._window.WindowInfo);
                renderer._context.LoadAll();

                return renderer;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("Offscreen sky renderer unavailable: " + exception.Message);
                renderer.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Draws the sky for a camera into a new bitmap the caller owns, or null if the draw
        /// failed. The camera is its axes in the cube's world and the tangents of half its field
        /// of view; an equirectangular pass ignores the tangents.
        /// </summary>
        public Bitmap Render(SkyboxPass pass, Vector3 right, Vector3 up, Vector3 forward,
            float tanX, float tanY, int width, int height)
        {
            if (pass == null || _context == null || width <= 0 || height <= 0)
                return null;

            try
            {
                _context.MakeCurrent(_window.WindowInfo);

                EnsureTarget(width, height);

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GL.Viewport(0, 0, width, height);

                Color background = pass.Void;
                GL.ClearColor(background.R / 255f, background.G / 255f, background.B / 255f, 1f);
                GL.Clear(ClearBufferMask.ColorBufferBit);

                pass.DrawBasis(_context, right, up, forward, tanX, tanY, width, height);

                var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                    GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
                    GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

                // GL counts rows from the bottom.
                bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
                return bitmap;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("Offscreen sky render failed: " + exception.Message);
                return null;
            }
        }

        /// <summary>A colour target of exactly this size; the sky writes no depth, so none is kept.</summary>
        private void EnsureTarget(int width, int height)
        {
            if (_framebuffer != 0 && _width == width && _height == height)
                return;

            if (_framebuffer == 0)
            {
                _framebuffer = GL.GenFramebuffer();
                _colour = GL.GenRenderbuffer();
            }

            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _colour);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, height);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                RenderbufferTarget.Renderbuffer, _colour);

            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);

            if (status != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Sky framebuffer incomplete: " + status);

            _width = width;
            _height = height;
        }

        public void Dispose()
        {
            try
            {
                if (_context != null && _window != null)
                {
                    _context.MakeCurrent(_window.WindowInfo);

                    if (_framebuffer != 0)
                    {
                        GL.DeleteFramebuffer(_framebuffer);
                        GL.DeleteRenderbuffer(_colour);
                    }
                }
            }
            catch (Exception)
            {
                // Tearing down a context that never finished starting.
            }

            _framebuffer = 0;
            _colour = 0;

            _context?.Dispose();
            _context = null;

            _window?.Dispose();
            _window = null;
        }

        /// <summary>Frees a pass's GL objects in this renderer's context.</summary>
        public void Release(SkyboxPass pass)
        {
            if (pass == null || _context == null)
                return;

            try
            {
                _context.MakeCurrent(_window.WindowInfo);
                pass.Dispose(_context);
            }
            catch (Exception)
            {
            }
        }
    }
}
