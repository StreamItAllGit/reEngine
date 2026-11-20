// @module: reEngine.Host.Rendering.HostVideoFilterPipeline
// @purpose: Deterministic universal 2D+3D color depth (24-bit vs 15-bit RGB555 + 4x4 Bayer dithering) and 8-preset live presentation filter pipeline.
// @depends-on: System, reEngine.Host.Contracts
// @exports: HostVideoFilterPipeline

using reEngine.Host.Contracts;

namespace reEngine.Host.Rendering;

/// <summary>
/// Pure deterministic C# RGBA span pipeline applying universal 24-bit vs 15-bit Bayer 4x4 dithering
/// and all 8 <see cref="HostVideoFilterMode"/> presentation presets.
/// </summary>
public sealed class HostVideoFilterPipeline
{
    private static readonly int[] Bayer4x4 =
    [
         0,  8,  2, 10,
        12,  4, 14,  6,
         3, 11,  1,  9,
        15,  7, 13,  5
    ];

    private byte[] _quantScratch = Array.Empty<byte>();
    private byte[] _edgeScratch = Array.Empty<byte>();
    private byte[] _outputScratch = Array.Empty<byte>();

    /// <summary>Returns whether the current filter and color flags permit zero-copy fast-path presentation.</summary>
    public static bool IsZeroCopyFastPath(
        HostVideoFilterMode filterMode,
        bool isTrueColor24BitEnabled,
        bool isDitheringEnabled) =>
        filterMode == HostVideoFilterMode.Nearest && isTrueColor24BitEnabled && !isDitheringEnabled;

    internal static (int TargetW, int TargetH) ResolveTargetSize(
        HostVideoFilterMode mode, bool trueColor24, bool dither, int resolutionScale, int contentW, int contentH, HostViewportRect contentDest)
    {
        if (IsZeroCopyFastPath(mode, trueColor24, dither)) return (contentW, contentH);
        if (mode == HostVideoFilterMode.Xbrz) { int s = Math.Clamp(resolutionScale, 2, 4); return (contentW * s, contentH * s); }
        int mult = mode is HostVideoFilterMode.Scanlines or HostVideoFilterMode.CrtLite or HostVideoFilterMode.LcdGrid ? 2 : 1;
        return (Math.Max(contentW * mult, contentDest.Width), Math.Max(contentH * mult, contentDest.Height));
    }

    internal static HostViewportRect ScaleSourceCrop(HostViewportRect crop, int srcW, int srcH, int outW, int outH) =>
        (outW == srcW && outH == srcH) ? crop : new HostViewportRect(
            crop.X * outW / srcW, crop.Y * outH / srcH,
            Math.Max(1, crop.Width * outW / srcW), Math.Max(1, crop.Height * outH / srcH));

    /// <summary>
    /// Applies 15-bit RGB555 quantization and optional 4x4 Bayer ordered dithering across <paramref name="srcRgba"/>.
    /// </summary>
    public static void ApplyColorQuantizationAndDither(
        ReadOnlySpan<byte> srcRgba,
        Span<byte> dstRgba,
        int width,
        int height,
        bool isTrueColor24BitEnabled,
        bool isDitheringEnabled)
    {
        if (width <= 0 || height <= 0) return;
        int needed = checked(width * height * 4);
        if (srcRgba.Length < needed || dstRgba.Length < needed) return;

        if (isTrueColor24BitEnabled && !isDitheringEnabled)
        {
            srcRgba[..needed].CopyTo(dstRgba[..needed]);
            return;
        }

        for (int y = 0; y < height; y++)
        {
            int bayerRow = (y & 3) << 2;
            int rowOff = y * width;
            for (int x = 0; x < width; x++)
            {
                int idx = (rowOff + x) * 4;
                int offset = isDitheringEnabled ? (((Bayer4x4[bayerRow | (x & 3)] * 8) + 4) / 16) - 4 : 0;
                dstRgba[idx] = Quantize5Bit(srcRgba[idx], offset);
                dstRgba[idx + 1] = Quantize5Bit(srcRgba[idx + 1], offset);
                dstRgba[idx + 2] = Quantize5Bit(srcRgba[idx + 2], offset);
                dstRgba[idx + 3] = srcRgba[idx + 3];
            }
        }
    }

    /// <summary>
    /// Processes <paramref name="srcRgba"/> into an output span using internal reusable buffers (zero-copy when fast-path applies).
    /// </summary>
    public ReadOnlySpan<byte> Process(
        ReadOnlySpan<byte> srcRgba,
        int srcWidth,
        int srcHeight,
        int targetWidth,
        int targetHeight,
        HostVideoFilterMode filterMode,
        bool isTrueColor24BitEnabled,
        bool isDitheringEnabled,
        int frameIndex,
        out int outWidth,
        out int outHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || srcRgba.Length < checked(srcWidth * srcHeight * 4))
        {
            outWidth = 0;
            outHeight = 0;
            return ReadOnlySpan<byte>.Empty;
        }

        int dstW = Math.Max(1, targetWidth);
        int dstH = Math.Max(1, targetHeight);
        if (IsZeroCopyFastPath(filterMode, isTrueColor24BitEnabled, isDitheringEnabled) && dstW == srcWidth && dstH == srcHeight)
        {
            outWidth = srcWidth;
            outHeight = srcHeight;
            return srcRgba;
        }

        ReadOnlySpan<byte> colorInput = PrepareColorInput(srcRgba, srcWidth, srcHeight, isTrueColor24BitEnabled, isDitheringEnabled);
        if (filterMode == HostVideoFilterMode.Xbrz)
        {
            int scale = HostEdgeScalerPasses.ResolveScaleFactor(srcWidth, srcHeight, dstW, dstH);
            int edgeW = srcWidth * scale;
            int edgeH = srcHeight * scale;
            int edgeBytes = checked(edgeW * edgeH * 4);
            EnsureBuffer(ref _edgeScratch, edgeBytes);
            HostEdgeScalerPasses.ScaleXbrz(colorInput, srcWidth, srcHeight, _edgeScratch.AsSpan(0, edgeBytes), scale);
            if (dstW == edgeW && dstH == edgeH)
            {
                outWidth = edgeW;
                outHeight = edgeH;
                return _edgeScratch.AsSpan(0, edgeBytes);
            }

            int outBytes = checked(dstW * dstH * 4);
            EnsureBuffer(ref _outputScratch, outBytes);
            BlitBilinear(_edgeScratch.AsSpan(0, edgeBytes), edgeW, edgeH, _outputScratch.AsSpan(0, outBytes), dstW, dstH, sharpFactor: 1.0f);
            outWidth = dstW;
            outHeight = dstH;
            return _outputScratch.AsSpan(0, outBytes);
        }

        int dstBytes = checked(dstW * dstH * 4);
        EnsureBuffer(ref _outputScratch, dstBytes);
        Span<byte> dst = _outputScratch.AsSpan(0, dstBytes);
        DispatchSpatialFilter(colorInput, srcWidth, srcHeight, dst, dstW, dstH, filterMode, frameIndex);
        outWidth = dstW;
        outHeight = dstH;
        return dst;
    }

    /// <summary>Applies color quantization/dithering and the requested spatial filter directly into caller-provided <paramref name="dstRgba"/>.</summary>
    public void ApplyFilter(
        ReadOnlySpan<byte> srcRgba,
        int srcWidth,
        int srcHeight,
        Span<byte> dstRgba,
        int dstWidth,
        int dstHeight,
        HostVideoFilterMode filterMode,
        bool isTrueColor24BitEnabled,
        bool isDitheringEnabled,
        int frameIndex = 0)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0) return;
        int neededDst = checked(dstWidth * dstHeight * 4);
        if (srcRgba.Length < checked(srcWidth * srcHeight * 4) || dstRgba.Length < neededDst) return;

        ReadOnlySpan<byte> colorInput = PrepareColorInput(srcRgba, srcWidth, srcHeight, isTrueColor24BitEnabled, isDitheringEnabled);
        if (filterMode == HostVideoFilterMode.Xbrz)
        {
            int scale = HostEdgeScalerPasses.ResolveScaleFactor(srcWidth, srcHeight, dstWidth, dstHeight);
            int edgeW = srcWidth * scale;
            int edgeH = srcHeight * scale;
            int edgeBytes = checked(edgeW * edgeH * 4);
            if (dstWidth == edgeW && dstHeight == edgeH)
            {
                HostEdgeScalerPasses.ScaleXbrz(colorInput, srcWidth, srcHeight, dstRgba, scale);
                return;
            }
            EnsureBuffer(ref _edgeScratch, edgeBytes);
            HostEdgeScalerPasses.ScaleXbrz(colorInput, srcWidth, srcHeight, _edgeScratch.AsSpan(0, edgeBytes), scale);
            BlitBilinear(_edgeScratch.AsSpan(0, edgeBytes), edgeW, edgeH, dstRgba, dstWidth, dstHeight, sharpFactor: 1.0f);
            return;
        }

        DispatchSpatialFilter(colorInput, srcWidth, srcHeight, dstRgba, dstWidth, dstHeight, filterMode, frameIndex);
    }

    internal static void BlitNearest(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH)
        {
            src[..checked(srcW * srcH * 4)].CopyTo(dst);
            return;
        }
        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = Math.Clamp((int)((long)dy * srcH / dstH), 0, srcH - 1);
            int sRow = sy * srcW * 4;
            int dRow = dy * dstW * 4;
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = Math.Clamp((int)((long)dx * srcW / dstW), 0, srcW - 1);
                int sIdx = sRow + (sx * 4);
                int dIdx = dRow + (dx * 4);
                dst[dIdx] = src[sIdx];
                dst[dIdx + 1] = src[sIdx + 1];
                dst[dIdx + 2] = src[sIdx + 2];
                dst[dIdx + 3] = src[sIdx + 3];
            }
        }
    }

    internal static void BlitBilinear(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH, float sharpFactor)
    {
        for (int dy = 0; dy < dstH; dy++)
        {
            for (int dx = 0; dx < dstW; dx++)
            {
                HostRetroFilterPasses.SampleBilinearRgb(src, srcW, srcH, dx, dy, dstW, dstH, sharpFactor, out float r, out float g, out float b, out byte a);
                int dIdx = ((dy * dstW) + dx) * 4;
                dst[dIdx] = HostRetroFilterPasses.ClampByte(r);
                dst[dIdx + 1] = HostRetroFilterPasses.ClampByte(g);
                dst[dIdx + 2] = HostRetroFilterPasses.ClampByte(b);
                dst[dIdx + 3] = a;
            }
        }
    }

    private ReadOnlySpan<byte> PrepareColorInput(ReadOnlySpan<byte> srcRgba, int srcW, int srcH, bool trueColor24, bool dither)
    {
        if (trueColor24 && !dither) return srcRgba;
        int srcBytes = checked(srcW * srcH * 4);
        EnsureBuffer(ref _quantScratch, srcBytes);
        ApplyColorQuantizationAndDither(srcRgba, _quantScratch.AsSpan(0, srcBytes), srcW, srcH, trueColor24, dither);
        return _quantScratch.AsSpan(0, srcBytes);
    }

    private static void DispatchSpatialFilter(
        ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH, HostVideoFilterMode mode, int frameIndex)
    {
        switch (mode)
        {
            case HostVideoFilterMode.Bilinear:
                BlitBilinear(src, srcW, srcH, dst, dstW, dstH, sharpFactor: 1.0f);
                break;
            case HostVideoFilterMode.SharpBilinear:
                BlitBilinear(src, srcW, srcH, dst, dstW, dstH, sharpFactor: MathF.Max(2.0f, MathF.Min((float)dstW / srcW, (float)dstH / srcH)));
                break;
            case HostVideoFilterMode.Scanlines:
                HostRetroFilterPasses.ApplyScanlines(src, srcW, srcH, dst, dstW, dstH);
                break;
            case HostVideoFilterMode.CrtLite:
                HostRetroFilterPasses.ApplyCrtLite(src, srcW, srcH, dst, dstW, dstH);
                break;
            case HostVideoFilterMode.LcdGrid:
                HostRetroFilterPasses.ApplyLcdGrid(src, srcW, srcH, dst, dstW, dstH);
                break;
            case HostVideoFilterMode.NtscComposite:
                HostRetroFilterPasses.ApplyNtscComposite(src, srcW, srcH, dst, dstW, dstH, frameIndex);
                break;
            default:
                BlitNearest(src, srcW, srcH, dst, dstW, dstH);
                break;
        }
    }

    private static byte Quantize5Bit(byte channel, int ditherOffset)
    {
        int q5 = Math.Clamp(channel + ditherOffset, 0, 255) >> 3;
        return (byte)((q5 << 3) | (q5 >> 2));
    }

    private static void EnsureBuffer(ref byte[] buffer, int neededBytes)
    {
        if (buffer.Length < neededBytes) buffer = new byte[neededBytes];
    }
}
