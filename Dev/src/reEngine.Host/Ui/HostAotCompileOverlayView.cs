// @module: reEngine.Host.Ui.HostAotCompileOverlayView
// @purpose: Full-screen glass "Preparing Native Module..." AOT progress overlay with animated spinner, progress bar, and responsive event pump.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Host.Contracts
// @exports: HostAotCompileOverlayView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Full-screen glass progress overlay displayed while compiling AOT modules without blocking the SDL event loop.
/// </summary>
public sealed class HostAotCompileOverlayView
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];

    private Rectangle _cardRect, _barRect;
    private double _uiTextScale = 1.0;

    /// <summary>Gets or sets the active AOT compilation progress snapshot.</summary>
    public HostAotCompileState State { get; set; } = new(false, "Idle", 0.0);

    /// <summary>Gets or sets the active overlay UI text scale (<c>0.75..2.0</c>).</summary>
    public double UiTextScale { get => _uiTextScale; set => _uiTextScale = HostOverlayTextRenderer.ClampUiTextScale(value); }

    /// <summary>Gets the current animation frame counter incremented on each visible render pass.</summary>
    public int SpinnerTick { get; private set; }

    /// <summary>Renders the full-screen glass AOT compile overlay when <see cref="HostAotCompileState.IsCompiling"/> is <see langword="true"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor? descriptor = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!State.IsCompiling) return;

        double scale = UiTextScale;
        int lineH = HostOverlayTextRenderer.GetLineHeight(scale);
        canvas.FillRect(0, 0, canvas.Width, canvas.Height, HostOverlayPalette.DeepSlateBackdrop);

        int cardW = Math.Clamp((int)Math.Round(540 * Math.Max(1.0, scale * 0.8)), Math.Min(300, canvas.Width - 24), Math.Max(300, canvas.Width - 24));
        int cardH = Math.Clamp(120 + (lineH * 3), Math.Min(160, canvas.Height - 24), Math.Max(160, canvas.Height - 24));
        _cardRect = new Rectangle((canvas.Width - cardW) / 2, (canvas.Height - cardH) / 2, cardW, cardH);
        HostOverlayLayoutHelpers.DrawPanel(canvas, _cardRect, HostOverlayPalette.Surface2Glass, HostOverlayPalette.AccentCyan, 2);

        int pad = 18;
        int innerW = Math.Max(80, cardW - (pad * 2));
        int y = _cardRect.Y + pad;
        string spinner = SpinnerFrames[SpinnerTick % SpinnerFrames.Length];
        SpinnerTick++;

        HostOverlayTextRenderer.DrawText(canvas, _cardRect.X + pad, y,
            HostOverlayTextRenderer.FitText($"[{spinner}] Preparing Native Module...", innerW, scale),
            HostOverlayPalette.AccentCyan, scale);
        y += lineH + 6;

        string moduleText = descriptor is null ? State.StageLabel : $"{descriptor.ModuleTitle} - {State.StageLabel}";
        HostOverlayTextRenderer.DrawText(canvas, _cardRect.X + pad, y,
            HostOverlayTextRenderer.FitText(moduleText, innerW, scale),
            HostOverlayPalette.TextPrimary, scale);
        y += lineH + 6;

        double ratio = double.IsFinite(State.ProgressRatio) ? Math.Clamp(State.ProgressRatio, 0.0, 1.0) : 0.0;
        int barH = Math.Clamp((int)Math.Round(18 * scale), 16, 30);
        _barRect = new Rectangle(_cardRect.X + pad, y, innerW, barH);
        HostOverlayLayoutHelpers.DrawPanel(canvas, _barRect, HostOverlayPalette.Surface0, HostOverlayPalette.Border);
        int fillW = (int)Math.Round(Math.Max(0, _barRect.Width - 4) * ratio);
        if (fillW > 0) canvas.FillRect(_barRect.X + 2, _barRect.Y + 2, fillW, Math.Max(2, _barRect.Height - 4), HostOverlayPalette.AccentCyan);

        y += barH + 8;
        string pct = string.Create(CultureInfo.InvariantCulture, $"{Math.Round(ratio * 100.0):F0}%");
        string detail = string.IsNullOrWhiteSpace(State.DetailMessage) ? pct : $"{pct} - {State.DetailMessage}";
        HostOverlayTextRenderer.DrawText(canvas, _cardRect.X + pad, y,
            HostOverlayTextRenderer.FitText(detail, innerW, scale), HostOverlayPalette.TextMuted, scale);
    }

    /// <summary>Returns widget geometry for automation inspection while compiling.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!State.IsCompiling) return Array.Empty<HostWidgetBounds>();
        int pct = (int)Math.Round(Math.Clamp(State.ProgressRatio, 0.0, 1.0) * 100.0);
        return
        [
            new("AotOverlay_Card", "Panel", "Preparing Native Module...", _cardRect, true),
            new("AotOverlay_Progress", "ProgressBar", $"{pct}%", _barRect, true)
        ];
    }

    /// <summary>Runs background AOT compilation while pumping SDL events and progress renders at ~60 Hz.</summary>
    public async Task<T> RunWithResponsivePumpAsync<T>(
        Func<Action<HostAotCompileState>, CancellationToken, Task<T>> compileWork,
        Action<HostAotCompileState> pumpFrame,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(compileWork);
        ArgumentNullException.ThrowIfNull(pumpFrame);

        State = new HostAotCompileState(true, "Initializing AOT Cache...", 0.05);
        Task<T> workTask = compileWork(next => State = next, ct);
        while (!workTask.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();
            pumpFrame(State);
            await Task.WhenAny(workTask, Task.Delay(16, ct)).ConfigureAwait(false);
        }

        T result = await workTask.ConfigureAwait(false);
        State = new HostAotCompileState(false, "Ready", 1.0);
        return result;
    }

    /// <summary>Runs background AOT compilation while invoking <paramref name="pumpFrame"/> on each frame interval.</summary>
    public Task RunWithResponsivePumpAsync(
        Func<Action<HostAotCompileState>, CancellationToken, Task> compileWork,
        Action<HostAotCompileState> pumpFrame,
        CancellationToken ct = default) =>
        RunWithResponsivePumpAsync<bool>(async (report, token) =>
        {
            await compileWork(report, token).ConfigureAwait(false);
            return true;
        }, pumpFrame, ct);
}
