using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The Source-engine light types VRAD distinguishes (bspfile.h emittype_t).
    /// </summary>
    public enum SourceLightType
    {
        /// <summary>light — omnidirectional, distance falloff.</summary>
        Point,

        /// <summary>light_spot — cone with penumbra, distance falloff.</summary>
        Spot,

        /// <summary>light_environment sun — directional, no falloff, needs sky visibility.</summary>
        SkyLight,

        /// <summary>light_environment ambient — sky dome, no falloff, needs sky visibility.</summary>
        SkyAmbient,

        /// <summary>
        /// emit_surface — a texture light. Not an entity: VRAD makes one of these per
        /// patch of every face whose material appears in lights.rad
        /// (CreateDirectLights, lightmap.cpp:1710). One-sided (nothing behind the
        /// emitting plane is lit) with a pure inverse-square falloff.
        /// The baker derives these itself from <c>BakeFace.EmitColor</c>.
        /// </summary>
        Surface,
    }

    /// <summary>
    /// One light exactly as VRAD would build it from a map entity (directlight_t,
    /// vrad.h + lightmap.cpp parsing). All the parsing math — gamma-2.2 color
    /// conversion, the _fifty_percent_distance inverse-quadratic solve, spot cone
    /// cosines, hard-falloff fades — is ported verbatim so a preview using these
    /// values matches a compile.
    /// </summary>
    public class SourceLight
    {
        public SourceLightType Type;

        public Vector3 Origin;

        /// <summary>
        /// Beam direction (the way the light travels), for Spot/SkyLight.
        /// vrad's dl->light.normal.
        /// </summary>
        public Vector3 Normal;

        /// <summary>Linear-space RGB intensity in vrad units (LightForString output).</summary>
        public Vector3 Intensity;

        public float ConstantAttn;
        public float LinearAttn;
        public float QuadraticAttn;

        /// <summary>cos(inner cone) — full brightness inside this (vrad stopdot).</summary>
        public float StopDot;

        /// <summary>cos(outer cone) — zero outside this (vrad stopdot2).</summary>
        public float StopDot2;

        /// <summary>Penumbra falloff exponent between the cones (_exponent).</summary>
        public float Exponent;

        /// <summary>Hard-falloff fade window (vrad m_flStartFadeDistance/m_flEndFadeDistance).</summary>
        public float StartFadeDistance;

        /// <summary>End of the fade window; end &lt; start means "not set".</summary>
        public float EndFadeDistance = -1.0f;

        /// <summary>Max distance fed into the falloff polynomial (vrad m_flCapDist).</summary>
        public float CapDist = 1.0e22f;

        /// <summary>sin(SunSpreadAngle) for soft sun sampling (vrad g_SunAngularExtent).</summary>
        public float SunAngularExtent;

        /// <summary>Whatever the caller wants to track the light back to (its entity).</summary>
        public object SourceKey;

        // ---- emitter clustering (Surface lights only) ----

        /// <summary>
        /// Centre of the emitter group this light belongs to, and the distance from it to
        /// the furthest member. Zero radius means "not clustered — always use this light".
        ///
        /// A texture light becomes a grid of emitters covering one face. Close up that
        /// grid is what gives the soft shadow of an area light; from across the room it
        /// is indistinguishable from a single light of the summed intensity, and paying
        /// for every emitter there is pure waste. So the baker emits both the members and
        /// one <see cref="IsClusterRoot"/> aggregate, and each receiving face picks
        /// whichever is appropriate for its distance — see LightmapBaker.UseInCluster.
        /// </summary>
        public Vector3 ClusterOrigin;

        public float ClusterRadius;

        /// <summary>True on the aggregate light standing in for the whole group.</summary>
        public bool IsClusterRoot;

        /// <summary>
        /// Distance beyond which this light contributes less than a quarter LDR step,
        /// used for face culling (replaces vrad's per-light PVS, which is a speedup
        /// only). Infinite for sun/sky and no-falloff lights.
        /// </summary>
        public float CullRadius()
        {
            if (Type == SourceLightType.SkyLight || Type == SourceLightType.SkyAmbient)
                return float.MaxValue;

            if (EndFadeDistance > StartFadeDistance)
                return EndFadeDistance;

            float maxC = Math.Max(Intensity.X, Math.Max(Intensity.Y, Intensity.Z));
            if (maxC <= 0)
                return 0;

            // Solve constant + linear·d + quadratic·d² = maxC / threshold
            const float threshold = 0.25f;
            float target = maxC / threshold;

            if (QuadraticAttn > 1e-10f)
            {
                // quadratic·d² + linear·d + (constant - target) = 0
                double a = QuadraticAttn, b = LinearAttn, c = ConstantAttn - target;
                double disc = b * b - 4 * a * c;
                if (disc <= 0)
                    return 0;
                return (float)Math.Min(((-b + Math.Sqrt(disc)) / (2 * a)), CapDistOrHuge());
            }

            if (LinearAttn > 1e-10f)
                return (float)Math.Min((target - ConstantAttn) / LinearAttn, CapDistOrHuge());

            // Pure constant attenuation never falls off.
            return float.MaxValue;
        }

        private double CapDistOrHuge()
        {
            // Past CapDist the falloff is frozen, so if the light is still bright there
            // it stays bright forever — the cull radius must not stop at CapDist unless
            // a fade window (handled above) kills it.
            return double.MaxValue;
        }

        // ==================== parsing (lightmap.cpp ports) ====================

        /// <summary>
        /// Parse a Source "_light"-style value ("R G B S", 1/3/4/8 numbers) into a
        /// linear intensity vector — LightForString (lightmap.cpp:1145) verbatim,
        /// LDR path.
        /// </summary>
        public static bool ParseLightString(string value, out Vector3 intensity)
        {
            intensity = Vector3.Zero;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            double r = 0, g = 0, b = 0, scaler = 0;
            int argCnt = 0;

            for (int i = 0; i < parts.Length && i < 8; i++)
            {
                double parsed;
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    break;

                switch (i)
                {
                    case 0: r = parsed; break;
                    case 1: g = parsed; break;
                    case 2: b = parsed; break;
                    case 3: scaler = parsed; break;
                }

                argCnt++;
            }

            if (argCnt == 8) // LDR 4-tuple + HDR 4-tuple; keep the LDR one
                argCnt = 4;

            if (r < 0 || g < 0 || b < 0 || scaler < 0)
                return false;

            float ir = (float)(Math.Pow(r / 255.0, 2.2) * 255); // convert to linear

            switch (argCnt)
            {
                case 1:
                    intensity = new Vector3(ir, ir, ir);
                    break;

                case 3:
                case 4:
                    intensity = new Vector3(
                        ir,
                        (float)(Math.Pow(g / 255.0, 2.2) * 255),
                        (float)(Math.Pow(b / 255.0, 2.2) * 255));

                    if (argCnt == 4)
                        intensity *= (float)(scaler / 255.0);
                    break;

                default:
                    return false;
            }

            return true;
        }

        /// <summary>
        /// mathlib SolveInverseQuadratic: fit y = a·x² + b·x + c through three points.
        /// </summary>
        public static bool SolveInverseQuadratic(float x1, float y1, float x2, float y2, float x3, float y3,
                                                 out float a, out float b, out float c)
        {
            a = b = c = 0;

            float det = (x1 - x2) * (x1 - x3) * (x2 - x3);

            if (det == 0.0f)
                return false;

            a = (x3 * (-y1 + y2) + x2 * (y1 - y3) + x1 * (-y2 + y3)) / det;
            b = (x3 * x3 * (y1 - y2) + x1 * x1 * (y2 - y3) + x2 * x2 * (-y1 + y3)) / det;
            c = (x1 * x3 * (-x1 + x3) * y2 + x2 * x2 * (x3 * y1 - x1 * y3) + x2 * (-(x3 * x3 * y1) + x1 * x1 * y3)) / det;

            return true;
        }

        private static float FLerp(float f1, float f2, float i1, float i2, float x)
        {
            return f1 + (f2 - f1) * (x - i1) / (i2 - i1);
        }

        /// <summary>
        /// mathlib SolveInverseQuadraticMonotonic (mathlib_base.cpp:1497): same fit, but
        /// nudges the midpoint toward the linear ramp until the curve is monotonic.
        /// </summary>
        public static bool SolveInverseQuadraticMonotonic(float x1, float y1, float x2, float y2, float x3, float y3,
                                                          out float a, out float b, out float c)
        {
            a = b = c = 0;

            // sort by x
            if (x1 > x2) { Swap(ref x1, ref x2); Swap(ref y1, ref y2); }
            if (x2 > x3) { Swap(ref x2, ref x3); Swap(ref y2, ref y3); }
            if (x1 > x2) { Swap(ref x1, ref x2); Swap(ref y1, ref y2); }

            for (float blend = 0.0f; blend <= 1.0f; blend += 0.05f)
            {
                float tempy2 = (1 - blend) * y2 + blend * FLerp(y1, y3, x1, x3, x2);

                if (!SolveInverseQuadratic(x1, y1, x2, tempy2, x3, y3, out a, out b, out c))
                    return false;

                float derivative = 2.0f * a + b;

                if (y1 < y2 && y2 < y3) // monotonically increasing
                {
                    if (derivative >= 0.0f)
                        return true;
                }
                else if (y1 > y2 && y2 > y3) // monotonically decreasing
                {
                    if (derivative <= 0.0f)
                        return true;
                }
                else
                {
                    return true;
                }
            }

            return true;
        }

        private static void Swap(ref float a, ref float b)
        {
            float t = a; a = b; b = t;
        }

        /// <summary>
        /// map_utils.cpp SetupLightNormalFromProps: build the beam direction from the
        /// entity's "angles"/"angle"/"pitch" keys. angle -1 = up, -2 = down.
        /// angles is Source's (pitch, yaw, roll).
        /// </summary>
        public static Vector3 BeamDirectionFromProps(Vector3 angles, float angle, float pitch)
        {
            const float ANGLE_UP = -1.0f;
            const float ANGLE_DOWN = -2.0f;

            float x, y, z;

            if (angle == ANGLE_UP)
            {
                x = 0; y = 0; z = 1;
            }
            else if (angle == ANGLE_DOWN)
            {
                x = 0; y = 0; z = -1;
            }
            else
            {
                if (angle == 0)
                    angle = angles.Y; // angles = (pitch, yaw, roll); yaw

                x = (float)Math.Cos(angle / 180.0 * Math.PI);
                y = (float)Math.Sin(angle / 180.0 * Math.PI);
                z = 0;
            }

            if (pitch == 0)
                pitch = angles.X;

            z = (float)Math.Sin(pitch / 180.0 * Math.PI);
            float cosPitch = (float)Math.Cos(pitch / 180.0 * Math.PI);
            x *= cosPitch;
            y *= cosPitch;

            return new Vector3(x, y, z);
        }
    }
}
