using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// Traces the rays of many faces on the GPU in a few wide dispatches.
    ///
    /// Each span of a face's luxels asks its visibility questions through its own
    /// <see cref="RayRecording"/>, which runs the span's lighting twice — once to record every ray, once to replay the
    /// answers (see there for why the two passes ask the same things). Recording and
    /// replaying are plain CPU work and run in parallel across spans on pool threads; this
    /// class does the part in between, on the one thread the GL context is current on.
    ///
    /// Batching across faces is what the GPU path needs. Traced one face at a time, every
    /// face paid a submit, a wait and a synchronous readback, and a single thread did all of
    /// that plus both lighting passes — the GPU sat idle most of the bake and the CPU path,
    /// using every core, kept up with it. Concatenating faces into dispatches of up to
    /// <see cref="MaxRaysPerDispatch"/> rays leaves the per-dispatch cost spread over many
    /// faces and the lighting passes on all the cores.
    /// </summary>
    public sealed class GpuBatchOracle
    {
        private readonly GpuRayTracer _tracer;

        /// <summary>
        /// Who answers for a span the GPU declined. Set per bake by the host, because it
        /// wraps that bake's BVH.
        /// </summary>
        public IRayOracle Fallback;

        /// <summary>
        /// Milliseconds to yield after a dispatch, or 0. The bake and the viewport share one
        /// GPU; pausing here is what leaves the renderer gaps to draw in while the user is
        /// interacting. See <see cref="GpuBakeSession.InteractingThrottleMs"/>.
        /// </summary>
        public int ThrottleAfterDispatch;

        /// <summary>
        /// Rays per dispatch. Large enough that the submit-and-read-back cost is noise, small
        /// enough that the staging copy (48 bytes a ray) stays a few tens of megabytes and a
        /// dispatch never holds the GPU long enough to stall the viewport visibly.
        /// </summary>
        public int MaxRaysPerDispatch = 1024 * 1024;

        /// <summary>
        /// Ceiling on one recording. A span that wants more than this — luxels under
        /// hundreds of lights — is handed back to the CPU, since its recording alone would
        /// hold hundreds of megabytes.
        /// </summary>
        public int MaxRaysPerRecording = 4 * 1024 * 1024;

        /// <summary>
        /// Recycled recording arrays. A span's recording is a few megabytes, so each new one
        /// landed on the large object heap, and a bake made thousands: the gen-2 collections
        /// that followed paused the viewport as well as the bake.
        /// </summary>
        private readonly RayArrayPool _rayArrays = new RayArrayPool();

        /// <summary>Where <see cref="ClosestStaging"/> callers pack their rays; made on first use.</summary>
        private float[] _staging;

        public GpuBatchOracle(GpuRayTracer tracer, IRayOracle fallback)
        {
            _tracer = tracer;
            Fallback = fallback;
        }

        /// <summary>Rays traced on the GPU since the last reset — for the status line.</summary>
        public long RaysTraced { get; private set; }

        /// <summary>Luxel spans handed back to the CPU because they would not fit or a dispatch failed.</summary>
        public long SpansDeclined { get; private set; }

        /// <summary>
        /// A recorder for one span of luxels, answering from this tracer once resolved, with
        /// room for <paramref name="expectedRays"/> before it has to grow.
        /// </summary>
        public RayRecording NewRecording(int expectedRays)
        {
            return new RayRecording(MaxRaysPerRecording, Fallback, expectedRays, _rayArrays);
        }

        /// <summary>
        /// Trace every recording and switch each to replaying. A recording the GPU could not
        /// serve — over the ceiling, or in a dispatch that failed — is switched to its CPU
        /// fallback instead, so a GPU that gives up degrades rather than drops faces.
        ///
        /// Must run on the thread the tracer's context is current on.
        /// </summary>
        public void TraceAll(IList<RayRecording> recordings, Func<bool> cancelled)
        {
            int first = 0;

            while (first < recordings.Count)
            {
                if (cancelled())
                    return;

                // Fill one dispatch with whole recordings. One larger than a dispatch goes
                // alone and is traced in slices.
                int total = 0;
                int end = first;

                while (end < recordings.Count)
                {
                    RayRecording recording = recordings[end];

                    if (!recording.Traceable)
                    {
                        end++;
                        continue;
                    }

                    if (total > 0 && total + recording.Count > MaxRaysPerDispatch)
                        break;

                    total += recording.Count;
                    end++;
                }

                if (total > 0)
                    TraceRange(recordings, first, end, total);

                // Whatever the dispatch did not answer — nothing to trace, over the ceiling,
                // or a failed dispatch — answers from the CPU.
                for (int i = first; i < end; i++)
                {
                    RayRecording recording = recordings[i];

                    if (!recording.Pending)
                        continue;

                    if (recording.Count > 0)
                        SpansDeclined++;

                    recording.UseFallback();
                }

                first = end;
            }
        }

        /// <summary>
        /// Trace a run of recordings in one dispatch and hand each its answers. On failure
        /// they are left pending, for the caller to send to the CPU.
        /// </summary>
        private void TraceRange(IList<RayRecording> recordings, int first, int end, int total)
        {
            // Fresh for each run rather than reused: the baker replays one run while this
            // traces the next, and the replaying recordings read their answers from here.
            byte[] answers = new byte[total];

            if (total <= MaxRaysPerDispatch)
            {
                // Straight from each recording to its place in the dispatch, with no copy
                // into one staging array first.
                if (!_tracer.BeginUpload(total))
                    return;

                int offset = 0;

                for (int i = first; i < end; i++)
                {
                    RayRecording recording = recordings[i];

                    if (!recording.Traceable)
                        continue;

                    _tracer.Upload(recording.Rays, recording.Count, offset);
                    offset += recording.Count;
                }

                if (!_tracer.TraceUploaded(total, answers, 0))
                    return;

                Throttle();
            }
            else
            {
                // Only one recording makes a run this long (see TraceAll): trace it in
                // dispatch-sized slices.
                RayRecording single = null;

                for (int i = first; i < end && single == null; i++)
                {
                    if (recordings[i].Traceable)
                        single = recordings[i];
                }

                for (int done = 0; done < total; done += MaxRaysPerDispatch)
                {
                    if (!_tracer.Trace(single.Rays, done, Math.Min(MaxRaysPerDispatch, total - done), answers, done))
                        return;

                    Throttle();
                }
            }

            RaysTraced += total;
            int at = 0;

            for (int i = first; i < end; i++)
            {
                RayRecording recording = recordings[i];

                if (!recording.Traceable)
                    continue;

                recording.Answer(answers, at);
                at += recording.Count;
            }
        }

        /// <summary>
        /// Yield the GPU briefly so the viewport can draw between batches. After the readback,
        /// not before: the answers are already in hand, so this costs the bake latency and
        /// nothing else.
        /// </summary>
        private void Throttle()
        {
            if (ThrottleAfterDispatch > 0)
                System.Threading.Thread.Sleep(ThrottleAfterDispatch);
        }
        /// <summary>
        /// Where to pack <paramref name="count"/> rays, with <see cref="GpuRayTracer.PackRay"/>,
        /// for <see cref="TraceClosest"/> — or null for more than one staging may hold. Valid
        /// until the next call that traces.
        ///
        /// For callers whose rays are known up front, as the bounce gather's are: they need
        /// no recording pass, only somewhere to put them.
        /// </summary>
        public float[] ClosestStaging(int count)
        {
            return EnsureStaging(count) ? _staging : null;
        }

        /// <summary>
        /// Trace the <paramref name="count"/> rays packed into <see cref="ClosestStaging"/>
        /// for their nearest face and distance (see <see cref="GpuRayTracer.TraceClosest"/>).
        /// False when a dispatch failed; the caller then traces on the CPU.
        /// </summary>
        public bool TraceClosest(int count, int[] faces, float[] distances)
        {
            for (int done = 0; done < count; done += MaxRaysPerDispatch)
            {
                int slice = Math.Min(MaxRaysPerDispatch, count - done);

                if (!_tracer.TraceClosest(_staging, done, slice, faces, distances, done))
                    return false;

                Throttle();
            }

            RaysTraced += count;
            return true;
        }

        /// <summary>
        /// Room for a run of rays, or false for one too large to stage — which then traces on
        /// the CPU rather than allocating without bound.
        /// </summary>
        private bool EnsureStaging(int rays)
        {
            if (rays > Math.Max(MaxRaysPerDispatch, MaxRaysPerRecording))
                return false;

            if (_staging != null && _staging.Length >= rays * GpuRayTracer.RayFloats)
                return true;

            int capacity = 4096;

            while (capacity < rays)
                capacity *= 2;

            _staging = new float[capacity * GpuRayTracer.RayFloats];
            return true;
        }
    }

    /// <summary>
    /// One span of a face's visibility questions, recorded so they can be traced in a batch
    /// and then answered in order.
    ///
    /// The trick is that the span's lighting runs twice. A batched tracer needs every ray up
    /// front, but the rays are decided deep inside the sample functions — so rather than
    /// restructuring the hottest, most VRAD-faithful code in the baker into a
    /// collect-then-shade shape, this replays it:
    ///
    ///   1. RECORD  — the bake runs with this oracle answering a fixed value. Every query
    ///                is appended to the recording. The light values it computes are thrown
    ///                away; only the ray set matters.
    ///   2. TRACE   — <see cref="GpuBatchOracle.TraceAll"/> traces many spans' recordings
    ///                in a few dispatches.
    ///   3. REPLAY  — the bake runs again over the same luxels. The queries come in the
    ///                same order, so each is answered from the recording by a cursor.
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
    /// <see cref="SupportsConcurrentUse"/> false, which makes the baker run the span's
    /// luxels serially. The parallelism is across spans, each with its own recording.
    /// </summary>
    public sealed class RayRecording : IRayOracle
    {
        private enum Phase
        {
            Recording,
            Replaying,

            /// <summary>Declined, failed or diverged: every query traces honestly.</summary>
            Fallback,
        }

        private readonly int _maxRays;
        private readonly IRayOracle _fallback;
        private readonly RayArrayPool _pool;

        private Phase _phase = Phase.Recording;

        private float[] _rays;
        private byte[] _answers;
        private int _answerOffset;
        private int _count;
        private int _cursor;

        internal RayRecording(int maxRays, IRayOracle fallback, int expectedRays, RayArrayPool pool)
        {
            _maxRays = maxRays;
            _fallback = fallback;
            _pool = pool;
            _rays = pool.Rent(Math.Max(16, Math.Min(expectedRays, maxRays)));
        }

        public bool SupportsConcurrentUse { get { return false; } }

        /// <summary>Rays recorded, including any past the ceiling.</summary>
        public int Count { get { return _count; } }

        internal float[] Rays { get { return _rays; } }

        /// <summary>Recorded, within the ceiling and with something to trace.</summary>
        internal bool Traceable
        {
            get { return _phase == Phase.Recording && _count > 0 && _count <= _maxRays; }
        }

        /// <summary>Still recording: neither answered nor sent to the fallback.</summary>
        internal bool Pending
        {
            get { return _phase == Phase.Recording; }
        }

        /// <summary>Install the traced answers and switch to replaying.</summary>
        internal void Answer(byte[] answers, int offset)
        {
            _answers = answers;
            _answerOffset = offset;
            _phase = Phase.Replaying;
            _cursor = 0;

            // The rays are spent once answered; the replay check reads them only in Debug.
#if !DEBUG
            Release();
#endif
        }

        /// <summary>
        /// Give the recorded rays back for reuse. The baker calls this once a span is
        /// replayed; a recording that was answered or declined may already have.
        /// </summary>
        public void Release()
        {
            float[] rays = _rays;
            _rays = null;

            if (rays != null)
                _pool.Return(rays);
        }

        /// <summary>
        /// Answer honestly from the fallback — for a span with nothing to trace this is also
        /// right, since its replay asks nothing.
        /// </summary>
        internal void UseFallback()
        {
            Release();

            if (_count == 0)
            {
                _phase = Phase.Replaying;
                _cursor = 0;
                return;
            }

            _phase = Phase.Fallback;
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
                    return Honest(origin, direction, tmax, skipId, mode, ignoreFlags);
            }
        }

        private bool Honest(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                            RayTriangleFlags ignoreFlags)
        {
            return mode == GpuRayTracer.ModeAnyHit
                ? _fallback.AnyHit(origin, direction, tmax, skipId, ignoreFlags)
                : _fallback.ReachesSky(origin, direction, tmax);
        }

        private void Append(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                            RayTriangleFlags ignoreFlags)
        {
            // Past the ceiling there is no point recording further: the span is declined
            // anyway, and the array would keep doubling for nothing.
            if (_count >= _maxRays)
            {
                _count++;
                return;
            }

            if ((_count + 1) * GpuRayTracer.RayFloats > _rays.Length)
            {
                float[] larger = _pool.Rent(_rays.Length / GpuRayTracer.RayFloats * 2);
                Array.Copy(_rays, larger, _count * GpuRayTracer.RayFloats);
                _pool.Return(_rays);
                _rays = larger;
            }

            GpuRayTracer.PackRay(_rays, _count, origin, direction, tmax, skipId, mode, ignoreFlags);
            _count++;
        }

        private bool Replay(Vector3 origin, Vector3 direction, float tmax, int skipId, int mode,
                            RayTriangleFlags ignoreFlags)
        {
            // More queries than were recorded means the two passes diverged, which would
            // silently mis-shadow from here on. Fall back for the rest of the span instead.
            if (_cursor >= _count)
            {
                _phase = Phase.Fallback;
                return Honest(origin, direction, tmax, skipId, mode, ignoreFlags);
            }

#if DEBUG
            VerifyRecordedRay(origin, direction, tmax);
#endif

            byte answer = _answers[_answerOffset + _cursor];
            _cursor++;

            return answer != 0;
        }

#if DEBUG
        /// <summary>
        /// Assert that the query being replayed is the one recorded in this slot. The whole
        /// design rests on the two passes asking the same things in the same order; this is
        /// where that assumption gets tested rather than assumed.
        /// </summary>
        private void VerifyRecordedRay(Vector3 origin, Vector3 direction, float tmax)
        {
            int at = _cursor * GpuRayTracer.RayFloats;

            bool same = _rays[at + 0] == origin.X
                     && _rays[at + 1] == origin.Y
                     && _rays[at + 2] == origin.Z
                     && _rays[at + 3] == tmax
                     && _rays[at + 4] == direction.X
                     && _rays[at + 5] == direction.Y
                     && _rays[at + 6] == direction.Z;

            // The message only when it fails: built eagerly it was a string concatenation
            // per replayed ray, a seventh of a Debug bake.
            if (!same)
                System.Diagnostics.Debug.Fail(
                    "RayRecording replay diverged from the recording pass at ray " + _cursor
                    + ": the two passes must ask the same questions in the same order.");
        }
#endif
    }

    /// <summary>
    /// Ray arrays for reuse, in size classes a quarter of a power of two apart, up to a bound
    /// on what it keeps: it smooths a bake's churn without holding a bake's peak forever.
    /// Classes, not exact sizes, so arrays are found again; quarter steps, not powers of two,
    /// so a recording is never more than a quarter larger than it asked for.
    /// </summary>
    internal sealed class RayArrayPool
    {
        /// <summary>Most floats held for reuse at once — 96 MB.</summary>
        private const long MaxRetainedFloats = 24L * 1024 * 1024;

        private readonly Stack<float[]>[] _classes = new Stack<float[]>[4 * 32];
        private long _retained;

        /// <summary>An array for at least <paramref name="rays"/> rays.</summary>
        public float[] Rent(int rays)
        {
            int size;
            int index = SizeClass(rays, out size);

            lock (_classes)
            {
                Stack<float[]> stack = _classes[index];

                if (stack != null && stack.Count > 0)
                {
                    float[] array = stack.Pop();
                    _retained -= array.Length;
                    return array;
                }
            }

            return new float[size * GpuRayTracer.RayFloats];
        }

        public void Return(float[] array)
        {
            int rays = array.Length / GpuRayTracer.RayFloats;
            int size;
            int index = SizeClass(rays, out size);

            // Only arrays this pool shaped: one of another size would come back out of a
            // class smaller than its callers expect.
            if (size != rays || size * GpuRayTracer.RayFloats != array.Length)
                return;

            lock (_classes)
            {
                if (_retained + array.Length > MaxRetainedFloats)
                    return;

                if (_classes[index] == null)
                    _classes[index] = new Stack<float[]>();

                _classes[index].Push(array);
                _retained += array.Length;
            }
        }

        /// <summary>
        /// The smallest class holding <paramref name="rays"/>: 2^k × 4/4, 5/4, 6/4 or 7/4,
        /// from 16 up.
        /// </summary>
        private static int SizeClass(int rays, out int size)
        {
            int k = 4;

            while ((2 << k) <= rays)
                k++;

            for (int quarter = 4; quarter < 8; quarter++)
            {
                size = (1 << k) / 4 * quarter;

                if (size >= rays)
                    return k * 4 + quarter - 4;
            }

            size = 2 << k;
            return (k + 1) * 4;
        }
    }
}