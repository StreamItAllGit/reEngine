// @module: reEngine.Host.Ui.F3HudOverlayView
// @purpose: Renders the toggleable F3 telemetry HUD overlay onto Foundation.Core.PixelOverlayCanvas.
// @depends-on: System, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Contracts
// @exports: F3HudTelemetry, F3HudOverlayView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Telemetry snapshot rendered inside the F3 overlay pill.
/// </summary>
/// <param name="CurrentFps">Measured presentation frames per second.</param>
/// <param name="AverageFrameMs">Smoothed average frame duration in milliseconds.</param>
/// <param name="BackendLabel">Active render backend display label.</param>
/// <param name="ResolutionScale">Active integer render scale multiplier.</param>
/// <param name="ExecutionTier">Active execution tier label.</param>
/// <param name="AudioFillRatio">Audio ring buffer fill ratio (<c>0..1</c>).</param>
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
}

/// <summary>
/// Renders and measures the F3 performance telemetry overlay on <see cref="PixelOverlayCanvas"/>.
/// </summary>
public sealed class F3HudOverlayView
{
    /// <summary>Gets or sets whether the F3 HUD overlay is visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Gets the bounds of the rendered F3 HUD pill.</summary>
    public Rectangle CurrentBounds { get; private set; } = new(12, 12, 310, 44);

    /// <summary>Renders <paramref name="telemetry"/> onto <paramref name="canvas"/> when <see cref="IsVisible"/> is <see langword="true"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, F3HudTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!IsVisible) return;

        int w = Math.Min(400, Math.Max(220, canvas.Width - 24));
        CurrentBounds = new Rectangle(12, 12, w, 46);
        HostOverlayLayoutHelpers.DrawPanel(canvas, CurrentBounds, HostOverlayPalette.HudPillBg, HostOverlayPalette.AccentCyan);

        string ffTag = telemetry.IsFastForwardEnabled ? " | FF" : string.Empty;
        string padTag = telemetry.ConnectedPadCount > 0 ? $" | Pads:{telemetry.ConnectedPadCount}" : string.Empty;
        string aspectTag = !string.IsNullOrWhiteSpace(telemetry.AspectRatioLabel) ? $" {telemetry.AspectRatioLabel}" : string.Empty;

        string line1 = string.Create(
            CultureInfo.InvariantCulture,
            $"FPS: {telemetry.CurrentFps:F1} | Frame: {telemetry.AverageFrameMs:F2}ms{ffTag}{padTag}");
        string line2 = string.Create(
            CultureInfo.InvariantCulture,
            $"{telemetry.BackendLabel} {telemetry.ResolutionScale}x{aspectTag} | {telemetry.ExecutionTier} | Audio {(telemetry.AudioFillRatio * 100.0):F0}%");

        canvas.DrawText(CurrentBounds.X + 10, CurrentBounds.Y + 9, HostOverlayLayoutHelpers.FitText(line1, w - 20, 1), HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(CurrentBounds.X + 10, CurrentBounds.Y + 25, HostOverlayLayoutHelpers.FitText(line2, w - 20, 1), HostOverlayPalette.TextPrimary, 1);
    }

    /// <summary>Builds automation widget bounds when the F3 HUD overlay is visible.</summary>
    public HostWidgetBounds? TryBuildWidgetBounds() =>
        IsVisible ? new HostWidgetBounds("F3Hud", "Label", $"F3 Telemetry ({CurrentBounds.Width}x{CurrentBounds.Height})", CurrentBounds, true) : null;
}
