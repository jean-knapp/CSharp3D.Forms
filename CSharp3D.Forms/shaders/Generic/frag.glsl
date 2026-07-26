#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

out vec4 FragColor;  // Output fragment color

in vec2 fragTexCoord;    // Input texture coordinates from the vertex shader
in vec3 fragNormal;      // Input normal vector from the vertex shader
in vec3 fragPosition;    // Input vertex position from the vertex shader
in vec3 fragWorldPosition; // Always world space (fragPosition may be tangent space)
in vec3 fragLightPosition[MAX_LIGHTS];
in vec3 fragCameraPosition;

uniform bool uUseDiffuseTexture;		    // Flag to enable/disable the diffuse texture
uniform bool uUseNormalTexture;          // Flag to enable/disable the normal texture
uniform bool uUseSpecularTexture;        // Flag to enable/disable the specular texture

uniform sampler2D diffuseTexture;       // Diffuse texture sampler
uniform sampler2D specularTexture;      // Specular texture sampler
uniform sampler2D normalTexture;        // Normal texture sampler

uniform vec4 uBaseColor;      // Base RGBA color
uniform vec3 uLightAttenuation[MAX_LIGHTS]; // x * d^2 + y * d + z
uniform vec4 uLightColor[MAX_LIGHTS]; // RGB and intensity
uniform vec4 uAmbientColor[MAX_LIGHTS]; // RGB and intensity
uniform float uSpecularStrength;

// Baked lightmap (QuakeFaceMesh): UV from world position. Texels hold exactly what
// the Source engine stores in an LDR lightmap (ColorSpace::LinearToLightmap: the
// linear luxel raised to 1/2.2 and halved), so the decode here is the engine's own
// one: multiply by LIGHT_MAP_SCALE = OVERBRIGHT = 2 and modulate the gamma-space
// albedo. uUseLightmap is reset to false for every mesh by the renderer; only
// lightmapped faces turn it on.
// Keep this file ASCII: see Shader.SetShaderSource for why non-ASCII bytes used to
// truncate the tail of the shader.
uniform bool uUseLightmap;
uniform sampler2D lightmapTexture;
uniform vec4 uLightmapMapU;      // xyz = GL-space axis / luxel size, w = luxel offset
uniform vec4 uLightmapMapV;
uniform vec2 uLightmapInvSize;   // 1/width, 1/height

// Hammer's editor face shading (CRender3D::LightPlane, render3dms.cpp:154):
// shade = 0.65 + 0.35 * dot(N, normalize(1,2,3)), floored at 0.30 so nothing goes
// black. A fixed world direction, no scene lights involved - it exists purely so
// faces at different angles read apart in the viewport. On for the Shaded and Flat
// view modes, off for plain Textured (which must stay flat so a baked lightmap is
// not shaded twice).
uniform bool uFaceShade;

// Editor viewports draw textured geometry at full brightness: the texture is the
// information and the scene's lights would only darken it, which is what Hammer's
// textured 3D view does. Set from Scene.FullBright, not from the draw mode, so a
// viewer that wants lit textured geometry keeps it. The baked lightmap path below
// still wins, so a lighting preview shows real light with this on.
uniform bool uFullBright;

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

    // Fullbright: the albedo IS the output. Throws away everything the light loop
    // accumulated rather than skipping it, so the branch stays uniform across the
    // draw and the lightmap path below can still overwrite this.
    if (uFullBright) {
        FragColor = vec4(texColor.rgb * uBaseColor.rgb, texColor.a * uBaseColor.a);
    }

    // Baked lightmap path: albedo * lightmap replaces the dynamic lighting entirely
    // (the bake already contains every light, with shadows and bounce). Uses the
    // always-world-space position, not fragPosition, which the geometry shader
    // rewrites into tangent space when a normal map is bound.
    if (uUseLightmap) {
        vec2 lmUV = vec2(dot(fragWorldPosition, uLightmapMapU.xyz) + uLightmapMapU.w,
                         dot(fragWorldPosition, uLightmapMapV.xyz) + uLightmapMapV.w) * uLightmapInvSize;
        vec3 lmTexel = texture(lightmapTexture, lmUV).rgb;
        vec3 lm = lmTexel * 2.0;   // OVERBRIGHT
        FragColor = vec4(texColor.rgb * uBaseColor.rgb * lm, texColor.a * uBaseColor.a);
    }

    // Editor face shading. Applied last so it modulates whatever the mode produced,
    // and skipped for the lightmap path, which already carries real lighting.
    if (uFaceShade && !uUseLightmap) {
        // The light direction is (1,2,3) in Hammer's world axes; this shader works in
        // GL axes, where world (x,y,z) maps to (-y, z, -x).
        vec3 shadeDir = normalize(vec3(-2.0, 3.0, -1.0));
        float shade = 0.65 + 0.35 * dot(normalize(fragNormal), shadeDir);
        FragColor = vec4(FragColor.rgb * max(shade, 0.30), FragColor.a);
    }
}
