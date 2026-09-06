#version 460
#include "common.glsl"

// The hit stage does no shading: it reads the triangle that was hit and reports where, which
// way it faces, its texture coordinate and its material. Raygen does the rest, so no stage
// ever traces from inside another.

layout(set = 0, binding = 2, scalar) buffer Geometries { GeometryRecord geometries[]; };

layout(location = 0) rayPayloadInEXT HitPayload hit;

hitAttributeEXT vec2 barycentrics;

void main()
{
    GeometryRecord geometry = geometries[gl_InstanceCustomIndexEXT + gl_GeometryIndexEXT];

    VertexBuffer vertices = VertexBuffer(geometry.vertexAddress);
    IndexBuffer indices = IndexBuffer(geometry.indexAddress);

    uint i0 = indices.i[gl_PrimitiveID * 3 + 0];
    uint i1 = indices.i[gl_PrimitiveID * 3 + 1];
    uint i2 = indices.i[gl_PrimitiveID * 3 + 2];

    // Eight floats per vertex: position, normal, uv - the GL renderer's own layout.
    vec3 p0 = vec3(vertices.v[i0 * 8 + 0], vertices.v[i0 * 8 + 1], vertices.v[i0 * 8 + 2]);
    vec3 p1 = vec3(vertices.v[i1 * 8 + 0], vertices.v[i1 * 8 + 1], vertices.v[i1 * 8 + 2]);
    vec3 p2 = vec3(vertices.v[i2 * 8 + 0], vertices.v[i2 * 8 + 1], vertices.v[i2 * 8 + 2]);

    vec3 n0 = vec3(vertices.v[i0 * 8 + 3], vertices.v[i0 * 8 + 4], vertices.v[i0 * 8 + 5]);
    vec3 n1 = vec3(vertices.v[i1 * 8 + 3], vertices.v[i1 * 8 + 4], vertices.v[i1 * 8 + 5]);
    vec3 n2 = vec3(vertices.v[i2 * 8 + 3], vertices.v[i2 * 8 + 4], vertices.v[i2 * 8 + 5]);

    vec2 uv0 = vec2(vertices.v[i0 * 8 + 6], vertices.v[i0 * 8 + 7]);
    vec2 uv1 = vec2(vertices.v[i1 * 8 + 6], vertices.v[i1 * 8 + 7]);
    vec2 uv2 = vec2(vertices.v[i2 * 8 + 6], vertices.v[i2 * 8 + 7]);

    vec3 weights = vec3(1.0 - barycentrics.x - barycentrics.y, barycentrics.x, barycentrics.y);

    vec3 objectPosition = p0 * weights.x + p1 * weights.y + p2 * weights.z;
    vec3 objectNormal = n0 * weights.x + n1 * weights.y + n2 * weights.z;
    vec3 objectFaceNormal = cross(p1 - p0, p2 - p0);

    // A mesh with no normals of its own falls back to the face.
    if (dot(objectNormal, objectNormal) < 1.0e-8)
        objectNormal = objectFaceNormal;

    hit.t = gl_HitTEXT;
    hit.position = vec3(gl_ObjectToWorldEXT * vec4(objectPosition, 1.0));
    hit.normal = normalize(vec3(objectNormal * gl_WorldToObjectEXT));
    hit.geometricNormal = normalize(vec3(objectFaceNormal * gl_WorldToObjectEXT));
    hit.uv = uv0 * weights.x + uv1 * weights.y + uv2 * weights.z;
    hit.materialIndex = geometry.materialIndex;
}
