using System;
using OpenTK.Graphics.OpenGL;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// Whether this GL context can run the compute-shader lightmap bake, and why not when
    /// it cannot.
    ///
    /// The renderer never asks for a context version — <c>GLControl</c> is built from a
    /// <c>GraphicsMode</c> alone, so the driver hands back the highest COMPATIBILITY
    /// profile it supports (4.6 on anything current). That is deliberate and must stay
    /// that way: the line guides draw with <c>GL_LINE_STIPPLE</c>, which a core profile
    /// removes. So rather than requesting 4.3 and risking a core context that breaks the
    /// dotted overlays, this asks the context we already have what it can do.
    ///
    /// Probing is per context and cached: the answer cannot change for the life of one,
    /// and the queries are not free enough to run per bake.
    /// </summary>
    public static class GpuBakeCapability
    {
        /// <summary>
        /// Compute shaders and the buffer types the bake needs. Both arrived in 4.3, but
        /// a 4.2 driver exposing the ARB extensions can run it just as well.
        /// </summary>
        private const string ComputeExtension = "GL_ARB_compute_shader";

        private const string StorageBufferExtension = "GL_ARB_shader_storage_buffer_object";

        private static readonly object Gate = new object();

        private static readonly System.Collections.Generic.Dictionary<object, Result> Cache
            = new System.Collections.Generic.Dictionary<object, Result>();

        public struct Result
        {
            public bool Supported;

            /// <summary>Why it is unsupported, for the status bar. Empty when supported.</summary>
            public string Reason;

            /// <summary>Largest total invocations one work group may have.</summary>
            public int MaxWorkGroupInvocations;

            /// <summary>Largest dispatch, in work groups, along X.</summary>
            public int MaxWorkGroupCountX;
        }

        /// <summary>
        /// Ask what <paramref name="context"/> can do. MUST be called with that context
        /// current — every query below goes to whichever context the calling thread holds,
        /// and the key is only how the answer is remembered.
        /// </summary>
        public static Result Probe(object context)
        {
            lock (Gate)
            {
                Result cached;

                if (context != null && Cache.TryGetValue(context, out cached))
                    return cached;

                Result result = Query();

                if (context != null)
                    Cache[context] = result;

                return result;
            }
        }

        private static Result Query()
        {
            Result result = new Result();

            try
            {
                int major = GL.GetInteger(GetPName.MajorVersion);
                int minor = GL.GetInteger(GetPName.MinorVersion);

                // A pre-3.0 driver answers GetInteger(MajorVersion) with an error rather
                // than a version, leaving major at 0. Fall back to parsing the string,
                // which every GL that has ever existed answers.
                if (major == 0)
                    ParseVersionString(out major, out minor);

                bool byVersion = major > 4 || (major == 4 && minor >= 3);
                bool byExtension = HasExtension(ComputeExtension) && HasExtension(StorageBufferExtension);

                if (!byVersion && !byExtension)
                {
                    result.Reason = "needs OpenGL 4.3 or ARB_compute_shader (this context is "
                        + major + "." + minor + ")";
                    return result;
                }

                result.MaxWorkGroupInvocations = GL.GetInteger((GetPName)All.MaxComputeWorkGroupInvocations);
                GL.GetInteger((GetIndexedPName)All.MaxComputeWorkGroupCount, 0, out result.MaxWorkGroupCountX);

                // A driver that advertises the extension but answers nonsense to its own
                // limits cannot be trusted to run the dispatch either.
                if (result.MaxWorkGroupInvocations < 64 || result.MaxWorkGroupCountX < 1)
                {
                    result.Reason = "driver reports unusable compute limits";
                    return result;
                }

                // Clear anything the queries left behind, so the first real dispatch is not
                // blamed for a pre-existing error.
                while (GL.GetError() != ErrorCode.NoError)
                {
                }

                result.Supported = true;
                return result;
            }
            catch (Exception ex)
            {
                // An entry point the driver does not export throws rather than returning:
                // that is a definitive "no", not a crash worth propagating into a bake.
                result.Supported = false;
                result.Reason = ex.Message;
                return result;
            }
        }

        private static void ParseVersionString(out int major, out int minor)
        {
            major = 0;
            minor = 0;

            string version = GL.GetString(StringName.Version);

            if (string.IsNullOrEmpty(version))
                return;

            // "4.6.0 NVIDIA 552.22" — the leading "major.minor" is all that is specified.
            int dot = version.IndexOf('.');

            if (dot <= 0)
                return;

            int.TryParse(version.Substring(0, dot), out major);

            int end = dot + 1;
            while (end < version.Length && char.IsDigit(version[end]))
                end++;

            int.TryParse(version.Substring(dot + 1, end - dot - 1), out minor);
        }

        private static bool HasExtension(string name)
        {
            int count = GL.GetInteger(GetPName.NumExtensions);

            for (int i = 0; i < count; i++)
            {
                if (string.Equals(GL.GetString(StringNameIndexed.Extensions, i), name, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
