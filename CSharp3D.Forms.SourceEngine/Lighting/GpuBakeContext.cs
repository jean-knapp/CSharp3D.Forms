using System;
using OpenTK.Graphics;
using OpenTK.Platform;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// A GL context of the bake's own, current on the baker's worker thread.
    ///
    /// Why a second context rather than the renderer's: a context can only be current on
    /// one thread at a time, and the renderer's belongs to the UI thread for the whole life
    /// of the control. Borrowing it would mean marshalling every dispatch through the UI
    /// thread and back — which also caps the bake at the display refresh rate, and on a map
    /// of many small faces that is slower than just tracing on the CPU.
    ///
    /// It deliberately does NOT share objects with the renderer. The tracer only ever
    /// touches buffers and a program of its own, and results come back as plain bytes, so
    /// there is nothing to share — and not sharing removes every question about who may
    /// touch which object from where.
    ///
    /// Creation is allowed to fail. A driver that will not give a second context, or a
    /// machine below 4.3, is a normal outcome answered by staying on the CPU — never by
    /// taking the editor down.
    /// </summary>
    public sealed class GpuBakeContext : IDisposable
    {
        private IWindowInfo _window;
        private IGraphicsContext _context;

        /// <summary>Null while usable; otherwise why the bake has no context.</summary>
        public string FailureReason { get; private set; }

        public bool IsCurrent { get; private set; }

        /// <summary>
        /// Build a context on <paramref name="windowHandle"/> and make it current on the
        /// CALLING thread — so this must be called from the thread that will dispatch.
        ///
        /// The handle is any window the host keeps alive for the duration; a hidden 1x1
        /// control is the usual answer. It is only ever a drawable to hang the context off,
        /// never presented to — the bake reads its results back and never swaps buffers.
        /// </summary>
        public bool MakeCurrent(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                FailureReason = "no window to create a bake context on";
                return false;
            }

            try
            {
                _window = Utilities.CreateWindowsWindowInfo(windowHandle);

                // Sharing off: nothing here is reachable from the render context, and a
                // shared context would drag in questions about cross-thread object
                // lifetimes that this design does not need to answer.
                _context = new GraphicsContext(GraphicsMode.Default, _window, 4, 3,
                    GraphicsContextFlags.Default);

                _context.MakeCurrent(_window);
                _context.LoadAll();

                IsCurrent = true;
                return true;
            }
            catch (Exception ex)
            {
                FailureReason = "could not create a bake context: " + ex.Message;
                Release();
                return false;
            }
        }

        /// <summary>
        /// Drop the context. MUST run on the thread that made it current, after whatever
        /// owns GL objects on it has deleted them.
        /// </summary>
        public void Dispose()
        {
            Release();
        }

        private void Release()
        {
            IsCurrent = false;

            try
            {
                if (_context != null)
                {
                    _context.Dispose();
                    _context = null;
                }
            }
            catch (Exception)
            {
                _context = null;
            }

            _window = null;
        }
    }
}
