// @module: reEngine.Host.Ui.HostOverlayLayoutHelpers
// @purpose: Shared palette constants, aspect-correct scaling rects, scaled text fitting, focus rings, steppers, and drawing helpers atop PixelOverlayCanvas.
// @depends-on: System, System.Drawing, Foundation.Core
// @exports: HostOverlayPalette, HostOverlayLayoutHelpers

using System.Drawing;
using Foundation.Core;

namespace reEngine.Host.Ui;

internal static class HostOverlayPalette
{
    public static readonly RgbaColor Surface0 = new(14, 14, 16, 255);
    public static readonly RgbaColor DeepSlateBackdrop = new(12, 15, 22, 238);
    public static readonly RgbaColor Surface2Glass = new(24, 24, 27, 236);
    public static readonly RgbaColor Surface3Glass = new(39, 39, 42, 240);
    public static readonly RgbaColor DropZoneFill = new(20, 26, 38, 242);
    public static readonly RgbaColor HudPillBg = new(18, 18, 22, 215);
    public static readonly RgbaColor Border = new(63, 63, 70, 255);
    public static readonly RgbaColor FocusRing = new(255, 255, 255, 255);
    public static readonly RgbaColor AccentCyan = new(56, 189, 248, 255);
    public static readonly RgbaColor AccentSuccess = new(16, 185, 129, 255);
    public static readonly RgbaColor AccentWarning = new(245, 158, 11, 255);
    public static readonly RgbaColor AccentDanger = new(220, 38, 38, 255);
    public static readonly RgbaColor WarnFill = new(54, 30, 22, 238);
    public static readonly RgbaColor TouchPadFill = new(24, 28, 36, 175);
    public static readonly RgbaColor TouchPadActive = new(56, 189, 248, 215);
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

    public static void DrawPanel(PixelOverlayCanvas canvas, Rectangle rect, RgbaColor fill, RgbaColor border, int thickness = 1)
    {
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, fill);
        canvas.StrokeRect(rect.X, rect.Y, rect.Width, rect.Height, border, thickness);
    }

    public static void DrawFocusOutline(PixelOverlayCanvas canvas, Rectangle rect)
    {
        if (rect.Width <= 2 || rect.Height <= 2) return;
        canvas.StrokeRect(rect.X, rect.Y, rect.Width, rect.Height, HostOverlayPalette.FocusRing, 2);
    }

    public static Rectangle DrawPillButton(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        bool isActive,
        RgbaColor activeAccent) =>
        DrawPillButton(canvas, rect, label, isActive, activeAccent, uiTextScale: 1.0, isFocused: false);

    public static Rectangle DrawPillButton(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        bool isActive,
        RgbaColor activeAccent,
        double uiTextScale,
        bool isFocused = false)
    {
        RgbaColor fill = isActive ? activeAccent : HostOverlayPalette.Surface3Glass;
        RgbaColor border = isActive ? activeAccent : HostOverlayPalette.Border;
        int luma = ((activeAccent.R * 299) + (activeAccent.G * 587) + (activeAccent.B * 114)) / 1000;
        RgbaColor textCol = isActive ? (luma < 120 ? HostOverlayPalette.TextPrimary : HostOverlayPalette.DarkText) : HostOverlayPalette.TextPrimary;
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, fill);
        canvas.StrokeRect(rect.X, rect.Y, rect.Width, rect.Height, isFocused ? HostOverlayPalette.FocusRing : border, isFocused ? 2 : 1);

        string fitted = HostOverlayTextRenderer.FitText(label, Math.Max(8, rect.Width - 10), uiTextScale);
        (int tw, int th) = HostOverlayTextRenderer.MeasureText(fitted, uiTextScale);
        int tx = rect.X + Math.Max(4, (rect.Width - tw) / 2);
        int ty = rect.Y + Math.Max(2, (rect.Height - th) / 2);
        HostOverlayTextRenderer.DrawText(canvas, tx, ty, fitted, textCol, uiTextScale);
        return rect;
    }

    public static Rectangle DrawToggleRow(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        bool isOn,
        RgbaColor accent) =>
        DrawToggleRow(canvas, rect, label, isOn, accent, uiTextScale: 1.0, isFocused: false);

    public static Rectangle DrawToggleRow(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        bool isOn,
        RgbaColor accent,
        double uiTextScale,
        bool isFocused = false)
    {
        double clamped = HostOverlayTextRenderer.ClampUiTextScale(uiTextScale);
        int badgeW = Math.Clamp((int)Math.Round(40 * clamped), 36, Math.Max(36, rect.Width / 3));
        int badgeH = Math.Max(14, rect.Height - 6);
        RgbaColor border = isFocused ? HostOverlayPalette.FocusRing : (isOn ? accent : HostOverlayPalette.Border);
        DrawPanel(canvas, rect, HostOverlayPalette.Surface3Glass, border, isFocused ? 2 : 1);

        string fitted = HostOverlayTextRenderer.FitText(label, Math.Max(20, rect.Width - badgeW - 18), clamped);
        (_, int th) = HostOverlayTextRenderer.MeasureText(fitted, clamped);
        int ty = rect.Y + Math.Max(2, (rect.Height - th) / 2);
        HostOverlayTextRenderer.DrawText(canvas, rect.X + 8, ty, fitted, HostOverlayPalette.TextPrimary, clamped);

        var badge = new Rectangle(rect.Right - badgeW - 4, rect.Y + ((rect.Height - badgeH) / 2), badgeW, badgeH);
        _ = DrawPillButton(canvas, badge, isOn ? "ON" : "OFF", isOn, accent, clamped, isFocused: false);
        return rect;
    }

    public static (Rectangle MinusRect, Rectangle PlusRect) DrawStepperRow(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string label,
        string valueText,
        RgbaColor accent,
        double uiTextScale = 1.0,
        bool isFocused = false,
        bool isMinusFocused = false,
        bool isPlusFocused = false)
    {
        double clamped = HostOverlayTextRenderer.ClampUiTextScale(uiTextScale);
        int btnW = Math.Clamp((int)Math.Round(26 * clamped), 24, 48);
        const int gap = 4;
        int btnH = Math.Max(16, rect.Height - 4);
        int btnY = rect.Y + ((rect.Height - btnH) / 2);
        var plusRect = new Rectangle(rect.Right - btnW - 3, btnY, btnW, btnH);
        var minusRect = new Rectangle(plusRect.X - gap - btnW, btnY, btnW, btnH);

        DrawPanel(canvas, rect, HostOverlayPalette.Surface3Glass, isFocused ? HostOverlayPalette.FocusRing : HostOverlayPalette.Border, isFocused ? 2 : 1);

        string combined = string.IsNullOrWhiteSpace(valueText) ? label : $"{label}: {valueText}";
        int textMaxW = Math.Max(20, minusRect.X - rect.X - 12);
        string fitted = HostOverlayTextRenderer.FitText(combined, textMaxW, clamped);
        (_, int th) = HostOverlayTextRenderer.MeasureText(fitted, clamped);
        int ty = rect.Y + Math.Max(2, (rect.Height - th) / 2);
        HostOverlayTextRenderer.DrawText(canvas, rect.X + 8, ty, fitted, HostOverlayPalette.TextPrimary, clamped);

        _ = DrawPillButton(canvas, minusRect, "-", false, accent, clamped, isMinusFocused);
        _ = DrawPillButton(canvas, plusRect, "+", false, accent, clamped, isPlusFocused);
        return (minusRect, plusRect);
    }

    public static void DrawBanner(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string text,
        RgbaColor fill,
        RgbaColor border,
        RgbaColor textColor) =>
        DrawBanner(canvas, rect, text, fill, border, textColor, uiTextScale: 1.0);

    public static void DrawBanner(
        PixelOverlayCanvas canvas,
        Rectangle rect,
        string text,
        RgbaColor fill,
        RgbaColor border,
        RgbaColor textColor,
        double uiTextScale)
    {
        DrawPanel(canvas, rect, fill, border);
        string fitted = HostOverlayTextRenderer.FitText(text, Math.Max(12, rect.Width - 12), uiTextScale);
        (_, int th) = HostOverlayTextRenderer.MeasureText(fitted, uiTextScale);
        int ty = rect.Y + Math.Max(2, (rect.Height - th) / 2);
        HostOverlayTextRenderer.DrawText(canvas, rect.X + 6, ty, fitted, textColor, uiTextScale);
    }

    public static string FitText(string? text, int maxWidthPixels, int scale = 1) =>
        HostOverlayTextRenderer.FitText(text, maxWidthPixels, scale <= 1 ? 1.0 : Math.Clamp(scale, 1.0, 2.0));

    public static string FitText(string? text, int maxWidthPixels, double uiTextScale) =>
        HostOverlayTextRenderer.FitText(text, maxWidthPixels, uiTextScale);
}
