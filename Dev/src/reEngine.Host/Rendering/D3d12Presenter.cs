// @module: reEngine.Host.Rendering.D3d12Presenter
// @purpose: Clean-room Windows DirectX 12 + IDXGISwapChain3 presenter with upload heap staging and clean COM teardown.
// @depends-on: System, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: D3d12Presenter

using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

/// <summary>
/// Windows-only DirectX 12 presenter creating <c>ID3D12Device</c> and <c>IDXGISwapChain3</c> over an SDL3 Win32 HWND.
/// </summary>
public sealed unsafe class D3d12Presenter : IHostSurfacePresenter
{
    private const int BufferCount = 2;

    private readonly IntPtr[] _backBuffers = new IntPtr[BufferCount];
    private readonly D3d12ReadbackHelper _readback = new();
    private IntPtr _sdlWindow, _hwnd, _factory, _device, _queue, _cmdAlloc, _cmdList, _swapChain, _uploadBuf, _fence, _fenceEvent;
    private void* _uploadMapped;
    private ulong _uploadBytes, _fenceValue;
    private uint _swapWidth, _swapHeight, _lastBackBufferIdx;
    private int _ownerThreadId;
    private HostViewportRect _lastPresentedDestRect;
    private bool _vsyncEnabled, _tearingSupported, _hasPresentedFrame, _disposed;
    private D3d12Context? _graphicsContext;

    /// <inheritdoc />
    public HostRenderBackendKind BackendKind => HostRenderBackendKind.DirectX;

    /// <inheritdoc />
    public string BackendId => HostRenderBackendIds.DirectX;

    /// <inheritdoc />
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;

    /// <inheritdoc />
    public bool TryInitialize(IntPtr sdlWindow, int width, int height, out string diagnostic)
    {
        if (!OperatingSystem.IsWindows()) { diagnostic = "DirectX 12 is only supported on Windows."; return false; }
        _sdlWindow = sdlWindow;
        _hwnd = Sdl3Native.GetWin32Hwnd(sdlWindow);
        if (_hwnd == IntPtr.Zero) { diagnostic = "SDL window has no Win32 HWND."; return false; }

        try
        {
            if (D3d12Native.CreateDXGIFactory1(in D3d12Native.IidIdxgiFactory4, out _factory) < 0 || _factory == IntPtr.Zero)
            {
                diagnostic = "CreateDXGIFactory1(IDXGIFactory4) failed."; Dispose(); return false;
            }
            if (!D3d12Native.TryCreateDeviceWithWarpFallback(_factory, out _device, out diagnostic) ||
                !CreateQueueAndCommandObjects(out diagnostic) ||
                !CreateSwapChain((uint)Math.Max(1, width), (uint)Math.Max(1, height), out diagnostic) ||
                !CreateFence(out diagnostic))
            {
                Dispose(); return false;
            }
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _graphicsContext = new D3d12Context(this, sdlWindow, _device, _queue, _factory, _swapChain);
            diagnostic = $"DirectX 12 initialized ({_swapWidth}x{_swapHeight}).";
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            diagnostic = $"DirectX 12 runtime unavailable: {ex.Message}";
            Dispose();
            return false;
        }
    }

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba) =>
        PresentRgbaCore(width, height, rgba, customDestRect: null, customSrcCrop: null);

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostViewportRect destRect, HostViewportRect srcCropRect) =>
        PresentRgbaCore(width, height, rgba, destRect, srcCropRect);

    private void PresentRgbaCore(
        int width,
        int height,
        ReadOnlySpan<byte> rgba,
        HostViewportRect? customDestRect,
        HostViewportRect? customSrcCrop)
    {
        if (_disposed || _device == IntPtr.Zero || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;
        if (!HostViewportGeometry.TryGetWindowPixelSize(_sdlWindow, width, height, out int winW, out int winH)) return;

        if ((uint)winW != _swapWidth || (uint)winH != _swapHeight)
        {
            if (!ResizeSwapChain((uint)winW, (uint)winH)) return;
        }

        uint pitch = D3d12Native.AlignPitch(_swapWidth * 4);
        ulong neededBytes = (ulong)pitch * _swapHeight;
        if (!EnsureUploadBuffer(neededBytes)) return;
        HostViewportRect destRect = customDestRect ?? HostViewportGeometry.ComputeDestinationRect(width, height, (int)_swapWidth, (int)_swapHeight, HostAspectRatioMode.NativeFit);
        HostViewportRect srcCrop = customSrcCrop ?? new HostViewportRect(0, 0, width, height);
        _lastPresentedDestRect = HostViewportGeometry.ClampDestinationRect((int)_swapWidth, (int)_swapHeight, destRect);
        D3d12Native.CopyRgbaToUpload(width, height, rgba, (byte*)_uploadMapped, (int)_swapWidth, (int)_swapHeight, (int)pitch, destRect, srcCrop);

        uint bbIdx = ((delegate* unmanaged[Stdcall]<IntPtr, uint>)D3d12Native.GetVtblSlot(_swapChain, 36))(_swapChain);
        IntPtr backBuffer = _backBuffers[bbIdx];
        if (backBuffer == IntPtr.Zero) return;

        _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)D3d12Native.GetVtblSlot(_cmdAlloc, 8))(_cmdAlloc);
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)D3d12Native.GetVtblSlot(_cmdList, 10))(_cmdList, _cmdAlloc, IntPtr.Zero);
        RecordCopyAndPresent(backBuffer, pitch);
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)D3d12Native.GetVtblSlot(_cmdList, 9))(_cmdList);

        IntPtr listPtr = _cmdList;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, void>)D3d12Native.GetVtblSlot(_queue, 10))(_queue, 1, &listPtr);
        int isFs = 0;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, int*, IntPtr*, int>)D3d12Native.GetVtblSlot(_swapChain, 11))(_swapChain, &isFs, null);
        (uint syncInterval, uint presentFlags) = D3d12ReadbackHelper.ComputePresentArgs(_vsyncEnabled, _tearingSupported, isFs != 0);
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)D3d12Native.GetVtblSlot(_swapChain, 8))(_swapChain, syncInterval, presentFlags) >= 0)
        {
            _lastBackBufferIdx = bbIdx;
            _hasPresentedFrame = true;
        }
        WaitForGpuIdle();
    }

    internal int OwnerThreadId => _ownerThreadId;
    internal HostViewportRect LastPresentedDestRect => _lastPresentedDestRect;

    internal void SetVSyncEnabled(bool enabled)
    {
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return;
        _vsyncEnabled = enabled;
    }

    internal void FlushAndSyncForSnapshot()
    {
        if (_disposed || _device == IntPtr.Zero) return;
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return;
        WaitForGpuIdle();
    }

    internal bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination)
    {
        if (_disposed || _device == IntPtr.Zero || !_hasPresentedFrame || _lastBackBufferIdx >= BufferCount) return false;
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return false;
        return _readback.TryReadBackBufferRect(
            _device, _queue, _cmdAlloc, _cmdList, _backBuffers[_lastBackBufferIdx],
            _swapWidth, _swapHeight, x, y, width, height, WaitForGpuIdle, rgbaDestination);
    }

    private bool CreateQueueAndCommandObjects(out string diagnostic)
    {
        var qDesc = new D3d12CommandQueueDesc { Type = D3d12Native.CommandListTypeDirect };
        Guid iidQ = D3d12Native.IidId3d12CommandQueue, iidAlloc = D3d12Native.IidId3d12CommandAllocator, iidList = D3d12Native.IidId3d12GraphicsCommandList;
        IntPtr q = IntPtr.Zero, alloc = IntPtr.Zero, list = IntPtr.Zero;
        if (((delegate* unmanaged[Stdcall]<IntPtr, D3d12CommandQueueDesc*, Guid*, IntPtr*, int>)D3d12Native.GetVtblSlot(_device, 8))(_device, &qDesc, &iidQ, &q) < 0 ||
            ((delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, IntPtr*, int>)D3d12Native.GetVtblSlot(_device, 9))(_device, D3d12Native.CommandListTypeDirect, &iidAlloc, &alloc) < 0 ||
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, int, IntPtr, IntPtr, Guid*, IntPtr*, int>)D3d12Native.GetVtblSlot(_device, 12))(_device, 0, D3d12Native.CommandListTypeDirect, alloc, IntPtr.Zero, &iidList, &list) < 0)
        {
            D3d12Native.ReleaseCom(ref list); D3d12Native.ReleaseCom(ref alloc); D3d12Native.ReleaseCom(ref q);
            diagnostic = "Failed creating D3D12 command queue/allocator/list.";
            return false;
        }
        _queue = q; _cmdAlloc = alloc; _cmdList = list;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, int>)D3d12Native.GetVtblSlot(_cmdList, 9))(_cmdList);
        diagnostic = string.Empty;
        return true;
    }

    private bool CreateSwapChain(uint width, uint height, out string diagnostic)
    {
        _tearingSupported = D3d12ReadbackHelper.CheckAllowTearingSupport(_factory);
        var scDesc = new DxgiSwapChainDesc1
        {
            Width = width, Height = height, Format = D3d12Native.DxgiFormatR8G8B8A8Unorm,
            SampleDesc = new DxgiSampleDesc { Count = 1, Quality = 0 }, BufferUsage = D3d12Native.DxgiUsageRenderTargetOutput,
            BufferCount = BufferCount, Scaling = D3d12Native.DxgiScalingStretch, SwapEffect = D3d12Native.DxgiSwapEffectFlipDiscard,
            Flags = _tearingSupported ? D3d12Native.DxgiSwapChainFlagAllowTearing : 0u
        };
        IntPtr sc1 = IntPtr.Zero;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, DxgiSwapChainDesc1*, IntPtr, IntPtr, IntPtr*, int>)D3d12Native.GetVtblSlot(_factory, 15))
            (_factory, _queue, _hwnd, &scDesc, IntPtr.Zero, IntPtr.Zero, &sc1);
        if (hr < 0 || sc1 == IntPtr.Zero) { diagnostic = $"CreateSwapChainForHwnd failed (HRESULT=0x{hr:X8})."; return false; }

        _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)D3d12Native.GetVtblSlot(_factory, 8))(_factory, _hwnd, D3d12Native.DxgiMwaNoAltEnter);
        int qi = D3d12Native.QueryInterface(sc1, in D3d12Native.IidIdxgiSwapChain3, out _swapChain);
        D3d12Native.ReleaseCom(ref sc1);
        if (qi < 0 || _swapChain == IntPtr.Zero) { diagnostic = $"QueryInterface(IDXGISwapChain3) failed (0x{qi:X8})."; return false; }

        _swapWidth = width; _swapHeight = height;
        diagnostic = AcquireBackBuffers() ? string.Empty : "Failed acquiring DXGI swapchain backbuffers.";
        return string.IsNullOrEmpty(diagnostic);
    }

    private bool ResizeSwapChain(uint width, uint height)
    {
        WaitForGpuIdle();
        ReleaseBackBuffers();
        _hasPresentedFrame = false;
        uint flags = _tearingSupported ? D3d12Native.DxgiSwapChainFlagAllowTearing : 0u;
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, uint, int>)D3d12Native.GetVtblSlot(_swapChain, 13))
            (_swapChain, BufferCount, width, height, D3d12Native.DxgiFormatR8G8B8A8Unorm, flags) < 0) return false;
        _swapWidth = width; _swapHeight = height;
        return AcquireBackBuffers();
    }

    private bool AcquireBackBuffers()
    {
        Guid iidRes = D3d12Native.IidId3d12Resource;
        for (uint i = 0; i < BufferCount; i++)
        {
            IntPtr buf = IntPtr.Zero;
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)D3d12Native.GetVtblSlot(_swapChain, 9))(_swapChain, i, &iidRes, &buf) < 0 || buf == IntPtr.Zero) return false;
            _backBuffers[i] = buf;
        }
        return true;
    }

    private bool CreateFence(out string diagnostic)
    {
        Guid iidFence = D3d12Native.IidId3d12Fence;
        IntPtr f = IntPtr.Zero;
        if (((delegate* unmanaged[Stdcall]<IntPtr, ulong, int, Guid*, IntPtr*, int>)D3d12Native.GetVtblSlot(_device, 36))(_device, 0, 0, &iidFence, &f) < 0 || f == IntPtr.Zero)
        {
            diagnostic = "CreateFence failed."; return false;
        }
        _fence = f;
        _fenceEvent = D3d12Native.CreateEventW(IntPtr.Zero, 0, 0, IntPtr.Zero);
        diagnostic = _fenceEvent != IntPtr.Zero ? string.Empty : "CreateEventW failed.";
        return _fenceEvent != IntPtr.Zero;
    }

    private bool EnsureUploadBuffer(ulong sizeBytes)
    {
        if (_uploadBuf != IntPtr.Zero && _uploadBytes >= sizeBytes) return true;
        ReleaseUploadBuffer();
        if (!D3d12Native.TryCreateMappedUploadBuffer(_device, sizeBytes, out _uploadBuf, out _uploadMapped)) return false;
        _uploadBytes = sizeBytes;
        return true;
    }

    private void RecordCopyAndPresent(IntPtr backBuffer, uint pitch)
    {
        var toCopy = new D3d12ResourceBarrier { Transition = new D3d12ResourceTransitionBarrier { PResource = backBuffer, StateBefore = D3d12Native.ResourceStatePresent, StateAfter = D3d12Native.ResourceStateCopyDest } };
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12ResourceBarrier*, void>)D3d12Native.GetVtblSlot(_cmdList, 26))(_cmdList, 1, &toCopy);

        var dst = new D3d12TextureCopyLocation { PResource = backBuffer, Type = 0 };
        var src = new D3d12TextureCopyLocation
        {
            PResource = _uploadBuf, Type = 1,
            PlacedFootprint = new D3d12PlacedSubresourceFootprint { Offset = 0, Footprint = new D3d12SubresourceFootprint { Format = D3d12Native.DxgiFormatR8G8B8A8Unorm, Width = _swapWidth, Height = _swapHeight, Depth = 1, RowPitch = pitch } }
        };
        var box = new D3d12Box { Right = _swapWidth, Bottom = _swapHeight, Back = 1 };
        ((delegate* unmanaged[Stdcall]<IntPtr, D3d12TextureCopyLocation*, uint, uint, uint, D3d12TextureCopyLocation*, D3d12Box*, void>)D3d12Native.GetVtblSlot(_cmdList, 16))(_cmdList, &dst, 0, 0, 0, &src, &box);

        var toPresent = new D3d12ResourceBarrier { Transition = new D3d12ResourceTransitionBarrier { PResource = backBuffer, StateBefore = D3d12Native.ResourceStateCopyDest, StateAfter = D3d12Native.ResourceStatePresent } };
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12ResourceBarrier*, void>)D3d12Native.GetVtblSlot(_cmdList, 26))(_cmdList, 1, &toPresent);
    }

    private void WaitForGpuIdle()
    {
        if (_queue == IntPtr.Zero || _fence == IntPtr.Zero) return;
        ulong target = ++_fenceValue;
        _ = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, int>)D3d12Native.GetVtblSlot(_queue, 14))(_queue, _fence, target);
        if (((delegate* unmanaged[Stdcall]<IntPtr, ulong>)D3d12Native.GetVtblSlot(_fence, 8))(_fence) < target)
        {
            _ = ((delegate* unmanaged[Stdcall]<IntPtr, ulong, IntPtr, int>)D3d12Native.GetVtblSlot(_fence, 9))(_fence, target, _fenceEvent);
            _ = D3d12Native.WaitForSingleObject(_fenceEvent, 5000);
        }
    }

    private void ReleaseBackBuffers() { for (int i = 0; i < _backBuffers.Length; i++) D3d12Native.ReleaseCom(ref _backBuffers[i]); }

    private void ReleaseUploadBuffer()
    {
        if (_uploadBuf != IntPtr.Zero && _uploadMapped != null)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, D3d12Range*, void>)D3d12Native.GetVtblSlot(_uploadBuf, 9))(_uploadBuf, 0, null);
            _uploadMapped = null;
        }
        D3d12Native.ReleaseCom(ref _uploadBuf);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        WaitForGpuIdle();
        _readback.Dispose();
        ReleaseUploadBuffer();
        ReleaseBackBuffers();
        if (_fenceEvent != IntPtr.Zero) { _ = D3d12Native.CloseHandle(_fenceEvent); _fenceEvent = IntPtr.Zero; }
        D3d12Native.ReleaseCom(ref _fence);
        D3d12Native.ReleaseCom(ref _cmdList);
        D3d12Native.ReleaseCom(ref _cmdAlloc);
        D3d12Native.ReleaseCom(ref _swapChain);
        D3d12Native.ReleaseCom(ref _queue);
        D3d12Native.ReleaseCom(ref _device);
        D3d12Native.ReleaseCom(ref _factory);
        _graphicsContext = null;
    }

    private sealed class D3d12Context : IHostGraphicsContext
    {
        private readonly D3d12Presenter _owner;
        public HostGraphicsContextHandle Handle { get; }
        public string BackendId => HostRenderBackendIds.DirectX;
        public bool IsHardwareAccelerated => true;
        public D3d12Context(D3d12Presenter owner, IntPtr w, IntPtr dev, IntPtr q, IntPtr fac, IntPtr sc) =>
            (_owner, Handle) = (owner, new HostGraphicsContextHandle(HostRenderBackendKind.DirectX, w, dev, q, fac, sc));
        public void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba) => _owner.PresentRgba(width, height, rgba);
        public int OwnerThreadId => _owner.OwnerThreadId;
        public HostViewportRect LastPresentedDestRect => _owner.LastPresentedDestRect;
        public bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination) =>
            _owner.TryReadSurfaceRect(x, y, width, height, rgbaDestination);
        public void FlushAndSyncForSnapshot() => _owner.FlushAndSyncForSnapshot();
        public void SetVSyncEnabled(bool enabled) => _owner.SetVSyncEnabled(enabled);
        public void Dispose() { }
    }
}
