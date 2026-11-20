// @module: reEngine.Host.Contracts.HostGraphicsContracts
// @purpose: Native GPU context, capability probe, and surface presenter contracts shared by reEngine.Host and child platform engines.
// @depends-on: System
// @exports: HostBackendCapability, HostGraphicsContextHandle, IHostGraphicsContext, IHostSurfacePresenter

namespace reEngine.Host.Contracts;

/// <summary>
/// Runtime hardware/driver availability probe result for a clean-room render backend.
/// </summary>
public sealed record HostBackendCapability(
    HostRenderBackendKind Kind,
    string BackendId,
    bool IsAvailable,
    string Diagnostic);

/// <summary>
/// Opaque native graphics handles exposed by <c>reEngine.Host</c> so console-specific engines (e.g. <c>reSlate</c>)
/// can issue hardware draw calls on the host-owned window/swapchain without owning SDL3 window boilerplate.
/// </summary>
public sealed record HostGraphicsContextHandle(
    HostRenderBackendKind BackendKind,
    IntPtr WindowHandle,
    IntPtr NativeDeviceHandle,
    IntPtr NativeQueueOrContextHandle,
    IntPtr NativeInstanceHandle,
    IntPtr NativeSurfaceHandle,
    Func<string, IntPtr>? GlGetProcAddress = null);

/// <summary>
/// Active native GPU or CPU presenter graphics context owned by <c>reEngine.Host</c>.
/// </summary>
public interface IHostGraphicsContext : IDisposable
{
    HostGraphicsContextHandle Handle { get; }
    string BackendId { get; }
    bool IsHardwareAccelerated { get; }
    void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba);

    /// <summary>Gets the managed thread ID of the context owner thread, or <c>0</c> when unbound.</summary>
    int OwnerThreadId => 0;

    /// <summary>
    /// Attempts to read back an RGBA8888 surface rectangle on the context owner thread without Y-flip inversion.
    /// </summary>
    /// <param name="x">Source X origin in pixels.</param>
    /// <param name="y">Source Y origin in pixels.</param>
    /// <param name="width">Rectangle width in pixels.</param>
    /// <param name="height">Rectangle height in pixels.</param>
    /// <param name="rgbaDestination">Destination span holding at least <c>width * height * 4</c> bytes.</param>
    /// <returns><c>true</c> if readback succeeded on the owner thread; otherwise <c>false</c>.</returns>
    bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination) => false;

    /// <summary>
    /// Flushes pending GPU work on the context owner thread before QuickSave capture or presenter switch teardown.
    /// </summary>
    void FlushAndSyncForSnapshot() { }
}

/// <summary>
/// Console-agnostic presentation surface abstraction implemented by OpenGL, Vulkan, DirectX 12, ParallelCpu, and Software backends.
/// </summary>
public interface IHostSurfacePresenter : IDisposable
{
    HostRenderBackendKind BackendKind { get; }
    string BackendId { get; }
    IHostGraphicsContext? GraphicsContext { get; }
    bool TryInitialize(IntPtr sdlWindow, int width, int height, out string diagnostic);
    void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba);
    void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostViewportRect destRect, HostViewportRect srcCropRect) => PresentRgba(width, height, rgba);
}
