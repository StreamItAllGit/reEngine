// @module: reEngine.Host.Ui.SystemTabView
// @purpose: Renders the F1 drawer System & Execution Resilience tab (tier toggle, cutscene skip, RAM preload, fast boot, Safe Mode reset).
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts
// @exports: SystemTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Immediate-mode renderer and pointer click dispatcher for the <c>F1</c> drawer <c>System</c> tab.
/// </summary>
public sealed class SystemTabView
{
    private sealed record ClickTarget(string ControlId, Rectangle Bounds, Action<bool> OnClick);

    private readonly List<ClickTarget> _targets = new();
    private readonly List<HostWidgetBounds> _widgets = new();

    /// <summary>Gets the bottom Y coordinate occupied by rendered System tab content.</summary>
    public int ContentBottomY { get; private set; }

    /// <summary>Renders System execution tier, RAM preload, Fast Boot, and Safe Mode controls.</summary>
    public void Render(
        PixelOverlayCanvas canvas,
        Rectangle body,
        F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor,
        out Rectangle cyclePrimaryRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);

        _targets.Clear();
        _widgets.Clear();

        HostExecutionResilienceOptions res = view.ActiveResilience;
        int y = body.Y;
        int fullW = body.Width;
        int buttonW = Math.Min(240, fullW);

        canvas.DrawText(body.X, y, "SYSTEM & EXECUTION TIER", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, y + 16, $"Tier Mode: {view.ExecutionTierLabel}", HostOverlayPalette.TextPrimary, 1);
        canvas.DrawText(body.X, y + 32, $"Active Policy: {res.ExecutionTierMode}", HostOverlayPalette.TextMuted, 1);
        canvas.DrawText(body.X, y + 48, HostOverlayLayoutHelpers.FitText($"Module: {descriptor.ModuleTitle}", fullW, 1), HostOverlayPalette.TextMuted, 1);

        cyclePrimaryRect = new Rectangle(body.X, y + 66, buttonW, 22);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, cyclePrimaryRect, "Cycle Tier Mode", false, HostOverlayPalette.AccentCyan);

        var skipRect = new Rectangle(body.X, y + 92, buttonW, 22);
        AddPill(canvas, skipRect, "System_SkipCutscene", "Skip Cutscene", false, HostOverlayPalette.AccentWarning,
            _ => view.EventCallbacks?.OnCutsceneSkipRequested());

        AddToggle(canvas, new(body.X, y + 118, fullW, 22), "System_ToggleJit", "Allow Tier 3 JIT", res.IsJitEnabled, HostOverlayPalette.AccentCyan,
            _ =>
            {
                bool nextJit = !view.ActiveResilience.IsJitEnabled;
                string nextMode = nextJit ? "aotWithJitFallback" : "staticAotOnly";
                view.ActiveResilience = view.ActiveResilience with { IsJitEnabled = nextJit, ExecutionTierMode = nextMode };
                view.ExecutionTierLabel = nextJit ? "JIT Bring-Up" : "AOT Primary";
                view.EventCallbacks?.OnExecutionTierChanged(nextMode, nextJit);
            });

        AddToggle(canvas, new(body.X, y + 144, fullW, 22), "System_TogglePreloadRam", "Preload Media to RAM", res.ShouldPreloadMediaToRam, HostOverlayPalette.AccentCyan,
            _ =>
            {
                bool next = !view.ActiveResilience.ShouldPreloadMediaToRam;
                view.ActiveResilience = view.ActiveResilience with { ShouldPreloadMediaToRam = next };
                view.EventCallbacks?.OnPreloadMediaToRamChanged(next);
            });

        AddToggle(canvas, new(body.X, y + 170, fullW, 22), "System_ToggleFastBoot", "Fast Boot (Skip Splash)", res.IsFastBootEnabled, HostOverlayPalette.AccentCyan,
            _ =>
            {
                bool next = !view.ActiveResilience.IsFastBootEnabled;
                view.ActiveResilience = view.ActiveResilience with { IsFastBootEnabled = next };
                view.EventCallbacks?.OnFastBootChanged(next);
            });

        string safeLabel = res.IsSafeModeActive ? "Safe Mode: ACTIVE (Reset)" : "Safe Mode: Off (Reset)";
        AddPill(canvas, new(body.X, y + 196, fullW, 22), "System_ResetSafeMode", safeLabel, res.IsSafeModeActive, HostOverlayPalette.AccentWarning,
            _ =>
            {
                view.ActiveResilience = view.ActiveResilience with { IsSafeModeActive = false };
                view.EventCallbacks?.OnSafeModeResetRequested();
            });

        ContentBottomY = y + 224;
    }

    /// <summary>Returns interactive widget bounds emitted by the latest render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click inside the System tab.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, bool isRightClick = false)
    {
        foreach (ClickTarget target in _targets)
        {
            bool match = !string.IsNullOrEmpty(targetId)
                ? string.Equals(targetId, target.ControlId, StringComparison.OrdinalIgnoreCase)
                : target.Bounds.Contains(x, y);
            if (!match) continue;
            target.OnClick(isRightClick);
            return true;
        }
        return false;
    }

    private void AddPill(PixelOverlayCanvas canvas, Rectangle rect, string id, string label, bool active, RgbaColor accent, Action<bool> onClick)
    {
        HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, active, accent);
        _targets.Add(new(id, rect, onClick));
        _widgets.Add(new(id, "Button", label, rect, true));
    }

    private void AddToggle(PixelOverlayCanvas canvas, Rectangle rect, string id, string label, bool val, RgbaColor accent, Action<bool> onClick)
    {
        HostOverlayLayoutHelpers.DrawToggleRow(canvas, rect, label, val, accent);
        _targets.Add(new(id, rect, onClick));
        _widgets.Add(new(id, "CheckBox", label, rect, true));
    }
}
