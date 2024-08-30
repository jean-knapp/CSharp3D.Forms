#version 330 core

out vec4 FragColor;  // Output fragment color

in vec2 fragTexCoord;    // Input texture coordinates from the vertex shader
in vec3 fragNormal;      // Input normal vector from the vertex shader
in vec3 fragPosition;    // Input vertex position from the vertex shader
in vec3 fragLightPosition;
in vec3 fragCameraPosition;

uniform bool uUseDiffuseTexture;		    // Flag to enable/disable the diffuse texture
uniform bool uUseNormalTexture;          // Flag to enable/disable the normal texture
uniform bool uUseSpecularTexture;        // Flag to enable/disable the specular texture

uniform sampler2D diffuseTexture;       // Diffuse texture sampler
uniform sampler2D specularTexture;      // Specular texture sampler
uniform sampler2D normalTexture;        // Normal texture sampler

uniform vec4 uBaseColor;      // Base RGBA color
uniform vec3 uLightAttenuation; // x * d² + y * d + z
uniform vec4 uLightColor; // RGB and intensity
uniform vec4 uAmbientColor; // RGB and intensity
uniform float uSpecularStrength;

// The main function calculates the fragment color by applying diffuse and specular lighting
void main()
{
    bool uUnlit = true;

    // Calculate the vector from the fragment position to the light position
    vec3 lightVector = fragLightPosition - fragPosition;
    
    // Calculate the distance to the light and the intensity with a simple attenuation model
    float lightDistance = length(lightVector);
    float lightIntensity = 1.0 / (uLightAttenuation.x * lightDistance * lightDistance + uLightAttenuation.y * lightDistance + uLightAttenuation.z);
    vec3 lightDirection = normalize(lightVector);

    // Normalize the normal vector and light direction for lighting calculations
    vec3 normal = uUseNormalTexture ? normalize(texture(normalTexture, fragTexCoord).rgb * 2.0 - 1.0) : normalize(fragNormal);
    
    // Specular reflection calculations using the view and reflection directions
    vec3 reflectDirection = reflect(-lightDirection, normal);
    vec3 viewDirection = normalize(fragCameraPosition - fragPosition);
    float specularFactor = uUseSpecularTexture ? texture(specularTexture, fragTexCoord).r : 1.0;
    float specularIntensity = pow(max(dot(viewDirection, reflectDirection), 0.0), 32);
    float specular = specularFactor * uSpecularStrength * specularIntensity * lightIntensity * uLightColor.w;

    // Diffuse lighting calculation (Lambertian reflection model)
    float diffuse = max(dot(normal, lightDirection), 0.0);

    // Calculate lighting
    vec3 brightness = uLightColor.rgb * uLightColor.w * lightIntensity * diffuse;
    vec3 ambient = uAmbientColor.rgb * uAmbientColor.w;

    // Sample the diffuse texture color using the texture coordinates
    vec4 texColor = uUseDiffuseTexture ? texture(diffuseTexture, fragTexCoord) : vec4(1.0, 1.0, 1.0, 1.0);

    // Calculate the final fragment color by combining diffuse, ambient, and specular components
    FragColor = vec4(texColor.rgb * uBaseColor.rgb * (brightness + ambient) + specular, texColor.a * uBaseColor.a);
}
