using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The two visibility questions the direct-light pass ever asks, behind an interface
    /// so the answers can come from somewhere other than a per-ray BVH descent.
    ///
    /// This is the seam the GPU bake needs. The lighting math — falloff, cones, colour,
    /// the sky and ambient-occlusion integrals — is what has to match VRAD, and it is
    /// arithmetically cheap; what costs is the millions of tree descents behind these two
    /// calls. Routing them through here lets a batched implementation answer the same
    /// questions from a compute dispatch without the lighting above it changing at all,
    /// so the GPU path cannot drift from the CPU path's lighting.
    ///
    /// Implementations are used from several worker threads at once, so they must be
    /// safe to call concurrently or be per-thread by construction.
    /// </summary>
    public interface IRayOracle
    {
        /// <summary>
        /// Is anything hit in (0, tmax)? The shadow-ray question — see
        /// <see cref="RayBvh.AnyHit"/>, whose epsilons and skip rules this reproduces.
        /// </summary>
        bool AnyHit(Vector3 origin, Vector3 direction, float tmax, int skipId,
                    RayTriangleFlags ignoreFlags);

        /// <summary>
        /// Does the ray reach the sky — nothing hit at all (a leaked map, which vrad
        /// treats leniently), or the closest thing hit is a sky face?
        ///
        /// One call rather than exposing a closest-hit result, because that result's only
        /// use here is this test. Keeping it a boolean is what lets a GPU implementation
        /// answer it with one bit per ray instead of a hit record.
        ///
        /// Deliberately has no skip id, unlike <see cref="AnyHit"/>: vrad's sun and sky
        /// integrals trace against everything, and the sample point is already lifted off
        /// its own face. Taking one would be an invitation for a CPU implementation to
        /// ignore it and a GPU one to honour it — a divergence of exactly one face, on
        /// grazing rays only, which is the kind that survives review.
        /// </summary>
        bool ReachesSky(Vector3 origin, Vector3 direction, float tmax);

        /// <summary>
        /// Whether several threads may query this at once. A per-ray tracer can; a batched
        /// one carries a cursor and cannot, so the baker bakes that face's luxels serially
        /// and lets the batch supply the parallelism instead.
        /// </summary>
        bool SupportsConcurrentUse { get; }
    }

    /// <summary>
    /// The oracle that traces, one ray at a time, on the calling thread — the behaviour
    /// the baker had before the seam existed, and the fallback whenever the GPU path is
    /// unavailable or declines.
    ///
    /// A null BVH answers "nothing in the way, and the sky is visible": that is the empty
    /// scene, and it keeps every caller free of null checks that used to be spread across
    /// the sample functions unevenly.
    /// </summary>
    public sealed class BvhRayOracle : IRayOracle
    {
        private readonly RayBvh _bvh;

        public BvhRayOracle(RayBvh bvh)
        {
            _bvh = bvh;
        }

        /// <summary>The BVH is immutable once built, so any number of threads may trace it.</summary>
        public bool SupportsConcurrentUse { get { return true; } }

        public bool AnyHit(Vector3 origin, Vector3 direction, float tmax, int skipId,
                           RayTriangleFlags ignoreFlags)
        {
            return _bvh != null && _bvh.AnyHit(origin, direction, tmax, skipId, ignoreFlags);
        }

        public bool ReachesSky(Vector3 origin, Vector3 direction, float tmax)
        {
            if (_bvh == null)
                return true;

            RayHit hit;

            if (!_bvh.ClosestHit(origin, direction, tmax, out hit))
                return true;   // left the map — leaked-map leniency, as vrad does

            return (hit.Flags & RayTriangleFlags.Sky) != 0;
        }
    }
}
