// @module: reEngine.Host.Ui.F1SettingsDrawerView
// @purpose: Console-agnostic 6-tab F1 slide-over drawer with scaled crisp text, full tab names, scroll support, and controller focus.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Audio, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: F1SettingsDrawerView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Immediate-mode 6-tab F1 settings drawer rendered directly onto <see cref="PixelOverlayCanvas"/> with live callback dispatch.
/// </summary>
public sealed class F1SettingsDrawerView
{
    private static readonly (HostDrawerTabId Tab, string ControlId, string Label)[] Tabs =
    [
        (HostDrawerTabId.Video, "Tab_Video", "Video"),
        (HostDrawerTabId.Audio, "Tab_Audio", "Audio"),
        (HostDrawerTabId.Controls, "Tab_Controls", "Controls"),
        (HostDrawerTabId.Keybinds, "Tab_Keybinds", "Keybinds"),
        (HostDrawerTabId.System, "Tab_System", "System"),
        (HostDrawerTabId.DiscMedia, "Tab_DiscMedia", "Media")
    ];

    private readonly AudioTabView _audioTab = new();
    private readonly ControlsTabView _controlsTab = new();
    private readonly KeybindsTabView _keybindsTab = new();
    private readonly SystemTabView _systemTab = new();
    private readonly Dictionary<HostDrawerTabId, Rectangle> _tabRects = new();
    private readonly Dictionary<HostDrawerTabId, int> _scrollOffsets = new();
    private readonly List<DrawerControl> _activeTabControls = new();
    private Rectangle _drawerRect, _cyclePrimaryRect, _closeRect;
    private HostMultiBindRegistry? _boundRegistry;
    private HostControlTierStore? _boundTierStore;
    private int _activeTabMaxScroll;
    private double _uiTextScale = 1.0;

    /// <summary>Gets or sets whether the F1 drawer is currently visible.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Gets or sets the active tab shown inside the drawer.</summary>
    public HostDrawerTabId ActiveTab { get; set; } = HostDrawerTabId.Video;

    /// <summary>Gets or sets the currently selected render backend.</summary>
    public HostRenderBackendKind SelectedBackend { get; set; } = HostRenderBackendKind.Software;

    /// <summary>Gets or sets the active integer resolution scale multiplier.</summary>
    public int ResolutionScale { get; set; } = 2;

    /// <summary>Gets or sets the active Standard 33 preset selection.</summary>
    public HostInputSchemaKind SelectedSchema { get; set; } = HostInputSchemaKind.SchemaB;

    /// <summary>Gets or sets the execution tier label displayed in the System tab.</summary>
    public string ExecutionTierLabel { get; set; } = "AOT Primary";

    /// <summary>Gets or sets whether fullscreen mode is active.</summary>
    public bool IsFullscreen { get; set; }

    /// <summary>Gets or sets whether HD texture packs are enabled.</summary>
    public bool IsHdTexturePackEnabled { get; set; }

    /// <summary>Gets or sets an optional backend fallback diagnostic banner shown in the Video tab.</summary>
    public string? FallbackBanner { get; set; }

    /// <summary>Gets or sets the active presentation aspect ratio mode.</summary>
    public HostAspectRatioMode AspectRatioMode { get; set; } = HostAspectRatioMode.NativePar;

    /// <summary>Gets or sets the active pixel edge overscan crop trim.</summary>
    public HostOverscanCrop OverscanCropPx { get; set; }

    /// <summary>Gets or sets whether runtime fast-forward is enabled.</summary>
    public bool IsFastForwardEnabled { get; set; }

    /// <summary>Gets or sets the active host window display mode.</summary>
    public HostDisplayMode DisplayMode { get; set; } = HostDisplayMode.Windowed;

    /// <summary>Gets or sets whether swapchain vertical sync is enabled.</summary>
    public bool IsVsyncEnabled { get; set; }

    /// <summary>Gets or sets the active Widescreen HUD anchor policy.</summary>
    public HostHudAnchorMode HudAnchorMode { get; set; } = HostHudAnchorMode.SafeFourByThree;

    /// <summary>Gets or sets the active live presentation filter preset.</summary>
    public HostVideoFilterMode VideoFilterMode { get; set; } = HostVideoFilterMode.Nearest;

    /// <summary>Gets or sets whether 24-bit internal RGBA color is enabled.</summary>
    public bool IsTrueColor24BitEnabled { get; set; } = true;

    /// <summary>Gets or sets whether 15-bit quantization + 4x4 Bayer dithering is enabled.</summary>
    public bool IsDitheringEnabled { get; set; }

    /// <summary>Gets or sets the <c>F12</c> screenshot capture source mode.</summary>
    public HostScreenshotCaptureMode ScreenshotCaptureMode { get; set; } = HostScreenshotCaptureMode.CleanNative;

    /// <summary>Gets or sets the active overlay UI text scale multiplier (<c>0.75..2.0</c>).</summary>
    public double UiTextScale { get => _uiTextScale; set => _uiTextScale = HostOverlayTextRenderer.ClampUiTextScale(value); }

    /// <summary>Gets or sets the active framerate cadence mode.</summary>
    public HostFrameRateMode FrameRateMode { get; set; } = HostFrameRateMode.Enhanced;

    /// <summary>Gets or sets the target frame rate (<c>0</c> = Max / Unlocked).</summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>Gets or sets the active camera/movement control mode.</summary>
    public HostControlMode ControlMode { get; set; } = HostControlMode.ModernWasdMouse;

    /// <summary>Gets or sets mouse look sensitivity multiplier.</summary>
    public double MouseSensitivity { get; set; } = 1.0;

    /// <summary>Gets or sets the gamepad overlay toggle chord.</summary>
    public HostMenuControllerChord MenuControllerChord { get; set; } = HostMenuControllerChord.L3PlusR3;

    /// <summary>Gets or sets whether the F3 FPS overlay HUD is toggled on.</summary>
    public bool IsFpsOverlayVisible { get; set; }

    /// <summary>Gets or sets whether Clean-Capture mode hides host overlays during screenshots/streaming.</summary>
    public bool IsCleanCaptureModeEnabled { get; set; }

    /// <summary>Gets or sets whether diagnostic runtime logging is enabled.</summary>
    public bool ShouldEnableRuntimeLogs { get; set; }

    /// <summary>Gets or sets the fast-forward speed multiplier (<c>2x..4x</c>).</summary>
    public double FastForwardMultiplier { get; set; } = 2.0;

    /// <summary>Gets or sets the currently focused control identifier for controller navigation.</summary>
    public string? FocusedControlId { get; set; }

    /// <summary>Gets or sets the scroll offset for the currently active tab.</summary>
    public int ActiveTabScrollOffset
    {
        get => ActiveTab == HostDrawerTabId.Keybinds ? _keybindsTab.ScrollOffset : (_scrollOffsets.TryGetValue(ActiveTab, out int v) ? v : 0);
        set
        {
            if (ActiveTab == HostDrawerTabId.Keybinds) _keybindsTab.ScrollBy(value - _keybindsTab.ScrollOffset, Math.Max(1, _keybindsTab.TotalRowCount));
            else _scrollOffsets[ActiveTab] = Math.Max(0, value);
        }
    }

    /// <summary>Gets or sets whether the user explicitly requested quitting via <c>Quit Game</c>.</summary>
    public bool QuitRequested { get; set; }

    /// <summary>Gets or sets the active audio DSP mixer settings displayed in the Audio tab.</summary>
    public HostAudioMixerSettings AudioSettings { get; set; } = new();

    /// <summary>Gets or sets enumerated OS audio playback endpoints displayed in the Audio tab.</summary>
    public IReadOnlyList<HostAudioDeviceInfo> AudioOutputDevices { get; set; } = Array.Empty<HostAudioDeviceInfo>();

    /// <summary>Gets or sets the Sound Test jukebox service bound to the Audio tab.</summary>
    public HostSoundTestService SoundTestService { get; set; } = new();

    /// <summary>Gets or sets active execution resilience options displayed in the System tab.</summary>
    public HostExecutionResilienceOptions ActiveResilience { get; set; } = new();

    /// <summary>Gets or sets live session event callbacks invoked when interactive drawer controls change state.</summary>
    public IHostSessionEventCallbacks? EventCallbacks { get; set; }

    /// <summary>Gets or sets an optional 3-tier control store bound to the Keybinds and Controls tabs.</summary>
    public HostControlTierStore? ControlTierStore { get; set; }

    /// <summary>Gets or sets a file-picker callback invoked when <c>Change Media...</c> is clicked.</summary>
    public Func<string?>? OnBrowseMediaRequested { get; set; }

    /// <summary>Gets or sets a file-picker callback invoked when <c>Load .recomp...</c> is clicked.</summary>
    public Func<string?>? OnBrowseModuleRequested { get; set; }

    /// <summary>Gets the Audio sub-view.</summary>
    public AudioTabView AudioTab => _audioTab;

    /// <summary>Gets the Controls sub-view.</summary>
    public ControlsTabView ControlsTab => _controlsTab;

    /// <summary>Gets the Keybinds sub-view.</summary>
    public KeybindsTabView KeybindsTab => _keybindsTab;

    /// <summary>Gets the System sub-view.</summary>
    public SystemTabView SystemTab => _systemTab;

    /// <summary>Renders the drawer using the bound registry or a default registry.</summary>
    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor) =>
        Render(canvas, descriptor, ControlTierStore?.ActiveRegistry ?? _boundRegistry ?? new HostMultiBindRegistry(SelectedSchema));

    /// <summary>Renders the drawer against the provided <paramref name="registry"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor, HostMultiBindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(registry);
        _boundRegistry = registry;
        _controlsTab.EventCallbacks = EventCallbacks;
        _keybindsTab.EventCallbacks = EventCallbacks;
        if (!IsOpen) return;

        _activeTabControls.Clear();
        double scale = UiTextScale;
        int maxAllowedW = Math.Max(220, canvas.Width - 20);
        int baseTarget = ActiveTab == HostDrawerTabId.Keybinds ? 720 : 460;
        int minAllowedW = Math.Min(ActiveTab == HostDrawerTabId.Keybinds ? 560 : 430, maxAllowedW);
        int capW = ActiveTab == HostDrawerTabId.Keybinds ? Math.Min(780, maxAllowedW) : maxAllowedW;
        int desiredW = (int)Math.Round(baseTarget * Math.Max(1.0, scale * 0.85));
        int drawerW = Math.Clamp(desiredW, minAllowedW, Math.Max(minAllowedW, capW));

        _drawerRect = new Rectangle(canvas.Width - drawerW - 10, 10, drawerW, Math.Max(200, canvas.Height - 20));
        HostOverlayLayoutHelpers.DrawPanel(canvas, _drawerRect, HostOverlayPalette.Surface2Glass, HostOverlayPalette.Border);

        int rowH = HostOverlayTextRenderer.GetRowHeight(scale);
        int closeW = Math.Clamp((int)Math.Round(60 * scale), 54, 110);
        HostOverlayTextRenderer.DrawText(canvas, _drawerRect.X + 12, _drawerRect.Y + 10,
            HostOverlayTextRenderer.FitText(descriptor.EngineDisplayName, Math.Max(40, _drawerRect.Width - closeW - 28), scale),
            HostOverlayPalette.TextPrimary, scale);

        _closeRect = HostOverlayLayoutHelpers.DrawPillButton(
            canvas, new Rectangle(_drawerRect.Right - closeW - 10, _drawerRect.Y + 8, closeW, rowH),
            "Close", false, HostOverlayPalette.AccentCyan, scale, IsFocused("Drawer_CloseButton"));

        int tabBarBottom = RenderTabBar(canvas, scale, rowH);
        var body = new Rectangle(_drawerRect.X + 12, tabBarBottom + 8, _drawerRect.Width - 24, Math.Max(60, _drawerRect.Bottom - tabBarBottom - 18));
        RenderActiveTabBody(canvas, body, descriptor, registry);
    }

    /// <summary>Returns widget geometry for the open drawer and active tab controls.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!IsOpen) return Array.Empty<HostWidgetBounds>();
        var list = new List<HostWidgetBounds>
        {
            new("F1Drawer", "Panel", $"F1 Drawer ({ActiveTab})", _drawerRect, true),
            new("Drawer_CloseButton", "Button", "Close", _closeRect, true)
        };
        foreach ((HostDrawerTabId tab, string id, string label) in Tabs)
        {
            if (_tabRects.TryGetValue(tab, out Rectangle r)) list.Add(new(id, "Button", label, r, true));
        }
        foreach (DrawerControl c in _activeTabControls) list.Add(new(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
        if (ActiveTab == HostDrawerTabId.Audio) list.AddRange(_audioTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.Controls) list.AddRange(_controlsTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.Keybinds) list.AddRange(_keybindsTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.System) list.AddRange(_systemTab.GetWidgets());
        if (!_cyclePrimaryRect.IsEmpty && !list.Exists(w => string.Equals(w.ControlId, "Drawer_ActionButton", StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new("Drawer_ActionButton", "Button", "Cycle Active Option", _cyclePrimaryRect, true));
        }
        return list;
    }

    /// <summary>Processes a primary pointer click inside the drawer.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper) =>
        TryHandleClick(x, y, targetId, inputMapper, isRightClick: false);

    /// <summary>Processes a primary or secondary (right) pointer click inside the drawer.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper, bool isRightClick)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _boundTierStore = ControlTierStore ?? inputMapper.ControlTiers;
        _boundRegistry = _boundTierStore.ActiveRegistry;
        _controlsTab.EventCallbacks = EventCallbacks;
        _keybindsTab.EventCallbacks = EventCallbacks;
        if (!IsOpen) return false;
        if (MatchClick(x, y, targetId, "Drawer_CloseButton", _closeRect)) { IsOpen = false; _keybindsTab.ClearCapture(); return true; }

        foreach ((HostDrawerTabId tab, string id, _) in Tabs)
        {
            if (_tabRects.TryGetValue(tab, out Rectangle rect) && MatchClick(x, y, targetId, id, rect))
            {
                ActiveTab = tab;
                if (tab != HostDrawerTabId.Keybinds) _keybindsTab.ClearCapture();
                return true;
            }
        }

        if (MatchClick(x, y, targetId, "Drawer_ActionButton", _cyclePrimaryRect)) { CyclePrimarySetting(inputMapper); return true; }
        foreach (DrawerControl c in _activeTabControls)
        {
            if (MatchClick(x, y, targetId, c.ControlId, c.Bounds)) { c.OnClick(isRightClick); return true; }
        }

        if (ActiveTab == HostDrawerTabId.Audio) return _audioTab.TryHandleClick(x, y, targetId, isRightClick);
        if (ActiveTab == HostDrawerTabId.Controls)
        {
            HostInputSchemaKind schema = SelectedSchema;
            bool handled = _controlsTab.TryHandleClick(x, y, targetId, inputMapper, ref schema, EventCallbacks);
            SelectedSchema = schema;
            return handled;
        }
        if (ActiveTab == HostDrawerTabId.System) return _systemTab.TryHandleClick(x, y, targetId, isRightClick);
        if (ActiveTab != HostDrawerTabId.Keybinds) return false;
        return _keybindsTab.TryHandleClick(x, y, targetId, _boundTierStore, SelectedSchema, isRightClick);
    }

    /// <summary>Activates a focused control programmatically by identifier (used by controller navigation).</summary>
    public bool TryActivateControl(string controlId, HostInputMapper inputMapper, bool isSecondary = false) =>
        !string.IsNullOrWhiteSpace(controlId) && TryHandleClick(0, 0, controlId, inputMapper, isSecondary);

    /// <summary>Processes vertical mouse-wheel scrolling on the open drawer's active tab.</summary>
    public bool TryHandleMouseWheel(float wheelDelta)
    {
        if (!IsOpen || MathF.Abs(wheelDelta) < 0.01f) return false;
        return ScrollActiveTab(wheelDelta > 0f ? -1 : 1);
    }

    /// <summary>Scrolls the active drawer tab by <paramref name="deltaSteps"/>.</summary>
    public bool ScrollActiveTab(int deltaSteps)
    {
        if (!IsOpen || deltaSteps == 0) return false;
        if (ActiveTab == HostDrawerTabId.Keybinds)
            return _keybindsTab.ScrollBy(deltaSteps, Math.Max(1, _keybindsTab.TotalRowCount));

        int cur = _scrollOffsets.TryGetValue(ActiveTab, out int v) ? v : 0;
        int next = Math.Clamp(cur + deltaSteps, 0, _activeTabMaxScroll);
        if (next == cur) return false;
        _scrollOffsets[ActiveTab] = next;
        return true;
    }

    internal bool CanScrollActiveTab(int direction) =>
        ActiveTab == HostDrawerTabId.Keybinds
            ? (direction < 0 ? _keybindsTab.ScrollOffset > 0 : _keybindsTab.ScrollOffset < _keybindsTab.MaxScrollOffset)
            : (direction < 0 ? ActiveTabScrollOffset > 0 : ActiveTabScrollOffset < _activeTabMaxScroll);

    internal static IReadOnlyList<HostDrawerTabId> OrderedTabs =>
        [HostDrawerTabId.Video, HostDrawerTabId.Audio, HostDrawerTabId.Controls, HostDrawerTabId.Keybinds, HostDrawerTabId.System, HostDrawerTabId.DiscMedia];

    /// <summary>Forwards an input binding code to the active Keybinds capture cell.</summary>
    public bool TryCaptureInputCode(int inputCode, HostInputMapper inputMapper)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _boundTierStore = ControlTierStore ?? inputMapper.ControlTiers;
        _boundRegistry = _boundTierStore.ActiveRegistry;
        return IsOpen && ActiveTab == HostDrawerTabId.Keybinds && _keybindsTab.TryCaptureInputCode(inputCode, _boundTierStore);
    }

    /// <summary>Returns the canonical automation control identifier for a drawer tab.</summary>
    public static string GetTabControlId(HostDrawerTabId tab) => tab switch
    {
        HostDrawerTabId.Video => "Tab_Video",
        HostDrawerTabId.Audio => "Tab_Audio",
        HostDrawerTabId.Controls => "Tab_Controls",
        HostDrawerTabId.Keybinds => "Tab_Keybinds",
        HostDrawerTabId.System => "Tab_System",
        _ => "Tab_DiscMedia"
    };

    private int RenderTabBar(PixelOverlayCanvas canvas, double scale, int rowH)
    {
        const int gap = 4;
        int availW = _drawerRect.Width - 24;
        int startX = _drawerRect.X + 12;
        int y = _drawerRect.Y + rowH + 14;
        int pad = Math.Max(8, (int)Math.Round(10 * scale));
        int[] widths = new int[Tabs.Length];
        int sumW = gap * (Tabs.Length - 1);
        for (int i = 0; i < Tabs.Length; i++)
        {
            widths[i] = HostOverlayTextRenderer.MeasureText(Tabs[i].Label, scale).Width + pad;
            sumW += widths[i];
        }

        if (sumW <= availW)
        {
            int slack = availW - sumW;
            int x = startX;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int extra = slack / (Tabs.Length - i);
                slack -= extra;
                int w = widths[i] + extra;
                var rect = new Rectangle(x, y, w, rowH);
                _tabRects[Tabs[i].Tab] = rect;
                HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, Tabs[i].Label, ActiveTab == Tabs[i].Tab, HostOverlayPalette.AccentCyan, scale, IsFocused(Tabs[i].ControlId));
                x += w + gap;
            }
            return y + rowH;
        }

        for (int row = 0; row < 2; row++)
        {
            int ry = y + (row * (rowH + gap));
            int colW = Math.Max(36, (availW - (gap * 2)) / 3);
            for (int col = 0; col < 3; col++)
            {
                int idx = (row * 3) + col;
                int x = startX + (col * (colW + gap));
                int w = col == 2 ? Math.Max(36, startX + availW - x) : colW;
                var rect = new Rectangle(x, ry, w, rowH);
                _tabRects[Tabs[idx].Tab] = rect;
                HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, Tabs[idx].Label, ActiveTab == Tabs[idx].Tab, HostOverlayPalette.AccentCyan, scale, IsFocused(Tabs[idx].ControlId));
            }
        }
        return y + (rowH * 2) + gap;
    }

    private void RenderActiveTabBody(PixelOverlayCanvas canvas, Rectangle body, HostDrawerDescriptor descriptor, HostMultiBindRegistry registry)
    {
        int offset = _scrollOffsets.TryGetValue(ActiveTab, out int s) ? s : 0;
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video:
                F1DrawerTabRenderers.RenderVideoTab(canvas, body, this, descriptor, _activeTabControls, ref offset, out _activeTabMaxScroll, out _cyclePrimaryRect);
                break;
            case HostDrawerTabId.Audio:
                _cyclePrimaryRect = Rectangle.Empty;
                _audioTab.Render(canvas, body, this, descriptor, ref offset, out _activeTabMaxScroll);
                break;
            case HostDrawerTabId.Controls:
                _controlsTab.Render(canvas, body, this, descriptor, ControlTierStore ?? _boundTierStore, ref offset, out _activeTabMaxScroll);
                _cyclePrimaryRect = _controlsTab.CycleSchemaButtonRect;
                break;
            case HostDrawerTabId.Keybinds:
                _cyclePrimaryRect = Rectangle.Empty;
                _keybindsTab.Render(canvas, body, SelectedSchema, ControlTierStore ?? _boundTierStore, registry, UiTextScale, FocusedControlId);
                _activeTabMaxScroll = _keybindsTab.MaxScrollOffset;
                return;
            case HostDrawerTabId.System:
                _systemTab.Render(canvas, body, this, descriptor, ref offset, out _activeTabMaxScroll, out _cyclePrimaryRect);
                break;
            default:
                _cyclePrimaryRect = Rectangle.Empty;
                F1DrawerTabRenderers.RenderDiscMediaTab(canvas, body, this, descriptor, _activeTabControls, ref offset, out _activeTabMaxScroll);
                break;
        }
        _scrollOffsets[ActiveTab] = offset;
    }

    private void CyclePrimarySetting(HostInputMapper inputMapper)
    {
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video:
                ResolutionScale = ResolutionScale switch { 1 => 2, 2 => 4, 4 => 8, _ => 1 };
                EventCallbacks?.OnResolutionScaleChanged(ResolutionScale);
                break;
            case HostDrawerTabId.Controls:
                SelectedSchema = HostInputSchemas.CycleNext(SelectedSchema);
                inputMapper.ActiveSchema = SelectedSchema;
                break;
            case HostDrawerTabId.System:
                bool nextJit = !ActiveResilience.IsJitEnabled && ExecutionTierLabel.Contains("AOT", StringComparison.OrdinalIgnoreCase);
                string nextMode = nextJit ? "jitBringup" : "staticAotOnly";
                ActiveResilience = ActiveResilience with { IsJitEnabled = nextJit, ExecutionTierMode = nextMode };
                ExecutionTierLabel = nextJit ? "JIT Bring-Up" : "AOT Primary";
                EventCallbacks?.OnExecutionTierChanged(nextMode, nextJit);
                break;
        }
    }

    private bool IsFocused(string id) =>
        !string.IsNullOrEmpty(FocusedControlId) && string.Equals(FocusedControlId, id, StringComparison.OrdinalIgnoreCase);

    private static bool MatchClick(int x, int y, string? targetId, string controlId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, controlId, StringComparison.OrdinalIgnoreCase) : !bounds.IsEmpty && bounds.Contains(x, y);
}
