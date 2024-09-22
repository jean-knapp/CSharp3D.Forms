#version 330 core

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex

uniform mat4 uModel;        // Transforms the object from local to world space
uniform mat4 uView;         //Transforms the object from world space to camera view space
uniform mat4 uProjection;   // Projects the object from camera view space to clip space

// The main function computes the vertex position in clip space
void main()
{
    // Transform the vertex position to clip space
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);
}
