// @module: reEngine.Host.Ui.HostOverlayTextRenderer
// @purpose: Crisp 2x2 coverage-sampled scalable 5x7 ASCII bitmap text renderer atop Foundation.Core.PixelOverlayCanvas.
// @depends-on: System, Foundation.Core
// @exports: HostOverlayTextRenderer

using Foundation.Core;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders legible, coverage-sampled scaled bitmap text onto <see cref="PixelOverlayCanvas"/> using an intermediate
/// <see cref="BaseScale"/> (<c>1.45x</c>) multiplied by user <c>uiTextScale</c> (<c>0.75x..2.0x</c>).
/// </summary>
public static class HostOverlayTextRenderer
{
    /// <summary>Base legibility multiplier producing ~10-11px glyphs at <c>uiTextScale = 1.0</c>.</summary>
    public const double BaseScale = 1.45;

    /// <summary>Minimum allowed user text scale multiplier.</summary>
    public const double MinUiTextScale = 0.75;

    /// <summary>Maximum allowed user text scale multiplier.</summary>
    public const double MaxUiTextScale = 2.0;

    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphAdvanceX = 6;
    private const int GlyphAdvanceY = 9;

    // Packed 5x7 ASCII bitmaps (7 rows x 5 bits, MSB row first) for printable ASCII 0x20..0x7E.
    private static readonly ulong[] Font5x7 =
    [
        0x00000000000000, 0x04040404000400, 0x0A0A0000000000, 0x0A1F0A0A1F0A00,
        0x040F140E051E04, 0x191A04080B1300, 0x0C12140815120D, 0x04040000000000,
        0x02040808080402, 0x08040202020408, 0x000A041F040A00, 0x0004041F040400,
        0x00000000000408, 0x0000001F000000, 0x00000000000400, 0x01020408100000,
        0x0E11131519110E, 0x040C040404040E, 0x0E11010608101F, 0x0E11010601110E,
        0x02060A121F0202, 0x1F101E0101110E, 0x0608101E11110E, 0x1F010204080808,
        0x0E11110E11110E, 0x0E11110F01020C, 0x00040000000400, 0x00040000000408,
        0x02040810080402, 0x00001F001F0000, 0x08040201020408, 0x0E110204040004,
        0x0E11171517100E, 0x0E11111F111111, 0x1E11111E11111E, 0x0E11101010110E,
        0x1E11111111111E, 0x1F10101E10101F, 0x1F10101E101010, 0x0E11101711110F,
        0x1111111F111111, 0x0E04040404040E, 0x0702020212120C, 0x11121418141211,
        0x1010101010101F, 0x111B1515111111, 0x11191513111111, 0x0E11111111110E,
        0x1E11111E101010, 0x0E11111115120D, 0x1E11111E141211, 0x0F10100E01011E,
        0x1F040404040404, 0x1111111111110E, 0x11111111110A04, 0x11111115151B11,
        0x11110A040A1111, 0x11110A04040404, 0x1F01020408101F, 0x0E08080808080E,
        0x10080402010000, 0x0E02020202020E, 0x040A1100000000, 0x0000000000001F,
        0x08040000000000, 0x00000E010F110F, 0x10101E1111111E, 0x00000E1110110E,
        0x01010F1111110F, 0x00000E111F100E, 0x0609081C080808, 0x000F11110F010E,
        0x10101619111111, 0x04000C0404040E, 0x0200060202120C, 0x10101214181412,
        0x0C04040404040E, 0x00001A15151111, 0x00001E11111111, 0x00000E1111110E,
        0x00001E111E1010, 0x00000F110F0101, 0x00001619101010, 0x00000F100E011E,
        0x08081E08080906, 0x0000111111130D, 0x00001111110A04, 0x0000111115150A,
        0x0000110A040A11, 0x000011110F010E, 0x00001F0204081F, 0x06080810080806,
        0x04040404040404, 0x0C02020102020C, 0x00000815020000
    ];

    /// <summary>Clamps <paramref name="uiTextScale"/> cleanly to <c>[0.75, 2.0]</c>.</summary>
    public static double ClampUiTextScale(double uiTextScale) =>
        double.IsFinite(uiTextScale) ? Math.Clamp(uiTextScale, MinUiTextScale, MaxUiTextScale) : 1.0;

    /// <summary>Returns recommended vertical line advance in pixels for <paramref name="uiTextScale"/>.</summary>
    public static int GetLineHeight(double uiTextScale = 1.0) =>
        Math.Max(9, (int)Math.Ceiling(GlyphAdvanceY * ResolveEffectiveScale(uiTextScale)) + 1);

    /// <summary>Returns recommended interactive button/row height in pixels for <paramref name="uiTextScale"/>.</summary>
    public static int GetRowHeight(double uiTextScale = 1.0) =>
        Math.Clamp((int)Math.Round(24.0 * ClampUiTextScale(uiTextScale)), 20, 48);

    /// <summary>Measures pixel bounds of <paramref name="text"/> at <paramref name="uiTextScale"/>.</summary>
    public static (int Width, int Height) MeasureText(string? text, double uiTextScale = 1.0)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        double s = ResolveEffectiveScale(uiTextScale);
        int maxCols = 0;
        int cols = 0;
        int lines = 1;

        foreach (char ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                maxCols = Math.Max(maxCols, cols);
                cols = 0;
                lines++;
                continue;
            }
            cols++;
        }

        maxCols = Math.Max(maxCols, cols);
        if (maxCols == 0) return (0, Math.Max(1, (int)Math.Ceiling(lines * GlyphHeight * s)));
        int w = Math.Max(1, (int)Math.Ceiling(((maxCols * GlyphAdvanceX) - 1) * s));
        int h = Math.Max(1, (int)Math.Ceiling(((lines * GlyphAdvanceY) - 2) * s));
        return (w, h);
    }

    /// <summary>Fits single-line <paramref name="text"/> inside <paramref name="maxWidthPx"/> with <c>".."</c> ellipsis if needed.</summary>
    public static string FitText(string? text, int maxWidthPx, double uiTextScale = 1.0)
    {
        if (string.IsNullOrEmpty(text) || maxWidthPx <= 0) return string.Empty;
        if (MeasureText(text, uiTextScale).Width <= maxWidthPx) return text;

        double s = ResolveEffectiveScale(uiTextScale);
        int estMax = Math.Clamp((int)(maxWidthPx / (GlyphAdvanceX * s)) + 2, 1, text.Length);
        for (int len = estMax; len >= 1; len--)
        {
            string candidate = len > 2 ? text[..(len - 2)] + ".." : text[..len];
            if (MeasureText(candidate, uiTextScale).Width <= maxWidthPx) return candidate;
        }
        return string.Empty;
    }

    /// <summary>Renders 2x2 coverage-sampled text onto <paramref name="canvas"/>.</summary>
    public static void DrawText(
        PixelOverlayCanvas canvas,
        int x,
        int y,
        string? text,
        RgbaColor color,
        double uiTextScale = 1.0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (string.IsNullOrEmpty(text) || color.A == 0) return;

        double s = ResolveEffectiveScale(uiTextScale);
        double advX = GlyphAdvanceX * s;
        double advY = GlyphAdvanceY * s;
        int dstW = Math.Max(1, (int)Math.Ceiling(GlyphWidth * s));
        int dstH = Math.Max(1, (int)Math.Ceiling(GlyphHeight * s));

        double cursorX = x;
        double cursorY = y;
        foreach (char rawChar in text)
        {
            if (rawChar == '\r') continue;
            if (rawChar == '\n')
            {
                cursorX = x;
                cursorY += advY;
                continue;
            }

            ulong glyph = GetGlyph(NormalizeChar(rawChar));
            if (glyph != 0)
            {
                DrawGlyph2x2(canvas, (int)Math.Round(cursorX), (int)Math.Round(cursorY), glyph, s, dstW, dstH, color);
            }
            cursorX += advX;
        }
    }

    private static double ResolveEffectiveScale(double uiTextScale) => BaseScale * ClampUiTextScale(uiTextScale);

    private static void DrawGlyph2x2(
        PixelOverlayCanvas canvas,
        int gx,
        int gy,
        ulong glyph,
        double s,
        int dstW,
        int dstH,
        RgbaColor color)
    {
        for (int dy = 0; dy < dstH; dy++)
        {
            int py = gy + dy;
            if ((uint)py >= (uint)canvas.Height) continue;

            double sy0 = (dy + 0.25) / s;
            double sy1 = (dy + 0.75) / s;
            int r0 = (int)Math.Floor(sy0);
            int r1 = (int)Math.Floor(sy1);
            int rowBits0 = (uint)r0 < GlyphHeight ? (int)((glyph >> ((GlyphHeight - 1 - r0) * 8)) & 0x1F) : 0;
            int rowBits1 = (uint)r1 < GlyphHeight ? (int)((glyph >> ((GlyphHeight - 1 - r1) * 8)) & 0x1F) : 0;
            if (rowBits0 == 0 && rowBits1 == 0) continue;

            for (int dx = 0; dx < dstW; dx++)
            {
                int px = gx + dx;
                if ((uint)px >= (uint)canvas.Width) continue;

                double sx0 = (dx + 0.25) / s;
                double sx1 = (dx + 0.75) / s;
                int c0 = (int)Math.Floor(sx0);
                int c1 = (int)Math.Floor(sx1);

                int hits = 0;
                if ((uint)c0 < GlyphWidth)
                {
                    int mask0 = 1 << (GlyphWidth - 1 - c0);
                    if ((rowBits0 & mask0) != 0) hits++;
                    if ((rowBits1 & mask0) != 0) hits++;
                }
                if ((uint)c1 < GlyphWidth)
                {
                    int mask1 = 1 << (GlyphWidth - 1 - c1);
                    if ((rowBits0 & mask1) != 0) hits++;
                    if ((rowBits1 & mask1) != 0) hits++;
                }

                if (hits == 0) continue;
                byte alpha = hits == 4 ? color.A : (byte)Math.Clamp(((color.A * hits) + 2) / 4, 1, 255);
                canvas.BlendSrcOver(px, py, color with { A = alpha });
            }
        }
    }

    private static char NormalizeChar(char ch) => ch switch
    {
        '\u2014' or '\u2013' => '-',
        '\u2192' => '>',
        '\u2190' => '<',
        '\u00D7' => 'x',
        _ => ch
    };

    private static ulong GetGlyph(char ch)
    {
        int idx = ch - ' ';
        return (uint)idx < (uint)Font5x7.Length ? Font5x7[idx] : Font5x7['?' - ' '];
    }
}
