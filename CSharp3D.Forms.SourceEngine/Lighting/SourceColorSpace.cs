using System;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// The Source engine's LDR lightmap output transform — the last step of "what will
    /// this look like in game", ported from
    /// <c>materialsystem\colorspace.cpp</c> (<c>ColorSpace::BuildGammaTable</c> /
    /// <c>LinearToLightmap</c>) and <c>public\materialsystem\imaterialsystem.h</c>
    /// (<c>OVERBRIGHT</c>).
    ///
    /// The chain in a real compile+run is:
    /// <list type="number">
    /// <item>VRAD's <c>FinalLightFace</c> writes RAW LINEAR light into the BSP as
    /// RGBExp32 — no gamma, no scale (radial.cpp:642; the old <c>-gamma</c>/<c>-scale</c>
    /// handling is gone in the CS:GO branch).</item>
    /// <item>The engine reads it back as <c>TexLightToLinear = c·2^exp/255</c>, i.e. the
    /// vrad value divided by 255, nominally a 0..4 range.</item>
    /// <item><c>ColorSpace::LinearToLightmap</c> converts that to the 8-bit lightmap
    /// texel: quantize to 1/1024, raise to 1/2.2, multiply by 1/OVERBRIGHT = 0.5,
    /// clamp, round to a byte.</item>
    /// <item>The pixel shader multiplies the texel by <c>LIGHT_MAP_SCALE</c> =
    /// <c>OVERBRIGHT</c> = 2 and modulates the (gamma-space) albedo with it.</item>
    /// </list>
    ///
    /// So on screen: <c>albedo_gamma · L^(1/2.2)</c>, where L is vrad's value / 255, with
    /// the multiplier capped at ~1.875 (the table's 4091 clamp binds before the ×0.5
    /// saturates). Note this is emphatically NOT <c>albedo · L</c> — the 1/2.2 lifts every
    /// midtone: a luxel at L = 0.25 shows as 0.53, not 0.25. Getting this wrong makes an
    /// otherwise perfect bake look far too dark and contrasty compared to the game.
    ///
    /// The preview stores exactly the byte the engine would store and the shader applies
    /// exactly the same ×2, so the preview's pixels are the game's pixels (modulo the
    /// bake itself), quantization included.
    /// </summary>
    public static class SourceColorSpace
    {
        /// <summary>OVERBRIGHT (imaterialsystem.h:16). LIGHT_MAP_SCALE in the shaders.</summary>
        public const float Overbright = 2.0f;

        /// <summary>MathLib_Init( 2.2f, 2.2f, 0.0f, OVERBRIGHT ) — cmaterialsystem.cpp:978.</summary>
        public const float ScreenGamma = 2.2f;

        /// <summary>
        /// colorspace.cpp's <c>linearToLightmap[4096]</c>: index = linear·1024.
        /// </summary>
        private static readonly byte[] _linearToLightmap = BuildLinearToLightmap();

        private static byte[] BuildLinearToLightmap()
        {
            // overbrightFactor = 1/overbright for the 2.0 and 4.0 cases (colorspace.cpp:120)
            float overbrightFactor = 1.0f / Overbright;

            byte[] table = new byte[4096];

            for (int i = 0; i < 4096; i++)
            {
                // convert from linear 0..4 (x1024) to screen corrected space
                float f = (float)Math.Pow(i / 1024.0, 1.0 / ScreenGamma);
                float v = f * overbrightFactor;

                if (v > 1.0f)
                    v = 1.0f;

                table[i] = (byte)(v * 255.0f + 0.5f);
            }

            return table;
        }

        /// <summary>
        /// vrad linear light units (255 = nominal full white) → the 8-bit lightmap texel
        /// the engine would upload. <c>LinearToLightmap</c> clamps the index at 4091, so
        /// the brightest representable texel is 239 → a ×1.875 albedo multiplier.
        /// </summary>
        public static byte EncodeLuxel(float linear255)
        {
            // TexLightToLinear's implied /255: vrad units → the engine's 0..4 range.
            float linear = linear255 * (1.0f / 255.0f);

            int index = (int)(linear * 1024.0f + 0.5f);   // RoundFloatToInt

            if (index < 0)
                index = 0;
            else if (index > 4091)
                index = 4091;

            return _linearToLightmap[index];
        }

        /// <summary>
        /// What the shader turns an encoded texel back into: the albedo multiplier
        /// (texel/255 · OVERBRIGHT). Only used by tests/tools — the GPU does this itself.
        /// </summary>
        public static float DecodeTexel(byte texel)
        {
            return texel * (1.0f / 255.0f) * Overbright;
        }
    }
}
