// @module: reEngine.Host.Rendering.VulkanReadbackHelper
// @purpose: Vulkan swapchain present-mode selector and owner-thread staging readback helper (image-to-buffer copy + BGRA->RGBA unswizzle).
// @depends-on: System, reEngine.Host.Contracts
// @exports: VulkanReadbackHelper

namespace reEngine.Host.Rendering;

internal sealed unsafe class VulkanReadbackHelper
{
    private ulong _readBuf;
    private ulong _readMem;
    private void* _readMapped;
    private ulong _readSize;

    public static uint SelectPresentMode(ReadOnlySpan<uint> supportedModes, bool vsyncEnabled)
    {
        if (vsyncEnabled)
        {
            return VulkanNative.VkPresentModeFifoKhr;
        }

        bool supportsImmediate = false;
        foreach (uint mode in supportedModes)
        {
            if (mode == VulkanNative.VkPresentModeMailboxKhr) return VulkanNative.VkPresentModeMailboxKhr;
            if (mode == VulkanNative.VkPresentModeImmediateKhr) supportsImmediate = true;
        }

        return supportsImmediate ? VulkanNative.VkPresentModeImmediateKhr : VulkanNative.VkPresentModeFifoKhr;
    }

    public bool TryReadSwapchainImageRect(
        in VkDispatch vk,
        IntPtr physicalDevice,
        IntPtr device,
        IntPtr queue,
        IntPtr cmdBuffer,
        ulong fence,
        ulong image,
        uint swapWidth,
        uint swapHeight,
        uint swapFormat,
        bool swapSupportsTransferSrc,
        void* fallbackStagingMapped,
        ulong fallbackStagingBytes,
        int x,
        int y,
        int width,
        int height,
        Span<byte> rgbaDestination)
    {
        if (device == IntPtr.Zero || x < 0 || y < 0 || width <= 0 || height <= 0) return false;
        if (x > (int)swapWidth - width || y > (int)swapHeight - height) return false;

        long requiredBytes = (long)width * height * 4L;
        if (requiredBytes <= 0 || requiredBytes > rgbaDestination.Length) return false;

        bool swapRb = swapFormat == VulkanNative.VkFormatB8G8R8A8Unorm;
        if (swapSupportsTransferSrc && image != 0 && queue != IntPtr.Zero && cmdBuffer != IntPtr.Zero &&
            vk.CmdCopyImageToBuffer != null && EnsureReadbackBuffer(in vk, physicalDevice, device, (ulong)requiredBytes))
        {
            if (ExecuteImageToBufferCopy(in vk, device, queue, cmdBuffer, fence, image, x, y, width, height))
            {
                CopyPixelsTopDown((byte*)_readMapped, width * 4, 0, 0, width, height, swapRb, rgbaDestination);
                return true;
            }
        }

        ulong minFallbackBytes = (ulong)swapWidth * swapHeight * 4UL;
        if (fallbackStagingMapped != null && fallbackStagingBytes >= minFallbackBytes)
        {
            CopyPixelsTopDown((byte*)fallbackStagingMapped, (int)swapWidth * 4, x, y, width, height, swapRb, rgbaDestination);
            return true;
        }

        return false;
    }

    public static void CopyPixelsTopDown(
        byte* srcBase,
        int srcRowBytes,
        int srcX,
        int srcY,
        int width,
        int height,
        bool swapRb,
        Span<byte> dst)
    {
        if (srcBase == null || width <= 0 || height <= 0 || dst.Length < checked(width * height * 4)) return;
        int dstRowBytes = width * 4;
        for (int row = 0; row < height; row++)
        {
            byte* sRow = srcBase + ((srcY + row) * srcRowBytes) + (srcX * 4);
            int dOff = row * dstRowBytes;
            if (!swapRb)
            {
                new ReadOnlySpan<byte>(sRow, dstRowBytes).CopyTo(dst.Slice(dOff, dstRowBytes));
                continue;
            }

            for (int col = 0; col < width; col++)
            {
                int s = col * 4;
                int d = dOff + s;
                dst[d] = sRow[s + 2];
                dst[d + 1] = sRow[s + 1];
                dst[d + 2] = sRow[s];
                dst[d + 3] = sRow[s + 3];
            }
        }
    }

    public void Dispose(in VkDispatch vk, IntPtr device)
    {
        if (device == IntPtr.Zero) return;
        if (_readMapped != null && vk.UnmapMemory != null) { vk.UnmapMemory(device, _readMem); _readMapped = null; }
        if (_readBuf != 0 && vk.DestroyBuffer != null) { vk.DestroyBuffer(device, _readBuf, IntPtr.Zero); _readBuf = 0; }
        if (_readMem != 0 && vk.FreeMemory != null) { vk.FreeMemory(device, _readMem, IntPtr.Zero); _readMem = 0; }
        _readSize = 0;
    }

    private bool EnsureReadbackBuffer(in VkDispatch vk, IntPtr physicalDevice, IntPtr device, ulong sizeBytes)
    {
        if (_readBuf != 0 && _readSize >= sizeBytes) return true;
        Dispose(in vk, device);
        if (!VulkanNative.TryCreateBuffer(
                in vk, physicalDevice, device, sizeBytes, VulkanNative.VkBufferUsageTransferDstBit,
                out _readBuf, out _readMem, out _readMapped))
        {
            return false;
        }
        _readSize = sizeBytes;
        return true;
    }

    private bool ExecuteImageToBufferCopy(
        in VkDispatch vk,
        IntPtr device,
        IntPtr queue,
        IntPtr cmdBuffer,
        ulong fence,
        ulong image,
        int x,
        int y,
        int width,
        int height)
    {
        _ = vk.DeviceWaitIdle(device);
        ulong f = fence;
        _ = vk.WaitForFences(device, 1, &f, 1, ulong.MaxValue);
        _ = vk.ResetFences(device, 1, &f);
        _ = vk.ResetCommandBuffer(cmdBuffer, 0);

        var begin = new VkCommandBufferBeginInfo { SType = 42u, Flags = VulkanNative.VkCommandBufferOneTimeSubmitBit };
        if (vk.BeginCommandBuffer(cmdBuffer, &begin) != VulkanNative.VkSuccess) return false;

        var range = new VkImageSubresourceRange { AspectMask = 1, LevelCount = 1, LayerCount = 1 };
        var toSrc = new VkImageMemoryBarrier
        {
            SType = 45u,
            DstAccessMask = VulkanNative.VkAccessTransferReadBit,
            OldLayout = VulkanNative.VkImageLayoutPresentSrcKhr,
            NewLayout = VulkanNative.VkImageLayoutTransferSrcOptimal,
            SrcQueueFamilyIndex = VulkanNative.VkQueueFamilyIgnored,
            DstQueueFamilyIndex = VulkanNative.VkQueueFamilyIgnored,
            Image = image,
            SubresourceRange = range
        };
        vk.CmdPipelineBarrier(cmdBuffer, VulkanNative.VkPipelineStageTopOfPipeBit, VulkanNative.VkPipelineStageTransferBit, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &toSrc);

        var copy = new VkBufferImageCopy
        {
            ImageSubresource = new VkImageSubresourceLayers { AspectMask = 1, LayerCount = 1 },
            OffsetX = x,
            OffsetY = y,
            ExtentWidth = (uint)width,
            ExtentHeight = (uint)height,
            ExtentDepth = 1
        };
        vk.CmdCopyImageToBuffer(cmdBuffer, image, VulkanNative.VkImageLayoutTransferSrcOptimal, _readBuf, 1, &copy);

        var toPresent = new VkImageMemoryBarrier
        {
            SType = 45u,
            SrcAccessMask = VulkanNative.VkAccessTransferReadBit,
            OldLayout = VulkanNative.VkImageLayoutTransferSrcOptimal,
            NewLayout = VulkanNative.VkImageLayoutPresentSrcKhr,
            SrcQueueFamilyIndex = VulkanNative.VkQueueFamilyIgnored,
            DstQueueFamilyIndex = VulkanNative.VkQueueFamilyIgnored,
            Image = image,
            SubresourceRange = range
        };
        vk.CmdPipelineBarrier(cmdBuffer, VulkanNative.VkPipelineStageTransferBit, VulkanNative.VkPipelineStageBottomOfPipeBit, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &toPresent);

        if (vk.EndCommandBuffer(cmdBuffer) != VulkanNative.VkSuccess) return false;

        IntPtr cmd = cmdBuffer;
        var submit = new VkSubmitInfo { SType = 4u, CommandBufferCount = 1, PCommandBuffers = &cmd };
        if (vk.QueueSubmit(queue, 1, &submit, fence) != VulkanNative.VkSuccess) return false;
        return vk.WaitForFences(device, 1, &f, 1, ulong.MaxValue) == VulkanNative.VkSuccess;
    }
}
