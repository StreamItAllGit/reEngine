// @module: reEngine.Host.Rendering.VulkanPresenter
// @purpose: Minimal clean-room Vulkan 1.x hardware swapchain presenter and HostGraphicsContextHandle interop provider.
// @depends-on: System, System.Text, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: VulkanPresenter

using System.Text;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

/// <summary>
/// Clean-room Vulkan 1.x presenter creating a native surface, device, swapchain, and staging upload pipeline.
/// </summary>
public sealed unsafe class VulkanPresenter : IHostSurfacePresenter
{
    private VkDispatch _vk;
    private IntPtr _sdlWindow, _instance, _physicalDevice, _device, _queue, _cmdBuffer;
    private ulong _surface, _swapchain, _cmdPool, _fence, _imgAvailSem, _renderDoneSem, _stagingBuf, _stagingMem;
    private void* _stagingMapped;
    private ulong _stagingSize;
    private ulong[] _swapImages = [];
    private uint _queueFamily, _swapWidth, _swapHeight, _swapFormat;
    private bool _vulkanLoaded, _disposed;
    private VulkanContext? _graphicsContext;

    public HostRenderBackendKind BackendKind => HostRenderBackendKind.Vulkan;
    public string BackendId => HostRenderBackendIds.Vulkan;
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;

    public bool TryInitialize(IntPtr sdlWindow, int width, int height, out string diagnostic)
    {
        if (sdlWindow == IntPtr.Zero) { diagnostic = "SDL window handle is zero."; return false; }
        _sdlWindow = sdlWindow;
        if (Sdl3Native.SDL_Vulkan_LoadLibrary(null) == 0) { diagnostic = $"SDL_Vulkan_LoadLibrary failed ({Sdl3Native.GetErrorString()})"; return false; }
        _vulkanLoaded = true;

        _vk.GetInstanceProcAddr = (delegate* unmanaged[Stdcall]<IntPtr, byte*, IntPtr>)Sdl3Native.SDL_Vulkan_GetVkGetInstanceProcAddr();
        if (_vk.GetInstanceProcAddr == null) { diagnostic = "vkGetInstanceProcAddr unavailable."; Dispose(); return false; }
        _vk.CreateInstance = (delegate* unmanaged[Stdcall]<VkInstanceCreateInfo*, IntPtr, IntPtr*, int>)_vk.GetInst(IntPtr.Zero, "vkCreateInstance");
        if (_vk.CreateInstance == null) { diagnostic = "vkCreateInstance unavailable."; Dispose(); return false; }

        if (!CreateInstance(out diagnostic) ||
            Sdl3Native.SDL_Vulkan_CreateSurface(sdlWindow, _instance, IntPtr.Zero, out _surface) == 0 ||
            !SelectPhysicalAndLogicalDevice(out diagnostic) ||
            !CreateSyncAndCommandObjects(out diagnostic) ||
            !CreateOrResizeSwapchain((uint)Math.Max(1, width), (uint)Math.Max(1, height), out diagnostic))
        {
            if (string.IsNullOrEmpty(diagnostic)) diagnostic = $"SDL_Vulkan_CreateSurface failed ({Sdl3Native.GetErrorString()})";
            Dispose();
            return false;
        }

        _graphicsContext = new VulkanContext(this, sdlWindow, _device, _queue, _instance, unchecked((IntPtr)(long)_surface));
        diagnostic = $"Vulkan swapchain initialized ({_swapWidth}x{_swapHeight}, fmt={_swapFormat}).";
        return true;
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_disposed || _device == IntPtr.Zero || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;
        int winW = width, winH = height;
        if (Sdl3Native.SDL_GetWindowSizeInPixels(_sdlWindow, out int pw, out int ph) != 0) { winW = pw; winH = ph; }
        if (winW <= 0 || winH <= 0) return;

        if ((uint)winW != _swapWidth || (uint)winH != _swapHeight)
        {
            if (!CreateOrResizeSwapchain((uint)winW, (uint)winH, out _)) return;
        }

        ulong fence = _fence;
        _ = _vk.WaitForFences(_device, 1, &fence, 1, ulong.MaxValue);
        uint imgIdx = 0;
        int acq = _vk.AcquireNextImageKHR(_device, _swapchain, ulong.MaxValue, _imgAvailSem, 0, &imgIdx);
        if (acq == VulkanNative.VkErrorOutOfDateKhr) { _ = CreateOrResizeSwapchain((uint)winW, (uint)winH, out _); return; }
        if (acq != VulkanNative.VkSuccess && acq != VulkanNative.VkSuboptimalKhr) return;

        ulong neededBytes = (ulong)_swapWidth * _swapHeight * 4;
        if (!EnsureStagingBuffer(neededBytes)) return;
        VulkanNative.CopyRgbaToStaging(width, height, rgba, (byte*)_stagingMapped, (int)_swapWidth, (int)_swapHeight, _swapFormat == VulkanNative.VkFormatB8G8R8A8Unorm);

        _ = _vk.ResetFences(_device, 1, &fence);
        _ = _vk.ResetCommandBuffer(_cmdBuffer, 0);
        var beginInfo = new VkCommandBufferBeginInfo { SType = 42u, Flags = VulkanNative.VkCommandBufferOneTimeSubmitBit };
        _ = _vk.BeginCommandBuffer(_cmdBuffer, &beginInfo);
        VulkanNative.RecordSwapchainCopy(in _vk, _cmdBuffer, _stagingBuf, _swapImages[imgIdx], _swapWidth, _swapHeight);
        _ = _vk.EndCommandBuffer(_cmdBuffer);

        ulong waitSem = _imgAvailSem, sigSem = _renderDoneSem, sc = _swapchain;
        uint waitStage = VulkanNative.VkPipelineStageTransferBit;
        IntPtr cmd = _cmdBuffer;
        var submit = new VkSubmitInfo { SType = 4u, WaitSemaphoreCount = 1, PWaitSemaphores = &waitSem, PWaitDstStageMask = &waitStage, CommandBufferCount = 1, PCommandBuffers = &cmd, SignalSemaphoreCount = 1, PSignalSemaphores = &sigSem };
        if (_vk.QueueSubmit(_queue, 1, &submit, _fence) != VulkanNative.VkSuccess) return;

        var present = new VkPresentInfoKHR { SType = 1000001001u, WaitSemaphoreCount = 1, PWaitSemaphores = &sigSem, SwapchainCount = 1, PSwapchains = &sc, PImageIndices = &imgIdx };
        int pres = _vk.QueuePresentKHR(_queue, &present);
        if (pres == VulkanNative.VkErrorOutOfDateKhr || pres == VulkanNative.VkSuboptimalKhr) _ = CreateOrResizeSwapchain((uint)winW, (uint)winH, out _);
    }

    private bool CreateInstance(out string diagnostic)
    {
        byte** sdlExts = Sdl3Native.SDL_Vulkan_GetInstanceExtensions(out uint extCount);
        if (sdlExts == null || extCount == 0) { diagnostic = $"SDL_Vulkan_GetInstanceExtensions failed ({Sdl3Native.GetErrorString()})"; return false; }

        byte[] appName = Encoding.UTF8.GetBytes("reEngine\0");
        fixed (byte* pApp = appName)
        {
            var appInfo = new VkApplicationInfo { SType = 0u, PApplicationName = pApp, ApplicationVersion = 1, PEngineName = pApp, EngineVersion = 1, ApiVersion = VulkanNative.MakeApiVersion(1, 1, 0) };
            var instInfo = new VkInstanceCreateInfo { SType = 1u, PApplicationInfo = &appInfo, EnabledExtensionCount = extCount, PpEnabledExtensionNames = sdlExts };
            IntPtr inst = IntPtr.Zero;
            int res = _vk.CreateInstance(&instInfo, IntPtr.Zero, &inst);
            if (res != VulkanNative.VkSuccess || inst == IntPtr.Zero) { diagnostic = $"vkCreateInstance failed (VkResult={res})."; return false; }
            _instance = inst;
        }
        if (!_vk.LoadInstanceProcs(_instance)) { diagnostic = "Failed loading Vulkan instance procedures."; return false; }
        diagnostic = string.Empty;
        return true;
    }

    private bool SelectPhysicalAndLogicalDevice(out string diagnostic)
    {
        uint count = 0;
        _ = _vk.EnumeratePhysicalDevices(_instance, &count, null);
        if (count == 0) { diagnostic = "No Vulkan physical devices found."; return false; }
        IntPtr* devs = stackalloc IntPtr[(int)Math.Min(count, 16u)];
        uint cap = Math.Min(count, 16u);
        _ = _vk.EnumeratePhysicalDevices(_instance, &cap, devs);
        var qProps = stackalloc VkQueueFamilyProperties[16];

        for (uint i = 0; i < cap; i++)
        {
            uint qCount = 0;
            _vk.GetPhysicalDeviceQueueFamilyProperties(devs[i], &qCount, null);
            uint qCap = Math.Min(qCount, 16u);
            _vk.GetPhysicalDeviceQueueFamilyProperties(devs[i], &qCap, qProps);
            for (uint q = 0; q < qCap; q++)
            {
                uint presentSupport = 0;
                _ = _vk.GetPhysicalDeviceSurfaceSupportKHR(devs[i], q, _surface, &presentSupport);
                if ((qProps[q].QueueFlags & VulkanNative.VkQueueGraphicsBit) != 0 && presentSupport != 0)
                {
                    _physicalDevice = devs[i]; _queueFamily = q; break;
                }
            }
            if (_physicalDevice != IntPtr.Zero) break;
        }
        if (_physicalDevice == IntPtr.Zero) { diagnostic = "No Vulkan graphics+present queue family available."; return false; }

        float prio = 1f;
        var qInfo = new VkDeviceQueueCreateInfo { SType = 2u, QueueFamilyIndex = _queueFamily, QueueCount = 1, PQueuePriorities = &prio };
        byte[] swapExt = Encoding.ASCII.GetBytes("VK_KHR_swapchain\0");
        fixed (byte* pSwapExt = swapExt)
        {
            byte* exts = pSwapExt;
            var devInfo = new VkDeviceCreateInfo { SType = 3u, QueueCreateInfoCount = 1, PQueueCreateInfos = &qInfo, EnabledExtensionCount = 1, PpEnabledExtensionNames = &exts };
            IntPtr dev = IntPtr.Zero;
            int res = _vk.CreateDevice(_physicalDevice, &devInfo, IntPtr.Zero, &dev);
            if (res != VulkanNative.VkSuccess || dev == IntPtr.Zero) { diagnostic = $"vkCreateDevice failed (VkResult={res})."; return false; }
            _device = dev;
        }
        if (!_vk.LoadDeviceProcs(_device)) { diagnostic = "Failed loading Vulkan device procedures."; return false; }
        IntPtr qHandle = IntPtr.Zero;
        _vk.GetDeviceQueue(_device, _queueFamily, 0, &qHandle);
        _queue = qHandle;
        diagnostic = string.Empty;
        return _queue != IntPtr.Zero;
    }

    private bool CreateSyncAndCommandObjects(out string diagnostic)
    {
        var poolInfo = new VkCommandPoolCreateInfo { SType = 39u, Flags = VulkanNative.VkCommandPoolCreateResetBit, QueueFamilyIndex = _queueFamily };
        ulong pool = 0, fence = 0, s1 = 0, s2 = 0;
        if (_vk.CreateCommandPool(_device, &poolInfo, IntPtr.Zero, &pool) != VulkanNative.VkSuccess) { diagnostic = "vkCreateCommandPool failed."; return false; }
        _cmdPool = pool;

        var allocInfo = new VkCommandBufferAllocateInfo { SType = 40u, CommandPool = _cmdPool, Level = 0, CommandBufferCount = 1 };
        IntPtr cmd = IntPtr.Zero;
        if (_vk.AllocateCommandBuffers(_device, &allocInfo, &cmd) != VulkanNative.VkSuccess) { diagnostic = "vkAllocateCommandBuffers failed."; return false; }
        _cmdBuffer = cmd;

        var fenceInfo = new VkFenceCreateInfo { SType = 8u, Flags = VulkanNative.VkFenceCreateSignaledBit };
        var semInfo = new VkSemaphoreCreateInfo { SType = 9u };
        if (_vk.CreateFence(_device, &fenceInfo, IntPtr.Zero, &fence) != VulkanNative.VkSuccess ||
            _vk.CreateSemaphore(_device, &semInfo, IntPtr.Zero, &s1) != VulkanNative.VkSuccess ||
            _vk.CreateSemaphore(_device, &semInfo, IntPtr.Zero, &s2) != VulkanNative.VkSuccess)
        {
            diagnostic = "Failed creating Vulkan sync primitives."; return false;
        }
        _fence = fence; _imgAvailSem = s1; _renderDoneSem = s2;
        diagnostic = string.Empty;
        return true;
    }

    private bool CreateOrResizeSwapchain(uint targetW, uint targetH, out string diagnostic)
    {
        _ = _vk.DeviceWaitIdle(_device);
        VkSurfaceCapabilitiesKHR caps;
        if (_vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(_physicalDevice, _surface, &caps) != VulkanNative.VkSuccess) { diagnostic = "GetPhysicalDeviceSurfaceCapabilitiesKHR failed."; return false; }

        uint extW = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent.Width : Math.Clamp(targetW, caps.MinImageExtent.Width, caps.MaxImageExtent.Width);
        uint extH = caps.CurrentExtent.Height != uint.MaxValue ? caps.CurrentExtent.Height : Math.Clamp(targetH, caps.MinImageExtent.Height, caps.MaxImageExtent.Height);
        if (extW == 0 || extH == 0) { diagnostic = "Minimized surface extent."; return false; }

        uint fmtCount = 0;
        _ = _vk.GetPhysicalDeviceSurfaceFormatsKHR(_physicalDevice, _surface, &fmtCount, null);
        var fmts = stackalloc VkSurfaceFormatKHR[(int)Math.Clamp(fmtCount, 1u, 32u)];
        uint fmtCap = Math.Min(fmtCount, 32u);
        if (fmtCap > 0) _ = _vk.GetPhysicalDeviceSurfaceFormatsKHR(_physicalDevice, _surface, &fmtCap, fmts);
        VkSurfaceFormatKHR chosen = fmtCap > 0 ? fmts[0] : new VkSurfaceFormatKHR { Format = VulkanNative.VkFormatB8G8R8A8Unorm };
        for (uint i = 0; i < fmtCap; i++) if (fmts[i].Format is VulkanNative.VkFormatB8G8R8A8Unorm or VulkanNative.VkFormatR8G8B8A8Unorm) { chosen = fmts[i]; break; }

        uint imgCount = Math.Max(caps.MinImageCount + 1, 2u);
        if (caps.MaxImageCount > 0 && imgCount > caps.MaxImageCount) imgCount = caps.MaxImageCount;
        uint usage = VulkanNative.VkImageUsageColorAttachmentBit | (caps.SupportedUsageFlags & VulkanNative.VkImageUsageTransferDstBit);
        if ((usage & VulkanNative.VkImageUsageTransferDstBit) == 0) { diagnostic = "Swapchain surface lacks TRANSFER_DST support."; return false; }

        ulong oldSc = _swapchain, newSc = 0;
        var scInfo = new VkSwapchainCreateInfoKHR { SType = 1000001000u, Surface = _surface, MinImageCount = imgCount, ImageFormat = chosen.Format, ImageColorSpace = chosen.ColorSpace, ImageExtent = new VkExtent2D { Width = extW, Height = extH }, ImageArrayLayers = 1, ImageUsage = usage, PreTransform = caps.CurrentTransform, CompositeAlpha = VulkanNative.VkCompositeAlphaOpaqueBitKhr, PresentMode = VulkanNative.VkPresentModeFifoKhr, Clipped = 1, OldSwapchain = oldSc };
        int res = _vk.CreateSwapchainKHR(_device, &scInfo, IntPtr.Zero, &newSc);
        if (oldSc != 0) _vk.DestroySwapchainKHR(_device, oldSc, IntPtr.Zero);
        if (res != VulkanNative.VkSuccess || newSc == 0) { _swapchain = 0; diagnostic = $"vkCreateSwapchainKHR failed (VkResult={res})."; return false; }

        _swapchain = newSc; _swapWidth = extW; _swapHeight = extH; _swapFormat = chosen.Format;
        uint actualCount = 0;
        _ = _vk.GetSwapchainImagesKHR(_device, _swapchain, &actualCount, null);
        _swapImages = new ulong[actualCount];
        fixed (ulong* pImgs = _swapImages) _ = _vk.GetSwapchainImagesKHR(_device, _swapchain, &actualCount, pImgs);
        diagnostic = string.Empty;
        return true;
    }

    private bool EnsureStagingBuffer(ulong sizeBytes)
    {
        if (_stagingBuf != 0 && _stagingSize >= sizeBytes) return true;
        if (_stagingMapped != null) { _vk.UnmapMemory(_device, _stagingMem); _stagingMapped = null; }
        if (_stagingBuf != 0) { _vk.DestroyBuffer(_device, _stagingBuf, IntPtr.Zero); _stagingBuf = 0; }
        if (_stagingMem != 0) { _vk.FreeMemory(_device, _stagingMem, IntPtr.Zero); _stagingMem = 0; }
        if (!VulkanNative.TryCreateStagingBuffer(in _vk, _physicalDevice, _device, sizeBytes, out _stagingBuf, out _stagingMem, out _stagingMapped)) return false;
        _stagingSize = sizeBytes;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device != IntPtr.Zero)
        {
            _ = _vk.DeviceWaitIdle(_device);
            if (_stagingMapped != null) { _vk.UnmapMemory(_device, _stagingMem); _stagingMapped = null; }
            if (_stagingBuf != 0) { _vk.DestroyBuffer(_device, _stagingBuf, IntPtr.Zero); _stagingBuf = 0; }
            if (_stagingMem != 0) { _vk.FreeMemory(_device, _stagingMem, IntPtr.Zero); _stagingMem = 0; }
            if (_fence != 0) { _vk.DestroyFence(_device, _fence, IntPtr.Zero); _fence = 0; }
            if (_imgAvailSem != 0) { _vk.DestroySemaphore(_device, _imgAvailSem, IntPtr.Zero); _imgAvailSem = 0; }
            if (_renderDoneSem != 0) { _vk.DestroySemaphore(_device, _renderDoneSem, IntPtr.Zero); _renderDoneSem = 0; }
            if (_cmdPool != 0) { _vk.DestroyCommandPool(_device, _cmdPool, IntPtr.Zero); _cmdPool = 0; }
            if (_swapchain != 0) { _vk.DestroySwapchainKHR(_device, _swapchain, IntPtr.Zero); _swapchain = 0; }
            _vk.DestroyDevice(_device, IntPtr.Zero);
            _device = IntPtr.Zero;
        }
        if (_surface != 0 && _instance != IntPtr.Zero) { Sdl3Native.SDL_Vulkan_DestroySurface(_instance, _surface, IntPtr.Zero); _surface = 0; }
        if (_instance != IntPtr.Zero) { _vk.DestroyInstance(_instance, IntPtr.Zero); _instance = IntPtr.Zero; }
        if (_vulkanLoaded) { Sdl3Native.SDL_Vulkan_UnloadLibrary(); _vulkanLoaded = false; }
        _graphicsContext = null;
    }

    private sealed class VulkanContext : IHostGraphicsContext
    {
        private readonly VulkanPresenter _owner;
        public HostGraphicsContextHandle Handle { get; }
        public string BackendId => HostRenderBackendIds.Vulkan;
        public bool IsHardwareAccelerated => true;
        public VulkanContext(VulkanPresenter owner, IntPtr w, IntPtr dev, IntPtr q, IntPtr inst, IntPtr surf) =>
            (_owner, Handle) = (owner, new HostGraphicsContextHandle(HostRenderBackendKind.Vulkan, w, dev, q, inst, surf));
        public void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba) => _owner.PresentRgba(width, height, rgba);
        public void Dispose() { }
    }
}
