#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex
layout(location = 1) in vec3 inNormal;      // Normal vector of the vertex (used for lighting and shading)
layout(location = 2) in vec2 inTexCoord;    // Texture coordinates of the vertex

out vec2 geomTexCoord;     // Pass the texture coordinates to the geometry shader
out vec3 geomNormal;       // Pass the normal vector to the geometry shader for lighting calculations
out vec3 geomPosition;     // Pass the transformed vertex position to the geometry shader
out vec3 geomWorldPosition; // World-space position, never rewritten into tangent space
out mat4 geomModel;

// Light positions are passed as uniforms to geometry shader, not per-vertex
out vec3 geomCameraPosition; // Pass the camera position to the fragment shader

uniform mat4 uModel;        // Transforms the object from local to world space
uniform mat4 uView;         // Transforms the object from world space to camera view space
uniform mat4 uProjection;   // Projects the object from camera view space to clip space

uniform vec3 uLightPosition[MAX_LIGHTS];   // Light source positions in world space
uniform vec3 uCameraPosition;  // Camera (viewer) position in world space

// The main function calculates the vertex position in clip space, transforms the vertex position, and passes texture coordinates and normals to the fragment shader
void main()
{
    // Compute the transformed position in world space
    geomPosition = vec3(uModel * vec4(inPosition, 1.0));
    geomWorldPosition = geomPosition;

    // Rotate the normal into world space. GetModelMatrix builds rotation + translation
    // with no scale, so the upper 3x3 is a pure rotation and needs no inverse-transpose.
    // Without this a rotated mesh (a model placed at a yaw, say) is lit as though it were
    // not rotated -- invisible while the only light was a point light near the object, but
    // obvious under a directional light, where the shading is pure dot(N, sunDir).
    geomNormal = normalize(mat3(uModel) * inNormal);
    geomTexCoord = inTexCoord; 

    geomModel = uModel;
    // Light positions are handled as uniforms in geometry shader
    geomCameraPosition = uCameraPosition;
    
    // Transform the vertex position to clip space
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);                             
}
