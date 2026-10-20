#version 330 core

layout(location = 0) in vec3 inPosition;    // the anchor (screen text) or the corner itself (world text)
layout(location = 1) in vec3 inOffset;      // xy: pixel offset from the anchor, y up; z: 1 = screen text
layout(location = 2) in vec2 inTexCoord;    // glyph texel in the atlas
layout(location = 3) in vec4 inColor;       // text colour

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec2 uViewportSize;        // the viewport in pixels
uniform float uMinPixelsPerUnit;   // orthographic only: hide screen text below this zoom (0 = never)

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    vTexCoord = inTexCoord;
    vColor = inColor;

    vec4 clip = uProjection * uView * uModel * vec4(inPosition, 1.0);

    if (inOffset.z > 0.5)
    {
        bool orthographic = abs(uProjection[3][3] - 1.0) < 0.0001;

        // Hammer prints entity names only once a world unit covers a pixel or more
        // (mapentity.cpp CMapEntity::Render2D); the projection's x scale says how many it does.
        float pixelsPerUnit = abs(uProjection[0][0]) * 0.5 * uViewportSize.x;

        // Behind the eye, or zoomed out past the threshold: outside the clip volume.
        if (clip.w <= 0.0 || uViewportSize.x <= 0.0
            || (orthographic && uMinPixelsPerUnit > 0.0 && pixelsPerUnit < uMinPixelsPerUnit))
        {
            gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
            return;
        }

        // Snap the anchor to a pixel corner, then step out by whole pixels: every glyph
        // texel then covers exactly one screen pixel, which is what keeps small text crisp.
        vec2 ndc = clip.xy / clip.w;
        vec2 pixel = floor((ndc * 0.5 + 0.5) * uViewportSize + 0.5) + inOffset.xy;
        clip.xy = (pixel / uViewportSize * 2.0 - 1.0) * clip.w;
    }

    gl_Position = clip;
}
