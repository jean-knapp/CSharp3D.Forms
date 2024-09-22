#version 330 core

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex
layout(location = 1) in vec3 inNormal;      // Normal vector of the vertex (used for lighting and shading)
layout(location = 2) in vec2 inTexCoord;    // Texture coordinates of the vertex

out vec2 fragTexCoord;  // Pass the texture coordinates to the fragment shader
out vec3 fragNormal;    // Pass the normal vector to the fragment shader for lighting calculations

uniform mat4 uModel;        //Transforms the object from local to world space
uniform mat4 uView;         // Transforms the object from world space to camera view space
uniform mat4 uProjection;   // Projects the object from camera view space to clip space

// The main function computes the vertex position in clip space
void main()
{
    // Pass the texture coordinates and normal vector to the fragment shader
    fragNormal = inNormal;   
    fragTexCoord = inTexCoord;

    // Transform the vertex position to clip space
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);
}
