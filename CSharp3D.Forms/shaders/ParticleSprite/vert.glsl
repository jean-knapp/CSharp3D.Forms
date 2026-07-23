#version 330 core

// ParticleSprite: billboarded particle quads from ParticleBatchMesh.
// Each vertex carries its quad's center plus a unit corner offset; the quad is
// expanded here against the camera basis (columns of the view matrix), rotated
// by the particle roll, and scaled by the particle radius.

layout(location = 0) in vec3 inCenter;    // quad center, GL world space
layout(location = 1) in vec2 inCorner;    // unit corner offset (+/-1, +/-1)
layout(location = 2) in float inRadius;   // half-size in world units
layout(location = 3) in float inRoll;     // roll angle, radians
layout(location = 4) in vec2 inTexCoord;  // sheet frame UV
layout(location = 5) in vec2 inTexCoord2; // next sheet frame UV (frame blending)
layout(location = 6) in float inBlend;    // 0..1 blend between the two frames
layout(location = 7) in vec4 inColor;     // particle tint x alpha

out vec2 fragTexCoord;
out vec2 fragTexCoord2;
out float fragBlend;
out vec4 fragColor;

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
    // Camera right/up in world space = rows of the view rotation (transposed columns).
    vec3 cameraRight = vec3(uView[0][0], uView[1][0], uView[2][0]);
    vec3 cameraUp = vec3(uView[0][1], uView[1][1], uView[2][1]);

    float c = cos(inRoll);
    float s = sin(inRoll);
    vec2 corner = vec2(inCorner.x * c - inCorner.y * s,
                       inCorner.x * s + inCorner.y * c);

    vec3 worldPosition = inCenter + (cameraRight * corner.x + cameraUp * corner.y) * inRadius;

    fragTexCoord = inTexCoord;
    fragTexCoord2 = inTexCoord2;
    fragBlend = inBlend;
    fragColor = inColor;

    gl_Position = uProjection * uView * vec4(worldPosition, 1.0);
}
