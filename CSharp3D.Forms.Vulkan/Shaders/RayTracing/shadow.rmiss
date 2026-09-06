#version 460
#include "common.glsl"

// A shadow ray that reached its light. Shadow rays terminate on the first hit and skip the
// hit stage entirely, so this running is the only way the payload changes.

layout(location = 1) rayPayloadInEXT float shadowT;

void main()
{
    shadowT = -1.0;
}
