using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The progressive VRAD-style lightmap baker: direct lighting with real shadow rays
    /// (GatherSampleLightSSE ports), soft sun + sky-dome ambient (GatherSampleSkyLightSSE /
    /// GatherSampleAmbientSkySSE), and Monte Carlo radiosity bounces replacing vrad's
    /// stored transfer matrix — same integral, no matrix build.
    ///
    /// Scene changes are INCREMENTAL (<see cref="UpdateScene"/>): unchanged faces are
    /// passed in as the same objects and keep their baked state; only changed faces and
    /// faces whose shadows a change can actually reach (light-frustum / sun-sweep / sky
    /// proximity tests) are requeued. Everything anti-flicker follows from that plus:
    /// faces that already display a result skip the coarse pass (no blur flash), bounce
    /// state swaps in atomically (no brightness dip), and publishes that produce
    /// byte-identical textures are dropped (no redundant uploads). A "settle sweep"
    /// revalidates the whole map at final quality after edits go quiet, so the
    /// conservative requeue heuristics cannot leave permanent errors.
    /// </summary>
    public class LightmapBaker : IDisposable
    {
        // ---- tuning (ray counts per pass level; the final level comes from the settings) ----

        private static readonly int[] SunRaysCoarse = { 1, 8 };            // final: LightPreviewSettings.SamplesSun
        private static readonly int[] SkyRaysCoarse = { 24, 64 };          // final: LightPreviewSettings.SamplesAmbient
        private static readonly int[] AoRaysPerLevel = { 0, 8, 32 };       // CalculateAmbientOcclusion4 = 32
        private static readonly float[] LuxelCoarsen = { 4.0f, 1.0f, 1.0f };

        // ---- light model, from LightPreviewSettings (ApplySettings) ----
        //
        // The defaults below are CS:GO VRAD's, which is what this baker always did before it
        // took settings; the editor replaces them with the target game's before baking.

        private volatile int _sunRaysFinal = 30;       // NSAMPLES_SUN_AREA_LIGHT
        private volatile int _skyRaysFinal = 162;      // NUMVERTEXNORMALS
        private volatile bool _softenCosine = true;
        private float _aoScale = 1.0f;
        private float _brightnessScale = 1.0f;
        private float _radiusScale = 1.0f;

        /// <summary>Bounce iterations used while <see cref="LightPreviewSettings.Bounced"/> is on.</summary>
        private const int BounceIterationsOn = 2;

        private int SunRays(int level)
        {
            int final = _sunRaysFinal;
            return level < SunRaysCoarse.Length ? Math.Min(SunRaysCoarse[level], final) : final;
        }

        private int SkyRays(int level)
        {
            int final = _skyRaysFinal;
            return level < SkyRaysCoarse.Length ? Math.Min(SkyRaysCoarse[level], final) : final;
        }

        private const int Levels = 3;

        /// <summary>Patch size multiplier over lightmapscale (16 × 4 = vrad chop 64).</summary>
        private const float PatchCoarsen = 4.0f;

        /// <summary>
        /// Cosine-hemisphere rays per patch per bounce gather iteration.
        ///
        /// The directions are a fixed set shared by every patch on a face
        /// (<see cref="SphereDirections.CosineHemisphereTable"/>), which turns what would
        /// be per-patch sampling noise into a smooth, structured error — but the
        /// structure only disappears once there are enough directions to resolve the
        /// surroundings. 32 left visible lobes; 128 is flat against a 256-ray reference.
        /// Patches are 4× coarser than luxels in each axis, so this is ~8 rays per
        /// luxel-equivalent and stays cheap next to direct lighting.
        /// </summary>
        public int BounceRaysPerPatch = 64;

        /// <summary>
        /// Rays per patch for the FIRST bounce after an edit. Indirect light shows up
        /// roughly 4× sooner at this quality; if the scene then stays quiet the baker
        /// re-solves at <see cref="BounceRaysPerPatch"/> and swaps the result in
        /// atomically, so the lobes this leaves are only ever on screen briefly.
        /// </summary>
        public int BounceCoarseRaysPerPatch = 32;

        /// <summary>
        /// How many times the [1 2 1] tent is applied across the patch grid before the
        /// indirect light is composited (see <see cref="SmoothPatches"/>). Two passes is
        /// a Gaussian of roughly one patch, ~64 world units at the default lightmapscale
        /// — comparable to the reach of vrad's own <c>BuildPatchRadial</c> blend, and
        /// enough to erase the angular banding a finite direction set leaves behind
        /// without flattening real colour bleed.
        /// </summary>
        public int BounceSmoothPasses = 3;

        /// <summary>CalculateAmbientOcclusion4's fixed 36-unit ray length.</summary>
        private const float AoRayLength = 36.0f;

        /// <summary>Bounce iterations (vrad -bounce, but each is a full MC gather). 0 = off.</summary>
        public int BounceIterations = 2;

        /// <summary>How often at most the ResultsAvailable event fires, ms.</summary>
        public int PublishIntervalMs = 40;

        /// <summary>
        /// The bounce solve only starts after the scene has been quiet this long, so a
        /// drag doesn't waste time starting gathers it will abort.
        /// </summary>
        public int BounceQuietMs = 400;

        /// <summary>
        /// Quiet time before the REGIONAL settle sweep: a final-quality recompute of the
        /// faces around everything that changed since the last sweep, correcting whatever
        /// the incremental requeue heuristics missed near the edit.
        /// </summary>
        public int SweepQuietMs = 1500;

        /// <summary>
        /// Quiet time before the FULL revalidation — every face at final quality plus an
        /// authoritative whole-map bounce solve. This is the "full compile in the
        /// background": it runs once per burst of editing, on the worker threads, and
        /// because recomputes are deterministic and byte-identical publishes are dropped,
        /// the only thing it ever changes on screen is a face the cheap paths got wrong.
        /// </summary>
        public int FullSweepQuietMs = 4000;

        /// <summary>
        /// How far beyond the changed region a regional pass reaches, in world units (or
        /// 2× the region's own size, whichever is larger). Indirect light and
        /// sampling-granularity shadow errors are local; anything further is left to the
        /// full revalidation.
        /// </summary>
        public float RegionPad = 512.0f;

        // ---- state (all guarded by _gate unless noted) ----

        private readonly object _gate = new object();
        private List<BakeFace> _faces = new List<BakeFace>();

        /// <summary>
        /// Shadow-only geometry that is not a face — static props. In the BVH with
        /// <see cref="OccluderId"/>, so it blocks light and bounce rays but never matches a
        /// face's self-shadow skip id and is never read as a bounce emitter (Hammer++ gives
        /// props zero reflectivity the same way).
        /// </summary>
        private List<RayTriangle> _occluders = new List<RayTriangle>();

        /// <summary>
        /// Triangle id of the <see cref="_occluders"/> entries: outside any face index. Ids
        /// below it are occluders too, and let a <see cref="LightProbe"/> skip its own prop.
        /// </summary>
        public const int OccluderId = -2;

        /// <summary>Probes to compute ambient cubes at (props), and the scene state they were computed for.</summary>
        private List<LightProbe> _probes = new List<LightProbe>();
        private int _probeGeneration = -1;
        private bool _probesDirty;

        /// <summary>
        /// Replace the probe list — one per lit model. Probes are recomputed after every
        /// scene, light or bounce change once the face work has drained; a probe whose
        /// position did not change keeps showing its previous cube until then.
        /// </summary>
        public void SetProbes(List<LightProbe> probes)
        {
            lock (_gate)
            {
                _probes = probes ?? new List<LightProbe>();
                _probesDirty = true;
            }

            _wake.Set();
        }

        /// <summary>
        /// The "skip nothing" id for shadow rays. Displacement luxels pass it instead of their
        /// face index: a displacement's own triangles must be able to shadow it (a hill
        /// shading the valley behind it), which skipping the face id would prevent.
        /// </summary>
        private const int NoSkip = int.MinValue;

        /// <summary>Lights that came from entities — the list callers hand us.</summary>
        private List<SourceLight> _entityLights = new List<SourceLight>();

        /// <summary>emit_surface lights derived from emissive faces (see BuildSurfaceLights).</summary>
        private List<SourceLight> _surfaceLights = new List<SourceLight>();

        /// <summary>Entity + surface lights: what the bake actually loops over.</summary>
        private List<SourceLight> _lights = new List<SourceLight>();

        /// <summary>Geometry or materials changed → the surface lights must be rederived.</summary>
        private bool _surfaceLightsDirty;

        private RayBvh _bvh;

        /// <summary>
        /// The live BVH no longer matches the geometry and a rebuild is owed. The old one
        /// is deliberately KEPT while that happens: a rebuild over a whole map can easily
        /// outlast the editor's update throttle, and dropping the BVH for its duration
        /// meant that during a drag the baker spent every slice rebuilding and never
        /// baked anything at all — the "it's working in the background but nothing
        /// appears" symptom. Baking against a one-edit-stale BVH costs a slightly wrong
        /// shadow for a fraction of a second; the regional sweep trues it up.
        /// </summary>
        private bool _bvhStale;

        /// <summary>
        /// Bumped whenever the face list's identity or order changes. Ray hits carry the
        /// face's list INDEX, so a BVH may only outlive a scene update that left those
        /// indices meaning the same faces — true for a move (same faces, new positions),
        /// false when brushes are created or deleted, where the old BVH is dropped.
        /// </summary>
        private int _topology;

        private int _bvhTopology = -1;

        private int _generation;         // bumped by any scene/light change → abort in-flight work
        private bool _bounceDirty;
        private bool _bounceRunning;

        /// <summary>
        /// The bounce needs re-solving over the whole map rather than just
        /// <see cref="_bounceMin"/>..<see cref="_bounceMax"/> — set on a full scene load,
        /// by a change to a global light (sun/sky), and once per edit burst by the full
        /// revalidation stage.
        /// </summary>
        private bool _bounceAll;

        private Vector3 _bounceMin = new Vector3(float.MaxValue);
        private Vector3 _bounceMax = new Vector3(float.MinValue);

        /// <summary>Union of everything that changed since the last regional sweep.</summary>
        private Vector3 _dirtyMin = new Vector3(float.MaxValue);
        private Vector3 _dirtyMax = new Vector3(float.MinValue);

        /// <summary>
        /// Whether the fast first bounce has already run for the current edit, i.e. the
        /// next bounce should be the full-quality one. Reset only by real scene/light
        /// changes — after a settle sweep the display is already good, so that re-solve
        /// goes straight to full quality instead of dropping back to the lobed version.
        /// </summary>
        private bool _bounceCoarseDone;

        /// <summary>
        /// Number of publishes that actually swapped a texture, and its value when the
        /// settle sweep started. The sweep recomputes every face deterministically, so it
        /// usually changes nothing — and re-running the whole bounce solve on an unchanged
        /// scene is pure waste. Comparing these tells us whether it is worth it.
        /// </summary>
        private int _publishCount, _publishMarkerAtSweep;

        private bool _sweepRunning;
        private bool _sweepPending;

        /// <summary>Whole-map revalidation owed (see <see cref="FullSweepQuietMs"/>).</summary>
        private bool _fullPending;

        private int _lastChangeTicks;
        private int _version;

        /// <summary>
        /// Per level: faces waiting to be baked. Drained from the BACK, so the most
        /// recently enqueued face bakes first — an edit's own faces are enqueued last and
        /// therefore light up immediately, ahead of the wider revalidation behind them.
        /// (Popping from the front was also quadratic on a whole-map queue.)
        /// </summary>
        private readonly List<BakeFace>[] _queues = { new List<BakeFace>(), new List<BakeFace>(), new List<BakeFace>() };
        private readonly HashSet<BakeFace>[] _queued =
            { new HashSet<BakeFace>(), new HashSet<BakeFace>(), new HashSet<BakeFace>() };

        private Thread _worker;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _running;

        private int _jobsTotal, _jobsDone;

        /// <summary>
        /// Worker parallelism for the per-luxel/per-patch loops. 0 = automatic
        /// (ProcessorCount − 1); 1 = fully serial (deterministic profiling).
        /// </summary>
        public int MaxDegreeOfParallelism = 0;

        private ParallelOptions ParallelOpts
        {
            get
            {
                int degree = MaxDegreeOfParallelism > 0
                    ? MaxDegreeOfParallelism
                    : Math.Max(1, Environment.ProcessorCount - 1);
                return new ParallelOptions { MaxDegreeOfParallelism = degree };
            }
        }

        /// <summary>Below this many items a Parallel.For costs more than it saves.</summary>
        private const int ParallelThreshold = 256;

        /// <summary>
        /// Direct jobs baked concurrently per worker iteration. Also the abort
        /// granularity: a scene change mid-batch wastes at most this many faces.
        /// </summary>
        private const int BatchSize = 64;

        /// <summary>New face snapshots exist. Raised on the worker thread, throttled.</summary>
        public event Action ResultsAvailable;

        /// <summary>"baking 42%", "bounce", "converged" — for a HUD/status bar.</summary>
        public string Status { get; private set; } = "idle";

        public bool IsConverged
        {
            get
            {
                lock (_gate)
                {
                    return _queues[0].Count + _queues[1].Count + _queues[2].Count == 0
                        && !_bounceDirty && !_bounceRunning
                        && !_sweepPending && !_sweepRunning && !_fullPending
                        && !_bvhStale && !_surfaceLightsDirty
                        && (_probes.Count == 0 || (!_probesDirty && _probeGeneration == _generation));
                }
            }
        }

        // ==================== lifecycle ====================

        public void Start()
        {
            if (_worker != null)
                return;

            _running = true;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "LightmapBaker",
                Priority = ThreadPriority.BelowNormal,
            };
            _worker.Start();
        }

        public void Dispose()
        {
            _running = false;
            _wake.Set();

            if (_worker != null && !_worker.Join(1000))
            {
                // background thread; let the process kill it
            }

            _worker = null;
        }

        // ==================== scene input (any thread) ====================

        /// <summary>
        /// Replace the whole scene and rebake everything from scratch. For incremental
        /// edits prefer <see cref="UpdateScene"/>.
        /// </summary>
        public void SetScene(List<BakeFace> faces, List<SourceLight> lights)
        {
            lock (_gate)
            {
                ReplaceFaces(faces);
                _entityLights = lights ?? new List<SourceLight>();
                _surfaceLightsDirty = true;
                CombineLights();
                _bvh = null;
                _bvhStale = true;
                _generation++;
                _bounceDirty = BounceIterations > 0;
                _bounceAll = true;
                _bounceCoarseDone = false;
                _sweepPending = false;
                _fullPending = false;
                ClearRegion(ref _dirtyMin, ref _dirtyMax);

                for (int level = 0; level < Levels; level++)
                {
                    _queues[level].Clear();
                    _queued[level].Clear();
                }

                foreach (BakeFace face in _faces)
                {
                    if (face.WantsLightmap)
                        EnqueueForRelight(face);
                }

                ResetJobCounters();
                Touch();
            }

            _wake.Set();
        }

        /// <summary>
        /// Incremental geometry update. <paramref name="faces"/> is the complete new face
        /// list where UNCHANGED faces are the same <see cref="BakeFace"/> objects as last
        /// time (their baked state is kept and they are not requeued unless a change can
        /// shadow them); <paramref name="changed"/> flags the new/modified entries;
        /// <paramref name="dirtyMin"/>/<paramref name="dirtyMax"/> bound the changed
        /// region (old AND new positions of everything that moved, plus removals).
        ///
        /// Requeued beyond the changed faces themselves are only the plausible shadow
        /// receivers: for point/spot lights, faces whose sight line to the light passes
        /// through the dirty box; for the sun, faces whose ray toward the sun passes
        /// through it; for sky ambient, faces near it. The settle sweep guarantees any
        /// miss of these conservative tests is corrected once editing pauses.
        /// </summary>
        public void UpdateScene(List<BakeFace> faces, List<SourceLight> lights,
                                Vector3 dirtyMin, Vector3 dirtyMax, bool[] changed)
        {
            lock (_gate)
            {
                ReplaceFaces(faces);
                _entityLights = lights ?? new List<SourceLight>();
                _surfaceLightsDirty = true;       // emitter patches move with the geometry
                CombineLights();

                // Only drop the BVH outright when the face list changed shape, which
                // invalidates the triangle ids it hands back. A move keeps the old one
                // live so baking continues while the replacement is built.
                if (_topology != _bvhTopology)
                    _bvh = null;

                _bvhStale = true;
                _generation++;
                _bounceDirty |= BounceIterations > 0;
                _bounceCoarseDone = false;
                _sweepPending = true;
                _fullPending = true;

                ExtendRegion(ref _dirtyMin, ref _dirtyMax, dirtyMin, dirtyMax);
                ExtendRegion(ref _bounceMin, ref _bounceMax, dirtyMin, dirtyMax);

                // Shadow receivers first, the faces that actually changed last: the queue
                // drains from the back, so the geometry under the cursor relights before
                // the wider requeue behind it.
                RequeueShadowReceivers(dirtyMin, dirtyMax, changed);

                for (int i = 0; i < _faces.Count; i++)
                {
                    if (changed != null && i < changed.Length && changed[i] && _faces[i].WantsLightmap)
                        EnqueueForRelight(_faces[i], urgent: true);
                }

                ResetJobCounters();
                Touch();
            }

            _wake.Set();
        }

        /// <summary>
        /// Lights changed but geometry did not: requeue only the faces any changed light
        /// can reach (the incremental path — vrad's BuildFacesVisibleToLights idea with
        /// sphere culling instead of PVS). Existing queue entries are kept.
        /// </summary>
        public void UpdateLights(List<SourceLight> lights)
        {
            lock (_gate)
            {
                // Only entity lights can change here — surface lights follow geometry,
                // which is UpdateScene's business.
                List<SourceLight> oldLights = _entityLights;
                _entityLights = lights ?? new List<SourceLight>();
                CombineLights();

                List<SourceLight> changed = new List<SourceLight>();

                foreach (SourceLight ol in oldLights)
                {
                    SourceLight match = FindByKeyAndType(_entityLights, ol);
                    if (match == null || !ValueEquals(ol, match))
                        changed.Add(ol);
                }

                foreach (SourceLight nl in _entityLights)
                {
                    SourceLight match = FindByKeyAndType(oldLights, nl);
                    if (match == null || !ValueEquals(nl, match))
                        changed.Add(nl);
                }

                if (changed.Count == 0)
                    return;

                _generation++;
                _bounceDirty |= BounceIterations > 0;
                _bounceCoarseDone = false;
                _sweepPending = true;
                _fullPending = true;

                bool global = false;
                foreach (SourceLight light in changed)
                {
                    if (light.Type == SourceLightType.SkyLight || light.Type == SourceLightType.SkyAmbient)
                    {
                        global = true;
                        break;
                    }

                    // A moved/retuned local light re-bounces (and gets revalidated) only
                    // inside its own reach.
                    float radius = light.CullRadius();
                    if (radius >= float.MaxValue)
                    {
                        global = true;
                        break;
                    }

                    Vector3 r = new Vector3(radius);
                    ExtendRegion(ref _dirtyMin, ref _dirtyMax, light.Origin - r, light.Origin + r);
                    ExtendRegion(ref _bounceMin, ref _bounceMax, light.Origin - r, light.Origin + r);
                }

                if (global)
                    _bounceAll = true;

                foreach (BakeFace face in _faces)
                {
                    if (!face.WantsLightmap)
                        continue;

                    bool affected = global;

                    if (!affected)
                    {
                        foreach (SourceLight light in changed)
                        {
                            if (LightTouchesFace(light, face))
                            {
                                affected = true;
                                break;
                            }
                        }
                    }

                    // Priority only means something when the affected set is small; a
                    // sun change affects everything, and appending the whole map again
                    // per edit would just grow the queue.
                    if (affected)
                        EnqueueForRelight(face, urgent: !global);
                }

                ResetJobCounters();
                Touch();
            }

            _wake.Set();
        }

        /// <summary>
        /// Take the user's preview settings. Changes that alter the result (sample counts,
        /// the light model, the scales, bounce) rebake the whole map; displayed lightmaps
        /// stay on screen until their replacements land, as with any edit.
        /// </summary>
        public void ApplySettings(LightPreviewSettings settings)
        {
            if (settings == null)
                return;

            LightPreviewSettings s = settings.Clone();
            s.Clamp();

            lock (_gate)
            {
                bool changed = s.SamplesSun != _sunRaysFinal
                            || s.SamplesAmbient != _skyRaysFinal
                            || s.SoftenCosine != _softenCosine
                            || s.AoScale != _aoScale
                            || s.BrightnessScale != _brightnessScale
                            || s.RadiusScale != _radiusScale
                            || (s.Bounced ? BounceIterationsOn : 0) != BounceIterations;

                if (!changed)
                    return;

                _sunRaysFinal = s.SamplesSun;
                _skyRaysFinal = s.SamplesAmbient;
                _softenCosine = s.SoftenCosine;
                _aoScale = s.AoScale;
                _brightnessScale = s.BrightnessScale;
                _radiusScale = s.RadiusScale;
                BounceIterations = s.Bounced ? BounceIterationsOn : 0;

                CombineLights();
                RequeueEverything();
            }

            _wake.Set();
        }

        /// <summary>
        /// Rebake every face and re-solve the bounce from scratch — Hammer++'s "bake entire
        /// scene". The incremental paths make this unnecessary in normal use; it is the
        /// explicit "I don't trust what I see" button, and how pending work is flushed when
        /// incremental baking is off.
        /// </summary>
        public void RebakeAll()
        {
            lock (_gate)
            {
                RequeueEverything();
            }

            _wake.Set();
        }

        /// <summary>
        /// Hammer++'s "bake in radius": recompute, at final quality and ahead of everything
        /// else queued, every face within <paramref name="radius"/> of
        /// <paramref name="center"/> (normally the 3D camera), then re-bounce that region.
        /// </summary>
        public void BakeAround(Vector3 center, float radius)
        {
            lock (_gate)
            {
                _generation++;
                Vector3 r = new Vector3(radius);

                foreach (BakeFace face in _faces)
                {
                    if (!face.WantsLightmap)
                        continue;

                    Vector3 min, max;
                    FaceBounds(face, out min, out max);

                    if (!SphereTouchesBox(center, radius, min, max))
                        continue;

                    if (face.BakedLevel >= 2)
                        face.BakedLevel = 1;

                    EnqueueForRelight(face, urgent: true);
                }

                if (BounceIterations > 0)
                {
                    _bounceDirty = true;
                    _bounceCoarseDone = true;      // straight to full quality
                    ExtendRegion(ref _bounceMin, ref _bounceMax, center - r, center + r);
                }

                ResetJobCounters();
                Touch();
            }

            _wake.Set();
        }

        /// <summary>
        /// Replace the shadow-only geometry (static props). <paramref name="dirtyMin"/>..
        /// <paramref name="dirtyMax"/> bound what changed — old and new extents of every prop
        /// that moved, appeared or vanished — and only faces whose shadows that region can
        /// reach are requeued, exactly as for a brush edit.
        /// </summary>
        public void UpdateOccluders(List<RayTriangle> occluders, Vector3 dirtyMin, Vector3 dirtyMax)
        {
            lock (_gate)
            {
                _occluders = occluders ?? new List<RayTriangle>();
                _bvhStale = true;
                _generation++;
                _bounceDirty |= BounceIterations > 0;
                _bounceCoarseDone = false;
                _sweepPending = true;
                _fullPending = true;

                ExtendRegion(ref _dirtyMin, ref _dirtyMax, dirtyMin, dirtyMax);
                ExtendRegion(ref _bounceMin, ref _bounceMax, dirtyMin, dirtyMax);
                RequeueShadowReceivers(dirtyMin, dirtyMax, null);

                ResetJobCounters();
                Touch();
            }

            _wake.Set();
        }

        // ---- input helpers (called under _gate) ----

        /// <summary>
        /// Every face back through the pass ladder plus an authoritative whole-map bounce.
        /// Faces showing a result keep it on screen until the rebake replaces it.
        /// </summary>
        private void RequeueEverything()
        {
            _generation++;

            foreach (BakeFace face in _faces)
            {
                if (!face.WantsLightmap)
                    continue;

                if (face.BakedLevel >= 1)
                    face.BakedLevel = 0;

                EnqueueForRelight(face);
            }

            _bounceDirty = BounceIterations > 0;
            _bounceAll = true;
            _bounceCoarseDone = false;
            _sweepPending = false;
            _fullPending = false;
            ClearRegion(ref _dirtyMin, ref _dirtyMax);

            if (BounceIterations == 0)
            {
                // Bounce just got switched off: drop the indirect light it left behind, or
                // the faces would keep composing it in.
                foreach (BakeFace face in _faces)
                    face.Bounce = null;
            }

            ResetJobCounters();
            Touch();
        }

        /// <summary>
        /// Refresh the combined light list the bake loops over, with the brightness and
        /// radius scales applied. Copies, never the caller's objects: the entity list is
        /// what <see cref="UpdateLights"/> diffs against, so it must stay unscaled.
        /// </summary>
        private void CombineLights()
        {
            List<SourceLight> all = new List<SourceLight>(_entityLights.Count + _surfaceLights.Count);
            bool scaled = _brightnessScale != 1.0f || _radiusScale != 1.0f;

            foreach (SourceLight light in _entityLights)
                all.Add(scaled ? Scaled(light) : light);

            foreach (SourceLight light in _surfaceLights)
                all.Add(scaled ? Scaled(light) : light);

            _lights = all;
        }

        private SourceLight Scaled(SourceLight light)
        {
            SourceLight copy = light.Clone();
            copy.Intensity *= _brightnessScale;
            copy.RadiusScale *= _radiusScale;
            return copy;
        }

        /// <summary>vrad's dlight_threshold (vrad.cpp:71): dimmer texlights are dropped.</summary>
        private const float DlightThreshold = 0.1f;

        /// <summary>DIRECT_SCALE (lightmap.cpp:1694) — brightness is defined at 100 units.</summary>
        private const float DirectScale = 100.0f * 100.0f;

        /// <summary>
        /// CreateDirectLights' surface pass (lightmap.cpp:1710) — the texture-light half
        /// of VRAD's light list, which has no entity behind it at all.
        ///
        /// vrad makes one <c>emit_surface</c> light per LEAF PATCH of every face whose
        /// material is in lights.rad, with
        /// <c>intensity = baselight · area · scale[0] · scale[1] / basearea · DIRECT_SCALE</c>.
        /// <c>scale[i]</c> is texels per world unit and <c>basearea</c> the material's
        /// pixel area, so that quotient is "how many texture tiles does this patch cover" —
        /// brightness is per tile, not per patch, which is why a stretched texture emits
        /// no more light than a tiled one.
        ///
        /// We subdivide with the same grid the bounce uses (lightmapscale × 4 ≈ vrad's
        /// maxchop of 4 luxel widths) and split the face's true area evenly across the
        /// emitters, so the total emitted power is independent of how finely we chop —
        /// only the softness of the shadows changes.
        /// </summary>
        private static List<SourceLight> BuildSurfaceLights(List<BakeFace> faces)
        {
            List<SourceLight> lights = new List<SourceLight>();
            List<Vector3> origins = new List<Vector3>();

            for (int f = 0; f < faces.Count; f++)
            {
                BakeFace face = faces[f];

                // Emitters depend only on the face, so they are cached on it. A geometry
                // edit replaces just the BakeFace objects that changed, which is what
                // keeps this from re-deriving every lit panel in the map (a patch-grid
                // build each) on every mouse step of a drag.
                if (face.SurfaceEmitters != null)
                {
                    lights.AddRange(face.SurfaceEmitters);
                    continue;
                }

                face.SurfaceEmitters = EmptyLights;

                // Emissive displacements are not modelled: their emitters would sit on the base
                // quad, not the sculpted surface. (Texture lights on displacements are rare.)
                if (face.Winding == null || face.Winding.Length < 3 || face.IsSky || face.IsDisplacement)
                    continue;

                Vector3 emit = face.EmitColor;

                // VectorAvg( p->baselight ) >= dlight_threshold
                if ((emit.X + emit.Y + emit.Z) * (1.0f / 3.0f) < DlightThreshold)
                    continue;

                float faceArea = PolygonArea(face.Winding);
                if (faceArea <= 1e-6f)
                    continue;

                List<SourceLight> emitters = new List<SourceLight>();
                face.SurfaceEmitters = emitters;

                float baseArea = Math.Max(1e-6f, face.TextureWidth * face.TextureHeight);
                float texelsPerUnitU = 1.0f / Math.Max(1e-6f, face.TextureScaleU);
                float texelsPerUnitV = 1.0f / Math.Max(1e-6f, face.TextureScaleV);

                origins.Clear();

                float patchSize = Math.Max(1, face.LightmapScale) * PatchCoarsen;
                LuxelGrid patches = LuxelGrid.Build(face.Winding, face.Normal, face.AxisU, face.AxisV, patchSize);

                if (patches != null)
                {
                    for (int p = 0; p < patches.Positions.Length; p++)
                    {
                        if (patches.Valid[p])
                            origins.Add(patches.Positions[p]);
                    }
                }

                if (origins.Count == 0)
                {
                    // Face smaller than one patch: emit from its centroid.
                    Vector3 centroid = Vector3.Zero;
                    for (int i = 0; i < face.Winding.Length; i++)
                        centroid += face.Winding[i];
                    origins.Add(centroid / face.Winding.Length);
                }

                float patchArea = faceArea / origins.Count;
                Vector3 intensity = emit *
                    (patchArea * texelsPerUnitU * texelsPerUnitV / baseArea * DirectScale);

                // Group centre and extent, for the distance-based collapse below.
                Vector3 centre = Vector3.Zero;
                for (int i = 0; i < origins.Count; i++)
                    centre += origins[i];
                centre /= origins.Count;

                float radius = 0;
                for (int i = 0; i < origins.Count; i++)
                    radius = Math.Max(radius, (origins[i] - centre).Length());

                // A single emitter is its own aggregate; don't emit both or the face
                // would be lit twice.
                bool cluster = origins.Count > 1 && radius > 1e-3f;

                for (int i = 0; i < origins.Count; i++)
                {
                    emitters.Add(new SourceLight
                    {
                        Type = SourceLightType.Surface,
                        Origin = origins[i],
                        Normal = face.Normal,
                        Intensity = intensity,

                        // emit_surface has its own 1/d² falloff; these are only what the
                        // cull-radius solve reads, and they describe exactly that curve.
                        ConstantAttn = 0,
                        LinearAttn = 0,
                        QuadraticAttn = 1,

                        ClusterOrigin = centre,
                        ClusterRadius = cluster ? radius : 0,
                        IsClusterRoot = false,

                        SourceKey = face.Key,
                    });
                }

                if (cluster)
                {
                    emitters.Add(new SourceLight
                    {
                        Type = SourceLightType.Surface,
                        Origin = centre,
                        Normal = face.Normal,
                        Intensity = intensity * origins.Count,   // the group's total power

                        ConstantAttn = 0,
                        LinearAttn = 0,
                        QuadraticAttn = 1,

                        ClusterOrigin = centre,
                        ClusterRadius = radius,
                        IsClusterRoot = true,

                        SourceKey = face.Key,
                    });
                }

                lights.AddRange(emitters);
            }

            return lights;
        }

        private static readonly List<SourceLight> EmptyLights = new List<SourceLight>();

        /// <summary>Area of a planar polygon (vrad WindingArea).</summary>
        private static float PolygonArea(Vector3[] winding)
        {
            Vector3 total = Vector3.Zero;

            for (int i = 2; i < winding.Length; i++)
                total += Vector3.Cross(winding[i - 1] - winding[0], winding[i] - winding[0]);

            return total.Length() * 0.5f;
        }

        private void ReplaceFaces(List<BakeFace> faces)
        {
            List<BakeFace> next = faces ?? new List<BakeFace>();

            // Did index i keep meaning the same face? That, not object identity, is what
            // decides whether a BVH built against the old list is still usable — its hits
            // report list indices. A modified face is a new object at the same index with
            // the same Id, and leaves the BVH's ids perfectly valid (only stale in
            // position); an inserted or deleted brush shifts everything after it.
            bool sameTopology = _faces.Count == next.Count;

            for (int i = 0; sameTopology && i < next.Count; i++)
            {
                if (_faces[i].Identity != next[i].Identity)
                    sameTopology = false;
            }

            if (!sameTopology)
                _topology++;

            // Faces no longer in the scene: mark so their queue entries get dropped.
            HashSet<BakeFace> keep = new HashSet<BakeFace>(next);
            foreach (BakeFace old in _faces)
            {
                if (!keep.Contains(old))
                    old.Index = -1;
            }

            _faces = next;

            for (int i = 0; i < _faces.Count; i++)
                _faces[i].Index = i;
        }

        // ---- dirty-region bookkeeping ----

        private static void ClearRegion(ref Vector3 min, ref Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
        }

        private static void ExtendRegion(ref Vector3 min, ref Vector3 max, Vector3 otherMin, Vector3 otherMax)
        {
            if (otherMin.X > otherMax.X)
                return; // empty

            min = Vector3.Min(min, otherMin);
            max = Vector3.Max(max, otherMax);
        }

        private static bool RegionIsEmpty(Vector3 min, Vector3 max)
        {
            return min.X > max.X;
        }

        /// <summary>The region grown by <see cref="RegionPad"/> (or 2× its own size).</summary>
        private void PaddedRegion(Vector3 min, Vector3 max, out Vector3 outMin, out Vector3 outMax)
        {
            Vector3 size = max - min;
            float pad = Math.Max(RegionPad, 2.0f * Math.Max(size.X, Math.Max(size.Y, size.Z)));

            outMin = min - new Vector3(pad);
            outMax = max + new Vector3(pad);
        }

        /// <summary>
        /// Queue a face for relighting. A face already showing a result skips the coarse
        /// level: replacing a displayed full-resolution lightmap with a ×4-coarse one
        /// (only to sharpen again moments later) is the single most visible flicker, and
        /// the stale-but-sharp image is the better bridge. Fresh faces (nothing on
        /// screen) still take the coarse level for fast first paint.
        /// </summary>
        private void EnqueueForRelight(BakeFace face, bool urgent = false)
        {
            if (face.Result != null)
            {
                if (face.BakedLevel > 0)
                    face.BakedLevel = 0;

                Enqueue(1, face, urgent);
                Enqueue(2, face, urgent);
            }
            else
            {
                face.BakedLevel = -1;
                Enqueue(0, face, urgent);
                Enqueue(1, face, urgent);
                Enqueue(2, face, urgent);
            }
        }

        /// <summary>
        /// Queue a face. <paramref name="urgent"/> appends unconditionally, even when the
        /// face is already waiting further back: queues drain from the back, so this is
        /// how the geometry being edited gets in front of the revalidation the previous
        /// mouse step queued behind it. The duplicate entry left behind costs nothing —
        /// the drain drops any entry whose face is already baked to that level.
        /// </summary>
        private void Enqueue(int level, BakeFace face, bool urgent = false)
        {
            bool added = _queued[level].Add(face);

            if (added || urgent)
                _queues[level].Add(face);
        }

        private void ResetJobCounters()
        {
            _jobsTotal = _queues[0].Count + _queues[1].Count + _queues[2].Count;
            _jobsDone = 0;
        }

        private void Touch()
        {
            _lastChangeTicks = Environment.TickCount;
        }

        private int QuietMs()
        {
            return Environment.TickCount - _lastChangeTicks;
        }

        // ---- shadow-receiver selection for incremental geometry changes ----

        /// <summary>
        /// Requeue unchanged faces whose lighting the dirty region can plausibly affect.
        /// All tests are conservative supersets of "a shadow ray from this face crosses
        /// the dirty box":
        /// - point/spot: some corner→light segment of the face's AABB crosses the box
        ///   (and the light both reaches the face and touches the box at all);
        /// - sun: some corner→sun segment crosses the box;
        /// - sky ambient: the face is within a proximity pad of the box (sky occlusion is
        ///   soft and local; the settle sweep trues up the far field).
        /// </summary>
        private void RequeueShadowReceivers(Vector3 dirtyMin, Vector3 dirtyMax, bool[] changed)
        {
            if (dirtyMin.X > dirtyMax.X)
                return; // empty dirty region

            // Pad for penumbra/luxel granularity.
            Vector3 pad = new Vector3(16, 16, 16);
            Vector3 boxMin = dirtyMin - pad;
            Vector3 boxMax = dirtyMax + pad;

            Vector3 dirtySize = dirtyMax - dirtyMin;
            float dirtyMaxDim = Math.Max(dirtySize.X, Math.Max(dirtySize.Y, dirtySize.Z));
            float ambientPad = Math.Max(256, 2.5f * dirtyMaxDim);

            bool anySkyAmbient = false;
            List<SourceLight> suns = new List<SourceLight>();
            List<SourceLight> locals = new List<SourceLight>();

            foreach (SourceLight light in _lights)
            {
                switch (light.Type)
                {
                    case SourceLightType.SkyAmbient:
                        anySkyAmbient = true;
                        break;

                    case SourceLightType.SkyLight:
                        suns.Add(light);
                        break;

                    default:
                        // Only lights whose sphere reaches the dirty region can have
                        // their shadows changed by it.
                        if (SphereTouchesBox(light.Origin, light.CullRadius(), boxMin, boxMax))
                            locals.Add(light);
                        break;
                }
            }

            for (int i = 0; i < _faces.Count; i++)
            {
                BakeFace face = _faces[i];

                if (!face.WantsLightmap)
                    continue;

                if (changed != null && i < changed.Length && changed[i])
                    continue; // already queued

                Vector3 faceMin, faceMax;
                FaceBounds(face, out faceMin, out faceMax);

                bool affected = false;

                if (anySkyAmbient
                    && BoxesTouch(faceMin, faceMax,
                                  boxMin - new Vector3(ambientPad), boxMax + new Vector3(ambientPad)))
                {
                    affected = true;
                }

                for (int s = 0; s < suns.Count && !affected; s++)
                {
                    // ray direction toward the sun = -beam direction, effectively infinite
                    Vector3 toSun = -suns[s].Normal * 65536.0f;
                    affected = AnyCornerSegmentCrossesBox(faceMin, faceMax, toSun, boxMin, boxMax, directional: true);
                }

                for (int l = 0; l < locals.Count && !affected; l++)
                {
                    SourceLight light = locals[l];

                    if (!LightTouchesFace(light, face))
                        continue;

                    affected = AnyCornerSegmentCrossesBox(faceMin, faceMax, light.Origin, boxMin, boxMax, directional: false);
                }

                if (affected)
                    EnqueueForRelight(face);
            }
        }

        private static void FaceBounds(BakeFace face, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);

            Vector3[] points = face.SurfacePoints;

            for (int i = 0; i < points.Length; i++)
            {
                min = Vector3.Min(min, points[i]);
                max = Vector3.Max(max, points[i]);
            }
        }

        private static bool BoxesTouch(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        {
            return aMin.X <= bMax.X && aMax.X >= bMin.X
                && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
                && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
        }

        private static bool SphereTouchesBox(Vector3 center, float radius, Vector3 min, Vector3 max)
        {
            if (radius >= float.MaxValue)
                return true;

            Vector3 clamped = Vector3.Min(Vector3.Max(center, min), max);
            return (clamped - center).LengthSquared() <= radius * radius;
        }

        /// <summary>
        /// Does the segment from any corner of the face AABB toward the target (a light
        /// position, or a directional offset when <paramref name="directional"/>) cross
        /// the dirty box? Conservative shadow-frustum test using the 8 AABB corners.
        /// </summary>
        private static bool AnyCornerSegmentCrossesBox(Vector3 faceMin, Vector3 faceMax, Vector3 target,
                                                       Vector3 boxMin, Vector3 boxMax, bool directional)
        {
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? faceMin.X : faceMax.X,
                    (c & 2) == 0 ? faceMin.Y : faceMax.Y,
                    (c & 4) == 0 ? faceMin.Z : faceMax.Z);

                Vector3 end = directional ? corner + target : target;

                if (SegmentCrossesBox(corner, end, boxMin, boxMax))
                    return true;
            }

            return false;
        }

        private static bool SegmentCrossesBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            Vector3 d = b - a;
            float tmin = 0.0f, tmax = 1.0f;

            for (int axis = 0; axis < 3; axis++)
            {
                float da = Axis(d, axis), pa = Axis(a, axis);
                float lo = Axis(min, axis), hi = Axis(max, axis);

                if (Math.Abs(da) < 1e-9f)
                {
                    if (pa < lo || pa > hi)
                        return false;
                    continue;
                }

                float t1 = (lo - pa) / da;
                float t2 = (hi - pa) / da;

                if (t1 > t2) { float t = t1; t1 = t2; t2 = t; }

                tmin = Math.Max(tmin, t1);
                tmax = Math.Min(tmax, t2);

                if (tmin > tmax)
                    return false;
            }

            return true;
        }

        private static float Axis(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return v.X;
                case 1: return v.Y;
                default: return v.Z;
            }
        }

        // ---- light comparison helpers ----

        private static SourceLight FindByKeyAndType(List<SourceLight> list, SourceLight like)
        {
            foreach (SourceLight l in list)
            {
                if (Equals(l.SourceKey, like.SourceKey) && l.Type == like.Type)
                    return l;
            }
            return null;
        }

        private static bool ValueEquals(SourceLight a, SourceLight b)
        {
            return a.Type == b.Type
                && a.Origin == b.Origin
                && a.Normal == b.Normal
                && a.Intensity == b.Intensity
                && a.ConstantAttn == b.ConstantAttn
                && a.LinearAttn == b.LinearAttn
                && a.QuadraticAttn == b.QuadraticAttn
                && a.StopDot == b.StopDot
                && a.StopDot2 == b.StopDot2
                && a.Exponent == b.Exponent
                && a.StartFadeDistance == b.StartFadeDistance
                && a.EndFadeDistance == b.EndFadeDistance
                && a.SunAngularExtent == b.SunAngularExtent;
        }

        private static bool LightTouchesFace(SourceLight light, BakeFace face)
        {
            if (light.Type == SourceLightType.SkyLight || light.Type == SourceLightType.SkyAmbient)
                return true;

            // A texture-light face expands into one emitter per patch, so a lit corridor
            // can hold thousands of them and this test runs per face per light. Both
            // half-space rejections below are exact (the kernel would return zero
            // anyway) and they knock out most of the map for each emitter.
            // (Displacements are not planar, so "turns toward the emitter" is not a half-space
            // test there; only the emitter-side rejection applies.)
            if (light.Type == SourceLightType.Surface && face.Winding != null)
            {
                Vector3[] points = face.SurfacePoints;
                bool anyInFront = false, anyFacing = face.IsDisplacement;

                for (int i = 0; i < points.Length && !(anyInFront && anyFacing); i++)
                {
                    Vector3 toPoint = points[i] - light.Origin;

                    if (Vector3.Dot(toPoint, light.Normal) > 0)
                        anyInFront = true;      // some of the face is in front of the emitter

                    if (Vector3.Dot(toPoint, face.Normal) < 0)
                        anyFacing = true;       // the face turns toward the emitter somewhere
                }

                if (!anyInFront || !anyFacing)
                    return false;
            }

            float radius = light.CullRadius();
            if (radius >= float.MaxValue)
                return true;

            Vector3 min, max;
            FaceBounds(face, out min, out max);

            Vector3 clamped = Vector3.Min(Vector3.Max(light.Origin, min), max);
            return (clamped - light.Origin).LengthSquared() <= radius * radius;
        }

        // ==================== worker ====================

        /// <summary>
        /// Run on the worker thread as it starts, before any bake. This is where a GPU
        /// tracer makes its GL context current: a context belongs to one thread at a time,
        /// so it can only be acquired from inside the thread that will dispatch on it.
        /// </summary>
        public Action WorkerStarted;

        /// <summary>
        /// Run on the worker thread as it exits. GL objects belong to the context that made
        /// them and can only be deleted with it current, so this is the only place a
        /// tracer's buffers can be released without leaking them.
        /// </summary>
        public Action WorkerStopping;

        private void WorkerLoop()
        {
            long lastPublish = 0;
            bool published = false;

            // A GPU tracer that cannot start is not fatal — the oracle factory simply keeps
            // answering null and every face traces on the CPU, which is what it did before.
            try
            {
                Action started = WorkerStarted;

                if (started != null)
                    started();
            }
            catch (Exception)
            {
            }

            try
            {

            while (_running)
            {
                BakeFace jobFace = null;
                int level = -1, generation;
                RayBvh bvh;
                List<SourceLight> lights;
                List<BakeFace> faces;
                bool runBounce = false;
                int bounceRays = BounceRaysPerPatch;
                bool bounceAll = true;
                Vector3 bounceMin = Vector3.Zero, bounceMax = Vector3.Zero;

                bool needBvh, needSurfaceLights;
                int topology;

                List<RayTriangle> occluders;

                lock (_gate)
                {
                    generation = _generation;
                    topology = _topology;
                    faces = _faces;
                    lights = _lights;
                    occluders = _occluders;
                    needBvh = _bvhStale && (_faces.Count > 0 || _occluders.Count > 0);
                    needSurfaceLights = _surfaceLightsDirty;
                    bvh = _bvh;
                }

                if (needBvh)
                {
                    // Build outside the lock — this can take a while on big maps and the
                    // UI thread must never wait on it (scene updates take _gate).
                    RayBvh built = BuildBvh(faces, occluders);

                    lock (_gate)
                    {
                        // Installable as long as list indices still mean the same faces;
                        // the geometry may have moved on again since the build started, in
                        // which case this is still strictly newer than what we hold, and
                        // _bvhStale stays set so the next slice builds again. Discarding
                        // it because a drag bumped the generation is what used to leave a
                        // long drag with no usable BVH at all.
                        if (topology == _topology)
                        {
                            _bvh = built;
                            _bvhTopology = topology;

                            if (generation == _generation)
                                _bvhStale = false;
                        }
                    }

                    continue; // re-enter with fresh state
                }

                if (bvh == null)
                {
                    // Nothing to trace against yet (empty scene). Idle rather than spin.
                    _wake.WaitOne(200);
                    continue;
                }

                if (needSurfaceLights)
                {
                    // Same deal as the BVH: derived from the geometry, so it must be
                    // rebuilt on every scene change, and off the UI thread. Both land
                    // before any face is baked, so no pass ever runs light-starved.
                    List<SourceLight> surface = BuildSurfaceLights(faces);

                    lock (_gate)
                    {
                        if (generation == _generation)
                        {
                            _surfaceLights = surface;
                            _surfaceLightsDirty = false;
                            CombineLights();
                        }
                    }

                    continue;
                }

                List<BakeFace> batch = null;

                lock (_gate)
                {
                    if (generation != _generation)
                        continue;

                    // Drain a batch of same-level jobs — they bake in parallel below.
                    // The real parallelism grain is ACROSS faces: most faces are far too
                    // small to split internally, so per-face threading alone leaves the
                    // other cores idle.
                    // Drained from the BACK: the newest entries are the faces the user just
                    // touched, and they must not queue behind the previous mouse step's
                    // revalidation. (It is also O(1) per pop; popping the front made a
                    // whole-map queue quadratic.)
                    HashSet<BakeFace> inBatch = null;

                    for (int q = 0; q < Levels && batch == null; q++)
                    {
                        while (_queues[q].Count > 0)
                        {
                            int last = _queues[q].Count - 1;
                            BakeFace candidate = _queues[q][last];
                            _queues[q].RemoveAt(last);
                            _queued[q].Remove(candidate);

                            // Drop stale entries: removed faces, already-done levels, and
                            // the duplicates an urgent re-enqueue leaves behind.
                            if (candidate.Index >= 0 && candidate.BakedLevel < q
                                && (inBatch == null || !inBatch.Contains(candidate)))
                            {
                                if (batch == null)
                                {
                                    batch = new List<BakeFace>();
                                    inBatch = new HashSet<BakeFace>();
                                    level = q;
                                }

                                batch.Add(candidate);
                                inBatch.Add(candidate);

                                if (batch.Count >= BatchSize)
                                    break;
                            }
                            else
                            {
                                _jobsDone++;
                            }
                        }
                    }

                    if (batch != null)
                        jobFace = batch[0]; // non-null marks "have work" for the branches below

                    // The settle sweep's jobs have all drained: re-bounce only if it
                    // actually corrected something. On a scene the incremental heuristics
                    // got right — the overwhelmingly common case — it republished nothing
                    // and the existing bounce is still valid.
                    if (jobFace == null && _sweepRunning)
                    {
                        _sweepRunning = false;

                        if (BounceIterations > 0 && Volatile.Read(ref _publishCount) != _publishMarkerAtSweep)
                            _bounceDirty = true;
                    }

                    if (jobFace == null && _bounceDirty && BounceIterations > 0 && QuietMs() >= BounceQuietMs)
                    {
                        runBounce = true;

                        // Fast pass first so indirect light appears promptly, then one
                        // full-quality re-solve once the scene is still.
                        bounceRays = _bounceCoarseDone ? BounceRaysPerPatch : BounceCoarseRaysPerPatch;

                        // Regional unless something global moved: the union of what has
                        // changed since the last solve, padded. The authoritative
                        // whole-map solve comes with the full revalidation below.
                        bounceAll = _bounceAll || RegionIsEmpty(_bounceMin, _bounceMax);
                        bounceMin = _bounceMin;
                        bounceMax = _bounceMax;

                        _bounceDirty = false;
                        _bounceAll = false;
                        ClearRegion(ref _bounceMin, ref _bounceMax);
                        _bounceRunning = true; // keeps IsConverged honest while gathering
                    }

                    // Revalidation, in two stages. Both force a final-quality recompute;
                    // values are deterministic and byte-identical results are not
                    // republished, so a face the cheap paths judged correctly produces no
                    // visible change and no GPU upload — only a real miss does.
                    //
                    //  - regional (SweepQuietMs): the neighbourhood of everything edited
                    //    since the last sweep. Catches what the shadow-receiver heuristics
                    //    miss, at a cost proportional to the edit rather than the map.
                    //  - full (FullSweepQuietMs): every face, plus an authoritative
                    //    whole-map bounce. This is the background "full compile" — it runs
                    //    once per burst of editing, after the user has stopped.
                    if (jobFace == null && !runBounce && !_bounceDirty
                        && (_sweepPending || _fullPending))
                    {
                        bool full = _fullPending && QuietMs() >= FullSweepQuietMs;
                        bool regional = !full && _sweepPending && QuietMs() >= SweepQuietMs;

                        if (full || regional)
                        {
                            Vector3 sweepMin = Vector3.Zero, sweepMax = Vector3.Zero;

                            if (regional)
                                PaddedRegion(_dirtyMin, _dirtyMax, out sweepMin, out sweepMax);

                            // A full sweep subsumes the regional one.
                            _sweepPending = false;
                            ClearRegion(ref _dirtyMin, ref _dirtyMax);

                            if (full)
                                _fullPending = false;

                            int queued = 0;

                            foreach (BakeFace face in _faces)
                            {
                                if (!face.WantsLightmap)
                                    continue;

                                if (regional)
                                {
                                    Vector3 fMin, fMax;
                                    FaceBounds(face, out fMin, out fMax);

                                    if (!BoxesTouch(fMin, fMax, sweepMin, sweepMax))
                                        continue;
                                }

                                if (face.BakedLevel >= 2)
                                    face.BakedLevel = 1;

                                if (face.BakedLevel < 1 && face.Result == null)
                                    Enqueue(0, face);
                                if (face.BakedLevel < 1)
                                    Enqueue(1, face);

                                Enqueue(2, face);
                                queued++;
                            }

                            // The full stage owes an authoritative whole-map bounce
                            // regardless of what the sweep turns up; the regional stage
                            // watches whether it actually corrected anything first (see
                            // where _sweepRunning is cleared).
                            if (full && BounceIterations > 0)
                            {
                                _bounceAll = true;
                                _bounceDirty = true;
                                _bounceCoarseDone = true;   // straight to full quality
                            }

                            Status = full ? "revalidating (full)" : "revalidating";

                            if (queued > 0)
                            {
                                _sweepRunning = true;
                                _publishMarkerAtSweep = Volatile.Read(ref _publishCount);
                                ResetJobCounters();
                                continue;
                            }
                        }
                    }
                }

                if (batch != null)
                {
                    Status = "baking " + (_jobsTotal > 0 ? (_jobsDone * 100 / Math.Max(1, _jobsTotal)) : 0) + "%";

                    int lvl = level;

                    BakeBatch(batch, lvl, bvh, lights, generation);

                    lock (_gate)
                    {
                        if (generation == _generation)
                        {
                            foreach (BakeFace face in batch)
                            {
                                face.BakedLevel = lvl;
                                _jobsDone++;
                            }
                        }
                    }

                    long now = Environment.TickCount;
                    published = true;
                    if (now - lastPublish >= PublishIntervalMs)
                    {
                        lastPublish = now;
                        published = false;
                        RaiseResults();
                    }

                    continue;
                }

                if (runBounce)
                {
                    Status = bounceAll
                        ? (_bounceCoarseDone ? "bounce (full)" : "bounce")
                        : "bounce (local)";

                    try
                    {
                        RunBounce(faces, bvh, generation, bounceRays, bounceAll, bounceMin, bounceMax);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            _bounceRunning = false;

                            // Coarse pass done and still current → queue the refinement.
                            if (generation == _generation && !_bounceCoarseDone)
                            {
                                _bounceCoarseDone = true;
                                _bounceDirty = true;

                                // The refinement covers what this pass covered.
                                if (bounceAll)
                                    _bounceAll = true;
                                else
                                    ExtendRegion(ref _bounceMin, ref _bounceMax, bounceMin, bounceMax);
                            }
                        }
                    }
                    lock (_gate)
                    {
                        // New indirect light: the probes gather it too.
                        _probesDirty = true;
                    }

                    RaiseResults();
                    published = false;
                    continue;
                }

                // Ambient cubes for models, once the faces they gather from have settled.
                List<LightProbe> probes = null;

                lock (_gate)
                {
                    bool facesIdle = _queues[0].Count + _queues[1].Count + _queues[2].Count == 0;

                    if (facesIdle && !_bounceRunning && _probes.Count > 0 && bvh != null
                        && (_probesDirty || _probeGeneration != _generation))
                    {
                        probes = _probes;
                        _probesDirty = false;
                    }
                }

                if (probes != null)
                {
                    Status = "lighting models";
                    ComputeProbes(probes, faces, lights, bvh, generation);

                    lock (_gate)
                    {
                        if (generation == _generation)
                            _probeGeneration = generation;
                        else
                            _probesDirty = true;
                    }

                    RaiseResults();
                    published = false;
                    continue;
                }

                if (published)
                {
                    RaiseResults();
                    published = false;
                }

                Status = IsConverged ? "converged" : Status;
                _wake.WaitOne(200);
            }

            }
            finally
            {
                try
                {
                    Action stopping = WorkerStopping;

                    if (stopping != null)
                        stopping();
                }
                catch (Exception)
                {
                }
            }
        }

        private void RaiseResults()
        {
            Action handler = ResultsAvailable;
            if (handler != null)
            {
                try { handler(); }
                catch { /* UI handler errors must not kill the baker */ }
            }
        }

        private static RayBvh BuildBvh(List<BakeFace> faces, List<RayTriangle> occluders)
        {
            List<RayTriangle> tris = new List<RayTriangle>(occluders != null ? occluders.Count : 0);

            for (int f = 0; f < faces.Count; f++)
            {
                BakeFace face = faces[f];

                if (!face.BlocksLight || face.Winding == null || face.Winding.Length < 3)
                    continue;

                RayTriangleFlags flags = face.IsSky ? RayTriangleFlags.Sky : RayTriangleFlags.None;

                if (face.IsDisplacement)
                {
                    // The sculpted surface casts the shadow, not the base quad.
                    AddDisplacementTriangles(tris, face, f, flags);
                    continue;
                }

                for (int i = 2; i < face.Winding.Length; i++)
                    tris.Add(new RayTriangle(face.Winding[0], face.Winding[i - 1], face.Winding[i], f, flags));
            }

            if (occluders != null)
            {
                // Callers may tag each occluder group with its own id at or below OccluderId
                // (one per prop, so that prop's probe can skip its own surface); anything
                // else is folded into OccluderId so it can never collide with a face index.
                foreach (RayTriangle tri in occluders)
                {
                    int id = tri.Id <= OccluderId ? tri.Id : OccluderId;
                    tris.Add(new RayTriangle(tri.V0, tri.V1, tri.V2, id, RayTriangleFlags.None));
                }
            }

            return RayBvh.Build(tris);
        }

        /// <summary>Two triangles per grid cell, Source's alternating diagonal.</summary>
        private static void AddDisplacementTriangles(List<RayTriangle> tris, BakeFace face, int id, RayTriangleFlags flags)
        {
            int dim = face.DispDim;
            Vector3[] p = face.DispPositions;

            for (int r = 0; r < dim - 1; r++)
            {
                for (int c = 0; c < dim - 1; c++)
                {
                    int i00 = r * dim + c, i01 = i00 + 1, i10 = i00 + dim, i11 = i10 + 1;

                    if (((r * (dim - 1) + c) & 1) == 0)
                    {
                        tris.Add(new RayTriangle(p[i00], p[i10], p[i11], id, flags));
                        tris.Add(new RayTriangle(p[i00], p[i11], p[i01], id, flags));
                    }
                    else
                    {
                        tris.Add(new RayTriangle(p[i00], p[i10], p[i01], id, flags));
                        tris.Add(new RayTriangle(p[i10], p[i11], p[i01], id, flags));
                    }
                }
            }
        }

        /// <summary>
        /// The luxel (or patch) grid a face is lit on: the planar lattice for a brush face,
        /// the lattice lifted onto the sculpted surface for a displacement.
        /// </summary>
        private static LuxelGrid BuildGrid(BakeFace face, float luxelSize)
        {
            if (face.IsDisplacement)
            {
                return LuxelGrid.BuildDisplacement(face.DispCorners, face.Normal, face.AxisU, face.AxisV, luxelSize,
                                                   face.DispPositions, face.DispNormals, face.DispDim);
            }

            return LuxelGrid.Build(face.Winding, face.Normal, face.AxisU, face.AxisV, luxelSize);
        }

        // ==================== direct lighting (GatherSample* ports) ====================

        /// <summary>
        /// lightmap.cpp SoftenCosineTerm: (dot + dot²) / 2 — CS:GO VRAD only (on unless
        /// <c>-nosoften</c>); SDK 2013 VRAD uses the plain cosine.
        /// </summary>
        private float SoftenCosine(float dot)
        {
            return _softenCosine ? (dot + dot * dot) * 0.5f : dot;
        }

        /// <summary>
        /// Supplies the GPU tracer for a bake, or null to trace on the CPU. Set by the host
        /// when a GPU tracer is available; left null the rest of the time, which is what
        /// makes the CPU path the default rather than the fallback.
        ///
        /// Called once per batch, from the worker thread. An implementation that cannot
        /// serve a particular BVH — no context, a scene too large for its buffers — returns
        /// null and gets the CPU path for that batch, and a dispatch that fails sends its
        /// faces to the CPU, so a GPU that gives up mid-bake degrades instead of stopping.
        /// </summary>
        public Func<RayBvh, GpuBatchOracle> OracleFactory;

        /// <summary>
        /// The GPU tracer for this bake, or null. Asked ONLY from the worker thread: a GPU
        /// tracer's GL context is current there and nowhere else, so calling the factory
        /// from a pool thread — which is where the parallel face bake runs — touched GL with
        /// no context and crashed the process (an AccessViolationException in UploadScene).
        /// </summary>
        private GpuBatchOracle ResolveGpuOracle(RayBvh bvh)
        {
            Func<RayBvh, GpuBatchOracle> factory = OracleFactory;
            return factory != null ? factory(bvh) : null;
        }

        /// <summary>
        /// Luxels per recording on the GPU path. A face's luxels are independent, so a large
        /// face splits into spans that record and replay on different cores; one recording
        /// per face left the biggest faces running on one thread while the rest waited.
        /// </summary>
        private const int GpuSpanLuxels = 512;

        /// <summary>One face's lighting inputs, worked out once and shared by its spans.</summary>
        private sealed class FaceJob
        {
            public BakeFace Face;
            public LuxelGrid Grid;
            public Vector3 Normal;
            public int FaceIndex;
            public List<SourceLight> Relevant;
            public int SunRays;
            public Vector3[] SkyDirs;
            public int AoRays;
            public Vector3[] Direct;
            public int Count;

            /// <summary>
            /// About the rays a luxel asks, to size its recordings up front: growing them by
            /// doubling copied every recorded ray several times over. An estimate - a span that
            /// asks more still grows.
            /// </summary>
            public int RaysPerLuxel;
        }

        /// <summary>A run of one face's luxels and the rays it asks.</summary>
        private sealed class SpanJob
        {
            public FaceJob Job;
            public int Start;
            public int End;
            public RayRecording Rays;
        }

        /// <summary>
        /// Bake one batch of same-level faces: on the GPU when one is tracing, else every
        /// face in parallel on the CPU.
        /// </summary>
        private void BakeBatch(List<BakeFace> batch, int level, RayBvh bvh, List<SourceLight> lights, int generation)
        {
            GpuBatchOracle gpu = ResolveGpuOracle(bvh);

            if (gpu != null)
            {
                BakeBatchOnGpu(batch, level, bvh, lights, generation, gpu);
                return;
            }

            if (batch.Count == 1)
                BakeFaceOnCpu(batch[0], level, bvh, lights, generation);
            else
                Parallel.ForEach(batch, ParallelOpts,
                    face => BakeFaceOnCpu(face, level, bvh, lights, generation));
        }

        private void BakeFaceOnCpu(BakeFace face, int level, RayBvh bvh,
                                   List<SourceLight> lights, int generation)
        {
            FaceJob job = PrepareFace(face, level, bvh, lights);

            if (job == null)
                return;

            BakeFaceLuxels(job.Grid, job.Normal, job.FaceIndex, job.Relevant, job.SunRays, job.SkyDirs,
                           job.AoRays, new BvhRayOracle(bvh), job.Direct, job.Count, generation);

            FinishFace(job, level, generation);
        }

        /// <summary>
        /// The GPU path, in three steps over the whole batch: record every span's rays in
        /// parallel, trace them all from this (the worker) thread — the only one the GPU
        /// context is current on — then replay the answers in parallel and publish.
        /// </summary>
        private void BakeBatchOnGpu(List<BakeFace> batch, int level, RayBvh bvh,
                                    List<SourceLight> lights, int generation, GpuBatchOracle gpu)
        {
            FaceJob[] jobs = new FaceJob[batch.Count];

            Parallel.For(0, batch.Count, ParallelOpts,
                i => jobs[i] = PrepareFace(batch[i], level, bvh, lights));

            List<SpanJob> spans = new List<SpanJob>();

            foreach (FaceJob job in jobs)
            {
                if (job == null)
                    continue;

                for (int start = 0; start < job.Count; start += GpuSpanLuxels)
                {
                    spans.Add(new SpanJob
                    {
                        Job = job,
                        Start = start,
                        End = Math.Min(job.Count, start + GpuSpanLuxels),
                    });
                }
            }

            // Pipelined in groups, so the GPU and the CPU work at the same time: while this
            // thread waits on one group's trace, the pool records the next group and replays
            // the last. Unpipelined, each side sat idle for the other's whole step.
            List<List<SpanJob>> groups = SplitSpans(spans, GpuPipelineGroups);
            List<Task> replays = new List<Task>();
            Func<bool> cancelled = () => Volatile.Read(ref _generation) != generation;

            Task recording = groups.Count > 0 ? Task.Run(() => RecordSpans(groups[0], gpu, generation)) : null;

            for (int g = 0; g < groups.Count; g++)
            {
                recording.Wait();

                if (cancelled())
                    break;

                List<SpanJob> group = groups[g];

                if (g + 1 < groups.Count)
                {
                    List<SpanJob> next = groups[g + 1];
                    recording = Task.Run(() => RecordSpans(next, gpu, generation));
                }

                RayRecording[] recordings = new RayRecording[group.Count];

                for (int i = 0; i < group.Count; i++)
                    recordings[i] = group[i].Rays;

                gpu.TraceAll(recordings, cancelled);

                replays.Add(Task.Run(() => Parallel.ForEach(group, ParallelOpts, span =>
                {
                    BakeSpan(span, generation);
                    span.Rays.Release();
                })));
            }

            // Every task finishes before this returns, cancelled or not: a span still being
            // recorded or replayed must not outlive the batch that owns its arrays.
            if (recording != null)
                recording.Wait();

            Task.WaitAll(replays.ToArray());

            if (cancelled())
                return;

            foreach (FaceJob job in jobs)
            {
                if (job != null)
                    FinishFace(job, level, generation);
            }
        }

        /// <summary>
        /// Groups the GPU path pipelines a batch's spans into. More overlaps the two sides
        /// better at the ends of a batch; fewer keeps each dispatch wide.
        /// </summary>
        private const int GpuPipelineGroups = 4;

        /// <summary>Split spans into up to <paramref name="count"/> runs of similar luxel totals.</summary>
        private static List<List<SpanJob>> SplitSpans(List<SpanJob> spans, int count)
        {
            long luxels = 0;

            foreach (SpanJob span in spans)
                luxels += span.End - span.Start;

            long target = Math.Max(1, (luxels + count - 1) / count);

            List<List<SpanJob>> groups = new List<List<SpanJob>>();
            List<SpanJob> group = null;
            long inGroup = 0;

            foreach (SpanJob span in spans)
            {
                if (group == null || inGroup >= target)
                {
                    group = new List<SpanJob>();
                    groups.Add(group);
                    inGroup = 0;
                }

                group.Add(span);
                inGroup += span.End - span.Start;
            }

            return groups;
        }

        private void RecordSpans(List<SpanJob> spans, GpuBatchOracle gpu, int generation)
        {
            Parallel.ForEach(spans, ParallelOpts, span =>
            {
                span.Rays = gpu.NewRecording((span.End - span.Start) * span.Job.RaysPerLuxel);
                BakeSpan(span, generation);
            });
        }

        private void BakeSpan(SpanJob span, int generation)
        {
            FaceJob job = span.Job;

            BakeLuxelSpan(job.Grid, job.Normal, job.FaceIndex, job.Relevant, job.SunRays, job.SkyDirs,
                          job.AoRays, span.Rays, job.Direct, span.Start, span.End, generation);
        }

        /// <summary>
        /// Everything about a face's lighting that does not depend on who traces it: the
        /// luxel grid, and the lights that can reach it. Null for a face with no grid.
        ///
        /// The light cull traces on the CPU whoever bakes the face. It decides which lights
        /// the face's rays are cast for, so it has to be settled before those rays can be
        /// recorded — and it is a handful of early-out probes per light, cheap next to the
        /// bake it trims.
        /// </summary>
        private FaceJob PrepareFace(BakeFace face, int level, RayBvh bvh, List<SourceLight> lights)
        {
            float luxelSize = Math.Max(1, face.LightmapScale) * LuxelCoarsen[level];

            LuxelGrid grid = BuildGrid(face, luxelSize);
            if (grid == null)
                return null;

            FaceJob job = new FaceJob
            {
                Face = face,
                Grid = grid,
                Normal = face.Normal,
                Count = grid.Width * grid.Height,
                SunRays = SunRays(level),
                SkyDirs = SphereDirections.Table(SkyRays(level)),
                AoRays = _aoScale > 0 ? AoRaysPerLevel[level] : 0,

                // The id shadow rays skip. A planar face cannot shadow itself, and skipping
                // it spares every ray the self-intersection test; a displacement can, so it
                // skips nothing and relies on the 1-unit normal offset alone, like vrad.
                FaceIndex = face.IsDisplacement ? NoSkip : face.Index,
            };

            job.Direct = new Vector3[job.Count];

            Vector3 faceMin, faceMax;
            FaceBounds(face, out faceMin, out faceMax);
            Vector3 faceCentre = (faceMin + faceMax) * 0.5f;
            float faceRadius = (faceMax - faceMin).Length() * 0.5f;

            List<SourceLight> relevant = new List<SourceLight>();
            foreach (SourceLight light in lights)
            {
                if (!UseInCluster(light, faceCentre, faceRadius))
                    continue;

                if (LightTouchesFace(light, face))
                    relevant.Add(light);
            }

            if (bvh != null && relevant.Count > VisibilityCullThreshold)
                relevant = CullInvisibleLights(relevant, grid, job.Normal, job.FaceIndex, new BvhRayOracle(bvh));

            job.Relevant = relevant;

            int raysPerLuxel = job.AoRays;

            foreach (SourceLight light in relevant)
            {
                switch (light.Type)
                {
                    // Only the directions above the surface are cast: half the sphere.
                    case SourceLightType.SkyAmbient: raysPerLuxel += job.SkyDirs.Length / 2 + 1; break;
                    case SourceLightType.SkyLight: raysPerLuxel += job.SunRays; break;
                    default: raysPerLuxel += 1; break;
                }
            }

            job.RaysPerLuxel = raysPerLuxel;
            return job;
        }

        /// <summary>Install a baked face — never data computed against a superseded scene.</summary>
        private void FinishFace(FaceJob job, int level, int generation)
        {
            if (Volatile.Read(ref _generation) != generation)
                return;

            job.Face.Grid = job.Grid;
            job.Face.Direct = job.Direct;

            Publish(job.Face, level);
        }

        /// <summary>
        /// How many group radii away a face has to be before an emitter grid collapses
        /// into its single aggregate light. The point approximation of an area source
        /// errs by roughly (r/d)², so 8 keeps it under ~1.5% — below one 8-bit lightmap
        /// step over most of the range, and the shadow it casts is equally soft either
        /// way at that distance.
        /// </summary>
        public float ClusterFarRatio = 8.0f;

        /// <summary>
        /// Pick exactly one of {the emitter grid, its aggregate} for this receiver: the
        /// individual emitters close up, where their spread shapes the penumbra, and the
        /// aggregate far away, where it does not. Lights with no cluster (entity lights,
        /// lone emitters) always pass.
        /// </summary>
        private bool UseInCluster(SourceLight light, Vector3 faceCentre, float faceRadius)
        {
            if (light.ClusterRadius <= 0)
                return true;

            // Measured to the near side of the face, so a big face that reaches into the
            // near zone anywhere keeps the full emitter grid.
            float distance = (faceCentre - light.ClusterOrigin).Length() - faceRadius;

            bool far = distance > ClusterFarRatio * light.ClusterRadius;
            return far == light.IsClusterRoot;
        }

        /// <summary>
        /// Above this many candidate lights on one face, probe visibility before baking.
        /// Below it the probes would cost more than the lights they might remove, and a
        /// simple scene then behaves exactly as if the cull did not exist.
        /// </summary>
        public int VisibilityCullThreshold = 12;

        /// <summary>Luxels probed per (face, light) by the visibility cull.</summary>
        private const int VisibilityProbeGrid = 4;   // 4x4 = 16 probes

        /// <summary>
        /// Drop lights that reach no part of this face at all.
        ///
        /// This stands in for VRAD's per-light PVS (<c>SetDLightVis</c>), which we cannot
        /// build without vvis. It matters most for texture lights: a lit ceiling panel
        /// expands into a patch grid of emitters, each with a cull radius of thousands of
        /// units, so on a real map every face ends up with hundreds of candidates that
        /// are in fact walled off — and pays a shadow ray per luxel for each one.
        ///
        /// The test evaluates exactly the predicate the kernel does (facing, cone,
        /// emitter side, then an occlusion ray) at a 4×4 spread of the face's own luxel
        /// positions, and keeps the light the moment any probe sees it. So it can only
        /// ever discard a light that is genuinely zero at all 16 probes.
        ///
        /// It is NOT conservative in the strict sense: light reaching a sliver of a face
        /// smaller than a quarter of it in both axes, and missing every probe, is lost.
        /// That is the one place the preview knowingly trades exactness for tractability;
        /// set <see cref="VisibilityCullThreshold"/> to <c>int.MaxValue</c> to turn it off
        /// and bake every candidate.
        /// </summary>
        private static List<SourceLight> CullInvisibleLights(List<SourceLight> candidates, LuxelGrid grid,
                                                             Vector3 normal, int faceIndex, IRayOracle rays)
        {
            int probeCount;
            int[] probes = ProbeIndices(grid, out probeCount);

            List<SourceLight> visible = new List<SourceLight>(candidates.Count);

            for (int l = 0; l < candidates.Count; l++)
            {
                SourceLight light = candidates[l];

                // Sun and sky dome are global and already sample their own visibility.
                if (light.Type == SourceLightType.SkyLight || light.Type == SourceLightType.SkyAmbient)
                {
                    visible.Add(light);
                    continue;
                }

                bool anySees = false;

                for (int p = 0; p < probeCount && !anySees; p++)
                {
                    Vector3 n = grid.Normals != null ? grid.Normals[probes[p]] : normal;
                    Vector3 pos = grid.Positions[probes[p]] + n;

                    Vector3 delta = light.Origin - pos;
                    float dist2 = delta.LengthSquared();

                    if (dist2 < 1e-8f)
                    {
                        anySees = true;
                        break;
                    }

                    float dist = (float)Math.Sqrt(dist2);
                    Vector3 L = delta / dist;

                    if (Vector3.Dot(n, L) <= 0)
                        continue;                       // luxel faces away

                    float dot2 = -Vector3.Dot(L, light.Normal);

                    if (light.Type == SourceLightType.Surface && dot2 <= 0)
                        continue;                       // behind the emitting plane

                    if (light.Type == SourceLightType.Spot && dot2 <= light.StopDot2)
                        continue;                       // outside the cone

                    if (!rays.AnyHit(pos, L, dist - 0.5f, faceIndex, RayTriangleFlags.None))
                        anySees = true;
                }

                if (anySees)
                    visible.Add(light);
            }

            return visible;
        }

        [ThreadStatic]
        private static int[] _probeBuffer;

        /// <summary>An evenly spread set of luxel indices, corners included.</summary>
        private static int[] ProbeIndices(LuxelGrid grid, out int count)
        {
            if (_probeBuffer == null)
                _probeBuffer = new int[VisibilityProbeGrid * VisibilityProbeGrid];

            int nx = Math.Min(VisibilityProbeGrid, grid.Width);
            int ny = Math.Min(VisibilityProbeGrid, grid.Height);

            count = 0;

            for (int j = 0; j < ny; j++)
            {
                int t = ny == 1 ? 0 : j * (grid.Height - 1) / (ny - 1);

                for (int i = 0; i < nx; i++)
                {
                    int s = nx == 1 ? 0 : i * (grid.Width - 1) / (nx - 1);
                    _probeBuffer[count++] = t * grid.Width + s;
                }
            }

            return _probeBuffer;
        }

        /// <summary>
        /// Bake a face's luxels, in parallel row-chunks when the oracle allows it.
        ///
        /// A batched oracle does not: it carries a cursor, and the batch it dispatches is
        /// already the parallelism. So the choice of how to spread the work follows from who
        /// is answering the rays, which is why it lives here rather than in the caller.
        /// </summary>
        private void BakeFaceLuxels(LuxelGrid grid, Vector3 normal, int faceIndex,
                                    List<SourceLight> relevant, int sunRays, Vector3[] skyDirs, int aoRays,
                                    IRayOracle rays, Vector3[] direct, int count, int generation)
        {
            if (count < ParallelThreshold || !rays.SupportsConcurrentUse)
            {
                BakeLuxelSpan(grid, normal, faceIndex, relevant, sunRays, skyDirs, aoRays, rays,
                              direct, 0, count, generation);
                return;
            }

            // Chunk rows so each task body is substantial — a fork/join per face
            // with tiny tasks costs more than it wins.
            int rowsPerChunk = Math.Max(1, (ParallelThreshold + grid.Width - 1) / grid.Width);
            int chunks = (grid.Height + rowsPerChunk - 1) / rowsPerChunk;

            Parallel.For(0, chunks, ParallelOpts, (chunk, state) =>
            {
                if (Volatile.Read(ref _generation) != generation)
                {
                    state.Stop();
                    return;
                }

                int start = chunk * rowsPerChunk * grid.Width;
                int end = Math.Min(count, start + rowsPerChunk * grid.Width);
                BakeLuxelSpan(grid, normal, faceIndex, relevant, sunRays, skyDirs, aoRays, rays,
                              direct, start, end, generation);
            });
        }

        /// <summary>The direct-light kernel over a contiguous luxel range (plain loop —
        /// no per-luxel delegate; this is the hottest code in the baker).</summary>
        private void BakeLuxelSpan(LuxelGrid grid, Vector3 normal, int faceIndex,
                                   List<SourceLight> relevant, int sunRays, Vector3[] skyDirs, int aoRays,
                                   IRayOracle rays,
                                   Vector3[] direct, int start, int end, int generation)
        {
            Vector3[] normals = grid.Normals;
            float aoScale = _aoScale;

            for (int i = start; i < end; i++)
            {
                if ((i & 63) == 0 && Volatile.Read(ref _generation) != generation)
                    return;

                // Displacements shade with the interpolated surface normal; brush faces with
                // their plane's.
                Vector3 n = normals != null ? normals[i] : normal;

                Vector3 pos = grid.Positions[i] + n; // 1 unit off the surface (anti-acne)
                Vector3 sum = Vector3.Zero;

                if (relevant.Count == 0)
                {
                    direct[i] = sum;
                    continue;
                }

                // vrad multiplies out.m_flDot by the AO term for every light type after
                // the per-type switch (lightmap.cpp:2269), and every contribution is
                // linear in that dot — so one factor over the whole sum is equivalent.
                // ao_scale 1 is vrad's term exactly; other strengths raise it to that power
                // (0 never gets here — the ray count is zero).
                float ao = 1.0f;

                if (aoRays > 0)
                {
                    ao = ComputeAmbientOcclusion(pos, n, rays, aoRays, faceIndex);

                    if (aoScale != 1.0f && ao > 0)
                        ao = (float)Math.Pow(ao, aoScale);
                }

                for (int l = 0; l < relevant.Count; l++)
                {
                    SourceLight light = relevant[l];

                    switch (light.Type)
                    {
                        case SourceLightType.Point:
                        case SourceLightType.Spot:
                            sum += SampleStandardLight(light, pos, n, rays, faceIndex);
                            break;

                        case SourceLightType.Surface:
                            sum += SampleSurfaceLight(light, pos, n, rays, faceIndex);
                            break;

                        case SourceLightType.SkyLight:
                            sum += SampleSkyLight(light, pos, n, rays, faceIndex, sunRays, i);
                            break;

                        case SourceLightType.SkyAmbient:
                            sum += SampleSkyAmbient(light, pos, n, rays, faceIndex, skyDirs);
                            break;
                    }
                }

                direct[i] = ao != 1.0f ? sum * ao : sum;
            }
        }

        /// <summary>
        /// CalculateAmbientOcclusion4 (lightmap.cpp:54), scalar. Fixed sphere directions
        /// mirrored onto the normal's hemisphere, 36-unit rays that ignore sky surfaces,
        /// cosine-weighted visibility fraction — then squared, "an artistic choice by the
        /// CS:GO team" (their comment). Deterministic: the direction table is shared by
        /// every luxel, exactly as vrad's per-call DirectionalSampler_t restarts from the
        /// same sequence each time.
        /// </summary>
        private static float ComputeAmbientOcclusion(Vector3 pos, Vector3 normal, IRayOracle rays,
                                                     int nSamples, int skipFace)
        {
            if (rays == null || nSamples <= 0)
                return 1.0f;

            Vector3[] dirs = SphereDirections.Table(nSamples);

            float totalVisible = 0, totalPossible = 0;

            for (int i = 0; i < nSamples; i++)
            {
                float dot = Vector3.Dot(dirs[i], normal);
                float absDot = Math.Abs(dot);

                // ray - n·(n·ray) + n·|n·ray| : reflect the lower hemisphere upward
                Vector3 dir = dirs[i] + normal * (absDot - dot);

                totalPossible += absDot;

                if (!rays.AnyHit(pos, dir, AoRayLength, skipFace, RayTriangleFlags.Sky))
                    totalVisible += absDot;
            }

            if (totalPossible <= 0)
                return 1.0f;

            float ao = totalVisible / totalPossible;
            return ao * ao;
        }

        /// <summary>
        /// GatherSampleStandardLightSSE (lightmap.cpp:2018), scalar: point + spot with
        /// the exact falloff polynomial, cone handling, hard-falloff quintic fade and a
        /// shadow ray.
        /// </summary>
        private Vector3 SampleStandardLight(SourceLight dl, Vector3 pos, Vector3 normal, IRayOracle rays, int skipFace)
        {
            Vector3 delta = dl.Origin - pos;
            float dist2 = delta.LengthSquared();
            float dist = (float)Math.Sqrt(dist2);

            if (dist < 1e-6f)
                return Vector3.Zero;

            Vector3 L = delta / dist;

            float dot = Vector3.Dot(normal, L);
            if (dot <= 0)
                return Vector3.Zero;

            dot = SoftenCosine(dot);

            bool hasHardFalloff = dl.EndFadeDistance > dl.StartFadeDistance;
            if (hasHardFalloff && dist > dl.EndFadeDistance)
                return Vector3.Zero;

            float falloffDist = Math.Max(dist, 1.0f);
            float falloffEvalDist = Math.Min(falloffDist, dl.CapDist);

            float denom = dl.ConstantAttn
                        + dl.LinearAttn * falloffEvalDist
                        + dl.QuadraticAttn * falloffEvalDist * falloffEvalDist;

            float falloff = denom > 1e-20f ? 1.0f / denom : 0.0f;

            if (dl.Type == SourceLightType.Spot)
            {
                float dot2 = -Vector3.Dot(L, dl.Normal);

                if (dot2 <= dl.StopDot2)
                    return Vector3.Zero; // outside the cone

                falloff *= dot2;

                if (dot2 <= dl.StopDot) // in the penumbra
                {
                    float mult = (dot2 - dl.StopDot2) / (dl.StopDot - dl.StopDot2);
                    mult = Math.Max(0, Math.Min(1, mult));

                    if (dl.Exponent != 0.0f && dl.Exponent != 1.0f)
                        mult = (float)Math.Pow(mult, dl.Exponent);

                    falloff *= mult;
                }
            }

            // fade region → quintic smoothstep to zero (t³(t(6t−15)+10))
            if (hasHardFalloff)
            {
                float t = (dist - dl.StartFadeDistance) / (dl.EndFadeDistance - dl.StartFadeDistance);
                t = Math.Max(0, Math.Min(1, t));
                t = 1.0f - t;
                falloff *= t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
            }

            // Shadow ray (vrad TestLine): anything opaque between sample and light kills it.
            if (rays.AnyHit(pos, L, dist - 0.5f, skipFace, RayTriangleFlags.None))
                return Vector3.Zero;

            return dl.Intensity * (falloff * dot);
        }

        /// <summary>
        /// GatherSampleStandardLightSSE's <c>emit_surface</c> branch (lightmap.cpp:2088):
        /// a texture light. One-sided — nothing behind the emitting plane is lit — with a
        /// plain inverse-square falloff weighted by the emitter's own cosine, which is
        /// what makes a grid of these behave like the area light it stands for.
        /// No constant/linear/quadratic keys and no cap distance are involved.
        /// </summary>
        private Vector3 SampleSurfaceLight(SourceLight dl, Vector3 pos, Vector3 normal, IRayOracle rays, int skipFace)
        {
            Vector3 delta = dl.Origin - pos;
            float dist2 = delta.LengthSquared();

            if (dist2 < 1e-8f)
                return Vector3.Zero;

            float dist = (float)Math.Sqrt(dist2);
            Vector3 L = delta / dist;

            float dot = Vector3.Dot(normal, L);
            if (dot <= 0)
                return Vector3.Zero;

            dot = SoftenCosine(dot);

            // dot2 = -(L · emitterNormal): light only leaves the front of the surface.
            float dot2 = -Vector3.Dot(L, dl.Normal);
            if (dot2 <= 0)
                return Vector3.Zero;

            float falloff = dot2 / dist2;

            // vrad nudges the trace endpoint off the emitting surface by DIST_EPSILON so
            // the ray doesn't hit the emitter itself; stopping short does the same.
            if (rays.AnyHit(pos, L, dist - 0.5f, skipFace, RayTriangleFlags.None))
                return Vector3.Zero;

            return dl.Intensity * (falloff * dot);
        }

        /// <summary>
        /// GatherSampleSkyLightSSE (lightmap.cpp:1834): directional sun; a ray toward the
        /// sun must reach a sky face (or leave the map). Soft sun jitters over the
        /// angular extent.
        /// </summary>
        private Vector3 SampleSkyLight(SourceLight dl, Vector3 pos, Vector3 normal, IRayOracle rays, int skipFace,
                                       int nsamples, int luxelSeed)
        {
            float dot = -Vector3.Dot(normal, dl.Normal);
            if (dot <= 0)
                return Vector3.Zero;

            dot = SoftenCosine(dot);

            if (dl.SunAngularExtent <= 0)
                nsamples = 1;

            const float MaxTrace = 1.0e6f;

            Vector3 toSun = -dl.Normal;
            int visible = 0;

            for (int d = 0; d < nsamples; d++)
            {
                Vector3 dir = toSun;

                if (d > 0)
                {
                    // jitter within the angular extent — deterministic per (luxel, ray)
                    Vector3 jitter = SphereDirections.Hash(luxelSeed * 977 + d) * dl.SunAngularExtent;
                    dir = Vector3.Normalize(toSun + jitter);
                }

                if (rays.ReachesSky(pos, dir, MaxTrace))
                    visible++;
            }

            float seeAmount = (float)visible / nsamples;
            return dl.Intensity * (dot * seeAmount);
        }

        /// <summary>
        /// GatherSampleAmbientSkySSE (lightmap.cpp:1922): cosine-weighted sky-dome
        /// integral over a fixed direction table; result = intensity × Σ(vis·dot)/Σ(dot),
        /// so a fully open surface gets exactly the _ambient value.
        /// </summary>
        private Vector3 SampleSkyAmbient(SourceLight dl, Vector3 pos, Vector3 normal, IRayOracle rays, int skipFace,
                                         Vector3[] dirs)
        {
            const float MaxTrace = 1.0e6f;

            float sumDot = 0;
            float sumVisible = 0;

            for (int j = 0; j < dirs.Length; j++)
            {
                Vector3 dir = dirs[j]; // toward the sky

                float dot = Vector3.Dot(normal, dir);
                if (dot <= 1e-3f)
                    continue;

                dot = SoftenCosine(dot);
                sumDot += dot;

                if (rays.ReachesSky(pos, dir, MaxTrace))
                    sumVisible += dot;
            }

            if (sumDot <= 0)
                return Vector3.Zero;

            return dl.Intensity * (sumVisible / sumDot);
        }

        // ==================== ambient cubes for models ====================

        private static readonly Vector3[] CubeAxes =
        {
            new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
            new Vector3(0, 1, 0), new Vector3(0, -1, 0),
            new Vector3(0, 0, 1), new Vector3(0, 0, -1),
        };

        /// <summary>Indirect-light rays per cube face.</summary>
        private const int ProbeIndirectRays = 48;

        /// <summary>
        /// Light each probe's model with an ambient cube: for each of the six axis directions,
        /// every light's direct contribution through the same kernels the luxels use (falloff,
        /// cone, sun and sky visibility, shadow rays), plus the indirect light arriving from
        /// the baked surfaces around it — the radiance of whatever face a cosine-weighted ray
        /// hits, read off that face's lightmap (direct + bounce) times its reflectivity.
        ///
        /// That is what VRAD gathers for its per-leaf ambient cubes and what Hammer++ hands
        /// its props; folding the direct lights into the cube as well, rather than passing the
        /// strongest few to the shader as local lights, keeps their shadows (the engine's
        /// local lights have none) at the cost of no per-vertex falloff across a big model.
        /// </summary>
        private void ComputeProbes(List<LightProbe> probes, List<BakeFace> faces, List<SourceLight> lights,
                                   RayBvh bvh, int generation)
        {
            int sunRays = SunRays(2);
            Vector3[] skyDirs = SphereDirections.Table(SkyRays(2));

            Vector3[][] indirectDirs = new Vector3[6][];
            for (int a = 0; a < 6; a++)
                indirectDirs[a] = SphereDirections.CosineHemisphereTable(CubeAxes[a], ProbeIndirectRays);

            Action<int> computeOne = index =>
            {
                if (Volatile.Read(ref _generation) != generation)
                    return;

                LightProbe probe = probes[index];
                Vector3 pos = probe.Position;
                int skip = probe.SkipId;
                IRayOracle rays = new ProbeRayOracle(bvh, skip);

                List<SourceLight> relevant = new List<SourceLight>();
                foreach (SourceLight light in lights)
                {
                    if (!UseInCluster(light, pos, 0))
                        continue;

                    float radius = light.CullRadius();
                    if (radius < float.MaxValue && (light.Origin - pos).LengthSquared() > radius * radius)
                        continue;

                    relevant.Add(light);
                }

                Vector3[] cube = new Vector3[6];

                for (int a = 0; a < 6; a++)
                {
                    Vector3 n = CubeAxes[a];
                    Vector3 sum = Vector3.Zero;

                    foreach (SourceLight light in relevant)
                    {
                        switch (light.Type)
                        {
                            case SourceLightType.Point:
                            case SourceLightType.Spot:
                                sum += SampleStandardLight(light, pos, n, rays, skip);
                                break;
                            case SourceLightType.Surface:
                                sum += SampleSurfaceLight(light, pos, n, rays, skip);
                                break;
                            case SourceLightType.SkyLight:
                                sum += SampleSkyLight(light, pos, n, rays, skip, sunRays, index * 7 + a);
                                break;
                            case SourceLightType.SkyAmbient:
                                sum += SampleSkyAmbient(light, pos, n, rays, skip, skyDirs);
                                break;
                        }
                    }

                    sum += GatherIndirect(pos, indirectDirs[a], faces, bvh, skip);
                    cube[a] = Vector3.Max(sum, Vector3.Zero);
                }

                if (Volatile.Read(ref _generation) != generation)
                    return;

                probe.Cube = cube;
                probe.Version++;
            };

            if (probes.Count < 8)
            {
                for (int i = 0; i < probes.Count; i++)
                    computeOne(i);
            }
            else
            {
                Parallel.For(0, probes.Count, ParallelOpts, computeOne);
            }
        }

        /// <summary>Mean radiance seen along cosine-weighted directions: the baked faces' light × reflectivity.</summary>
        private static Vector3 GatherIndirect(Vector3 pos, Vector3[] dirs, List<BakeFace> faces, RayBvh bvh, int skip)
        {
            Vector3 sum = Vector3.Zero;

            for (int m = 0; m < dirs.Length; m++)
            {
                ProbeRayOracle.Hit hit;
                if (!ProbeRayOracle.ClosestHitPast(bvh, pos, dirs[m], skip, out hit))
                    continue;

                if (hit.Id < 0 || hit.Id >= faces.Count || (hit.Flags & RayTriangleFlags.Sky) != 0)
                    continue;

                BakeFace face = faces[hit.Id];
                LuxelGrid grid = face.Grid;
                Vector3[] direct = face.Direct;

                if (grid == null || direct == null || !face.WantsLightmap)
                    continue;

                Vector3 point = hit.Point;
                Vector3 radiance = SampleGrid(grid, direct, point);

                PatchLighting bounce = face.Bounce;
                if (bounce != null && bounce.Grid != null)
                    radiance += SampleGrid(bounce.Grid, bounce.Indirect, point);

                sum += radiance * face.Reflectivity;
            }

            return sum / dirs.Length;
        }

        // ==================== bounce (Monte Carlo radiosity, replaces vrad transfers) ====================

        /// <summary>
        /// vrad seeds patches with direct light (BuildPatchLights), then iterates
        /// GatherLight over the stored transfer matrix. We iterate the same integral by
        /// cosine-hemisphere sampling: with cosine-weighted directions, the irradiance
        /// gathered at a patch is simply the average radiosity of what the rays hit —
        /// the exact quantity the transfer sums compute, without the N² matrix.
        ///
        /// Everything is computed into locals and installed on the faces only at the
        /// very end, complete: an aborted bounce leaves the previous bounce visible, and
        /// the display can never dip to direct-only between bounce runs.
        /// </summary>
        /// <param name="all">
        /// Solve every face. When false only faces inside <paramref name="regionMin"/>..
        /// <paramref name="regionMax"/> (padded) are re-gathered; the rest keep the
        /// indirect light they already have, but still light the region — their radiosity
        /// is what the region's rays read. That is the local-first half of the design: an
        /// edit's colour bleed appears at once, and the authoritative whole-map solve
        /// follows in the background when the editor goes quiet.
        /// </param>
        private void RunBounce(List<BakeFace> faces, RayBvh bvh, int generation, int raysPerPatch,
                               bool all, Vector3 regionMin, Vector3 regionMax)
        {
            if (bvh == null)
                return;

            int n = faces.Count;

            LuxelGrid[] grids = new LuxelGrid[n];
            Vector3[][] emit = new Vector3[n][];
            Vector3[][] gathered = new Vector3[n][];
            Vector3[][] indirect = new Vector3[n][];
            bool[] solve = new bool[n];

            Vector3 padMin = Vector3.Zero, padMax = Vector3.Zero;
            if (!all)
                PaddedRegion(regionMin, regionMax, out padMin, out padMax);

            // 1. Patch grids seeded with direct light averaged from luxels. The lattice
            //    depends only on the face, so it is cached — rebuilding one per face per
            //    solve was most of the cost of a re-bounce on a large map.
            for (int f = 0; f < n; f++)
            {
                BakeFace face = faces[f];

                if (!face.WantsLightmap || face.Grid == null || face.Direct == null)
                    continue;

                LuxelGrid patches = face.PatchGridCache;

                if (patches == null)
                {
                    float patchSize = Math.Max(1, face.LightmapScale) * PatchCoarsen;
                    patches = BuildGrid(face, patchSize);
                    face.PatchGridCache = patches;
                }

                if (patches == null)
                    continue;

                int pcount = patches.Width * patches.Height;
                grids[f] = patches;
                emit[f] = new Vector3[pcount];

                if (all)
                {
                    solve[f] = true;
                }
                else
                {
                    Vector3 fMin, fMax;
                    FaceBounds(face, out fMin, out fMax);
                    solve[f] = BoxesTouch(fMin, fMax, padMin, padMax);
                }

                if (solve[f])
                {
                    gathered[f] = new Vector3[pcount];
                    indirect[f] = new Vector3[pcount];
                }

                for (int p = 0; p < pcount; p++)
                {
                    // First iteration shoots direct light scaled by reflectivity
                    // (vrad: emitlight = totallight; the shooter's reflectivity is
                    // folded in at shoot time — same product either way).
                    emit[f][p] = SampleGrid(face.Grid, face.Direct, patches.LatticePosition(p)) * face.Reflectivity;
                }
            }

            // 2. Iterate gathers. Parallel ACROSS faces (each face's writes are its own
            //    arrays; reads of emit[] are stable within an iteration), with the rare
            //    huge patch grid additionally split internally. On the GPU when one is
            //    tracing — this runs on the worker thread, where its context is current.
            GpuBatchOracle gpu = ResolveGpuOracle(bvh);

            for (int iteration = 0; iteration < BounceIterations; iteration++)
            {
                int iter = iteration;

                if (gpu != null)
                {
                    GatherOnGpu(faces, grids, emit, gathered, solve, n, iter, bvh, generation, raysPerPatch, gpu);
                }
                else
                {
                    Parallel.For(0, n, ParallelOpts, (f, state) =>
                    {
                        if (grids[f] == null || !solve[f])
                            return;

                        if (Volatile.Read(ref _generation) != generation)
                        {
                            state.Stop();
                            return;
                        }

                        GatherFacePatches(faces, grids, emit, gathered, f, n, iter, bvh, generation, raysPerPatch);
                    });
                }

                if (Volatile.Read(ref _generation) != generation)
                    return; // aborted — faces keep their previous bounce

                // Collect: indirect += gathered; next emission = gathered × reflectivity
                // (CollectLight/BounceLight: emitlight = addlight, totallight += addlight).
                for (int f = 0; f < n; f++)
                {
                    if (grids[f] == null)
                        continue;

                    Vector3 reflectivity = faces[f].Reflectivity;

                    if (!solve[f])
                    {
                        // Outside the region: this face is not re-gathering, but the next
                        // iteration's rays still have to see it re-emit. Its total indirect
                        // from the last full solve is exactly the radiosity a full solve
                        // would have it emitting, so use that and hold it fixed.
                        PatchLighting held = faces[f].Bounce;
                        int held_count = emit[f].Length;

                        for (int p = 0; p < held_count; p++)
                        {
                            emit[f][p] = held != null && held.Grid != null
                                ? SampleGrid(held.Grid, held.Indirect, grids[f].LatticePosition(p)) * reflectivity
                                : Vector3.Zero;
                        }

                        continue;
                    }

                    int pcount = indirect[f].Length;

                    for (int p = 0; p < pcount; p++)
                    {
                        indirect[f][p] += gathered[f][p];
                        emit[f][p] = gathered[f][p] * reflectivity;
                    }
                }
            }

            if (Volatile.Read(ref _generation) != generation)
                return;

            // 3. Install + republish, atomically per face. Publishing skips faces whose
            //    bytes didn't change, so a bounce refresh over a static scene is silent.
            long lastPublish = Environment.TickCount;

            for (int f = 0; f < n; f++)
            {
                if (grids[f] == null || !solve[f])
                    continue;

                BakeFace face = faces[f];

                Vector3[] smoothed = indirect[f];
                for (int pass = 0; pass < BounceSmoothPasses; pass++)
                    smoothed = SmoothPatches(smoothed, grids[f].Width, grids[f].Height);

                face.Bounce = new PatchLighting(grids[f], smoothed);
                Publish(face, face.BakedLevel);

                // A whole-map solve can take a while to walk; let the view see the faces
                // that are already done instead of waiting for the last one.
                long now = Environment.TickCount;
                if (now - lastPublish >= PublishIntervalMs)
                {
                    lastPublish = now;
                    RaiseResults();
                }
            }
        }

        /// <summary>
        /// Separable [1 2 1] tent over the patch grid.
        ///
        /// vrad does not read a luxel's indirect light from one patch either: it builds a
        /// <c>BuildPatchRadial</c> (radial.cpp) — a radial-basis blend of the patches
        /// AROUND the luxel — and samples that. This is the cheap equivalent, and since
        /// indirect light is low-frequency by nature it costs nothing real while removing
        /// what is left of the per-patch sampling error.
        /// </summary>
        private static Vector3[] SmoothPatches(Vector3[] data, int width, int height)
        {
            if (data == null || width < 3 || height < 3)
                return data;

            Vector3[] tmp = new Vector3[data.Length];
            Vector3[] outp = new Vector3[data.Length];

            // horizontal
            for (int t = 0; t < height; t++)
            {
                int row = t * width;

                for (int s = 0; s < width; s++)
                {
                    Vector3 left = data[row + (s > 0 ? s - 1 : 0)];
                    Vector3 right = data[row + (s < width - 1 ? s + 1 : width - 1)];
                    tmp[row + s] = (left + data[row + s] * 2.0f + right) * 0.25f;
                }
            }

            // vertical
            for (int t = 0; t < height; t++)
            {
                int row = t * width;
                int up = (t > 0 ? t - 1 : 0) * width;
                int down = (t < height - 1 ? t + 1 : height - 1) * width;

                for (int s = 0; s < width; s++)
                    outp[row + s] = (tmp[up + s] + tmp[row + s] * 2.0f + tmp[down + s]) * 0.25f;
            }

            return outp;
        }

        /// <summary>One face's patch gather for one bounce iteration (plain loop body).</summary>
        private void GatherFacePatches(List<BakeFace> faces, LuxelGrid[] grids, Vector3[][] emit,
                                       Vector3[][] gathered, int f, int n, int iteration,
                                       RayBvh bvh, int generation, int raysPerPatch)
        {
            BakeFace face = faces[f];
            LuxelGrid patches = grids[f];
            int pcount = patches.Width * patches.Height;

            Vector3[] faceDirs = SphereDirections.CosineHemisphereTable(face.Normal, raysPerPatch);

            for (int p = 0; p < pcount; p++)
            {
                if ((p & 63) == 0 && Volatile.Read(ref _generation) != generation)
                    return;

                Vector3 origin;
                Vector3[] dirs = PatchRays(face, patches, p, faceDirs, raysPerPatch, out origin);
                Vector3 sum = Vector3.Zero;

                for (int m = 0; m < dirs.Length; m++)
                {
                    Vector3 dir = dirs[m];

                    RayHit hit;
                    if (!bvh.ClosestHit(origin, dir, BounceRayLength, out hit))
                        continue;

                    if ((hit.Flags & RayTriangleFlags.Sky) != 0)
                        continue; // sky handled by the direct sky-ambient term

                    sum += GatheredFrom(faces, grids, emit, n, origin, dir, hit.Id, hit.T);
                }

                // cosine-weighted MC: E = average of emitted radiosity over rays
                gathered[f][p] = sum / dirs.Length;
            }
        }

        /// <summary>How far a bounce ray looks for the face it gathers from.</summary>
        private const float BounceRayLength = 1.0e6f;

        /// <summary>
        /// Where a patch gathers from, and along which directions. One direction set serves
        /// the whole face — see CosineHemisphereTable for why per-patch directions are what
        /// produced the blotching. A displacement is the exception: its patches face
        /// different ways, so each gets the same sequence rotated onto its own normal (still
        /// correlated between neighbours).
        /// </summary>
        private static Vector3[] PatchRays(BakeFace face, LuxelGrid patches, int p, Vector3[] faceDirs,
                                           int raysPerPatch, out Vector3 origin)
        {
            Vector3 patchNormal = face.Normal;
            Vector3[] dirs = faceDirs;

            if (patches.Normals != null)
            {
                patchNormal = patches.Normals[p];
                dirs = SphereDirections.CosineHemisphereTable(patchNormal, raysPerPatch);
            }

            origin = patches.Positions[p] + patchNormal;
            return dirs;
        }

        /// <summary>
        /// The light a bounce ray brings back from the face it hit — nothing for a hit on no
        /// face, or on the back of one, since light only leaves the front.
        /// </summary>
        private static Vector3 GatheredFrom(List<BakeFace> faces, LuxelGrid[] grids, Vector3[][] emit, int n,
                                           Vector3 origin, Vector3 dir, int hitFace, float t)
        {
            if (hitFace < 0 || hitFace >= n || grids[hitFace] == null)
                return Vector3.Zero;

            if (Vector3.Dot(faces[hitFace].Normal, dir) >= 0)
                return Vector3.Zero;

            return SampleGrid(grids[hitFace], emit[hitFace], origin + dir * t);
        }

        /// <summary>
        /// One bounce iteration's gather with its rays traced on the GPU: faces are packed a
        /// dispatch at a time, in parallel, traced from this (the worker) thread, then
        /// gathered from the answers in parallel. The rays are the CPU gather's exactly —
        /// same origins, directions and length — so the two differ only where the GPU's
        /// float arithmetic lands a hit differently.
        ///
        /// A dispatch that fails gathers its faces, and every face after them, on the CPU.
        /// </summary>
        private void GatherOnGpu(List<BakeFace> faces, LuxelGrid[] grids, Vector3[][] emit,
                                 Vector3[][] gathered, bool[] solve, int n, int iteration, RayBvh bvh,
                                 int generation, int raysPerPatch, GpuBatchOracle gpu)
        {
            List<int> chunk = new List<int>();
            List<int> offsets = new List<int>();
            int[] hitFaces = null;
            float[] hitDistances = null;
            int next = 0;

            while (next < n)
            {
                if (Volatile.Read(ref _generation) != generation)
                    return;

                // A dispatch's worth of whole faces; a face larger than that goes alone.
                chunk.Clear();
                offsets.Clear();
                int total = 0;

                for (; next < n; next++)
                {
                    if (grids[next] == null || !solve[next])
                        continue;

                    int rays = grids[next].Width * grids[next].Height * raysPerPatch;

                    if (total > 0 && total + rays > gpu.MaxRaysPerDispatch)
                        break;

                    chunk.Add(next);
                    offsets.Add(total);
                    total += rays;
                }

                if (chunk.Count == 0)
                    break;

                float[] staging = gpu != null ? gpu.ClosestStaging(total) : null;

                if (staging != null)
                {
                    Parallel.For(0, chunk.Count, ParallelOpts,
                        i => PackPatchRays(faces[chunk[i]], grids[chunk[i]], raysPerPatch, staging, offsets[i]));

                    if (hitFaces == null || hitFaces.Length < total)
                    {
                        hitFaces = new int[total];
                        hitDistances = new float[total];
                    }

                    if (!gpu.TraceClosest(total, hitFaces, hitDistances))
                        staging = null;
                }

                if (staging == null)
                {
                    // No GPU for this chunk — too large to stage, or the dispatch failed —
                    // and after a failure none for the rest of this bake either.
                    gpu = null;

                    Parallel.For(0, chunk.Count, ParallelOpts, i => GatherFacePatches(
                        faces, grids, emit, gathered, chunk[i], n, iteration, bvh, generation, raysPerPatch));
                    continue;
                }

                int[] answeredFaces = hitFaces;
                float[] answeredDistances = hitDistances;

                Parallel.For(0, chunk.Count, ParallelOpts, i => GatherPatchHits(
                    faces, grids, emit, gathered, chunk[i], n, raysPerPatch, answeredFaces, answeredDistances,
                    offsets[i]));
            }
        }

        /// <summary>Pack a face's bounce rays, patch by patch, from <paramref name="first"/>.</summary>
        private static void PackPatchRays(BakeFace face, LuxelGrid patches, int raysPerPatch, float[] staging, int first)
        {
            Vector3[] faceDirs = SphereDirections.CosineHemisphereTable(face.Normal, raysPerPatch);
            int pcount = patches.Width * patches.Height;
            int at = first;

            for (int p = 0; p < pcount; p++)
            {
                Vector3 origin;
                Vector3[] dirs = PatchRays(face, patches, p, faceDirs, raysPerPatch, out origin);

                for (int m = 0; m < dirs.Length; m++)
                {
                    GpuRayTracer.PackRay(staging, at++, origin, dirs[m], BounceRayLength, NoSkip,
                        GpuRayTracer.ModeClosestHit, RayTriangleFlags.None);
                }
            }
        }

        /// <summary>
        /// <see cref="GatherFacePatches"/> with the hits already traced: the same sum, over the
        /// same rays in the same order, read from the GPU's answers.
        /// </summary>
        private static void GatherPatchHits(List<BakeFace> faces, LuxelGrid[] grids, Vector3[][] emit,
                                            Vector3[][] gathered, int f, int n, int raysPerPatch,
                                            int[] hitFaces, float[] hitDistances, int first)
        {
            BakeFace face = faces[f];
            LuxelGrid patches = grids[f];
            int pcount = patches.Width * patches.Height;

            Vector3[] faceDirs = SphereDirections.CosineHemisphereTable(face.Normal, raysPerPatch);
            int at = first;

            for (int p = 0; p < pcount; p++)
            {
                Vector3 origin;
                Vector3[] dirs = PatchRays(face, patches, p, faceDirs, raysPerPatch, out origin);
                Vector3 sum = Vector3.Zero;

                for (int m = 0; m < dirs.Length; m++, at++)
                    sum += GatheredFrom(faces, grids, emit, n, origin, dirs[m], hitFaces[at], hitDistances[at]);

                gathered[f][p] = sum / dirs.Length;
            }
        }
        /// <summary>Bilinear lookup of a per-luxel/per-patch array at a world position.</summary>
        private static Vector3 SampleGrid(LuxelGrid grid, Vector3[] data, Vector3 worldPos)
        {
            float s = Vector3.Dot(worldPos, grid.AxisU) / grid.LuxelSize - grid.MinS;
            float t = Vector3.Dot(worldPos, grid.AxisV) / grid.LuxelSize - grid.MinT;

            s = Math.Max(0, Math.Min(grid.Width - 1.001f, s));
            t = Math.Max(0, Math.Min(grid.Height - 1.001f, t));

            int s0 = (int)s, t0 = (int)t;
            float fs = s - s0, ft = t - t0;

            int i00 = t0 * grid.Width + s0;
            int i10 = i00 + 1;
            int i01 = i00 + grid.Width;
            int i11 = i01 + 1;

            return data[i00] * ((1 - fs) * (1 - ft))
                 + data[i10] * (fs * (1 - ft))
                 + data[i01] * ((1 - fs) * ft)
                 + data[i11] * (fs * ft);
        }

        // ==================== publishing ====================

        /// <summary>
        /// Compose direct + patch-sampled indirect into the encoded texture snapshot.
        /// Indirect lives in world-anchored patch space, so it composes correctly with a
        /// direct grid of ANY pass level (a luxel-array indirect indexed into a
        /// different-sized grid was a flicker bug). Encoding is the engine's own
        /// (<see cref="SourceColorSpace.EncodeLuxel"/>); the shader applies ×OVERBRIGHT.
        ///
        /// A publish that produces byte-identical content to what the face already shows
        /// is dropped entirely — deterministic recomputes (settle sweep, bounce refresh)
        /// thus cause no texture uploads and no flicker.
        /// </summary>
        private void Publish(BakeFace face, int level)
        {
            LuxelGrid grid = face.Grid;
            Vector3[] direct = face.Direct;

            if (grid == null || direct == null)
                return;

            PatchLighting bounce = face.Bounce;

            int count = grid.Width * grid.Height;
            byte[] rgb = new byte[count * 3];

            for (int i = 0; i < count; i++)
            {
                Vector3 v = direct[i];

                if (bounce != null && bounce.Grid != null)
                    v += SampleGrid(bounce.Grid, bounce.Indirect, grid.LatticePosition(i));

                rgb[i * 3 + 0] = Encode(v.X);
                rgb[i * 3 + 1] = Encode(v.Y);
                rgb[i * 3 + 2] = Encode(v.Z);
            }

            LightmapResult previous = face.Result;

            if (previous != null
                && previous.Width == grid.Width && previous.Height == grid.Height
                && previous.MinS == grid.MinS && previous.MinT == grid.MinT
                && previous.LuxelSize == grid.LuxelSize
                && BytesEqual(previous.Rgb, rgb))
            {
                return; // nothing visibly changed — don't touch the texture
            }

            Interlocked.Increment(ref _publishCount);

            face.Result = new LightmapResult
            {
                Width = grid.Width,
                Height = grid.Height,
                Rgb = rgb,
                AxisU = grid.AxisU,
                AxisV = grid.AxisV,
                LuxelSize = grid.LuxelSize,
                MinS = grid.MinS,
                MinT = grid.MinT,
                Level = level,
                Version = Interlocked.Increment(ref _version),
            };
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// linear vrad units → the exact byte the engine would put in the lightmap
        /// texture (<see cref="SourceColorSpace.EncodeLuxel"/>). The shader then applies
        /// the engine's ×OVERBRIGHT, so preview pixels == in-game pixels.
        /// </summary>
        private static byte Encode(float linear255)
        {
            return SourceColorSpace.EncodeLuxel(linear255);
        }
    }

    /// <summary>
    /// Visibility for a probe that sits inside its own model (a static prop's centre): every
    /// query looks straight through that model's triangles, so the prop neither shadows nor
    /// hides the sky from its own ambient cube. Other geometry blocks as usual.
    /// </summary>
    internal sealed class ProbeRayOracle : IRayOracle
    {
        private readonly RayBvh _bvh;
        private readonly int _skip;

        public ProbeRayOracle(RayBvh bvh, int skip)
        {
            _bvh = bvh;
            _skip = skip;
        }

        public bool SupportsConcurrentUse { get { return true; } }

        public bool AnyHit(Vector3 origin, Vector3 direction, float tmax, int skipId, RayTriangleFlags ignoreFlags)
        {
            return _bvh.AnyHit(origin, direction, tmax, _skip, ignoreFlags);
        }

        public bool ReachesSky(Vector3 origin, Vector3 direction, float tmax)
        {
            Hit hit;

            if (!ClosestHitPast(_bvh, origin, direction, _skip, out hit, tmax))
                return true;   // left the map, as BvhRayOracle

            return (hit.Flags & RayTriangleFlags.Sky) != 0;
        }

        /// <summary>A probe's closest-hit query, stepping past any triangle tagged <paramref name="skip"/>.</summary>
        public struct Hit
        {
            public int Id;
            public RayTriangleFlags Flags;
            public Vector3 Point;
        }

        public static bool ClosestHitPast(RayBvh bvh, Vector3 origin, Vector3 direction, int skip, out Hit result,
                                          float tmax = 1.0e6f)
        {
            result = new Hit { Id = -1 };
            Vector3 from = origin;
            float remaining = tmax;

            // A few steps: a ray can leave a closed model through several of its triangles.
            for (int step = 0; step < 8; step++)
            {
                RayHit hit;
                if (!bvh.ClosestHit(from, direction, remaining, out hit))
                    return false;

                Vector3 point = from + direction * hit.T;

                if (hit.Id == skip && skip != int.MinValue)
                {
                    const float Nudge = 0.05f;
                    from = point + direction * Nudge;
                    remaining -= hit.T + Nudge;

                    if (remaining <= 0)
                        return false;

                    continue;
                }

                result = new Hit { Id = hit.Id, Flags = hit.Flags, Point = point };
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Deterministic direction tables — vrad's DirectionalSampler_t role. Fixed tables
    /// keep the lightmaps noise-free across luxels (every luxel integrates the same
    /// directions), exactly like vrad's shared sampler sequence — and make recomputes
    /// reproduce identical bytes, which the baker's publish-if-different logic relies on.
    /// </summary>
    internal static class SphereDirections
    {
        /// <summary>
        /// Copy-on-write: asked once per luxel from every bake thread, so a hit must not
        /// take a lock — a shared one serialised the parallel face bakes on it. Only a miss,
        /// which happens once per table size, locks and publishes a new dictionary.
        /// </summary>
        private static volatile Dictionary<int, Vector3[]> _tables = new Dictionary<int, Vector3[]>();
        private static readonly object _gate = new object();

        /// <summary>N roughly-uniform directions on the full sphere (Fibonacci spiral).</summary>
        public static Vector3[] Table(int n)
        {
            Vector3[] table;
            if (_tables.TryGetValue(n, out table))
                return table;

            lock (_gate)
            {
                if (_tables.TryGetValue(n, out table))
                    return table;

                table = new Vector3[n];
                double golden = Math.PI * (3.0 - Math.Sqrt(5.0));

                for (int i = 0; i < n; i++)
                {
                    double z = 1.0 - 2.0 * (i + 0.5) / n;
                    double r = Math.Sqrt(Math.Max(0, 1.0 - z * z));
                    double phi = golden * i;
                    table[i] = new Vector3((float)(r * Math.Cos(phi)), (float)(r * Math.Sin(phi)), (float)z);
                }

                Dictionary<int, Vector3[]> next = new Dictionary<int, Vector3[]>(_tables);
                next[n] = table;
                _tables = next;
                return table;
            }
        }

        /// <summary>Deterministic pseudo-random unit vector from an integer (sun jitter).</summary>
        public static Vector3 Hash(int seed)
        {
            float x = Fract(seed * 0.1031f);
            float y = Fract(seed * 0.11369f);
            float z = Fract(seed * 0.13787f);

            Vector3 v = new Vector3(x, y, z) * 2.0f - Vector3.One;
            float len = v.Length();
            return len > 1e-6f ? v / len : new Vector3(0, 0, 1);
        }

        private static float Fract(float v)
        {
            double s = Math.Sin(v * 43758.5453);
            return (float)(s - Math.Floor(s));
        }

        /// <summary>
        /// N cosine-weighted directions on the hemisphere around <paramref name="normal"/>,
        /// from a fixed low-discrepancy sequence (Vogel spiral on the disc lifted by
        /// Malley's method — <c>r = sqrt((i+½)/n)</c> is exactly the cosine-weighted
        /// radial CDF).
        ///
        /// Crucially this is the SAME set for every patch sharing a normal. Drawing a
        /// different random set per patch — which is what this used to do — leaves each
        /// patch with an independent estimate of the same smooth integral, and the
        /// difference between neighbours shows up as salt-and-pepper speckle that the
        /// bilinear upsample turns into star-shaped blotches. Sharing the directions
        /// correlates neighbours, so what is left of the sampling error varies smoothly
        /// across the surface instead of per patch. It is also what vrad effectively does
        /// everywhere it samples: <c>DirectionalSampler_t</c> is constructed inside the
        /// function, so every call walks the identical sequence.
        /// </summary>
        public static Vector3[] CosineHemisphereTable(Vector3 normal, int n)
        {
            // Orthonormal basis around the normal.
            Vector3 tangent = Math.Abs(normal.Z) < 0.999f
                ? Vector3.Normalize(Vector3.Cross(new Vector3(0, 0, 1), normal))
                : new Vector3(1, 0, 0);
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            Vector3[] dirs = new Vector3[n];
            double golden = Math.PI * (3.0 - Math.Sqrt(5.0));

            for (int i = 0; i < n; i++)
            {
                double u = (i + 0.5) / n;

                float r = (float)Math.Sqrt(u);
                double phi = golden * i;

                float x = r * (float)Math.Cos(phi);
                float y = r * (float)Math.Sin(phi);
                float z = (float)Math.Sqrt(Math.Max(0, 1.0 - u));

                dirs[i] = Vector3.Normalize(tangent * x + bitangent * y + normal * z);
            }

            return dirs;
        }
    }
}
