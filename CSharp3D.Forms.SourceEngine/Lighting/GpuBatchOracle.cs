using System;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// An <see cref="IRayOracle"/> that answers a whole face's visibility from one compute
    /// dispatch instead of one BVH descent per question.
    ///
    /// The trick is that it runs the lighting twice. A batched tracer needs every ray up
    /// front, but the rays are decided deep inside the sample functions — so rather than
    /// restructuring the hottest, most VRAD-faithful code in the baker into a
    /// collect-then-shade shape, this replays it:
    ///
    ///   1. RECORD  — the bake runs with this oracle answering a fixed value. Every query
    ///                is appended to a ray batch. The light values it computes are thrown
    ///                away; only the ray set matters.
    ///   2. DISPATCH— the whole batch is traced in one compute dispatch.
    ///   3. REPLAY  — the bake runs again over the same luxels. The queries come in the
    ///                same order, so each is answered from the batch by a cursor.
    ///
    /// That the order matches is not luck: no occlusion result in the direct pass decides
    /// whether another ray is cast. Every shadow ray is the last thing its sample function
    /// does, and the sky and ambient-occlusion loops step a fixed direction table. The
    /// lighting is therefore deterministic given the luxel, and the second pass asks for
    /// exactly what the first recorded. Debug builds check that claim per query rather than
    /// trusting it — a drift would otherwise show as shadows shifted by one ray, which is
    /// the kind of thing that survives review.
    ///
    /// The doubled lighting arithmetic is the price. It is a fraction of one BVH descent,
    /// which is what it buys back thousands of.
    ///
    /// Not safe for concurrent use — a cursor cannot be shared — hence
    /// <see cref="SupportsConcurrentUse"/> false, which makes the baker run the face's
    /// luxels serially and let the GPU supply the parallelism.
    /// </summary>
    public sealed class GpuBatchOracle : IRayOracle
    {
        private enum Phase
        {
            /// <summary>Between faces: queries are a mistake, so they trace honestly.</summary>
            Idle,
            Recording,
            Replaying,
        }

        private readonly GpuRayTracer _tracer;

        /// <summary>
        /// Who answers when this cannot: a declined batch, or a query outside a face. Set
        /// per face by the host, because it wraps that face's BVH.
        /// </summary>
        public IRayOracle Fallback;

        /// <summary>
        /// Milliseconds to yield after a dispatch, or 0. The bake and the viewport share one
        /// GPU; pausing here is what leaves the renderer gaps to draw in while the user is
        /// interacting. See <see cref="GpuBakeSession.InteractingThrottleMs"/>.
        /// </summary>
        public int ThrottleAfterDispatch;

        private Phase _phase = Phase.Idle;

        private float[] _rays = new float[GpuRayTracer.RayFloats * 4096];
        private byte[] _answers = new byte[4096];
        private int _count;
        private int _cursor;

        /// <summary>
        /// Ceiling on one face's batch. A face that wants more than this — a huge lightmap
        /// under many lights — is handed back to the CPU rather than split, because a split
        /// would have to re-run the lighting once per chunk and the win erodes fast.
        /// </summary>
        public int MaxRaysPerFace = 4 * 1024 * 1024;

        public GpuBatchOracle(GpuRayTracer tracer, IRayOracle fallback)
        {
            _tracer = tracer;
            Fallback = fallback;
        }

        /// <summary>Rays traced on the GPU since the last reset — for the status line.</summary>
        public long RaysTraced { get; private set; }

        /// <summary>Faces handed back to the CPU because the batch would not fit or failed.</summary>
        public long FacesDeclined { get; private set; }

        public bool SupportsConcurrentUse { get { return false; } }

        /// <summary>Start collecting a face's rays. Discards anything left from before.</summary>
        public void BeginRecording()
        {
            _phase = Phase.Recording;
            _count = 0;
            _cursor = 0;
        }

        /// <summary>
        /// Trace what was recorded and switch to answering from it. False means this face
        /// has to be baked on the CPU instead — too many rays, or the dispatch failed.
        /// </summary>
        public bool Resolve()
        {
            if (_phase != Phase.Recording)
                return false;

            if (_count == 0)
            {
                // A face with no visibility questions at all: nothing to trace, and replay
                // will ask nothing either.
                _phase = Phase.Replaying;
                _cursor = 0;
                return true;
            }

            if (_count > MaxRaysPerFace || !_tracer.Trace(_rays, _count, _answers))
            {
                _phase = Phase.Idle;
                FacesDeclined++;
                return false;
            }

            RaysTraced += _count;

            // Yield the GPU briefly so the viewport can draw between batches. After the
            // readback, not before: the answers are already in hand, so this costs the bake
            // latency and nothing else.
            if (ThrottleAfterDispatch > 0)
                System.Threading.Thread.Sleep(ThrottleAfterDispatch);

            _phase = Phase.Replaying;
            _cursor = 0;
            return true;
        }

        /// <summary>Done with this face; further queries trace honestly until the next Begin.</summary>
        public void EndFace()
        {
            _phase = Phase.Idle;
        }

        // ==================== IRayOracle ====================

        public bool AnyHit(Vector3 origin, Vector3 direction, float tmax, int skipId,
                           RayTriangleFlags ignoreFlags)
        {
            return Query(origin, direction, tmax, skipId, GpuRayTracer.ModeAnyHit, ignoreFlags,
                recordedAnswer: false);
        }

        public bool ReachesSky(Vector3 origin, Vector3 direction, float tmax)
        {
            // NoSkip, so the shader's skip test can never fire — the CPU's ClosestHit has
            // no skip either, and the two must agree ray for ray.
            return Query(origin, direction, tmax, NoSkip, GpuRayTracer.ModeReachesSky,
                RayTriangleFlags.None, recordedAnswer: true);
        }

        /// <summary>
        /// A skip id no triangle can carry. <see cref="RayBvh.AnyHit"/> uses the same value
        /// as its default for "skip nothing".
        /// </summary>
        private const int NoSkip = int.MinValue;

        /// <param name="recordedAnswer">
        /// What to answer during recording. Any fixed value works — the pass's light values
        /// are discarded — but answering "unoccluded" and "sky visible" keeps the recording
        /// pass on the same branches the replay will take, so its cost matches too.
        /// </param>
        private bool Query(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                           RayTriangleFlags ignoreFlags, bool recordedAnswer)
        {
            switch (_phase)
            {
                case Phase.Recording:
                    Append(origin, direction, tmax, skipId, mode, ignoreFlags);
                    return recordedAnswer;

                case Phase.Replaying:
                    return Replay(origin, direction, tmax, skipId, mode, ignoreFlags);

                default:
                    // Outside a face, or after a declined Resolve: the honest answer.
                    return mode == GpuRayTracer.ModeAnyHit
                        ? Fallback.AnyHit(origin, direction, tmax, skipId, ignoreFlags)
                        : Fallback.ReachesSky(origin, direction, tmax);
            }
        }

        private void Append(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                            RayTriangleFlags ignoreFlags)
        {
            // Past the ceiling there is no point recording further: Resolve will decline the
            // face anyway, and the arrays would keep doubling for nothing.
            if (_count >= MaxRaysPerFace)
            {
                _count++;
                return;
            }

            EnsureCapacity(_count + 1);
            GpuRayTracer.PackRay(_rays, _count, origin, direction, tmax, skipId, mode, ignoreFlags);
            _count++;
        }

        private bool Replay(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                            RayTriangleFlags ignoreFlags)
        {
            // More queries than were recorded means the two passes diverged, which would
            // silently mis-shadow from here on. Fall back for the rest of the face instead.
            if (_cursor >= _count)
            {
                _phase = Phase.Idle;
                FacesDeclined++;

                return mode == GpuRayTracer.ModeAnyHit
                    ? Fallback.AnyHit(origin, direction, tmax, skipId, ignoreFlags)
                    : Fallback.ReachesSky(origin, direction, tmax);
            }

#if DEBUG
            VerifyRecordedRay(origin, direction, tmax, skipId, mode, ignoreFlags);
#endif

            byte answer = _answers[_cursor];
            _cursor++;

            return answer != 0;
        }

#if DEBUG
        /// <summary>
        /// Assert that the query being replayed is the one recorded in this slot. The whole
        /// design rests on the two passes asking the same things in the same order; this is
        /// where that assumption gets tested rather than assumed.
        /// </summary>
        private void VerifyRecordedRay(Vector3 origin, Vector3 direction, float tmax, int skipId,
                                       int mode, RayTriangleFlags ignoreFlags)
        {
            int at = _cursor * GpuRayTracer.RayFloats;

            bool same = _rays[at + 0] == origin.X
                     && _rays[at + 1] == origin.Y
                     && _rays[at + 2] == origin.Z
                     && _rays[at + 3] == tmax
                     && _rays[at + 4] == direction.X
                     && _rays[at + 5] == direction.Y
                     && _rays[at + 6] == direction.Z;

            System.Diagnostics.Debug.Assert(same,
                "GpuBatchOracle replay diverged from the recording pass at ray " + _cursor
                + ": the two passes must ask the same questions in the same order.");
        }
#endif

        private void EnsureCapacity(int rays)
        {
            if (_answers.Length >= rays)
                return;

            int capacity = _answers.Length;

            while (capacity < rays)
                capacity *= 2;

            Array.Resize(ref _rays, capacity * GpuRayTracer.RayFloats);
            Array.Resize(ref _answers, capacity);
        }
    }
}
