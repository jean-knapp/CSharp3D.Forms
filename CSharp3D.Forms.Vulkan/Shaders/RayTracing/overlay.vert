#version 460

// The editor's guides over the ray traced picture: plain rasterisation of lines, boxes and
// camera-facing icons, in the same GL-space world the rays trace.

layout(push_constant) uniform Push
{
    mat4 viewProj;       // world (GL space) -> GL clip
    vec4 right;          // the camera's right, world space, for billboards
    vec4 up;             // the camera's up
    vec4 cameraPos;      // xyz eye position, world space
    uvec4 flags;         // x history parity, y depth mode, z dotted, w texture index (all ones: none)
} push;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec4 inColour;
layout(location = 2) in vec2 inUv;
layout(location = 3) in vec2 inCorner;

layout(location = 0) out vec4 vColour;
layout(location = 1) out vec2 vUv;
layout(location = 2) out vec3 vWorld;

void main()
{
    // A billboard vertex carries its corner as an offset along the camera's axes; every
    // other vertex has a zero corner.
    vec3 world = inPosition + push.right.xyz * inCorner.x + push.up.xyz * inCorner.y;

    vec4 clip = push.viewProj * vec4(world, 1.0);

    // The matrices are the GL view's: +y up and depth in [-w, w]. Vulkan wants +y down and
    // depth in [0, w] - the same flip raygen does for its rays, so the two line up.
    clip.y = -clip.y;
    clip.z = (clip.z + clip.w) * 0.5;

    gl_Position = clip;
    vColour = inColour;
    vUv = inUv;
    vWorld = world;
}
