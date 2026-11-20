// @module: reEngine.Host.Ui.HostDrawerControllerNav
// @purpose: 100% gamepad focus navigation across F1SettingsDrawerView tabs/controls and FirstRunMediaGateView with repeat throttling.
// @depends-on: System, System.Collections.Generic, System.Drawing, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: HostDrawerControllerNav

using System.Drawing;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Translates gamepad <c>D-Pad</c>, <c>Left Stick</c>, <c>LB/RB</c>, and <c>A/B/X/Y</c> inputs into F1 drawer and Media Gate focus navigation.
/// </summary>
public sealed class HostDrawerControllerNav
{
    private enum NavDir { None, Up, Down, Left, Right }

    private const int InitialRepeatFrames = 18;
    private const int RepeatIntervalFrames = 6;
    private static readonly string[] MediaGateFallbackOrder = ["MediaGate_BrowseButton", "MediaGate_SelectModuleButton", "MediaGate_LaunchDemoButton"];

    private HostGamepadButtons _prevDrawerButtons, _prevGateButtons;
    private NavDir _lastDrawerDir, _lastGateDir;
    private int _drawerHoldFrames, _gateHoldFrames;

    /// <summary>Gets or sets the currently focused control identifier.</summary>
    public string? FocusedControlId { get; set; }

    /// <summary>Processes controller navigation for an open <see cref="F1SettingsDrawerView"/>.</summary>
    public bool TryProcessFrame(HostInputFrame input, F1SettingsDrawerView drawer, HostInputMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(mapper);

        HostGamepadButtons justPressed = input.DigitalButtons & ~_prevDrawerButtons;
        _prevDrawerButtons = input.DigitalButtons;

        if (!drawer.IsOpen)
        {
            _lastDrawerDir = NavDir.None;
            _drawerHoldFrames = 0;
            if (FocusedControlId is not null) { FocusedControlId = null; drawer.FocusedControlId = null; }
            return false;
        }

        if (drawer.ActiveTab == HostDrawerTabId.Keybinds && drawer.KeybindsTab.IsCapturing)
        {
            if (justPressed.HasFlag(HostGamepadButtons.East)) { drawer.KeybindsTab.ClearCapture(); return true; }
            return false;
        }

        int tabStep = justPressed.HasFlag(HostGamepadButtons.RightShoulder) ? 1 : (justPressed.HasFlag(HostGamepadButtons.LeftShoulder) ? -1 : 0);
        if (tabStep != 0)
        {
            IReadOnlyList<HostDrawerTabId> ordered = F1SettingsDrawerView.OrderedTabs;
            int idx = 0;
            for (int i = 0; i < ordered.Count; i++) if (ordered[i] == drawer.ActiveTab) { idx = i; break; }
            drawer.ActiveTab = ordered[(idx + tabStep + ordered.Count) % ordered.Count];
            drawer.KeybindsTab.ClearCapture();
            FocusedControlId = null;
            drawer.FocusedControlId = null;
            return true;
        }

        if (justPressed.HasFlag(HostGamepadButtons.East))
        {
            drawer.IsOpen = false;
            FocusedControlId = null;
            drawer.FocusedControlId = null;
            return true;
        }

        List<HostWidgetBounds> focusables = CollectFocusableDrawerWidgets(drawer.GetWidgets());
        EnsureValidFocus(focusables, drawer);

        if (FocusedControlId is not null)
        {
            if (justPressed.HasFlag(HostGamepadButtons.South)) return drawer.TryActivateControl(FocusedControlId, mapper, isSecondary: false);
            if (justPressed.HasFlag(HostGamepadButtons.West) || justPressed.HasFlag(HostGamepadButtons.North))
                return drawer.TryActivateControl(FocusedControlId, mapper, isSecondary: true);
        }

        NavDir stepDir = PollDirectionWithThrottle(input, ref _lastDrawerDir, ref _drawerHoldFrames);
        if (stepDir == NavDir.None || focusables.Count == 0) return false;

        int currentIdx = FindIndexById(focusables, FocusedControlId);
        int nextIdx = FindDirectionalNeighbor(focusables, currentIdx, stepDir);
        if (nextIdx >= 0 && nextIdx != currentIdx)
        {
            FocusedControlId = focusables[nextIdx].ControlId;
            drawer.FocusedControlId = FocusedControlId;
            return true;
        }

        if (stepDir == NavDir.Down && drawer.CanScrollActiveTab(1)) return drawer.ScrollActiveTab(1);
        if (stepDir == NavDir.Up && drawer.CanScrollActiveTab(-1)) return drawer.ScrollActiveTab(-1);
        return false;
    }

    /// <summary>Processes controller focus navigation for a visible <see cref="FirstRunMediaGateView"/>.</summary>
    public bool TryProcessMediaGate(HostInputFrame input, FirstRunMediaGateView mediaGate)
    {
        ArgumentNullException.ThrowIfNull(mediaGate);
        HostGamepadButtons justPressed = input.DigitalButtons & ~_prevGateButtons;
        _prevGateButtons = input.DigitalButtons;

        if (!mediaGate.IsVisible)
        {
            _lastGateDir = NavDir.None;
            _gateHoldFrames = 0;
            mediaGate.FocusedControlId = null;
            return false;
        }

        var buttons = new List<HostWidgetBounds>(4);
        foreach (HostWidgetBounds w in mediaGate.GetWidgets())
            if (w.IsVisible && string.Equals(w.ControlType, "Button", StringComparison.OrdinalIgnoreCase)) buttons.Add(w);

        string activeId = mediaGate.FocusedControlId ?? FocusedControlId ?? MediaGateFallbackOrder[0];
        if (buttons.Count > 0 && FindIndexById(buttons, activeId) < 0) activeId = buttons[0].ControlId;
        FocusedControlId = activeId;
        mediaGate.FocusedControlId = activeId;

        if (justPressed.HasFlag(HostGamepadButtons.South)) return mediaGate.TryHandleClick(0, 0, activeId);

        NavDir dir = PollDirectionWithThrottle(input, ref _lastGateDir, ref _gateHoldFrames);
        if (dir == NavDir.None) return false;

        int delta = dir is NavDir.Right or NavDir.Down ? 1 : -1;
        if (buttons.Count > 0) activeId = buttons[(Math.Max(0, FindIndexById(buttons, activeId)) + delta + buttons.Count) % buttons.Count].ControlId;
        else activeId = MediaGateFallbackOrder[(Math.Max(0, Array.IndexOf(MediaGateFallbackOrder, activeId)) + delta + MediaGateFallbackOrder.Length) % MediaGateFallbackOrder.Length];

        FocusedControlId = activeId;
        mediaGate.FocusedControlId = activeId;
        return true;
    }

    private static List<HostWidgetBounds> CollectFocusableDrawerWidgets(IReadOnlyList<HostWidgetBounds> all)
    {
        var list = new List<HostWidgetBounds>(all.Count);
        foreach (HostWidgetBounds w in all)
            if (w.IsVisible && w.ControlType is "Button" or "CheckBox" && !w.ControlId.StartsWith("Tab_", StringComparison.OrdinalIgnoreCase)) list.Add(w);
        return list;
    }

    private void EnsureValidFocus(List<HostWidgetBounds> focusables, F1SettingsDrawerView drawer)
    {
        if (focusables.Count == 0) { FocusedControlId = null; drawer.FocusedControlId = null; return; }
        if (FindIndexById(focusables, FocusedControlId) >= 0) { drawer.FocusedControlId = FocusedControlId; return; }
        HostWidgetBounds? preferred = focusables.Find(w =>
            !string.Equals(w.ControlId, "Drawer_CloseButton", StringComparison.OrdinalIgnoreCase) &&
            !w.ControlId.EndsWith("_ScrollUp", StringComparison.OrdinalIgnoreCase) &&
            !w.ControlId.EndsWith("_ScrollDown", StringComparison.OrdinalIgnoreCase));
        FocusedControlId = (preferred ?? focusables[0]).ControlId;
        drawer.FocusedControlId = FocusedControlId;
    }

    private static NavDir PollDirectionWithThrottle(HostInputFrame input, ref NavDir lastDir, ref int holdFrames)
    {
        NavDir raw = input.DigitalButtons.HasFlag(HostGamepadButtons.DPadUp) || input.LeftStickY <= -0.5f ? NavDir.Up
            : input.DigitalButtons.HasFlag(HostGamepadButtons.DPadDown) || input.LeftStickY >= 0.5f ? NavDir.Down
            : input.DigitalButtons.HasFlag(HostGamepadButtons.DPadLeft) || input.LeftStickX <= -0.5f ? NavDir.Left
            : input.DigitalButtons.HasFlag(HostGamepadButtons.DPadRight) || input.LeftStickX >= 0.5f ? NavDir.Right : NavDir.None;
        if (raw == NavDir.None) { lastDir = NavDir.None; holdFrames = 0; return NavDir.None; }
        if (raw != lastDir) { lastDir = raw; holdFrames = 0; return raw; }
        holdFrames++;
        return holdFrames >= InitialRepeatFrames && (holdFrames - InitialRepeatFrames) % RepeatIntervalFrames == 0 ? raw : NavDir.None;
    }

    private static int FindIndexById(IReadOnlyList<HostWidgetBounds> list, string? id)
    {
        if (!string.IsNullOrEmpty(id)) for (int i = 0; i < list.Count; i++) if (string.Equals(list[i].ControlId, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static int FindDirectionalNeighbor(IReadOnlyList<HostWidgetBounds> list, int curIdx, NavDir dir)
    {
        if (curIdx < 0 || curIdx >= list.Count) return list.Count > 0 ? 0 : -1;
        Rectangle cur = list[curIdx].Bounds;
        int curCx = cur.X + (cur.Width / 2), curCy = cur.Y + (cur.Height / 2), bestIdx = -1;
        double bestScore = double.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            if (i == curIdx) continue;
            Rectangle r = list[i].Bounds;
            int dx = r.X + (r.Width / 2) - curCx, dy = r.Y + (r.Height / 2) - curCy;
            bool valid = dir switch
            {
                NavDir.Up => dy < -4,
                NavDir.Down => dy > 4,
                NavDir.Left => dx < -4 && Math.Abs(dy) <= Math.Max(cur.Height, r.Height),
                NavDir.Right => dx > 4 && Math.Abs(dy) <= Math.Max(cur.Height, r.Height),
                _ => false
            };
            if (!valid) continue;
            double score = dir is NavDir.Up or NavDir.Down ? Math.Abs(dy) + (Math.Abs(dx) * 1.5) : Math.Abs(dx) + (Math.Abs(dy) * 2.0);
            if (score < bestScore) { bestScore = score; bestIdx = i; }
        }
        return bestIdx;
    }
}
