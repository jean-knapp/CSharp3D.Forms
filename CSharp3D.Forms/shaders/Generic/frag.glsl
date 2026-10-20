#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

out vec4 FragColor;  // Output fragment color

in vec2 fragTexCoord;    // Input texture coordinates from the vertex shader
in vec3 fragNormal;      // Input normal vector from the vertex shader
in vec3 fragPosition;    // Input vertex position from the vertex shader
in vec3 fragWorldPosition; // Always world space (fragPosition may be tangent space)
in vec3 fragLightPosition[MAX_LIGHTS];
in vec3 fragCameraPosition;
in vec3 fragSunDirection;

uniform bool uUseDiffuseTexture;		    // Flag to enable/disable the diffuse texture
uniform bool uUseNormalTexture;          // Flag to enable/disable the normal texture
uniform bool uUseSpecularTexture;        // Flag to enable/disable the specular texture

uniform sampler2D diffuseTexture;       // Diffuse texture sampler
uniform sampler2D specularTexture;      // Specular texture sampler
uniform sampler2D normalTexture;        // Normal texture sampler

// Two-texture vertex blend (Source's WorldVertexTransition): the diffuse texture at blend
// weight 0, blendTexture at 1, mixed per fragment by the interpolated vertex weight.
// Set per draw by the renderer; only a mesh with blend weights turns it on.
uniform bool uUseBlendTexture;
uniform sampler2D blendTexture;
in float fragBlend;

uniform vec4 uBaseColor;      // Base RGBA color
uniform vec3 uLightAttenuation[MAX_LIGHTS]; // x * d^2 + y * d + z
uniform vec4 uLightColor[MAX_LIGHTS]; // RGB and intensity
uniform vec4 uAmbientColor[MAX_LIGHTS]; // RGB and intensity
uniform float uSpecularStrength;

// The scene's sun (Source's light_environment): parallel rays with no falloff, so there
// is no position and no attenuation, just a direction and a colour. Intensity 0 means the
// scene has no sun and this whole term drops out, which is what an unset uniform gives.
uniform vec4 uSunColor;  // RGB and intensity

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
uniform vec2 uLightmapInvSize;   // 1/width, 1/height of the lightmap texture (an atlas, or one face's)

// The face's slot in that texture, in texels (x, y, w, h). Every lookup is clamped to it,
// which is exactly CLAMP_TO_EDGE for a face with a texture of its own, and keeps a face of
// an atlas from bleeding into its neighbours.
uniform vec4 uLightmapRect;

// Batched faces (AtlasLitMesh) carry their mapping per vertex instead: luxel coordinates in
// the atlas and the slot. A slot of width 0 is a face with no bake yet.
uniform bool uLightmapAtlas;
in vec2 fragLightmapCoord;
in vec4 fragLightmapSlot;

// A displacement's lightmap is laid over its flat base face, so its UVs come from the
// undisplaced position each vertex carries (LitIndexedMesh), not from where the sculpted
// surface actually is. Set together with uUseLightmap.
uniform bool uLightmapFromVertex;
in vec3 fragLightmapPosition;

// Bicubic (B-spline) lightmap filtering, the editor's r_lightmap_bicubic. Set together
// with uUseLightmap.
uniform bool uLightmapBicubic;

// Ambient cube (LitIndexedMesh, models in the lighting preview): the light arriving from
// each world axis direction, +X -X +Y -Y +Z -Z, as an albedo multiplier, blended by the
// squared normal like Source's vertexlit shaders. Reset to false by the renderer every draw.
uniform bool uUseAmbientCube;
uniform vec3 uAmbientCube[6];

// Cubic B-spline weights for a fractional position t, as (w0, w1, w2, w3).
vec4 CubicWeights(float t)
{
    vec4 n = vec4(1.0, 2.0, 3.0, 4.0) - t;
    vec4 s = n * n * n;
    float x = s.x;
    float y = s.y - 4.0 * s.x;
    float z = s.z - 4.0 * s.y + 6.0 * s.x;
    float w = 6.0 - x - y - z;
    return vec4(x, y, z, w) * (1.0 / 6.0);
}

// A tap position (texels) held inside the slot's outermost texel centres: a bilinear fetch
// there is the edge texel, as CLAMP_TO_EDGE would give past the edge of a face's own texture.
vec4 ClampTaps(vec4 taps, vec4 slot)
{
    vec2 lo = slot.xy + 0.5;
    vec2 hi = slot.xy + slot.zw - 0.5;
    return clamp(taps, lo.xxyy, hi.xxyy);
}

// Bilinear lookup at luxel coordinates st (texels), inside the slot.
vec3 SampleLightmapBilinear(vec2 st, vec4 slot)
{
    vec2 p = clamp(st, slot.xy + 0.5, slot.xy + slot.zw - 0.5);
    return texture(lightmapTexture, p * uLightmapInvSize).rgb;
}

// Bicubic B-spline lookup from four bilinear taps (the hardware does the inner blends), at
// luxel coordinates st (texels). Texels outside the slot replicate its edge.
vec3 SampleLightmapBicubic(vec2 lmST, vec4 slot)
{
    vec2 st = lmST - 0.5;
    vec2 f = fract(st);
    st -= f;

    vec4 xw = CubicWeights(f.x);
    vec4 yw = CubicWeights(f.y);

    vec4 c = st.xxyy + vec2(-0.5, 1.5).xyxy;
    vec4 s = vec4(xw.xz + xw.yw, yw.xz + yw.yw);
    vec4 offset = ClampTaps(c + vec4(xw.yw, yw.yw) / s, slot) * uLightmapInvSize.xxyy;

    vec3 s0 = texture(lightmapTexture, offset.xz).rgb;
    vec3 s1 = texture(lightmapTexture, offset.yz).rgb;
    vec3 s2 = texture(lightmapTexture, offset.xw).rgb;
    vec3 s3 = texture(lightmapTexture, offset.yw).rgb;

    float sx = s.x / (s.x + s.y);
    float sy = s.z / (s.z + s.w);
    return mix(mix(s3, s2, sx), mix(s1, s0, sx), sy);
}

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

// The Normals view mode: the surface normal in Source axes as a colour (X red, Y green,
// Z blue). Set only for triangles, so overlay lines keep their own colour.
uniform bool uShowNormals;

// Cutout (Material.AlphaTest, Source's $alphatest): texels whose albedo alpha is below this
// are discarded. 0 means off; the renderer sets it on every draw.
uniform float uAlphaTestRef;

// Scene.TextureBrightness: multiplier on the sampled albedo, 1 = as authored.
uniform float uTextureBrightness;

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

    // The sun. Parallel rays, so the light vector is the same everywhere and there is no
    // distance term -- just Lambert against the surface normal, plus its own specular.
    if (uSunColor.w > 0.0) {
        vec3 sunDirection = normalize(-fragSunDirection);
        float sunDiffuse = max(dot(normal, sunDirection), 0.0);
        totalBrightness += uSunColor.rgb * uSunColor.w * sunDiffuse;

        vec3 sunReflect = reflect(-sunDirection, normal);
        float sunSpecularFactor = uUseSpecularTexture ? texture(specularTexture, fragTexCoord).r : 1.0;
        float sunSpecularIntensity = pow(max(dot(viewDirection, sunReflect), 0.0), 32);
        totalSpecular += sunSpecularFactor * uSpecularStrength * sunSpecularIntensity * uSunColor.w * uSunColor.rgb;
    }

    // Sample the diffuse texture color using the texture coordinates
    vec4 texColor = uUseDiffuseTexture ? texture(diffuseTexture, fragTexCoord) : vec4(1.0, 1.0, 1.0, 1.0);

    if (uUseBlendTexture)
        texColor = mix(texColor, texture(blendTexture, fragTexCoord), clamp(fragBlend, 0.0, 1.0));

    if (uAlphaTestRef > 0.0 && texColor.a < uAlphaTestRef)
        discard;

    // Hammer's 3D brightness (Ctrl+Home/End). 0 is an unset uniform and means "as authored";
    // a lightmapped face keeps its texture as is, since the bake already carries the light.
    if (uUseDiffuseTexture && !uUseLightmap && uTextureBrightness > 0.0)
        texColor.rgb = min(texColor.rgb * uTextureBrightness, vec3(1.0));

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
    // Ambient cube (models in the lighting preview): replaces the dynamic lighting the same
    // way the lightmap path does for brush faces. fragNormal is the world-space geometric
    // normal in GL axes, where world (x, y, z) = GL (-z, -x, y).
    if (uUseAmbientCube) {
        vec3 n = normalize(fragNormal);
        vec3 w = vec3(-n.z, -n.x, n.y);
        vec3 w2 = w * w;
        vec3 cube = w2.x * (w.x >= 0.0 ? uAmbientCube[0] : uAmbientCube[1])
                  + w2.y * (w.y >= 0.0 ? uAmbientCube[2] : uAmbientCube[3])
                  + w2.z * (w.z >= 0.0 ? uAmbientCube[4] : uAmbientCube[5]);
        FragColor = vec4(texColor.rgb * uBaseColor.rgb * cube, texColor.a * uBaseColor.a);
    }

    if (uUseLightmap) {
        vec2 lmST;
        vec4 slot;

        if (uLightmapAtlas) {
            lmST = fragLightmapCoord;
            slot = fragLightmapSlot;
        } else {
            vec3 lmPosition = uLightmapFromVertex ? fragLightmapPosition : fragWorldPosition;
            // No slot given (a renderer that predates the atlas): the whole texture.
            slot = uLightmapRect.z > 0.0 ? uLightmapRect : vec4(0.0, 0.0, 1.0 / uLightmapInvSize);
            lmST = vec2(dot(lmPosition, uLightmapMapU.xyz) + uLightmapMapU.w,
                        dot(lmPosition, uLightmapMapV.xyz) + uLightmapMapV.w) + slot.xy;
        }

        if (slot.z > 0.0 && slot.w > 0.0) {
            vec3 lmTexel = uLightmapBicubic ? SampleLightmapBicubic(lmST, slot) : SampleLightmapBilinear(lmST, slot);
            vec3 lm = lmTexel * 2.0;   // OVERBRIGHT
            FragColor = vec4(texColor.rgb * uBaseColor.rgb * lm, texColor.a * uBaseColor.a);
        } else {
            // Not baked yet: the plain texture (with the 3D brightness, as unlit faces get),
            // never the near-black of no lighting at all.
            vec3 albedo = uUseDiffuseTexture && uTextureBrightness > 0.0 ? min(texColor.rgb * uTextureBrightness, vec3(1.0)) : texColor.rgb;
            FragColor = vec4(albedo * uBaseColor.rgb, texColor.a * uBaseColor.a);
        }
    }

    // Editor face shading. Applied last so it modulates whatever the mode produced,
    // and skipped for the lightmap path, which already carries real lighting.
    if (uFaceShade && !uUseLightmap && !uUseAmbientCube) {
        // The light direction is (1,2,3) in Hammer's world axes; this shader works in
        // GL axes, where world (x,y,z) maps to (-y, z, -x).
        vec3 shadeDir = normalize(vec3(-2.0, 3.0, -1.0));
        float shade = 0.65 + 0.35 * dot(normalize(fragNormal), shadeDir);
        FragColor = vec4(FragColor.rgb * max(shade, 0.30), FragColor.a);
    }

    if (uShowNormals) {
        // GL axes (x, y, z) are world (-y, z, -x); colour in the world's own axes.
        vec3 n = normalize(fragNormal);
        FragColor = vec4(vec3(-n.z, -n.x, n.y) * 0.5 + 0.5, 1.0);
    }
}
