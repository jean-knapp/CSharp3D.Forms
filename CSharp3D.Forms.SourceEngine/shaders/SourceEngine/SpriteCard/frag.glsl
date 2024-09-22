#version 330 core
out vec4 FragColor;

in vec2 fragTexCoord;

uniform sampler2D diffuseTexture;       // Diffuse texture sampler

uniform vec4 uBaseColor;      // Base RGBA color
uniform float uAddSelf;        // Additive self blend
uniform float uOverbrightFactor;      // Overbright factor

void main()
{
    float uAdditiveSelfBlendWeight = uAddSelf * 0.25;

    // Sample the diffuse texture color using the texture coordinates
    vec4 texColor = texture(diffuseTexture, fragTexCoord);

    // ADDSELF
    if (uAddSelf > 0)
    {
        texColor.a *= uBaseColor.a;
        texColor.rgb *= texColor.a;
        texColor.rgb += (uOverbrightFactor * 8 * uAdditiveSelfBlendWeight * texColor).rgb;
        texColor.rgb *= 2 * uBaseColor.rgb;
    }

    FragColor = texColor;

    // Calculate the final fragment color by combining diffuse, ambient, and specular components
    //FragColor = vec4(texColor.rgb, texColor.a * uBaseColor.a);
}