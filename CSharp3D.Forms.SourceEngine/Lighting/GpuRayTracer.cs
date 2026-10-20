using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace CSharp3D.Forms.SourceEngine.Lighting
{
    /// <summary>
    /// Casts batches of shadow rays against a <see cref="RayBvh"/> on the GPU, answering
    /// the same occlusion question <see cref="RayBvh.AnyHit"/> answers per ray on the CPU.
    ///
    /// Only the traversal moves. The light model — falloff, cones, colour, clustering,
    /// the sky and ambient-occlusion integrals — stays in <see cref="LightmapBaker"/>,
    /// because that is what has to match VRAD and it is arithmetically cheap. What costs
    /// is the millions of BVH descents behind it, and those are embarrassingly parallel.
    /// Splitting it here means the GPU path cannot drift from the CPU path's lighting; it
    /// can only answer the same visibility faster, or decline and let the CPU answer.
    ///
    /// Threading: every method must run with the tracer's GL context current. The baker
    /// owns a context shared with the renderer's for exactly this, so batches dispatch
    /// from the worker thread without stalling the UI.
    /// </summary>
    public sealed class GpuRayTracer : IDisposable
    {
        /// <summary>Must match local_size_x in the shader.</summary>
        private const int WorkGroupSize = 64;

        /// <summary>
        /// Where the compute shader is deployed, relative to the process directory. The
        /// IDE moves the engine's Shaders folder under resources/ at build time, so this
        /// mirrors what <c>Scene.ShaderDirectory</c> is set to.
        /// </summary>
        public static string ShaderDirectory = "resources/shaders/";

        private const string ShaderFile = "SourceEngine/LightmapBake/occlusion.glsl";

        private int _program;
        private int _nodeBuffer;
        private int _triBuffer;
        private int _rayBuffer;
        private int _resultBuffer;
        private int _distanceBuffer;

        private int _rayCapacity;
        private int _rayCountLocation;

        /// <summary>The BVH currently uploaded, so an unchanged scene re-uploads nothing.</summary>
        private RayBvh _uploaded;

        /// <summary>Null while usable; otherwise why this tracer gave up.</summary>
        public string FailureReason { get; private set; }

        public bool IsUsable { get { return FailureReason == null && _program != 0; } }

        /// <summary>
        /// Compile the program. Returns false — with <see cref="FailureReason"/> set — when
        /// the context cannot run it, which is a normal outcome the caller answers by
        /// staying on the CPU rather than an error worth throwing over.
        /// </summary>
        public bool Initialize(object context)
        {
            GpuBakeCapability.Result capability = GpuBakeCapability.Probe(context);

            if (!capability.Supported)
            {
                FailureReason = capability.Reason;
                return false;
            }

            string source = ReadShaderSource();

            if (source == null)
            {
                FailureReason = "compute shader not found at " + Path.Combine(ShaderDirectory, ShaderFile);
                return false;
            }

            int shader = GL.CreateShader(ShaderType.ComputeShader);

            // NUL-terminated, not OpenTK's GL.ShaderSource(int, string): that overload passes
            // the CHARACTER count as the byte length, so any multi-byte UTF-8 character in the
            // file cut that many bytes off the end and the compile failed with "unexpected
            // $end" (the same bug Shader.SetShaderSource fixes for the render shaders).
            GL.ShaderSource(shader, 1, new[] { source }, (int[])null);
            GL.CompileShader(shader);

            int compiled;
            GL.GetShader(shader, ShaderParameter.CompileStatus, out compiled);

            if (compiled == 0)
            {
                FailureReason = "compute shader failed to compile: " + GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                return false;
            }

            _program = GL.CreateProgram();
            GL.AttachShader(_program, shader);
            GL.LinkProgram(_program);

            // The program holds its own copy once linked; keeping the shader object alive
            // past this only leaks a name.
            GL.DetachShader(_program, shader);
            GL.DeleteShader(shader);

            int linked;
            GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out linked);

            if (linked == 0)
            {
                FailureReason = "compute program failed to link: " + GL.GetProgramInfoLog(_program);
                GL.DeleteProgram(_program);
                _program = 0;
                return false;
            }

            _rayCountLocation = GL.GetUniformLocation(_program, "uRayCount");

            _nodeBuffer = GL.GenBuffer();
            _triBuffer = GL.GenBuffer();
            _rayBuffer = GL.GenBuffer();
            _resultBuffer = GL.GenBuffer();
            _distanceBuffer = GL.GenBuffer();

            return true;
        }

        private static string ReadShaderSource()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string path = Path.Combine(Path.Combine(root, ShaderDirectory), ShaderFile);

            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        // ==================== scene ====================

        /// <summary>
        /// Upload a BVH, skipping the work when it is the one already there. The baker
        /// rebuilds its BVH only when geometry changes, so identity is the right test:
        /// the same object is by construction the same tree.
        /// </summary>
        public bool UploadScene(RayBvh bvh)
        {
            if (!IsUsable || bvh == null)
                return false;

            if (ReferenceEquals(_uploaded, bvh))
                return true;

            float[] nodes = bvh.PackNodesForGpu();
            float[] tris = bvh.PackTrianglesForGpu();

            if (nodes == null || tris == null)
                return false;

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _nodeBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, nodes.Length * sizeof(float), nodes,
                BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _triBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, tris.Length * sizeof(float), tris,
                BufferUsageHint.StaticDraw);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

            _uploaded = bvh;
            return true;
        }

        /// <summary>Forget the uploaded scene, so the next batch re-uploads it.</summary>
        public void InvalidateScene()
        {
            _uploaded = null;
        }

        // ==================== rays ====================

        /// <summary>
        /// Trace <paramref name="count"/> rays starting at ray <paramref name="first"/> and
        /// fill <paramref name="answers"/> from <paramref name="answerOffset"/> with one flag
        /// each — so a caller can slice one staged batch across several dispatches.
        ///
        /// <paramref name="rays"/> is the packed batch, <see cref="RayFloats"/> per ray in
        /// the order the shader declares (see <see cref="PackRay"/>), so the upload is a
        /// straight memcpy with no per-ray marshalling.
        /// </summary>
        public bool Trace(float[] rays, int first, int count, byte[] answers, int answerOffset)
        {
            if (!BeginUpload(count))
                return false;

            GL.BufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero,
                count * RayFloats * sizeof(float), ref rays[first * RayFloats]);

            return TraceUploaded(count, answers, answerOffset);
        }

        /// <summary>
        /// Make room for a dispatch of <paramref name="count"/> rays, to be filled with
        /// <see cref="Upload"/> and traced with <see cref="TraceUploaded"/> — for a caller
        /// whose rays are spread over several arrays, which can then go to the GPU without
        /// first being copied into one.
        /// </summary>
        public bool BeginUpload(int count)
        {
            if (!IsUsable || count <= 0 || _uploaded == null)
                return false;

            EnsureRayCapacity(count);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _rayBuffer);
            return true;
        }

        /// <summary>Upload the first <paramref name="count"/> rays of an array to slot <paramref name="at"/>.</summary>
        public void Upload(float[] rays, int count, int at)
        {
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _rayBuffer);
            GL.BufferSubData(BufferTarget.ShaderStorageBuffer, (IntPtr)((long)at * RayFloats * sizeof(float)),
                count * RayFloats * sizeof(float), rays);
        }

        /// <summary>Trace the <paramref name="count"/> rays uploaded since <see cref="BeginUpload"/>.</summary>
        public bool TraceUploaded(int count, byte[] answers, int answerOffset)
        {
            Run(count);

            uint[] results = ResultScratch(count);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _resultBuffer);
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero,
                count * sizeof(uint), results);

            if (!Finish())
                return false;

            for (int i = 0; i < count; i++)
                answers[answerOffset + i] = results[i] != 0 ? (byte)1 : (byte)0;

            return true;
        }

        /// <summary>
        /// Trace <see cref="ModeClosestHit"/> rays, as <see cref="Trace"/> does, filling
        /// <paramref name="faces"/> with the id of the nearest face each ray hits — -1 for a
        /// sky face or no hit — and <paramref name="distances"/> with how far along it is.
        /// </summary>
        public bool TraceClosest(float[] rays, int first, int count, int[] faces, float[] distances,
                                 int answerOffset)
        {
            if (!BeginUpload(count))
                return false;

            GL.BufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero,
                count * RayFloats * sizeof(float), ref rays[first * RayFloats]);

            Run(count);

            // The shader's NO_FACE is all bits set, which reads back as -1 into an int.
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _resultBuffer);
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero,
                count * sizeof(int), ref faces[answerOffset]);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _distanceBuffer);
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero,
                count * sizeof(float), ref distances[answerOffset]);

            return Finish();
        }

        /// <summary>Run the kernel over the uploaded rays.</summary>
        private void Run(int count)
        {
            GL.UseProgram(_program);

            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, _nodeBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1, _triBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2, _rayBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, _resultBuffer);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, _distanceBuffer);

            GL.Uniform1(_rayCountLocation, count);

            GL.DispatchCompute((count + WorkGroupSize - 1) / WorkGroupSize, 1, 1);

            // The reads that follow must see the writes, and only this barrier says so.
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
        }

        /// <summary>
        /// Unbind after the readback, and say whether the dispatch worked. One that errored
        /// gives no useful answers, and treating whatever is in the buffers as visibility
        /// would bake wrong shadows silently.
        /// </summary>
        private bool Finish()
        {
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);
            GL.UseProgram(0);

            return GL.GetError() == ErrorCode.NoError;
        }

        /// <summary>Reused readback staging — one batch per face, so this never shrinks.</summary>
        private uint[] _results;

        private uint[] ResultScratch(int count)
        {
            if (_results == null || _results.Length < count)
                _results = new uint[Math.Max(1024, count)];

            return _results;
        }

        /// <summary>
        /// Floats per packed ray: origin.xyz + tmax, direction.xyz + skipId, mode +
        /// ignoreFlags + two spare. Must match the shader's Ray struct.
        /// </summary>
        public const int RayFloats = 12;

        /// <summary>Answer "is anything hit" — <see cref="IRayOracle.AnyHit"/>.</summary>
        public const int ModeAnyHit = 0;

        /// <summary>Answer "does it reach the sky" — <see cref="IRayOracle.ReachesSky"/>.</summary>
        public const int ModeReachesSky = 1;

        /// <summary>Answer the nearest face and its distance — <see cref="RayBvh.ClosestHit"/>; see <see cref="TraceClosest"/>.</summary>
        public const int ModeClosestHit = 2;

        /// <summary>Pack one ray into the layout <see cref="Trace"/> expects.</summary>
        public static void PackRay(float[] rays, int index, Vector3 origin, Vector3 direction,
            float tmax, int skipId, int mode, RayTriangleFlags ignoreFlags)
        {
            int at = index * RayFloats;

            rays[at + 0] = origin.X;
            rays[at + 1] = origin.Y;
            rays[at + 2] = origin.Z;
            rays[at + 3] = tmax;
            rays[at + 4] = direction.X;
            rays[at + 5] = direction.Y;
            rays[at + 6] = direction.Z;
            rays[at + 7] = IntAsFloat(skipId);
            rays[at + 8] = IntAsFloat(mode);
            rays[at + 9] = IntAsFloat((int)ignoreFlags);
            rays[at + 10] = 0f;
            rays[at + 11] = 0f;
        }

        /// <summary>
        /// The bits of an int as a float, without the byte array BitConverter would allocate
        /// — this runs three times for every ray recorded.
        /// </summary>
        private static float IntAsFloat(int value)
        {
            IntFloat bits = default(IntFloat);
            bits.Int = value;
            return bits.Float;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct IntFloat
        {
            [FieldOffset(0)] public int Int;
            [FieldOffset(0)] public float Float;
        }

        /// <summary>
        /// Grow the ray and result buffers to hold a batch, in steps rather than exactly:
        /// batch sizes vary per face, and reallocating a multi-megabyte buffer per batch
        /// costs more than the slack does.
        /// </summary>
        private void EnsureRayCapacity(int count)
        {
            if (count <= _rayCapacity)
                return;

            int capacity = Math.Max(1024, _rayCapacity);

            while (capacity < count)
                capacity *= 2;

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _rayBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                capacity * RayFloats * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamDraw);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _resultBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                capacity * sizeof(uint), IntPtr.Zero, BufferUsageHint.StreamRead);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, _distanceBuffer);
            GL.BufferData(BufferTarget.ShaderStorageBuffer,
                capacity * sizeof(float), IntPtr.Zero, BufferUsageHint.StreamRead);

            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, 0);

            _rayCapacity = capacity;
        }

        public void Dispose()
        {
            // Buffer names belong to the context that made them; this must run with that
            // context current, as the baker's shutdown does.
            if (_program != 0)
            {
                GL.DeleteProgram(_program);
                _program = 0;
            }

            DeleteBuffer(ref _nodeBuffer);
            DeleteBuffer(ref _triBuffer);
            DeleteBuffer(ref _rayBuffer);
            DeleteBuffer(ref _resultBuffer);
            DeleteBuffer(ref _distanceBuffer);

            _uploaded = null;
            _rayCapacity = 0;
        }

        private static void DeleteBuffer(ref int buffer)
        {
            if (buffer == 0)
                return;

            GL.DeleteBuffer(buffer);
            buffer = 0;
        }
    }
}
