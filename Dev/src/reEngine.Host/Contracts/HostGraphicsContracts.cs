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
}
