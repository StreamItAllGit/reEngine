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
    private readonly KeybindsTabView _keybindsTab = new();
    private readonly Dictionary<HostDrawerTabId, Rectangle> _tabRects = new();
    private Rectangle _drawerRect;
    private Rectangle _cyclePrimaryRect;
    private Rectangle _closeRect;

    public bool IsOpen { get; set; }
    public HostDrawerTabId ActiveTab { get; set; } = HostDrawerTabId.Video;
    public HostRenderBackendKind SelectedBackend { get; set; } = HostRenderBackendKind.Software;
    public int ResolutionScale { get; set; } = 2;
    public HostInputSchemaKind SelectedSchema { get; set; } = HostInputSchemaKind.SchemaB;
    public string ExecutionTierLabel { get; set; } = "AOT Primary";

    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsOpen) return;

        int drawerW = Math.Min(360, Math.Max(260, canvas.Width / 3));
        _drawerRect = new Rectangle(canvas.Width - drawerW - 10, 10, drawerW, Math.Max(200, canvas.Height - 20));
        HostOverlayLayoutHelpers.DrawPanel(canvas, _drawerRect, HostOverlayPalette.Surface2Glass, HostOverlayPalette.Border);

        canvas.DrawText(_drawerRect.X + 12, _drawerRect.Y + 12, descriptor.EngineDisplayName, HostOverlayPalette.TextPrimary, 1);
        _closeRect = HostOverlayLayoutHelpers.DrawPillButton(canvas, new Rectangle(_drawerRect.Right - 64, _drawerRect.Y + 8, 54, 20), "Close", false, HostOverlayPalette.AccentCyan);

        RenderTabBar(canvas);
        var body = new Rectangle(_drawerRect.X + 12, _drawerRect.Y + 72, _drawerRect.Width - 24, _drawerRect.Height - 84);
        RenderActiveTabBody(canvas, body, descriptor);
    }

    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!IsOpen) return Array.Empty<HostWidgetBounds>();
        var list = new List<HostWidgetBounds>
        {
            new("F1Drawer", "Panel", $"F1 Drawer ({ActiveTab})", _drawerRect, true),
            new("Drawer_CloseButton", "Button", "Close", _closeRect, true)
        };
        foreach ((HostDrawerTabId tab, Rectangle rect) in _tabRects)
        {
            list.Add(new HostWidgetBounds(GetTabControlId(tab), "Button", GetTabLabel(tab), rect, true));
        }
        if (!_cyclePrimaryRect.IsEmpty)
        {
            list.Add(new HostWidgetBounds("Drawer_ActionButton", "Button", "Cycle Active Option", _cyclePrimaryRect, true));
        }
        return list;
    }

    public bool TryHandleClick(int x, int y, string? targetId, HostInputMapper inputMapper)
    {
        ArgumentNullException.ThrowIfNull(inputMapper);
        if (!IsOpen) return false;

        if (string.Equals(targetId, "Drawer_CloseButton", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(targetId) && _closeRect.Contains(x, y)))
        {
            IsOpen = false;
            return true;
        }

        foreach ((HostDrawerTabId tab, Rectangle rect) in _tabRects)
        {
            if (string.Equals(targetId, GetTabControlId(tab), StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrEmpty(targetId) && rect.Contains(x, y)))
            {
                ActiveTab = tab;
                return true;
            }
        }

        if (string.Equals(targetId, "Drawer_ActionButton", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(targetId) && _cyclePrimaryRect.Contains(x, y)))
        {
            CyclePrimarySetting(inputMapper);
            return true;
        }

        return false;
    }

    public static string GetTabControlId(HostDrawerTabId tab) => tab switch
    {
        HostDrawerTabId.Video => "Tab_Video",
        HostDrawerTabId.Controls => "Tab_Controls",
        HostDrawerTabId.Keybinds => "Tab_Keybinds",
        HostDrawerTabId.System => "Tab_System",
        _ => "Tab_DiscMedia"
    };

    private static string GetTabLabel(HostDrawerTabId tab) => tab switch
    {
        HostDrawerTabId.Video => "Video",
        HostDrawerTabId.Controls => "Ctrl",
        HostDrawerTabId.Keybinds => "Keys",
        HostDrawerTabId.System => "Sys",
        _ => "Media"
    };

    private void RenderTabBar(PixelOverlayCanvas canvas)
    {
        HostDrawerTabId[] tabs =
        [
            HostDrawerTabId.Video,
            HostDrawerTabId.Controls,
            HostDrawerTabId.Keybinds,
            HostDrawerTabId.System,
            HostDrawerTabId.DiscMedia
        ];
        int gap = 4;
        int availW = _drawerRect.Width - 24 - (gap * (tabs.Length - 1));
        int tabW = Math.Max(38, availW / tabs.Length);
        int x = _drawerRect.X + 12;
        int y = _drawerRect.Y + 36;

        foreach (HostDrawerTabId tab in tabs)
        {
            var rect = new Rectangle(x, y, tabW, 24);
            _tabRects[tab] = rect;
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, GetTabLabel(tab), ActiveTab == tab, HostOverlayPalette.AccentCyan);
            x += tabW + gap;
        }
    }

    private void RenderActiveTabBody(PixelOverlayCanvas canvas, Rectangle body, HostDrawerDescriptor descriptor)
    {
        _cyclePrimaryRect = new Rectangle(body.X, body.Y + 72, Math.Min(210, body.Width), 26);
        switch (ActiveTab)
        {
            case HostDrawerTabId.Video:
                canvas.DrawText(body.X, body.Y, "VIDEO & SCALING", HostOverlayPalette.AccentCyan, 1);
                canvas.DrawText(body.X, body.Y + 22, $"Backend: {SelectedBackend}", HostOverlayPalette.TextPrimary, 1);
                canvas.DrawText(body.X, body.Y + 40, $"Resolution Scale: {ResolutionScale}x", HostOverlayPalette.TextPrimary, 1);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, _cyclePrimaryRect, "Cycle Scale (1x/2x/4x)", true, HostOverlayPalette.AccentCyan);
                break;

            case HostDrawerTabId.Controls:
                canvas.DrawText(body.X, body.Y, "CONTROLLER SCHEMA", HostOverlayPalette.AccentCyan, 1);
                canvas.DrawText(body.X, body.Y + 22, $"Active Profile: Schema {HostInputSchemas.ToShortCode(SelectedSchema)}", HostOverlayPalette.TextPrimary, 1);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, _cyclePrimaryRect, "Cycle Schema (A/B/C)", true, HostOverlayPalette.AccentCyan);
                break;

            case HostDrawerTabId.Keybinds:
                _cyclePrimaryRect = Rectangle.Empty;
                _keybindsTab.Render(canvas, body, SelectedSchema);
                break;

            case HostDrawerTabId.System:
                canvas.DrawText(body.X, body.Y, "SYSTEM & EXECUTION TIER", HostOverlayPalette.AccentCyan, 1);
                canvas.DrawText(body.X, body.Y + 22, $"Tier Mode: {ExecutionTierLabel}", HostOverlayPalette.TextPrimary, 1);
                canvas.DrawText(body.X, body.Y + 40, $"Module: {descriptor.ModuleTitle}", HostOverlayPalette.TextMuted, 1);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, _cyclePrimaryRect, "Cycle Tier Mode", false, HostOverlayPalette.AccentCyan);
                break;

            default:
                _cyclePrimaryRect = Rectangle.Empty;
                canvas.DrawText(body.X, body.Y, "DISC / MEDIA", HostOverlayPalette.AccentCyan, 1);
                canvas.DrawText(body.X, body.Y + 22, $"Media: {descriptor.MediaLabel}", HostOverlayPalette.TextPrimary, 1);
                canvas.DrawText(body.X, body.Y + 40, "Storage: gitignored data\\rom\\", HostOverlayPalette.TextMuted, 1);
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
                ExecutionTierLabel = ExecutionTierLabel.Contains("AOT", StringComparison.OrdinalIgnoreCase)
                    ? "JIT Bring-Up"
                    : "AOT Primary";
                break;
        }
    }
}
