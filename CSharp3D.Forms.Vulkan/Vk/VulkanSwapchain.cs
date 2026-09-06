using System;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace CSharp3D.Forms.Vulkan.Vk
{
    /// <summary>
    /// A window's presentation surface and swapchain: what a view owns, on top of the shared
    /// <see cref="VulkanDevice"/>. Rebuilt whenever the window changes size.
    ///
    /// The swapchain images are only ever a copy target: the renderer draws into its own
    /// storage images and blits the finished frame across, which keeps the swapchain format
    /// (whatever the driver prefers) out of every shader.
    /// </summary>
    public sealed unsafe class VulkanSwapchain : IDisposable
    {
        private readonly VulkanDevice _device;
        private readonly IntPtr _hwnd;
        private SurfaceKHR _surface;

        public SwapchainKHR Handle { get; private set; }
        public Silk.NET.Vulkan.Image[] Images { get; private set; } = new Silk.NET.Vulkan.Image[0];
        public Format Format { get; private set; }
        public uint Width { get; private set; }
        public uint Height { get; private set; }

        /// <summary>Whether <see cref="Rebuild"/> has produced something that can be drawn to.</summary>
        public bool IsUsable => Handle.Handle != 0 && Width > 0 && Height > 0;

        public VulkanSwapchain(VulkanDevice device, IntPtr hwnd)
        {
            _device = device;
            _hwnd = hwnd;

            Win32SurfaceCreateInfoKHR info = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr,
                Hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle,
                Hwnd = hwnd,
            };

            SurfaceKHR surface;
            VulkanDevice.Check(device.KhrWin32Surface.CreateWin32Surface(device.Instance, &info, null, &surface),
                "vkCreateWin32SurfaceKHR");
            _surface = surface;
        }

        /// <summary>
        /// Make (or remake) the swapchain at the window's current size. A zero-sized window -
        /// minimised, or not laid out yet - leaves it unusable rather than failing.
        /// </summary>
        public void Rebuild(uint width, uint height)
        {
            SurfaceCapabilitiesKHR capabilities;
            VulkanDevice.Check(_device.KhrSurface.GetPhysicalDeviceSurfaceCapabilities(_device.PhysicalDevice, _surface, &capabilities),
                "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

            Extent2D extent = capabilities.CurrentExtent;

            if (extent.Width == uint.MaxValue)
                extent = new Extent2D(width, height);

            extent.Width = Math.Max(capabilities.MinImageExtent.Width, Math.Min(capabilities.MaxImageExtent.Width, extent.Width));
            extent.Height = Math.Max(capabilities.MinImageExtent.Height, Math.Min(capabilities.MaxImageExtent.Height, extent.Height));

            _device.WaitIdle();
            Destroy();

            if (extent.Width == 0 || extent.Height == 0)
                return;

            uint formatCount = 0;
            _device.KhrSurface.GetPhysicalDeviceSurfaceFormats(_device.PhysicalDevice, _surface, &formatCount, null);
            SurfaceFormatKHR[] formats = new SurfaceFormatKHR[formatCount];

            fixed (SurfaceFormatKHR* p = formats)
                _device.KhrSurface.GetPhysicalDeviceSurfaceFormats(_device.PhysicalDevice, _surface, &formatCount, p);

            // An 8-bit UNORM surface: the tonemapped frame is already sRGB-encoded, so the
            // swapchain must not encode it again.
            SurfaceFormatKHR chosen = formats[0];

            foreach (SurfaceFormatKHR candidate in formats)
            {
                if (candidate.Format == Silk.NET.Vulkan.Format.B8G8R8A8Unorm || candidate.Format == Silk.NET.Vulkan.Format.R8G8B8A8Unorm)
                {
                    chosen = candidate;
                    break;
                }
            }

            uint imageCount = Math.Max(2, capabilities.MinImageCount);

            if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
                imageCount = capabilities.MaxImageCount;

            SwapchainCreateInfoKHR info = new SwapchainCreateInfoKHR
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = chosen.Format,
                ImageColorSpace = chosen.ColorSpace,
                ImageExtent = extent,
                ImageArrayLayers = 1,
                ImageUsage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.ColorAttachmentBit,
                ImageSharingMode = SharingMode.Exclusive,
                PreTransform = capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,

                PresentMode = ChoosePresentMode(),
                Clipped = true,
            };

            SwapchainKHR handle;
            VulkanDevice.Check(_device.KhrSwapchain.CreateSwapchain(_device.Device, &info, null, &handle), "vkCreateSwapchainKHR");
            Handle = handle;
            Format = chosen.Format;
            Width = extent.Width;
            Height = extent.Height;

            uint count = 0;
            _device.KhrSwapchain.GetSwapchainImages(_device.Device, handle, &count, null);
            Images = new Silk.NET.Vulkan.Image[count];

            fixed (Silk.NET.Vulkan.Image* p = Images)
                _device.KhrSwapchain.GetSwapchainImages(_device.Device, handle, &count, p);
        }

        /// <summary>
        /// Mailbox when the driver has it: a present replaces the frame waiting to be shown
        /// and never blocks, so the render thread is paced by the GPU alone and the display
        /// shows the newest complete frame. Immediate is the same without the queue. FIFO -
        /// wait for the display - is what every driver has and the fallback.
        /// </summary>
        private PresentModeKHR ChoosePresentMode()
        {
            uint count = 0;
            _device.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_device.PhysicalDevice, _surface, &count, null);

            if (count == 0)
                return PresentModeKHR.FifoKhr;

            PresentModeKHR[] modes = new PresentModeKHR[count];

            fixed (PresentModeKHR* p = modes)
                _device.KhrSurface.GetPhysicalDeviceSurfacePresentModes(_device.PhysicalDevice, _surface, &count, p);

            foreach (PresentModeKHR wanted in new[] { PresentModeKHR.MailboxKhr, PresentModeKHR.ImmediateKhr })
            {
                foreach (PresentModeKHR mode in modes)
                {
                    if (mode == wanted)
                        return mode;
                }
            }

            return PresentModeKHR.FifoKhr;
        }

        public Result AcquireNext(Semaphore signal, out uint imageIndex)
        {
            uint index = 0;
            Result result = _device.KhrSwapchain.AcquireNextImage(_device.Device, Handle, ulong.MaxValue, signal, default, &index);
            imageIndex = index;
            return result;
        }

        public Result Present(Semaphore wait, uint imageIndex)
        {
            SwapchainKHR swapchain = Handle;

            PresentInfoKHR info = new PresentInfoKHR
            {
                SType = StructureType.PresentInfoKhr,
                WaitSemaphoreCount = 1,
                PWaitSemaphores = &wait,
                SwapchainCount = 1,
                PSwapchains = &swapchain,
                PImageIndices = &imageIndex,
            };

            lock (_device.QueueGate)
                return _device.KhrSwapchain.QueuePresent(_device.Queue, &info);
        }

        private void Destroy()
        {
            if (Handle.Handle != 0)
            {
                _device.KhrSwapchain.DestroySwapchain(_device.Device, Handle, null);
                Handle = default;
            }

            Images = new Silk.NET.Vulkan.Image[0];
            Width = Height = 0;
        }

        public void Dispose()
        {
            Destroy();

            if (_surface.Handle != 0)
            {
                _device.KhrSurface.DestroySurface(_device.Instance, _surface, null);
                _surface = default;
            }
        }
    }
}
