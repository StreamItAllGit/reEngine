// @module: reEngine.Host.Ui.HostDrawerScrollLayout
// @purpose: Shared scrollable row layout, header scroll buttons, focus highlighting, and widget registration for F1 drawer tabs.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts
// @exports: DrawerControl, HostDrawerScrollLayout

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

internal sealed record DrawerControl(string ControlId, string ControlType, string Label, Rectangle Bounds, Action<bool> OnClick);

internal sealed class HostDrawerScrollLayout
{
    public sealed record RowItem(int Height, Action<PixelOverlayCanvas, int, int, int> Draw);

    private readonly List<RowItem> _rows = new();

    public HostDrawerScrollLayout(Rectangle body, double uiTextScale, string? focusedControlId)
    {
        Body = body;
        Scale = HostOverlayTextRenderer.ClampUiTextScale(uiTextScale);
        FocusedControlId = focusedControlId;
        RowH = HostOverlayTextRenderer.GetRowHeight(Scale);
        LineH = HostOverlayTextRenderer.GetLineHeight(Scale);
    }

    public Rectangle Body { get; }
    public double Scale { get; }
    public string? FocusedControlId { get; }
    public int RowH { get; }
    public int LineH { get; }
    public int Gap => 4;

    public bool IsFocused(string id) =>
        !string.IsNullOrEmpty(FocusedControlId) && string.Equals(FocusedControlId, id, StringComparison.OrdinalIgnoreCase);

    public void AddText(string text, RgbaColor color, int extraPad = 2)
    {
        int h = LineH + extraPad;
        _rows.Add(new(h, (canvas, x, y, w) =>
            HostOverlayTextRenderer.DrawText(canvas, x, y + 1, HostOverlayTextRenderer.FitText(text, w, Scale), color, Scale)));
    }

    public void AddPill(
        string id, string label, bool isActive, RgbaColor accent, Action<bool> onClick,
        List<DrawerControl> controls, Action<Rectangle>? onBounds = null)
    {
        int h = RowH + Gap;
        _rows.Add(new(h, (canvas, x, y, w) =>
        {
            var rect = new Rectangle(x, y, w, RowH);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, isActive, accent, Scale, IsFocused(id));
            controls.Add(new(id, "Button", label, rect, onClick));
            onBounds?.Invoke(rect);
        }));
    }

    public void AddToggle(
        string id, string label, bool isOn, RgbaColor accent, Action<bool> onClick,
        List<DrawerControl> controls)
    {
        int h = RowH + Gap;
        _rows.Add(new(h, (canvas, x, y, w) =>
        {
            var rect = new Rectangle(x, y, w, RowH);
            HostOverlayLayoutHelpers.DrawToggleRow(canvas, rect, label, isOn, accent, Scale, IsFocused(id));
            controls.Add(new(id, "CheckBox", label, rect, onClick));
        }));
    }

    public void AddStepper(
        string rowId, string decId, string incId, string label, string valueText, RgbaColor accent,
        Action<bool> onRowClick, Action<bool> onDec, Action<bool> onInc, List<DrawerControl> controls)
    {
        int h = RowH + Gap;
        _rows.Add(new(h, (canvas, x, y, w) =>
        {
            var rect = new Rectangle(x, y, w, RowH);
            (Rectangle minusRect, Rectangle plusRect) = HostOverlayLayoutHelpers.DrawStepperRow(
                canvas, rect, label, valueText, accent, Scale, IsFocused(rowId), IsFocused(decId), IsFocused(incId));
            int labelW = Math.Max(20, minusRect.X - rect.X - 4);
            controls.Add(new(decId, "Button", $"{label} -", minusRect, onDec));
            controls.Add(new(incId, "Button", $"{label} +", plusRect, onInc));
            controls.Add(new(rowId, "Button", $"{label}: {valueText}", new Rectangle(rect.X, rect.Y, labelW, rect.Height), onRowClick));
        }));
    }

    public void AddColumns(List<DrawerControl> controls, params (string Id, string Label, bool Active, bool IsToggle, RgbaColor Accent, Action<bool> OnClick, Action<Rectangle>? OnBounds)[] items)
    {
        if (items.Length == 0) return;
        int h = RowH + Gap;
        _rows.Add(new(h, (canvas, x, y, w) =>
        {
            int count = items.Length;
            int colW = Math.Max(34, (w - (Gap * (count - 1))) / count);
            for (int i = 0; i < count; i++)
            {
                int ix = x + (i * (colW + Gap));
                int iw = i == count - 1 ? Math.Max(34, x + w - ix) : colW;
                var item = items[i];
                var rect = new Rectangle(ix, y, iw, RowH);
                if (item.IsToggle)
                {
                    HostOverlayLayoutHelpers.DrawToggleRow(canvas, rect, item.Label, item.Active, item.Accent, Scale, IsFocused(item.Id));
                    controls.Add(new(item.Id, "CheckBox", item.Label, rect, item.OnClick));
                }
                else
                {
                    HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, item.Label, item.Active, item.Accent, Scale, IsFocused(item.Id));
                    controls.Add(new(item.Id, "Button", item.Label, rect, item.OnClick));
                }
                item.OnBounds?.Invoke(rect);
            }
        }));
    }

    public void AddBanner(string id, string text, RgbaColor fill, RgbaColor border, RgbaColor textColor, List<DrawerControl> controls)
    {
        int h = RowH + Gap;
        _rows.Add(new(h, (canvas, x, y, w) =>
        {
            var rect = new Rectangle(x, y, w, RowH);
            HostOverlayLayoutHelpers.DrawBanner(canvas, rect, text, fill, border, textColor, Scale);
            controls.Add(new(id, "Label", text, rect, static _ => { }));
        }));
    }

    public int Render(
        PixelOverlayCanvas canvas,
        string headerTitle,
        string scrollPrefix,
        ref int scrollOffset,
        out int maxScroll,
        List<DrawerControl> controls,
        Action<int>? onScrollAction = null)
    {
        int headerH = Math.Max(LineH + 4, 22);
        int scrollBtnW = Math.Clamp((int)Math.Round(24 * Scale), 22, 38);
        int scrollBtnH = Math.Max(18, headerH - 2);
        int availH = Math.Max(20, Body.Height - headerH - 4);
        maxScroll = ComputeMaxScroll(availH);
        scrollOffset = Math.Clamp(scrollOffset, 0, maxScroll);

        int titleMaxW = Math.Max(40, Body.Width - (scrollBtnW * 2) - 10);
        HostOverlayTextRenderer.DrawText(canvas, Body.X, Body.Y + 2, HostOverlayTextRenderer.FitText(headerTitle, titleMaxW, Scale), HostOverlayPalette.AccentCyan, Scale);

        string upId = $"{scrollPrefix}_ScrollUp";
        string dnId = $"{scrollPrefix}_ScrollDown";
        var upRect = new Rectangle(Body.Right - (scrollBtnW * 2) - 4, Body.Y, scrollBtnW, scrollBtnH);
        var dnRect = new Rectangle(Body.Right - scrollBtnW, Body.Y, scrollBtnW, scrollBtnH);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, upRect, "^", scrollOffset > 0, HostOverlayPalette.AccentCyan, Scale, IsFocused(upId));
        HostOverlayLayoutHelpers.DrawPillButton(canvas, dnRect, "v", scrollOffset < maxScroll, HostOverlayPalette.AccentCyan, Scale, IsFocused(dnId));
        controls.Add(new(upId, "Button", "Scroll Up", upRect, _ => onScrollAction?.Invoke(-1)));
        controls.Add(new(dnId, "Button", "Scroll Down", dnRect, _ => onScrollAction?.Invoke(1)));

        int y = Body.Y + headerH + 4;
        for (int i = scrollOffset; i < _rows.Count; i++)
        {
            RowItem row = _rows[i];
            if (y + row.Height - Gap > Body.Bottom) break;
            row.Draw(canvas, Body.X, y, Body.Width);
            y += row.Height;
        }
        return y;
    }

    public static void AppendExtraRows(
        HostDrawerScrollLayout layout,
        HostDrawerTabId tab,
        IReadOnlyList<HostDrawerRowDescriptor>? rows,
        F1SettingsDrawerView view,
        List<DrawerControl> controls)
    {
        if (rows is null) return;
        foreach (HostDrawerRowDescriptor row in rows)
        {
            if (row.Tab != tab) continue;
            string text = string.IsNullOrWhiteSpace(row.Value) ? row.Label : $"{row.Label}: {row.Value}";
            if (row.IsInteractive)
            {
                HostDrawerRowDescriptor captured = row;
                layout.AddPill($"ExtraRow_{row.RowId}", text, false, HostOverlayPalette.AccentCyan,
                    isRight => view.EventCallbacks?.OnExtraRowActivated(captured.RowId, isRight), controls);
            }
            else
            {
                layout.AddText(text, HostOverlayPalette.TextMuted);
            }
        }
    }

    private int ComputeMaxScroll(int availH)
    {
        if (_rows.Count == 0) return 0;
        int acc = 0;
        int firstFitFromEnd = _rows.Count - 1;
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            int nextAcc = acc + _rows[i].Height;
            if (nextAcc - Gap > availH && i < _rows.Count - 1) break;
            acc = nextAcc;
            firstFitFromEnd = i;
        }
        return Math.Max(0, firstFitFromEnd);
    }
}
