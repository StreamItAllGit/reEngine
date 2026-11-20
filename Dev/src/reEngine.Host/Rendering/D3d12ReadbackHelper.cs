// @module: reEngine.Host.Rendering.D3d12ReadbackHelper
// @purpose: DXGI tearing capability/present-arg helpers and D3D12_HEAP_TYPE_READBACK CopyTextureRegion surface readback helper.
// @depends-on: System
// @exports: D3d12ReadbackHelper

namespace reEngine.Host.Rendering;

internal sealed unsafe class D3d12ReadbackHelper : IDisposable
{
    private IntPtr _readbackBuf;
    private ulong _readbackBytes;

    public static bool CheckAllowTearingSupport(IntPtr factory)
    {
        if (factory == IntPtr.Zero) return false;
        if (D3d12Native.QueryInterface(factory, in D3d12Native.IidIdxgiFactory5, out IntPtr factory5) < 0 || factory5 == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            int allowTearing = 0;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, int*, uint, int>)D3d12Native.GetVtblSlot(factory5, 28))(
                factory5, D3d12Native.DxgiFeaturePresentAllowTearing, &allowTearing, sizeof(int));
            return hr >= 0 && allowTearing != 0;
        }
        finally
        {
            D3d12Native.ReleaseCom(ref factory5);
        }
    }

    public static (uint SyncInterval, uint PresentFlags) ComputePresentArgs(
        bool vsyncEnabled,
        bool tearingSupported,
        bool isExclusiveFullscreen)
    {
        if (vsyncEnabled)
        {
            return (1u, 0u);
        }

        uint flags = tearingSupported && !isExclusiveFullscreen ? D3d12Native.DxgiPresentAllowTearing : 0u;
        return (0u, flags);
    }

    public bool TryReadBackBufferRect(
        IntPtr device,
        IntPtr queue,
        IntPtr cmdAlloc,
        IntPtr cmdList,
        IntPtr backBuffer,
        uint swapWidth,
        uint swapHeight,
        int x,
        int y,
        int width,
        int height,
        Action waitForGpuIdle,
        Span<byte> rgbaDestination)
    {
        if (device == IntPtr.Zero || queue == IntPtr.Zero || cmdAlloc == IntPtr.Zero || cmdList == IntPtr.Zero || backBuffer == IntPtr.Zero)
        {
            return false;
        }
        if (x < 0 || y < 0 || width <= 0 || height <= 0) return false;
        if (x > (int)swapWidth - width || y > (int)swapHeight - height) return false;
        long requiredBytes = (long)width * height * 4L;
        if (requiredBytes <= 0 || requiredBytes > rgbaDestination.Length) return false;

        uint rowPitch = D3d12Native.AlignPitch((uint)width * 4u);
        ulong neededBytes = (ulong)rowPitch * (uint)height;
        if (!EnsureReadbackBuffer(device, neededBytes)) return false;

        waitForGpuIdle();
        if (((delegate* unmanaged[Stdcall]<IntPtr, int>)D3d12Native.GetVtblSlot(cmdAlloc, 8))(cmdAlloc) < 0) return false;
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)D3d12Native.GetVtblSlot(cmdList, 10))(cmdList, cmdAlloc, IntPtr.Zero) < 0) return false;

        var toSrc = new D3d12ResourceBarrier
        {
            Transition = new D3d12ResourceTransitionBarrier
            {
                PResource = backBuffer,
                StateBefore = D3d12Native.ResourceStatePresent,
                StateAfter = D3d12Native.ResourceStateCopySource
            }
        };
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12ResourceBarrier*, void>)D3d12Native.GetVtblSlot(cmdList, 26))(cmdList, 1, &toSrc);

        var dst = new D3d12TextureCopyLocation
        {
            PResource = _readbackBuf,
            Type = 1,
            PlacedFootprint = new D3d12PlacedSubresourceFootprint
            {
                Offset = 0,
                Footprint = new D3d12SubresourceFootprint
                {
                    Format = D3d12Native.DxgiFormatR8G8B8A8Unorm,
                    Width = (uint)width,
                    Height = (uint)height,
                    Depth = 1,
                    RowPitch = rowPitch
                }
            }
        };
        var src = new D3d12TextureCopyLocation { PResource = backBuffer, Type = 0 };
        var box = new D3d12Box
        {
            Left = (uint)x,
            Top = (uint)y,
            Front = 0,
            Right = (uint)(x + width),
            Bottom = (uint)(y + height),
            Back = 1
        };
        ((delegate* unmanaged[Stdcall]<IntPtr, D3d12TextureCopyLocation*, uint, uint, uint, D3d12TextureCopyLocation*, D3d12Box*, void>)D3d12Native.GetVtblSlot(cmdList, 16))(
            cmdList, &dst, 0, 0, 0, &src, &box);

        var toPresent = new D3d12ResourceBarrier
        {
            Transition = new D3d12ResourceTransitionBarrier
            {
                PResource = backBuffer,
                StateBefore = D3d12Native.ResourceStateCopySource,
                StateAfter = D3d12Native.ResourceStatePresent
            }
        };
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12ResourceBarrier*, void>)D3d12Native.GetVtblSlot(cmdList, 26))(cmdList, 1, &toPresent);

        if (((delegate* unmanaged[Stdcall]<IntPtr, int>)D3d12Native.GetVtblSlot(cmdList, 9))(cmdList) < 0) return false;
        IntPtr listPtr = cmdList;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, void>)D3d12Native.GetVtblSlot(queue, 10))(queue, 1, &listPtr);
        waitForGpuIdle();

        var readRange = new D3d12Range { Begin = 0, End = (nuint)neededBytes };
        void* mapped = null;
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12Range*, void**, int>)D3d12Native.GetVtblSlot(_readbackBuf, 8))(_readbackBuf, 0, &readRange, &mapped) < 0 || mapped == null)
        {
            return false;
        }

        try
        {
            CopyPitchedRowsTopDown((byte*)mapped, (int)rowPitch, width, height, rgbaDestination);
            return true;
        }
        finally
        {
            var writeRange = new D3d12Range { Begin = 0, End = 0 };
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12Range*, void>)D3d12Native.GetVtblSlot(_readbackBuf, 9))(_readbackBuf, 0, &writeRange);
        }
    }

    public static void CopyPitchedRowsTopDown(byte* srcMapped, int srcRowPitch, int width, int height, Span<byte> dst)
    {
        if (srcMapped == null || width <= 0 || height <= 0 || srcRowPitch < width * 4 || dst.Length < checked(width * height * 4)) return;
        int dstRowBytes = width * 4;
        for (int row = 0; row < height; row++)
        {
            new ReadOnlySpan<byte>(srcMapped + (row * srcRowPitch), dstRowBytes).CopyTo(dst.Slice(row * dstRowBytes, dstRowBytes));
        }
    }

    public void Dispose()
    {
        D3d12Native.ReleaseCom(ref _readbackBuf);
        _readbackBytes = 0;
    }

    private bool EnsureReadbackBuffer(IntPtr device, ulong sizeBytes)
    {
        if (_readbackBuf != IntPtr.Zero && _readbackBytes >= sizeBytes) return true;
        Dispose();
        if (!D3d12Native.TryCreateCommittedBuffer(device, sizeBytes, D3d12Native.HeapTypeReadback, D3d12Native.ResourceStateCopyDest, out _readbackBuf))
        {
            return false;
        }
        _readbackBytes = sizeBytes;
        return true;
    }
}
