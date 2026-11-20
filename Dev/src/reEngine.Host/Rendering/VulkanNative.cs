// @module: reEngine.Host.Rendering.VulkanNative
// @purpose: Clean-room Vulkan 1.x C-ABI structs, dynamic dispatch loader, staging buffer allocator, and command-buffer copy helper.
// @depends-on: System, System.Runtime.InteropServices, System.Text
// @exports: VkDispatch, VulkanNative

using System.Runtime.InteropServices;
using System.Text;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

internal static unsafe class VulkanNative
{
    public const int VkSuccess = 0, VkSuboptimalKhr = 1000001003, VkErrorOutOfDateKhr = -1000001004;
    public const uint VkFormatR8G8B8A8Unorm = 37u, VkFormatB8G8R8A8Unorm = 44u, VkQueueGraphicsBit = 1u;
    public const uint VkImageUsageTransferDstBit = 0x02u, VkImageUsageColorAttachmentBit = 0x10u, VkBufferUsageTransferSrcBit = 0x01u;
    public const uint VkMemoryPropertyHostVisibleBit = 0x02u, VkMemoryPropertyHostCoherentBit = 0x04u;
    public const uint VkImageLayoutUndefined = 0u, VkImageLayoutTransferDstOptimal = 7u, VkImageLayoutPresentSrcKhr = 1000001002u;
    public const uint VkAccessTransferWriteBit = 0x1000u, VkPipelineStageTopOfPipeBit = 0x1u, VkPipelineStageTransferBit = 0x1000u, VkPipelineStageBottomOfPipeBit = 0x2000u;
    public const uint VkFenceCreateSignaledBit = 1u, VkCommandPoolCreateResetBit = 2u, VkCommandBufferOneTimeSubmitBit = 1u;
    public const uint VkPresentModeFifoKhr = 2u, VkCompositeAlphaOpaqueBitKhr = 1u, VkQueueFamilyIgnored = ~0u;

    public static uint MakeApiVersion(uint major, uint minor, uint patch) => (major << 22) | (minor << 12) | patch;

    public static bool TryCreateStagingBuffer(in VkDispatch vk, IntPtr physicalDevice, IntPtr device, ulong sizeBytes, out ulong buffer, out ulong memory, out void* mapped)
    {
        buffer = 0; memory = 0; mapped = null;
        var bufInfo = new VkBufferCreateInfo { SType = 12u, Size = sizeBytes, Usage = VkBufferUsageTransferSrcBit };
        ulong buf = 0, mem = 0;
        if (vk.CreateBuffer(device, &bufInfo, IntPtr.Zero, &buf) != VkSuccess) return false;
        VkMemoryRequirements req;
        vk.GetBufferMemoryRequirements(device, buf, &req);
        VkPhysicalDeviceMemoryProperties props;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, &props);

        uint memIdx = uint.MaxValue;
        uint want = VkMemoryPropertyHostVisibleBit | VkMemoryPropertyHostCoherentBit;
        for (uint i = 0; i < props.MemoryTypeCount; i++)
        {
            if ((req.MemoryTypeBits & (1u << (int)i)) != 0 && (props.MemoryTypes[(int)(i * 2)] & want) == want) { memIdx = i; break; }
        }
        if (memIdx == uint.MaxValue) { vk.DestroyBuffer(device, buf, IntPtr.Zero); return false; }

        var alloc = new VkMemoryAllocateInfo { SType = 5u, AllocationSize = req.Size, MemoryTypeIndex = memIdx };
        void* m = null;
        if (vk.AllocateMemory(device, &alloc, IntPtr.Zero, &mem) != VkSuccess ||
            vk.BindBufferMemory(device, buf, mem, 0) != VkSuccess ||
            vk.MapMemory(device, mem, 0, sizeBytes, 0, &m) != VkSuccess)
        {
            vk.DestroyBuffer(device, buf, IntPtr.Zero);
            if (mem != 0) vk.FreeMemory(device, mem, IntPtr.Zero);
            return false;
        }
        buffer = buf; memory = mem; mapped = m;
        return true;
    }

    public static void CopyRgbaToStaging(int srcW, int srcH, ReadOnlySpan<byte> rgba, byte* dst, int dstW, int dstH, bool swapRb) =>
        CopyRgbaToStaging(srcW, srcH, rgba, dst, dstW, dstH, swapRb, new HostViewportRect(0, 0, dstW, dstH), new HostViewportRect(0, 0, srcW, srcH));

    internal static void CopyRgbaToStagingSpan(
        int srcW, int srcH, ReadOnlySpan<byte> rgba, Span<byte> dst, int dstW, int dstH, bool swapRb, HostViewportRect destRect, HostViewportRect srcCropRect)
    {
        if (dst.Length < checked(dstW * dstH * 4)) return;
        fixed (byte* pDst = dst) CopyRgbaToStaging(srcW, srcH, rgba, pDst, dstW, dstH, swapRb, destRect, srcCropRect);
    }

    public static void CopyRgbaToStaging(
        int srcW,
        int srcH,
        ReadOnlySpan<byte> rgba,
        byte* dst,
        int dstW,
        int dstH,
        bool swapRb,
        HostViewportRect destRect,
        HostViewportRect srcCropRect)
    {
        if (dst == null || srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0 || rgba.Length < checked(srcW * srcH * 4)) return;

        HostViewportRect src = HostViewportGeometry.ClampSourceRect(srcW, srcH, srcCropRect);
        if (!swapRb && destRect.X == 0 && destRect.Y == 0 && destRect.Width == dstW && destRect.Height == dstH &&
            src.X == 0 && src.Y == 0 && src.Width == srcW && src.Height == srcH && srcW == dstW && srcH == dstH)
        {
            fixed (byte* srcPtr = rgba) Buffer.MemoryCopy(srcPtr, dst, (long)dstW * dstH * 4, (long)dstW * dstH * 4);
            return;
        }

        FillSurface0(dst, dstW * dstH, swapRb);
        if (destRect.Width <= 0 || destRect.Height <= 0) return;

        int dyStart = Math.Max(0, -destRect.Y);
        int dyEnd = Math.Min(destRect.Height, dstH - destRect.Y);
        int dxStart = Math.Max(0, -destRect.X);
        int dxEnd = Math.Min(destRect.Width, dstW - destRect.X);
        if (dyStart >= dyEnd || dxStart >= dxEnd) return;

        for (int dy = dyStart; dy < dyEnd; dy++)
        {
            int sy = src.Y + (int)((long)dy * src.Height / destRect.Height);
            byte* dstRow = dst + ((destRect.Y + dy) * dstW * 4);
            int srcRowOffset = sy * srcW * 4;
            for (int dx = dxStart; dx < dxEnd; dx++)
            {
                int sx = src.X + (int)((long)dx * src.Width / destRect.Width);
                int sIdx = srcRowOffset + (sx * 4);
                int dIdx = (destRect.X + dx) * 4;
                dstRow[dIdx] = swapRb ? rgba[sIdx + 2] : rgba[sIdx];
                dstRow[dIdx + 1] = rgba[sIdx + 1];
                dstRow[dIdx + 2] = swapRb ? rgba[sIdx] : rgba[sIdx + 2];
                dstRow[dIdx + 3] = 255;
            }
        }
    }

    private static void FillSurface0(byte* dst, int pixelCount, bool swapRb)
    {
        if (BitConverter.IsLittleEndian)
        {
            uint pixel = swapRb ? 0xFF0E0E10u : 0xFF100E0Eu;
            new Span<uint>(dst, pixelCount).Fill(pixel);
            return;
        }

        byte b0 = swapRb ? HostViewportGeometry.Surface0B : HostViewportGeometry.Surface0R;
        byte b1 = HostViewportGeometry.Surface0G;
        byte b2 = swapRb ? HostViewportGeometry.Surface0R : HostViewportGeometry.Surface0B;
        for (int i = 0; i < pixelCount; i++)
        {
            int off = i * 4;
            dst[off] = b0;
            dst[off + 1] = b1;
            dst[off + 2] = b2;
            dst[off + 3] = HostViewportGeometry.Surface0A;
        }
    }

    public static void RecordSwapchainCopy(in VkDispatch vk, IntPtr cmdBuffer, ulong stagingBuf, ulong image, uint width, uint height)
    {
        var range = new VkImageSubresourceRange { AspectMask = 1, LevelCount = 1, LayerCount = 1 };
        var toTransfer = new VkImageMemoryBarrier { SType = 45u, DstAccessMask = VkAccessTransferWriteBit, OldLayout = VkImageLayoutUndefined, NewLayout = VkImageLayoutTransferDstOptimal, SrcQueueFamilyIndex = VkQueueFamilyIgnored, DstQueueFamilyIndex = VkQueueFamilyIgnored, Image = image, SubresourceRange = range };
        vk.CmdPipelineBarrier(cmdBuffer, VkPipelineStageTopOfPipeBit, VkPipelineStageTransferBit, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &toTransfer);

        var copy = new VkBufferImageCopy { ImageSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 }, ExtentWidth = width, ExtentHeight = height, ExtentDepth = 1 };
        vk.CmdCopyBufferToImage(cmdBuffer, stagingBuf, image, VkImageLayoutTransferDstOptimal, 1, &copy);

        var toPresent = new VkImageMemoryBarrier { SType = 45u, SrcAccessMask = VkAccessTransferWriteBit, OldLayout = VkImageLayoutTransferDstOptimal, NewLayout = VkImageLayoutPresentSrcKhr, SrcQueueFamilyIndex = VkQueueFamilyIgnored, DstQueueFamilyIndex = VkQueueFamilyIgnored, Image = image, SubresourceRange = range };
        vk.CmdPipelineBarrier(cmdBuffer, VkPipelineStageTransferBit, VkPipelineStageBottomOfPipeBit, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &toPresent);
    }
}

[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkApplicationInfo { public uint SType; public IntPtr PNext; public byte* PApplicationName; public uint ApplicationVersion; public byte* PEngineName; public uint EngineVersion, ApiVersion; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkInstanceCreateInfo { public uint SType; public IntPtr PNext; public uint Flags; public VkApplicationInfo* PApplicationInfo; public uint EnabledLayerCount; public byte** PpEnabledLayerNames; public uint EnabledExtensionCount; public byte** PpEnabledExtensionNames; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkDeviceQueueCreateInfo { public uint SType; public IntPtr PNext; public uint Flags, QueueFamilyIndex, QueueCount; public float* PQueuePriorities; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkDeviceCreateInfo { public uint SType; public IntPtr PNext; public uint Flags, QueueCreateInfoCount; public VkDeviceQueueCreateInfo* PQueueCreateInfos; public uint EnabledLayerCount; public byte** PpEnabledLayerNames; public uint EnabledExtensionCount; public byte** PpEnabledExtensionNames; public IntPtr PEnabledFeatures; }
[StructLayout(LayoutKind.Sequential)] internal struct VkQueueFamilyProperties { public uint QueueFlags, QueueCount, TimestampValidBits, GranX, GranY, GranZ; }
[StructLayout(LayoutKind.Sequential)] internal struct VkExtent2D { public uint Width, Height; }
[StructLayout(LayoutKind.Sequential)] internal struct VkSurfaceFormatKHR { public uint Format, ColorSpace; }
[StructLayout(LayoutKind.Sequential)] internal struct VkSurfaceCapabilitiesKHR { public uint MinImageCount, MaxImageCount; public VkExtent2D CurrentExtent, MinImageExtent, MaxImageExtent; public uint MaxImageArrayLayers, SupportedTransforms, CurrentTransform, SupportedCompositeAlpha, SupportedUsageFlags; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkSwapchainCreateInfoKHR { public uint SType; public IntPtr PNext; public uint Flags; public ulong Surface; public uint MinImageCount, ImageFormat, ImageColorSpace; public VkExtent2D ImageExtent; public uint ImageArrayLayers, ImageUsage, ImageSharingMode, QueueFamilyIndexCount; public uint* PQueueFamilyIndices; public uint PreTransform, CompositeAlpha, PresentMode, Clipped; public ulong OldSwapchain; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkBufferCreateInfo { public uint SType; public IntPtr PNext; public uint Flags; public ulong Size; public uint Usage, SharingMode, QueueFamilyIndexCount; public uint* PQueueFamilyIndices; }
[StructLayout(LayoutKind.Sequential)] internal struct VkMemoryRequirements { public ulong Size, Alignment; public uint MemoryTypeBits; }
[StructLayout(LayoutKind.Sequential)] internal struct VkMemoryAllocateInfo { public uint SType; public IntPtr PNext; public ulong AllocationSize; public uint MemoryTypeIndex; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkPhysicalDeviceMemoryProperties { public uint MemoryTypeCount; public fixed uint MemoryTypes[64]; public uint MemoryHeapCount; public fixed byte MemoryHeaps[256]; }
[StructLayout(LayoutKind.Sequential)] internal struct VkCommandPoolCreateInfo { public uint SType; public IntPtr PNext; public uint Flags, QueueFamilyIndex; }
[StructLayout(LayoutKind.Sequential)] internal struct VkCommandBufferAllocateInfo { public uint SType; public IntPtr PNext; public ulong CommandPool; public uint Level, CommandBufferCount; }
[StructLayout(LayoutKind.Sequential)] internal struct VkCommandBufferBeginInfo { public uint SType; public IntPtr PNext; public uint Flags; public IntPtr PInheritanceInfo; }
[StructLayout(LayoutKind.Sequential)] internal struct VkFenceCreateInfo { public uint SType; public IntPtr PNext; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct VkSemaphoreCreateInfo { public uint SType; public IntPtr PNext; public uint Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct VkImageSubresourceRange { public uint AspectMask, BaseMipLevel, LevelCount, BaseArrayLayer, LayerCount; }
[StructLayout(LayoutKind.Sequential)] internal struct VkImageMemoryBarrier { public uint SType; public IntPtr PNext; public uint SrcAccessMask, DstAccessMask, OldLayout, NewLayout, SrcQueueFamilyIndex, DstQueueFamilyIndex; public ulong Image; public VkImageSubresourceRange SubresourceRange; }
[StructLayout(LayoutKind.Sequential)] internal struct VkImageSubresourceLayers { public uint AspectMask, MipLevel, BaseArrayLayer, LayerCount; }
[StructLayout(LayoutKind.Sequential)] internal struct VkBufferImageCopy { public ulong BufferOffset; public uint BufferRowLength, BufferImageHeight; public VkImageSubresourceLayers ImageSubresource; public int OffsetX, OffsetY, OffsetZ; public uint ExtentWidth, ExtentHeight, ExtentDepth; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkSubmitInfo { public uint SType; public IntPtr PNext; public uint WaitSemaphoreCount; public ulong* PWaitSemaphores; public uint* PWaitDstStageMask; public uint CommandBufferCount; public IntPtr* PCommandBuffers; public uint SignalSemaphoreCount; public ulong* PSignalSemaphores; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct VkPresentInfoKHR { public uint SType; public IntPtr PNext; public uint WaitSemaphoreCount; public ulong* PWaitSemaphores; public uint SwapchainCount; public ulong* PSwapchains; public uint* PImageIndices; public int* PResults; }

internal unsafe struct VkDispatch
{
    public delegate* unmanaged[Stdcall]<IntPtr, byte*, IntPtr> GetInstanceProcAddr;
    public delegate* unmanaged[Stdcall]<VkInstanceCreateInfo*, IntPtr, IntPtr*, int> CreateInstance;
    public delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> DestroyInstance;
    public delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr*, int> EnumeratePhysicalDevices;
    public delegate* unmanaged[Stdcall]<IntPtr, uint*, VkQueueFamilyProperties*, void> GetPhysicalDeviceQueueFamilyProperties;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, ulong, uint*, int> GetPhysicalDeviceSurfaceSupportKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, VkPhysicalDeviceMemoryProperties*, void> GetPhysicalDeviceMemoryProperties;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, VkSurfaceCapabilitiesKHR*, int> GetPhysicalDeviceSurfaceCapabilitiesKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, uint*, VkSurfaceFormatKHR*, int> GetPhysicalDeviceSurfaceFormatsKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, VkDeviceCreateInfo*, IntPtr, IntPtr*, int> CreateDevice;
    public delegate* unmanaged[Stdcall]<IntPtr, byte*, IntPtr> GetDeviceProcAddr;
    public delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void> DestroyDevice;
    public delegate* unmanaged[Stdcall]<IntPtr, int> DeviceWaitIdle;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void> GetDeviceQueue;
    public delegate* unmanaged[Stdcall]<IntPtr, VkSwapchainCreateInfoKHR*, IntPtr, ulong*, int> CreateSwapchainKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> DestroySwapchainKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, uint*, ulong*, int> GetSwapchainImagesKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, ulong, uint*, int> AcquireNextImageKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, VkPresentInfoKHR*, int> QueuePresentKHR;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, VkSubmitInfo*, ulong, int> QueueSubmit;
    public delegate* unmanaged[Stdcall]<IntPtr, VkCommandPoolCreateInfo*, IntPtr, ulong*, int> CreateCommandPool;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> DestroyCommandPool;
    public delegate* unmanaged[Stdcall]<IntPtr, VkCommandBufferAllocateInfo*, IntPtr*, int> AllocateCommandBuffers;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> DestroyFence;
    public delegate* unmanaged[Stdcall]<IntPtr, VkFenceCreateInfo*, IntPtr, ulong*, int> CreateFence;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, ulong*, uint, ulong, int> WaitForFences;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, ulong*, int> ResetFences;
    public delegate* unmanaged[Stdcall]<IntPtr, VkSemaphoreCreateInfo*, IntPtr, ulong*, int> CreateSemaphore;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> DestroySemaphore;
    public delegate* unmanaged[Stdcall]<IntPtr, VkBufferCreateInfo*, IntPtr, ulong*, int> CreateBuffer;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> DestroyBuffer;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, VkMemoryRequirements*, void> GetBufferMemoryRequirements;
    public delegate* unmanaged[Stdcall]<IntPtr, VkMemoryAllocateInfo*, IntPtr, ulong*, int> AllocateMemory;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void> FreeMemory;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, int> BindBufferMemory;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, uint, void**, int> MapMemory;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, void> UnmapMemory;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, int> ResetCommandBuffer;
    public delegate* unmanaged[Stdcall]<IntPtr, VkCommandBufferBeginInfo*, int> BeginCommandBuffer;
    public delegate* unmanaged[Stdcall]<IntPtr, int> EndCommandBuffer;
    public delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, uint, VkImageMemoryBarrier*, void> CmdPipelineBarrier;
    public delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, uint, uint, VkBufferImageCopy*, void> CmdCopyBufferToImage;

    public bool LoadInstanceProcs(IntPtr instance)
    {
        CreateDevice = (delegate* unmanaged[Stdcall]<IntPtr, VkDeviceCreateInfo*, IntPtr, IntPtr*, int>)GetInst(instance, "vkCreateDevice");
        DestroyInstance = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)GetInst(instance, "vkDestroyInstance");
        EnumeratePhysicalDevices = (delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr*, int>)GetInst(instance, "vkEnumeratePhysicalDevices");
        GetPhysicalDeviceQueueFamilyProperties = (delegate* unmanaged[Stdcall]<IntPtr, uint*, VkQueueFamilyProperties*, void>)GetInst(instance, "vkGetPhysicalDeviceQueueFamilyProperties");
        GetPhysicalDeviceSurfaceSupportKHR = (delegate* unmanaged[Stdcall]<IntPtr, uint, ulong, uint*, int>)GetInst(instance, "vkGetPhysicalDeviceSurfaceSupportKHR");
        GetPhysicalDeviceMemoryProperties = (delegate* unmanaged[Stdcall]<IntPtr, VkPhysicalDeviceMemoryProperties*, void>)GetInst(instance, "vkGetPhysicalDeviceMemoryProperties");
        GetPhysicalDeviceSurfaceCapabilitiesKHR = (delegate* unmanaged[Stdcall]<IntPtr, ulong, VkSurfaceCapabilitiesKHR*, int>)GetInst(instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        GetPhysicalDeviceSurfaceFormatsKHR = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint*, VkSurfaceFormatKHR*, int>)GetInst(instance, "vkGetPhysicalDeviceSurfaceFormatsKHR");
        GetDeviceProcAddr = (delegate* unmanaged[Stdcall]<IntPtr, byte*, IntPtr>)GetInst(instance, "vkGetDeviceProcAddr");
        return CreateDevice != null && EnumeratePhysicalDevices != null && GetDeviceProcAddr != null;
    }

    public bool LoadDeviceProcs(IntPtr device)
    {
        DestroyDevice = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)GetDev(device, "vkDestroyDevice");
        DeviceWaitIdle = (delegate* unmanaged[Stdcall]<IntPtr, int>)GetDev(device, "vkDeviceWaitIdle");
        GetDeviceQueue = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, void>)GetDev(device, "vkGetDeviceQueue");
        CreateSwapchainKHR = (delegate* unmanaged[Stdcall]<IntPtr, VkSwapchainCreateInfoKHR*, IntPtr, ulong*, int>)GetDev(device, "vkCreateSwapchainKHR");
        DestroySwapchainKHR = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkDestroySwapchainKHR");
        GetSwapchainImagesKHR = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint*, ulong*, int>)GetDev(device, "vkGetSwapchainImagesKHR");
        AcquireNextImageKHR = (delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, ulong, uint*, int>)GetDev(device, "vkAcquireNextImageKHR");
        QueuePresentKHR = (delegate* unmanaged[Stdcall]<IntPtr, VkPresentInfoKHR*, int>)GetDev(device, "vkQueuePresentKHR");
        QueueSubmit = (delegate* unmanaged[Stdcall]<IntPtr, uint, VkSubmitInfo*, ulong, int>)GetDev(device, "vkQueueSubmit");
        CreateCommandPool = (delegate* unmanaged[Stdcall]<IntPtr, VkCommandPoolCreateInfo*, IntPtr, ulong*, int>)GetDev(device, "vkCreateCommandPool");
        DestroyCommandPool = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkDestroyCommandPool");
        AllocateCommandBuffers = (delegate* unmanaged[Stdcall]<IntPtr, VkCommandBufferAllocateInfo*, IntPtr*, int>)GetDev(device, "vkAllocateCommandBuffers");
        CreateFence = (delegate* unmanaged[Stdcall]<IntPtr, VkFenceCreateInfo*, IntPtr, ulong*, int>)GetDev(device, "vkCreateFence");
        DestroyFence = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkDestroyFence");
        WaitForFences = (delegate* unmanaged[Stdcall]<IntPtr, uint, ulong*, uint, ulong, int>)GetDev(device, "vkWaitForFences");
        ResetFences = (delegate* unmanaged[Stdcall]<IntPtr, uint, ulong*, int>)GetDev(device, "vkResetFences");
        CreateSemaphore = (delegate* unmanaged[Stdcall]<IntPtr, VkSemaphoreCreateInfo*, IntPtr, ulong*, int>)GetDev(device, "vkCreateSemaphore");
        DestroySemaphore = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkDestroySemaphore");
        CreateBuffer = (delegate* unmanaged[Stdcall]<IntPtr, VkBufferCreateInfo*, IntPtr, ulong*, int>)GetDev(device, "vkCreateBuffer");
        DestroyBuffer = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkDestroyBuffer");
        GetBufferMemoryRequirements = (delegate* unmanaged[Stdcall]<IntPtr, ulong, VkMemoryRequirements*, void>)GetDev(device, "vkGetBufferMemoryRequirements");
        AllocateMemory = (delegate* unmanaged[Stdcall]<IntPtr, VkMemoryAllocateInfo*, IntPtr, ulong*, int>)GetDev(device, "vkAllocateMemory");
        FreeMemory = (delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, void>)GetDev(device, "vkFreeMemory");
        BindBufferMemory = (delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, int>)GetDev(device, "vkBindBufferMemory");
        MapMemory = (delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, ulong, uint, void**, int>)GetDev(device, "vkMapMemory");
        UnmapMemory = (delegate* unmanaged[Stdcall]<IntPtr, ulong, void>)GetDev(device, "vkUnmapMemory");
        ResetCommandBuffer = (delegate* unmanaged[Stdcall]<IntPtr, uint, int>)GetDev(device, "vkResetCommandBuffer");
        BeginCommandBuffer = (delegate* unmanaged[Stdcall]<IntPtr, VkCommandBufferBeginInfo*, int>)GetDev(device, "vkBeginCommandBuffer");
        EndCommandBuffer = (delegate* unmanaged[Stdcall]<IntPtr, int>)GetDev(device, "vkEndCommandBuffer");
        CmdPipelineBarrier = (delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, uint, VkImageMemoryBarrier*, void>)GetDev(device, "vkCmdPipelineBarrier");
        CmdCopyBufferToImage = (delegate* unmanaged[Stdcall]<IntPtr, ulong, ulong, uint, uint, VkBufferImageCopy*, void>)GetDev(device, "vkCmdCopyBufferToImage");
        return CreateSwapchainKHR != null && QueueSubmit != null && QueuePresentKHR != null;
    }

    public IntPtr GetInst(IntPtr instance, string name) { byte[] b = Encoding.ASCII.GetBytes(name + "\0"); fixed (byte* p = b) return GetInstanceProcAddr(instance, p); }
    private IntPtr GetDev(IntPtr device, string name) { byte[] b = Encoding.ASCII.GetBytes(name + "\0"); fixed (byte* p = b) return GetDeviceProcAddr(device, p); }
}
