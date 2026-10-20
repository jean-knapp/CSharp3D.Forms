using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The lightmap sample grid of one face — the preview equivalent of VRAD's
    /// lightinfo_t + CalcPoints (lightmap.cpp:985): a rectangle in lightmap (s,t)
    /// space, luxelSize world units per luxel, sample points on the face plane at
    /// integer lattice positions, nudged into the winding when the lattice point
    /// falls outside it (BuildFacesamples' pull-inside behavior, simplified).
    /// </summary>
    public class LuxelGrid
    {
        /// <summary>Normalized lightmap axes (world space).</summary>
        public Vector3 AxisU, AxisV;

        /// <summary>World units per luxel (the face's lightmapscale, possibly coarsened).</summary>
        public float LuxelSize;

        /// <summary>Integer lattice origin: world s of luxel column 0 is MinS luxels.</summary>
        public int MinS, MinT;

        public int Width, Height;

        /// <summary>
        /// World position of every luxel's sample (row-major, t rows). On the face plane for
        /// a brush face; on the displaced surface for a displacement.
        /// </summary>
        public Vector3[] Positions;

        /// <summary>
        /// Per-luxel shading normal, or null when every luxel uses the face's plane normal
        /// (brush faces). Displacements carry the surface normal interpolated from the
        /// displacement grid, which is what makes a sculpted hill shade like one.
        /// </summary>
        public Vector3[] Normals;

        /// <summary>
        /// The undisplaced lattice point of every luxel, or null when it equals
        /// <see cref="Positions"/>. Lightmap lookups between two grids of the same face
        /// (luxels ↔ bounce patches) go through lattice space, which a displaced position
        /// would only approximate.
        /// </summary>
        public Vector3[] PlanarPositions;

        /// <summary>Whether the luxel's lattice point (possibly nudged) is on the face.</summary>
        public bool[] Valid;

        /// <summary>
        /// Largest lightmap side in luxels. Hammer++'s preview limit: vbsp's own limit
        /// (MAX_LIGHTMAP_DIM_WITHOUT_BORDER, 32 in SDK 2013 and 125 in CS:GO) is met in a
        /// real compile by SUBDIVIDING the face, so the compiled map keeps full luxel
        /// density on a big floor. Coarsening at vbsp's limit instead — as this preview
        /// used to — halved or quartered the resolution of exactly the large surfaces
        /// where shadows are most visible. Past these limits the luxel size doubles.
        /// </summary>
        public const int MaxDim = 1024;

        /// <summary>Largest lightmap area in luxels (Hammer++'s 48000).</summary>
        public const int MaxLuxels = 48000;

        /// <summary>The lattice (undisplaced) point of luxel <paramref name="index"/>.</summary>
        public Vector3 LatticePosition(int index)
        {
            return PlanarPositions != null ? PlanarPositions[index] : Positions[index];
        }

        /// <summary>
        /// Build the grid for a face. <paramref name="winding"/> is the face polygon,
        /// <paramref name="normal"/> its plane normal, axes the VMF uaxis/vaxis
        /// (normalized here), luxelSize the lightmapscale times any preview coarsening.
        /// Returns null for degenerate faces.
        /// </summary>
        public static LuxelGrid Build(IList<Vector3> winding, Vector3 normal, Vector3 uAxis, Vector3 vAxis, float luxelSize)
        {
            if (winding == null || winding.Count < 3 || luxelSize <= 0)
                return null;

            if (uAxis.LengthSquared() < 1e-10f || vAxis.LengthSquared() < 1e-10f)
                TextureAxisFromPlane(normal, out uAxis, out vAxis);

            uAxis = Vector3.Normalize(uAxis);
            vAxis = Vector3.Normalize(vAxis);

            float planeDist = Vector3.Dot(normal, winding[0]);

            // Coarsen until the face fits the lightmap limit (vbsp would error out).
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float minS = float.MaxValue, maxS = float.MinValue;
                float minT = float.MaxValue, maxT = float.MinValue;

                for (int i = 0; i < winding.Count; i++)
                {
                    float s = Vector3.Dot(winding[i], uAxis) / luxelSize;
                    float t = Vector3.Dot(winding[i], vAxis) / luxelSize;
                    if (s < minS) minS = s;
                    if (s > maxS) maxS = s;
                    if (t < minT) minT = t;
                    if (t > maxT) maxT = t;
                }

                int iMinS = (int)Math.Floor(minS + 1e-4f);
                int iMinT = (int)Math.Floor(minT + 1e-4f);
                int width = (int)Math.Ceiling(maxS - 1e-4f) - iMinS + 1;
                int height = (int)Math.Ceiling(maxT - 1e-4f) - iMinT + 1;

                if (width < 2) width = 2;
                if (height < 2) height = 2;

                if (width > MaxDim || height > MaxDim || width * height > MaxLuxels)
                {
                    luxelSize *= 2;
                    continue;
                }

                LuxelGrid grid = new LuxelGrid
                {
                    AxisU = uAxis,
                    AxisV = vAxis,
                    LuxelSize = luxelSize,
                    MinS = iMinS,
                    MinT = iMinT,
                    Width = width,
                    Height = height,
                    Positions = new Vector3[width * height],
                    Valid = new bool[width * height],
                };

                grid.PlaceSamples(winding, normal, planeDist);
                return grid;
            }

            return null;
        }

        /// <summary>
        /// The lightmap grid of a displacement.
        ///
        /// <para>
        /// Source lays a displacement's lightmap over its BASE face: a displaced vertex's
        /// luxel coordinate is the bilinear blend of the base corners' coordinates, and since
        /// the planar lightmap projection is affine that is simply the projection of the
        /// vertex's undisplaced position. So the lattice here is exactly the base quad's
        /// (same MinS/MinT/size/axes, hence the same UVs the renderer derives), and only the
        /// SAMPLE moves: each lattice point is located in the quad's (u,v) parameter space
        /// and lifted onto the displaced surface, taking the interpolated surface normal with
        /// it — what Hammer++ does too (sample on the displaced surface, interpolated normal).
        /// </para>
        /// </summary>
        /// <param name="corners">Base quad corners in displacement order: index [0,0], [D,0], [D,D], [0,D].</param>
        /// <param name="positions">Displaced vertices, <c>row * dim + col</c>; row runs corner 0 → 1, col runs corner 0 → 3.</param>
        /// <param name="normals">Per-vertex surface normals, same layout.</param>
        public static LuxelGrid BuildDisplacement(Vector3[] corners, Vector3 normal, Vector3 uAxis, Vector3 vAxis,
                                                  float luxelSize, Vector3[] positions, Vector3[] normals, int dim)
        {
            if (corners == null || corners.Length != 4 || positions == null || dim < 2
                || positions.Length < dim * dim)
                return null;

            LuxelGrid grid = Build(corners, normal, uAxis, vAxis, luxelSize);
            if (grid == null)
                return null;

            int count = grid.Width * grid.Height;
            grid.PlanarPositions = grid.Positions;
            grid.Positions = new Vector3[count];
            grid.Normals = new Vector3[count];

            // Corners in the lattice's own 2D coordinates.
            Vector2[] quad = new Vector2[4];
            for (int i = 0; i < 4; i++)
                quad[i] = new Vector2(Vector3.Dot(corners[i], grid.AxisU), Vector3.Dot(corners[i], grid.AxisV));

            float span = dim - 1;

            for (int i = 0; i < count; i++)
            {
                Vector3 planar = grid.PlanarPositions[i];
                Vector2 p = new Vector2(Vector3.Dot(planar, grid.AxisU), Vector3.Dot(planar, grid.AxisV));

                Vector2 uv = InverseBilinear(quad, p);
                float u = Math.Max(0, Math.Min(1, uv.X));
                float v = Math.Max(0, Math.Min(1, uv.Y));

                Vector3 position, surfaceNormal;
                SampleDisplacement(positions, normals, dim, u * span, v * span, normal, out position, out surfaceNormal);

                grid.Positions[i] = position;
                grid.Normals[i] = surfaceNormal;
            }

            return grid;
        }

        /// <summary>
        /// Bilinear sample of the displacement grid at fractional (row, col).
        /// </summary>
        private static void SampleDisplacement(Vector3[] positions, Vector3[] normals, int dim, float row, float col,
                                               Vector3 fallbackNormal, out Vector3 position, out Vector3 normal)
        {
            int r0 = Math.Min(dim - 2, Math.Max(0, (int)row));
            int c0 = Math.Min(dim - 2, Math.Max(0, (int)col));
            float fr = row - r0, fc = col - c0;

            int i00 = r0 * dim + c0, i01 = i00 + 1, i10 = i00 + dim, i11 = i10 + 1;

            float w00 = (1 - fr) * (1 - fc), w01 = (1 - fr) * fc, w10 = fr * (1 - fc), w11 = fr * fc;

            position = positions[i00] * w00 + positions[i01] * w01 + positions[i10] * w10 + positions[i11] * w11;

            if (normals == null || normals.Length < dim * dim)
            {
                normal = fallbackNormal;
                return;
            }

            Vector3 n = normals[i00] * w00 + normals[i01] * w01 + normals[i10] * w10 + normals[i11] * w11;
            float length = n.Length();
            normal = length > 1e-6f ? n / length : fallbackNormal;
        }

        /// <summary>
        /// (u,v) with <c>bilerp(quad; u, v) == p</c>, where quad is [P(0,0), P(1,0), P(1,1), P(0,1)].
        /// The quadratic closed form (h = u·e + v·f + u·v·g); for a parallelogram it reduces
        /// to the linear solve.
        /// </summary>
        private static Vector2 InverseBilinear(Vector2[] quad, Vector2 p)
        {
            Vector2 e = quad[1] - quad[0];
            Vector2 f = quad[3] - quad[0];
            Vector2 g = quad[0] - quad[1] + quad[2] - quad[3];
            Vector2 h = p - quad[0];

            float k2 = Cross(g, f);
            float k1 = Cross(e, f) + Cross(h, g);
            float k0 = Cross(h, e);

            float v;

            if (Math.Abs(k2) < 1e-6f * Math.Max(1e-6f, Math.Abs(k1)))
            {
                if (Math.Abs(k1) < 1e-12f)
                    return Vector2.Zero;

                v = -k0 / k1;
            }
            else
            {
                float disc = k1 * k1 - 4 * k0 * k2;
                if (disc < 0)
                    disc = 0;

                float w = (float)Math.Sqrt(disc);
                float v1 = (-k1 - w) / (2 * k2);
                float v2 = (-k1 + w) / (2 * k2);

                // The root inside the quad; otherwise whichever is nearer to it.
                v = Math.Abs(v1 - 0.5f) <= Math.Abs(v2 - 0.5f) ? v1 : v2;
            }

            // u from whichever component is better conditioned.
            float denomX = e.X + g.X * v;
            float denomY = e.Y + g.Y * v;
            float u = Math.Abs(denomX) >= Math.Abs(denomY)
                ? (Math.Abs(denomX) > 1e-12f ? (h.X - f.X * v) / denomX : 0)
                : (h.Y - f.Y * v) / denomY;

            return new Vector2(u, v);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        /// <summary>
        /// vrad's CalcFaceVectors solves world position from (s,t) with a 3x3 inverse
        /// ([axisU; axisV; normal] · P = [s·luxel, t·luxel, planeDist]); same here.
        /// </summary>
        private void PlaceSamples(IList<Vector3> winding, Vector3 normal, float planeDist)
        {
            // Rows of the matrix
            Vector3 r0 = AxisU, r1 = AxisV, r2 = normal;

            // Inverse via cross products / determinant (columns of adjugate)
            float det =
                r0.X * (r1.Y * r2.Z - r1.Z * r2.Y) -
                r0.Y * (r1.X * r2.Z - r1.Z * r2.X) +
                r0.Z * (r1.X * r2.Y - r1.Y * r2.X);

            if (Math.Abs(det) < 1e-8f)
            {
                // Face plane nearly parallel to a lightmap axis (broken uaxis/vaxis) —
                // fall back to projecting the face centroid everywhere so we at least
                // produce something instead of NaNs.
                Vector3 centroid = Vector3.Zero;
                for (int i = 0; i < winding.Count; i++)
                    centroid += winding[i];
                centroid /= winding.Count;

                for (int i = 0; i < Positions.Length; i++)
                {
                    Positions[i] = centroid;
                    Valid[i] = true;
                }
                return;
            }

            float invDet = 1.0f / det;

            Vector3 c0 = Vector3.Cross(r1, r2) * invDet;
            Vector3 c1 = Vector3.Cross(r2, r0) * invDet;
            Vector3 c2 = Vector3.Cross(r0, r1) * invDet;

            // 2D winding in (s,t) luxel units for the containment tests
            Vector2[] poly = new Vector2[winding.Count];
            for (int i = 0; i < winding.Count; i++)
            {
                poly[i] = new Vector2(
                    Vector3.Dot(winding[i], AxisU) / LuxelSize,
                    Vector3.Dot(winding[i], AxisV) / LuxelSize);
            }

            // Nudge pattern for lattice points off the face (BuildFacesamples pulls
            // samples inside; we try progressively deeper inward offsets).
            float[] nudges = { 0.0f, 0.35f, 0.49f };

            for (int j = 0; j < Height; j++)
            {
                for (int i = 0; i < Width; i++)
                {
                    float s = MinS + i;
                    float t = MinT + j;

                    bool placed = false;
                    float bestS = s, bestT = t;

                    for (int n = 0; n < nudges.Length && !placed; n++)
                    {
                        float d = nudges[n];

                        // center at depth 0, then the 4 diagonal nudges at this depth
                        int tries = d == 0.0f ? 1 : 4;

                        for (int k = 0; k < tries && !placed; k++)
                        {
                            float ns = s, nt = t;

                            if (d != 0.0f)
                            {
                                ns += (k & 1) == 0 ? d : -d;
                                nt += (k & 2) == 0 ? d : -d;
                            }

                            if (PointInPoly(poly, ns, nt, 0.05f))
                            {
                                bestS = ns;
                                bestT = nt;
                                placed = true;
                            }
                        }
                    }

                    if (!placed)
                    {
                        // Clamp to the winding's nearest point (off-face luxels still
                        // get plausible data; the engine bilinear-filters into them).
                        Vector2 nearest = NearestPointOnPoly(poly, new Vector2(s, t));
                        bestS = nearest.X;
                        bestT = nearest.Y;
                    }

                    Vector3 world = c0 * (bestS * LuxelSize) + c1 * (bestT * LuxelSize) + c2 * planeDist;

                    int index = j * Width + i;
                    Positions[index] = world;
                    Valid[index] = placed;
                }
            }
        }

        private static bool PointInPoly(Vector2[] poly, float x, float y, float epsilon)
        {
            // Winding order is arbitrary — test "same side of every edge" both ways.
            int pos = 0, neg = 0;

            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];

                float cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

                if (cross > epsilon) pos++;
                else if (cross < -epsilon) neg++;

                if (pos > 0 && neg > 0)
                    return false;
            }

            return true;
        }

        private static Vector2 NearestPointOnPoly(Vector2[] poly, Vector2 p)
        {
            float bestDist = float.MaxValue;
            Vector2 best = poly[0];

            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];
                Vector2 ab = b - a;

                float len2 = ab.LengthSquared();
                float t = len2 > 1e-12f ? Vector2.Dot(p - a, ab) / len2 : 0;
                t = Math.Max(0, Math.Min(1, t));

                Vector2 q = a + ab * t;
                float d = (p - q).LengthSquared();

                if (d < bestDist)
                {
                    bestDist = d;
                    best = q;
                }
            }

            // Pull slightly inside so the sample doesn't sit exactly on the edge.
            Vector2 centroid = Vector2.Zero;
            for (int i = 0; i < poly.Length; i++)
                centroid += poly[i];
            centroid /= poly.Length;

            Vector2 inward = centroid - best;
            float ilen = inward.Length();
            if (ilen > 1e-6f)
                best += inward / ilen * Math.Min(0.45f, ilen);

            return best;
        }

        /// <summary>
        /// vbsp's TextureAxisFromPlane fallback (the classic quake baseaxis table):
        /// picks the best-aligned cardinal plane's axes.
        /// </summary>
        public static void TextureAxisFromPlane(Vector3 normal, out Vector3 uAxis, out Vector3 vAxis)
        {
            Vector3[] baseaxis =
            {
                new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, -1, 0), // floor
                new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, -1, 0), // ceiling
                new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1), // west wall
                new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, -1), // east wall
                new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), // south wall
                new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), // north wall
            };

            int bestAxis = 0;
            float best = 0;

            for (int i = 0; i < 6; i++)
            {
                float dot = Vector3.Dot(normal, baseaxis[i * 3]);
                if (dot > best)
                {
                    best = dot;
                    bestAxis = i;
                }
            }

            uAxis = baseaxis[bestAxis * 3 + 1];
            vAxis = baseaxis[bestAxis * 3 + 2];
        }
    }
}
