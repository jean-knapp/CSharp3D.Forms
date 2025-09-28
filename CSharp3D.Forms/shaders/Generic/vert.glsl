#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex
layout(location = 1) in vec3 inNormal;      // Normal vector of the vertex (used for lighting and shading)
layout(location = 2) in vec2 inTexCoord;    // Texture coordinates of the vertex

out vec2 geomTexCoord;     // Pass the texture coordinates to the geometry shader
out vec3 geomNormal;       // Pass the normal vector to the geometry shader for lighting calculations
out vec3 geomPosition;     // Pass the transformed vertex position to the geometry shader
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

    // Pass the texture coordinates and normal vector to the fragment shader
    geomNormal = inNormal;   
    geomTexCoord = inTexCoord; 

    geomModel = uModel;
    // Light positions are handled as uniforms in geometry shader
    geomCameraPosition = uCameraPosition;
    
    // Transform the vertex position to clip space
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);                             
}
