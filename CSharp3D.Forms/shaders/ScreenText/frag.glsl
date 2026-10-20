#version 330 core

in vec2 vTexCoord;
in vec4 vColor;

out vec4 FragColor;

uniform sampler2D uAtlas;   // white glyphs, coverage in alpha (GlyphAtlas)

void main()
{
    float coverage = texture(uAtlas, vTexCoord).a;

    if (coverage <= 0.0)
        discard;

    FragColor = vec4(vColor.rgb, vColor.a * coverage);
}
