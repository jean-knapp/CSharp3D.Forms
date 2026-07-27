#version 430 core

// Shadow-ray occlusion for the lightmap bake: one invocation per ray, answering the
// same question RayBvh.AnyHit answers on the CPU — "is anything hit in (0, tmax)?".
//
// Deliberately ONLY the traversal. The light model (falloff curves, cone angles, colour,
// clustering) stays in C#, because that is the part that has to match VRAD exactly and it
// is arithmetically cheap; what costs is casting millions of rays through the BVH, and
// that is what this does. Keeping the split here means the GPU path cannot drift from the
// CPU path's lighting, only compute the same visibility faster.
//
// Every epsilon and sign below is copied from RayBvh, not re-derived. A ray that the two
// paths disagree about would show up as a single luxel flickering between light and shade
// as the bake alternates between them.

layout(local_size_x = 64) in;

// vec4-packed so std430 needs no padding rules applied by hand: xyz is the vector, w
// carries an int through floatBitsToInt/intBitsToFloat rather than a second member that
// would round the stride up to 32 bytes anyway.
struct Node
{
    vec4 boundsMin;   // xyz = min, w = leftOrStart (as int bits)
    vec4 boundsMax;   // xyz = max, w = count (as int bits); count 0 means interior
};

struct Tri
{
    vec4 v0;          // xyz = vertex 0, w = id (as int bits)
    vec4 v1;          // xyz = vertex 1, w = flags (as int bits)
    vec4 v2;          // xyz = vertex 2, w unused
};

struct Ray
{
    vec4 originTmax;  // xyz = origin, w = tmax
    vec4 dirSkip;     // xyz = direction, w = skipId (as int bits)
    vec4 modeFlags;   // x = mode (as int bits), y = ignoreFlags (as int bits), zw unused
};

/// Is anything hit at all? (IRayOracle.AnyHit — a shadow ray.)
const int MODE_ANY_HIT = 0;

/// Does the ray reach the sky: nothing hit, or the CLOSEST hit is a sky face?
/// (IRayOracle.ReachesSky — the sun and sky-dome integrals.) Needs the nearest hit
/// rather than any hit, so it cannot share the early-out below.
const int MODE_REACHES_SKY = 1;

/// Mirrors RayTriangleFlags.Sky.
const int FLAG_SKY = 1;

layout(std430, binding = 0) readonly  buffer NodeBuffer   { Node nodes[]; };
layout(std430, binding = 1) readonly  buffer TriBuffer    { Tri  tris[];  };
layout(std430, binding = 2) readonly  buffer RayBuffer    { Ray  rays[];  };
layout(std430, binding = 3) writeonly buffer ResultBuffer { uint hits[];  };

uniform int uRayCount;

/// Deep enough for a median-split BVH over any map this previews: the tree is balanced by
/// construction, so depth is ceil(log2(tris/4)) — 64 covers 7e19 triangles. The CPU side
/// grows its stack because it cannot know; here a fixed array keeps the whole thing in
/// registers.
const int STACK_SIZE = 64;

vec3 safeInverse(vec3 d)
{
    // Matches RayBvh.SafeInverse: never divide by zero, and keep the sign so the slab
    // test still orders the planes correctly for an axis-aligned ray.
    const float tiny = 1e-12;

    vec3 s = vec3(
        abs(d.x) < tiny ? (d.x < 0.0 ? -tiny : tiny) : d.x,
        abs(d.y) < tiny ? (d.y < 0.0 ? -tiny : tiny) : d.y,
        abs(d.z) < tiny ? (d.z < 0.0 ? -tiny : tiny) : d.z);

    return 1.0 / s;
}

bool boxHit(vec3 bmin, vec3 bmax, vec3 origin, vec3 invDir, float tmax)
{
    vec3 t1 = (bmin - origin) * invDir;
    vec3 t2 = (bmax - origin) * invDir;

    vec3 lo = min(t1, t2);
    vec3 hi = max(t1, t2);

    float tmin = max(max(lo.x, lo.y), lo.z);
    float tmx  = min(min(hi.x, hi.y), hi.z);

    return tmx >= max(tmin, 0.0) && tmin <= tmax;
}

/// Moller-Trumbore, two-sided — light is blocked from either side of a face.
bool triHit(vec3 a, vec3 b, vec3 c, vec3 origin, vec3 direction, float tmax, out float t)
{
    t = 0.0;

    vec3 e1 = b - a;
    vec3 e2 = c - a;
    vec3 p = cross(direction, e2);
    float det = dot(e1, p);

    if (det > -1e-9 && det < 1e-9)
        return false;

    float invDet = 1.0 / det;
    vec3 s = origin - a;
    float u = dot(s, p) * invDet;

    if (u < -1e-5 || u > 1.0 + 1e-5)
        return false;

    vec3 q = cross(s, e1);
    float v = dot(direction, q) * invDet;

    if (v < -1e-5 || u + v > 1.0 + 1e-5)
        return false;

    t = dot(e2, q) * invDet;
    return t > 0.0 && t < tmax;
}

void main()
{
    uint index = gl_GlobalInvocationID.x;

    if (index >= uint(uRayCount))
        return;

    Ray ray = rays[index];

    vec3 origin = ray.originTmax.xyz;
    float tmax = ray.originTmax.w;
    vec3 direction = ray.dirSkip.xyz;
    int skipId = floatBitsToInt(ray.dirSkip.w);
    int mode = floatBitsToInt(ray.modeFlags.x);
    int ignoreFlags = floatBitsToInt(ray.modeFlags.y);

    vec3 invDir = safeInverse(direction);

    int stack[STACK_SIZE];
    int sp = 0;
    stack[sp++] = 0;

    bool anyHit = false;

    // Closest-hit state, only meaningful in MODE_REACHES_SKY. Shrinking the search
    // distance as hits are found is what makes it converge on the nearest one, exactly as
    // RayBvh.ClosestHit does with hit.T.
    float nearestT = tmax;
    int nearestFlags = 0;

    while (sp > 0)
    {
        int nodeIndex = stack[--sp];
        Node node = nodes[nodeIndex];

        // MODE_ANY_HIT never narrows, so it tests against the original tmax; the sky mode
        // tests against the nearest hit so far and skips boxes entirely behind it.
        if (!boxHit(node.boundsMin.xyz, node.boundsMax.xyz, origin, invDir, nearestT))
            continue;

        int count = floatBitsToInt(node.boundsMax.w);
        int leftOrStart = floatBitsToInt(node.boundsMin.w);

        if (count > 0)
        {
            int end = leftOrStart + count;

            for (int i = leftOrStart; i < end; i++)
            {
                Tri tri = tris[i];

                if (floatBitsToInt(tri.v0.w) == skipId)
                    continue;

                if (ignoreFlags != 0 && (floatBitsToInt(tri.v1.w) & ignoreFlags) != 0)
                    continue;

                float t;

                // The t > 1e-4 guard is the CPU path's, and it is what stops a luxel
                // sitting exactly on its own face from shadowing itself.
                if (!triHit(tri.v0.xyz, tri.v1.xyz, tri.v2.xyz, origin, direction, nearestT, t) || t <= 1e-4)
                    continue;

                anyHit = true;

                if (mode == MODE_ANY_HIT)
                    break;              // one hit is the whole answer

                nearestT = t;
                nearestFlags = floatBitsToInt(tri.v1.w);
            }

            if (anyHit && mode == MODE_ANY_HIT)
                break;
        }
        else if (sp + 2 <= STACK_SIZE)
        {
            stack[sp++] = leftOrStart;
            stack[sp++] = leftOrStart + 1;
        }
    }

    if (mode == MODE_REACHES_SKY)
    {
        // Nothing hit means the ray left the map, which vrad treats as sky (leaked-map
        // leniency); otherwise only a sky face counts.
        hits[index] = (!anyHit || (nearestFlags & FLAG_SKY) != 0) ? 1u : 0u;
    }
    else
    {
        hits[index] = anyHit ? 1u : 0u;
    }
}
