using System;
using System.Collections.Generic;
using System.Drawing;
using CSharp3D.Forms.Engine;
using CSharp3D.Forms.Meshes;
using OpenTK;

namespace CSharp3D.Forms.Vulkan.RayTracing
{
    /// <summary>
    /// The editor's guides, drawn over the ray traced picture: entity boxes and icons,
    /// selection outlines, tool handles and previews. None of them is part of the world the
    /// rays see - they are unlit, and a box round an entity must not cast a shadow - so they
    /// are rasterised on top of the finished frame, in their own colours, depth tested
    /// against the surfaces the rays found.
    ///
    /// Gathered on the UI thread from the scene and the view's own meshes into an
    /// immutable snapshot; the render thread uploads whichever snapshot is current when a
    /// frame starts.
    /// </summary>
    public sealed class OverlaySet
    {
        /// <summary>Position (3), colour (4), uv (2), billboard corner (2).</summary>
        public const int FloatsPerVertex = 11;

        public static readonly OverlaySet Empty = new OverlaySet(new float[0], new List<OverlayBatch>(), 0);

        public float[] Vertices { get; }
        public IReadOnlyList<OverlayBatch> Batches { get; }
        public int Version { get; }

        public int VertexCount => Vertices.Length / FloatsPerVertex;

        public OverlaySet(float[] vertices, List<OverlayBatch> batches, int version)
        {
            Vertices = vertices;
            Batches = batches;
            Version = version;
        }
    }

    public enum OverlayTopology
    {
        Lines = 0,
        Triangles = 1,
    }

    /// <summary>One draw: a run of vertices sharing a topology, depth rule, stipple and texture.</summary>
    public sealed class OverlayBatch
    {
        public OverlayTopology Topology;
        public MeshDepthMode DepthMode;
        public bool Dotted;
        public int Texture = -1;
        public int FirstVertex;
        public int VertexCount;

        public bool SameAs(OverlayBatch other)
        {
            return other != null && Topology == other.Topology && DepthMode == other.DepthMode && Dotted == other.Dotted
                && Texture == other.Texture && FirstVertex == other.FirstVertex && VertexCount == other.VertexCount;
        }
    }

    /// <summary>Builds an <see cref="OverlaySet"/> from the meshes the ray tracer does not trace.</summary>
    internal static class OverlayGather
    {
        private struct Key : IEquatable<Key>
        {
            public OverlayTopology Topology;
            public MeshDepthMode DepthMode;
            public bool Dotted;
            public int Texture;

            public bool Equals(Key other)
            {
                return Topology == other.Topology && DepthMode == other.DepthMode && Dotted == other.Dotted && Texture == other.Texture;
            }

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                return ((int)Topology * 397) ^ ((int)DepthMode * 31) ^ (Dotted ? 1 : 0) ^ (Texture * 7919);
            }
        }

        /// <summary>Is this a mesh the overlay pass draws? Lines, entity boxes and icons.</summary>
        public static bool IsOverlay(Mesh mesh)
        {
            return mesh is LineMesh || mesh is CuboidMesh || mesh is SpriteMesh;
        }

        /// <summary>
        /// Gather the overlays. Returns <paramref name="previous"/> itself when nothing has
        /// changed, so a caller can tell by reference whether there is anything to upload.
        /// </summary>
        public static OverlaySet Build(IEnumerable<Mesh> sceneMeshes, IEnumerable<Mesh> viewMeshes, Func<Bitmap, int> textureIndex, OverlaySet previous)
        {
            Dictionary<Key, List<float>> groups = new Dictionary<Key, List<float>>();
            List<Key> order = new List<Key>();

            if (sceneMeshes != null)
            {
                foreach (Mesh mesh in sceneMeshes)
                    Append(mesh, groups, order, textureIndex);
            }

            if (viewMeshes != null)
            {
                foreach (Mesh mesh in viewMeshes)
                    Append(mesh, groups, order, textureIndex);
            }

            int total = 0;

            foreach (Key key in order)
                total += groups[key].Count;

            float[] vertices = new float[total];
            List<OverlayBatch> batches = new List<OverlayBatch>(order.Count);
            int at = 0;

            foreach (Key key in order)
            {
                List<float> data = groups[key];
                data.CopyTo(vertices, at);

                batches.Add(new OverlayBatch
                {
                    Topology = key.Topology,
                    DepthMode = key.DepthMode,
                    Dotted = key.Dotted,
                    Texture = key.Texture,
                    FirstVertex = at / OverlaySet.FloatsPerVertex,
                    VertexCount = data.Count / OverlaySet.FloatsPerVertex,
                });

                at += data.Count;
            }

            if (previous != null && Same(previous, vertices, batches))
                return previous;

            return new OverlaySet(vertices, batches, (previous?.Version ?? 0) + 1);
        }

        private static bool Same(OverlaySet previous, float[] vertices, List<OverlayBatch> batches)
        {
            if (previous.Vertices.Length != vertices.Length || previous.Batches.Count != batches.Count)
                return false;

            for (int i = 0; i < batches.Count; i++)
            {
                if (!batches[i].SameAs(previous.Batches[i]))
                    return false;
            }

            float[] old = previous.Vertices;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (old[i] != vertices[i])
                    return false;
            }

            return true;
        }

        private static void Append(Mesh mesh, Dictionary<Key, List<float>> groups, List<Key> order, Func<Bitmap, int> textureIndex)
        {
            if (mesh == null || mesh.Material == null || !IsOverlay(mesh))
                return;

            // What the 3D pane would draw: the 2D-only guides stay out.
            if (!mesh.IsVisibleIn(MeshDrawMode.Textured))
                return;

            Color colour = mesh.Material.Color;

            if (colour.A == 0)
                return;

            LineMesh line = mesh as LineMesh;
            SpriteMesh sprite = mesh as SpriteMesh;

            Key key = new Key
            {
                Topology = line != null ? OverlayTopology.Lines : OverlayTopology.Triangles,
                DepthMode = mesh.DepthMode,

                // The GL view stipples in perspective only when the mesh asks for it.
                Dotted = line != null && line.Dotted && line.DottedInPerspective,
                Texture = -1,
            };

            if (sprite != null && sprite.Material.Albedo != null && sprite.Material.Albedo.Bitmap != null && textureIndex != null)
                key.Texture = textureIndex(sprite.Material.Albedo.Bitmap);

            float[] gl = mesh.GetGLVertexArray();

            if (gl == null || gl.Length < 8)
                return;

            Matrix4 model = mesh.GetModelMatrix(Matrix4.Identity);

            List<float> data;

            if (!groups.TryGetValue(key, out data))
            {
                data = new List<float>();
                groups[key] = data;
                order.Add(key);
            }

            float r = colour.R / 255f, g = colour.G / 255f, b = colour.B / 255f, a = colour.A / 255f;

            if (sprite != null)
            {
                // A billboard: the centre goes in as the position, the quad's corners as
                // offsets the vertex shader lays along the camera's right and up.
                Vector3 centre = Vector3.TransformPosition(Vector3.Zero, model);
                uint[] indices = mesh.GetIndexArray();

                foreach (uint index in indices)
                {
                    int i = (int)index * 8;

                    if (i + 7 >= gl.Length)
                        continue;

                    // The sprite's own vertex array faces east in Source terms, which is GL
                    // -x for its width: flip it so that the left of the texture is on the
                    // camera's left.
                    Push(data, centre.X, centre.Y, centre.Z, r, g, b, a, gl[i + 6], gl[i + 7], -gl[i], gl[i + 1]);
                }

                return;
            }

            if (line != null)
            {
                int count = gl.Length / 8;
                count -= count % 2;   // pairs only

                for (int v = 0; v < count; v++)
                {
                    Vector3 p = Vector3.TransformPosition(new Vector3(gl[v * 8], gl[v * 8 + 1], gl[v * 8 + 2]), model);
                    Push(data, p.X, p.Y, p.Z, r, g, b, a, 0f, 0f, 0f, 0f);
                }

                return;
            }

            // A box: its triangles, through its indices.
            {
                uint[] indices = mesh.GetIndexArray();

                if (indices == null)
                    return;

                int triangles = indices.Length / 3;

                for (int t = 0; t < triangles * 3; t++)
                {
                    int i = (int)indices[t] * 8;

                    if (i + 2 >= gl.Length)
                        continue;

                    Vector3 p = Vector3.TransformPosition(new Vector3(gl[i], gl[i + 1], gl[i + 2]), model);
                    Push(data, p.X, p.Y, p.Z, r, g, b, a, 0f, 0f, 0f, 0f);
                }
            }
        }

        private static void Push(List<float> data, float x, float y, float z, float r, float g, float b, float a, float u, float v, float cx, float cy)
        {
            data.Add(x); data.Add(y); data.Add(z);
            data.Add(r); data.Add(g); data.Add(b); data.Add(a);
            data.Add(u); data.Add(v);
            data.Add(cx); data.Add(cy);
        }
    }
}
