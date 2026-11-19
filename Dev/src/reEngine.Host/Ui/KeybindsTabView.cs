// @module: reEngine.Host.Ui.KeybindsTabView
// @purpose: Interactive 5-column Keybinds matrix (Action | 1st | 2nd | 3rd | Gamepad) rendered on PixelOverlayCanvas with capture and scroll.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: KeybindsTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders the 5-column multi-bind matrix (<c>Action | 1st | 2nd | 3rd | Gamepad</c>) and manages interactive rebind capture.
/// </summary>
public sealed class KeybindsTabView
{
    private sealed record SlotHitTarget(string ControlId, Rectangle Bounds, string ActionId, int ColumnIndex);

    private readonly List<SlotHitTarget> _slotTargets = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private Rectangle _scrollUpRect;
    private Rectangle _scrollDownRect;
    private Rectangle _resetRect;
    private int _lastVisibleRows = 6;

    public string? CapturingActionId { get; private set; }
    public int CapturingColumnIndex { get; private set; }
    public string? CapturingDisplayName { get; private set; }
    public int ScrollOffset { get; private set; }
    public bool IsCapturing => !string.IsNullOrWhiteSpace(CapturingActionId);

    public void ClearCapture()
    {
        CapturingActionId = null;
        CapturingColumnIndex = 0;
        CapturingDisplayName = null;
    }

    public bool ScrollBy(int deltaRows, int totalCount)
    {
        int maxOffset = Math.Max(0, totalCount - Math.Max(1, _lastVisibleRows));
        int next = Math.Clamp(ScrollOffset + deltaRows, 0, maxOffset);
        if (next == ScrollOffset) return false;
        ScrollOffset = next;
        return true;
    }

    public void Render(
        PixelOverlayCanvas canvas,
        Rectangle bodyRect,
        HostInputSchemaKind schema,
        HostMultiBindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(registry);

        _slotTargets.Clear();
        _widgets.Clear();

        IReadOnlyList<HostMultiBindSlot> slots = registry.Slots;
        const int rowH = 24;
        const int rowGap = 4;
        int headerH = 48;
        int footerH = 30;
        int visibleRows = Math.Max(1, (bodyRect.Height - headerH - footerH) / (rowH + rowGap));
        _lastVisibleRows = visibleRows;
        int maxOffset = Math.Max(0, slots.Count - visibleRows);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);

        int scrollAreaW = 52;
        int gridW = bodyRect.Width - scrollAreaW - 4;
        int slotColW = Math.Clamp((int)(gridW * 0.165), 44, 108);
        const int colGap = 4;
        int actW = Math.Max(80, bodyRect.Width - (4 * (slotColW + colGap)) - scrollAreaW);
        int slotsStartX = bodyRect.X + actW + colGap;

        int endRow = Math.Min(slots.Count, ScrollOffset + visibleRows);
        string title = $"Action ({(slots.Count == 0 ? 0 : ScrollOffset + 1)}-{endRow}/{slots.Count})";
        canvas.DrawText(bodyRect.X, bodyRect.Y + 4, HostOverlayLayoutHelpers.FitText(title, actW, 1), HostOverlayPalette.AccentCyan, 1);

        string[] colHeaders = ["1st", "2nd", "3rd", "Gamepad"];
        for (int c = 0; c < 4; c++)
        {
            int cx = slotsStartX + (c * (slotColW + colGap));
            canvas.DrawText(cx + 4, bodyRect.Y + 4, HostOverlayLayoutHelpers.FitText(colHeaders[c], slotColW - 4, 1), HostOverlayPalette.TextMuted, 1);
        }

        _scrollUpRect = new Rectangle(bodyRect.Right - 50, bodyRect.Y, 23, 18);
        _scrollDownRect = new Rectangle(bodyRect.Right - 24, bodyRect.Y, 24, 18);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollUpRect, "^", ScrollOffset > 0, HostOverlayPalette.AccentCyan);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollDownRect, "v", ScrollOffset < maxOffset, HostOverlayPalette.AccentCyan);
        _widgets.Add(new HostWidgetBounds("Keybinds_ScrollUp", "Button", "Scroll Up", _scrollUpRect, true));
        _widgets.Add(new HostWidgetBounds("Keybinds_ScrollDown", "Button", "Scroll Down", _scrollDownRect, true));

        var statusRect = new Rectangle(bodyRect.X, bodyRect.Y + 21, bodyRect.Width, 20);
        string statusText = IsCapturing
            ? $"Bind {CapturingDisplayName} [{colHeaders[Math.Clamp(CapturingColumnIndex, 0, 3)]}]: Input (Esc cancel, Del clear)"
            : $"Schema {HostInputSchemas.ToShortCode(schema)} | Click slot to rebind (Del/RClick clears)";
        HostOverlayLayoutHelpers.DrawBanner(
            canvas,
            statusRect,
            statusText,
            IsCapturing ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass,
            IsCapturing ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border,
            IsCapturing ? HostOverlayPalette.AccentWarning : HostOverlayPalette.TextMuted);

        int y = bodyRect.Y + headerH;
        for (int r = 0; r < visibleRows && (ScrollOffset + r) < slots.Count; r++)
        {
            HostMultiBindSlot slot = slots[ScrollOffset + r];
            bool rowActive = string.Equals(CapturingActionId, slot.ActionId, StringComparison.OrdinalIgnoreCase);
            var labelRect = new Rectangle(bodyRect.X, y, actW, rowH);
            HostOverlayLayoutHelpers.DrawPanel(
                canvas,
                labelRect,
                rowActive ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass,
                rowActive ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border);
            canvas.DrawText(labelRect.X + 6, labelRect.Y + 8, HostOverlayLayoutHelpers.FitText(slot.DisplayName, actW - 10, 1), HostOverlayPalette.TextPrimary, 1);

            int[] codes = [slot.Slot1, slot.Slot2, slot.Slot3, slot.GamepadCode];
            for (int c = 0; c < 4; c++)
            {
                bool cellActive = rowActive && CapturingColumnIndex == c;
                var pillRect = new Rectangle(slotsStartX + (c * (slotColW + colGap)), y, slotColW, rowH);
                string cellLabel = cellActive ? "..." : HostMultiBindRegistry.FormatBindingLabel(codes[c]);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, pillRect, cellLabel, cellActive, HostOverlayPalette.AccentWarning);

                string id = $"Keybinds_Slot_{slot.ActionId}_{c}";
                _slotTargets.Add(new SlotHitTarget(id, pillRect, slot.ActionId, c));
                _widgets.Add(new HostWidgetBounds(id, "Button", $"{slot.DisplayName} {colHeaders[c]}", pillRect, true));
            }
            y += rowH + rowGap;
        }

        _resetRect = new Rectangle(bodyRect.X, bodyRect.Bottom - 26, Math.Min(210, bodyRect.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _resetRect, "Reset Defaults", false, HostOverlayPalette.AccentCyan);
        _widgets.Add(new HostWidgetBounds("Keybinds_ResetDefaults", "Button", "Reset Defaults", _resetRect, true));
    }

    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    public bool TryHandleClick(
        int x,
        int y,
        string? targetId,
        HostMultiBindRegistry registry,
        HostInputSchemaKind schema,
        bool isRightClick = false)
    {
        ArgumentNullException.ThrowIfNull(registry);

        if (MatchTarget(x, y, targetId, "Keybinds_ScrollUp", _scrollUpRect))
        {
            _ = ScrollBy(-1, registry.Slots.Count);
            return true;
        }
        if (MatchTarget(x, y, targetId, "Keybinds_ScrollDown", _scrollDownRect))
        {
            _ = ScrollBy(1, registry.Slots.Count);
            return true;
        }
        if (MatchTarget(x, y, targetId, "Keybinds_ResetDefaults", _resetRect))
        {
            registry.ResetToSchemaDefaults(schema);
            ClearCapture();
            return true;
        }

        foreach (SlotHitTarget target in _slotTargets)
        {
            if (!MatchTarget(x, y, targetId, target.ControlId, target.Bounds)) continue;
            if (isRightClick)
            {
                _ = registry.ClearSlotBinding(target.ActionId, target.ColumnIndex);
                ClearCapture();
                return true;
            }

            CapturingActionId = target.ActionId;
            CapturingColumnIndex = target.ColumnIndex;
            CapturingDisplayName = registry.TryGetSlot(target.ActionId, out HostMultiBindSlot slot)
                ? slot.DisplayName
                : target.ActionId;
            return true;
        }

        return false;
    }

    public bool TryCaptureInputCode(int inputCode, HostMultiBindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!IsCapturing || string.IsNullOrWhiteSpace(CapturingActionId)) return false;
        if (inputCode == HostScancodes.F1) return false;

        if (inputCode == HostScancodes.Escape)
        {
            ClearCapture();
            return true;
        }

        if (inputCode is HostScancodes.Delete or HostScancodes.Backspace)
        {
            _ = registry.ClearSlotBinding(CapturingActionId, CapturingColumnIndex);
            ClearCapture();
            return true;
        }

        bool validForColumn = CapturingColumnIndex == 3
            ? HostMultiBindRegistry.IsGamepadCode(inputCode)
            : HostMultiBindRegistry.IsValidBindingCode(inputCode);
        if (!validForColumn) return true;

        _ = registry.SetSlotBinding(CapturingActionId, CapturingColumnIndex, inputCode);
        ClearCapture();
        return true;
    }

    private static bool MatchTarget(int x, int y, string? targetId, string expectedId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId)
            ? string.Equals(targetId, expectedId, StringComparison.OrdinalIgnoreCase)
            : bounds.Contains(x, y);
}
