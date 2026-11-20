// @module: reEngine.Host.Core.HostRgbaSurfaceBackend
// @purpose: Reusable RGBA framebuffer composite backend atop PixelOverlayCanvas and RgbaPngWriter with shared row-range blit helper.
// @depends-on: System, System.Drawing, Foundation.Core, Foundation.Retro, reEngine.Host.Contracts, reEngine.Host.Rendering, reEngine.Host.Ui
// @exports: HostRgbaSurfaceBackend

using System.Drawing;
using Foundation.Core;
using Foundation.Retro;
using reEngine.Host.Contracts;
using reEngine.Host.Rendering;
using reEngine.Host.Ui;

namespace reEngine.Host.Core;

/// <summary>
/// Thread-safe RGBA render surface composing engine pixels and <see cref="PixelOverlayCanvas"/> overlays.
/// </summary>
public sealed class HostRgbaSurfaceBackend : IHostRenderBackend
{
    private readonly object _snapshotSync = new();
    private byte[] _snapshotRgba;
    private bool _disposed;

    /// <summary>Gets the canonical backend identifier string.</summary>
    public string BackendId { get; }

    /// <summary>Gets or sets the configured integer resolution multiplier (1..8).</summary>
    public int ScaleFactor { get; set; }

    /// <summary>Gets or sets whether horizontal-band parallel blitting is enabled.</summary>
    public bool UseParallelBandBlit { get; set; }

    /// <summary>Gets or sets the active aspect ratio mode applied when blitting content onto <see cref="Canvas"/>.</summary>
    public HostAspectRatioMode AspectRatioMode { get; set; } = HostAspectRatioMode.NativePar;

    /// <summary>Gets or sets the active overscan pixel crop applied to source frames.</summary>
    public HostOverscanCrop OverscanCropPx { get; set; }

    /// <summary>Gets or sets an optional explicit pixel aspect ratio override for <see cref="HostAspectRatioMode.NativePar"/>.</summary>
    public double? ContentPixelAspectRatio { get; set; }

    /// <summary>Gets or sets whether an FMV sequence is active, centering 4:3 FMVs cleanly inside widescreen modes.</summary>
    public bool IsFmvActive { get; set; }

    /// <summary>Gets the destination rectangle of the most recently presented content frame inside <see cref="Canvas"/>.</summary>
    public HostViewportRect LastContentDestRect { get; private set; }

    /// <summary>Gets the source crop rectangle of the most recently presented content frame.</summary>
    public HostViewportRect LastSourceCropRect { get; private set; }

    /// <summary>Gets the host surface width in pixels.</summary>
    public int SurfaceWidth => Canvas.Width;

    /// <summary>Gets the host surface height in pixels.</summary>
    public int SurfaceHeight => Canvas.Height;

    /// <summary>Gets the underlying RGBA overlay canvas.</summary>
    public PixelOverlayCanvas Canvas { get; }

    /// <summary>
    /// Initializes a new <see cref="HostRgbaSurfaceBackend"/> with the specified canvas dimensions and backend ID.
    /// </summary>
    /// <param name="surfaceWidth">Host composite surface width in pixels.</param>
    /// <param name="surfaceHeight">Host composite surface height in pixels.</param>
    /// <param name="scaleFactor">Initial resolution multiplier (1..8).</param>
    /// <param name="backendId">Canonical backend identifier.</param>
    public HostRgbaSurfaceBackend(
        int surfaceWidth = 1280,
        int surfaceHeight = 720,
        int scaleFactor = 2,
        string backendId = RecompRenderBackendIds.Software)
    {
        BackendId = string.IsNullOrWhiteSpace(backendId) ? RecompRenderBackendIds.Software : backendId.Trim();
        ScaleFactor = Math.Clamp(scaleFactor, 1, 8);
        Canvas = new PixelOverlayCanvas(Math.Max(64, surfaceWidth), Math.Max(64, surfaceHeight));
        _snapshotRgba = new byte[Canvas.Buffer.Length];
        LastContentDestRect = new HostViewportRect(0, 0, Canvas.Width, Canvas.Height);
        LastSourceCropRect = new HostViewportRect(0, 0, Canvas.Width, Canvas.Height);
        Canvas.Clear(HostOverlayPalette.Surface0);
    }

    /// <summary>Clears the surface canvas to <c>Surface0</c> at the start of a frame.</summary>
    /// <param name="width">Expected source content width.</param>
    /// <param name="height">Expected source content height.</param>
    public void BeginFrame(int width, int height)
    {
        Canvas.Clear(HostOverlayPalette.Surface0);
    }

    /// <summary>
    /// Crops and scales <paramref name="rgba"/> onto <see cref="Canvas"/> using <see cref="OverscanCropPx"/> and <see cref="AspectRatioMode"/>.
    /// </summary>
    /// <param name="width">Source frame width in pixels.</param>
    /// <param name="height">Source frame height in pixels.</param>
    /// <param name="rgba">Tightly packed RGBA8888 source pixels.</param>
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        HostViewportRect srcCrop = HostViewportGeometry.ComputeSourceCropRect(width, height, OverscanCropPx);
        HostViewportRect dst = HostViewportGeometry.ComputeDestinationRect(
            srcCrop.Width, srcCrop.Height, Canvas.Width, Canvas.Height, AspectRatioMode, ContentPixelAspectRatio, IsFmvActive);
        PresentRgba(width, height, rgba, dst, srcCrop);
    }

    /// <summary>
    /// Blits the specified <paramref name="srcCropRect"/> region of <paramref name="rgba"/> into <paramref name="destRect"/>
    /// on <see cref="Canvas"/>, clearing surrounding margins to <c>Surface0</c>.
    /// </summary>
    /// <param name="width">Full source buffer width in pixels.</param>
    /// <param name="height">Full source buffer height in pixels.</param>
    /// <param name="rgba">Tightly packed RGBA8888 source pixels.</param>
    /// <param name="destRect">Destination rectangle in <see cref="Canvas"/> coordinates.</param>
    /// <param name="srcCropRect">Source crop rectangle in <paramref name="rgba"/> coordinates.</param>
    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba, HostViewportRect destRect, HostViewportRect srcCropRect) =>
        PresentRgba(width, height, rgba, destRect, srcCropRect, publishSnapshot: true);

    internal void PresentRgba(
        int width,
        int height,
        ReadOnlySpan<byte> rgba,
        HostViewportRect destRect,
        HostViewportRect srcCropRect,
        bool publishSnapshot)
    {
        if (width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        HostViewportRect src = HostViewportGeometry.ClampSourceRect(width, height, srcCropRect);
        HostViewportRect dst = HostViewportGeometry.ClampDestinationRect(Canvas.Width, Canvas.Height, destRect);
        LastSourceCropRect = src;
        LastContentDestRect = dst;

        Canvas.Clear(HostOverlayPalette.Surface0);
        if (dst.Width > 0 && dst.Height > 0 && src.Width > 0 && src.Height > 0)
        {
            var rect = new Rectangle(dst.X, dst.Y, dst.Width, dst.Height);
            if (UseParallelBandBlit)
            {
                ParallelCpuPresenter.BlitParallelBands(width, height, rgba, Canvas.Buffer, Canvas.Width, Canvas.Height, rect, src);
            }
            else
            {
                BlitNearest(width, height, rgba, rect, src);
            }
        }
        if (publishSnapshot) PublishSnapshot();
    }

    /// <summary>Publishes the current <see cref="Canvas"/> contents into the thread-safe snapshot buffer.</summary>
    public void PublishSnapshot()
    {
        lock (_snapshotSync)
        {
            if (_snapshotRgba.Length != Canvas.Buffer.Length)
            {
                _snapshotRgba = new byte[Canvas.Buffer.Length];
            }
            Buffer.BlockCopy(Canvas.Buffer, 0, _snapshotRgba, 0, Canvas.Buffer.Length);
        }
    }

    /// <summary>Copies the most recently published RGBA composite frame.</summary>
    /// <param name="width">Captured surface width.</param>
    /// <param name="height">Captured surface height.</param>
    /// <param name="rgbaBytes">Newly allocated RGBA8888 pixel buffer.</param>
    /// <returns><c>true</c> if a valid frame was copied; otherwise <c>false</c>.</returns>
    public bool TryCopyRgbaFrame(out int width, out int height, out byte[] rgbaBytes)
    {
        lock (_snapshotSync)
        {
            width = Canvas.Width;
            height = Canvas.Height;
            rgbaBytes = new byte[_snapshotRgba.Length];
            Buffer.BlockCopy(_snapshotRgba, 0, rgbaBytes, 0, _snapshotRgba.Length);
            return rgbaBytes.Length == width * height * 4;
        }
    }

    /// <summary>Copies a top-down RGBA8888 sub-rectangle from the most recently published surface snapshot.</summary>
    public bool TryReadSurfaceRect(int x, int y, int width, int height, Span<byte> rgbaDestination)
    {
        if (_disposed || x < 0 || y < 0 || width <= 0 || height <= 0) return false;
        if (x > Canvas.Width - width || y > Canvas.Height - height) return false;
        long required = (long)width * height * 4L;
        if (required <= 0 || required > rgbaDestination.Length) return false;

        lock (_snapshotSync)
        {
            int surfaceStride = Canvas.Width * 4;
            if (_snapshotRgba.Length < surfaceStride * Canvas.Height) return false;
            int dstStride = width * 4;
            for (int row = 0; row < height; row++)
            {
                int srcOff = ((y + row) * surfaceStride) + (x * 4);
                _snapshotRgba.AsSpan(srcOff, dstStride).CopyTo(rgbaDestination.Slice(row * dstStride, dstStride));
            }
            return true;
        }
    }

    /// <summary>Writes the latest snapshot as a PNG file to <paramref name="outputPath"/>.</summary>
    /// <param name="outputPath">Destination file path.</param>
    public void SavePng(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!TryCopyRgbaFrame(out int w, out int h, out byte[] rgba))
        {
            Canvas.SavePng(outputPath);
            return;
        }
        RgbaPngWriter.WritePng(outputPath, w, h, rgba);
    }

    private unsafe void BlitNearest(int srcW, int srcH, ReadOnlySpan<byte> src, Rectangle dst, HostViewportRect srcCrop)
    {
        fixed (byte* srcPtr = src)
        {
            BlitNearestRowRange(srcW, srcH, srcPtr, src.Length, Canvas.Buffer, Canvas.Width, Canvas.Height, dst, srcCrop, 0, dst.Height);
        }
    }

    internal static unsafe void BlitNearestRowRange(
        int srcW,
        int srcH,
        byte* src,
        int srcByteLength,
        byte[] dstBuf,
        int dstCanvasW,
        int dstCanvasH,
        Rectangle dst,
        int startDy,
        int endDy) =>
        BlitNearestRowRange(srcW, srcH, src, srcByteLength, dstBuf, dstCanvasW, dstCanvasH, dst, new HostViewportRect(0, 0, srcW, srcH), startDy, endDy);

    internal static unsafe void BlitNearestRowRange(
        int srcW,
        int srcH,
        byte* src,
        int srcByteLength,
        byte[] dstBuf,
        int dstCanvasW,
        int dstCanvasH,
        Rectangle dst,
        HostViewportRect srcCrop,
        int startDy,
        int endDy)
    {
        if (src == null || srcW <= 0 || srcH <= 0 || dst.Width <= 0 || dst.Height <= 0) return;
        if (srcByteLength < checked(srcW * srcH * 4)) return;

        HostViewportRect crop = HostViewportGeometry.ClampSourceRect(srcW, srcH, srcCrop);
        int clampedStart = Math.Max(0, startDy);
        int clampedEnd = Math.Min(dst.Height, endDy);
        int dstStride = dstCanvasW * 4;
        int srcStride = srcW * 4;

        for (int dy = clampedStart; dy < clampedEnd; dy++)
        {
            int targetY = dst.Y + dy;
            if ((uint)targetY >= (uint)dstCanvasH) continue;
            int sy = crop.Y + (int)((long)dy * crop.Height / dst.Height);
            int dstRowOffset = (targetY * dstStride) + (dst.X * 4);
            int srcRowOffset = sy * srcStride;

            for (int dx = 0; dx < dst.Width; dx++)
            {
                int targetX = dst.X + dx;
                if ((uint)targetX >= (uint)dstCanvasW) continue;
                int sx = crop.X + (int)((long)dx * crop.Width / dst.Width);
                int sIdx = srcRowOffset + (sx * 4);
                int dIdx = dstRowOffset + (dx * 4);
                dstBuf[dIdx] = src[sIdx];
                dstBuf[dIdx + 1] = src[sIdx + 1];
                dstBuf[dIdx + 2] = src[sIdx + 2];
                dstBuf[dIdx + 3] = 255;
            }
        }
    }

    /// <summary>Releases resources owned by this surface backend.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
