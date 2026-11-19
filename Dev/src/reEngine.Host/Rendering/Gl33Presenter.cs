// @module: reEngine.Host.Rendering.Gl33Presenter
// @purpose: SDL3 OpenGL 3.3 Core hardware presentation surface and HostGraphicsContextHandle interop provider.
// @depends-on: System, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: Gl33Presenter

using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

/// <summary>
/// SDL3 OpenGL 3.3 Core presenter uploading RGBA frames to a GL texture and blitting directly to the swapchain backbuffer.
/// </summary>
public sealed unsafe class Gl33Presenter : IHostSurfacePresenter
{
    private Gl33Dispatch _gl;
    private IntPtr _sdlWindow;
    private IntPtr _glContext;
    private uint _textureId;
    private uint _readFboId;
    private int _textureWidth;
    private int _textureHeight;
    private Gl33Context? _graphicsContext;
    private bool _disposed;

    public HostRenderBackendKind BackendKind => HostRenderBackendKind.OpenGl;
    public string BackendId => HostRenderBackendIds.OpenGl;
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;

    public static void ConfigureSdlAttributes()
    {
        Sdl3Native.SDL_GL_ResetAttributes();
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlRedSize, 8);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlGreenSize, 8);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlBlueSize, 8);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlAlphaSize, 8);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlDoubleBuffer, 1);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlContextMajorVersion, 3);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlContextMinorVersion, 3);
        _ = Sdl3Native.SDL_GL_SetAttribute(Sdl3Native.GlContextProfileMask, Sdl3Native.GlContextProfileCore);
    }

    public bool TryInitialize(IntPtr sdlWindow, int width, int height, out string diagnostic)
    {
        if (sdlWindow == IntPtr.Zero)
        {
            diagnostic = "SDL window handle is zero.";
            return false;
        }

        _sdlWindow = sdlWindow;
        _glContext = Sdl3Native.SDL_GL_CreateContext(sdlWindow);
        if (_glContext == IntPtr.Zero)
        {
            diagnostic = $"SDL_GL_CreateContext failed ({Sdl3Native.GetErrorString()})";
            return false;
        }

        if (Sdl3Native.SDL_GL_MakeCurrent(sdlWindow, _glContext) == 0)
        {
            diagnostic = $"SDL_GL_MakeCurrent failed ({Sdl3Native.GetErrorString()})";
            Dispose();
            return false;
        }

        _ = Sdl3Native.SDL_GL_SetSwapInterval(0);
        if (!Gl33Dispatch.TryLoad(out _gl, out string missing))
        {
            diagnostic = $"Missing OpenGL 3.3 entry point '{missing}'.";
            Dispose();
            return false;
        }

        int major = 0;
        int minor = 0;
        _gl.GetIntegerv(Gl33Native.MajorVersion, &major);
        _gl.GetIntegerv(Gl33Native.MinorVersion, &minor);
        if (major < 3 || (major == 3 && minor < 3))
        {
            diagnostic = $"OpenGL context {major}.{minor} is below required 3.3 Core.";
            Dispose();
            return false;
        }

        uint tex = 0;
        uint fbo = 0;
        _gl.GenTextures(1, &tex);
        _gl.GenFramebuffers(1, &fbo);
        _textureId = tex;
        _readFboId = fbo;
        if (_textureId == 0 || _readFboId == 0)
        {
            diagnostic = "Failed to allocate OpenGL texture or FBO.";
            Dispose();
            return false;
        }

        _gl.BindTexture(Gl33Native.Texture2D, _textureId);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMinFilter, Gl33Native.Nearest);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMagFilter, Gl33Native.Nearest);
        _gl.BindTexture(Gl33Native.Texture2D, 0);

        _graphicsContext = new Gl33Context(this, sdlWindow, _glContext);
        diagnostic = $"OpenGL {major}.{minor} Core initialized.";
        return true;
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_disposed || _glContext == IntPtr.Zero || width <= 0 || height <= 0) return;
        if (rgba.Length < checked(width * height * 4)) return;
        if (Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext) == 0) return;

        int winW = width;
        int winH = height;
        if (Sdl3Native.SDL_GetWindowSizeInPixels(_sdlWindow, out int pw, out int ph) != 0 && pw > 0 && ph > 0)
        {
            winW = pw;
            winH = ph;
        }
        else if (Sdl3Native.SDL_GetWindowSize(_sdlWindow, out int lw, out int lh) != 0 && lw > 0 && lh > 0)
        {
            winW = lw;
            winH = lh;
        }

        _gl.BindTexture(Gl33Native.Texture2D, _textureId);
        _gl.PixelStorei(Gl33Native.UnpackAlignment, 1);
        _gl.PixelStorei(Gl33Native.UnpackRowLength, 0);

        fixed (byte* px = rgba)
        {
            if (_textureWidth != width || _textureHeight != height)
            {
                _gl.TexImage2D(Gl33Native.Texture2D, 0, Gl33Native.Rgba8, width, height, 0, Gl33Native.Rgba, Gl33Native.UnsignedByte, px);
                _textureWidth = width;
                _textureHeight = height;
            }
            else
            {
                _gl.TexSubImage2D(Gl33Native.Texture2D, 0, 0, 0, width, height, Gl33Native.Rgba, Gl33Native.UnsignedByte, px);
            }
        }

        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, _readFboId);
        _gl.FramebufferTexture2D(Gl33Native.ReadFramebuffer, Gl33Native.ColorAttachment0, Gl33Native.Texture2D, _textureId, 0);
        _gl.BindFramebuffer(Gl33Native.DrawFramebuffer, 0);
        _gl.Viewport(0, 0, winW, winH);
        _gl.ClearColor(14f / 255f, 14f / 255f, 16f / 255f, 1f);
        _gl.Clear(Gl33Native.ColorBufferBit);
        _gl.BlitFramebuffer(0, height, width, 0, 0, 0, winW, winH, Gl33Native.ColorBufferBit, (uint)Gl33Native.Nearest);
        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, 0);
        _gl.BindTexture(Gl33Native.Texture2D, 0);

        _ = Sdl3Native.SDL_GL_SwapWindow(_sdlWindow);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_glContext != IntPtr.Zero && _sdlWindow != IntPtr.Zero)
        {
            _ = Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext);
            if (_readFboId != 0 && _gl.DeleteFramebuffers != null)
            {
                uint fbo = _readFboId;
                _gl.DeleteFramebuffers(1, &fbo);
                _readFboId = 0;
            }
            if (_textureId != 0 && _gl.DeleteTextures != null)
            {
                uint tex = _textureId;
                _gl.DeleteTextures(1, &tex);
                _textureId = 0;
            }
            _ = Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, IntPtr.Zero);
            _ = Sdl3Native.SDL_GL_DestroyContext(_glContext);
            _glContext = IntPtr.Zero;
        }
        _sdlWindow = IntPtr.Zero;
        _graphicsContext = null;
    }

    private sealed class Gl33Context : IHostGraphicsContext
    {
        private readonly Gl33Presenter _owner;
        public HostGraphicsContextHandle Handle { get; }
        public string BackendId => HostRenderBackendIds.OpenGl;
        public bool IsHardwareAccelerated => true;

        public Gl33Context(Gl33Presenter owner, IntPtr sdlWindow, IntPtr glContext)
        {
            _owner = owner;
            Handle = new HostGraphicsContextHandle(
                HostRenderBackendKind.OpenGl,
                sdlWindow,
                glContext,
                glContext,
                IntPtr.Zero,
                IntPtr.Zero,
                Gl33Native.GetProcAddress);
        }

        public void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba) => _owner.PresentRgba(width, height, rgba);
        public void Dispose() { }
    }
}
