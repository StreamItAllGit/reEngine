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
    /// <summary>Fixed scanline height per parallel rasterization band.</summary>
    public const int BandHeight = 16;

    private readonly HostRgbaSurfaceBackend _surface;
    private IntPtr _sdlWindow;
    private IntPtr _sdlRenderer;
    private IntPtr _sdlTexture;
    private int _textureW;
    private int _textureH;
    private IHostGraphicsContext? _graphicsContext;
    private bool _disposed;

    /// <inheritdoc />
    public HostRenderBackendKind BackendKind => HostRenderBackendKind.ParallelCpu;

    /// <inheritdoc />
    public string BackendId => HostRenderBackendIds.ParallelCpu;

    /// <inheritdoc />
    public IHostGraphicsContext? GraphicsContext => _graphicsContext;

    /// <inheritdoc />
    public int SurfaceWidth => _surface.SurfaceWidth;

    /// <inheritdoc />
    public int SurfaceHeight => _surface.SurfaceHeight;

    /// <summary>Gets or sets the configured integer resolution multiplier (1..8).</summary>
    public int ScaleFactor { get => _surface.ScaleFactor; set => _surface.ScaleFactor = Math.Clamp(value, 1, 8); }

    /// <summary>Gets the backing RGBA overlay canvas used in headless/CPU mode.</summary>
    public PixelOverlayCanvas Canvas => _surface.Canvas;

    /// <summary>Initializes a default 1280x720 <see cref="ParallelCpuPresenter"/>.</summary>
    public ParallelCpuPresenter() : this(1280, 720, 2) { }

    /// <summary>Initializes a <see cref="ParallelCpuPresenter"/> with explicit backing surface dimensions.</summary>
    /// <param name="surfaceWidth">Host backing surface width in pixels.</param>
    /// <param name="surfaceHeight">Host backing surface height in pixels.</param>
    /// <param name="scaleFactor">Initial resolution multiplier (1..8).</param>
    public ParallelCpuPresenter(int surfaceWidth, int surfaceHeight, int scaleFactor = 2)
    {
        _surface = new HostRgbaSurfaceBackend(surfaceWidth, surfaceHeight, scaleFactor, HostRenderBackendIds.ParallelCpu)
        {
            UseParallelBandBlit = true
        };
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

    /// <summary>Clears the backing CPU surface at the start of a frame.</summary>
    /// <param name="width">Expected source width.</param>
    /// <param name="height">Expected source height.</param>
    public void BeginFrame(int width, int height) => _surface.BeginFrame(width, height);

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (_disposed || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        if (_sdlRenderer != IntPtr.Zero)
        {
            if (!HostViewportGeometry.TryGetWindowPixelSize(_sdlWindow, width, height, out int renderW, out int renderH)) return;
            HostViewportRect destRect = HostViewportGeometry.ComputeDestinationRect(width, height, renderW, renderH, HostAspectRatioMode.NativeFit);
            PresentViaSdlRenderer(width, height, rgba, renderW, renderH, destRect, new HostViewportRect(0, 0, width, height));
            return;
        }

        _surface.PresentRgba(width, height, rgba);
    }

    /// <inheritdoc />
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostViewportRect destRect, HostViewportRect srcCropRect)
    {
        if (_disposed || width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        if (_sdlRenderer != IntPtr.Zero)
        {
            if (!HostViewportGeometry.TryGetWindowPixelSize(_sdlWindow, width, height, out int renderW, out int renderH)) return;
            PresentViaSdlRenderer(width, height, rgba, renderW, renderH, destRect, srcCropRect);
            return;
        }

        _surface.PresentRgba(width, height, rgba, destRect, srcCropRect);
    }

    /// <inheritdoc />
    public bool TryCopyRgbaFrame(out int width, out int height, out byte[] rgbaBytes) =>
        _surface.TryCopyRgbaFrame(out width, out height, out rgbaBytes);

    /// <inheritdoc />
    public void SavePng(string outputPath) => _surface.SavePng(outputPath);

    internal static void BlitParallelBands(
        int srcW,
        int srcH,
        ReadOnlySpan<byte> src,
        byte[] dstBuf,
        int dstCanvasW,
        int dstCanvasH,
        Rectangle dst) =>
        BlitParallelBands(srcW, srcH, src, dstBuf, dstCanvasW, dstCanvasH, dst, new HostViewportRect(0, 0, srcW, srcH));

    internal static void BlitParallelBands(
        int srcW,
        int srcH,
        ReadOnlySpan<byte> src,
        byte[] dstBuf,
        int dstCanvasW,
        int dstCanvasH,
        Rectangle dst,
        HostViewportRect srcCrop)
    {
        if (dst.Width <= 0 || dst.Height <= 0 || src.IsEmpty) return;

        int bandCount = (dst.Height + BandHeight - 1) / BandHeight;
        fixed (byte* srcPtr = src)
        {
            nint srcAddr = (nint)srcPtr;
            int srcLen = src.Length;
            if (bandCount <= 1)
            {
                HostRgbaSurfaceBackend.BlitNearestRowRange(srcW, srcH, srcPtr, srcLen, dstBuf, dstCanvasW, dstCanvasH, dst, srcCrop, 0, dst.Height);
                return;
            }

            Parallel.For(0, bandCount, bandIndex =>
            {
                int startDy = bandIndex * BandHeight;
                int endDy = Math.Min(dst.Height, startDy + BandHeight);
                HostRgbaSurfaceBackend.BlitNearestRowRange(srcW, srcH, (byte*)srcAddr, srcLen, dstBuf, dstCanvasW, dstCanvasH, dst, srcCrop, startDy, endDy);
            });
        }
    }

    private void PresentViaSdlRenderer(
        int width,
        int height,
        ReadOnlySpan<byte> rgba,
        int renderW,
        int renderH,
        HostViewportRect destRect,
        HostViewportRect srcCropRect)
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

        Sdl3Native.PresentSdlTexture(_sdlRenderer, _sdlTexture, width, height, renderW, renderH, destRect, srcCropRect);
    }

    /// <inheritdoc />
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
