// @module: reEngine.Host.Rendering.HostEdgeScalerPasses
// @purpose: Deterministic pure-C# 2x/3x/4x edge-aware pixel art scaler (xBRZ) comparing perceptual YCbCr diagonal color weights.
// @depends-on: System
// @exports: HostEdgeScalerPasses

using reEngine.Host.Contracts;

namespace reEngine.Host.Rendering;

/// <summary>
/// Deterministic <c>2x</c>/<c>3x</c>/<c>4x</c> edge-aware pixel art scaler (<see cref="HostVideoFilterMode.Xbrz"/>).
/// </summary>
public static class HostEdgeScalerPasses
{
    private const float SimilarityThreshold = 42.0f;

    /// <summary>Selects <c>2</c>, <c>3</c>, or <c>4</c> scale factor based on requested target dimensions.</summary>
    public static int ResolveScaleFactor(int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW <= 0 || srcH <= 0) return 2;
        if (dstW >= srcW * 4 && dstH >= srcH * 4) return 4;
        if (dstW >= srcW * 3 && dstH >= srcH * 3) return 3;
        return 2;
    }

    /// <summary>Applies deterministic <c>2x</c>, <c>3x</c>, or <c>4x</c> edge-aware scaling into <paramref name="dstRgba"/>.</summary>
    public static void ScaleXbrz(ReadOnlySpan<byte> srcRgba, int srcW, int srcH, Span<byte> dstRgba, int scale)
    {
        if (srcW <= 0 || srcH <= 0) return;
        int s = Math.Clamp(scale, 2, 4);
        int outW = srcW * s;
        int outH = srcH * s;
        if (srcRgba.Length < checked(srcW * srcH * 4) || dstRgba.Length < checked(outW * outH * 4)) return;

        for (int y = 0; y < srcH; y++)
        {
            for (int x = 0; x < srcW; x++)
            {
                uint a = ReadPx(srcRgba, srcW, srcH, x - 1, y - 1);
                uint b = ReadPx(srcRgba, srcW, srcH, x, y - 1);
                uint c = ReadPx(srcRgba, srcW, srcH, x + 1, y - 1);
                uint d = ReadPx(srcRgba, srcW, srcH, x - 1, y);
                uint e = ReadPx(srcRgba, srcW, srcH, x, y);
                uint f = ReadPx(srcRgba, srcW, srcH, x + 1, y);
                uint g = ReadPx(srcRgba, srcW, srcH, x - 1, y + 1);
                uint h = ReadPx(srcRgba, srcW, srcH, x, y + 1);
                uint i = ReadPx(srcRgba, srcW, srcH, x + 1, y + 1);
                uint b2 = ReadPx(srcRgba, srcW, srcH, x, y - 2);
                uint d2 = ReadPx(srcRgba, srcW, srcH, x - 2, y);
                uint f2 = ReadPx(srcRgba, srcW, srcH, x + 2, y);
                uint h2 = ReadPx(srcRgba, srcW, srcH, x, y + 2);

                FillBlock(dstRgba, outW, x * s, y * s, s, e);

                BlendCorner(dstRgba, outW, x * s, y * s, s, 0, 0, 1, 1, e, d, b, a, d2, b2, c, g, f, h);
                BlendCorner(dstRgba, outW, x * s, y * s, s, s - 1, 0, -1, 1, e, f, b, c, f2, b2, a, i, d, h);
                BlendCorner(dstRgba, outW, x * s, y * s, s, 0, s - 1, 1, -1, e, d, h, g, d2, h2, a, i, f, b);
                BlendCorner(dstRgba, outW, x * s, y * s, s, s - 1, s - 1, -1, -1, e, f, h, i, f2, h2, c, g, d, b);
            }
        }
    }

    private static void BlendCorner(
        Span<byte> dst,
        int outW,
        int bx,
        int by,
        int s,
        int cx,
        int cy,
        int sx,
        int sy,
        uint e,
        uint side1,
        uint side2,
        uint diag,
        uint s1Ext,
        uint s2Ext,
        uint opp1,
        uint opp2,
        uint oppS1,
        uint oppS2)
    {
        if (AreSimilar(e, side1) || AreSimilar(e, side2)) return;

        float wDiagEdge = ColorDistance(e, opp1) + ColorDistance(e, opp2)
            + ColorDistance(diag, s1Ext) + ColorDistance(diag, s2Ext)
            + (4.0f * ColorDistance(side1, side2));
        float wCrossEdge = ColorDistance(side1, oppS1) + ColorDistance(side2, oppS2)
            + (4.0f * ColorDistance(e, diag));
        if (wDiagEdge >= wCrossEdge) return;

        uint blendCol = ColorDistance(e, side1) <= ColorDistance(e, side2) ? side1 : side2;
        bool shallow = ColorDistance(e, side2) < ColorDistance(e, side1) * 0.75f;
        bool steep = ColorDistance(e, side1) < ColorDistance(e, side2) * 0.75f;

        if (s == 2)
        {
            WritePx(dst, outW, bx + cx, by + cy, LerpColor(e, blendCol, 0.5f));
            return;
        }

        if (s == 3)
        {
            WritePx(dst, outW, bx + cx, by + cy, LerpColor(e, blendCol, 0.75f));
            if (shallow) WritePx(dst, outW, bx + cx + sx, by + cy, LerpColor(e, blendCol, 0.25f));
            if (steep) WritePx(dst, outW, bx + cx, by + cy + sy, LerpColor(e, blendCol, 0.25f));
            return;
        }

        WritePx(dst, outW, bx + cx, by + cy, LerpColor(e, blendCol, 0.875f));
        WritePx(dst, outW, bx + cx + sx, by + cy, LerpColor(e, blendCol, shallow ? 0.50f : 0.25f));
        WritePx(dst, outW, bx + cx, by + cy + sy, LerpColor(e, blendCol, steep ? 0.50f : 0.25f));
        WritePx(dst, outW, bx + cx + sx, by + cy + sy, LerpColor(e, blendCol, 0.125f));
    }

    private static void FillBlock(Span<byte> dst, int outW, int bx, int by, int s, uint rgba)
    {
        for (int dy = 0; dy < s; dy++)
        {
            for (int dx = 0; dx < s; dx++)
            {
                WritePx(dst, outW, bx + dx, by + dy, rgba);
            }
        }
    }

    private static uint ReadPx(ReadOnlySpan<byte> src, int w, int h, int x, int y)
    {
        int cx = Math.Clamp(x, 0, w - 1);
        int cy = Math.Clamp(y, 0, h - 1);
        int idx = ((cy * w) + cx) * 4;
        return ((uint)src[idx] << 24) | ((uint)src[idx + 1] << 16) | ((uint)src[idx + 2] << 8) | src[idx + 3];
    }

    private static void WritePx(Span<byte> dst, int outW, int x, int y, uint rgba)
    {
        int idx = ((y * outW) + x) * 4;
        dst[idx] = (byte)(rgba >> 24);
        dst[idx + 1] = (byte)(rgba >> 16);
        dst[idx + 2] = (byte)(rgba >> 8);
        dst[idx + 3] = (byte)rgba;
    }

    private static bool AreSimilar(uint c1, uint c2) => c1 == c2 || ColorDistance(c1, c2) < SimilarityThreshold;

    private static float ColorDistance(uint c1, uint c2)
    {
        if (c1 == c2) return 0f;
        float dr = (int)(c1 >> 24) - (int)(c2 >> 24);
        float dg = (int)((c1 >> 16) & 0xFF) - (int)((c2 >> 16) & 0xFF);
        float db = (int)((c1 >> 8) & 0xFF) - (int)((c2 >> 8) & 0xFF);
        float y = (0.299f * dr) + (0.587f * dg) + (0.114f * db);
        float u = (-0.169f * dr) - (0.331f * dg) + (0.500f * db);
        float v = (0.500f * dr) - (0.419f * dg) - (0.081f * db);
        return MathF.Sqrt((48.0f * y * y) + (7.0f * u * u) + (6.0f * v * v));
    }

    private static uint LerpColor(uint c1, uint c2, float t)
    {
        float inv = 1.0f - t;
        byte r = (byte)Math.Clamp((int)MathF.Round(((c1 >> 24) * inv) + ((c2 >> 24) * t)), 0, 255);
        byte g = (byte)Math.Clamp((int)MathF.Round((((c1 >> 16) & 0xFF) * inv) + (((c2 >> 16) & 0xFF) * t)), 0, 255);
        byte b = (byte)Math.Clamp((int)MathF.Round((((c1 >> 8) & 0xFF) * inv) + (((c2 >> 8) & 0xFF) * t)), 0, 255);
        byte a = (byte)Math.Clamp((int)MathF.Round(((c1 & 0xFF) * inv) + ((c2 & 0xFF) * t)), 0, 255);
        return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
    }
}
