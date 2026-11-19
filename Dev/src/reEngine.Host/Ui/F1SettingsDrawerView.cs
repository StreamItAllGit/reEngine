// @module: reEngine.Host.Ui.F1SettingsDrawerView
// @purpose: Console-agnostic 5-tab F1 slide-over drawer (Video, Controls, Keybinds, System, Disc/Media) on PixelOverlayCanvas.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: F1SettingsDrawerView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Immediate-mode 5-tab F1 settings drawer rendered directly onto <see cref="Foundation.Core.PixelOverlayCanvas"/>.
/// </summary>
public sealed class F1SettingsDrawerView
{
    private sealed record DrawerControl(string ControlId, string ControlType, string Label, Rectangle Bounds, Action OnClick);

    private static readonly (HostDrawerTabId Tab, string ControlId, string Label)[] Tabs =
    [
        (HostDrawerTabId.Video, "Tab_Video", "Video"),
        (HostDrawerTabId.Controls, "Tab_Controls", "Ctrl"),
        (HostDrawerTabId.Keybinds, "Tab_Keybinds", "Keys"),
        (HostDrawerTabId.System, "Tab_System", "Sys"),
        (HostDrawerTabId.DiscMedia, "Tab_DiscMedia", "Media")
    ];

    private static readonly (HostRenderBackendKind Kind, string Label)[] VideoBackends =
    [
        (HostRenderBackendKind.OpenGl, "OpenGl"),
        (HostRenderBackendKind.Vulkan, "Vulkan"),
        (HostRenderBackendKind.DirectX, "DirectX"),
        (HostRenderBackendKind.ParallelCpu, "ParallelCpu"),
        (HostRenderBackendKind.Software, "Software")
    ];

    private readonly ControlsTabView _controlsTab = new();
    private readonly KeybindsTabView _keybindsTab = new();
    private readonly Dictionary<HostDrawerTabId, Rectangle> _tabRects = new();
    private readonly List<DrawerControl> _activeTabControls = new();
    private Rectangle _drawerRect, _cyclePrimaryRect, _closeRect;
    private HostMultiBindRegistry? _boundRegistry;

    public bool IsOpen { get; set; }
    public HostDrawerTabId ActiveTab { get; set; } = HostDrawerTabId.Video;
    public HostRenderBackendKind SelectedBackend { get; set; } = HostRenderBackendKind.Software;
    public int ResolutionScale { get; set; } = 2;
    public HostInputSchemaKind SelectedSchema { get; set; } = HostInputSchemaKind.SchemaB;
    public string ExecutionTierLabel { get; set; } = "AOT Primary";
    public bool IsFullscreen { get; set; }
    public bool IsHdTexturePackEnabled { get; set; }
    public string? FallbackBanner { get; set; }
    public ControlsTabView ControlsTab => _controlsTab;
    public KeybindsTabView KeybindsTab => _keybindsTab;

    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor) =>
        Render(canvas, descriptor, _boundRegistry ?? new HostMultiBindRegistry(SelectedSchema));

    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor, HostMultiBindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(registry);
        _boundRegistry = registry;
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

    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!IsOpen) return Array.Empty<HostWidgetBounds>();
        var list = new List<HostWidgetBounds> { new("F1Drawer", "Panel", $"F1 Drawer ({ActiveTab})", _drawerRect, true), new("Drawer_CloseButton", "Button", "Close", _closeRect, true) };
        foreach ((HostDrawerTabId tab, string id, string label) in Tabs)
        {
            if (_tabRects.TryGetValue(tab, out Rectangle r)) list.Add(new HostWidgetBounds(id, "Button", label, r, true));
        }
        if (!_cyclePrimaryRect.IsEmpty) list.Add(new HostWidgetBounds("Drawer_ActionButton", "Button", "Cycle Active Option", _cyclePrimaryRect, true));
        foreach (DrawerControl c in _activeTabControls) list.Add(new HostWidgetBounds(c.ControlId, c.ControlType, c.Label, c.Bounds, true));
        if (ActiveTab == HostDrawerTabId.Controls) list.AddRange(_controlsTab.GetWidgets());
        else if (ActiveTab == HostDrawerTabId.Keybinds) list.AddRange(_keybindsTab.GetWidgets());
        return list;
    }

    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper) =>
        TryHandleClick(x, y, targetId, inputMapper, isRightClick: false);

    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper, bool isRightClick)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _boundRegistry = inputMapper.Bindings;
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
        foreach (DrawerControl c in _activeTabControls) { if (MatchClick(x, y, targetId, c.ControlId, c.Bounds)) { c.OnClick(); return true; } }

        if (ActiveTab == HostDrawerTabId.Controls)
        {
            HostInputSchemaKind schema = SelectedSchema;
            bool handled = _controlsTab.TryHandleClick(x, y, targetId, inputMapper, ref schema);
            SelectedSchema = schema;
            return handled;
        }
        return ActiveTab == HostDrawerTabId.Keybinds && _keybindsTab.TryHandleClick(x, y, targetId, inputMapper.Bindings, SelectedSchema, isRightClick);
    }

    public bool TryCaptureInputCode(int inputCode, HostInputMapper inputMapper)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        _boundRegistry = inputMapper.Bindings;
        return IsOpen && ActiveTab == HostDrawerTabId.Keybinds && _keybindsTab.TryCaptureInputCode(inputCode, inputMapper.Bindings);
    }

    public static string GetTabControlId(HostDrawerTabId tab) => tab switch
    {
        HostDrawerTabId.Video => "Tab_Video", HostDrawerTabId.Controls => "Tab_Controls",
        HostDrawerTabId.Keybinds => "Tab_Keybinds", HostDrawerTabId.System => "Tab_System", _ => "Tab_DiscMedia"
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
            case HostDrawerTabId.Video: RenderVideoTab(canvas, body); break;
            case HostDrawerTabId.Controls:
                _controlsTab.Render(canvas, body, SelectedSchema, descriptor.ControlOptions);
                _cyclePrimaryRect = _controlsTab.CycleSchemaButtonRect;
                break;
            case HostDrawerTabId.Keybinds:
                _cyclePrimaryRect = Rectangle.Empty;
                _keybindsTab.Render(canvas, body, SelectedSchema, registry);
                break;
            case HostDrawerTabId.System: RenderSystemTab(canvas, body, descriptor); break;
            default: RenderDiscMediaTab(canvas, body, descriptor); break;
        }
    }

    private void RenderVideoTab(PixelOverlayCanvas canvas, Rectangle body)
    {
        canvas.DrawText(body.X, body.Y, "VIDEO & SCALING", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 16, $"Backend: {SelectedBackend}", HostOverlayPalette.TextPrimary, 1);
        const int gap = 4;
        int colW3 = Math.Max(60, (body.Width - (gap * 2)) / 3), colW2 = Math.Max(80, (body.Width - gap) / 2);
        for (int i = 0; i < VideoBackends.Length; i++)
        {
            (HostRenderBackendKind kind, string label) = VideoBackends[i];
            int row = i < 3 ? 0 : 1, col = i < 3 ? i : i - 3, w = row == 0 ? colW3 : colW2;
            var rect = new Rectangle(body.X + (col * (w + gap)), body.Y + 32 + (row * 26), w, 22);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, SelectedBackend == kind, HostOverlayPalette.AccentCyan);
            _activeTabControls.Add(new($"Video_Backend_{label}", "Button", label, rect, () => SelectedBackend = kind));
        }

        canvas.DrawText(body.X, body.Y + 88, $"Resolution Scale: {ResolutionScale}x", HostOverlayPalette.TextPrimary, 1);
        int[] scales = [1, 2, 4];
        for (int i = 0; i < scales.Length; i++)
        {
            int sc = scales[i];
            var rect = new Rectangle(body.X + (i * (colW3 + gap)), body.Y + 104, colW3, 22);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, $"{sc}x", ResolutionScale == sc, HostOverlayPalette.AccentCyan);
            _activeTabControls.Add(new($"Video_Scale_{sc}x", "Button", $"{sc}x", rect, () => ResolutionScale = sc));
        }

        _cyclePrimaryRect = new Rectangle(body.X, body.Y + 132, Math.Min(220, body.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _cyclePrimaryRect, "Cycle Scale (1x/2x/4x)", true, HostOverlayPalette.AccentCyan);
        AddToggle(canvas, new(body.X, body.Y + 162, body.Width, 24), "Video_Toggle_Fullscreen", "Fullscreen", IsFullscreen, HostOverlayPalette.AccentCyan, () => IsFullscreen = !IsFullscreen);
        AddToggle(canvas, new(body.X, body.Y + 190, body.Width, 24), "Video_Toggle_HdPacks", "HD Packs", IsHdTexturePackEnabled, HostOverlayPalette.AccentSuccess, () => IsHdTexturePackEnabled = !IsHdTexturePackEnabled);

        if (!string.IsNullOrWhiteSpace(FallbackBanner))
        {
            var bannerRect = new Rectangle(body.X, body.Y + 220, body.Width, 26);
            HostOverlayLayoutHelpers.DrawBanner(canvas, bannerRect, FallbackBanner!, HostOverlayPalette.WarnFill, HostOverlayPalette.AccentWarning, HostOverlayPalette.AccentWarning);
            _activeTabControls.Add(new("Video_FallbackBanner", "Label", FallbackBanner!, bannerRect, static () => { }));
        }
    }

    private void AddToggle(PixelOverlayCanvas c, Rectangle rect, string id, string label, bool val, RgbaColor accent, Action onClick)
    {
        HostOverlayLayoutHelpers.DrawToggleRow(c, rect, label, val, accent);
        _activeTabControls.Add(new(id, "CheckBox", label, rect, onClick));
    }

    private void RenderSystemTab(PixelOverlayCanvas canvas, Rectangle body, HostDrawerDescriptor descriptor)
    {
        canvas.DrawText(body.X, body.Y, "SYSTEM & EXECUTION TIER", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 20, $"Tier Mode: {ExecutionTierLabel}", HostOverlayPalette.TextPrimary, 1);
        canvas.DrawText(body.X, body.Y + 38, "Runtime: Static Recompilation Host", HostOverlayPalette.TextMuted, 1);
        canvas.DrawText(body.X, body.Y + 56, HostOverlayLayoutHelpers.FitText($"Module: {descriptor.ModuleTitle}", body.Width, 1), HostOverlayPalette.TextMuted, 1);
        _cyclePrimaryRect = new Rectangle(body.X, body.Y + 78, Math.Min(220, body.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _cyclePrimaryRect, "Cycle Tier Mode", false, HostOverlayPalette.AccentCyan);
        RenderExtraRows(canvas, body, body.Y + 114, HostDrawerTabId.System, descriptor.ExtraRows);
    }

    private void RenderDiscMediaTab(PixelOverlayCanvas canvas, Rectangle body, HostDrawerDescriptor descriptor)
    {
        _cyclePrimaryRect = Rectangle.Empty;
        canvas.DrawText(body.X, body.Y, "DISC / MEDIA", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 22, HostOverlayLayoutHelpers.FitText($"Media: {descriptor.MediaLabel}", body.Width, 1), HostOverlayPalette.TextPrimary, 1);
        canvas.DrawText(body.X, body.Y + 40, "Storage: gitignored data\\rom\\", HostOverlayPalette.TextMuted, 1);
        RenderExtraRows(canvas, body, body.Y + 66, HostDrawerTabId.DiscMedia, descriptor.ExtraRows);
    }

    private static void RenderExtraRows(PixelOverlayCanvas canvas, Rectangle body, int startY, HostDrawerTabId tab, IReadOnlyList<HostDrawerRowDescriptor>? rows)
    {
        if (rows is null) return;
        int y = startY;
        foreach (HostDrawerRowDescriptor row in rows)
        {
            if (row.Tab != tab || y + 16 > body.Bottom) continue;
            canvas.DrawText(body.X, y, HostOverlayLayoutHelpers.FitText($"{row.Label}: {row.Value}", body.Width, 1), HostOverlayPalette.TextMuted, 1);
            y += 16;
        }
    }

    private void CyclePrimarySetting(HostInputMapper inputMapper)
    {
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video: ResolutionScale = ResolutionScale switch { 1 => 2, 2 => 4, _ => 1 }; break;
            case HostDrawerTabId.Controls: SelectedSchema = HostInputSchemas.CycleNext(SelectedSchema); inputMapper.ActiveSchema = SelectedSchema; break;
            case HostDrawerTabId.System: ExecutionTierLabel = ExecutionTierLabel.Contains("AOT", StringComparison.OrdinalIgnoreCase) ? "JIT Bring-Up" : "AOT Primary"; break;
        }
    }

    private static bool MatchClick(int x, int y, string? targetId, string controlId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, controlId, StringComparison.OrdinalIgnoreCase) : !bounds.IsEmpty && bounds.Contains(x, y);
}
