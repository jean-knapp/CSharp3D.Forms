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

        /// <summary>World position of every luxel (row-major, t rows), on the face plane.</summary>
        public Vector3[] Positions;

        /// <summary>Whether the luxel's lattice point (possibly nudged) is on the face.</summary>
        public bool[] Valid;

        /// <summary>
        /// vbsp's MAX_LIGHTMAP_DIM_WITHOUT_BORDER for CS:GO is 125; the preview clamps a
        /// touch lower and coarsens the luxel size instead of erroring like vbsp does.
        /// </summary>
        public const int MaxDim = 125;

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

                if (width > MaxDim || height > MaxDim)
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
