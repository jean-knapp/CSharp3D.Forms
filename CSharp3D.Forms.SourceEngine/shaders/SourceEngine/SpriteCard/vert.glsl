#version 330 core

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex
layout(location = 1) in vec3 inNormal;      // Normal vector of the vertex (used for lighting and shading)
layout(location = 2) in vec2 inTexCoord;    // Texture coordinates of the vertex

out vec2 fragTexCoord;      // Pass the texture coordinates to the fragment shader

uniform mat4 uModel;        // Transforms the object from local to world space
uniform mat4 uView;         // Transforms the object from world space to camera view space
uniform mat4 uProjection;   // Projects the object from camera view space to clip space

// The main function calculates the vertex position in clip space, transforms the vertex position, and passes texture coordinates and normals to the fragment shader
void main()
{
    fragTexCoord = inTexCoord;
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);
}
