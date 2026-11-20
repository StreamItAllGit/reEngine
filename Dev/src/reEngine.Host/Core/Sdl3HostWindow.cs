// @module: reEngine.Host.Core.Sdl3HostWindow
// @purpose: Manages SDL3 video/gamepad init, fullscreen/GPU/CPU presenter routing, event polling, and clean teardown without coupling audio init.
// @depends-on: System, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Input, reEngine.Host.Rendering
// @exports: Sdl3HostWindow

using System.Runtime.InteropServices;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Rendering;

namespace reEngine.Host.Core;

internal sealed unsafe class Sdl3HostWindow : IDisposable
{
    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;
    private int _textureWidth;
    private int _textureHeight;
    private IHostSurfacePresenter? _presenter;
    private bool _videoGamepadInitialized;
    private bool _isVisible;
    private bool _disposed;

    public bool HasNativeWindow => _window != IntPtr.Zero && (_renderer != IntPtr.Zero || _presenter is not null);
    public string DiagnosticStatus { get; private set; } = "uninitialized";
    public int ClientWidth { get; private set; }
    public int ClientHeight { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool IsWindowFocused { get; private set; } = true;
    public bool IsRelativeMouseCaptured { get; private set; }
    public HostDisplayMode DisplayMode { get; private set; } = HostDisplayMode.Windowed;
    public int MonitorIndex { get; private set; }
    public bool IsVsyncEnabled { get; private set; }
    public float LastMouseX { get; private set; }
    public float LastMouseY { get; private set; }
    public bool HasMousePosition { get; private set; }
    public HostViewportRect LastClientDestRect { get; private set; }
    public HostRenderBackendKind ActiveBackendKind { get; private set; } = HostRenderBackendKind.Software;
    public string ActiveBackendId { get; private set; } = HostRenderBackendIds.Software;
    public IHostGraphicsContext? GraphicsContext => _presenter?.GraphicsContext;

    public bool TryOpen(string title, int width, int height, bool visible) =>
        TryOpen(title, width, height, visible, HostRenderBackendKind.Software, fullscreen: false, out _);

    public bool TryOpen(string title, int width, int height, bool visible, HostRenderBackendKind backendKind, bool fullscreen, out string diagnostic) =>
        TryOpen(title, width, height, visible, backendKind, fullscreen ? HostDisplayMode.BorderlessFullscreen : HostDisplayMode.Windowed, 0, false, out diagnostic);

    public bool TryOpen(
        string title, int width, int height, bool visible, HostRenderBackendKind backendKind,
        HostDisplayMode displayMode, int monitorIndex, bool vsyncEnabled, out string diagnostic)
    {
        CloseWindowResources();
        ClientWidth = Math.Max(320, width);
        ClientHeight = Math.Max(240, height);
        Sdl3Native.InstallResolver();
        if (!_videoGamepadInitialized)
        {
            if (!Sdl3Native.TryInitSubSystem(Sdl3Native.InitVideo | Sdl3Native.InitGamepad, out string initError))
            { DiagnosticStatus = $"SDL_InitSubSystem fallback ({initError})"; diagnostic = DiagnosticStatus; return false; }
            _videoGamepadInitialized = true;
        }

        ulong flags = Sdl3Native.WindowResizable | (visible ? 0UL : Sdl3Native.WindowHidden);
        if (displayMode == HostDisplayMode.BorderlessFullscreen) flags |= Sdl3Native.WindowFullscreen | Sdl3Native.WindowBorderless;
        else if (displayMode == HostDisplayMode.ExclusiveFullscreen) flags |= Sdl3Native.WindowFullscreen;
        if (backendKind == HostRenderBackendKind.OpenGl) { Gl33Presenter.ConfigureSdlAttributes(); flags |= Sdl3Native.WindowOpenGl; }
        else if (backendKind == HostRenderBackendKind.Vulkan) flags |= Sdl3Native.WindowVulkan;

        _window = Sdl3Native.CreateWindowUtf8(title, ClientWidth, ClientHeight, flags);
        if (_window == IntPtr.Zero) { DiagnosticStatus = $"SDL_CreateWindow fallback ({Sdl3Native.GetErrorString()})"; diagnostic = DiagnosticStatus; return false; }

        _isVisible = visible;
        ApplyMonitorAndDisplayMode(displayMode, monitorIndex);
        if (!TryAttachPresenter(backendKind, out diagnostic)) { CloseWindowResources(); DiagnosticStatus = diagnostic; return false; }
        SetVSyncEnabled(vsyncEnabled);
        IsWindowFocused = true;
        if (Sdl3Native.SDL_GetWindowSize(_window, out int actualW, out int actualH) != 0 && actualW > 0 && actualH > 0) { ClientWidth = actualW; ClientHeight = actualH; }
        LastClientDestRect = new HostViewportRect(0, 0, ClientWidth, ClientHeight);
        ActiveBackendKind = backendKind;
        ActiveBackendId = HostBackendProbe.GetCanonicalBackendId(backendKind);
        if (visible) Sdl3Native.BringToForeground(_window);
        DiagnosticStatus = visible ? $"visible-{ActiveBackendId}" : $"hidden-{ActiveBackendId}";
        diagnostic = DiagnosticStatus;
        return true;
    }

    internal static HostDisplayMode ResolveDisplayMode(HostSessionOptions options) =>
        options.DisplayMode != HostDisplayMode.Windowed ? options.DisplayMode
            : (options.IsFullscreen ? HostDisplayMode.BorderlessFullscreen : HostDisplayMode.Windowed);

    internal bool TryOpenWithFallback(HostSessionOptions options, HostDisplayMode mode, ref HostRenderBackendKind kind, ref string backendId, ref string? fallbackBanner, out string? failReason)
    {
        if (TryOpen(options.WindowTitle, options.WindowWidth, options.WindowHeight, true, kind, mode, options.MonitorIndex, options.IsVsyncEnabled, out string diag))
        { failReason = null; return true; }
        failReason = diag;
        if (options.RequireBackend) return false;
        if (!TryOpenFallbackPresenter(options.WindowTitle, options.WindowWidth, options.WindowHeight, true, mode, options.MonitorIndex, options.IsVsyncEnabled, kind)) return false;
        kind = ActiveBackendKind;
        backendId = ActiveBackendId;
        fallbackBanner ??= $"Fallback to {backendId}: init failed ({diag})";
        return true;
    }

    public bool TrySwitchBackend(HostSessionOptions options, HostRenderBackendKind targetKind, out string activeBackendId, out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (HasNativeWindow && ActiveBackendKind == targetKind) { activeBackendId = ActiveBackendId; diagnostic = $"{activeBackendId} already active."; return true; }

        HostDisplayMode mode = _window != IntPtr.Zero ? DisplayMode : ResolveDisplayMode(options);
        int mon = _window != IntPtr.Zero ? MonitorIndex : options.MonitorIndex;
        bool vsync = _window != IntPtr.Zero ? IsVsyncEnabled : options.IsVsyncEnabled;
        int w = ClientWidth > 0 ? ClientWidth : options.WindowWidth;
        int h = ClientHeight > 0 ? ClientHeight : options.WindowHeight;
        bool visible = _window != IntPtr.Zero ? _isVisible : (options.RequireVisibleWindow && !options.HeadlessFrameCount.HasValue);
        bool hadCapture = IsRelativeMouseCaptured;

        HostBackendCapability probed = HostBackendProbe.Probe(targetKind);
        string failDiag = probed.Diagnostic;
        if (probed.IsAvailable && TryOpen(options.WindowTitle, w, h, visible, targetKind, mode, mon, vsync, out failDiag))
        {
            if (hadCapture) _ = SetRelativeMouseMode(true);
            activeBackendId = ActiveBackendId; diagnostic = failDiag; return true;
        }
        if (TryOpenFallbackPresenter(options.WindowTitle, w, h, visible, mode, mon, vsync, targetKind))
        {
            if (hadCapture) _ = SetRelativeMouseMode(true);
            activeBackendId = ActiveBackendId;
            diagnostic = $"Fallback to {activeBackendId}: {HostBackendProbe.GetCanonicalBackendId(targetKind)} failed ({failDiag})";
            return true;
        }
        activeBackendId = ActiveBackendId; diagnostic = failDiag; return false;
    }

    private bool TryOpenFallbackPresenter(string title, int w, int h, bool visible, HostDisplayMode mode, int monitorIndex, bool vsync, HostRenderBackendKind excluded)
    {
        foreach (HostRenderBackendKind fb in new[] { HostRenderBackendKind.ParallelCpu, HostRenderBackendKind.Software })
        {
            if (excluded != fb && TryOpen(title, w, h, visible, fb, mode, monitorIndex, vsync, out _)) return true;
        }
        return false;
    }

    public HostMonitorInfo[] EnumerateMonitors() => EnumerateMonitorsStatic();

    public static HostMonitorInfo[] EnumerateMonitorsStatic()
    {
        Sdl3Native.InstallResolver();
        bool startedVideo = false;
        try
        {
            if ((Sdl3Native.SDL_WasInit(Sdl3Native.InitVideo) & Sdl3Native.InitVideo) == 0)
            {
                if (!Sdl3Native.TryInitSubSystem(Sdl3Native.InitVideo, out _)) return [FallbackMonitor()];
                startedVideo = true;
            }

            uint* ids = Sdl3Native.SDL_GetDisplays(out int count);
            if (ids == null || count <= 0)
            {
                if (ids != null) Sdl3Native.SDL_free((IntPtr)ids);
                return [FallbackMonitor()];
            }

            try
            {
                uint primaryId = Sdl3Native.SDL_GetPrimaryDisplay();
                var list = new HostMonitorInfo[count];
                for (int i = 0; i < count; i++)
                {
                    uint id = ids[i];
                    SdlRect bounds = default;
                    _ = Sdl3Native.SDL_GetDisplayBounds(id, &bounds);
                    byte* namePtr = Sdl3Native.SDL_GetDisplayName(id);
                    string name = namePtr != null ? (Marshal.PtrToStringUTF8((IntPtr)namePtr) ?? $"Display {i + 1}") : $"Display {i + 1}";
                    SdlDisplayMode* mode = Sdl3Native.SDL_GetCurrentDisplayMode(id);
                    int w = mode != null && mode->W > 0 ? mode->W : Math.Max(320, bounds.W);
                    int h = mode != null && mode->H > 0 ? mode->H : Math.Max(240, bounds.H);
                    float hz = mode != null && mode->RefreshRate > 0f ? mode->RefreshRate : 60f;
                    list[i] = new HostMonitorInfo(i, id, name, bounds.X, bounds.Y, w, h, hz, primaryId != 0 ? id == primaryId : i == 0);
                }
                return list;
            }
            finally
            {
                Sdl3Native.SDL_free((IntPtr)ids);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return [FallbackMonitor()];
        }
        finally
        {
            if (startedVideo) Sdl3Native.SDL_QuitSubSystem(Sdl3Native.InitVideo);
        }
    }

    public bool TrySetFullscreen(bool fullscreen) =>
        TrySetDisplayMode(fullscreen ? HostDisplayMode.BorderlessFullscreen : HostDisplayMode.Windowed, MonitorIndex);

    public bool TrySetDisplayMode(HostDisplayMode mode, int monitorIndex = -1)
    {
        if (_window == IntPtr.Zero) return false;
        ApplyMonitorAndDisplayMode(mode, monitorIndex >= 0 ? monitorIndex : MonitorIndex);
        if (Sdl3Native.SDL_GetWindowSize(_window, out int winW, out int winH) != 0 && winW > 0 && winH > 0) { ClientWidth = winW; ClientHeight = winH; }
        return true;
    }

    public bool TryMoveToMonitor(int monitorIndex) => TrySetDisplayMode(DisplayMode, monitorIndex);

    public void SetVSyncEnabled(bool enabled)
    {
        IsVsyncEnabled = enabled;
        if (_presenter?.GraphicsContext is { } ctx) ctx.SetVSyncEnabled(enabled);
        else if (_renderer != IntPtr.Zero) _ = Sdl3Native.SDL_SetRenderVSync(_renderer, enabled ? 1 : 0);
    }

    public bool SetRelativeMouseMode(bool enabled)
    {
        if (_window == IntPtr.Zero) { IsRelativeMouseCaptured = false; return false; }
        bool ok = Sdl3Native.SDL_SetWindowRelativeMouseMode(_window, enabled ? (byte)1 : (byte)0) != 0;
        IsRelativeMouseCaptured = ok ? enabled : (Sdl3Native.SDL_GetWindowRelativeMouseMode(_window) != 0);
        return ok;
    }

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick) =>
        PollEvents(inputMapper, onLeftClick, onRightClick: null, onFileDropped: null);

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick, Action<int, int>? onRightClick) =>
        PollEvents(inputMapper, onLeftClick, onRightClick, onFileDropped: null);

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick, Action<int, int>? onRightClick, Action<string>? onFileDropped)
    {
        if (!_videoGamepadInitialized) return true;
        byte* ev = stackalloc byte[128];
        while (Sdl3Native.SDL_PollEvent(ev) != 0)
        {
            switch (Sdl3Native.EventType(ev))
            {
                case Sdl3Native.EventQuit:
                case Sdl3Native.EventWindowCloseRequested:
                    return false;
                case Sdl3Native.EventWindowFocusGained: IsWindowFocused = true; break;
                case Sdl3Native.EventWindowFocusLost: IsWindowFocused = false; break;
                case Sdl3Native.EventKeyDown:
                    if (!Sdl3Native.EventKeyIsRepeat(ev)) inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), true);
                    break;
                case Sdl3Native.EventKeyUp:
                    inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), false);
                    break;
                case Sdl3Native.EventMouseMotion:
                {
                    (LastMouseX, LastMouseY) = Sdl3Native.EventMousePosition(ev);
                    HasMousePosition = true;
                    (float dx, float dy) = Sdl3Native.EventMouseRelative(ev);
                    inputMapper.AccumulateMouse(dx, dy, Sdl3Native.EventMouseMotionButtons(ev));
                    break;
                }
                case Sdl3Native.EventMouseButtonDown:
                {
                    byte button = Sdl3Native.EventMouseButton(ev);
                    (LastMouseX, LastMouseY) = Sdl3Native.EventMousePosition(ev);
                    HasMousePosition = true;
                    inputMapper.SetMouseButton(button, true);
                    if (button is 1 or 3)
                    {
                        int px = (int)MathF.Round(LastMouseX), py = (int)MathF.Round(LastMouseY);
                        if (button == 1) onLeftClick(px, py); else onRightClick?.Invoke(px, py);
                    }
                    break;
                }
                case Sdl3Native.EventMouseButtonUp: inputMapper.SetMouseButton(Sdl3Native.EventMouseButton(ev), false); break;
                case Sdl3Native.EventMouseWheel: inputMapper.AccumulateMouseWheel(Sdl3Native.EventMouseWheelDeltaY(ev)); break;
                case Sdl3Native.EventDropFile:
                {
                    string? dropPath = Sdl3Native.EventDropPath(ev);
                    if (!string.IsNullOrWhiteSpace(dropPath)) onFileDropped?.Invoke(dropPath);
                    break;
                }
            }
        }
        return true;
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba) =>
        PresentRgba(width, height, rgba, HostAspectRatioMode.NativeFit);

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostAspectRatioMode aspectMode, HostOverscanCrop? overscanCrop = null, double? contentPixelAspectRatio = null)
    {
        if (!HasNativeWindow || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;
        if (Sdl3Native.SDL_GetWindowSize(_window, out int winW, out int winH) != 0)
        {
            if (winW <= 0 || winH <= 0) return;
            ClientWidth = winW; ClientHeight = winH;
        }

        HostViewportRect srcCrop = overscanCrop.HasValue ? HostViewportGeometry.ComputeSourceCropRect(width, height, overscanCrop.Value) : new HostViewportRect(0, 0, width, height);
        LastClientDestRect = HostViewportGeometry.ComputeDestinationRect(srcCrop.Width, srcCrop.Height, ClientWidth, ClientHeight, aspectMode, contentPixelAspectRatio);
        if (!HostViewportGeometry.TryGetWindowPixelSize(_window, ClientWidth, ClientHeight, out int renderW, out int renderH)) return;
        HostViewportRect destRect = HostViewportGeometry.ComputeDestinationRect(srcCrop.Width, srcCrop.Height, renderW, renderH, aspectMode, contentPixelAspectRatio);

        if (_presenter is not null) { _presenter.PresentRgba(width, height, rgba, destRect, srcCrop); return; }

        EnsureTextureSize(width, height);
        if (_texture == IntPtr.Zero) return;
        fixed (byte* px = rgba) { _ = Sdl3Native.SDL_UpdateTexture(_texture, IntPtr.Zero, px, width * 4); }
        Sdl3Native.PresentSdlTexture(_renderer, _texture, width, height, renderW, renderH, destRect, srcCrop);
    }

    private bool TryAttachPresenter(HostRenderBackendKind backendKind, out string diagnostic)
    {
        if (backendKind == HostRenderBackendKind.Metal) { diagnostic = "Metal surface presenter is not supported on this OS."; return false; }

        IHostSurfacePresenter? candidate = backendKind switch
        {
            HostRenderBackendKind.OpenGl => new Gl33Presenter(),
            HostRenderBackendKind.Vulkan => new VulkanPresenter(),
            HostRenderBackendKind.DirectX => new D3d12Presenter(),
            HostRenderBackendKind.ParallelCpu => new ParallelCpuPresenter(ClientWidth, ClientHeight),
            _ => null
        };
        if (candidate is not null)
        {
            if (candidate.TryInitialize(_window, ClientWidth, ClientHeight, out diagnostic)) { _presenter = candidate; return true; }
            candidate.Dispose();
            return false;
        }

        _renderer = Sdl3Native.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero) { diagnostic = $"SDL_CreateRenderer fallback ({Sdl3Native.GetErrorString()})"; return false; }
        _ = Sdl3Native.SDL_SetRenderVSync(_renderer, IsVsyncEnabled ? 1 : 0);
        diagnostic = "SDL3 software renderer initialized.";
        return true;
    }

    private void ApplyMonitorAndDisplayMode(HostDisplayMode mode, int monitorIndex)
    {
        HostMonitorInfo[] monitors = EnumerateMonitors();
        int idx = monitors.Length > 0 ? Math.Clamp(monitorIndex, 0, monitors.Length - 1) : 0;
        MonitorIndex = idx;
        DisplayMode = mode;
        IsFullscreen = mode != HostDisplayMode.Windowed;

        HostMonitorInfo mon = monitors.Length > 0 ? monitors[idx] : FallbackMonitor();
        if (mode == HostDisplayMode.Windowed)
        {
            _ = Sdl3Native.SDL_SetWindowFullscreen(_window, 0);
            _ = Sdl3Native.SDL_SetWindowFullscreenMode(_window, null);
            _ = Sdl3Native.SDL_SetWindowBordered(_window, 1);
            int posX = mon.X + Math.Max(0, (mon.Width - ClientWidth) / 2);
            int posY = mon.Y + Math.Max(0, (mon.Height - ClientHeight) / 2);
            _ = Sdl3Native.SDL_SetWindowPosition(_window, posX, posY);
            return;
        }

        _ = Sdl3Native.SDL_SetWindowPosition(_window, mon.X, mon.Y);
        SdlDisplayMode* nativeMode = (mode == HostDisplayMode.ExclusiveFullscreen && mon.DisplayId != 0)
            ? Sdl3Native.SDL_GetCurrentDisplayMode(mon.DisplayId) : null;
        _ = Sdl3Native.SDL_SetWindowFullscreenMode(_window, nativeMode);
        _ = Sdl3Native.SDL_SetWindowFullscreen(_window, 1);
    }

    private static HostMonitorInfo FallbackMonitor() => new(0, 0u, "Default Display", 0, 0, 1280, 720, 60f, true);

    private void EnsureTextureSize(int width, int height)
    {
        if (_texture != IntPtr.Zero && _textureWidth == width && _textureHeight == height) return;
        if (_texture != IntPtr.Zero) Sdl3Native.SDL_DestroyTexture(_texture);
        _texture = Sdl3Native.SDL_CreateTexture(_renderer, Sdl3Native.PixelFormatAbgr8888, Sdl3Native.TextureAccessStreaming, width, height);
        if (_texture == IntPtr.Zero) return;
        _textureWidth = width; _textureHeight = height;
        _ = Sdl3Native.SDL_SetTextureScaleMode(_texture, Sdl3Native.ScaleModeNearest);
    }

    private void CloseWindowResources()
    {
        _presenter?.GraphicsContext?.FlushAndSyncForSnapshot();
        _presenter?.Dispose(); _presenter = null;
        if (_texture != IntPtr.Zero) { Sdl3Native.SDL_DestroyTexture(_texture); _texture = IntPtr.Zero; _textureWidth = 0; _textureHeight = 0; }
        if (_renderer != IntPtr.Zero) { Sdl3Native.SDL_DestroyRenderer(_renderer); _renderer = IntPtr.Zero; }
        if (_window != IntPtr.Zero)
        {
            if (IsRelativeMouseCaptured) _ = Sdl3Native.SDL_SetWindowRelativeMouseMode(_window, 0);
            Sdl3Native.SDL_DestroyWindow(_window); _window = IntPtr.Zero;
        }
        IsRelativeMouseCaptured = false; _isVisible = false;
        IsFullscreen = false; DisplayMode = HostDisplayMode.Windowed; IsWindowFocused = true;
        HasMousePosition = false; LastMouseX = 0f; LastMouseY = 0f; LastClientDestRect = default;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseWindowResources();
        if (_videoGamepadInitialized)
        {
            Sdl3Native.SDL_QuitSubSystem(Sdl3Native.InitVideo | Sdl3Native.InitGamepad);
            if (Sdl3Native.SDL_WasInit(0) == 0) Sdl3Native.SDL_Quit();
            _videoGamepadInitialized = false;
        }
    }
}
