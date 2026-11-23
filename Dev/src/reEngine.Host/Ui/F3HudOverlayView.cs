// @module: reEngine.Host.Ui.F3HudOverlayView
// @purpose: Renders the scaled 2-line F3 telemetry HUD overlay onto PixelOverlayCanvas with mode/FF badges and Clean-Capture suppression.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Contracts
// @exports: F3HudTelemetry, F3HudOverlayView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Telemetry snapshot rendered inside the F3 overlay pill.
/// </summary>
public sealed record F3HudTelemetry(
    double CurrentFps,
    double AverageFrameMs,
    string BackendLabel,
    int ResolutionScale,
    string ExecutionTier,
    double AudioFillRatio)
{
    /// <summary>Gets or sets an optional aspect ratio display badge shown on the HUD strip.</summary>
    public string? AspectRatioLabel { get; init; }

    /// <summary>Gets or sets whether fast-forward mode is active.</summary>
    public bool IsFastForwardEnabled { get; init; }

    /// <summary>Gets or sets the count of connected controller ports (<c>0..4</c>).</summary>
    public int ConnectedPadCount { get; init; }

    /// <summary>Gets or sets an optional live presentation filter badge label.</summary>
    public string? VideoFilterLabel { get; init; }

    /// <summary>Gets or sets an optional host display mode badge label.</summary>
    public string? DisplayModeLabel { get; init; }

    /// <summary>Gets or sets whether crash-recovery Safe Mode is active.</summary>
    public bool IsSafeModeActive { get; init; }

    /// <summary>Gets or sets whether master audio output is currently muted.</summary>
    public bool IsAudioMuted { get; init; }

    /// <summary>Gets or sets the framerate cadence badge (<c>"[ORIG]"</c>, <c>"[ENH]"</c>, or <c>"[MAX]"</c>).</summary>
    public string? FrameRateModeBadge { get; init; }

    /// <summary>Gets or sets the active fast-forward multiplier (<c>2x..4x</c>).</summary>
    public double FastForwardMultiplier { get; init; } = 2.0;

    /// <summary>Gets or sets whether Clean-Capture Mode suppresses all F3 HUD rendering.</summary>
    public bool IsCleanCaptureModeEnabled { get; init; }

    /// <summary>Gets or sets the active overlay UI text scale (<c>0.75..2.0</c>).</summary>
    public double UiTextScale { get; init; } = 1.0;
}

/// <summary>
/// Renders and measures the scaled 2-line F3 performance telemetry overlay on <see cref="PixelOverlayCanvas"/>.
/// </summary>
public sealed class F3HudOverlayView
{
    private bool _lastCleanCaptureSuppressed;

    /// <summary>Gets or sets whether the F3 HUD overlay is visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Gets or sets whether Clean-Capture Mode is forcing the F3 HUD hidden.</summary>
    public bool IsCleanCaptureModeEnabled { get; set; }

    /// <summary>Gets the bounds of the rendered F3 HUD pill.</summary>
    public Rectangle CurrentBounds { get; private set; } = new(12, 12, 340, 50);

    /// <summary>Renders <paramref name="telemetry"/> onto <paramref name="canvas"/> unless hidden or suppressed by Clean-Capture Mode.</summary>
    public void Render(PixelOverlayCanvas canvas, F3HudTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(telemetry);
        _lastCleanCaptureSuppressed = telemetry.IsCleanCaptureModeEnabled;
        if (!IsVisible || IsCleanCaptureModeEnabled || telemetry.IsCleanCaptureModeEnabled) return;

        double scale = HostOverlayTextRenderer.ClampUiTextScale(telemetry.UiTextScale);
        int lineH = HostOverlayTextRenderer.GetLineHeight(scale);

        string modeBadge = string.IsNullOrWhiteSpace(telemetry.FrameRateModeBadge)
            ? string.Empty
            : telemetry.FrameRateModeBadge.Trim().PadRight(6) + " ";
        double ffMult = Math.Clamp(double.IsFinite(telemetry.FastForwardMultiplier) ? telemetry.FastForwardMultiplier : 2.0, 2.0, 4.0);
        string ffTag = telemetry.IsFastForwardEnabled
            ? string.Create(CultureInfo.InvariantCulture, $"[FF {ffMult:0.#}x] ")
            : string.Empty;
        string padTag = telemetry.ConnectedPadCount > 0 ? $" | Pads:{telemetry.ConnectedPadCount}" : string.Empty;

        double dispFps = Math.Clamp(double.IsFinite(telemetry.CurrentFps) ? telemetry.CurrentFps : 0.0, 0.0, 999.9);
        double dispMs = Math.Clamp(double.IsFinite(telemetry.AverageFrameMs) ? telemetry.AverageFrameMs : 0.0, 0.0, 999.99);
        string line1 = string.Create(
            CultureInfo.InvariantCulture,
            $"{modeBadge}{ffTag}FPS: {dispFps,5:F1} | Frame: {dispMs,6:F2}ms{padTag}");

        string aspectTag = !string.IsNullOrWhiteSpace(telemetry.AspectRatioLabel) ? $" {telemetry.AspectRatioLabel.Trim()}" : string.Empty;
        double dispAudioPct = Math.Clamp(double.IsFinite(telemetry.AudioFillRatio) ? telemetry.AudioFillRatio * 100.0 : 0.0, 0.0, 999.0);
        string audioText = telemetry.IsAudioMuted
            ? "MUTED"
            : string.Create(CultureInfo.InvariantCulture, $"{dispAudioPct:F0}%").PadLeft(5);
        var line2Parts = new List<string>(6)
        {
            $"{telemetry.BackendLabel} {telemetry.ResolutionScale}x{aspectTag}",
            telemetry.ExecutionTier,
            $"Audio {audioText}"
        };
        if (!string.IsNullOrWhiteSpace(telemetry.DisplayModeLabel)) line2Parts.Add(telemetry.DisplayModeLabel.Trim());
        if (!string.IsNullOrWhiteSpace(telemetry.VideoFilterLabel)) line2Parts.Add(telemetry.VideoFilterLabel.Trim());
        if (telemetry.IsSafeModeActive) line2Parts.Add("SAFE MODE");
        string line2 = string.Join(" | ", line2Parts);

        string templateLine1 = $"{modeBadge}{ffTag}FPS: 888.8 | Frame: 888.88ms{padTag}";
        line2Parts[2] = "Audio MUTED";
        string templateLine2 = string.Join(" | ", line2Parts);

        int w1 = Math.Max(HostOverlayTextRenderer.MeasureText(line1, scale).Width, HostOverlayTextRenderer.MeasureText(templateLine1, scale).Width);
        int w2 = Math.Max(HostOverlayTextRenderer.MeasureText(line2, scale).Width, HostOverlayTextRenderer.MeasureText(templateLine2, scale).Width);
        int maxW = Math.Max(200, canvas.Width - 24);
        int w = Math.Clamp(Math.Max(w1, w2) + 24, Math.Min(280, maxW), maxW);
        int h = 16 + (lineH * 2);
        CurrentBounds = new Rectangle(12, 12, w, h);

        HostOverlayLayoutHelpers.DrawPanel(
            canvas,
            CurrentBounds,
            HostOverlayPalette.HudPillBg,
            telemetry.IsSafeModeActive ? HostOverlayPalette.AccentWarning : HostOverlayPalette.AccentCyan);

        int textW = Math.Max(40, w - 20);
        HostOverlayTextRenderer.DrawText(canvas, CurrentBounds.X + 10, CurrentBounds.Y + 8,
            HostOverlayTextRenderer.FitText(line1, textW, scale), HostOverlayPalette.AccentCyan, scale);
        RgbaColor line2Color = telemetry.IsSafeModeActive || telemetry.IsAudioMuted
            ? HostOverlayPalette.AccentWarning
            : HostOverlayPalette.TextPrimary;
        HostOverlayTextRenderer.DrawText(canvas, CurrentBounds.X + 10, CurrentBounds.Y + 10 + lineH,
            HostOverlayTextRenderer.FitText(line2, textW, scale), line2Color, scale);
    }

    /// <summary>Builds automation widget bounds when visible and not suppressed by Clean-Capture Mode.</summary>
    public HostWidgetBounds? TryBuildWidgetBounds() =>
        IsVisible && !IsCleanCaptureModeEnabled && !_lastCleanCaptureSuppressed
            ? new HostWidgetBounds("F3Hud", "Label", $"F3 Telemetry ({CurrentBounds.Width}x{CurrentBounds.Height})", CurrentBounds, true)
            : null;
}
