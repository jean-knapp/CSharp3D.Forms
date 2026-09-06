// Shared between every stage of the ray tracer. Pasted in by ShaderCompiler where a stage
// says #include "common.glsl"; keep this file free of anything stage-specific.
//
// Layouts are scalar (GL_EXT_scalar_block_layout) so the C# structs match byte for byte
// with no std430 padding rules applied by hand. Every struct here has a twin in
// GpuScene.cs; change one and change the other.

#extension GL_EXT_ray_tracing : require
#extension GL_EXT_nonuniform_qualifier : require
#extension GL_EXT_scalar_block_layout : require
#extension GL_EXT_buffer_reference2 : require
#extension GL_EXT_shader_explicit_arithmetic_types_int64 : require

// ---- geometry ----------------------------------------------------------------------------

// One per geometry the acceleration structures know: a world face in the world BLAS, or a
// prop's whole BLAS. Found from gl_InstanceCustomIndexEXT (the instance's first record) plus
// gl_GeometryIndexEXT.
struct GeometryRecord
{
    uint64_t vertexAddress;   // Vertex[] - 8 floats each: position, normal, uv (GL space)
    uint64_t indexAddress;    // uint[]
    uint materialIndex;
    uint pad0, pad1, pad2;
};

layout(buffer_reference, scalar) buffer VertexBuffer { float v[]; };
layout(buffer_reference, scalar) buffer IndexBuffer  { uint  i[]; };

// ---- materials ---------------------------------------------------------------------------

const uint MATERIAL_SKY        = 1u;   // a sky face: rays through it see the sky
const uint MATERIAL_UNLIT      = 2u;   // drawn as its texture, no lighting
const uint MATERIAL_TRANSLUCENT = 4u;

struct MaterialRecord
{
    vec4 color;               // tint, straight multiply on the albedo; a = alpha
    int albedoTexture;        // index into textures[], -1 for none
    uint flags;
    float roughness;
    float metallic;
};

// ---- lights --------------------------------------------------------------------------------

const uint LIGHT_POINT = 0u;
const uint LIGHT_SPOT  = 1u;
const uint LIGHT_SUN   = 2u;

// Lambda Engine's lights, in Unreal's terms: a point or spot light is so many candela with an
// attenuation radius, the sun is so many lux. Positions and directions are in GL space.
struct Light
{
    vec4 positionRadius;      // xyz position, w attenuation radius (world units)
    vec4 directionCone;       // xyz beam direction, w cos(outer cone)
    vec4 radiance;            // rgb linear colour x intensity
    vec4 params;              // x cos(inner cone), y type, z sun angular radius (rad), w unused
};

// ---- per frame -----------------------------------------------------------------------------

struct FrameData
{
    mat4 invViewProj;         // clip -> world (GL space)
    mat4 prevViewProj;        // world -> clip of the previous frame, to find a point's old pixel
    vec4 cameraPosition;      // xyz, w unused
    vec4 sky;                 // rgb sky radiance (skylight colour x intensity), w unused
    uvec4 counts;             // x frame index (random seed; bit 0 is the history parity), y light count, z samples since the camera settled, w flags
    vec4 units;               // x world units per metre, y bounces, z max lights per sample, w samples per frame
    vec4 history;             // x most history an indirect pixel keeps, y the same for direct, z 1 when the camera is still, w unused
};

const uint FRAME_RESET = 1u;   // the scene changed: no pixel may keep its history

// ---- payloads ------------------------------------------------------------------------------

// What the closest-hit stage hands back. Everything is done in raygen - the hit stage only
// says what was hit - so no stage ever recurses and the pipeline needs a depth of one.
struct HitPayload
{
    float t;                  // distance along the ray; < 0 for a miss
    vec3 position;            // world space
    vec3 normal;              // shading normal, world space, unit
    vec3 geometricNormal;     // face normal, world space, unit
    vec2 uv;
    uint materialIndex;
    float uvDensity;          // texture-space length per world unit on the triangle, for the mip level
};

// ---- random --------------------------------------------------------------------------------

// PCG, seeded per pixel per frame. Cheap and good enough for a progressive estimator.
uint pcgHash(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float rand(inout uint seed)
{
    seed = pcgHash(seed);
    return float(seed) * (1.0 / 4294967296.0);
}

// A cosine-weighted direction around n; the pdf is cos/pi, which cancels the Lambert term.
vec3 cosineSample(vec3 n, inout uint seed)
{
    float r1 = rand(seed);
    float r2 = rand(seed);

    float phi = 6.28318530718 * r1;
    float r = sqrt(r2);

    vec3 t = normalize(abs(n.x) > 0.5 ? cross(n, vec3(0.0, 1.0, 0.0)) : cross(n, vec3(1.0, 0.0, 0.0)));
    vec3 b = cross(n, t);

    return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(max(0.0, 1.0 - r2)));
}

// A direction within a cone of half-angle `angle` around axis d, for a soft sun.
vec3 coneSample(vec3 d, float angle, inout uint seed)
{
    float r1 = rand(seed);
    float r2 = rand(seed);

    float cosTheta = 1.0 - r1 * (1.0 - cos(angle));
    float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
    float phi = 6.28318530718 * r2;

    vec3 t = normalize(abs(d.x) > 0.5 ? cross(d, vec3(0.0, 1.0, 0.0)) : cross(d, vec3(1.0, 0.0, 0.0)));
    vec3 b = cross(d, t);

    return normalize(t * (sinTheta * cos(phi)) + b * (sinTheta * sin(phi)) + d * cosTheta);
}

// ---- colour --------------------------------------------------------------------------------

vec3 srgbToLinear(vec3 c)
{
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
