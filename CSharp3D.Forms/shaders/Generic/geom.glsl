#version 330 core

#define MAX_LIGHTS 8  // Change this value to support more/fewer lights (must match C# MAX_LIGHTS)

layout (triangles) in;
layout (triangle_strip, max_vertices = 3) out;

out vec2 fragTexCoord;     // Pass the texture coordinates to the fragment shader
out vec3 fragNormal;       // Pass the normal vector to the fragment shader for lighting calculations
out vec3 fragPosition;     // Pass the transformed vertex position to the fragment shader
out vec3 fragLightPosition[MAX_LIGHTS];
out vec3 fragCameraPosition;

in vec2 geomTexCoord[];	// The texture coordinates for the geometry
in vec3 geomNormal[]; 	// The normal vector for the geometry
in vec3 geomPosition[]; 	// The transformed vertex position for the geometry
in mat4 geomModel[];

uniform vec3 uLightPosition[MAX_LIGHTS];   // Light positions as uniforms
in vec3 geomCameraPosition[];

uniform bool uUseNormalTexture;

// Default main function
void main()
{
	mat3 TBN;
	if (gl_in.length() >= 3) {

		// Calculate normals
		vec3 edge0 = geomPosition[1] - geomPosition[0];
		vec3 edge1 = geomPosition[2] - geomPosition[0];
		vec2 deltaUV0 = geomTexCoord[1] - geomTexCoord[0];
		vec2 deltaUV1 = geomTexCoord[2] - geomTexCoord[0];

		float invDet = 1.0f / (deltaUV0.x * deltaUV1.y - deltaUV1.x * deltaUV0.y);

		vec3 tangent = vec3(invDet * (deltaUV1.y * edge0 - deltaUV0.y * edge1));
		vec3 bitangent = vec3(invDet * (-deltaUV1.x * edge0 + deltaUV0.x * edge1));

		vec3 T = normalize(vec3(geomModel[0] * vec4(tangent, 0.0f)));
		vec3 B = normalize(vec3(geomModel[0] * vec4(bitangent, 0.0f)));
		vec3 N = normalize(vec3(geomModel[0] * vec4(cross(edge1, edge0), 0.0f)));

		TBN = mat3(T,B,N);
		TBN = -transpose(TBN);
	}
	else {
		TBN = mat3(1.0f);
	}

    for (int i = 0; i < gl_in.length(); i++)
	{
		fragTexCoord = geomTexCoord[i];
		fragNormal = geomNormal[i];

		if (uUseNormalTexture)
		{
			fragPosition = TBN * geomPosition[i];
			for (int j = 0; j < MAX_LIGHTS; j++) {
				fragLightPosition[j] = TBN * uLightPosition[j];
			}
			fragCameraPosition = TBN * geomCameraPosition[i];
		}
		else {
			fragPosition = geomPosition[i];
			for (int j = 0; j < MAX_LIGHTS; j++) {
				fragLightPosition[j] = uLightPosition[j];
			}
			fragCameraPosition = geomCameraPosition[i];
		}

		gl_Position = gl_in[i].gl_Position;
		EmitVertex();
	}

    EndPrimitive();
}