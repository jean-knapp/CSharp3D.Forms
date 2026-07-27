using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>Flags carried per triangle, mirroring vrad's TRACE_ID_* tags.</summary>
    [Flags]
    public enum RayTriangleFlags
    {
        None = 0,

        /// <summary>A sky-textured face: blocks ordinary light, IS the target of sun/sky rays.</summary>
        Sky = 1,
    }

    /// <summary>One input triangle for the BVH, with an id to map hits back to faces.</summary>
    public struct RayTriangle
    {
        public Vector3 V0, V1, V2;
        public int Id;
        public RayTriangleFlags Flags;

        public RayTriangle(Vector3 v0, Vector3 v1, Vector3 v2, int id, RayTriangleFlags flags)
        {
            V0 = v0; V1 = v1; V2 = v2; Id = id; Flags = flags;
        }
    }

    /// <summary>Closest-hit result.</summary>
    public struct RayHit
    {
        public float T;
        public int Id;
        public RayTriangleFlags Flags;
        public Vector3 GeometricNormal; // NOT normalized

        /// <summary>Barycentric u,v of the hit (w = 1-u-v on V0).</summary>
        public float U, V;
    }

    /// <summary>
    /// A bounding-volume hierarchy over triangles for shadow/visibility rays — the role
    /// vrad's RayTracingEnvironment KD-tree plays (public/raytrace.h). Median-split
    /// build, ordered stack traversal, Möller–Trumbore intersection. Immutable once
    /// built, so any number of worker threads can trace against it concurrently.
    /// </summary>
    public class RayBvh
    {
        private struct Node
        {
            public Vector3 Min, Max;

            /// <summary>Interior: index of left child (right = left+1). Leaf: first tri index.</summary>
            public int LeftOrStart;

            /// <summary>0 for interior nodes, else the leaf's triangle count.</summary>
            public int Count;
        }

        private Node[] _nodes;
        private RayTriangle[] _tris;

        public int TriangleCount { get { return _tris != null ? _tris.Length : 0; } }

        public Vector3 SceneMin { get; private set; }
        public Vector3 SceneMax { get; private set; }

        private const int LeafSize = 4;

        public static RayBvh Build(List<RayTriangle> triangles)
        {
            RayBvh bvh = new RayBvh();
            bvh._tris = triangles.ToArray();

            int n = bvh._tris.Length;

            if (n == 0)
            {
                bvh._nodes = new[] { new Node { Min = Vector3.Zero, Max = Vector3.Zero, LeftOrStart = 0, Count = 0 } };
                return bvh;
            }

            // Work arrays: per-tri bounds + centroids, and an index list we sort in place.
            Vector3[] triMin = new Vector3[n];
            Vector3[] triMax = new Vector3[n];
            Vector3[] centroid = new Vector3[n];
            int[] order = new int[n];

            for (int i = 0; i < n; i++)
            {
                RayTriangle t = bvh._tris[i];
                triMin[i] = Vector3.Min(t.V0, Vector3.Min(t.V1, t.V2));
                triMax[i] = Vector3.Max(t.V0, Vector3.Max(t.V1, t.V2));
                centroid[i] = (triMin[i] + triMax[i]) * 0.5f;
                order[i] = i;
            }

            List<Node> nodes = new List<Node>(2 * n / LeafSize + 4);
            nodes.Add(default(Node)); // root placeholder

            BuildNode(nodes, 0, order, 0, n, triMin, triMax, centroid, bvh._tris);

            // Reorder triangles to match the leaf ranges (order[] is the permutation).
            RayTriangle[] sorted = new RayTriangle[n];
            for (int i = 0; i < n; i++)
                sorted[i] = bvh._tris[order[i]];
            bvh._tris = sorted;

            bvh._nodes = nodes.ToArray();
            bvh.SceneMin = bvh._nodes[0].Min;
            bvh.SceneMax = bvh._nodes[0].Max;
            return bvh;
        }

        private static void BuildNode(List<Node> nodes, int nodeIndex, int[] order, int start, int count,
                                      Vector3[] triMin, Vector3[] triMax, Vector3[] centroid, RayTriangle[] tris)
        {
            // Bounds of this range
            Vector3 min = new Vector3(float.MaxValue);
            Vector3 max = new Vector3(float.MinValue);
            Vector3 cmin = new Vector3(float.MaxValue);
            Vector3 cmax = new Vector3(float.MinValue);

            for (int i = start; i < start + count; i++)
            {
                int t = order[i];
                min = Vector3.Min(min, triMin[t]);
                max = Vector3.Max(max, triMax[t]);
                cmin = Vector3.Min(cmin, centroid[t]);
                cmax = Vector3.Max(cmax, centroid[t]);
            }

            Node node = new Node { Min = min, Max = max };

            Vector3 extent = cmax - cmin;

            if (count <= LeafSize || (extent.X <= 1e-4f && extent.Y <= 1e-4f && extent.Z <= 1e-4f))
            {
                node.LeftOrStart = start;
                node.Count = count;
                nodes[nodeIndex] = node;
                return;
            }

            // Split at the centroid median along the widest axis.
            int axis = 0;
            if (extent.Y > extent.X)
                axis = 1;
            if (extent.Z > (axis == 0 ? extent.X : extent.Y))
                axis = 2;

            int mid = start + count / 2;
            NthElementByCentroid(order, start, start + count - 1, mid, centroid, axis);

            int left = nodes.Count;
            node.LeftOrStart = left;
            node.Count = 0;
            nodes[nodeIndex] = node;

            nodes.Add(default(Node));
            nodes.Add(default(Node));

            BuildNode(nodes, left, order, start, mid - start, triMin, triMax, centroid, tris);
            BuildNode(nodes, left + 1, order, mid, start + count - mid, triMin, triMax, centroid, tris);
        }

        /// <summary>Quickselect: partition order[lo..hi] so order[k] is the centroid median.</summary>
        private static void NthElementByCentroid(int[] order, int lo, int hi, int k, Vector3[] centroid, int axis)
        {
            while (lo < hi)
            {
                float pivot = Axis(centroid[order[(lo + hi) / 2]], axis);
                int i = lo, j = hi;

                while (i <= j)
                {
                    while (Axis(centroid[order[i]], axis) < pivot) i++;
                    while (Axis(centroid[order[j]], axis) > pivot) j--;

                    if (i <= j)
                    {
                        int tmp = order[i]; order[i] = order[j]; order[j] = tmp;
                        i++; j--;
                    }
                }

                if (k <= j)
                    hi = j;
                else if (k >= i)
                    lo = i;
                else
                    return;
            }
        }

        private static float Axis(Vector3 v, int axis)
        {
            switch (axis)
            {
                case 0: return v.X;
                case 1: return v.Y;
                default: return v.Z;
            }
        }

        // ==================== GPU upload ====================

        /// <summary>
        /// The nodes in the vec4 pair the compute shader declares: xyz is the bound, w
        /// carries the int through its bit pattern. Packed here rather than in the tracer
        /// so the layout lives next to the traversal it has to agree with — the shader
        /// walks this exact tree, and a mismatch is a silently wrong shadow, not a crash.
        /// </summary>
        public float[] PackNodesForGpu()
        {
            if (_nodes == null)
                return null;

            float[] data = new float[_nodes.Length * 8];

            for (int i = 0; i < _nodes.Length; i++)
            {
                int at = i * 8;
                Node node = _nodes[i];

                data[at + 0] = node.Min.X;
                data[at + 1] = node.Min.Y;
                data[at + 2] = node.Min.Z;
                data[at + 3] = IntAsFloat(node.LeftOrStart);
                data[at + 4] = node.Max.X;
                data[at + 5] = node.Max.Y;
                data[at + 6] = node.Max.Z;
                data[at + 7] = IntAsFloat(node.Count);
            }

            return data;
        }

        /// <summary>
        /// The triangles as three vec4s each: the vertices, with the id and flags riding
        /// in the unused w lanes. Already in leaf order — Build permutes them — so a leaf's
        /// range indexes straight into this.
        /// </summary>
        public float[] PackTrianglesForGpu()
        {
            if (_tris == null)
                return null;

            float[] data = new float[_tris.Length * 12];

            for (int i = 0; i < _tris.Length; i++)
            {
                int at = i * 12;
                RayTriangle tri = _tris[i];

                data[at + 0] = tri.V0.X;
                data[at + 1] = tri.V0.Y;
                data[at + 2] = tri.V0.Z;
                data[at + 3] = IntAsFloat(tri.Id);
                data[at + 4] = tri.V1.X;
                data[at + 5] = tri.V1.Y;
                data[at + 6] = tri.V1.Z;
                data[at + 7] = IntAsFloat((int)tri.Flags);
                data[at + 8] = tri.V2.X;
                data[at + 9] = tri.V2.Y;
                data[at + 10] = tri.V2.Z;
                data[at + 11] = 0f;
            }

            return data;
        }

        /// <summary>
        /// An int's bit pattern as a float, for the w lanes above — the shader reads it
        /// back with floatBitsToInt. BitConverter rather than an unsafe cast so the
        /// assembly stays verifiable; this runs once per BVH build, not per ray.
        /// </summary>
        private static float IntAsFloat(int value)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(value), 0);
        }

        // ==================== traversal ====================

        [ThreadStatic]
        private static int[] _stack;

        private static int[] Stack()
        {
            if (_stack == null)
                _stack = new int[128];
            return _stack;
        }

        /// <summary>
        /// Is anything hit in (tmin, tmax)? For shadow rays. Sky faces block like any
        /// opaque face (vrad adds them to the trace environment too); triangles whose
        /// Id equals <paramref name="skipId"/> are ignored (self-shadow avoidance), as
        /// are triangles carrying any of <paramref name="ignoreFlags"/> — passing
        /// <see cref="RayTriangleFlags.Sky"/> gives vrad's <c>TestLine_IgnoreSky</c>,
        /// which ambient occlusion needs so the open sky doesn't read as an occluder.
        /// </summary>
        public bool AnyHit(Vector3 origin, Vector3 direction, float tmax, int skipId = int.MinValue,
                           RayTriangleFlags ignoreFlags = RayTriangleFlags.None)
        {
            if (_tris.Length == 0)
                return false;

            Vector3 invDir = SafeInverse(direction);
            int[] stack = Stack();
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                Node node = _nodes[stack[--sp]];

                if (!BoxHit(ref node, origin, invDir, tmax))
                    continue;

                if (node.Count > 0)
                {
                    int end = node.LeftOrStart + node.Count;
                    for (int i = node.LeftOrStart; i < end; i++)
                    {
                        if (_tris[i].Id == skipId)
                            continue;

                        if (ignoreFlags != RayTriangleFlags.None && (_tris[i].Flags & ignoreFlags) != 0)
                            continue;

                        float t, u, v;
                        if (TriHit(ref _tris[i], origin, direction, tmax, out t, out u, out v) && t > 1e-4f)
                            return true;
                    }
                }
                else
                {
                    if (sp + 2 > stack.Length)
                        Array.Resize(ref stack, stack.Length * 2);
                    stack[sp++] = node.LeftOrStart;
                    stack[sp++] = node.LeftOrStart + 1;
                    _stack = stack;
                }
            }

            return false;
        }

        /// <summary>Closest hit in (~0, tmax). Returns false if nothing is hit.</summary>
        public bool ClosestHit(Vector3 origin, Vector3 direction, float tmax, out RayHit hit)
        {
            hit = new RayHit { T = tmax, Id = -1 };

            if (_tris.Length == 0)
                return false;

            Vector3 invDir = SafeInverse(direction);
            int[] stack = Stack();
            int sp = 0;
            stack[sp++] = 0;

            bool found = false;

            while (sp > 0)
            {
                Node node = _nodes[stack[--sp]];

                if (!BoxHit(ref node, origin, invDir, hit.T))
                    continue;

                if (node.Count > 0)
                {
                    int end = node.LeftOrStart + node.Count;
                    for (int i = node.LeftOrStart; i < end; i++)
                    {
                        float t, u, v;
                        if (TriHit(ref _tris[i], origin, direction, hit.T, out t, out u, out v) && t > 1e-4f)
                        {
                            hit.T = t;
                            hit.Id = _tris[i].Id;
                            hit.Flags = _tris[i].Flags;
                            hit.U = u;
                            hit.V = v;
                            hit.GeometricNormal = Vector3.Cross(_tris[i].V1 - _tris[i].V0, _tris[i].V2 - _tris[i].V0);
                            found = true;
                        }
                    }
                }
                else
                {
                    if (sp + 2 > stack.Length)
                        Array.Resize(ref stack, stack.Length * 2);
                    stack[sp++] = node.LeftOrStart;
                    stack[sp++] = node.LeftOrStart + 1;
                    _stack = stack;
                }
            }

            return found;
        }

        private static Vector3 SafeInverse(Vector3 d)
        {
            const float tiny = 1e-12f;
            return new Vector3(
                1.0f / (Math.Abs(d.X) < tiny ? (d.X < 0 ? -tiny : tiny) : d.X),
                1.0f / (Math.Abs(d.Y) < tiny ? (d.Y < 0 ? -tiny : tiny) : d.Y),
                1.0f / (Math.Abs(d.Z) < tiny ? (d.Z < 0 ? -tiny : tiny) : d.Z));
        }

        private static bool BoxHit(ref Node node, Vector3 origin, Vector3 invDir, float tmax)
        {
            float tx1 = (node.Min.X - origin.X) * invDir.X;
            float tx2 = (node.Max.X - origin.X) * invDir.X;
            float tmin = Math.Min(tx1, tx2);
            float tmx = Math.Max(tx1, tx2);

            float ty1 = (node.Min.Y - origin.Y) * invDir.Y;
            float ty2 = (node.Max.Y - origin.Y) * invDir.Y;
            tmin = Math.Max(tmin, Math.Min(ty1, ty2));
            tmx = Math.Min(tmx, Math.Max(ty1, ty2));

            float tz1 = (node.Min.Z - origin.Z) * invDir.Z;
            float tz2 = (node.Max.Z - origin.Z) * invDir.Z;
            tmin = Math.Max(tmin, Math.Min(tz1, tz2));
            tmx = Math.Min(tmx, Math.Max(tz1, tz2));

            return tmx >= Math.Max(tmin, 0.0f) && tmin <= tmax;
        }

        /// <summary>Möller–Trumbore, two-sided (light is blocked from both sides).</summary>
        private static bool TriHit(ref RayTriangle tri, Vector3 origin, Vector3 direction, float tmax,
                                   out float t, out float u, out float v)
        {
            t = u = v = 0;

            Vector3 e1 = tri.V1 - tri.V0;
            Vector3 e2 = tri.V2 - tri.V0;
            Vector3 p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);

            if (det > -1e-9f && det < 1e-9f)
                return false;

            float invDet = 1.0f / det;
            Vector3 s = origin - tri.V0;
            u = Vector3.Dot(s, p) * invDet;

            if (u < -1e-5f || u > 1.0f + 1e-5f)
                return false;

            Vector3 q = Vector3.Cross(s, e1);
            v = Vector3.Dot(direction, q) * invDet;

            if (v < -1e-5f || u + v > 1.0f + 1e-5f)
                return false;

            t = Vector3.Dot(e2, q) * invDet;
            return t > 0 && t < tmax;
        }
    }
}
