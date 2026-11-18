// @module: reEngine.Host.Core.Sdl3HostWindow
// @purpose: Manages SDL3 window creation, visible presentation texture streaming, event polling, and clean teardown.
// @depends-on: System, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: Sdl3HostWindow

using reEngine.Host.Input;

namespace reEngine.Host.Core;

internal sealed unsafe class Sdl3HostWindow : IDisposable
{
    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;
    private int _textureWidth;
    private int _textureHeight;
    private bool _sdlInitialized;
    private bool _disposed;

    public bool HasNativeWindow => _window != IntPtr.Zero && _renderer != IntPtr.Zero;
    public string DiagnosticStatus { get; private set; } = "uninitialized";
    public int ClientWidth { get; private set; }
    public int ClientHeight { get; private set; }

    public bool TryOpen(string title, int width, int height, bool visible)
    {
        ClientWidth = Math.Max(320, width);
        ClientHeight = Math.Max(240, height);
        Sdl3Native.InstallResolver();

        if (!Sdl3Native.TryInit(Sdl3Native.InitVideo | Sdl3Native.InitAudio, out string initError))
        {
            DiagnosticStatus = $"SDL_Init fallback ({initError})";
            return false;
        }
        _sdlInitialized = true;

        ulong flags = Sdl3Native.WindowResizable | (visible ? 0UL : Sdl3Native.WindowHidden);
        _window = Sdl3Native.CreateWindowUtf8(title, ClientWidth, ClientHeight, flags);
        if (_window == IntPtr.Zero)
        {
            DiagnosticStatus = $"SDL_CreateWindow fallback ({Sdl3Native.GetErrorString()})";
            return false;
        }

        _renderer = Sdl3Native.SDL_CreateRenderer(_window, null);
        if (_renderer == IntPtr.Zero)
        {
            DiagnosticStatus = $"SDL_CreateRenderer fallback ({Sdl3Native.GetErrorString()})";
            return false;
        }

        _ = Sdl3Native.SDL_SetRenderVSync(_renderer, 0);
        if (visible)
        {
            Sdl3Native.BringToForeground(_window);
        }

        DiagnosticStatus = visible ? "visible-sdl3" : "hidden-sdl3";
        return true;
    }

    public bool PollEvents(HostInputMapper inputMapper, Action<int, int> onLeftClick)
    {
        if (!_sdlInitialized) return true;
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
                    inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), true);
                    break;

                case Sdl3Native.EventKeyUp:
                    inputMapper.SetScancodeState(Sdl3Native.EventScancode(ev), false);
                    break;

                case Sdl3Native.EventMouseMotion:
                {
                    (float dx, float dy) = Sdl3Native.EventMouseRelative(ev);
                    inputMapper.AccumulateMouse(dx, dy, 0);
                    break;
                }

                case Sdl3Native.EventMouseButtonDown when Sdl3Native.EventMouseButton(ev) == 1:
                {
                    (float mx, float my) = Sdl3Native.EventMousePosition(ev);
                    onLeftClick((int)MathF.Round(mx), (int)MathF.Round(my));
                    break;
                }
            }
        }
        return true;
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (!HasNativeWindow || width <= 0 || height <= 0) return;
        EnsureTextureSize(width, height);
        if (_texture == IntPtr.Zero) return;

        if (Sdl3Native.SDL_GetWindowSize(_window, out int winW, out int winH) != 0 && winW > 0 && winH > 0)
        {
            ClientWidth = winW;
            ClientHeight = winH;
        }

        fixed (byte* px = rgba)
        {
            _ = Sdl3Native.SDL_UpdateTexture(_texture, IntPtr.Zero, px, width * 4);
        }

        _ = Sdl3Native.SDL_SetRenderDrawColor(_renderer, 14, 14, 16, 255);
        _ = Sdl3Native.SDL_RenderClear(_renderer);
        _ = Sdl3Native.SDL_RenderTexture(_renderer, _texture, null, null);
        _ = Sdl3Native.SDL_RenderPresent(_renderer);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_texture != IntPtr.Zero) { Sdl3Native.SDL_DestroyTexture(_texture); _texture = IntPtr.Zero; }
        if (_renderer != IntPtr.Zero) { Sdl3Native.SDL_DestroyRenderer(_renderer); _renderer = IntPtr.Zero; }
        if (_window != IntPtr.Zero) { Sdl3Native.SDL_DestroyWindow(_window); _window = IntPtr.Zero; }
        if (_sdlInitialized) { Sdl3Native.SDL_Quit(); _sdlInitialized = false; }
    }
}
