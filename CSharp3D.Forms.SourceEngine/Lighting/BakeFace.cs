using System;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// One face handed to the <see cref="LightmapBaker"/>: geometry + lightmap basis in,
    /// a published <see cref="LightmapResult"/> out. The baker owns all internal state;
    /// the UI thread only ever reads <see cref="Result"/> (an immutable snapshot swapped
    /// atomically).
    ///
    /// Instances are long-lived across incremental scene updates: a face whose geometry
    /// did not change is passed in again as the SAME object, keeping its baked state so
    /// it neither rebakes nor republishes (the core of flicker-free incremental updates).
    /// </summary>
    public class BakeFace
    {
        /// <summary>Caller's identity for this face (the editor's MapFace).</summary>
        public object Key;

        private static long _nextIdentity;

        /// <summary>
        /// Identity that survives an edit: a modified face is handed to the baker as a
        /// NEW <see cref="BakeFace"/>, and <see cref="InheritFrom"/> carries this across.
        /// It lets the baker tell "the same face moved" (list indices still mean the same
        /// faces, so a BVH built before the edit is still usable) from "the face list
        /// changed shape" (indices shifted, the BVH's triangle ids are meaningless).
        /// </summary>
        internal long Identity = System.Threading.Interlocked.Increment(ref _nextIdentity);

        /// <summary>Face polygon, world space, outward winding.</summary>
        public Vector3[] Winding;

        /// <summary>Unit outward plane normal.</summary>
        public Vector3 Normal;

        /// <summary>Raw VMF uaxis/vaxis (need not be normalized).</summary>
        public Vector3 AxisU, AxisV;

        /// <summary>The face's lightmapscale in world units per luxel (VMF default 16).</summary>
        public float LightmapScale = 16;

        /// <summary>Sky-textured face: target of sun/sky rays, no lightmap of its own.</summary>
        public bool IsSky;

        /// <summary>Whether the face occludes light (false for trigger/skip/hint etc.).</summary>
        public bool BlocksLight = true;

        /// <summary>Whether the face gets a lightmap (false for sky and tool textures).</summary>
        public bool WantsLightmap = true;

        /// <summary>
        /// Average diffuse reflectivity of the face's material in linear [0,1] RGB —
        /// vrad reads it from the VTF's stored reflectivity; the editor approximates
        /// from the cached texture bitmap. Drives bounce color.
        /// </summary>
        public Vector3 Reflectivity = new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>
        /// Light this face's material emits, straight from lights.rad in vrad units
        /// (<see cref="RadFile.LightForTexture"/>). Zero for ordinary materials. When
        /// nonzero the baker turns the face into a grid of <c>emit_surface</c> lights,
        /// exactly as CreateDirectLights does for every texlight patch.
        /// </summary>
        public Vector3 EmitColor;

        /// <summary>
        /// Material dimensions in texels and the face's texture scale in world units per
        /// texel (the VMF uaxis/vaxis scale). Together they give the world size of one
        /// texture tile, which is what converts <see cref="EmitColor"/> — a per-tile
        /// brightness — into per-patch intensity in <c>BaseLightForFace</c>'s terms.
        /// </summary>
        public float TextureWidth = 1, TextureHeight = 1;

        public float TextureScaleU = 1, TextureScaleV = 1;

        // ---- displacement (null/0 for an ordinary brush face) ----

        /// <summary>
        /// Displaced vertices in world space, <c>row * DispDim + col</c>, where row runs
        /// from base corner 0 toward corner 1 and col from corner 0 toward corner 3 (see
        /// <see cref="DispCorners"/>). Non-null makes this face a displacement: it receives
        /// light on the displaced surface and its triangles, not its base quad, cast shadows.
        /// <see cref="Winding"/> stays the base quad — the lightmap is laid out over it.
        /// </summary>
        public Vector3[] DispPositions;

        /// <summary>Per-vertex surface normals, same layout as <see cref="DispPositions"/>.</summary>
        public Vector3[] DispNormals;

        /// <summary>Vertices per side, <c>2^power + 1</c>.</summary>
        public int DispDim;

        /// <summary>The base quad in displacement corner order ([0,0], [D,0], [D,D], [0,D]).</summary>
        public Vector3[] DispCorners;

        public bool IsDisplacement
        {
            get { return DispPositions != null && DispDim >= 2 && DispCorners != null && DispCorners.Length == 4; }
        }

        /// <summary>
        /// The points that bound the surface light actually lands on: the displaced grid for
        /// a displacement, the winding otherwise.
        /// </summary>
        internal Vector3[] SurfacePoints
        {
            get { return IsDisplacement ? DispPositions : Winding; }
        }

        // ---- baker-internal state ----

        /// <summary>
        /// Position in the baker's current face list (= this face's triangle id in the
        /// BVH, and the self-shadow skip id). -1 once the face has been removed from the
        /// scene, which is how stale queue entries are recognized and dropped.
        /// </summary>
        internal int Index = -1;

        internal LuxelGrid Grid;

        /// <summary>Direct light per luxel of <see cref="Grid"/>, linear vrad units.</summary>
        internal Vector3[] Direct;

        /// <summary>Highest completed pass level (-1 = nothing baked yet).</summary>
        internal int BakedLevel = -1;

        /// <summary>
        /// Bounce output: indirect light per patch, plus the patch grid it is indexed by,
        /// as ONE immutable object. Patch space is world-anchored and independent of the
        /// direct grid's resolution, so <see cref="LightmapBaker"/> resamples it per
        /// publish — mixing bounce from one solve with direct light of any pass level
        /// stays correct. Swapped in complete (never cleared first), so the displayed
        /// image cannot dip to direct-only between bounce runs; grid and data travel
        /// together so a publish racing a solve can never pair one solve's grid with
        /// another's data.
        /// </summary>
        internal volatile PatchLighting Bounce;

        /// <summary>
        /// The patch lattice for this face's geometry, cached across bounce solves —
        /// it depends only on the winding, and rebuilding it for every face on every
        /// solve was the bulk of a re-bounce's cost on a large map.
        /// </summary>
        internal LuxelGrid PatchGridCache;

        /// <summary>
        /// The <c>emit_surface</c> lights this face expands into (empty for a
        /// non-emissive material), cached because they too depend only on the face.
        /// A geometry edit makes new BakeFace objects for the faces that changed, so
        /// their caches are empty and only they pay to rebuild.
        /// </summary>
        internal System.Collections.Generic.List<SourceLight> SurfaceEmitters;

        /// <summary>Published, immutable snapshot for the renderer. Atomic swap.</summary>
        public volatile LightmapResult Result;

        /// <summary>
        /// Take over the display state of the previous incarnation of this face (the
        /// object the baker held before this edit replaced it).
        ///
        /// The lightmap itself is "stale until replaced" — plan §3. The BOUNCE has to
        /// come with it: without it, the face's first republish composes direct light
        /// alone, which reads as the face suddenly going dark and only recovering when
        /// the next whole-map bounce solve lands seconds later. Patch space is
        /// world-anchored and sampling clamps at the grid edge, so inheriting it is
        /// exactly as safe as inheriting the lightmap.
        /// </summary>
        public void InheritFrom(BakeFace previous)
        {
            if (previous == null || ReferenceEquals(previous, this))
                return;

            Identity = previous.Identity;
            Result = previous.Result;
            Bounce = previous.Bounce;
        }
    }

    /// <summary>
    /// A point the baker computes an ambient cube at — one per lit model. The editor reads
    /// <see cref="Cube"/> (an immutable array swapped atomically) and lights the model with
    /// it the way Source lights props at run time.
    /// </summary>
    public sealed class LightProbe
    {
        /// <summary>Caller's identity for the probe (the model's entity).</summary>
        public object Key;

        /// <summary>Where the light is gathered, world space (the model's centre).</summary>
        public Vector3 Position;

        /// <summary>
        /// Occluder id of the model's own surface, so a static prop does not shadow its own
        /// probe; <c>int.MinValue</c> when the model casts no shadow.
        /// </summary>
        public int SkipId = int.MinValue;

        /// <summary>
        /// Light arriving from +X, −X, +Y, −Y, +Z, −Z in linear vrad units (255 = white), or
        /// null until first computed. Swapped whole.
        /// </summary>
        public volatile Vector3[] Cube;

        /// <summary>Bumped whenever <see cref="Cube"/> is replaced.</summary>
        public volatile int Version;
    }

    /// <summary>
    /// One face's indirect light and the patch lattice it is indexed by, immutable so the
    /// pair can be swapped atomically (see <see cref="BakeFace.Bounce"/>).
    /// </summary>
    internal sealed class PatchLighting
    {
        public readonly LuxelGrid Grid;
        public readonly Vector3[] Indirect;

        public PatchLighting(LuxelGrid grid, Vector3[] indirect)
        {
            Grid = grid;
            Indirect = indirect;
        }
    }

    /// <summary>
    /// An immutable baked lightmap for one face. Rgb holds exactly the bytes the Source
    /// engine would store (<see cref="SourceColorSpace.EncodeLuxel"/>); the shader
    /// applies the engine's ×OVERBRIGHT on the way out.
    /// </summary>
    public class LightmapResult
    {
        public int Width, Height;
        public byte[] Rgb;

        /// <summary>Normalized lightmap axes (world space) for UV reconstruction.</summary>
        public Vector3 AxisU, AxisV;

        public float LuxelSize;
        public int MinS, MinT;

        /// <summary>Bake pass level this snapshot came from (0 coarse … 2 final).</summary>
        public int Level;

        /// <summary>Monotonic counter so the renderer knows to re-upload.</summary>
        public int Version;
    }
}
