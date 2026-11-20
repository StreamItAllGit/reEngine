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
    private uint _surfaceTexId;
    private uint _surfaceFboId;
    private int _textureWidth;
    private int _textureHeight;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _ownerThreadId;
    private bool _vsyncEnabled;
    private HostViewportRect _lastPresentedDestRect;
    private Gl33Context? _graphicsContext;
    private bool _disposed;

    /// <inheritdoc />
    public HostRenderBackendKind BackendKind => HostRenderBackendKind.OpenGl;

    /// <inheritdoc />
    public string BackendId => HostRenderBackendIds.OpenGl;

    /// <inheritdoc />
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;

    /// <summary>Configures required SDL3 OpenGL 3.3 Core attributes prior to window creation.</summary>
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

    /// <inheritdoc />
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

        _ = Sdl3Native.SDL_GL_SetSwapInterval(_vsyncEnabled ? 1 : 0);
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

        uint* tex = stackalloc uint[2];
        uint* fbo = stackalloc uint[2];
        _gl.GenTextures(2, tex);
        _gl.GenFramebuffers(2, fbo);
        _textureId = tex[0];
        _surfaceTexId = tex[1];
        _readFboId = fbo[0];
        _surfaceFboId = fbo[1];
        if (_textureId == 0 || _readFboId == 0 || _surfaceTexId == 0 || _surfaceFboId == 0)
        {
            diagnostic = "Failed to allocate OpenGL textures or FBOs.";
            Dispose();
            return false;
        }

        _gl.BindTexture(Gl33Native.Texture2D, _textureId);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMinFilter, Gl33Native.Nearest);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMagFilter, Gl33Native.Nearest);
        _gl.BindTexture(Gl33Native.Texture2D, _surfaceTexId);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMinFilter, Gl33Native.Nearest);
        _gl.TexParameteri(Gl33Native.Texture2D, Gl33Native.TextureMagFilter, Gl33Native.Nearest);
        _gl.BindTexture(Gl33Native.Texture2D, 0);

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _graphicsContext = new Gl33Context(this, sdlWindow, _glContext);
        diagnostic = $"OpenGL {major}.{minor} Core initialized.";
        return true;
    }

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_disposed || _glContext == IntPtr.Zero || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;
        if (!HostViewportGeometry.TryGetWindowPixelSize(_sdlWindow, width, height, out int winW, out int winH)) return;

        HostViewportRect destRect = HostViewportGeometry.ComputeDestinationRect(width, height, winW, winH, HostAspectRatioMode.NativeFit);
        PresentRgba(width, height, rgba, destRect, new HostViewportRect(0, 0, width, height));
    }

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostViewportRect destRect, HostViewportRect srcCropRect)
    {
        if (_disposed || _glContext == IntPtr.Zero || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;
        if (!HostViewportGeometry.TryGetWindowPixelSize(_sdlWindow, width, height, out int winW, out int winH)) return;
        if (Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext) == 0) return;

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

        EnsureSurfaceTexture(winW, winH);
        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, _readFboId);
        _gl.FramebufferTexture2D(Gl33Native.ReadFramebuffer, Gl33Native.ColorAttachment0, Gl33Native.Texture2D, _textureId, 0);
        _gl.BindFramebuffer(Gl33Native.DrawFramebuffer, _surfaceFboId);
        _gl.FramebufferTexture2D(Gl33Native.DrawFramebuffer, Gl33Native.ColorAttachment0, Gl33Native.Texture2D, _surfaceTexId, 0);
        uint readStatus = _gl.CheckFramebufferStatus(Gl33Native.ReadFramebuffer);
        uint drawStatus = _gl.CheckFramebufferStatus(Gl33Native.DrawFramebuffer);
        if (!IsRenderTargetReady(winW, winH, readStatus) || drawStatus != Gl33Native.FramebufferComplete)
        {
            _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, 0);
            _gl.BindFramebuffer(Gl33Native.DrawFramebuffer, 0);
            _gl.BindTexture(Gl33Native.Texture2D, 0);
            return;
        }

        _gl.Viewport(0, 0, winW, winH);
        _gl.ClearColor(14f / 255f, 14f / 255f, 16f / 255f, 1f);
        _gl.Clear(Gl33Native.ColorBufferBit);

        HostViewportRect clampedSrc = HostViewportGeometry.ClampSourceRect(width, height, srcCropRect);
        HostViewportRect clampedDst = HostViewportGeometry.ClampDestinationRect(winW, winH, destRect);
        _lastPresentedDestRect = clampedDst;
        if (clampedDst.Width > 0 && clampedDst.Height > 0 && clampedSrc.Width > 0 && clampedSrc.Height > 0)
        {
            _gl.BlitFramebuffer(
                clampedSrc.X, clampedSrc.Y, clampedSrc.X + clampedSrc.Width, clampedSrc.Y + clampedSrc.Height,
                clampedDst.X, clampedDst.Y, clampedDst.X + clampedDst.Width, clampedDst.Y + clampedDst.Height,
                Gl33Native.ColorBufferBit, (uint)Gl33Native.Nearest);
        }

        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, _surfaceFboId);
        _gl.BindFramebuffer(Gl33Native.DrawFramebuffer, 0);
        _gl.BlitFramebuffer(0, winH, winW, 0, 0, 0, winW, winH, Gl33Native.ColorBufferBit, (uint)Gl33Native.Nearest);

        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, 0);
        _gl.BindTexture(Gl33Native.Texture2D, 0);
        _ = Sdl3Native.SDL_GL_SwapWindow(_sdlWindow);
    }

    private void EnsureSurfaceTexture(int winW, int winH)
    {
        if (_surfaceWidth == winW && _surfaceHeight == winH) return;
        _gl.BindTexture(Gl33Native.Texture2D, _surfaceTexId);
        _gl.TexImage2D(Gl33Native.Texture2D, 0, Gl33Native.Rgba8, winW, winH, 0, Gl33Native.Rgba, Gl33Native.UnsignedByte, null);
        _gl.BindTexture(Gl33Native.Texture2D, 0);
        _surfaceWidth = winW;
        _surfaceHeight = winH;
    }

    internal int OwnerThreadId => _ownerThreadId;
    internal HostViewportRect LastPresentedDestRect => _lastPresentedDestRect;

    internal void SetVSyncEnabled(bool enabled)
    {
        _vsyncEnabled = enabled;
        if (_disposed || _glContext == IntPtr.Zero || _sdlWindow == IntPtr.Zero) return;
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return;
        if (Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext) == 0) return;
        _ = Sdl3Native.SDL_GL_SetSwapInterval(enabled ? 1 : 0);
    }

    internal void FlushAndSyncForSnapshot()
    {
        if (_disposed || _glContext == IntPtr.Zero || _sdlWindow == IntPtr.Zero) return;
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return;
        if (Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext) == 0) return;
        if (_gl.Finish != null) _gl.Finish();
    }

    internal bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination)
    {
        if (_disposed || _glContext == IntPtr.Zero || _sdlWindow == IntPtr.Zero) return false;
        if (_ownerThreadId != 0 && Environment.CurrentManagedThreadId != _ownerThreadId) return false;
        if (x < 0 || y < 0 || width <= 0 || height <= 0) return false;
        if (_surfaceTexId == 0 || _surfaceFboId == 0 || _surfaceWidth <= 0 || _surfaceHeight <= 0) return false;
        if (x > _surfaceWidth - width || y > _surfaceHeight - height) return false;
        long requiredBytes = (long)width * height * 4L;
        if (requiredBytes <= 0 || requiredBytes > rgbaDestination.Length) return false;
        if (_gl.ReadPixels == null || Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext) == 0) return false;

        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, _surfaceFboId);
        _gl.FramebufferTexture2D(Gl33Native.ReadFramebuffer, Gl33Native.ColorAttachment0, Gl33Native.Texture2D, _surfaceTexId, 0);
        uint fboStatus = _gl.CheckFramebufferStatus(Gl33Native.ReadFramebuffer);
        if (fboStatus != Gl33Native.FramebufferComplete)
        {
            _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, 0);
            return false;
        }

        if (_gl.Finish != null) _gl.Finish();
        _gl.PixelStorei(Gl33Native.PackAlignment, 1);
        _gl.PixelStorei(Gl33Native.PackRowLength, 0);
        fixed (byte* dst = rgbaDestination)
        {
            _gl.ReadPixels(x, y, width, height, Gl33Native.Rgba, Gl33Native.UnsignedByte, dst);
        }
        _gl.BindFramebuffer(Gl33Native.ReadFramebuffer, 0);
        return true;
    }

    internal static bool IsRenderTargetReady(int winW, int winH, uint readFboStatus) =>
        winW > 0 && winH > 0 && readFboStatus == Gl33Native.FramebufferComplete;

    internal static (int SrcX0, int SrcY0, int SrcX1, int SrcY1, int DstX0, int DstY0, int DstX1, int DstY1) ComputeBlitCoordinates(
        int texW,
        int texH,
        int winW,
        int winH,
        HostViewportRect destRect,
        HostViewportRect srcCropRect)
    {
        HostViewportRect src = HostViewportGeometry.ClampSourceRect(texW, texH, srcCropRect);
        HostViewportRect dst = HostViewportGeometry.ClampDestinationRect(winW, winH, destRect);
        int dstY0 = winH - (dst.Y + dst.Height);
        int dstY1 = winH - dst.Y;
        return (src.X, src.Y + src.Height, src.X + src.Width, src.Y, dst.X, dstY0, dst.X + dst.Width, dstY1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_glContext != IntPtr.Zero && _sdlWindow != IntPtr.Zero)
        {
            _ = Sdl3Native.SDL_GL_MakeCurrent(_sdlWindow, _glContext);
            if (_gl.DeleteFramebuffers != null)
            {
                if (_readFboId != 0) { uint fbo = _readFboId; _gl.DeleteFramebuffers(1, &fbo); _readFboId = 0; }
                if (_surfaceFboId != 0) { uint fbo = _surfaceFboId; _gl.DeleteFramebuffers(1, &fbo); _surfaceFboId = 0; }
            }
            if (_gl.DeleteTextures != null)
            {
                if (_textureId != 0) { uint tex = _textureId; _gl.DeleteTextures(1, &tex); _textureId = 0; }
                if (_surfaceTexId != 0) { uint tex = _surfaceTexId; _gl.DeleteTextures(1, &tex); _surfaceTexId = 0; }
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
        public int OwnerThreadId => _owner.OwnerThreadId;
        public HostViewportRect LastPresentedDestRect => _owner.LastPresentedDestRect;
        public bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination) =>
            _owner.TryReadSurfaceRect(x, y, width, height, rgbaDestination);
        public void FlushAndSyncForSnapshot() => _owner.FlushAndSyncForSnapshot();
        public void SetVSyncEnabled(bool enabled) => _owner.SetVSyncEnabled(enabled);
        public void Dispose() { }
    }
}
