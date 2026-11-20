// @module: reEngine.Host.Ui.SystemTabView
// @purpose: Scrollable System & Execution Resilience tab (Quick Save/Load, FF speed, runtime logs, Clean-Capture, execution tier, Safe Mode, Quit Game).
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Contracts
// @exports: SystemTabView

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Immediate-mode renderer and pointer click dispatcher for the <c>F1</c> drawer <c>System</c> tab.
/// </summary>
public sealed class SystemTabView
{
    private readonly List<DrawerControl> _controls = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private int _standaloneScroll;

    /// <summary>Gets the bottom Y coordinate occupied by rendered System tab content.</summary>
    public int ContentBottomY { get; private set; }

    /// <summary>Renders System execution tier, session actions, resilience toggles, and Quit Game.</summary>
    public void Render(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, out Rectangle cyclePrimaryRect) =>
        Render(canvas, body, view, descriptor, ref _standaloneScroll, out _, out cyclePrimaryRect);

    internal void Render(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, ref int scrollOffset, out int maxScroll, out Rectangle cyclePrimaryRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(descriptor);

        _controls.Clear();
        _widgets.Clear();
        cyclePrimaryRect = Rectangle.Empty;

        HostExecutionResilienceOptions res = view.ActiveResilience;
        var layout = new HostDrawerScrollLayout(body, view.UiTextScale, view.FocusedControlId);

        layout.AddText($"Tier: {view.ExecutionTierLabel} ({res.ExecutionTierMode})", HostOverlayPalette.TextPrimary);
        layout.AddText($"Module: {descriptor.ModuleTitle}", HostOverlayPalette.TextMuted);

        layout.AddColumns(_controls,
            ("System_QuickSave", "Quick Save (F5)", false, false, HostOverlayPalette.AccentCyan,
                _ => view.EventCallbacks?.OnQuickSaveRequested(), null),
            ("System_QuickLoad", "Quick Load (F9)", false, false, HostOverlayPalette.AccentCyan,
                _ => view.EventCallbacks?.OnQuickLoadRequested(), null));

        double ffMult = Math.Clamp(double.IsFinite(view.FastForwardMultiplier) ? view.FastForwardMultiplier : 2.0, 2.0, 4.0);
        string ffSpeedLabel = string.Create(CultureInfo.InvariantCulture, $"FF Speed: {ffMult:0.#}x");
        layout.AddColumns(_controls,
            ("System_ToggleFastForward", "Fast-Forward (F6)", view.IsFastForwardEnabled, true, HostOverlayPalette.AccentWarning, _ =>
            {
                view.IsFastForwardEnabled = !view.IsFastForwardEnabled;
                view.EventCallbacks?.OnFastForwardChanged(view.IsFastForwardEnabled);
            }, null),
            ("System_FfMultiplier", ffSpeedLabel, ffMult > 2.01, false, HostOverlayPalette.AccentWarning, _ =>
            {
                double next = view.FastForwardMultiplier < 2.5 ? 3.0 : (view.FastForwardMultiplier < 3.5 ? 4.0 : 2.0);
                view.FastForwardMultiplier = next;
                view.EventCallbacks?.OnFastForwardMultiplierChanged(next);
            }, null));

        layout.AddPill("System_SkipCutscene", "Skip Cutscene (F7)", false, HostOverlayPalette.AccentWarning,
            _ => view.EventCallbacks?.OnCutsceneSkipRequested(), _controls);

        layout.AddToggle("System_ToggleRuntimeLogs", "Runtime Logging", view.ShouldEnableRuntimeLogs, HostOverlayPalette.AccentCyan, _ =>
        {
            view.ShouldEnableRuntimeLogs = !view.ShouldEnableRuntimeLogs;
            view.EventCallbacks?.OnRuntimeLoggingChanged(view.ShouldEnableRuntimeLogs);
        }, _controls);

        layout.AddToggle("System_ToggleCleanCapture", "Clean-Capture Mode", view.IsCleanCaptureModeEnabled, HostOverlayPalette.AccentCyan, _ =>
        {
            view.IsCleanCaptureModeEnabled = !view.IsCleanCaptureModeEnabled;
            view.EventCallbacks?.OnCleanCaptureModeChanged(view.IsCleanCaptureModeEnabled);
        }, _controls);

        Rectangle capturedCycleRect = Rectangle.Empty;
        layout.AddPill("Drawer_ActionButton", "Cycle Execution Tier", false, HostOverlayPalette.AccentCyan, _ =>
        {
            bool nextJit = !view.ActiveResilience.IsJitEnabled && view.ExecutionTierLabel.Contains("AOT", StringComparison.OrdinalIgnoreCase);
            string nextMode = nextJit ? "jitBringup" : "staticAotOnly";
            view.ActiveResilience = view.ActiveResilience with { IsJitEnabled = nextJit, ExecutionTierMode = nextMode };
            view.ExecutionTierLabel = nextJit ? "JIT Bring-Up" : "AOT Primary";
            view.EventCallbacks?.OnExecutionTierChanged(nextMode, nextJit);
        }, _controls, r => capturedCycleRect = r);

        layout.AddToggle("System_ToggleJit", "Allow Tier 3 JIT", res.IsJitEnabled, HostOverlayPalette.AccentCyan, _ =>
        {
            bool nextJit = !view.ActiveResilience.IsJitEnabled;
            string nextMode = nextJit ? "aotWithJitFallback" : "staticAotOnly";
            view.ActiveResilience = view.ActiveResilience with { IsJitEnabled = nextJit, ExecutionTierMode = nextMode };
            view.ExecutionTierLabel = nextJit ? "JIT Bring-Up" : "AOT Primary";
            view.EventCallbacks?.OnExecutionTierChanged(nextMode, nextJit);
        }, _controls);

        layout.AddToggle("System_TogglePreloadRam", "Preload Media to RAM", res.ShouldPreloadMediaToRam, HostOverlayPalette.AccentCyan, _ =>
        {
            bool next = !view.ActiveResilience.ShouldPreloadMediaToRam;
            view.ActiveResilience = view.ActiveResilience with { ShouldPreloadMediaToRam = next };
            view.EventCallbacks?.OnPreloadMediaToRamChanged(next);
        }, _controls);

        layout.AddToggle("System_ToggleFastBoot", "Fast Boot (Skip Splash)", res.IsFastBootEnabled, HostOverlayPalette.AccentCyan, _ =>
        {
            bool next = !view.ActiveResilience.IsFastBootEnabled;
            view.ActiveResilience = view.ActiveResilience with { IsFastBootEnabled = next };
            view.EventCallbacks?.OnFastBootChanged(next);
        }, _controls);

        string safeLabel = res.IsSafeModeActive ? "Safe Mode: ACTIVE (Reset)" : "Safe Mode: Off (Reset)";
        layout.AddPill("System_ResetSafeMode", safeLabel, res.IsSafeModeActive, HostOverlayPalette.AccentWarning, _ =>
        {
            view.ActiveResilience = view.ActiveResilience with { IsSafeModeActive = false };
            view.EventCallbacks?.OnSafeModeResetRequested();
        }, _controls);

        layout.AddPill("System_QuitGame", "Quit Game", true, HostOverlayPalette.AccentDanger, _ =>
        {
            view.QuitRequested = true;
            view.EventCallbacks?.OnQuitGameRequested();
        }, _controls);

        HostDrawerScrollLayout.AppendExtraRows(layout, HostDrawerTabId.System, descriptor.ExtraRows, view, _controls);
        ContentBottomY = layout.Render(canvas, "SYSTEM & EXECUTION", "System", ref scrollOffset, out maxScroll, _controls, d => view.ScrollActiveTab(d));
        cyclePrimaryRect = capturedCycleRect;
        foreach (DrawerControl c in _controls) _widgets.Add(new(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
    }

    /// <summary>Returns interactive widget bounds emitted by the latest render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Processes a pointer click inside the System tab.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, bool isRightClick = false)
    {
        foreach (DrawerControl target in _controls)
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
}
