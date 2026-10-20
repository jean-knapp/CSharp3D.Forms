#version 330 core

layout(location = 0) in vec3 inPosition;    // 3D position of the vertex
layout(location = 1) in vec3 inNormal;      // xy: screen offset in pixels (LineMesh.ScreenOffsets), z: see below
layout(location = 2) in vec2 inTexCoord;    // with inNormal.z: the extents of the shape a handle marks

uniform mat4 uModel;        // Transforms the object from local to world space
uniform mat4 uView;         //Transforms the object from world space to camera view space
uniform mat4 uProjection;   // Projects the object from camera view space to clip space
uniform vec2 uViewportSize; // The viewport in pixels; only set for meshes that carry screen offsets

// The main function computes the vertex position in clip space
void main()
{
    // Transform the vertex position to clip space
    gl_Position = uProjection * uView * uModel * vec4(inPosition, 1.0);

    // A vertex with a screen offset belongs to a handle - an editor mark such as Hammer's
    // centre X on a brush - that keeps the same size in pixels at every zoom. Ordinary lines
    // carry a zero offset and are left exactly as they were.
    if (inNormal.x != 0.0 || inNormal.y != 0.0)
    {
        // Handles are a 2D-view convention: hidden in perspective, and hidden whenever the
        // shape they mark is smaller on screen than the handle itself (mapsolid.cpp Render2D).
        vec3 extents = vec3(inTexCoord.x, inTexCoord.y, inNormal.z);
        vec2 projected = abs((uProjection * uView * uModel * vec4(extents, 0.0)).xy) * 0.5 * uViewportSize;
        float handle = 2.0 * max(abs(inNormal.x), abs(inNormal.y));
        bool orthographic = abs(uProjection[3][3] - 1.0) < 0.0001;

        if (!orthographic || uViewportSize.x <= 0.0 || projected.x < handle || projected.y < handle)
            gl_Position = vec4(2.0, 2.0, 2.0, 1.0); // outside the clip volume, so never drawn
        else
            gl_Position.xy += inNormal.xy * 2.0 / uViewportSize * gl_Position.w;
    }
}
