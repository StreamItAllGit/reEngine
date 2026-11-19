// @module: reEngine.Host.Rendering.ParallelCpuPresenter
// @purpose: Deterministic 16px horizontal-band parallel CPU scaler and SDL3 presenter bit-exact with Software at 1x.
// @depends-on: System, System.Drawing, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Core, reEngine.Host.Ui
// @exports: ParallelCpuPresenter

using System.Drawing;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Ui;

namespace reEngine.Host.Rendering;

/// <summary>
/// Deterministic 16px horizontal-band parallel CPU presenter and <see cref="IHostRenderBackend"/> surface.
/// </summary>
public sealed unsafe class ParallelCpuPresenter : IHostSurfacePresenter, IHostRenderBackend
{
    public const int BandHeight = 16;

    private readonly HostRgbaSurfaceBackend _surface;
    private IntPtr _sdlWindow;
    private IntPtr _sdlRenderer;
    private IntPtr _sdlTexture;
    private int _textureW;
    private int _textureH;
    private IHostGraphicsContext? _graphicsContext;
    private bool _disposed;

    public HostRenderBackendKind BackendKind => HostRenderBackendKind.ParallelCpu;
    public string BackendId => HostRenderBackendIds.ParallelCpu;
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;
    public int SurfaceWidth => _surface.SurfaceWidth;
    public int SurfaceHeight => _surface.SurfaceHeight;
    public int ScaleFactor { get => _surface.ScaleFactor; set => _surface.ScaleFactor = Math.Clamp(value, 1, 8); }
    public PixelOverlayCanvas Canvas => _surface.Canvas;

    public ParallelCpuPresenter() : this(1280, 720, 2) { }

    public ParallelCpuPresenter(int surfaceWidth, int surfaceHeight, int scaleFactor = 2)
    {
        _surface = new HostRgbaSurfaceBackend(surfaceWidth, surfaceHeight, scaleFactor, HostRenderBackendIds.ParallelCpu)
        {
            UseParallelBandBlit = true
        };
    }

    public bool TryInitialize(IntPtr sdlWindow, int width, int height, out string diagnostic)
    {
        if (sdlWindow == IntPtr.Zero)
        {
            diagnostic = "SDL window handle is zero.";
            return false;
        }

        _sdlWindow = sdlWindow;
        _sdlRenderer = Sdl3Native.SDL_CreateRenderer(sdlWindow, null);
        if (_sdlRenderer == IntPtr.Zero)
        {
            diagnostic = $"SDL_CreateRenderer failed ({Sdl3Native.GetErrorString()})";
            return false;
        }

        _ = Sdl3Native.SDL_SetRenderVSync(_sdlRenderer, 0);
        _graphicsContext = new CpuPresenterContext(this, sdlWindow, _sdlRenderer);
        diagnostic = "ParallelCpu SDL3 presenter initialized.";
        return true;
    }

    public void BeginFrame(int width, int height) => _surface.BeginFrame(width, height);

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_disposed || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        if (_sdlRenderer != IntPtr.Zero)
        {
            PresentViaSdlRenderer(width, height, rgba);
            return;
        }

        _surface.PresentRgba(width, height, rgba);
    }

    public bool TryCopyRgbaFrame(out int width, out int height, out byte[] rgbaBytes) =>
        _surface.TryCopyRgbaFrame(out width, out height, out rgbaBytes);

    public void SavePng(string outputPath) => _surface.SavePng(outputPath);

    internal static void BlitParallelBands(
        int srcW,
        int srcH,
        ReadOnlySpan<byte> src,
        byte[] dstBuf,
        int dstCanvasW,
        int dstCanvasH,
        Rectangle dst)
    {
        if (dst.Width <= 0 || dst.Height <= 0 || src.IsEmpty) return;

        int bandCount = (dst.Height + BandHeight - 1) / BandHeight;
        fixed (byte* srcPtr = src)
        {
            nint srcAddr = (nint)srcPtr;
            int srcLen = src.Length;
            if (bandCount <= 1)
            {
                HostRgbaSurfaceBackend.BlitNearestRowRange(srcW, srcH, srcPtr, srcLen, dstBuf, dstCanvasW, dstCanvasH, dst, 0, dst.Height);
                return;
            }

            Parallel.For(0, bandCount, bandIndex =>
            {
                int startDy = bandIndex * BandHeight;
                int endDy = Math.Min(dst.Height, startDy + BandHeight);
                HostRgbaSurfaceBackend.BlitNearestRowRange(srcW, srcH, (byte*)srcAddr, srcLen, dstBuf, dstCanvasW, dstCanvasH, dst, startDy, endDy);
            });
        }
    }

    private void PresentViaSdlRenderer(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_sdlTexture == IntPtr.Zero || _textureW != width || _textureH != height)
        {
            if (_sdlTexture != IntPtr.Zero) Sdl3Native.SDL_DestroyTexture(_sdlTexture);
            _sdlTexture = Sdl3Native.SDL_CreateTexture(_sdlRenderer, Sdl3Native.PixelFormatAbgr8888, Sdl3Native.TextureAccessStreaming, width, height);
            if (_sdlTexture == IntPtr.Zero) return;
            _textureW = width;
            _textureH = height;
            _ = Sdl3Native.SDL_SetTextureScaleMode(_sdlTexture, Sdl3Native.ScaleModeNearest);
        }

        fixed (byte* px = rgba)
        {
            _ = Sdl3Native.SDL_UpdateTexture(_sdlTexture, IntPtr.Zero, px, width * 4);
        }

        _ = Sdl3Native.SDL_SetRenderDrawColor(_sdlRenderer, 14, 14, 16, 255);
        _ = Sdl3Native.SDL_RenderClear(_sdlRenderer);
        _ = Sdl3Native.SDL_RenderTexture(_sdlRenderer, _sdlTexture, null, null);
        _ = Sdl3Native.SDL_RenderPresent(_sdlRenderer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sdlTexture != IntPtr.Zero) { Sdl3Native.SDL_DestroyTexture(_sdlTexture); _sdlTexture = IntPtr.Zero; }
        if (_sdlRenderer != IntPtr.Zero) { Sdl3Native.SDL_DestroyRenderer(_sdlRenderer); _sdlRenderer = IntPtr.Zero; }
        _sdlWindow = IntPtr.Zero;
        _graphicsContext = null;
        _surface.Dispose();
    }

    private sealed class CpuPresenterContext : IHostGraphicsContext
    {
        private readonly ParallelCpuPresenter _owner;
        public HostGraphicsContextHandle Handle { get; }
        public string BackendId => HostRenderBackendIds.ParallelCpu;
        public bool IsHardwareAccelerated => false;

        public CpuPresenterContext(ParallelCpuPresenter owner, IntPtr sdlWindow, IntPtr sdlRenderer)
        {
            _owner = owner;
            Handle = new HostGraphicsContextHandle(HostRenderBackendKind.ParallelCpu, sdlWindow, sdlRenderer, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        public void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba) => _owner.PresentRgba(width, height, rgba);
        public void Dispose() { }
    }
}
