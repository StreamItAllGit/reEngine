// @module: reEngine.Host.Rendering.D3d12Native
// @purpose: Clean-room Windows DirectX 12 and DXGI 1.4 C-ABI structs, IIDs, COM vtable helpers, and upload staging helpers.
// @depends-on: System, System.Runtime.InteropServices
// @exports: D3d12Native

using System.Runtime.InteropServices;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

internal static unsafe class D3d12Native
{
    public const uint FeatureLevel11_0 = 0xB000u;
    public const uint DxgiFormatR8G8B8A8Unorm = 28u;
    public const uint DxgiUsageRenderTargetOutput = 0x20u;
    public const uint DxgiSwapEffectFlipDiscard = 4u;
    public const uint DxgiScalingStretch = 0u;
    public const uint DxgiMwaNoAltEnter = 2u;
    public const uint DxgiSwapChainFlagAllowTearing = 0x800u;
    public const uint DxgiPresentAllowTearing = 0x200u;
    public const uint DxgiFeaturePresentAllowTearing = 0u;
    public const int ResourceStatePresent = 0;
    public const int ResourceStateCopyDest = 0x400;
    public const int ResourceStateCopySource = 0x800;
    public const int ResourceStateGenericRead = 0x1 | 0x2 | 0x40 | 0x80 | 0x200 | 0x800;
    public const int HeapTypeUpload = 2;
    public const int HeapTypeReadback = 3;
    public const int ResourceDimensionBuffer = 1;
    public const int TextureLayoutRowMajor = 1;
    public const int CommandListTypeDirect = 0;
    public const uint TextureDataPitchAlignment = 256u;

    public static readonly Guid IidId3d12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    public static readonly Guid IidId3d12CommandQueue = new("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    public static readonly Guid IidId3d12CommandAllocator = new("6102dee4-af59-4b09-b999-b44d73f09b24");
    public static readonly Guid IidId3d12GraphicsCommandList = new("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
    public static readonly Guid IidId3d12Resource = new("696442be-a72e-4059-bc79-5b5c98040fad");
    public static readonly Guid IidId3d12Fence = new("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
    public static readonly Guid IidIdxgiFactory4 = new("1bc6ea02-ef36-464f-bf0c-21ca39e5168a");
    public static readonly Guid IidIdxgiFactory5 = new("7632e1f5-ee65-4dca-87fd-84cd75f8838d");
    public static readonly Guid IidIdxgiAdapter1 = new("29038f61-3839-4626-91fd-086879011a05");
    public static readonly Guid IidIdxgiSwapChain3 = new("94d99bdb-f1f8-4ab0-b236-7da0170edab1");

    [DllImport("d3d12.dll", ExactSpelling = true)]
    public static extern int D3D12CreateDevice(IntPtr pAdapter, uint minimumFeatureLevel, in Guid riid, out IntPtr ppDevice);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    public static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, int bManualReset, int bInitialState, IntPtr lpName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int CloseHandle(IntPtr hObject);

    public static nint GetVtblSlot(IntPtr comPtr, int slot) => (*(nint**)comPtr)[slot];

    public static void ReleaseCom(ref IntPtr comPtr)
    {
        if (comPtr == IntPtr.Zero) return;
        IntPtr p = comPtr;
        comPtr = IntPtr.Zero;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, uint>)GetVtblSlot(p, 2))(p);
    }

    public static int QueryInterface(IntPtr comPtr, in Guid iid, out IntPtr result)
    {
        Guid g = iid;
        IntPtr r = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)GetVtblSlot(comPtr, 0))(comPtr, &g, &r);
        result = r;
        return hr;
    }

    public static uint AlignPitch(uint rowBytes) => (rowBytes + TextureDataPitchAlignment - 1) & ~(TextureDataPitchAlignment - 1);

    public static bool TryCreateDeviceWithWarpFallback(IntPtr factory, out IntPtr device, out string diagnostic)
    {
        int hr = D3D12CreateDevice(IntPtr.Zero, FeatureLevel11_0, in IidId3d12Device, out device);
        if (hr >= 0 && device != IntPtr.Zero) { diagnostic = string.Empty; return true; }

        Guid iidAdapter = IidIdxgiAdapter1;
        IntPtr warp = IntPtr.Zero;
        int warpHr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)GetVtblSlot(factory, 27))(factory, &iidAdapter, &warp);
        if (warpHr >= 0 && warp != IntPtr.Zero)
        {
            hr = D3D12CreateDevice(warp, FeatureLevel11_0, in IidId3d12Device, out device);
            ReleaseCom(ref warp);
            if (hr >= 0 && device != IntPtr.Zero) { diagnostic = string.Empty; return true; }
        }
        diagnostic = $"D3D12CreateDevice failed (HRESULT=0x{hr:X8}).";
        return false;
    }

    public static bool TryCreateCommittedBuffer(IntPtr device, ulong sizeBytes, int heapType, int initialState, out IntPtr resource)
    {
        resource = IntPtr.Zero;
        var heap = new D3d12HeapProperties { Type = heapType, CreationNodeMask = 1, VisibleNodeMask = 1 };
        var desc = new D3d12ResourceDesc
        {
            Dimension = ResourceDimensionBuffer, Width = sizeBytes, Height = 1, DepthOrArraySize = 1, MipLevels = 1,
            SampleDesc = new DxgiSampleDesc { Count = 1, Quality = 0 }, Layout = TextureLayoutRowMajor
        };
        Guid iidRes = IidId3d12Resource;
        IntPtr res = IntPtr.Zero;
        if (((delegate* unmanaged[Stdcall]<IntPtr, D3d12HeapProperties*, int, D3d12ResourceDesc*, int, IntPtr, Guid*, IntPtr*, int>)GetVtblSlot(device, 27))
            (device, &heap, 0, &desc, initialState, IntPtr.Zero, &iidRes, &res) < 0 || res == IntPtr.Zero)
        {
            return false;
        }
        resource = res;
        return true;
    }

    public static bool TryCreateMappedUploadBuffer(IntPtr device, ulong sizeBytes, out IntPtr resource, out void* mapped)
    {
        mapped = null;
        if (!TryCreateCommittedBuffer(device, sizeBytes, HeapTypeUpload, ResourceStateGenericRead, out resource))
        {
            return false;
        }
        var readRange = new D3d12Range { Begin = 0, End = 0 };
        void* m = null;
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12Range*, void**, int>)GetVtblSlot(resource, 8))(resource, 0, &readRange, &m) < 0 || m == null)
        {
            ReleaseCom(ref resource);
            return false;
        }
        mapped = m;
        return true;
    }

    public static void CopyRgbaToUpload(int srcW, int srcH, ReadOnlySpan<byte> rgba, byte* dst, int dstW, int dstH, int dstPitch) =>
        CopyRgbaToUpload(srcW, srcH, rgba, dst, dstW, dstH, dstPitch, new HostViewportRect(0, 0, dstW, dstH), new HostViewportRect(0, 0, srcW, srcH));

    internal static void CopyRgbaToUploadSpan(
        int srcW, int srcH, ReadOnlySpan<byte> rgba, Span<byte> dst, int dstW, int dstH, int dstPitch, HostViewportRect destRect, HostViewportRect srcCropRect)
    {
        if (dst.Length < checked(dstPitch * dstH)) return;
        fixed (byte* pDst = dst) CopyRgbaToUpload(srcW, srcH, rgba, pDst, dstW, dstH, dstPitch, destRect, srcCropRect);
    }

    public static void CopyRgbaToUpload(
        int srcW,
        int srcH,
        ReadOnlySpan<byte> rgba,
        byte* dst,
        int dstW,
        int dstH,
        int dstPitch,
        HostViewportRect destRect,
        HostViewportRect srcCropRect)
    {
        if (dst == null || srcW <= 0 || srcH <= 0 || dstW <= 0 || dstH <= 0 || dstPitch < dstW * 4 || rgba.Length < checked(srcW * srcH * 4)) return;

        HostViewportRect src = HostViewportGeometry.ClampSourceRect(srcW, srcH, srcCropRect);
        fixed (byte* srcPtr = rgba)
        {
            if (destRect.X == 0 && destRect.Y == 0 && destRect.Width == dstW && destRect.Height == dstH &&
                src.X == 0 && src.Y == 0 && src.Width == srcW && src.Height == srcH && srcW == dstW && srcH == dstH)
            {
                for (int y = 0; y < dstH; y++) Buffer.MemoryCopy(srcPtr + (y * srcW * 4), dst + (y * dstPitch), dstPitch, (long)dstW * 4);
                return;
            }

            ClearUploadSurface0(dst, dstW, dstH, dstPitch);
            if (destRect.Width <= 0 || destRect.Height <= 0) return;

            int dyStart = Math.Max(0, -destRect.Y);
            int dyEnd = Math.Min(destRect.Height, dstH - destRect.Y);
            int dxStart = Math.Max(0, -destRect.X);
            int dxEnd = Math.Min(destRect.Width, dstW - destRect.X);
            if (dyStart >= dyEnd || dxStart >= dxEnd) return;

            for (int dy = dyStart; dy < dyEnd; dy++)
            {
                int sy = src.Y + (int)((long)dy * src.Height / destRect.Height);
                byte* dRow = dst + ((destRect.Y + dy) * dstPitch);
                byte* sRow = srcPtr + (sy * srcW * 4);
                for (int dx = dxStart; dx < dxEnd; dx++)
                {
                    int sx = src.X + (int)((long)dx * src.Width / destRect.Width);
                    int sIdx = sx * 4;
                    int dIdx = (destRect.X + dx) * 4;
                    dRow[dIdx] = sRow[sIdx];
                    dRow[dIdx + 1] = sRow[sIdx + 1];
                    dRow[dIdx + 2] = sRow[sIdx + 2];
                    dRow[dIdx + 3] = 255;
                }
            }
        }
    }

    private static void ClearUploadSurface0(byte* dst, int dstW, int dstH, int dstPitch)
    {
        if (BitConverter.IsLittleEndian)
        {
            for (int y = 0; y < dstH; y++)
            {
                new Span<uint>(dst + (y * dstPitch), dstW).Fill(0xFF100E0Eu);
            }
            return;
        }

        for (int y = 0; y < dstH; y++)
        {
            byte* row = dst + (y * dstPitch);
            for (int x = 0; x < dstW; x++)
            {
                int off = x * 4;
                row[off] = HostViewportGeometry.Surface0R;
                row[off + 1] = HostViewportGeometry.Surface0G;
                row[off + 2] = HostViewportGeometry.Surface0B;
                row[off + 3] = HostViewportGeometry.Surface0A;
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential)] internal struct DxgiSampleDesc { public uint Count, Quality; }
[StructLayout(LayoutKind.Sequential)] internal struct DxgiSwapChainDesc1 { public uint Width, Height, Format; public int Stereo; public DxgiSampleDesc SampleDesc; public uint BufferUsage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12CommandQueueDesc { public int Type, Priority, Flags; public uint NodeMask; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12HeapProperties { public int Type, CpuPageProperty, MemoryPoolPreference; public uint CreationNodeMask, VisibleNodeMask; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12ResourceDesc { public int Dimension; public ulong Alignment, Width; public uint Height; public ushort DepthOrArraySize, MipLevels; public uint Format; public DxgiSampleDesc SampleDesc; public int Layout, Flags; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12SubresourceFootprint { public uint Format, Width, Height, Depth, RowPitch; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12PlacedSubresourceFootprint { public ulong Offset; public D3d12SubresourceFootprint Footprint; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12TextureCopyLocation { public IntPtr PResource; public int Type; public D3d12PlacedSubresourceFootprint PlacedFootprint; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12ResourceTransitionBarrier { public IntPtr PResource; public uint Subresource; public int StateBefore, StateAfter; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12ResourceBarrier { public int Type, Flags; public D3d12ResourceTransitionBarrier Transition; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12Range { public nuint Begin, End; }
[StructLayout(LayoutKind.Sequential)] internal struct D3d12Box { public uint Left, Top, Front, Right, Bottom, Back; }
