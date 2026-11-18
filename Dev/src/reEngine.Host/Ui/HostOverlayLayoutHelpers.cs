// @module: reEngine.Host.Ui.HostOverlayLayoutHelpers
// @purpose: Shared palette constants, aspect-correct scaling rects, and drawing helpers atop Foundation.Core.PixelOverlayCanvas.
// @depends-on: System, System.Drawing, Foundation.Core
// @exports: HostOverlayPalette, HostOverlayLayoutHelpers

using System.Drawing;
using Foundation.Core;

namespace reEngine.Host.Ui;

internal static class HostOverlayPalette
{
    public static readonly RgbaColor Surface0 = new(14, 14, 16, 255);
    public static readonly RgbaColor Surface2Glass = new(24, 24, 27, 232);
    public static readonly RgbaColor Surface3Glass = new(39, 39, 42, 240);
    public static readonly RgbaColor HudPillBg = new(18, 18, 22, 215);
    public static readonly RgbaColor Border = new(63, 63, 70, 255);
    public static readonly RgbaColor AccentCyan = new(56, 189, 248, 255);
    public static readonly RgbaColor AccentSuccess = new(16, 185, 129, 255);
    public static readonly RgbaColor AccentWarning = new(245, 158, 11, 255);
    public static readonly RgbaColor TextPrimary = new(244, 244, 245, 255);
    public static readonly RgbaColor TextMuted = new(161, 161, 170, 255);
    public static readonly RgbaColor DarkText = new(12, 14, 18, 255);
}

internal static class HostOverlayLayoutHelpers
{
    public static Rectangle ComputeAspectFitRect(int srcWidth, int srcHeight, int dstWidth, int dstHeight, int scale = 1)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
        {
            return new Rectangle(0, 0, Math.Max(1, dstWidth), Math.Max(1, dstHeight));
        }

        int clampedScale = Math.Clamp(scale, 1, 8);
        int integerW = srcWidth * clampedScale;
        int integerH = srcHeight * clampedScale;

        int fitW;
        int fitH;
        if (integerW <= dstWidth && integerH <= dstHeight)
        {
            fitW = integerW;
            fitH = integerH;
        }
        else
        {
            double ratio = Math.Min((double)dstWidth / srcWidth, (double)dstHeight / srcHeight);
            fitW = Math.Max(1, (int)Math.Floor(srcWidth * ratio));
            fitH = Math.Max(1, (int)Math.Floor(srcHeight * ratio));
        }

        int x = Math.Max(0, (dstWidth - fitW) / 2);
        int y = Math.Max(0, (dstHeight - fitH) / 2);
        return new Rectangle(x, y, fitW, fitH);
    }

    public static void DrawPanel(PixelOverlayCanvas canvas, Rectangle rect, RgbaColor fill, RgbaColor border)
    {
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, fill);
        canvas.StrokeRect(rect.X, rect.Y, rect.Width, rect.Height, border, 1);
    }

    public static Rectangle DrawPillButton(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        bool isActive,
        RgbaColor activeAccent)
    {
        RgbaColor fill = isActive ? activeAccent : HostOverlayPalette.Surface3Glass;
        RgbaColor border = isActive ? activeAccent : HostOverlayPalette.Border;
        RgbaColor textCol = isActive ? HostOverlayPalette.DarkText : HostOverlayPalette.TextPrimary;
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, fill);
        canvas.StrokeRect(rect.X, rect.Y, rect.Width, rect.Height, border, 1);

        (int tw, int th) = canvas.MeasureText(label, 1);
        int tx = rect.X + Math.Max(4, (rect.Width - tw) / 2);
        int ty = rect.Y + Math.Max(4, (rect.Height - th) / 2);
        canvas.DrawText(tx, ty, label, textCol, 1);
        return rect;
    }
}
