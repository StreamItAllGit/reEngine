// @module: reEngine.Host.Core.HostRgbaSurfaceBackend
// @purpose: Reusable RGBA framebuffer composite backend directly atop Foundation.Core.PixelOverlayCanvas and RgbaPngWriter.
// @depends-on: System, System.Drawing, Foundation.Core, Foundation.Retro, reEngine.Host.Contracts, reEngine.Host.Ui
// @exports: HostRgbaSurfaceBackend

using System.Drawing;
using Foundation.Core;
using Foundation.Retro;
using reEngine.Host.Contracts;
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

    public string BackendId { get; }
    public int ScaleFactor { get; set; }
    public int SurfaceWidth => Canvas.Width;
    public int SurfaceHeight => Canvas.Height;
    public PixelOverlayCanvas Canvas { get; }

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
        Canvas.Clear(HostOverlayPalette.Surface0);
    }

    public void BeginFrame(int width, int height)
    {
        Canvas.Clear(HostOverlayPalette.Surface0);
    }

    public void PresentRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4)) return;

        Rectangle dst = HostOverlayLayoutHelpers.ComputeAspectFitRect(width, height, Canvas.Width, Canvas.Height, ScaleFactor);
        BlitNearest(width, height, rgba, dst);
        PublishSnapshot();
    }

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

    private void BlitNearest(int srcW, int srcH, ReadOnlySpan<byte> src, Rectangle dst)
    {
        byte[] dstBuf = Canvas.Buffer;
        int dstStride = Canvas.Width * 4;
        int srcStride = srcW * 4;

        for (int dy = 0; dy < dst.Height; dy++)
        {
            int targetY = dst.Y + dy;
            if ((uint)targetY >= (uint)Canvas.Height) continue;
            int sy = (int)((long)dy * srcH / dst.Height);
            int dstRowOffset = (targetY * dstStride) + (dst.X * 4);
            int srcRowOffset = sy * srcStride;

            for (int dx = 0; dx < dst.Width; dx++)
            {
                int targetX = dst.X + dx;
                if ((uint)targetX >= (uint)Canvas.Width) continue;
                int sx = (int)((long)dx * srcW / dst.Width);
                int sIdx = srcRowOffset + (sx * 4);
                int dIdx = dstRowOffset + (dx * 4);
                dstBuf[dIdx] = src[sIdx];
                dstBuf[dIdx + 1] = src[sIdx + 1];
                dstBuf[dIdx + 2] = src[sIdx + 2];
                dstBuf[dIdx + 3] = 255;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
