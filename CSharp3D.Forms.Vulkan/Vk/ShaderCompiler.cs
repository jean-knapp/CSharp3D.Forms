using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

namespace CSharp3D.Forms.Vulkan.Vk
{
    /// <summary>
    /// GLSL to SPIR-V, at run time, through shaderc.
    ///
    /// Vulkan takes only SPIR-V, and the machine building this has no Vulkan SDK to compile it
    /// with at build time - shaderc ships as a NuGet native instead, so the shaders stay GLSL
    /// text beside the GL ones and are compiled on first use. The result is cached next to the
    /// source keyed on a hash of the text, so a shader that has not changed costs a file read.
    ///
    /// A single <c>#include "common.glsl"</c> line is honoured by pasting the file in: shaderc's
    /// include callbacks are more machinery than one shared block of structs is worth.
    /// </summary>
    public sealed unsafe class ShaderCompiler
    {
        public enum Kind { RayGeneration, Miss, ClosestHit, AnyHit, Compute, Vertex, Fragment }

        /// <summary>
        /// Where the GLSL lives, when a host wants to say. Otherwise the usual places are
        /// tried: the scene's own shader directory, <c>resources/shaders</c> (the IDE),
        /// and <c>Shaders</c> (where the project's build puts them).
        /// </summary>
        public static string ShaderDirectory;

        private const string Subdirectory = "RayTracing";

        private readonly Shaderc _api;
        private readonly string _preferred;
        private string _root;

        /// <param name="preferredDirectory">A directory to look in first: the scene's ShaderDirectory, say.</param>
        public ShaderCompiler(string preferredDirectory = null)
        {
            _preferred = preferredDirectory;
            PreloadNative();
            _api = Shaderc.GetApi();
        }

        /// <summary>
        /// The directory holding RayTracing/, found once. Relative candidates are tried
        /// against the application directory and against this assembly's own, in that
        /// order, since a host may keep its assemblies in a subfolder.
        /// </summary>
        private string FindRoot(string fileName)
        {
            if (_root != null)
                return _root;

            List<string> candidates = new List<string>();

            foreach (string given in new[] { ShaderDirectory, _preferred, "resources/shaders", "Shaders", "shaders" })
            {
                if (string.IsNullOrEmpty(given))
                    continue;

                if (Path.IsPathRooted(given))
                {
                    candidates.Add(given);
                    continue;
                }

                candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, given));

                string beside = Path.GetDirectoryName(typeof(ShaderCompiler).Assembly.Location);

                if (!string.IsNullOrEmpty(beside))
                    candidates.Add(Path.Combine(beside, given));
            }

            foreach (string candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, Subdirectory, fileName)))
                {
                    _root = candidate;
                    return _root;
                }
            }

            throw new VulkanException("Shader " + fileName + " not found. Looked for " + Subdirectory + "/" + fileName + " under:\n  "
                + string.Join("\n  ", candidates.Select(Path.GetFullPath).Distinct()));
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        /// <summary>
        /// Load shaderc from beside this assembly before Silk.NET asks for it by name.
        ///
        /// Silk's loader looks in the application directory and the runtimes folder, not next
        /// to the assembly that binds the library. A host that keeps its assemblies in a
        /// subfolder - the IDE moves everything into bin\ after building - puts the native
        /// where Silk will not find it. Loading it here by full path means Silk's LoadLibrary
        /// by name is handed the module already in the process.
        /// </summary>
        private static void PreloadNative()
        {
            try
            {
                string beside = Path.Combine(Path.GetDirectoryName(typeof(ShaderCompiler).Assembly.Location) ?? string.Empty,
                    "shaderc_shared.dll");

                if (File.Exists(beside))
                    LoadLibraryW(beside);
            }
            catch (Exception)
            {
                // Not fatal here: Silk's own lookup runs next and reports properly if it fails.
            }
        }

        /// <summary>The SPIR-V for a shader file, compiled if the cache does not have it.</summary>
        public byte[] Compile(string fileName, Kind kind)
        {
            string path = Path.Combine(FindRoot(fileName), Subdirectory, fileName);

            if (!File.Exists(path))
                throw new VulkanException("Shader not found: " + Path.GetFullPath(path));

            string source = Resolve(File.ReadAllText(path), Path.GetDirectoryName(path));

            string cachePath = path + "." + Hash(source) + ".spv";

            if (File.Exists(cachePath))
                return File.ReadAllBytes(cachePath);

            byte[] spirv = CompileSource(source, fileName, kind);

            try
            {
                // Best effort: a read-only install just compiles every launch.
                foreach (string stale in Directory.GetFiles(Path.GetDirectoryName(path), fileName + ".*.spv"))
                    File.Delete(stale);

                File.WriteAllBytes(cachePath, spirv);
            }
            catch (Exception)
            {
            }

            return spirv;
        }

        private static string Resolve(string source, string directory)
        {
            const string marker = "#include \"common.glsl\"";

            int at = source.IndexOf(marker, StringComparison.Ordinal);

            if (at < 0)
                return source;

            string common = File.ReadAllText(Path.Combine(directory, "common.glsl"));
            return source.Substring(0, at) + common + source.Substring(at + marker.Length);
        }

        private static string Hash(string text)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder hex = new StringBuilder();

                for (int i = 0; i < 8; i++)
                    hex.Append(digest[i].ToString("x2"));

                return hex.ToString();
            }
        }

        private byte[] CompileSource(string source, string fileName, Kind kind)
        {
            Compiler* compiler = _api.CompilerInitialize();
            CompileOptions* options = _api.CompileOptionsInitialize();

            byte* sourcePtr = (byte*)SilkMarshal.StringToPtr(source);
            byte* namePtr = (byte*)SilkMarshal.StringToPtr(fileName);
            byte* entryPtr = (byte*)SilkMarshal.StringToPtr("main");

            try
            {
                // Ray tracing needs SPIR-V 1.4 or later; 1.5 is what Vulkan 1.2 guarantees.
                _api.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan12);
                _api.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc15);
                _api.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);

                int length = Encoding.UTF8.GetByteCount(source);

                CompilationResult* result = _api.CompileIntoSpv(compiler, sourcePtr, (nuint)length,
                    ToShaderKind(kind), namePtr, entryPtr, options);

                try
                {
                    CompilationStatus status = _api.ResultGetCompilationStatus(result);

                    if (status != CompilationStatus.Success)
                    {
                        string message = _api.ResultGetErrorMessageS(result);
                        throw new VulkanException("Shader " + fileName + " did not compile:\n" + message);
                    }

                    nuint size = _api.ResultGetLength(result);
                    byte* bytes = _api.ResultGetBytes(result);

                    byte[] spirv = new byte[(int)size];

                    fixed (byte* dst = spirv)
                        System.Buffer.MemoryCopy(bytes, dst, (long)size, (long)size);

                    return spirv;
                }
                finally
                {
                    _api.ResultRelease(result);
                }
            }
            finally
            {
                SilkMarshal.Free((nint)sourcePtr);
                SilkMarshal.Free((nint)namePtr);
                SilkMarshal.Free((nint)entryPtr);
                _api.CompileOptionsRelease(options);
                _api.CompilerRelease(compiler);
            }
        }

        private static ShaderKind ToShaderKind(Kind kind)
        {
            switch (kind)
            {
                case Kind.RayGeneration: return ShaderKind.RaygenShader;
                case Kind.Miss: return ShaderKind.MissShader;
                case Kind.ClosestHit: return ShaderKind.ClosesthitShader;
                case Kind.AnyHit: return ShaderKind.AnyhitShader;
                case Kind.Vertex: return ShaderKind.VertexShader;
                case Kind.Fragment: return ShaderKind.FragmentShader;
                default: return ShaderKind.ComputeShader;
            }
        }
    }
}
