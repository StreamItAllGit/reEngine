// @module: reEngine.Host.Ui.F1SettingsDrawerView
// @purpose: Console-agnostic 6-tab F1 slide-over drawer (Video, Audio, Controls, Keybinds, System, Disc/Media) on PixelOverlayCanvas.
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
        (HostDrawerTabId.Controls, "Tab_Controls", "Ctrl"),
        (HostDrawerTabId.Keybinds, "Tab_Keybinds", "Keys"),
        (HostDrawerTabId.System, "Tab_System", "Sys"),
        (HostDrawerTabId.DiscMedia, "Tab_DiscMedia", "Media")
    ];

    private readonly AudioTabView _audioTab = new();
    private readonly ControlsTabView _controlsTab = new();
    private readonly KeybindsTabView _keybindsTab = new();
    private readonly SystemTabView _systemTab = new();
    private readonly Dictionary<HostDrawerTabId, Rectangle> _tabRects = new();
    private readonly List<DrawerControl> _activeTabControls = new();
    private Rectangle _drawerRect, _cyclePrimaryRect, _closeRect;
    private HostMultiBindRegistry? _boundRegistry;
    private HostControlTierStore? _boundTierStore;

    /// <summary>Gets or sets whether the F1 drawer is currently visible.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Gets or sets the active tab shown inside the drawer.</summary>
    public HostDrawerTabId ActiveTab { get; set; } = HostDrawerTabId.Video;

    /// <summary>Gets or sets the currently selected render backend.</summary>
    public HostRenderBackendKind SelectedBackend { get; set; } = HostRenderBackendKind.Software;

    /// <summary>Gets or sets the active integer resolution scale multiplier.</summary>
    public int ResolutionScale { get; set; } = 2;

    /// <summary>Gets or sets the active Standard 33 schema preset.</summary>
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

    /// <summary>Gets or sets an optional 3-tier control store bound to the Keybinds tab.</summary>
    public HostControlTierStore? ControlTierStore { get; set; }

    /// <summary>Gets the Audio sub-view.</summary>
    public AudioTabView AudioTab => _audioTab;

    /// <summary>Gets the Controls sub-view.</summary>
    public ControlsTabView ControlsTab => _controlsTab;

    /// <summary>Gets the Keybinds sub-view.</summary>
    public KeybindsTabView KeybindsTab => _keybindsTab;

    /// <summary>Gets the System sub-view.</summary>
    public SystemTabView SystemTab => _systemTab;

    /// <summary>Renders the drawer using the bound registry or a default schema registry.</summary>
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
        if (!IsOpen) return;

        _activeTabControls.Clear();
        int desiredW = ActiveTab == HostDrawerTabId.Keybinds ? Math.Min(canvas.Width - 20, 720) : Math.Min(Math.Max(340, canvas.Width / 3), 460);
        int drawerW = Math.Clamp(desiredW, Math.Min(260, canvas.Width - 20), Math.Max(260, canvas.Width - 20));
        _drawerRect = new Rectangle(canvas.Width - drawerW - 10, 10, drawerW, Math.Max(200, canvas.Height - 20));
        HostOverlayLayoutHelpers.DrawPanel(canvas, _drawerRect, HostOverlayPalette.Surface2Glass, HostOverlayPalette.Border);
        canvas.DrawText(_drawerRect.X + 12, _drawerRect.Y + 12, HostOverlayLayoutHelpers.FitText(descriptor.EngineDisplayName, _drawerRect.Width - 88, 1), HostOverlayPalette.TextPrimary, 1);
        _closeRect = HostOverlayLayoutHelpers.DrawPillButton(canvas, new Rectangle(_drawerRect.Right - 64, _drawerRect.Y + 8, 54, 20), "Close", false, HostOverlayPalette.AccentCyan);

        RenderTabBar(canvas);
        var body = new Rectangle(_drawerRect.X + 12, _drawerRect.Y + 70, _drawerRect.Width - 24, _drawerRect.Height - 82);
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
        if (!_cyclePrimaryRect.IsEmpty) list.Add(new("Drawer_ActionButton", "Button", "Cycle Active Option", _cyclePrimaryRect, true));
        foreach (DrawerControl c in _activeTabControls) list.Add(new(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
        if (ActiveTab == HostDrawerTabId.Audio) list.AddRange(_audioTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.Controls) list.AddRange(_controlsTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.Keybinds) list.AddRange(_keybindsTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.System) list.AddRange(_systemTab.GetWidgets());
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

    private void RenderTabBar(PixelOverlayCanvas canvas)
    {
        const int gap = 4;
        int tabW = Math.Max(38, (_drawerRect.Width - 24 - (gap * (Tabs.Length - 1))) / Tabs.Length);
        int x = _drawerRect.X + 12, y = _drawerRect.Y + 36;
        foreach ((HostDrawerTabId tab, _, string label) in Tabs)
        {
            var rect = new Rectangle(x, y, tabW, 24);
            _tabRects[tab] = rect;
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, ActiveTab == tab, HostOverlayPalette.AccentCyan);
            x += tabW + gap;
        }
    }

    private void RenderActiveTabBody(PixelOverlayCanvas canvas, Rectangle body, HostDrawerDescriptor descriptor, HostMultiBindRegistry registry)
    {
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video:
                F1DrawerTabRenderers.RenderVideoTab(canvas, body, this, descriptor, _activeTabControls, out _cyclePrimaryRect);
                break;
            case HostDrawerTabId.Audio:
                _cyclePrimaryRect = Rectangle.Empty;
                _audioTab.Render(canvas, body, this, descriptor);
                F1DrawerTabRenderers.RenderExtraRows(canvas, body, _audioTab.ContentBottomY, HostDrawerTabId.Audio, descriptor.ExtraRows, this, _activeTabControls);
                break;
            case HostDrawerTabId.Controls:
                _controlsTab.Render(canvas, body, SelectedSchema, descriptor.ControlOptions);
                _cyclePrimaryRect = _controlsTab.CycleSchemaButtonRect;
                F1DrawerTabRenderers.RenderExtraRows(canvas, body, _controlsTab.ContentBottomY + 4, HostDrawerTabId.Controls, descriptor.ExtraRows, this, _activeTabControls);
                break;
            case HostDrawerTabId.Keybinds:
                _cyclePrimaryRect = Rectangle.Empty;
                HostControlTierStore? tierStore = ControlTierStore ?? _boundTierStore;
                if (tierStore is not null) _keybindsTab.Render(canvas, body, SelectedSchema, tierStore);
                else _keybindsTab.Render(canvas, body, SelectedSchema, registry);
                break;
            case HostDrawerTabId.System:
                _systemTab.Render(canvas, body, this, descriptor, out _cyclePrimaryRect);
                F1DrawerTabRenderers.RenderExtraRows(canvas, body, _systemTab.ContentBottomY, HostDrawerTabId.System, descriptor.ExtraRows, this, _activeTabControls);
                break;
            default:
                _cyclePrimaryRect = Rectangle.Empty;
                F1DrawerTabRenderers.RenderDiscMediaTab(canvas, body, this, descriptor, _activeTabControls);
                break;
        }
    }

    private void CyclePrimarySetting(HostInputMapper inputMapper)
    {
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video:
                ResolutionScale = ResolutionScale switch { 1 => 2, 2 => 4, _ => 1 };
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

    private static bool MatchClick(int x, int y, string? targetId, string controlId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, controlId, StringComparison.OrdinalIgnoreCase) : !bounds.IsEmpty && bounds.Contains(x, y);
}
