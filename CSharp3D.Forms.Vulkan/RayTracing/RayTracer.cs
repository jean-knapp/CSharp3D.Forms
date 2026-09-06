using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using CSharp3D.Forms.Vulkan.Vk;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Buffer = CSharp3D.Forms.Vulkan.Vk.VulkanDevice.Buffer;
using Image = CSharp3D.Forms.Vulkan.Vk.VulkanDevice.Image;

namespace CSharp3D.Forms.Vulkan.RayTracing
{
    /// <summary>
    /// One view's worth of ray tracing: the pipeline and its shader binding table, the
    /// descriptor sets, the images at the view's size, the exposure state, and the recording
    /// of a frame.
    ///
    /// A frame is: trace a few samples per pixel and fold them into the pixel's history
    /// (found again through the previous frame's camera when this one has moved), smooth the
    /// indirect light across each surface for as long as the history is short, compose,
    /// measure the average luminance and adapt the exposure to it, tone map into an 8-bit
    /// image, and blit that to the swapchain. A camera move keeps most of the picture; a
    /// change to the scene starts it over; a still view refines it every frame until it has
    /// converged.
    ///
    /// Not thread-affine, but not thread-safe either: the view that owns one calls it from
    /// one thread at a time.
    /// </summary>
    public sealed unsafe class RayTracer : IDisposable
    {
        private const int MaxTextures = 4096;

        /// <summary>Samples a pixel needs of its own before the composition stops smoothing it.</summary>
        private const float HistoryFull = 256f;

        /// <summary>How much history a pixel keeps through a camera move. Short enough to follow the picture.</summary>
        private const float MovingHistoryIndirect = 24f;
        private const float MovingHistoryDirect = 8f;

        /// <summary>Set to log every step of every frame. For chasing a hang, not for use.</summary>
        public static bool TraceFrames;

        private readonly VulkanDevice _device;
        private readonly GpuScene _scene;

        // ---- pipelines ----
        private DescriptorSetLayout _rtSetLayout;
        private DescriptorSetLayout _denoiseSetLayout;
        private DescriptorSetLayout _luminanceSetLayout;
        private DescriptorSetLayout _tonemapSetLayout;
        private PipelineLayout _rtPipelineLayout;
        private PipelineLayout _denoisePipelineLayout;
        private PipelineLayout _luminancePipelineLayout;
        private PipelineLayout _tonemapPipelineLayout;
        private Pipeline _rtPipeline;
        private Pipeline _denoisePipeline;
        private Pipeline _luminancePipeline;
        private Pipeline _tonemapPipeline;
        private readonly List<ShaderModule> _modules = new List<ShaderModule>();

        private Buffer _sbt;
        private StridedDeviceAddressRegionKHR _raygenRegion;
        private StridedDeviceAddressRegionKHR _missRegion;
        private StridedDeviceAddressRegionKHR _hitRegion;
        private StridedDeviceAddressRegionKHR _callableRegion;

        // ---- descriptors ----
        private DescriptorPool _pool;
        private DescriptorSet _rtSet;
        private DescriptorSet _denoiseSet;
        private DescriptorSet _luminanceSet;
        private DescriptorSet _tonemapSet;
        private int _boundSceneVersion = -1;
        private bool _imagesRebound;

        // ---- per size ----
        // The history is double buffered: a frame reads the other parity and writes its own.
        private readonly Image[] _position = new Image[2];
        private readonly Image[] _normal = new Image[2];
        private readonly Image[] _direct = new Image[2];
        private readonly Image[] _indirect = new Image[2];
        private Image _albedo;
        private Image _pingA;
        private Image _pingB;
        private Image _hdr;
        private Image _ldr;
        public uint Width { get; private set; }
        public uint Height { get; private set; }

        // ---- per frame ----
        private Buffer _frameBuffer;
        private Buffer _exposureBuffer;
        private CommandBuffer _cmd;
        private Semaphore _acquired;
        private Semaphore _rendered;
        private Fence _fence;
        private bool _fenceInUse;
        private uint _frameIndex;
        private Matrix4x4 _lastViewProj = Matrix4x4.Identity;
        private Matrix4x4 _lastInvViewProj;
        private bool _resetPending = true;
        private bool _exposureReset = true;

        /// <summary>Samples the picture has taken since the camera last moved. Zero right after a change.</summary>
        public uint Samples { get; private set; }

        /// <summary>Diffuse bounces after the first hit. One is what Lumen's final gather amounts to.</summary>
        public int Bounces { get; set; } = 1;

        /// <summary>Lights sampled per hit per frame; a random subset when the map has more.</summary>
        public int LightsPerSample { get; set; } = 8;

        /// <summary>Paths traced per pixel in one frame. More converge faster when a frame is cheap.</summary>
        public int SamplesPerFrame { get; set; } = 1;

        /// <summary>Unreal's Exposure Compensation, in stops. Lambda Engine ships at 0.</summary>
        public float ExposureBias { get; set; } = 0f;

        /// <summary>World units in a metre. Lambda Engine: 1 unit = 1.905 cm.</summary>
        public float UnitsPerMetre { get; set; } = 100f / 1.905f;

        public RayTracer(VulkanDevice device, GpuScene scene, ShaderCompiler compiler)
        {
            _device = device;
            _scene = scene;

            CreateLayouts();
            VulkanDevice.Stage("layouts created");
            CreateRayTracingPipeline(compiler);
            VulkanDevice.Stage("ray tracing pipeline created");
            CreateComputePipelines(compiler);
            VulkanDevice.Stage("compute pipelines created");
            CreateShaderBindingTable();
            VulkanDevice.Stage("shader binding table built");
            CreatePool();
            VulkanDevice.Stage("descriptors allocated");
            CreateFrameObjects();
            VulkanDevice.Stage("frame objects created");
        }

        // ==================== layouts ====================

        private void CreateLayouts()
        {
            // Set 0 of the ray tracing pipeline: everything raygen.rgen declares. The texture
            // array is bindless - only as many entries as there are textures are written, and
            // the shader indexes it with whatever the material says - and, being the one
            // binding with a variable count, it has to be the last.
            const ShaderStageFlags rg = ShaderStageFlags.RaygenBitKhr;

            DescriptorSetLayoutBinding[] rt =
            {
                Binding(0, DescriptorType.AccelerationStructureKhr, 1, rg),
                Binding(1, DescriptorType.UniformBuffer, 1, rg),
                Binding(2, DescriptorType.StorageBuffer, 1, rg | ShaderStageFlags.ClosestHitBitKhr),
                Binding(3, DescriptorType.StorageBuffer, 1, rg),
                Binding(4, DescriptorType.StorageBuffer, 1, rg),
                Binding(5, DescriptorType.StorageImage, 2, rg),
                Binding(6, DescriptorType.StorageImage, 2, rg),
                Binding(7, DescriptorType.StorageImage, 2, rg),
                Binding(8, DescriptorType.StorageImage, 2, rg),
                Binding(9, DescriptorType.StorageImage, 1, rg),
                Binding(10, DescriptorType.CombinedImageSampler, MaxTextures, rg),
            };

            DescriptorBindingFlags[] flags = new DescriptorBindingFlags[rt.Length];
            flags[rt.Length - 1] = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.VariableDescriptorCountBit;

            fixed (DescriptorSetLayoutBinding* pBindings = rt)
            fixed (DescriptorBindingFlags* pFlags = flags)
            {
                DescriptorSetLayoutBindingFlagsCreateInfo flagsInfo = new DescriptorSetLayoutBindingFlagsCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                    BindingCount = (uint)flags.Length,
                    PBindingFlags = pFlags,
                };

                DescriptorSetLayoutCreateInfo info = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)rt.Length,
                    PBindings = pBindings,
                    PNext = &flagsInfo,
                };

                DescriptorSetLayout layout;
                VulkanDevice.Check(_device.Api.CreateDescriptorSetLayout(_device.Device, &info, null, &layout), "vkCreateDescriptorSetLayout");
                _rtSetLayout = layout;
            }

            const ShaderStageFlags cs = ShaderStageFlags.ComputeBit;

            _denoiseSetLayout = SimpleLayout(
                Binding(0, DescriptorType.StorageImage, 2, cs),
                Binding(1, DescriptorType.StorageImage, 2, cs),
                Binding(2, DescriptorType.StorageImage, 2, cs),
                Binding(3, DescriptorType.StorageImage, 1, cs),
                Binding(4, DescriptorType.StorageImage, 1, cs),
                Binding(5, DescriptorType.StorageImage, 2, cs),
                Binding(6, DescriptorType.StorageImage, 1, cs),
                Binding(7, DescriptorType.StorageImage, 1, cs));

            _luminanceSetLayout = SimpleLayout(
                Binding(0, DescriptorType.StorageImage, 1, cs),
                Binding(1, DescriptorType.StorageBuffer, 1, cs));

            _tonemapSetLayout = SimpleLayout(
                Binding(0, DescriptorType.StorageImage, 1, cs),
                Binding(1, DescriptorType.StorageImage, 1, cs),
                Binding(2, DescriptorType.StorageBuffer, 1, cs));

            _rtPipelineLayout = PipelineLayoutFor(_rtSetLayout, 0);
            _denoisePipelineLayout = PipelineLayoutFor(_denoiseSetLayout, (uint)sizeof(DenoisePush));
            _luminancePipelineLayout = PipelineLayoutFor(_luminanceSetLayout, (uint)sizeof(ExposurePush));
            _tonemapPipelineLayout = PipelineLayoutFor(_tonemapSetLayout, 0);
        }

        private static DescriptorSetLayoutBinding Binding(uint index, DescriptorType type, uint count, ShaderStageFlags stages)
        {
            return new DescriptorSetLayoutBinding
            {
                Binding = index,
                DescriptorType = type,
                DescriptorCount = count,
                StageFlags = stages,
            };
        }

        private DescriptorSetLayout SimpleLayout(params DescriptorSetLayoutBinding[] bindings)
        {
            fixed (DescriptorSetLayoutBinding* p = bindings)
            {
                DescriptorSetLayoutCreateInfo info = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)bindings.Length,
                    PBindings = p,
                };

                DescriptorSetLayout layout;
                VulkanDevice.Check(_device.Api.CreateDescriptorSetLayout(_device.Device, &info, null, &layout), "vkCreateDescriptorSetLayout");
                return layout;
            }
        }

        private PipelineLayout PipelineLayoutFor(DescriptorSetLayout setLayout, uint pushBytes)
        {
            PushConstantRange push = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.ComputeBit,
                Offset = 0,
                Size = pushBytes,
            };

            PipelineLayoutCreateInfo info = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = pushBytes > 0 ? 1u : 0u,
                PPushConstantRanges = pushBytes > 0 ? &push : null,
            };

            PipelineLayout layout;
            VulkanDevice.Check(_device.Api.CreatePipelineLayout(_device.Device, &info, null, &layout), "vkCreatePipelineLayout");
            return layout;
        }

        // ==================== pipelines ====================

        private PipelineShaderStageCreateInfo Stage(ShaderCompiler compiler, string file, ShaderCompiler.Kind kind, ShaderStageFlags stage, byte* entry)
        {
            VulkanDevice.Stage("compiling " + file);
            ShaderModule module = _device.CreateShaderModule(compiler.Compile(file, kind));
            VulkanDevice.Stage("compiled " + file);
            _modules.Add(module);

            return new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = stage,
                Module = module,
                PName = entry,
            };
        }

        private void CreateRayTracingPipeline(ShaderCompiler compiler)
        {
            byte* entry = (byte*)SilkMarshal.StringToPtr("main");

            try
            {
                PipelineShaderStageCreateInfo[] stages =
                {
                    Stage(compiler, "raygen.rgen", ShaderCompiler.Kind.RayGeneration, ShaderStageFlags.RaygenBitKhr, entry),
                    Stage(compiler, "miss.rmiss", ShaderCompiler.Kind.Miss, ShaderStageFlags.MissBitKhr, entry),
                    Stage(compiler, "shadow.rmiss", ShaderCompiler.Kind.Miss, ShaderStageFlags.MissBitKhr, entry),
                    Stage(compiler, "closesthit.rchit", ShaderCompiler.Kind.ClosestHit, ShaderStageFlags.ClosestHitBitKhr, entry),
                };

                const uint unused = Silk.NET.Vulkan.Vk.ShaderUnusedKhr;

                RayTracingShaderGroupCreateInfoKHR[] groups =
                {
                    Group(RayTracingShaderGroupTypeKHR.GeneralKhr, general: 0, closestHit: unused),
                    Group(RayTracingShaderGroupTypeKHR.GeneralKhr, general: 1, closestHit: unused),
                    Group(RayTracingShaderGroupTypeKHR.GeneralKhr, general: 2, closestHit: unused),
                    Group(RayTracingShaderGroupTypeKHR.TrianglesHitGroupKhr, general: unused, closestHit: 3),
                };

                fixed (PipelineShaderStageCreateInfo* pStages = stages)
                fixed (RayTracingShaderGroupCreateInfoKHR* pGroups = groups)
                {
                    RayTracingPipelineCreateInfoKHR info = new RayTracingPipelineCreateInfoKHR
                    {
                        SType = StructureType.RayTracingPipelineCreateInfoKhr,
                        StageCount = (uint)stages.Length,
                        PStages = pStages,
                        GroupCount = (uint)groups.Length,
                        PGroups = pGroups,

                        // Raygen does every trace itself; no stage traces from another.
                        MaxPipelineRayRecursionDepth = 1,
                        Layout = _rtPipelineLayout,
                    };

                    Pipeline pipeline;
                    VulkanDevice.Check(_device.KhrRayTracingPipeline.CreateRayTracingPipelines(_device.Device,
                        default(DeferredOperationKHR), default(PipelineCache), 1, &info, null, &pipeline),
                        "vkCreateRayTracingPipelinesKHR");

                    _rtPipeline = pipeline;
                }
            }
            finally
            {
                SilkMarshal.Free((nint)entry);
            }
        }

        private static RayTracingShaderGroupCreateInfoKHR Group(RayTracingShaderGroupTypeKHR type, uint general, uint closestHit)
        {
            return new RayTracingShaderGroupCreateInfoKHR
            {
                SType = StructureType.RayTracingShaderGroupCreateInfoKhr,
                Type = type,
                GeneralShader = general,
                ClosestHitShader = closestHit,
                AnyHitShader = Silk.NET.Vulkan.Vk.ShaderUnusedKhr,
                IntersectionShader = Silk.NET.Vulkan.Vk.ShaderUnusedKhr,
            };
        }

        private void CreateComputePipelines(ShaderCompiler compiler)
        {
            byte* entry = (byte*)SilkMarshal.StringToPtr("main");

            try
            {
                _denoisePipeline = ComputePipeline(
                    Stage(compiler, "denoise.comp", ShaderCompiler.Kind.Compute, ShaderStageFlags.ComputeBit, entry),
                    _denoisePipelineLayout);

                _luminancePipeline = ComputePipeline(
                    Stage(compiler, "luminance.comp", ShaderCompiler.Kind.Compute, ShaderStageFlags.ComputeBit, entry),
                    _luminancePipelineLayout);

                _tonemapPipeline = ComputePipeline(
                    Stage(compiler, "tonemap.comp", ShaderCompiler.Kind.Compute, ShaderStageFlags.ComputeBit, entry),
                    _tonemapPipelineLayout);
            }
            finally
            {
                SilkMarshal.Free((nint)entry);
            }
        }

        private Pipeline ComputePipeline(PipelineShaderStageCreateInfo stage, PipelineLayout layout)
        {
            ComputePipelineCreateInfo info = new ComputePipelineCreateInfo
            {
                SType = StructureType.ComputePipelineCreateInfo,
                Stage = stage,
                Layout = layout,
            };

            Pipeline pipeline;
            VulkanDevice.Check(_device.Api.CreateComputePipelines(_device.Device, default(PipelineCache), 1, &info, null, &pipeline),
                "vkCreateComputePipelines");
            return pipeline;
        }

        // ==================== shader binding table ====================

        /// <summary>
        /// The table the trace call reads shader handles from: raygen, the two miss shaders
        /// (camera rays, shadow rays), and the hit group, each region at the alignment the
        /// device asks for.
        /// </summary>
        private void CreateShaderBindingTable()
        {
            PhysicalDeviceRayTracingPipelinePropertiesKHR props = _device.RayTracingProperties;

            uint handleSize = props.ShaderGroupHandleSize;
            uint handleStride = AlignUp(handleSize, props.ShaderGroupHandleAlignment);
            uint baseAlign = props.ShaderGroupBaseAlignment;

            const uint groupCount = 4;

            uint raygenOffset = 0;
            uint missOffset = AlignUp(raygenOffset + handleStride, baseAlign);
            uint hitOffset = AlignUp(missOffset + 2 * handleStride, baseAlign);
            uint total = AlignUp(hitOffset + handleStride, baseAlign);

            byte[] handles = new byte[groupCount * handleSize];

            fixed (byte* p = handles)
            {
                VulkanDevice.Check(_device.KhrRayTracingPipeline.GetRayTracingShaderGroupHandles(_device.Device, _rtPipeline,
                    0, groupCount, (nuint)handles.Length, p), "vkGetRayTracingShaderGroupHandlesKHR");
            }

            byte[] table = new byte[total];
            Array.Copy(handles, 0 * handleSize, table, raygenOffset, handleSize);
            Array.Copy(handles, 1 * handleSize, table, missOffset, handleSize);
            Array.Copy(handles, 2 * handleSize, table, missOffset + handleStride, handleSize);
            Array.Copy(handles, 3 * handleSize, table, hitOffset, handleSize);

            _sbt = _device.CreateBuffer(total,
                BufferUsageFlags.ShaderBindingTableBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, deviceAddress: true);

            _sbt.Write(table);

            ulong address = _sbt.DeviceAddress;

            _raygenRegion = new StridedDeviceAddressRegionKHR { DeviceAddress = address + raygenOffset, Stride = handleStride, Size = handleStride };
            _missRegion = new StridedDeviceAddressRegionKHR { DeviceAddress = address + missOffset, Stride = handleStride, Size = 2 * handleStride };
            _hitRegion = new StridedDeviceAddressRegionKHR { DeviceAddress = address + hitOffset, Stride = handleStride, Size = handleStride };
            _callableRegion = new StridedDeviceAddressRegionKHR();
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }

        // ==================== descriptors ====================

        private void CreatePool()
        {
            DescriptorPoolSize[] sizes =
            {
                new DescriptorPoolSize(DescriptorType.AccelerationStructureKhr, 1),
                new DescriptorPoolSize(DescriptorType.StorageImage, 32),
                new DescriptorPoolSize(DescriptorType.UniformBuffer, 1),
                new DescriptorPoolSize(DescriptorType.StorageBuffer, 6),
                new DescriptorPoolSize(DescriptorType.CombinedImageSampler, MaxTextures),
            };

            fixed (DescriptorPoolSize* p = sizes)
            {
                DescriptorPoolCreateInfo info = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    MaxSets = 4,
                    PoolSizeCount = (uint)sizes.Length,
                    PPoolSizes = p,
                    Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
                };

                DescriptorPool pool;
                VulkanDevice.Check(_device.Api.CreateDescriptorPool(_device.Device, &info, null, &pool), "vkCreateDescriptorPool");
                _pool = pool;
            }

            uint textureCount = MaxTextures;

            DescriptorSetVariableDescriptorCountAllocateInfo variable = new DescriptorSetVariableDescriptorCountAllocateInfo
            {
                SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
                DescriptorSetCount = 1,
                PDescriptorCounts = &textureCount,
            };

            _rtSet = Allocate(_rtSetLayout, &variable);
            _denoiseSet = Allocate(_denoiseSetLayout, null);
            _luminanceSet = Allocate(_luminanceSetLayout, null);
            _tonemapSet = Allocate(_tonemapSetLayout, null);
        }

        private DescriptorSet Allocate(DescriptorSetLayout layout, void* next)
        {
            DescriptorSetAllocateInfo info = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _pool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
                PNext = next,
            };

            DescriptorSet set;
            VulkanDevice.Check(_device.Api.AllocateDescriptorSets(_device.Device, &info, &set), "vkAllocateDescriptorSets");
            return set;
        }

        /// <summary>Point every set at the current buffers, images and textures.</summary>
        private void WriteDescriptors()
        {
            List<WriteDescriptorSet> writes = new List<WriteDescriptorSet>();
            List<IntPtr> pinned = new List<IntPtr>();

            try
            {
                // ---- the ray tracing set ----

                if (_scene.Tlas.Handle != 0)
                {
                    AccelerationStructureKHR* tlas = (AccelerationStructureKHR*)Pin(pinned, sizeof(AccelerationStructureKHR));
                    *tlas = _scene.Tlas;

                    WriteDescriptorSetAccelerationStructureKHR* asInfo =
                        (WriteDescriptorSetAccelerationStructureKHR*)Pin(pinned, sizeof(WriteDescriptorSetAccelerationStructureKHR));

                    *asInfo = new WriteDescriptorSetAccelerationStructureKHR
                    {
                        SType = StructureType.WriteDescriptorSetAccelerationStructureKhr,
                        AccelerationStructureCount = 1,
                        PAccelerationStructures = tlas,
                    };

                    writes.Add(new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _rtSet,
                        DstBinding = 0,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.AccelerationStructureKhr,
                        PNext = asInfo,
                    });
                }

                writes.Add(BufferWrite(pinned, _rtSet, 1, _frameBuffer, DescriptorType.UniformBuffer));
                writes.Add(BufferWrite(pinned, _rtSet, 2, _scene.GeometryBuffer, DescriptorType.StorageBuffer));
                writes.Add(BufferWrite(pinned, _rtSet, 3, _scene.MaterialBuffer, DescriptorType.StorageBuffer));
                writes.Add(BufferWrite(pinned, _rtSet, 4, _scene.LightBuffer, DescriptorType.StorageBuffer));
                writes.Add(ImageWrite(pinned, _rtSet, 5, _position));
                writes.Add(ImageWrite(pinned, _rtSet, 6, _normal));
                writes.Add(ImageWrite(pinned, _rtSet, 7, _direct));
                writes.Add(ImageWrite(pinned, _rtSet, 8, _indirect));
                writes.Add(ImageWrite(pinned, _rtSet, 9, _albedo));

                int textureCount = _scene.Textures.Count;

                if (textureCount > 0)
                {
                    DescriptorImageInfo* images = (DescriptorImageInfo*)Pin(pinned, sizeof(DescriptorImageInfo) * textureCount);

                    for (int i = 0; i < textureCount; i++)
                    {
                        images[i] = new DescriptorImageInfo
                        {
                            Sampler = _scene.Sampler,
                            ImageView = _scene.Textures[i].View,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        };
                    }

                    writes.Add(new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _rtSet,
                        DstBinding = 10,
                        DescriptorCount = (uint)textureCount,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        PImageInfo = images,
                    });
                }

                // ---- the denoiser ----

                writes.Add(ImageWrite(pinned, _denoiseSet, 0, _position));
                writes.Add(ImageWrite(pinned, _denoiseSet, 1, _normal));
                writes.Add(ImageWrite(pinned, _denoiseSet, 2, _indirect));
                writes.Add(ImageWrite(pinned, _denoiseSet, 3, _pingA));
                writes.Add(ImageWrite(pinned, _denoiseSet, 4, _pingB));
                writes.Add(ImageWrite(pinned, _denoiseSet, 5, _direct));
                writes.Add(ImageWrite(pinned, _denoiseSet, 6, _albedo));
                writes.Add(ImageWrite(pinned, _denoiseSet, 7, _hdr));

                // ---- the post-process sets ----

                writes.Add(ImageWrite(pinned, _luminanceSet, 0, _hdr));
                writes.Add(BufferWrite(pinned, _luminanceSet, 1, _exposureBuffer, DescriptorType.StorageBuffer));

                writes.Add(ImageWrite(pinned, _tonemapSet, 0, _hdr));
                writes.Add(ImageWrite(pinned, _tonemapSet, 1, _ldr));
                writes.Add(BufferWrite(pinned, _tonemapSet, 2, _exposureBuffer, DescriptorType.StorageBuffer));

                WriteDescriptorSet[] array = writes.ToArray();

                fixed (WriteDescriptorSet* p = array)
                    _device.Api.UpdateDescriptorSets(_device.Device, (uint)array.Length, p, 0, null);
            }
            finally
            {
                foreach (IntPtr block in pinned)
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(block);
            }

            _boundSceneVersion = _scene.Version;
            _imagesRebound = false;
        }

        private static void* Pin(List<IntPtr> pinned, int bytes)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes);
            pinned.Add(block);
            return (void*)block;
        }

        private static WriteDescriptorSet BufferWrite(List<IntPtr> pinned, DescriptorSet set, uint binding, Buffer buffer, DescriptorType type)
        {
            DescriptorBufferInfo* info = (DescriptorBufferInfo*)Pin(pinned, sizeof(DescriptorBufferInfo));
            *info = new DescriptorBufferInfo { Buffer = buffer.Handle, Offset = 0, Range = Silk.NET.Vulkan.Vk.WholeSize };

            return new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = binding,
                DescriptorCount = 1,
                DescriptorType = type,
                PBufferInfo = info,
            };
        }

        private static WriteDescriptorSet ImageWrite(List<IntPtr> pinned, DescriptorSet set, uint binding, Image image)
        {
            return ImageWrite(pinned, set, binding, new[] { image });
        }

        /// <summary>A storage image binding; an array of them when the shader declares one.</summary>
        private static WriteDescriptorSet ImageWrite(List<IntPtr> pinned, DescriptorSet set, uint binding, Image[] images)
        {
            DescriptorImageInfo* info = (DescriptorImageInfo*)Pin(pinned, sizeof(DescriptorImageInfo) * images.Length);

            for (int i = 0; i < images.Length; i++)
                info[i] = new DescriptorImageInfo { ImageView = images[i].View, ImageLayout = ImageLayout.General };

            return new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = binding,
                DescriptorCount = (uint)images.Length,
                DescriptorType = DescriptorType.StorageImage,
                PImageInfo = info,
            };
        }

        // ==================== per-frame objects ====================

        private void CreateFrameObjects()
        {
            _frameBuffer = _device.CreateBuffer((ulong)sizeof(FrameData), BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _exposureBuffer = _device.CreateBuffer((ulong)sizeof(ExposureState), BufferUsageFlags.StorageBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _exposureBuffer.Write(new[] { new ExposureState { AdaptedLogLuminance = 0f, Exposure = 1f } });

            CommandBufferAllocateInfo allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = _device.CommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };

            CommandBuffer cmd;
            VulkanDevice.Check(_device.Api.AllocateCommandBuffers(_device.Device, &allocate, &cmd), "vkAllocateCommandBuffers");
            _cmd = cmd;

            SemaphoreCreateInfo semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
            Semaphore acquired, rendered;
            VulkanDevice.Check(_device.Api.CreateSemaphore(_device.Device, &semaphoreInfo, null, &acquired), "vkCreateSemaphore");
            VulkanDevice.Check(_device.Api.CreateSemaphore(_device.Device, &semaphoreInfo, null, &rendered), "vkCreateSemaphore");
            _acquired = acquired;
            _rendered = rendered;

            FenceCreateInfo fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            Fence fence;
            VulkanDevice.Check(_device.Api.CreateFence(_device.Device, &fenceInfo, null, &fence), "vkCreateFence");
            _fence = fence;
        }

        /// <summary>Make the images for a new view size. Starts the picture over.</summary>
        public void Resize(uint width, uint height)
        {
            if (width == Width && height == Height && _hdr != null)
                return;

            WaitForFrame();
            DisposeImages();

            Width = width;
            Height = height;

            if (width == 0 || height == 0)
                return;

            const ImageUsageFlags storage = ImageUsageFlags.StorageBit | ImageUsageFlags.TransferDstBit;

            for (int i = 0; i < 2; i++)
            {
                _position[i] = _device.CreateImage(width, height, Format.R32G32B32A32Sfloat, storage);
                _normal[i] = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
                _direct[i] = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
                _indirect[i] = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
            }

            _albedo = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
            _pingA = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
            _pingB = _device.CreateImage(width, height, Format.R16G16B16A16Sfloat, storage);
            _hdr = _device.CreateImage(width, height, Format.R32G32B32A32Sfloat, storage);
            _ldr = _device.CreateImage(width, height, Format.R8G8B8A8Unorm, ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit);

            CommandBuffer cmd = _device.BeginOneShot();

            foreach (Image image in AllImages())
                _device.Transition(cmd, image, ImageLayout.General);

            _device.EndOneShot(cmd);

            _imagesRebound = true;
            Restart();
        }

        private IEnumerable<Image> AllImages()
        {
            for (int i = 0; i < 2; i++)
            {
                if (_position[i] != null) yield return _position[i];
                if (_normal[i] != null) yield return _normal[i];
                if (_direct[i] != null) yield return _direct[i];
                if (_indirect[i] != null) yield return _indirect[i];
            }

            if (_albedo != null) yield return _albedo;
            if (_pingA != null) yield return _pingA;
            if (_pingB != null) yield return _pingB;
            if (_hdr != null) yield return _hdr;
            if (_ldr != null) yield return _ldr;
        }

        private void DisposeImages()
        {
            foreach (Image image in AllImages())
                image.Dispose();

            for (int i = 0; i < 2; i++)
                _position[i] = _normal[i] = _direct[i] = _indirect[i] = null;

            _albedo = _pingA = _pingB = _hdr = _ldr = null;
        }

        private void WaitForFrame()
        {
            if (!_fenceInUse)
                return;

            Fence fence = _fence;
            _device.Api.WaitForFences(_device.Device, 1, &fence, true, ulong.MaxValue);
            _fenceInUse = false;
        }

        /// <summary>Start the picture over on the next frame: something in the scene changed, so no pixel's history holds.</summary>
        public void Restart()
        {
            Samples = 0;
            _resetPending = true;
            _exposureReset = true;
        }

        // ==================== a frame ====================

        /// <summary>
        /// Trace, denoise, expose, tone map and present one frame. Returns false when the
        /// swapchain has to be rebuilt first (the window changed size under it).
        /// </summary>
        public bool Render(VulkanSwapchain swapchain, Matrix4x4 viewProj, Matrix4x4 invViewProj, Vector3 cameraPosition, double deltaSeconds)
        {
            if (_hdr == null || !swapchain.IsUsable)
                return true;

            Trace("wait");
            WaitForFrame();

            // A moved camera keeps the picture through reprojection but caps how much history
            // a pixel may carry; a still one lets it grow, which is what converges.
            bool moved = invViewProj != _lastInvViewProj;

            if (moved)
            {
                _lastInvViewProj = invViewProj;
                Samples = 0;
            }

            if (_scene.Version != _boundSceneVersion || _imagesRebound)
                WriteDescriptors();

            Trace("acquire");
            uint imageIndex;
            Result acquire = swapchain.AcquireNext(_acquired, out imageIndex);

            if (acquire == Result.ErrorOutOfDateKhr || acquire == Result.SuboptimalKhr)
                return false;

            VulkanDevice.Check(acquire, "vkAcquireNextImageKHR");

            int spp = Math.Max(1, SamplesPerFrame);

            FrameData frame = new FrameData
            {
                InvViewProj = invViewProj,
                PrevViewProj = _lastViewProj,
                CameraPosition = new Vector4(cameraPosition, 0f),
                Sky = new Vector4(_scene.SkyRadiance, 0f),
                FrameIndex = _frameIndex,
                LightCount = (uint)_scene.LightCount,
                Samples = Samples,
                Flags = _resetPending ? FrameData.FlagReset : 0u,
                Units = new Vector4(UnitsPerMetre, Bounces, Math.Max(1, LightsPerSample), spp),
                History = moved
                    ? new Vector4(MovingHistoryIndirect, MovingHistoryDirect, 0f, 0f)
                    : new Vector4(65536f, 65536f, 0f, 0f),
            };

            _frameBuffer.Write(new[] { frame });
            _lastViewProj = viewProj;

            Trace("record");
            Record(swapchain.Images[imageIndex], deltaSeconds);
            Trace("submit");
            Submit();
            Trace("present");

            Result present = swapchain.Present(_rendered, imageIndex);
            Trace("presented " + present);

            _frameIndex++;
            Samples = Math.Min(Samples + (uint)spp, 1u << 20);
            _resetPending = false;
            _exposureReset = false;

            if (present == Result.ErrorOutOfDateKhr || present == Result.SuboptimalKhr)
                return false;

            VulkanDevice.Check(present, "vkQueuePresentKHR");
            return true;
        }

        private void Trace(string what)
        {
            if (TraceFrames)
                VulkanDevice.Stage("frame " + _frameIndex + ": " + what);
        }

        /// <summary>
        /// How many smoothing passes the indirect light gets: four while the picture has just
        /// changed, none once it has enough samples to stand on its own.
        /// </summary>
        private int DenoisePasses()
        {
            if (Samples < 8) return 4;
            if (Samples < 32) return 3;
            if (Samples < 128) return 2;
            if (Samples < 512) return 1;
            return 0;
        }

        private void Record(Silk.NET.Vulkan.Image target, double deltaSeconds)
        {
            Silk.NET.Vulkan.Vk vk = _device.Api;

            vk.ResetCommandBuffer(_cmd, 0);

            CommandBufferBeginInfo begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };

            VulkanDevice.Check(vk.BeginCommandBuffer(_cmd, &begin), "vkBeginCommandBuffer");

            uint parity = _frameIndex & 1u;
            uint groupsX = (Width + 15) / 16;
            uint groupsY = (Height + 15) / 16;

            if (_scene.Tlas.Handle != 0)
            {
                // ---- trace ----

                {
                    DescriptorSet set = _rtSet;
                    vk.CmdBindPipeline(_cmd, PipelineBindPoint.RayTracingKhr, _rtPipeline);
                    vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.RayTracingKhr, _rtPipelineLayout, 0, 1, &set, 0, null);

                    StridedDeviceAddressRegionKHR raygen = _raygenRegion;
                    StridedDeviceAddressRegionKHR miss = _missRegion;
                    StridedDeviceAddressRegionKHR hit = _hitRegion;
                    StridedDeviceAddressRegionKHR callable = _callableRegion;

                    _device.KhrRayTracingPipeline.CmdTraceRays(_cmd, &raygen, &miss, &hit, &callable, Width, Height, 1);
                }

                _device.FullBarrier(_cmd);

                // ---- denoise and compose ----

                {
                    DescriptorSet set = _denoiseSet;
                    vk.CmdBindPipeline(_cmd, PipelineBindPoint.Compute, _denoisePipeline);
                    vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.Compute, _denoisePipelineLayout, 0, 1, &set, 0, null);

                    uint source = DenoisePush.SourceHistory;
                    int passes = DenoisePasses();

                    for (int pass = 0; pass < passes; pass++)
                    {
                        uint dest = source == DenoisePush.SourceA ? DenoisePush.SourceB : DenoisePush.SourceA;

                        DenoisePush push = new DenoisePush
                        {
                            Parity = parity,
                            Source = source,
                            Dest = dest,
                            Mode = DenoisePush.ModeFilter,
                            Step = 1 << pass,
                            HistoryFull = HistoryFull,
                        };

                        vk.CmdPushConstants(_cmd, _denoisePipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(DenoisePush), &push);
                        vk.CmdDispatch(_cmd, groupsX, groupsY, 1);
                        _device.FullBarrier(_cmd);

                        source = dest;
                    }

                    DenoisePush compose = new DenoisePush
                    {
                        Parity = parity,
                        Source = source,
                        Dest = 0,
                        Mode = DenoisePush.ModeCompose,
                        Step = 1,
                        HistoryFull = HistoryFull,
                    };

                    vk.CmdPushConstants(_cmd, _denoisePipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(DenoisePush), &compose);
                    vk.CmdDispatch(_cmd, groupsX, groupsY, 1);
                }
            }
            else
            {
                // Nothing to trace: the picture is the sky.
                ClearColorValue sky = new ClearColorValue(_scene.SkyRadiance.X, _scene.SkyRadiance.Y, _scene.SkyRadiance.Z, 1f);
                ImageSubresourceRange range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
                vk.CmdClearColorImage(_cmd, _hdr.Handle, ImageLayout.General, &sky, 1, &range);
            }

            _device.FullBarrier(_cmd);

            // ---- exposure ----

            {
                DescriptorSet set = _luminanceSet;
                vk.CmdBindPipeline(_cmd, PipelineBindPoint.Compute, _luminancePipeline);
                vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.Compute, _luminancePipelineLayout, 0, 1, &set, 0, null);

                ExposurePush push = new ExposurePush
                {
                    DeltaSeconds = (float)Math.Min(deltaSeconds, 0.25),
                    ExposureBias = ExposureBias,

                    // Unreal's range with ExtendDefaultLuminanceRange on.
                    MinEV100 = -10f,
                    MaxEV100 = 20f,
                    Reset = _exposureReset ? 1u : 0u,
                };

                vk.CmdPushConstants(_cmd, _luminancePipelineLayout, ShaderStageFlags.ComputeBit, 0, (uint)sizeof(ExposurePush), &push);
                vk.CmdDispatch(_cmd, 1, 1, 1);
            }

            _device.FullBarrier(_cmd);

            // ---- tone map ----

            {
                DescriptorSet set = _tonemapSet;
                vk.CmdBindPipeline(_cmd, PipelineBindPoint.Compute, _tonemapPipeline);
                vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.Compute, _tonemapPipelineLayout, 0, 1, &set, 0, null);
                vk.CmdDispatch(_cmd, groupsX, groupsY, 1);
            }

            _device.FullBarrier(_cmd);

            // ---- to the window ----

            _device.Transition(_cmd, _ldr, ImageLayout.TransferSrcOptimal);
            _device.Transition(_cmd, target, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

            ImageBlit blit = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            };

            blit.SrcOffsets[1] = new Offset3D((int)Width, (int)Height, 1);
            blit.DstOffsets[1] = new Offset3D((int)Width, (int)Height, 1);

            vk.CmdBlitImage(_cmd, _ldr.Handle, ImageLayout.TransferSrcOptimal, target, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Nearest);

            _device.Transition(_cmd, target, ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr);
            _device.Transition(_cmd, _ldr, ImageLayout.General);

            VulkanDevice.Check(vk.EndCommandBuffer(_cmd), "vkEndCommandBuffer");
        }

        private void Submit()
        {
            CommandBuffer cmd = _cmd;
            Semaphore acquired = _acquired;
            Semaphore rendered = _rendered;
            PipelineStageFlags waitStage = PipelineStageFlags.AllCommandsBit;

            SubmitInfo submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &acquired,
                PWaitDstStageMask = &waitStage,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &rendered,
            };

            Fence fence = _fence;
            _device.Api.ResetFences(_device.Device, 1, &fence);

            lock (_device.QueueGate)
                VulkanDevice.Check(_device.Api.QueueSubmit(_device.Queue, 1, &submit, fence), "vkQueueSubmit");

            _fenceInUse = true;
        }

        // ==================== capture ====================

        /// <summary>The last tone-mapped frame, read back. For tests and for saving views.</summary>
        public Bitmap Capture()
        {
            if (_ldr == null)
                return null;

            WaitForFrame();
            _device.WaitIdle();

            ulong bytes = (ulong)(Width * Height * 4);

            Buffer readback = _device.CreateBuffer(bytes, BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            CommandBuffer cmd = _device.BeginOneShot();
            _device.Transition(cmd, _ldr, ImageLayout.TransferSrcOptimal);

            BufferImageCopy region = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D(Width, Height, 1),
            };

            _device.Api.CmdCopyImageToBuffer(cmd, _ldr.Handle, ImageLayout.TransferSrcOptimal, readback.Handle, 1, &region);
            _device.Transition(cmd, _ldr, ImageLayout.General);
            _device.EndOneShot(cmd);

            byte[] rgba = readback.Read<byte>((int)bytes);
            readback.Dispose();

            Bitmap bitmap = new Bitmap((int)Width, (int)Height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                byte* dst = (byte*)data.Scan0;

                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        int s = (y * (int)Width + x) * 4;
                        int d = y * data.Stride + x * 4;

                        dst[d + 0] = rgba[s + 2];
                        dst[d + 1] = rgba[s + 1];
                        dst[d + 2] = rgba[s + 0];
                        dst[d + 3] = 255;
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        // ==================== teardown ====================

        public void Dispose()
        {
            WaitForFrame();
            _device.WaitIdle();

            Silk.NET.Vulkan.Vk vk = _device.Api;

            DisposeImages();
            _frameBuffer?.Dispose();
            _exposureBuffer?.Dispose();
            _sbt?.Dispose();

            if (_fence.Handle != 0) vk.DestroyFence(_device.Device, _fence, null);
            if (_acquired.Handle != 0) vk.DestroySemaphore(_device.Device, _acquired, null);
            if (_rendered.Handle != 0) vk.DestroySemaphore(_device.Device, _rendered, null);

            if (_cmd.Handle != 0)
            {
                CommandBuffer cmd = _cmd;
                vk.FreeCommandBuffers(_device.Device, _device.CommandPool, 1, &cmd);
            }

            if (_pool.Handle != 0) vk.DestroyDescriptorPool(_device.Device, _pool, null);

            if (_rtPipeline.Handle != 0) vk.DestroyPipeline(_device.Device, _rtPipeline, null);
            if (_denoisePipeline.Handle != 0) vk.DestroyPipeline(_device.Device, _denoisePipeline, null);
            if (_luminancePipeline.Handle != 0) vk.DestroyPipeline(_device.Device, _luminancePipeline, null);
            if (_tonemapPipeline.Handle != 0) vk.DestroyPipeline(_device.Device, _tonemapPipeline, null);

            if (_rtPipelineLayout.Handle != 0) vk.DestroyPipelineLayout(_device.Device, _rtPipelineLayout, null);
            if (_denoisePipelineLayout.Handle != 0) vk.DestroyPipelineLayout(_device.Device, _denoisePipelineLayout, null);
            if (_luminancePipelineLayout.Handle != 0) vk.DestroyPipelineLayout(_device.Device, _luminancePipelineLayout, null);
            if (_tonemapPipelineLayout.Handle != 0) vk.DestroyPipelineLayout(_device.Device, _tonemapPipelineLayout, null);

            if (_rtSetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(_device.Device, _rtSetLayout, null);
            if (_denoiseSetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(_device.Device, _denoiseSetLayout, null);
            if (_luminanceSetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(_device.Device, _luminanceSetLayout, null);
            if (_tonemapSetLayout.Handle != 0) vk.DestroyDescriptorSetLayout(_device.Device, _tonemapSetLayout, null);

            foreach (ShaderModule module in _modules)
                vk.DestroyShaderModule(_device.Device, module, null);

            _modules.Clear();
        }
    }
}
