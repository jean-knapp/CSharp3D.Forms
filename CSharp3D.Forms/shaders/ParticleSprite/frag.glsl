#version 330 core

// ParticleSprite fragment: dual sheet-frame blend, vertex color modulation, and the
// Source spritecard-style $addself / $overbrightfactor brightness controls.

out vec4 FragColor;

in vec2 fragTexCoord;
in vec2 fragTexCoord2;
in float fragBlend;
in vec4 fragColor;

uniform sampler2D diffuseTexture;
uniform bool uUseDiffuseTexture;
uniform float uAddSelf;           // adds the texture onto itself ($addself)
uniform float uOverbrightFactor;  // brightness multiplier ($overbrightfactor)

void main()
{
    vec4 frame0 = uUseDiffuseTexture ? texture(diffuseTexture, fragTexCoord) : vec4(1.0);
    vec4 frame1 = uUseDiffuseTexture ? texture(diffuseTexture, fragTexCoord2) : vec4(1.0);
    vec4 tex = mix(frame0, frame1, fragBlend);

    vec3 rgb = tex.rgb * fragColor.rgb * uOverbrightFactor * (1.0 + uAddSelf);
    float alpha = tex.a * fragColor.a;

    FragColor = vec4(rgb, alpha);
}
