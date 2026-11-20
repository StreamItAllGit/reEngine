// @module: reEngine.Host.Rendering.HostRetroFilterPasses
// @purpose: Pure-C# Scanlines, CrtLite (aperture grille + scanlines + vignette), LcdGrid (RGB cell mask), and NtscComposite (YIQ chroma bleed + phase shift) passes.
// @depends-on: System, System.Buffers
// @exports: HostRetroFilterPasses

using System.Buffers;

namespace reEngine.Host.Rendering;

/// <summary>
/// Deterministic retro CRT, LCD, Scanline, and NTSC Composite YIQ presentation filter passes.
/// </summary>
public static class HostRetroFilterPasses
{
    /// <summary>Applies alternating horizontal scanline modulation.</summary>
    public static void ApplyScanlines(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH)
    {
        if (!ValidateBuffers(src, srcW, srcH, dst, dstW, dstH)) return;
        for (int dy = 0; dy < dstH; dy++)
        {
            float scan = ComputeScanlineBeam(dy, srcH, dstH);
            int sy = Math.Clamp((int)((long)dy * srcH / dstH), 0, srcH - 1);
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = Math.Clamp((int)((long)dx * srcW / dstW), 0, srcW - 1);
                int sIdx = ((sy * srcW) + sx) * 4;
                int dIdx = ((dy * dstW) + dx) * 4;
                dst[dIdx] = ClampByte(src[sIdx] * scan);
                dst[dIdx + 1] = ClampByte(src[sIdx + 1] * scan);
                dst[dIdx + 2] = ClampByte(src[sIdx + 2] * scan);
                dst[dIdx + 3] = src[sIdx + 3];
            }
        }
    }

    /// <summary>Applies <c>CrtLite</c>: sharp bilinear sampling + scanline beam + aperture grille triad + subtle vignette.</summary>
    public static void ApplyCrtLite(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH)
    {
        if (!ValidateBuffers(src, srcW, srcH, dst, dstW, dstH)) return;
        float sharp = MathF.Max(2.0f, MathF.Min((float)dstW / srcW, (float)dstH / srcH));
        for (int dy = 0; dy < dstH; dy++)
        {
            float scan = ComputeScanlineBeam(dy, srcH, dstH) * 1.04f;
            float ny = (((dy + 0.5f) / dstH) * 2.0f) - 1.0f;
            for (int dx = 0; dx < dstW; dx++)
            {
                float nx = (((dx + 0.5f) / dstW) * 2.0f) - 1.0f;
                float vig = Math.Clamp(1.0f - (0.16f * ((nx * nx) + (ny * ny)) * 0.5f), 0.72f, 1.0f);
                SampleBilinearRgb(src, srcW, srcH, dx, dy, dstW, dstH, sharp, out float r, out float g, out float b, out byte a);

                int triad = dx % 3;
                float gr = triad == 0 ? 1.08f : 0.86f;
                float gg = triad == 1 ? 1.08f : 0.86f;
                float gb = triad == 2 ? 1.08f : 0.86f;

                float gain = scan * vig;
                int dIdx = ((dy * dstW) + dx) * 4;
                dst[dIdx] = ClampByte(r * gain * gr);
                dst[dIdx + 1] = ClampByte(g * gain * gg);
                dst[dIdx + 2] = ClampByte(b * gain * gb);
                dst[dIdx + 3] = a;
            }
        }
    }

    /// <summary>Applies <c>LcdGrid</c>: crisp pixel cells with dark subpixel border + RGB triad mask.</summary>
    public static void ApplyLcdGrid(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH)
    {
        if (!ValidateBuffers(src, srcW, srcH, dst, dstW, dstH)) return;
        bool upscaled = dstW >= srcW * 2 && dstH >= srcH * 2;
        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = Math.Clamp((int)((long)dy * srcH / dstH), 0, srcH - 1);
            float cellY = upscaled ? (((dy + 0.5f) * srcH / dstH) % 1.0f) : 0.5f;
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = Math.Clamp((int)((long)dx * srcW / dstW), 0, srcW - 1);
                float cellX = upscaled ? (((dx + 0.5f) * srcW / dstW) % 1.0f) : 0.5f;
                float border = upscaled
                    ? (cellX > 0.80f || cellY > 0.80f ? 0.72f : 1.0f)
                    : (((dx & 1) == 1 && (dy & 1) == 1) ? 0.78f : 1.0f);

                int triad = dx % 3;
                float sr = triad == 0 ? 1.06f : 0.88f;
                float sg = triad == 1 ? 1.06f : 0.88f;
                float sb = triad == 2 ? 1.06f : 0.88f;

                int sIdx = ((sy * srcW) + sx) * 4;
                int dIdx = ((dy * dstW) + dx) * 4;
                dst[dIdx] = ClampByte(src[sIdx] * border * sr);
                dst[dIdx + 1] = ClampByte(src[sIdx + 1] * border * sg);
                dst[dIdx + 2] = ClampByte(src[sIdx + 2] * border * sb);
                dst[dIdx + 3] = src[sIdx + 3];
            }
        }
    }

    /// <summary>
    /// Applies <c>NtscComposite</c>: RGB-&gt;YIQ conversion, horizontal chroma bleed, subcarrier scanline phase shift, and YIQ-&gt;RGB decode.
    /// </summary>
    public static void ApplyNtscComposite(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH, int frameIndex)
    {
        if (!ValidateBuffers(src, srcW, srcH, dst, dstW, dstH)) return;

        int pixelCount = checked(srcW * srcH);
        int byteCount = checked(pixelCount * 4);
        float[] yBuf = ArrayPool<float>.Shared.Rent(pixelCount);
        float[] iBuf = ArrayPool<float>.Shared.Rent(pixelCount);
        float[] qBuf = ArrayPool<float>.Shared.Rent(pixelCount);
        byte[] ntscNative = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            for (int y = 0; y < srcH; y++)
            {
                int rowOff = y * srcW;
                for (int x = 0; x < srcW; x++)
                {
                    int sIdx = (rowOff + x) * 4;
                    float r = src[sIdx];
                    float g = src[sIdx + 1];
                    float b = src[sIdx + 2];
                    int pIdx = rowOff + x;
                    yBuf[pIdx] = (0.299f * r) + (0.587f * g) + (0.114f * b);
                    iBuf[pIdx] = (0.596f * r) - (0.274f * g) - (0.322f * b);
                    qBuf[pIdx] = (0.211f * r) - (0.523f * g) + (0.312f * b);
                }
            }

            float framePhase = (frameIndex & 1) * MathF.PI;
            for (int y = 0; y < srcH; y++)
            {
                int rowOff = y * srcW;
                float linePhase = (y * (2.0f * MathF.PI / 3.0f)) + framePhase;
                float lineScan = (y & 1) == 1 ? 0.92f : 1.0f;

                for (int x = 0; x < srcW; x++)
                {
                    int xm2 = Math.Max(0, x - 2), xm1 = Math.Max(0, x - 1);
                    int xp1 = Math.Min(srcW - 1, x + 1), xp2 = Math.Min(srcW - 1, x + 2);

                    float lum = (0.12f * yBuf[rowOff + xm1]) + (0.76f * yBuf[rowOff + x]) + (0.12f * yBuf[rowOff + xp1]);
                    float chrI = (0.15f * iBuf[rowOff + xm2]) + (0.22f * iBuf[rowOff + xm1]) + (0.26f * iBuf[rowOff + x])
                               + (0.22f * iBuf[rowOff + xp1]) + (0.15f * iBuf[rowOff + xp2]);
                    float chrQ = (0.15f * qBuf[rowOff + xm2]) + (0.22f * qBuf[rowOff + xm1]) + (0.26f * qBuf[rowOff + x])
                               + (0.22f * qBuf[rowOff + xp1]) + (0.15f * qBuf[rowOff + xp2]);

                    float phi = linePhase + (x * (2.0f * MathF.PI / 3.0f));
                    float edgeDelta = yBuf[rowOff + x] - yBuf[rowOff + xm1];
                    lum = Math.Clamp(lum + (0.06f * (iBuf[rowOff + x] - iBuf[rowOff + xm1]) * MathF.Cos(phi)), 0f, 255f) * lineScan;
                    chrI += 0.08f * edgeDelta * MathF.Sin(phi);
                    chrQ += 0.08f * edgeDelta * MathF.Cos(phi);

                    int dIdx = (rowOff + x) * 4;
                    ntscNative[dIdx] = ClampByte(lum + (0.956f * chrI) + (0.621f * chrQ));
                    ntscNative[dIdx + 1] = ClampByte(lum - (0.272f * chrI) - (0.647f * chrQ));
                    ntscNative[dIdx + 2] = ClampByte(lum - (1.106f * chrI) + (1.703f * chrQ));
                    ntscNative[dIdx + 3] = src[dIdx + 3];
                }
            }

            HostVideoFilterPipeline.BlitBilinear(ntscNative.AsSpan(0, byteCount), srcW, srcH, dst, dstW, dstH, sharpFactor: 1.0f);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(yBuf);
            ArrayPool<float>.Shared.Return(iBuf);
            ArrayPool<float>.Shared.Return(qBuf);
            ArrayPool<byte>.Shared.Return(ntscNative);
        }
    }

    internal static void SampleBilinearRgb(
        ReadOnlySpan<byte> src, int srcW, int srcH, int dx, int dy, int dstW, int dstH, float sharpFactor,
        out float r, out float g, out float b, out byte a)
    {
        float u = (((dx + 0.5f) * srcW) / dstW) - 0.5f;
        float v = (((dy + 0.5f) * srcH) / dstH) - 0.5f;
        int x0 = Math.Clamp((int)MathF.Floor(u), 0, srcW - 1);
        int y0 = Math.Clamp((int)MathF.Floor(v), 0, srcH - 1);
        int x1 = Math.Min(srcW - 1, x0 + 1);
        int y1 = Math.Min(srcH - 1, y0 + 1);
        float fx = u - MathF.Floor(u);
        float fy = v - MathF.Floor(v);
        if (sharpFactor > 1.0f)
        {
            fx = Math.Clamp(((fx - 0.5f) * sharpFactor) + 0.5f, 0f, 1f);
            fy = Math.Clamp(((fy - 0.5f) * sharpFactor) + 0.5f, 0f, 1f);
        }

        int i00 = ((y0 * srcW) + x0) * 4, i10 = ((y0 * srcW) + x1) * 4;
        int i01 = ((y1 * srcW) + x0) * 4, i11 = ((y1 * srcW) + x1) * 4;
        float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
        r = (src[i00] * w00) + (src[i10] * w10) + (src[i01] * w01) + (src[i11] * w11);
        g = (src[i00 + 1] * w00) + (src[i10 + 1] * w10) + (src[i01 + 1] * w01) + (src[i11 + 1] * w11);
        b = (src[i00 + 2] * w00) + (src[i10 + 2] * w10) + (src[i01 + 2] * w01) + (src[i11 + 2] * w11);
        a = src[i00 + 3];
    }

    private static float ComputeScanlineBeam(int dy, int srcH, int dstH)
    {
        if (dstH <= srcH) return (dy & 1) == 1 ? 0.64f : 1.0f;
        float syF = ((dy + 0.5f) * srcH) / dstH;
        float phase = syF - MathF.Floor(syF);
        return 0.60f + (0.40f * MathF.Sin(phase * MathF.PI));
    }

    private static bool ValidateBuffers(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH) =>
        srcW > 0 && srcH > 0 && dstW > 0 && dstH > 0
        && src.Length >= checked(srcW * srcH * 4)
        && dst.Length >= checked(dstW * dstH * 4);

    internal static byte ClampByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
