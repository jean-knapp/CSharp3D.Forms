using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// Builds <see cref="SourceLight"/>s from entity keyvalues — the entity-parsing half
    /// of VRAD's CreateDirectLights (lightmap.cpp:1695), covering the classnames
    /// light, light_spot and light_environment. The caller supplies keyvalues through a
    /// delegate so this stays independent of any VMF/BSP object model.
    /// </summary>
    public static class SourceLightParser
    {
        /// <summary>Reads one entity key, null when absent.</summary>
        public delegate string KeyValueGetter(string key);

        /// <summary>Resolves a targetname to that entity's origin, null when absent.</summary>
        public delegate Vector3? TargetResolver(string targetName);

        /// <summary>
        /// Parse one entity into zero or more lights (light_environment yields two: sun
        /// + sky ambient, ParseLightEnvironment lightmap.cpp:1595). Returns an empty
        /// list for non-light classnames.
        ///
        /// <paramref name="haveSkyLight"/> mirrors vrad's gSkyLight: only the FIRST
        /// light_environment in the map takes effect. The caller owns that flag across
        /// the entity loop.
        /// </summary>
        public static List<SourceLight> Parse(string className, KeyValueGetter kv, TargetResolver resolveTarget,
                                              ref bool haveSkyLight, object sourceKey = null)
        {
            List<SourceLight> results = new List<SourceLight>();

            if (string.IsNullOrEmpty(className))
                return results;

            switch (className.ToLowerInvariant())
            {
                case "light":
                {
                    SourceLight dl = new SourceLight { SourceKey = sourceKey };
                    ParseGeneric(dl, kv, resolveTarget);
                    dl.Type = SourceLightType.Point;
                    SetFalloffParams(dl, kv);
                    if (dl.Intensity != Vector3.Zero)
                        results.Add(dl);
                    break;
                }

                case "light_spot":
                {
                    SourceLight dl = new SourceLight { SourceKey = sourceKey };
                    ParseGeneric(dl, kv, resolveTarget);
                    ParseSpot(dl, kv);
                    SetFalloffParams(dl, kv);
                    if (dl.Intensity != Vector3.Zero)
                        results.Add(dl);
                    break;
                }

                case "light_environment":
                {
                    if (haveSkyLight)
                        break; // vrad honors only the first light_environment (gSkyLight)

                    SourceLight sun = new SourceLight { SourceKey = sourceKey };
                    ParseGeneric(sun, kv, resolveTarget);
                    sun.Type = SourceLightType.SkyLight;

                    string angleStr = kv("SunSpreadAngle");
                    if (!string.IsNullOrEmpty(angleStr))
                        sun.SunAngularExtent = (float)Math.Sin(Math.PI / 180.0 * ParseFloat(angleStr));

                    SourceLight ambient = new SourceLight { SourceKey = sourceKey };
                    ambient.Type = SourceLightType.SkyAmbient;
                    ambient.Origin = sun.Origin;

                    Vector3 ambientIntensity;
                    if (SourceLight.ParseLightString(kv("_ambient"), out ambientIntensity))
                        ambient.Intensity = ambientIntensity;
                    else
                        ambient.Intensity = sun.Intensity * 0.5f; // vrad default: half the sun

                    haveSkyLight = true;
                    results.Add(sun);
                    results.Add(ambient);
                    break;
                }
            }

            return results;
        }

        /// <summary>ParseLightGeneric (lightmap.cpp:1213): intensity + beam direction.</summary>
        private static void ParseGeneric(SourceLight dl, KeyValueGetter kv, TargetResolver resolveTarget)
        {
            dl.Origin = ParseVector(kv("origin"));

            Vector3 intensity;
            if (SourceLight.ParseLightString(kv("_light"), out intensity))
                dl.Intensity = intensity;

            // point towards target if we have one
            string target = kv("target");
            Vector3? dest = null;

            if (!string.IsNullOrEmpty(target) && resolveTarget != null)
                dest = resolveTarget(target);

            if (dest.HasValue)
            {
                Vector3 normal = dest.Value - dl.Origin;
                float length = normal.Length();
                dl.Normal = length > 1e-6f ? normal / length : new Vector3(0, 0, -1);
            }
            else
            {
                // point down angle: "angles" (pitch yaw roll) + "pitch" + "angle" keys
                Vector3 angles = ParseVector(kv("angles"));
                float pitch = ParseFloat(kv("pitch"));
                float angle = ParseFloat(kv("angle"));
                dl.Normal = SourceLight.BeamDirectionFromProps(angles, angle, pitch);
            }
        }

        /// <summary>SetLightFalloffParams (lightmap.cpp:1274), standard falloff model.</summary>
        private static void SetFalloffParams(SourceLight dl, KeyValueGetter kv)
        {
            dl.StartFadeDistance = 0;
            dl.EndFadeDistance = -1;
            dl.CapDist = 1.0e22f;

            float d50 = ParseFloat(kv("_fifty_percent_distance"));

            if (d50 != 0)
            {
                float d0 = ParseFloat(kv("_zero_percent_distance"));
                if (d0 < d50)
                    d0 = 2.0f * d50;

                float a, b, c;
                if (!SourceLight.SolveInverseQuadraticMonotonic(0, 1.0f, d50, 2.0f, d0, 256.0f, out a, out b, out c))
                {
                    a = 0; b = 1; c = 0;
                }

                // rescale so at least the 50% value is exact even if monotonicity moved it
                float v50 = c + d50 * (b + d50 * a);
                float scale = 2.0f / v50;
                a *= scale;
                b *= scale;
                c *= scale;

                dl.QuadraticAttn = a;
                dl.LinearAttn = b;
                dl.ConstantAttn = c;

                if (ParseFloat(kv("_hardfalloff")) != 0)
                {
                    dl.EndFadeDistance = d0;
                    dl.StartFadeDistance = 0.75f * d0 + 0.25f * d50; // fade starts 3/4 between 50 and 0
                }
                else
                {
                    // Prevent the quadratic from brightening past its minimum: cap there
                    // and fade to zero at 10x that distance.
                    if (Math.Abs(a) > 0)
                    {
                        float flMax = b / (-2.0f * a); // where f' = 0
                        if (flMax > 0)
                        {
                            dl.CapDist = flMax;
                            dl.StartFadeDistance = flMax;
                            dl.EndFadeDistance = 10.0f * flMax;
                        }
                    }
                }
            }
            else
            {
                const float EQUAL_EPSILON = 0.001f;

                dl.ConstantAttn = ParseFloat(kv("_constant_attn"));
                dl.LinearAttn = ParseFloat(kv("_linear_attn"));
                dl.QuadraticAttn = ParseFloat(kv("_quadratic_attn"));

                // clamp values to >= 0
                if (dl.ConstantAttn < EQUAL_EPSILON)
                    dl.ConstantAttn = 0;

                if (dl.LinearAttn < EQUAL_EPSILON)
                    dl.LinearAttn = 0;

                if (dl.QuadraticAttn < EQUAL_EPSILON)
                    dl.QuadraticAttn = 0;

                if (dl.ConstantAttn < EQUAL_EPSILON && dl.LinearAttn < EQUAL_EPSILON && dl.QuadraticAttn < EQUAL_EPSILON)
                    dl.ConstantAttn = 1;

                // scale intensity for unit 100 distance
                float ratio = dl.ConstantAttn + 100 * dl.LinearAttn + 100 * 100 * dl.QuadraticAttn;
                if (ratio > 0)
                    dl.Intensity *= ratio;
            }
        }

        /// <summary>ParseLightSpot (lightmap.cpp:1376): cones, exponent, 180° = point.</summary>
        private static void ParseSpot(SourceLight dl, KeyValueGetter kv)
        {
            dl.Type = SourceLightType.Spot;

            float stopdot = ParseFloat(kv("_inner_cone"));
            if (stopdot == 0)
                stopdot = 10;

            float stopdot2 = ParseFloat(kv("_cone"));
            if (stopdot2 == 0)
                stopdot2 = stopdot;
            if (stopdot2 < stopdot)
                stopdot2 = stopdot;

            // This is a point light if stop dots are 180
            if (stopdot == 180 && stopdot2 == 180)
            {
                dl.StopDot = dl.StopDot2 = 0;
                dl.Type = SourceLightType.Point;
                dl.Exponent = 0;
                return;
            }

            if (stopdot > 90)
                stopdot = 90;
            if (stopdot2 > 90)
                stopdot2 = 90;

            dl.StopDot2 = (float)Math.Cos(stopdot2 / 180.0 * Math.PI);
            dl.StopDot = (float)Math.Cos(stopdot / 180.0 * Math.PI);
            dl.Exponent = ParseFloat(kv("_exponent"));
        }

        // ==================== small keyvalue helpers ====================

        public static float ParseFloat(string value)
        {
            float result;
            if (value != null && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return result;
            return 0;
        }

        public static Vector3 ParseVector(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Vector3.Zero;

            string[] parts = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            float x = parts.Length > 0 ? ParseFloat(parts[0]) : 0;
            float y = parts.Length > 1 ? ParseFloat(parts[1]) : 0;
            float z = parts.Length > 2 ? ParseFloat(parts[2]) : 0;

            return new Vector3(x, y, z);
        }
    }
}
