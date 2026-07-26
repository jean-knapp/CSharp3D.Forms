using System;
using System.Collections.Generic;
using System.Numerics;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// A parsed <c>lights.rad</c> — the file that tells VRAD which materials emit light,
    /// and how much. Port of <c>ReadLightFile</c> / <c>LightForTexture</c>
    /// (utils\vrad\vrad.cpp:197 and :305).
    ///
    /// VRAD reads three of these, later ones overriding earlier ones:
    /// the global <c>lights.rad</c> (game dir, else next to vrad.exe), an optional
    /// designer file from <c>-lights</c>, and <c>&lt;mapname&gt;.rad</c> beside the map.
    /// Load them into one instance in that order with <see cref="Parse"/>.
    ///
    /// Line forms handled (everything else is ignored, as vrad does):
    /// <list type="bullet">
    /// <item><c>materialname R G B [brightness]</c> — a texture light. The value goes
    /// through the same <c>LightForString</c> gamma-2.2 conversion as a <c>_light</c>
    /// keyvalue.</item>
    /// <item><c>hdr:</c> / <c>ldr:</c> prefixes — we bake LDR, so <c>hdr:</c> lines are
    /// skipped and <c>ldr:</c> lines are taken with the prefix stripped.</item>
    /// <item><c>noshadow &lt;name&gt;</c> — material that casts no shadow.</item>
    /// <item><c>forcetextureshadow &lt;model&gt;</c> — recorded, not used yet (it drives
    /// -TextureShadows, which the preview doesn't do).</item>
    /// </list>
    /// </summary>
    public class RadFile
    {
        private readonly Dictionary<string, Vector3> _texlights =
            new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _noShadow =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _forceTextureShadow = new List<string>();

        /// <summary>Number of emissive materials known.</summary>
        public int TexlightCount { get { return _texlights.Count; } }

        /// <summary>Material names flagged <c>noshadow</c>.</summary>
        public ICollection<string> NoShadowMaterials { get { return _noShadow; } }

        /// <summary>Models named by <c>forcetextureshadow</c> (recorded, unused so far).</summary>
        public IList<string> ForceTextureShadowModels { get { return _forceTextureShadow; } }

        /// <summary>
        /// Merge the lines of one .rad file into this instance. Later files override
        /// earlier ones for the same material — vrad warns and does the same.
        /// </summary>
        public void Parse(IEnumerable<string> lines)
        {
            if (lines == null)
                return;

            foreach (string raw in lines)
            {
                if (raw == null)
                    continue;

                string line = raw;

                // ldr:/hdr: selector — we produce LDR lightmaps.
                if (line.StartsWith("hdr:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.StartsWith("ldr:", StringComparison.OrdinalIgnoreCase))
                    line = line.Substring(4);

                line = line.Trim();

                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                    continue;

                if (string.Equals(parts[0], "noshadow", StringComparison.OrdinalIgnoreCase))
                {
                    _noShadow.Add(StripExtension(Normalize(parts[1])));
                    continue;
                }

                if (string.Equals(parts[0], "forcetextureshadow", StringComparison.OrdinalIgnoreCase))
                {
                    _forceTextureShadow.Add(Normalize(parts[1]));
                    continue;
                }

                Vector3 value;
                if (!SourceLight.ParseLightString(line.Substring(parts[0].Length).Trim(), out value))
                    continue;

                _texlights[Normalize(parts[0])] = value;
            }
        }

        /// <summary>
        /// <c>LightForTexture</c>: the linear emitted color for a material name, or zero
        /// when the material is not a texture light.
        /// </summary>
        public Vector3 LightForTexture(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return Vector3.Zero;

            Vector3 value;
            return _texlights.TryGetValue(Normalize(materialName), out value) ? value : Vector3.Zero;
        }

        public bool IsNoShadow(string materialName)
        {
            return !string.IsNullOrEmpty(materialName)
                && _noShadow.Contains(StripExtension(Normalize(materialName)));
        }

        /// <summary>Material names are matched case-insensitively with / separators.</summary>
        private static string Normalize(string name)
        {
            return name.Replace('\\', '/').Trim();
        }

        private static string StripExtension(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }
    }
}
