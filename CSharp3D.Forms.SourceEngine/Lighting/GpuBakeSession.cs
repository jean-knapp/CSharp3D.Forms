using System;
using System.Threading;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// Wires a <see cref="LightmapBaker"/> to the GPU: owns the bake context, the tracer and
    /// the oracle, and answers the baker's per-face request for someone to trace its rays.
    ///
    /// Attach it and the bake uses the GPU where it can. Every failure mode — no 4.3, no
    /// second context, a shader that will not compile, a face too big for one batch, a
    /// dispatch that errors — resolves to "that face traces on the CPU", so the worst case
    /// is the speed the baker had before rather than a broken preview. That containment is
    /// the whole reason this is safe to attempt on unknown hardware.
    /// </summary>
    public sealed class GpuBakeSession
    {
        private readonly LightmapBaker _baker;
        private readonly Func<IntPtr> _windowHandle;

        private GpuBakeContext _context;
        private GpuRayTracer _tracer;
        private GpuBatchOracle _oracle;

        /// <summary>Set once the worker has tried to start the GPU, either way.</summary>
        private volatile bool _resolved;

        private volatile string _failureReason;

        /// <summary>
        /// Whether the user is touching the editor right now. Drives how hard the bake is
        /// allowed to lean on the GPU — see <see cref="ThrottleAfterDispatch"/>.
        /// </summary>
        private volatile bool _interacting;

        /// <param name="windowHandle">
        /// Supplies the drawable to build the bake context on, read from the worker thread.
        /// A function rather than a value so the host can create the window lazily and
        /// answer IntPtr.Zero until its handle exists.
        /// </param>
        public GpuBakeSession(LightmapBaker baker, Func<IntPtr> windowHandle)
        {
            _baker = baker;
            _windowHandle = windowHandle;

            _baker.WorkerStarted += Start;
            _baker.WorkerStopping += Stop;
            _baker.OracleFactory = CreateOracle;
        }

        /// <summary>
        /// What the GPU path is doing, for the status bar: "gpu" once it is tracing, or why
        /// it is not.
        /// </summary>
        public string Status
        {
            get
            {
                if (!_resolved)
                    return "gpu: starting";

                if (_failureReason != null)
                    return "cpu: " + _failureReason;

                GpuBatchOracle oracle = _oracle;

                if (oracle == null)
                    return "cpu";

                long declined = oracle.FacesDeclined;

                return declined > 0
                    ? "gpu (" + declined + " faces on cpu)"
                    : "gpu";
            }
        }

        /// <summary>
        /// Tell the session the user is (or is not) interacting. The bake keeps running
        /// either way; what changes is how much of the GPU it takes while the viewport is
        /// also trying to draw.
        /// </summary>
        public void SetInteracting(bool interacting)
        {
            _interacting = interacting;
        }

        // ==================== worker-thread lifecycle ====================

        private void Start()
        {
            GpuBakeContext context = new GpuBakeContext();

            if (!context.MakeCurrent(_windowHandle()))
            {
                _failureReason = context.FailureReason;
                _resolved = true;
                return;
            }

            GpuRayTracer tracer = new GpuRayTracer();

            if (!tracer.Initialize(context))
            {
                _failureReason = tracer.FailureReason;
                context.Dispose();
                _resolved = true;
                return;
            }

            _context = context;
            _tracer = tracer;
            _oracle = new GpuBatchOracle(tracer, null);
            _resolved = true;
        }

        private void Stop()
        {
            // Order matters: the buffers belong to the context, so they have to go first.
            if (_tracer != null)
            {
                _tracer.Dispose();
                _tracer = null;
            }

            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }

            _oracle = null;
        }

        // ==================== per-face oracle ====================

        /// <summary>
        /// The baker asking who should trace this face. Runs on the worker thread, with the
        /// bake context current.
        /// </summary>
        private IRayOracle CreateOracle(RayBvh bvh)
        {
            GpuRayTracer tracer = _tracer;
            GpuBatchOracle oracle = _oracle;

            if (tracer == null || oracle == null || bvh == null || bvh.TriangleCount == 0)
                return null;

            // Uploading is a no-op once the BVH is the one already there, so this is the
            // cheap way to keep the GPU's copy in step with a scene that changed.
            if (!tracer.UploadScene(bvh))
                return null;

            oracle.Fallback = new BvhRayOracle(bvh);
            oracle.ThrottleAfterDispatch = _interacting ? InteractingThrottleMs : 0;

            return oracle;
        }

        /// <summary>
        /// Milliseconds the worker pauses after each dispatch while the user is interacting.
        ///
        /// The bake and the viewport share one GPU, and a dispatch that saturates it delays
        /// the next frame however cheap it was to submit. Yielding briefly between batches
        /// leaves gaps the renderer can draw in — the bake converges slower exactly while
        /// someone is watching it move, which is the trade the adaptive budget is for.
        /// </summary>
        public int InteractingThrottleMs = 4;
    }
}
