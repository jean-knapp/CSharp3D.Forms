#version 460
#include "common.glsl"

// A camera or bounce ray that left the world. Raygen turns t < 0 into sky.

layout(location = 0) rayPayloadInEXT HitPayload hit;

void main()
{
    hit.t = -1.0;
}
