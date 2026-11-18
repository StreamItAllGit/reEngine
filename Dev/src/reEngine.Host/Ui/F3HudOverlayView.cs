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
public sealed record F3HudTelemetry(
    double CurrentFps,
    double AverageFrameMs,
    string BackendLabel,
    int ResolutionScale,
    string ExecutionTier,
    double AudioFillRatio);

/// <summary>
/// Renders and measures the F3 performance telemetry overlay on <see cref="PixelOverlayCanvas"/>.
/// </summary>
public sealed class F3HudOverlayView
{
    public bool IsVisible { get; set; }

    public Rectangle CurrentBounds { get; private set; } = new(12, 12, 310, 44);

    public void Render(PixelOverlayCanvas canvas, F3HudTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (!IsVisible) return;

        int w = Math.Min(360, Math.Max(220, canvas.Width - 24));
        CurrentBounds = new Rectangle(12, 12, w, 46);
        HostOverlayLayoutHelpers.DrawPanel(canvas, CurrentBounds, HostOverlayPalette.HudPillBg, HostOverlayPalette.AccentCyan);

        string line1 = string.Create(
            CultureInfo.InvariantCulture,
            $"FPS: {telemetry.CurrentFps:F1} | Frame: {telemetry.AverageFrameMs:F2}ms");
        string line2 = string.Create(
            CultureInfo.InvariantCulture,
            $"{telemetry.BackendLabel} {telemetry.ResolutionScale}x | {telemetry.ExecutionTier} | Audio {(telemetry.AudioFillRatio * 100.0):F0}%");

        canvas.DrawText(CurrentBounds.X + 10, CurrentBounds.Y + 9, line1, HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(CurrentBounds.X + 10, CurrentBounds.Y + 25, line2, HostOverlayPalette.TextPrimary, 1);
    }

    public HostWidgetBounds? TryBuildWidgetBounds() =>
        IsVisible ? new HostWidgetBounds("F3Hud", "Label", $"F3 Telemetry ({CurrentBounds.Width}x{CurrentBounds.Height})", CurrentBounds, true) : null;
}
