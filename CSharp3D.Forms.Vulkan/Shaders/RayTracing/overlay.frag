#version 460
#extension GL_EXT_nonuniform_qualifier : require

// The overlay's fragment: the mesh's colour (times its icon texture), depth tested by hand
// against the distance the camera ray travelled to the surface under this pixel. There is
// no depth attachment - the ray tracer's history images already say how far the world is.

layout(push_constant) uniform Push
{
    mat4 viewProj;
    vec4 right;
    vec4 up;
    vec4 cameraPos;
    uvec4 flags;         // x history parity, y depth mode, z dotted, w texture index (all ones: none)
} push;

layout(set = 0, binding = 0, rgba32f) uniform readonly image2D positions[2];   // w: 1 where a ray hit something
layout(set = 0, binding = 1, rgba16f) uniform readonly image2D normals[2];     // w: how far the hit is from the eye
layout(set = 0, binding = 2) uniform sampler2D textures[];

layout(location = 0) in vec4 vColour;
layout(location = 1) in vec2 vUv;
layout(location = 2) in vec3 vWorld;

layout(location = 0) out vec4 outColour;

// MeshDepthMode, as the host maps it.
const uint DEPTH_NORMAL = 0u;          // hidden where the world is in front
const uint DEPTH_OVERLAY = 1u;         // always drawn
const uint DEPTH_OCCLUDED_ONLY = 2u;   // drawn only where the world is in front

const uint NO_TEXTURE = 0xFFFFFFFFu;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    uint parity = push.flags.x;

    vec4 hit = imageLoad(positions[parity], pixel);
    float sceneDistance = hit.w > 0.5 ? imageLoad(normals[parity], pixel).w : 1.0e30;

    float distance = length(vWorld - push.cameraPos.xyz);

    // A little slack, so a guide drawn exactly on a surface is not eaten by it.
    bool occluded = distance > sceneDistance * 1.004 + 0.5;

    uint mode = push.flags.y;

    if (mode == DEPTH_NORMAL && occluded)
        discard;

    if (mode == DEPTH_OCCLUDED_ONLY && !occluded)
        discard;

    // A screen-space stipple stands in for GL's line pattern.
    if (push.flags.z != 0u)
    {
        int t = (int(gl_FragCoord.x) + int(gl_FragCoord.y)) / 2;

        if ((t & 1) == 1)
            discard;
    }

    vec4 colour = vColour;

    if (push.flags.w != NO_TEXTURE)
        colour *= texture(textures[nonuniformEXT(push.flags.w)], vUv);

    if (colour.a < 0.02)
        discard;

    outColour = colour;
}
