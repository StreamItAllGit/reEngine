// @module: reEngine.Host.Ui.KeybindsTabView
// @purpose: Interactive 5-column Keybinds matrix (Action | 1st | 2nd | 3rd | Gamepad) rendered on PixelOverlayCanvas with 3-tier switching, conflict prevention, capture, and scroll.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: KeybindsTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders the 5-column multi-bind matrix (<c>Action | 1st | 2nd | 3rd | Gamepad</c>) and manages interactive rebind capture and conflict detection.
/// </summary>
public sealed class KeybindsTabView
{
    private sealed record SlotHitTarget(string ControlId, Rectangle Bounds, string ActionId, int ColumnIndex);
    private sealed record TierHitTarget(string ControlId, Rectangle Bounds, HostControlTierMode Tier);

    private static readonly (HostControlTierMode Tier, string Id, string Label)[] Tiers =
    [
        (HostControlTierMode.GlobalController, "Keybinds_Tier_globalController", "Global 1:1"),
        (HostControlTierMode.PerGameController, "Keybinds_Tier_perGameController", "Game 1:1"),
        (HostControlTierMode.PerGameModern, "Keybinds_Tier_perGameModern", "Modern PC")
    ];

    private readonly List<SlotHitTarget> _slotTargets = new();
    private readonly List<TierHitTarget> _tierTargets = new();
    private readonly List<HostWidgetBounds> _widgets = new();
    private Rectangle _scrollUpRect, _scrollDownRect, _resetRect;
    private int _lastVisibleRows = 6;

    /// <summary>Gets the action identifier currently awaiting input capture, or <see langword="null"/>.</summary>
    public string? CapturingActionId { get; private set; }

    /// <summary>Gets the slot column index (<c>0..2</c> KB/M, <c>3</c> Gamepad) currently awaiting capture.</summary>
    public int CapturingColumnIndex { get; private set; }

    /// <summary>Gets the human-readable name of the action currently awaiting capture.</summary>
    public string? CapturingDisplayName { get; private set; }

    /// <summary>Gets the current vertical row scroll offset.</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>Gets whether a binding cell is currently listening for keyboard/mouse/gamepad input.</summary>
    public bool IsCapturing => !string.IsNullOrWhiteSpace(CapturingActionId);

    /// <summary>Gets the latest slot collision warning message if a conflicting rebind was blocked.</summary>
    public string? LastConflictMessage { get; private set; }

    /// <summary>Cancels active input capture and clears any pending conflict banner.</summary>
    public void ClearCapture() { CapturingActionId = null; CapturingColumnIndex = 0; CapturingDisplayName = null; LastConflictMessage = null; }

    /// <summary>Scrolls the keybind table by <paramref name="deltaRows"/> clamped to <paramref name="totalCount"/>.</summary>
    public bool ScrollBy(int deltaRows, int totalCount)
    {
        int maxOffset = Math.Max(0, totalCount - Math.Max(1, _lastVisibleRows));
        int next = Math.Clamp(ScrollOffset + deltaRows, 0, maxOffset);
        if (next == ScrollOffset) return false;
        ScrollOffset = next;
        return true;
    }

    /// <summary>Renders the 3-tier Standard 33 selector bar and 5-column multi-bind grid using <paramref name="tierStore"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema, HostControlTierStore tierStore)
    {
        ArgumentNullException.ThrowIfNull(tierStore);
        RenderCore(canvas, bodyRect, schema, tierStore.ActiveRegistry, tierStore);
    }

    /// <summary>Renders the 5-column multi-bind grid directly against <paramref name="registry"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema, HostMultiBindRegistry registry) =>
        RenderCore(canvas, bodyRect, schema, registry, tierStore: null);

    /// <summary>Returns interactive widget bounds produced by the most recent render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Handles pointer clicks including 3-tier switching against <paramref name="tierStore"/>.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostControlTierStore tierStore, HostInputSchemaKind schema, bool isRightClick = false)
    {
        ArgumentNullException.ThrowIfNull(tierStore);
        foreach (TierHitTarget tierTarget in _tierTargets)
        {
            if (!MatchTarget(x, y, targetId, tierTarget.ControlId, tierTarget.Bounds)) continue;
            tierStore.ActiveTier = tierTarget.Tier;
            ClearCapture();
            return true;
        }
        return TryHandleClick(x, y, targetId, tierStore.ActiveRegistry, schema, isRightClick);
    }

    /// <summary>Handles pointer clicks against <paramref name="registry"/>.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostMultiBindRegistry registry, HostInputSchemaKind schema, bool isRightClick = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (MatchTarget(x, y, targetId, "Keybinds_ScrollUp", _scrollUpRect)) { _ = ScrollBy(-1, registry.Slots.Count); return true; }
        if (MatchTarget(x, y, targetId, "Keybinds_ScrollDown", _scrollDownRect)) { _ = ScrollBy(1, registry.Slots.Count); return true; }
        if (MatchTarget(x, y, targetId, "Keybinds_ResetDefaults", _resetRect)) { registry.ResetToSchemaDefaults(schema); ClearCapture(); return true; }

        foreach (SlotHitTarget target in _slotTargets)
        {
            if (!MatchTarget(x, y, targetId, target.ControlId, target.Bounds)) continue;
            if (isRightClick) { _ = registry.ClearSlotBinding(target.ActionId, target.ColumnIndex); ClearCapture(); return true; }
            LastConflictMessage = null;
            CapturingActionId = target.ActionId;
            CapturingColumnIndex = target.ColumnIndex;
            CapturingDisplayName = registry.TryGetSlot(target.ActionId, out HostMultiBindSlot slot) ? slot.DisplayName : target.ActionId;
            return true;
        }
        return false;
    }

    /// <summary>Captures an incoming input code into the active tier of <paramref name="tierStore"/> while blocking collisions.</summary>
    public bool TryCaptureInputCode(int inputCode, HostControlTierStore tierStore)
    {
        ArgumentNullException.ThrowIfNull(tierStore);
        return TryCaptureInputCode(inputCode, tierStore.ActiveRegistry);
    }

    /// <summary>Captures an incoming input code into <paramref name="registry"/> while preventing silent slot overrides.</summary>
    public bool TryCaptureInputCode(int inputCode, HostMultiBindRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!IsCapturing || string.IsNullOrWhiteSpace(CapturingActionId)) return false;
        if (inputCode == HostScancodes.F1) return false;
        if (inputCode == HostScancodes.Escape) { ClearCapture(); return true; }
        if (inputCode is HostScancodes.Delete or HostScancodes.Backspace) { _ = registry.ClearSlotBinding(CapturingActionId, CapturingColumnIndex); ClearCapture(); return true; }

        bool validForColumn = CapturingColumnIndex == 3 ? HostMultiBindRegistry.IsGamepadCode(inputCode) : HostMultiBindRegistry.IsValidBindingCode(inputCode);
        if (!validForColumn) return true;

        if (!registry.TrySetSlotBindingUnique(CapturingActionId, CapturingColumnIndex, inputCode, out HostSlotConflict? conflict))
        {
            LastConflictMessage = conflict?.Reason ?? $"Conflict: {HostMultiBindRegistry.FormatBindingLabel(inputCode)} already bound.";
            return true;
        }
        ClearCapture();
        return true;
    }

    private void RenderCore(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema, HostMultiBindRegistry registry, HostControlTierStore? tierStore)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(registry);
        _slotTargets.Clear(); _tierTargets.Clear(); _widgets.Clear();

        int tierBarOffset = 0;
        if (tierStore is not null)
        {
            tierBarOffset = 24;
            int tierGap = 4;
            int tierW = Math.Max(60, (bodyRect.Width - (tierGap * 2)) / 3);
            for (int i = 0; i < Tiers.Length; i++)
            {
                (HostControlTierMode tier, string id, string label) = Tiers[i];
                var tRect = new Rectangle(bodyRect.X + (i * (tierW + tierGap)), bodyRect.Y, tierW, 20);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, tRect, label, tierStore.ActiveTier == tier, HostOverlayPalette.AccentCyan);
                _tierTargets.Add(new(id, tRect, tier));
                _widgets.Add(new(id, "Button", label, tRect, true));
            }
        }

        IReadOnlyList<HostMultiBindSlot> slots = registry.Slots;
        const int rowH = 24, rowGap = 4, colGap = 4, footerH = 30, scrollAreaW = 52;
        int topY = bodyRect.Y + tierBarOffset, headerH = 48;
        int visibleRows = Math.Max(1, (bodyRect.Height - tierBarOffset - headerH - footerH) / (rowH + rowGap));
        _lastVisibleRows = visibleRows;
        int maxOffset = Math.Max(0, slots.Count - visibleRows);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);

        int gridW = bodyRect.Width - scrollAreaW - 4;
        int slotColW = Math.Clamp((int)(gridW * 0.165), 44, 108);
        int actW = Math.Max(80, bodyRect.Width - (4 * (slotColW + colGap)) - scrollAreaW);
        int slotsStartX = bodyRect.X + actW + colGap;
        int endRow = Math.Min(slots.Count, ScrollOffset + visibleRows);
        canvas.DrawText(bodyRect.X, topY + 4, HostOverlayLayoutHelpers.FitText($"Action ({(slots.Count == 0 ? 0 : ScrollOffset + 1)}-{endRow}/{slots.Count})", actW, 1), HostOverlayPalette.AccentCyan, 1);

        string[] colHeaders = ["1st", "2nd", "3rd", "Gamepad"];
        for (int c = 0; c < 4; c++) canvas.DrawText(slotsStartX + (c * (slotColW + colGap)) + 4, topY + 4, HostOverlayLayoutHelpers.FitText(colHeaders[c], slotColW - 4, 1), HostOverlayPalette.TextMuted, 1);

        _scrollUpRect = new(bodyRect.Right - 50, topY, 23, 18); _scrollDownRect = new(bodyRect.Right - 24, topY, 24, 18);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollUpRect, "^", ScrollOffset > 0, HostOverlayPalette.AccentCyan);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollDownRect, "v", ScrollOffset < maxOffset, HostOverlayPalette.AccentCyan);
        _widgets.Add(new("Keybinds_ScrollUp", "Button", "Scroll Up", _scrollUpRect, true));
        _widgets.Add(new("Keybinds_ScrollDown", "Button", "Scroll Down", _scrollDownRect, true));

        bool warnBanner = IsCapturing || !string.IsNullOrWhiteSpace(LastConflictMessage);
        string statusText = !string.IsNullOrWhiteSpace(LastConflictMessage) ? LastConflictMessage!
            : IsCapturing ? $"Bind {CapturingDisplayName} [{colHeaders[Math.Clamp(CapturingColumnIndex, 0, 3)]}]: Input (Esc cancel, Del clear)"
            : $"{(tierStore is not null ? HostControlTierStore.ToDisplayLabel(tierStore.ActiveTier) + " | " : string.Empty)}Schema {HostInputSchemas.ToShortCode(schema)} | Click slot to rebind";
        HostOverlayLayoutHelpers.DrawBanner(canvas, new(bodyRect.X, topY + 21, bodyRect.Width, 20), statusText,
            warnBanner ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass,
            warnBanner ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border,
            warnBanner ? HostOverlayPalette.AccentWarning : HostOverlayPalette.TextMuted);

        int y = topY + headerH;
        for (int r = 0; r < visibleRows && (ScrollOffset + r) < slots.Count; r++)
        {
            HostMultiBindSlot slot = slots[ScrollOffset + r];
            bool rowActive = string.Equals(CapturingActionId, slot.ActionId, StringComparison.OrdinalIgnoreCase);
            var labelRect = new Rectangle(bodyRect.X, y, actW, rowH);
            HostOverlayLayoutHelpers.DrawPanel(canvas, labelRect, rowActive ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass, rowActive ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border);
            canvas.DrawText(labelRect.X + 6, labelRect.Y + 8, HostOverlayLayoutHelpers.FitText(slot.DisplayName, actW - 10, 1), HostOverlayPalette.TextPrimary, 1);

            int[] codes = [slot.Slot1, slot.Slot2, slot.Slot3, slot.GamepadCode];
            for (int c = 0; c < 4; c++)
            {
                bool cellActive = rowActive && CapturingColumnIndex == c;
                var pillRect = new Rectangle(slotsStartX + (c * (slotColW + colGap)), y, slotColW, rowH);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, pillRect, cellActive ? "..." : HostMultiBindRegistry.FormatBindingLabel(codes[c]), cellActive, HostOverlayPalette.AccentWarning);
                string id = $"Keybinds_Slot_{slot.ActionId}_{c}";
                _slotTargets.Add(new(id, pillRect, slot.ActionId, c));
                _widgets.Add(new(id, "Button", $"{slot.DisplayName} {colHeaders[c]}", pillRect, true));
            }
            y += rowH + rowGap;
        }

        _resetRect = new(bodyRect.X, bodyRect.Bottom - 26, Math.Min(210, bodyRect.Width), 24);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _resetRect, "Reset Defaults", false, HostOverlayPalette.AccentCyan);
        _widgets.Add(new("Keybinds_ResetDefaults", "Button", "Reset Defaults", _resetRect, true));
    }

    private static bool MatchTarget(int x, int y, string? targetId, string expectedId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, expectedId, StringComparison.OrdinalIgnoreCase) : bounds.Contains(x, y);
}
