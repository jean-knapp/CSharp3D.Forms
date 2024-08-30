#version 330 core

out vec4 FragColor; // Declare the output fragment color

in vec2 fragTexCoord;   // Input texture coordinates passed from the vertex shader
in vec3 fragNormal;     // Input normal vector passed from the vertex shader, used for shading

uniform sampler2D diffuseTexture;   // Sampler for the diffuse texture

// The main function computes the fragment color with simple shading based on the normal vector
void main()
{
    // Sample the texture color using the provided texture coordinates
    vec4 texColor = texture(diffuseTexture, fragTexCoord);

    // Calculate a simple shading factor based on the y and z components of the normal
    // The shading varies from light to dark depending on the orientation of the surface
    float shade = 0.5 + 0.3 * -fragNormal.y + 0.2 * -fragNormal.z;

    // Mix the texture color with black based on the calculated shading factor
    FragColor = mix(texColor, vec4(0.0, 0.0, 0.0, texColor.a), shade);
}
