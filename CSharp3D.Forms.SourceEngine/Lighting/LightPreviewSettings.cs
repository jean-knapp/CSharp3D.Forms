using System;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The user-facing knobs of the lighting preview, one set per map.
    ///
    /// The fields and their ranges are Hammer++'s <c>light_plus</c> VMF block
    /// (lighting-preview-re/01-how-hammerpp-does-it.md §3), so a map tuned in either
    /// editor keeps its settings in the other. Two of them have a different meaning
    /// here because the baker is progressive rather than one-shot:
    /// <see cref="SamplesSun"/> and <see cref="SamplesAmbient"/> are the ray counts of
    /// the FINAL pass (the coarse passes use fewer), not of every bake.
    ///
    /// Fields the preview does not use (<see cref="SamplesVis"/>, <see cref="Supersample"/>,
    /// <see cref="BleedHack"/>, <see cref="Cubemap"/>, <see cref="Debug"/>) are still
    /// carried so a Hammer++ map round-trips byte-for-byte through this editor.
    /// </summary>
    public sealed class LightPreviewSettings
    {
        // ---- ray counts ----

        /// <summary>Soft-sun rays per luxel in the final pass (Hammer++ 1..300).</summary>
        public int SamplesSun;

        /// <summary>Sky-dome rays per luxel in the final pass (Hammer++ 1..1024).</summary>
        public int SamplesAmbient;

        /// <summary>Hammer++'s light visibility-octree rays. Carried, not used.</summary>
        public int SamplesVis = 256;

        // ---- light model ----

        /// <summary>
        /// CS:GO VRAD's <c>SoftenCosineTerm</c>: N·L becomes (N·L + N·L²)/2. On in CS:GO-branch
        /// VRAD (off with <c>-nosoften</c>); absent from the SDK 2013 / Hammer++ era VRAD.
        /// </summary>
        public bool SoftenCosine;

        /// <summary>
        /// Ambient occlusion strength. 0 = off (SDK 2013 VRAD has no AO); 1 = CS:GO VRAD's
        /// AO exactly (its squared visibility term); higher values darken creases further.
        /// Hammer++ range 0..3.
        /// </summary>
        public float AoScale;

        /// <summary>Multiplies every light's intensity (Hammer++ ≥ 0).</summary>
        public float BrightnessScale = 1.0f;

        /// <summary>Multiplies every light's cull radius (Hammer++ 0.1..10).</summary>
        public float RadiusScale = 1.0f;

        /// <summary>Radiosity bounce on/off.</summary>
        public bool Bounced = true;

        // ---- scheduling ----

        /// <summary>Re-bake automatically after every edit. Off = only on an explicit bake command.</summary>
        public bool Incremental = true;

        /// <summary>Seconds to wait after the last edit before an automatic re-bake (Hammer++ 0..60).</summary>
        public float IncrementalDelay;

        /// <summary>Radius of "bake around camera", world units (Hammer++ 1..99999).</summary>
        public float BakeDist = 1024.0f;

        // ---- inputs ----

        /// <summary>Extra .rad file of texture lights, on top of lights.rad and &lt;map&gt;.rad.</summary>
        public string Texlight = string.Empty;

        // ---- carried for Hammer++ round-trip, unused by this preview ----

        public bool Supersample;
        public bool BleedHack = true;
        public bool Cubemap = true;
        public bool Debug;

        /// <summary>
        /// Defaults that reproduce the target game's own VRAD. <paramref name="csgoBranch"/>
        /// selects CS:GO-family VRAD (soften + AO on by default); everything else gets the
        /// SDK 2013 behaviour, which is also Hammer++'s default. Ray counts are VRAD's own
        /// final-quality numbers (NSAMPLES_SUN_AREA_LIGHT = 30, NUMVERTEXNORMALS = 162), not
        /// Hammer++'s lower interactive defaults: this baker refines toward them in the
        /// background instead of paying for them up front.
        /// </summary>
        public static LightPreviewSettings Defaults(bool csgoBranch)
        {
            return new LightPreviewSettings
            {
                SamplesSun = 30,
                SamplesAmbient = 162,
                SoftenCosine = csgoBranch,
                AoScale = csgoBranch ? 1.0f : 0.0f,
            };
        }

        /// <summary>Clamp every field into Hammer++'s accepted range.</summary>
        public void Clamp()
        {
            SamplesSun = Math.Max(1, Math.Min(300, SamplesSun));
            SamplesAmbient = Math.Max(1, Math.Min(1024, SamplesAmbient));
            SamplesVis = Math.Max(1, SamplesVis);
            AoScale = Clamp(AoScale, 0.0f, 3.0f);
            BrightnessScale = Math.Max(0.0f, Finite(BrightnessScale, 1.0f));
            RadiusScale = Clamp(RadiusScale, 0.1f, 10.0f);
            IncrementalDelay = Clamp(IncrementalDelay, 0.0f, 60.0f);
            BakeDist = Clamp(BakeDist, 1.0f, 99999.0f);
            Texlight = Texlight ?? string.Empty;
        }

        public LightPreviewSettings Clone()
        {
            return (LightPreviewSettings)MemberwiseClone();
        }

        /// <summary>
        /// Whether two settings bake to the same lightmaps. Scheduling fields and the
        /// carried-only fields do not count — changing them must not throw away a bake.
        /// </summary>
        public bool SameBake(LightPreviewSettings other)
        {
            return other != null
                && SamplesSun == other.SamplesSun
                && SamplesAmbient == other.SamplesAmbient
                && SoftenCosine == other.SoftenCosine
                && AoScale == other.AoScale
                && BrightnessScale == other.BrightnessScale
                && RadiusScale == other.RadiusScale
                && Bounced == other.Bounced
                && string.Equals(Texlight, other.Texlight, StringComparison.OrdinalIgnoreCase);
        }

        private static float Clamp(float value, float min, float max)
        {
            value = Finite(value, min);
            return value < min ? min : value > max ? max : value;
        }

        private static float Finite(float value, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }
    }
}
