#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

out vec4 FragColor;  // Output fragment color

in vec2 fragTexCoord;    // Input texture coordinates from the vertex shader
in vec3 fragNormal;      // Input normal vector from the vertex shader
in vec3 fragPosition;    // Input vertex position from the vertex shader
in vec3 fragLightPosition[MAX_LIGHTS];
in vec3 fragCameraPosition;

uniform bool uUseDiffuseTexture;		    // Flag to enable/disable the diffuse texture
uniform bool uUseNormalTexture;          // Flag to enable/disable the normal texture
uniform bool uUseSpecularTexture;        // Flag to enable/disable the specular texture

uniform sampler2D diffuseTexture;       // Diffuse texture sampler
uniform sampler2D specularTexture;      // Specular texture sampler
uniform sampler2D normalTexture;        // Normal texture sampler

uniform vec4 uBaseColor;      // Base RGBA color
uniform vec3 uLightAttenuation[MAX_LIGHTS]; // x * d² + y * d + z
uniform vec4 uLightColor[MAX_LIGHTS]; // RGB and intensity
uniform vec4 uAmbientColor[MAX_LIGHTS]; // RGB and intensity
uniform float uSpecularStrength;

// The main function calculates the fragment color by applying diffuse and specular lighting
void main()
{
    bool uUnlit = true;

    // Normalize the normal vector for lighting calculations
    vec3 normal = uUseNormalTexture ? normalize(texture(normalTexture, fragTexCoord).rgb * 2.0 - 1.0) : normalize(fragNormal);
    vec3 viewDirection = normalize(fragCameraPosition - fragPosition);
    
    // Initialize lighting components
    vec3 totalBrightness = vec3(0.0);
    vec3 totalSpecular = vec3(0.0);
    vec3 ambient = uAmbientColor[0].rgb * uAmbientColor[0].w; // Use first ambient color

    // Calculate lighting for each light source
    for (int i = 0; i < MAX_LIGHTS; i++) {
        // Calculate the vector from the fragment position to the light position
        vec3 lightVector = fragLightPosition[i] - fragPosition;
        
        // Calculate the distance to the light and the intensity with a simple attenuation model
        float lightDistance = length(lightVector);
        float lightIntensity = 1.0 / (uLightAttenuation[i].x * lightDistance * lightDistance + uLightAttenuation[i].y * lightDistance + uLightAttenuation[i].z);
        vec3 lightDirection = normalize(lightVector);

        // Specular reflection calculations using the view and reflection directions
        vec3 reflectDirection = reflect(-lightDirection, normal);
        float specularFactor = uUseSpecularTexture ? texture(specularTexture, fragTexCoord).r : 1.0;
        float specularIntensity = pow(max(dot(viewDirection, reflectDirection), 0.0), 32);
        vec3 specular = specularFactor * uSpecularStrength * specularIntensity * lightIntensity * uLightColor[i].w * uLightColor[i].rgb;

        // Diffuse lighting calculation (Lambertian reflection model)
        float diffuse = max(dot(normal, lightDirection), 0.0);

        // Accumulate lighting from this light source
        totalBrightness += uLightColor[i].rgb * uLightColor[i].w * lightIntensity * diffuse;
        totalSpecular += specular;
    }

    // Sample the diffuse texture color using the texture coordinates
    vec4 texColor = uUseDiffuseTexture ? texture(diffuseTexture, fragTexCoord) : vec4(1.0, 1.0, 1.0, 1.0);

    // Calculate the final fragment color by combining diffuse, ambient, and specular components
    FragColor = vec4(texColor.rgb * uBaseColor.rgb * (totalBrightness + ambient) + totalSpecular, texColor.a * uBaseColor.a);
}
