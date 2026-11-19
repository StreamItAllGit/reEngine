// @module: reEngine.Host.Core.Sdl3HostWindow
// @purpose: Manages SDL3 video/gamepad init, fullscreen/GPU/CPU presenter routing, event polling, and clean teardown without coupling audio init.
// @depends-on: System, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Input, reEngine.Host.Rendering
// @exports: Sdl3HostWindow

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
    private bool _disposed;

    public bool HasNativeWindow => _window != IntPtr.Zero && (_renderer != IntPtr.Zero || _presenter is not null);
    public string DiagnosticStatus { get; private set; } = "uninitialized";
    public int ClientWidth { get; private set; }
    public int ClientHeight { get; private set; }
    public HostRenderBackendKind ActiveBackendKind { get; private set; } = HostRenderBackendKind.Software;
    public string ActiveBackendId { get; private set; } = HostRenderBackendIds.Software;
    public IHostGraphicsContext? GraphicsContext => _presenter?.GraphicsContext;

    public bool TryOpen(string title, int width, int height, bool visible) =>
        TryOpen(title, width, height, visible, HostRenderBackendKind.Software, fullscreen: false, out _);

    public bool TryOpen(
        string title,
        int width,
        int height,
        bool visible,
        HostRenderBackendKind backendKind,
        bool fullscreen,
        out string diagnostic)
    {
        CloseWindowResources();
        ClientWidth = Math.Max(320, width);
        ClientHeight = Math.Max(240, height);
        Sdl3Native.InstallResolver();

        if (!_videoGamepadInitialized)
        {
            if (!Sdl3Native.TryInitSubSystem(Sdl3Native.InitVideo | Sdl3Native.InitGamepad, out string initError))
            {
                DiagnosticStatus = $"SDL_InitSubSystem fallback ({initError})";
                diagnostic = DiagnosticStatus;
                return false;
            }
            _videoGamepadInitialized = true;
        }

        ulong flags = Sdl3Native.WindowResizable
            | (visible ? 0UL : Sdl3Native.WindowHidden)
            | (fullscreen ? Sdl3Native.WindowFullscreen : 0UL);

        if (backendKind == HostRenderBackendKind.OpenGl)
        {
            Gl33Presenter.ConfigureSdlAttributes();
            flags |= Sdl3Native.WindowOpenGl;
        }
        else if (backendKind == HostRenderBackendKind.Vulkan)
        {
            flags |= Sdl3Native.WindowVulkan;
        }

        _window = Sdl3Native.CreateWindowUtf8(title, ClientWidth, ClientHeight, flags);
        if (_window == IntPtr.Zero)
        {
            DiagnosticStatus = $"SDL_CreateWindow fallback ({Sdl3Native.GetErrorString()})";
            diagnostic = DiagnosticStatus;
            return false;
        }

        if (!TryAttachPresenter(backendKind, out diagnostic))
        {
            CloseWindowResources();
            DiagnosticStatus = diagnostic;
            return false;
        }

        ActiveBackendKind = backendKind;
        ActiveBackendId = HostBackendProbe.GetCanonicalBackendId(backendKind);
        if (visible) Sdl3Native.BringToForeground(_window);

        DiagnosticStatus = visible ? $"visible-{ActiveBackendId}" : $"hidden-{ActiveBackendId}";
        diagnostic = DiagnosticStatus;
        return true;
    }

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick) =>
        PollEvents(inputMapper, onLeftClick, onRightClick: null);

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick, Action<int, int>? onRightClick)
    {
        if (!_videoGamepadInitialized) return true;
        byte* ev = stackalloc byte[128];
        while (Sdl3Native.SDL_PollEvent(ev) != 0)
        {
            uint evType = Sdl3Native.EventType(ev);
            switch (evType)
            {
                case Sdl3Native.EventQuit:
                case Sdl3Native.EventWindowCloseRequested:
                    return false;

                case Sdl3Native.EventKeyDown:
                    if (!Sdl3Native.EventKeyIsRepeat(ev))
                    {
                        inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), true);
                    }
                    break;

                case Sdl3Native.EventKeyUp:
                    inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), false);
                    break;

                case Sdl3Native.EventMouseMotion:
                {
                    (float dx, float dy) = Sdl3Native.EventMouseRelative(ev);
                    inputMapper.AccumulateMouse(dx, dy, Sdl3Native.EventMouseMotionButtons(ev));
                    break;
                }

                case Sdl3Native.EventMouseButtonDown:
                {
                    byte button = Sdl3Native.EventMouseButton(ev);
                    inputMapper.SetMouseButton(button, true);
                    if (button is 1 or 3)
                    {
                        (float mx, float my) = Sdl3Native.EventMousePosition(ev);
                        int px = (int)MathF.Round(mx), py = (int)MathF.Round(my);
                        if (button == 1) onLeftClick(px, py);
                        else onRightClick?.Invoke(px, py);
                    }
                    break;
                }

                case Sdl3Native.EventMouseButtonUp:
                    inputMapper.SetMouseButton(Sdl3Native.EventMouseButton(ev), false);
                    break;

                case Sdl3Native.EventMouseWheel:
                    inputMapper.AccumulateMouseWheel(Sdl3Native.EventMouseWheelDeltaY(ev));
                    break;
            }
        }
        return true;
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (!HasNativeWindow || width <= 0 || height <= 0) return;

        if (Sdl3Native.SDL_GetWindowSize(_window, out int winW, out int winH) != 0 && winW > 0 && winH > 0)
        {
            ClientWidth = winW;
            ClientHeight = winH;
        }

        if (_presenter is not null)
        {
            _presenter.PresentRgba(width, height, rgba);
            return;
        }

        EnsureTextureSize(width, height);
        if (_texture == IntPtr.Zero) return;

        fixed (byte* px = rgba)
        {
            _ = Sdl3Native.SDL_UpdateTexture(_texture, IntPtr.Zero, px, width * 4);
        }

        _ = Sdl3Native.SDL_SetRenderDrawColor(_renderer, 14, 14, 16, 255);
        _ = Sdl3Native.SDL_RenderClear(_renderer);
        _ = Sdl3Native.SDL_RenderTexture(_renderer, _texture, null, null);
        _ = Sdl3Native.SDL_RenderPresent(_renderer);
    }

    private bool TryAttachPresenter(HostRenderBackendKind backendKind, out string diagnostic)
    {
        IHostSurfacePresenter? candidate = backendKind switch
        {
            HostRenderBackendKind.OpenGl => new Gl33Presenter(),
            HostRenderBackendKind.Vulkan => new VulkanPresenter(),
            HostRenderBackendKind.DirectX => new D3d12Presenter(),
            HostRenderBackendKind.ParallelCpu => new ParallelCpuPresenter(ClientWidth, ClientHeight),
            HostRenderBackendKind.Metal => null,
            _ => null
        };

        if (backendKind == HostRenderBackendKind.Metal)
        {
            diagnostic = "Metal surface presenter is not supported on this OS.";
            return false;
        }

        if (candidate is not null)
        {
            if (candidate.TryInitialize(_window, ClientWidth, ClientHeight, out diagnostic))
            {
                _presenter = candidate;
                return true;
            }
            candidate.Dispose();
            return false;
        }

        _renderer = Sdl3Native.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero)
        {
            diagnostic = $"SDL_CreateRenderer fallback ({Sdl3Native.GetErrorString()})";
            return false;
        }
        _ = Sdl3Native.SDL_SetRenderVSync(_renderer, 0);
        diagnostic = "SDL3 software renderer initialized.";
        return true;
    }

    private void EnsureTextureSize(int width, int height)
    {
        if (_texture != IntPtr.Zero && _textureWidth == width && _textureHeight == height) return;
        if (_texture != IntPtr.Zero) Sdl3Native.SDL_DestroyTexture(_texture);

        _texture = Sdl3Native.SDL_CreateTexture(_renderer, Sdl3Native.PixelFormatAbgr8888, Sdl3Native.TextureAccessStreaming, width, height);
        if (_texture != IntPtr.Zero)
        {
            _textureWidth = width;
            _textureHeight = height;
            _ = Sdl3Native.SDL_SetTextureScaleMode(_texture, Sdl3Native.ScaleModeNearest);
        }
    }

    private void CloseWindowResources()
    {
        _presenter?.Dispose();
        _presenter = null;
        if (_texture != IntPtr.Zero) { Sdl3Native.SDL_DestroyTexture(_texture); _texture = IntPtr.Zero; _textureWidth = 0; _textureHeight = 0; }
        if (_renderer != IntPtr.Zero) { Sdl3Native.SDL_DestroyRenderer(_renderer); _renderer = IntPtr.Zero; }
        if (_window != IntPtr.Zero) { Sdl3Native.SDL_DestroyWindow(_window); _window = IntPtr.Zero; }
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
