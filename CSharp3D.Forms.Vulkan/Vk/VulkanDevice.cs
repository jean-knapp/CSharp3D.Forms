using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace CSharp3D.Forms.Vulkan.Vk
{
    /// <summary>
    /// The Vulkan instance, the one physical device that can ray trace, and the logical device
    /// on it - plus the small set of helpers everything else is built from: memory, buffers,
    /// images and one-shot command buffers.
    ///
    /// One of these per process. The GL renderer keeps one context per view; a Vulkan device
    /// is a heavier thing to make and there is no reason to have two, so views share it and
    /// own only their swapchain.
    ///
    /// Everything that can fail at setup fails with <see cref="VulkanUnavailableException"/>
    /// carrying a reason a status bar can show: the machine may simply not have a ray tracing
    /// GPU, and that is a normal outcome the host answers by keeping its GL view.
    /// </summary>
    public sealed unsafe class VulkanDevice : IDisposable
    {
        private static VulkanDevice _shared;
        private static readonly object _sharedGate = new object();

        /// <summary>The process-wide device, made on first use.</summary>
        public static VulkanDevice Shared
        {
            get
            {
                lock (_sharedGate)
                {
                    // A device the driver has lost never comes back; a fresh one does.
                    if (_shared != null && _shared.IsLost)
                    {
                        try { _shared.Dispose(); } catch (Exception) { }
                        _shared = null;
                    }

                    if (_shared == null)
                        _shared = new VulkanDevice();

                    return _shared;
                }
            }
        }

        /// <summary>
        /// Set once any call reports VK_ERROR_DEVICE_LOST. Nothing on the device works after
        /// that; whoever holds it releases what it can and asks <see cref="Shared"/> again.
        /// </summary>
        public bool IsLost { get; private set; }

        /// <summary>Forget the shared device without touching it: for a host that knows it is gone.</summary>
        public void MarkLost()
        {
            IsLost = true;
        }

        public Silk.NET.Vulkan.Vk Api { get; }
        public Instance Instance { get; private set; }
        public PhysicalDevice PhysicalDevice { get; private set; }
        public Device Device { get; private set; }
        public Queue Queue { get; private set; }
        public uint QueueFamily { get; private set; }
        public CommandPool CommandPool { get; private set; }

        public KhrSurface KhrSurface { get; private set; }
        public KhrWin32Surface KhrWin32Surface { get; private set; }
        public KhrSwapchain KhrSwapchain { get; private set; }
        public KhrAccelerationStructure KhrAccelerationStructure { get; private set; }
        public KhrRayTracingPipeline KhrRayTracingPipeline { get; private set; }

        public PhysicalDeviceRayTracingPipelinePropertiesKHR RayTracingProperties { get; private set; }
        public PhysicalDeviceMemoryProperties MemoryProperties { get; private set; }
        public string DeviceName { get; private set; }

        /// <summary>
        /// Serialises every submit to the one queue. The UI thread renders, but a scene sync
        /// may build acceleration structures from a helper; the queue itself is not thread safe.
        /// </summary>
        public readonly object QueueGate = new object();

        private VulkanDevice()
        {
            Api = Silk.NET.Vulkan.Vk.GetApi();
            Stage("vulkan loaded");

            CreateInstance();
            Stage("instance created");
            PickPhysicalDevice();
            Stage("device picked: " + DeviceName);
            CreateLogicalDevice();
            Stage("logical device created");
        }

        /// <summary>A line of set-up progress, for whoever is listening (a probe writes a file).</summary>
        public static void Stage(string what)
        {
            System.Diagnostics.Trace.WriteLine("[vk] " + what);
        }

        // ==================== setup ====================

        private void CreateInstance()
        {
            string[] extensions =
            {
                KhrSurface.ExtensionName,
                KhrWin32Surface.ExtensionName,
                "VK_KHR_get_physical_device_properties2",
            };

            byte* appName = (byte*)SilkMarshal.StringToPtr("XMT Level Editor");
            byte* engineName = (byte*)SilkMarshal.StringToPtr("CSharp3D.Forms.Vulkan");
            byte** extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);

            try
            {
                ApplicationInfo app = new ApplicationInfo
                {
                    SType = StructureType.ApplicationInfo,
                    PApplicationName = appName,
                    ApplicationVersion = Silk.NET.Vulkan.Vk.MakeVersion(1, 0, 0),
                    PEngineName = engineName,
                    EngineVersion = Silk.NET.Vulkan.Vk.MakeVersion(1, 0, 0),
                    ApiVersion = Silk.NET.Vulkan.Vk.Version12,
                };

                InstanceCreateInfo info = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &app,
                    EnabledExtensionCount = (uint)extensions.Length,
                    PpEnabledExtensionNames = extensionNames,
                };

                Instance instance;
                Result result = Api.CreateInstance(&info, null, &instance);

                if (result != Result.Success)
                    throw new VulkanUnavailableException("Vulkan is not available on this machine (" + result + ").");

                Instance = instance;
            }
            finally
            {
                SilkMarshal.Free((nint)appName);
                SilkMarshal.Free((nint)engineName);
                SilkMarshal.Free((nint)extensionNames);
            }

            KhrSurface surface;
            KhrWin32Surface win32;

            if (!Api.TryGetInstanceExtension(Instance, out surface) || !Api.TryGetInstanceExtension(Instance, out win32))
                throw new VulkanUnavailableException("The Vulkan driver cannot present to a window.");

            KhrSurface = surface;
            KhrWin32Surface = win32;
        }

        private static readonly string[] RequiredDeviceExtensions =
        {
            KhrSwapchain.ExtensionName,
            KhrAccelerationStructure.ExtensionName,
            KhrRayTracingPipeline.ExtensionName,
            KhrDeferredHostOperations.ExtensionName,
        };

        /// <summary>
        /// The first discrete GPU that has every extension the ray tracer needs; failing that
        /// any GPU that does. A machine with an RTX card and an integrated GPU has to land on
        /// the RTX card.
        /// </summary>
        private void PickPhysicalDevice()
        {
            uint count = 0;
            Api.EnumeratePhysicalDevices(Instance, &count, null);

            if (count == 0)
                throw new VulkanUnavailableException("No Vulkan device was found.");

            PhysicalDevice[] devices = new PhysicalDevice[count];

            fixed (PhysicalDevice* p = devices)
                Api.EnumeratePhysicalDevices(Instance, &count, p);

            PhysicalDevice best = default;
            int bestScore = -1;
            string bestName = null;
            List<string> rejected = new List<string>();

            foreach (PhysicalDevice candidate in devices)
            {
                PhysicalDeviceProperties props;
                Api.GetPhysicalDeviceProperties(candidate, &props);
                string name = SilkMarshal.PtrToString((nint)props.DeviceName);

                string missing = MissingExtension(candidate);

                if (missing != null)
                {
                    rejected.Add(name + " (no " + missing + ")");
                    continue;
                }

                if (FindQueueFamily(candidate) < 0)
                {
                    rejected.Add(name + " (no graphics queue that can present)");
                    continue;
                }

                int score = props.DeviceType == PhysicalDeviceType.DiscreteGpu ? 2 : 1;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    bestName = name;
                }
            }

            if (bestScore < 0)
                throw new VulkanUnavailableException("No GPU here can ray trace: " + string.Join(", ", rejected) + ".");

            PhysicalDevice = best;
            DeviceName = bestName;
            QueueFamily = (uint)FindQueueFamily(best);

            PhysicalDeviceMemoryProperties memory;
            Api.GetPhysicalDeviceMemoryProperties(best, &memory);
            MemoryProperties = memory;

            // The shader-group sizes the shader binding table is laid out with.
            PhysicalDeviceRayTracingPipelinePropertiesKHR rt = new PhysicalDeviceRayTracingPipelinePropertiesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelinePropertiesKhr,
            };

            PhysicalDeviceProperties2 props2 = new PhysicalDeviceProperties2
            {
                SType = StructureType.PhysicalDeviceProperties2,
                PNext = &rt,
            };

            Api.GetPhysicalDeviceProperties2(best, &props2);
            RayTracingProperties = rt;
        }

        private string MissingExtension(PhysicalDevice device)
        {
            uint count = 0;
            Api.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, null);

            ExtensionProperties[] available = new ExtensionProperties[count];
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

            // The name is an inline byte array in each entry, which C# only lets at through
            // a pointer.
            fixed (ExtensionProperties* p = available)
            {
                Api.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, p);

                for (int i = 0; i < count; i++)
                    names.Add(SilkMarshal.PtrToString((nint)p[i].ExtensionName));
            }

            foreach (string required in RequiredDeviceExtensions)
            {
                if (!names.Contains(required))
                    return required;
            }

            return null;
        }

        private int FindQueueFamily(PhysicalDevice device)
        {
            uint count = 0;
            Api.GetPhysicalDeviceQueueFamilyProperties(device, &count, null);

            QueueFamilyProperties[] families = new QueueFamilyProperties[count];

            fixed (QueueFamilyProperties* p = families)
                Api.GetPhysicalDeviceQueueFamilyProperties(device, &count, p);

            for (int i = 0; i < count; i++)
            {
                bool graphics = (families[i].QueueFlags & QueueFlags.GraphicsBit) != 0;
                bool compute = (families[i].QueueFlags & QueueFlags.ComputeBit) != 0;

                // Presentation is asked of the family rather than a surface: every window this
                // renders to is a Win32 one on this same device.
                bool present = KhrWin32Surface.GetPhysicalDeviceWin32PresentationSupport(device, (uint)i);

                if (graphics && compute && present)
                    return i;
            }

            return -1;
        }

        private void CreateLogicalDevice()
        {
            float priority = 1f;

            DeviceQueueCreateInfo queueInfo = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = QueueFamily,
                QueueCount = 1,
                PQueuePriorities = &priority,
            };

            // The feature chain: ray tracing pipelines and acceleration structures, buffer
            // device addresses (the shaders read vertex data through pointers), and the
            // descriptor indexing that makes a single array of every texture possible.
            PhysicalDeviceRayTracingPipelineFeaturesKHR rtFeatures = new PhysicalDeviceRayTracingPipelineFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceRayTracingPipelineFeaturesKhr,
                RayTracingPipeline = true,
            };

            PhysicalDeviceAccelerationStructureFeaturesKHR asFeatures = new PhysicalDeviceAccelerationStructureFeaturesKHR
            {
                SType = StructureType.PhysicalDeviceAccelerationStructureFeaturesKhr,
                AccelerationStructure = true,
                PNext = &rtFeatures,
            };

            PhysicalDeviceVulkan12Features features12 = new PhysicalDeviceVulkan12Features
            {
                SType = StructureType.PhysicalDeviceVulkan12Features,
                BufferDeviceAddress = true,
                DescriptorIndexing = true,
                RuntimeDescriptorArray = true,
                ShaderSampledImageArrayNonUniformIndexing = true,
                DescriptorBindingPartiallyBound = true,
                DescriptorBindingVariableDescriptorCount = true,
                ScalarBlockLayout = true,
                ShaderInt8 = false,
                PNext = &asFeatures,
            };

            PhysicalDeviceFeatures2 features2 = new PhysicalDeviceFeatures2
            {
                SType = StructureType.PhysicalDeviceFeatures2,
                PNext = &features12,
            };

            features2.Features.ShaderInt64 = true;
            features2.Features.SamplerAnisotropy = true;

            byte** extensionNames = (byte**)SilkMarshal.StringArrayToPtr(RequiredDeviceExtensions);

            try
            {
                DeviceCreateInfo info = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    PNext = &features2,
                    QueueCreateInfoCount = 1,
                    PQueueCreateInfos = &queueInfo,
                    EnabledExtensionCount = (uint)RequiredDeviceExtensions.Length,
                    PpEnabledExtensionNames = extensionNames,
                };

                Device device;
                Result result = Api.CreateDevice(PhysicalDevice, &info, null, &device);

                if (result != Result.Success)
                    throw new VulkanUnavailableException("Could not create a ray tracing device on " + DeviceName + " (" + result + ").");

                Device = device;
            }
            finally
            {
                SilkMarshal.Free((nint)extensionNames);
            }

            Queue queue;
            Api.GetDeviceQueue(Device, QueueFamily, 0, &queue);
            Queue = queue;

            KhrSwapchain swapchain;
            KhrAccelerationStructure accel;
            KhrRayTracingPipeline rt;

            if (!Api.TryGetDeviceExtension(Instance, Device, out swapchain)
                || !Api.TryGetDeviceExtension(Instance, Device, out accel)
                || !Api.TryGetDeviceExtension(Instance, Device, out rt))
                throw new VulkanUnavailableException("The device exposes ray tracing but its entry points could not be loaded.");

            KhrSwapchain = swapchain;
            KhrAccelerationStructure = accel;
            KhrRayTracingPipeline = rt;

            CommandPoolCreateInfo poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = QueueFamily,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            };

            CommandPool pool;
            Check(Api.CreateCommandPool(Device, &poolInfo, null, &pool), "vkCreateCommandPool");
            CommandPool = pool;
        }

        // ==================== memory ====================

        public uint FindMemoryType(uint typeBits, MemoryPropertyFlags wanted)
        {
            PhysicalDeviceMemoryProperties props = MemoryProperties;

            // The memory types are an inline array inside the struct; walked through a pointer
            // to the copy on this stack frame.
            MemoryType* types = (MemoryType*)&props.MemoryTypes;

            for (int i = 0; i < props.MemoryTypeCount; i++)
            {
                bool allowed = (typeBits & (1u << i)) != 0;
                bool matches = (types[i].PropertyFlags & wanted) == wanted;

                if (allowed && matches)
                    return (uint)i;
            }

            throw new VulkanException("No memory type offers " + wanted + ".");
        }

        // ==================== buffers ====================

        /// <summary>A buffer, its memory, and its device address when it was made with one.</summary>
        public sealed class Buffer : IDisposable
        {
            private readonly VulkanDevice _device;

            public Silk.NET.Vulkan.Buffer Handle;
            public DeviceMemory Memory;
            public ulong Size;
            public ulong DeviceAddress;
            public bool HostVisible;

            internal Buffer(VulkanDevice device)
            {
                _device = device;
            }

            /// <summary>Write host data into a host-visible buffer.</summary>
            public void Write<T>(ReadOnlySpan<T> data, ulong offset = 0) where T : unmanaged
            {
                if (!HostVisible)
                    throw new VulkanException("Buffer is not host visible.");

                ulong bytes = (ulong)(data.Length * sizeof(T));

                if (bytes == 0)
                    return;

                void* mapped;
                Check(_device.Api.MapMemory(_device.Device, Memory, offset, bytes, 0, &mapped), "vkMapMemory");

                fixed (T* src = data)
                    System.Buffer.MemoryCopy(src, mapped, bytes, bytes);

                _device.Api.UnmapMemory(_device.Device, Memory);
            }

            public void Write<T>(T[] data, ulong offset = 0) where T : unmanaged
            {
                Write(new ReadOnlySpan<T>(data), offset);
            }

            /// <summary>Read a host-visible buffer back.</summary>
            public T[] Read<T>(int count, ulong offset = 0) where T : unmanaged
            {
                T[] result = new T[count];

                if (count == 0)
                    return result;

                void* mapped;
                Check(_device.Api.MapMemory(_device.Device, Memory, offset, (ulong)(count * sizeof(T)), 0, &mapped), "vkMapMemory");

                fixed (T* dst = result)
                    System.Buffer.MemoryCopy(mapped, dst, count * sizeof(T), count * sizeof(T));

                _device.Api.UnmapMemory(_device.Device, Memory);
                return result;
            }

            public void Dispose()
            {
                if (Handle.Handle != 0)
                {
                    _device.Api.DestroyBuffer(_device.Device, Handle, null);
                    Handle = default;
                }

                if (Memory.Handle != 0)
                {
                    _device.Api.FreeMemory(_device.Device, Memory, null);
                    Memory = default;
                }
            }
        }

        public Buffer CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags memory, bool deviceAddress = false)
        {
            if (size == 0)
                size = 16;

            if (deviceAddress)
                usage |= BufferUsageFlags.ShaderDeviceAddressBit;

            BufferCreateInfo info = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };

            Buffer buffer = new Buffer(this) { Size = size, HostVisible = (memory & MemoryPropertyFlags.HostVisibleBit) != 0 };

            Silk.NET.Vulkan.Buffer handle;
            Check(Api.CreateBuffer(Device, &info, null, &handle), "vkCreateBuffer");
            buffer.Handle = handle;

            MemoryRequirements requirements;
            Api.GetBufferMemoryRequirements(Device, handle, &requirements);

            MemoryAllocateFlagsInfo flags = new MemoryAllocateFlagsInfo
            {
                SType = StructureType.MemoryAllocateFlagsInfo,
                Flags = MemoryAllocateFlags.DeviceAddressBit,
            };

            MemoryAllocateInfo allocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, memory),
                PNext = deviceAddress ? &flags : null,
            };

            DeviceMemory deviceMemory;
            Check(Api.AllocateMemory(Device, &allocate, null, &deviceMemory), "vkAllocateMemory");
            buffer.Memory = deviceMemory;

            Check(Api.BindBufferMemory(Device, handle, deviceMemory, 0), "vkBindBufferMemory");

            if (deviceAddress)
            {
                BufferDeviceAddressInfo addressInfo = new BufferDeviceAddressInfo
                {
                    SType = StructureType.BufferDeviceAddressInfo,
                    Buffer = handle,
                };

                buffer.DeviceAddress = Api.GetBufferDeviceAddress(Device, &addressInfo);
            }

            return buffer;
        }

        /// <summary>
        /// A device-local buffer filled from host data through a staging copy. The usual way
        /// to get geometry onto the card.
        /// </summary>
        public Buffer CreateDeviceBuffer<T>(T[] data, BufferUsageFlags usage, bool deviceAddress = true) where T : unmanaged
        {
            ulong size = (ulong)Math.Max(16, data.Length * sizeof(T));

            Buffer staging = CreateBuffer(size, BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            staging.Write(data);

            Buffer result = CreateBuffer(size, usage | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit, deviceAddress);

            CommandBuffer cmd = BeginOneShot();

            BufferCopy region = new BufferCopy { Size = size };
            Api.CmdCopyBuffer(cmd, staging.Handle, result.Handle, 1, &region);

            EndOneShot(cmd);
            staging.Dispose();

            return result;
        }

        // ==================== images ====================

        public sealed class Image : IDisposable
        {
            private readonly VulkanDevice _device;

            public Silk.NET.Vulkan.Image Handle;
            public DeviceMemory Memory;
            public ImageView View;
            public Format Format;
            public uint Width;
            public uint Height;
            public ImageLayout Layout = ImageLayout.Undefined;

            internal Image(VulkanDevice device)
            {
                _device = device;
            }

            public void Dispose()
            {
                if (View.Handle != 0)
                {
                    _device.Api.DestroyImageView(_device.Device, View, null);
                    View = default;
                }

                if (Handle.Handle != 0)
                {
                    _device.Api.DestroyImage(_device.Device, Handle, null);
                    Handle = default;
                }

                if (Memory.Handle != 0)
                {
                    _device.Api.FreeMemory(_device.Device, Memory, null);
                    Memory = default;
                }
            }
        }

        public Image CreateImage(uint width, uint height, Format format, ImageUsageFlags usage, uint mipLevels = 1)
        {
            ImageCreateInfo info = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = format,
                Extent = new Extent3D(width, height, 1),
                MipLevels = mipLevels,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };

            Image image = new Image(this) { Format = format, Width = width, Height = height };

            Silk.NET.Vulkan.Image handle;
            Check(Api.CreateImage(Device, &info, null, &handle), "vkCreateImage");
            image.Handle = handle;

            MemoryRequirements requirements;
            Api.GetImageMemoryRequirements(Device, handle, &requirements);

            MemoryAllocateInfo allocate = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit),
            };

            DeviceMemory memory;
            Check(Api.AllocateMemory(Device, &allocate, null, &memory), "vkAllocateMemory");
            image.Memory = memory;
            Check(Api.BindImageMemory(Device, handle, memory, 0), "vkBindImageMemory");

            ImageViewCreateInfo viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = handle,
                ViewType = ImageViewType.Type2D,
                Format = format,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, mipLevels, 0, 1),
            };

            ImageView view;
            Check(Api.CreateImageView(Device, &viewInfo, null, &view), "vkCreateImageView");
            image.View = view;

            return image;
        }

        /// <summary>A layout transition, with the broad barrier that is always correct.</summary>
        public void Transition(CommandBuffer cmd, Image image, ImageLayout to, uint mipLevels = 1)
        {
            Transition(cmd, image.Handle, image.Layout, to, mipLevels);
            image.Layout = to;
        }

        public void Transition(CommandBuffer cmd, Silk.NET.Vulkan.Image image, ImageLayout from, ImageLayout to, uint mipLevels = 1)
        {
            ImageMemoryBarrier barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = from,
                NewLayout = to,
                SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, mipLevels, 0, 1),
                SrcAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
                DstAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
            };

            Api.CmdPipelineBarrier(cmd,
                PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
                0, 0, null, 0, null, 1, &barrier);
        }

        /// <summary>A barrier making every write so far visible to everything after.</summary>
        public void FullBarrier(CommandBuffer cmd)
        {
            MemoryBarrier barrier = new MemoryBarrier
            {
                SType = StructureType.MemoryBarrier,
                SrcAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
                DstAccessMask = AccessFlags.MemoryWriteBit | AccessFlags.MemoryReadBit,
            };

            Api.CmdPipelineBarrier(cmd,
                PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit,
                0, 1, &barrier, 0, null, 0, null);
        }

        // ==================== one-shot commands ====================

        public CommandBuffer BeginOneShot()
        {
            CommandBufferAllocateInfo allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = CommandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };

            CommandBuffer cmd;
            Check(Api.AllocateCommandBuffers(Device, &allocate, &cmd), "vkAllocateCommandBuffers");

            CommandBufferBeginInfo begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };

            Check(Api.BeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
            return cmd;
        }

        /// <summary>Submit and wait. Setup work only; a frame never waits like this.</summary>
        public void EndOneShot(CommandBuffer cmd)
        {
            Check(Api.EndCommandBuffer(cmd), "vkEndCommandBuffer");

            SubmitInfo submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &cmd,
            };

            lock (QueueGate)
            {
                Check(Api.QueueSubmit(Queue, 1, &submit, default), "vkQueueSubmit");
                Check(Api.QueueWaitIdle(Queue), "vkQueueWaitIdle");
            }

            Api.FreeCommandBuffers(Device, CommandPool, 1, &cmd);
        }

        public void WaitIdle()
        {
            lock (QueueGate)
                Api.DeviceWaitIdle(Device);
        }

        // ==================== shader modules ====================

        public ShaderModule CreateShaderModule(byte[] spirv)
        {
            fixed (byte* code = spirv)
            {
                ShaderModuleCreateInfo info = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)spirv.Length,
                    PCode = (uint*)code,
                };

                ShaderModule module;
                Check(Api.CreateShaderModule(Device, &info, null, &module), "vkCreateShaderModule");
                return module;
            }
        }

        // ==================== errors ====================

        public static void Check(Result result, string what)
        {
            if (result == Result.Success)
                return;

            if (result == Result.ErrorDeviceLost)
                _shared?.MarkLost();

            throw new VulkanException(what + " failed: " + result);
        }

        public void Dispose()
        {
            if (Device.Handle != 0)
            {
                Api.DeviceWaitIdle(Device);
                Api.DestroyCommandPool(Device, CommandPool, null);
                Api.DestroyDevice(Device, null);
                Device = default;
            }

            if (Instance.Handle != 0)
            {
                Api.DestroyInstance(Instance, null);
                Instance = default;
            }
        }
    }

    /// <summary>The machine cannot do this; the host should keep whatever renderer it had.</summary>
    public class VulkanUnavailableException : Exception
    {
        public VulkanUnavailableException(string message) : base(message) { }
    }

    /// <summary>A Vulkan call failed on a machine that can do this.</summary>
    public class VulkanException : Exception
    {
        public VulkanException(string message) : base(message) { }
    }
}
