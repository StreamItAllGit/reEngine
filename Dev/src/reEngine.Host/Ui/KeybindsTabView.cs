// @module: reEngine.Host.Ui.KeybindsTabView
// @purpose: Interactive 5-column Keybinds matrix displaying game + hostActionBindings with 3-tier switching, cross-conflict blocking, capture, and scroll.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input
// @exports: KeybindsTabView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;

namespace reEngine.Host.Ui;

/// <summary>
/// Renders the unified 5-column multi-bind matrix (<c>Action | 1st | 2nd | 3rd | Gamepad</c>) for game and host actions.
/// </summary>
public sealed class KeybindsTabView
{
    private sealed record SlotHitTarget(string ControlId, Rectangle Bounds, string ActionId, int ColumnIndex, bool IsHostAction);
    private sealed record TierHitTarget(string ControlId, Rectangle Bounds, HostControlTierMode Tier);
    private sealed record MatrixRow(bool IsSeparator, string Title, HostMultiBindSlot? Slot, bool IsHostAction);

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
    private bool _capturingIsHostAction;
    private int _lastVisibleRows = 6;

    /// <summary>Gets the action identifier currently awaiting input capture, or <see langword="null"/>.</summary>
    public string? CapturingActionId { get; private set; }

    /// <summary>Gets the slot column index (<c>0..2</c> KB/M, <c>3</c> Gamepad) currently awaiting capture.</summary>
    public int CapturingColumnIndex { get; private set; }

    /// <summary>Gets the human-readable name of the action currently awaiting capture.</summary>
    public string? CapturingDisplayName { get; private set; }

    /// <summary>Gets the current vertical row scroll offset.</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>Gets the total row count in the most recently rendered matrix.</summary>
    public int TotalRowCount { get; private set; }

    /// <summary>Gets the maximum valid scroll offset from the most recent render pass.</summary>
    public int MaxScrollOffset => Math.Max(0, TotalRowCount - Math.Max(1, _lastVisibleRows));

    /// <summary>Gets whether a binding cell is currently listening for keyboard/mouse/gamepad input.</summary>
    public bool IsCapturing => !string.IsNullOrWhiteSpace(CapturingActionId);

    /// <summary>Gets the latest slot collision warning message if a conflicting rebind was blocked.</summary>
    public string? LastConflictMessage { get; private set; }

    /// <summary>Gets or sets live session event callbacks notified when host action bindings change.</summary>
    public IHostSessionEventCallbacks? EventCallbacks { get; set; }

    /// <summary>Cancels active input capture and clears any pending conflict banner.</summary>
    public void ClearCapture()
    {
        CapturingActionId = null;
        CapturingColumnIndex = 0;
        CapturingDisplayName = null;
        _capturingIsHostAction = false;
        LastConflictMessage = null;
    }

    /// <summary>Scrolls the keybind table by <paramref name="deltaRows"/> clamped to <paramref name="totalCount"/>.</summary>
    public bool ScrollBy(int deltaRows, int totalCount)
    {
        int maxOffset = Math.Max(0, totalCount - Math.Max(1, _lastVisibleRows));
        int next = Math.Clamp(ScrollOffset + deltaRows, 0, maxOffset);
        if (next == ScrollOffset) return false;
        ScrollOffset = next;
        return true;
    }

    /// <summary>Renders the 3-tier selector bar and unified game + host 5-column multi-bind grid using <paramref name="tierStore"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema, HostControlTierStore tierStore) =>
        RenderCore(canvas, bodyRect, schema, tierStore.ActiveRegistry, tierStore, 1.0, null);

    /// <summary>Renders the 5-column multi-bind grid directly against <paramref name="registry"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema, HostMultiBindRegistry registry) =>
        RenderCore(canvas, bodyRect, schema, registry, tierStore: null, 1.0, null);

    internal void Render(
        PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema,
        HostControlTierStore? tierStore, HostMultiBindRegistry registry, double uiTextScale, string? focusedControlId) =>
        RenderCore(canvas, bodyRect, schema, registry, tierStore, uiTextScale, focusedControlId);

    /// <summary>Returns interactive widget bounds produced by the most recent render pass.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets() => _widgets;

    /// <summary>Handles pointer clicks including 3-tier switching and host action bindings against <paramref name="tierStore"/>.</summary>
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
        return TryHandleClickCore(x, y, targetId, tierStore.ActiveRegistry, tierStore.HostActions, schema, isRightClick);
    }

    /// <summary>Handles pointer clicks against <paramref name="registry"/>.</summary>
    public bool TryHandleClick(int x, int y, string? targetId, HostMultiBindRegistry registry, HostInputSchemaKind schema, bool isRightClick = false) =>
        TryHandleClickCore(x, y, targetId, registry, hostActions: null, schema, isRightClick);

    /// <summary>Captures an incoming input code into the active game or host slot of <paramref name="tierStore"/>.</summary>
    public bool TryCaptureInputCode(int inputCode, HostControlTierStore tierStore)
    {
        ArgumentNullException.ThrowIfNull(tierStore);
        return TryCaptureCore(inputCode, tierStore.ActiveRegistry, tierStore.HostActions);
    }

    /// <summary>Captures an incoming input code into <paramref name="registry"/> while preventing silent slot overrides.</summary>
    public bool TryCaptureInputCode(int inputCode, HostMultiBindRegistry registry) =>
        TryCaptureCore(inputCode, registry, hostActions: null);

    private bool TryHandleClickCore(
        int x, int y, string? targetId, HostMultiBindRegistry registry,
        IHostActionBindingSet? hostActions, HostInputSchemaKind schema, bool isRightClick)
    {
        ArgumentNullException.ThrowIfNull(registry);
        int totalRows = TotalRowCount > 0 ? TotalRowCount : registry.Slots.Count;
        if (MatchTarget(x, y, targetId, "Keybinds_ScrollUp", _scrollUpRect)) { _ = ScrollBy(-1, totalRows); return true; }
        if (MatchTarget(x, y, targetId, "Keybinds_ScrollDown", _scrollDownRect)) { _ = ScrollBy(1, totalRows); return true; }
        if (MatchTarget(x, y, targetId, "Keybinds_ResetDefaults", _resetRect))
        {
            registry.ResetToSchemaDefaults(schema);
            if (hostActions is not null)
            {
                hostActions.ResetToDefaults();
                EventCallbacks?.OnHostActionBindingsChanged();
            }
            ClearCapture();
            return true;
        }

        foreach (SlotHitTarget target in _slotTargets)
        {
            if (!MatchTarget(x, y, targetId, target.ControlId, target.Bounds)) continue;
            if (isRightClick)
            {
                if (target.IsHostAction)
                {
                    if (hostActions?.ClearSlotBinding(target.ActionId, target.ColumnIndex) == true)
                        EventCallbacks?.OnHostActionBindingsChanged();
                }
                else
                {
                    _ = registry.ClearSlotBinding(target.ActionId, target.ColumnIndex);
                }
                ClearCapture();
                return true;
            }

            LastConflictMessage = null;
            CapturingActionId = target.ActionId;
            CapturingColumnIndex = target.ColumnIndex;
            _capturingIsHostAction = target.IsHostAction;
            CapturingDisplayName = ResolveSlotDisplayName(target.ActionId, target.IsHostAction, registry, hostActions);
            return true;
        }
        return false;
    }

    private bool TryCaptureCore(int inputCode, HostMultiBindRegistry registry, IHostActionBindingSet? hostActions)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (!IsCapturing || string.IsNullOrWhiteSpace(CapturingActionId)) return false;
        if (inputCode == HostScancodes.F1) return false;
        if (inputCode == HostScancodes.Escape) { ClearCapture(); return true; }

        if (inputCode is HostScancodes.Delete or HostScancodes.Backspace)
        {
            if (_capturingIsHostAction)
            {
                if (hostActions?.ClearSlotBinding(CapturingActionId, CapturingColumnIndex) == true)
                    EventCallbacks?.OnHostActionBindingsChanged();
            }
            else
            {
                _ = registry.ClearSlotBinding(CapturingActionId, CapturingColumnIndex);
            }
            ClearCapture();
            return true;
        }

        bool validForColumn = CapturingColumnIndex == 3
            ? HostMultiBindRegistry.IsGamepadCode(inputCode)
            : HostMultiBindRegistry.IsValidBindingCode(inputCode);
        if (!validForColumn) return true;

        if (_capturingIsHostAction)
        {
            if (hostActions is null) { ClearCapture(); return true; }
            if (TryFindBoundSlotName(registry.Slots, inputCode, null, out string? gameConflict))
            {
                LastConflictMessage = $"Conflict: {HostMultiBindRegistry.FormatBindingLabel(inputCode)} bound to '{gameConflict}'.";
                return true;
            }
            if (!hostActions.TrySetSlotBindingUnique(CapturingActionId, CapturingColumnIndex, inputCode, out string? hostConflict))
            {
                LastConflictMessage = hostConflict ?? $"Conflict: {HostMultiBindRegistry.FormatBindingLabel(inputCode)} already bound.";
                return true;
            }
            EventCallbacks?.OnHostActionBindingsChanged();
            ClearCapture();
            return true;
        }

        if (hostActions is not null && TryFindBoundSlotName(hostActions.Slots, inputCode, null, out string? hostOwner))
        {
            LastConflictMessage = $"Conflict: {HostMultiBindRegistry.FormatBindingLabel(inputCode)} bound to host '{hostOwner}'.";
            return true;
        }

        if (!registry.TrySetSlotBindingUnique(CapturingActionId, CapturingColumnIndex, inputCode, out HostSlotConflict? conflict))
        {
            LastConflictMessage = conflict?.Reason ?? $"Conflict: {HostMultiBindRegistry.FormatBindingLabel(inputCode)} already bound.";
            return true;
        }

        ClearCapture();
        return true;
    }

    private void RenderCore(
        PixelOverlayCanvas canvas, Rectangle bodyRect, HostInputSchemaKind schema,
        HostMultiBindRegistry registry, HostControlTierStore? tierStore, double uiTextScale, string? focusedControlId)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(registry);
        _slotTargets.Clear(); _tierTargets.Clear(); _widgets.Clear();

        double scale = HostOverlayTextRenderer.ClampUiTextScale(uiTextScale);
        int rowH = HostOverlayTextRenderer.GetRowHeight(scale);
        int lineH = HostOverlayTextRenderer.GetLineHeight(scale);
        int tierBarOffset = 0;

        if (tierStore is not null)
        {
            tierBarOffset = rowH + 4;
            int tierGap = 4;
            int tierW = Math.Max(60, (bodyRect.Width - (tierGap * 2)) / 3);
            for (int i = 0; i < Tiers.Length; i++)
            {
                (HostControlTierMode tier, string id, string label) = Tiers[i];
                var tRect = new Rectangle(bodyRect.X + (i * (tierW + tierGap)), bodyRect.Y, tierW, rowH);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, tRect, label, tierStore.ActiveTier == tier, HostOverlayPalette.AccentCyan, scale, IsFocused(focusedControlId, id));
                _tierTargets.Add(new(id, tRect, tier));
                _widgets.Add(new(id, "Button", label, tRect, true));
            }
        }

        List<MatrixRow> rows = BuildRows(registry, tierStore?.HostActions);
        TotalRowCount = rows.Count;
        const int rowGap = 4, colGap = 4;
        int scrollBtnW = Math.Clamp((int)Math.Round(24 * scale), 22, 36);
        int scrollAreaW = (scrollBtnW * 2) + 8;
        int footerH = rowH + 6;
        int bannerH = Math.Max(lineH + 6, 22);
        int topH = Math.Max(lineH + 6, 22);
        int headerH = topH + bannerH + 6;
        int topY = bodyRect.Y + tierBarOffset;
        int visibleRows = Math.Max(1, (bodyRect.Height - tierBarOffset - headerH - footerH) / (rowH + rowGap));
        _lastVisibleRows = visibleRows;
        int maxOffset = Math.Max(0, rows.Count - visibleRows);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, maxOffset);

        int gridW = bodyRect.Width - scrollAreaW - 4;
        int slotColW = Math.Clamp((int)(gridW * 0.165), 44, 128);
        int actW = Math.Max(80, bodyRect.Width - (4 * (slotColW + colGap)) - scrollAreaW);
        int slotsStartX = bodyRect.X + actW + colGap;
        int endRow = Math.Min(rows.Count, ScrollOffset + visibleRows);

        HostOverlayTextRenderer.DrawText(canvas, bodyRect.X, topY + 2,
            HostOverlayTextRenderer.FitText($"Action ({(rows.Count == 0 ? 0 : ScrollOffset + 1)}-{endRow}/{rows.Count})", actW, scale),
            HostOverlayPalette.AccentCyan, scale);

        string[] colHeaders = ["1st", "2nd", "3rd", "Gamepad"];
        for (int c = 0; c < 4; c++)
        {
            HostOverlayTextRenderer.DrawText(canvas, slotsStartX + (c * (slotColW + colGap)) + 4, topY + 2,
                HostOverlayTextRenderer.FitText(colHeaders[c], slotColW - 4, scale), HostOverlayPalette.TextMuted, scale);
        }

        int scrollH = Math.Max(18, topH - 2);
        _scrollUpRect = new(bodyRect.Right - (scrollBtnW * 2) - 4, topY, scrollBtnW, scrollH);
        _scrollDownRect = new(bodyRect.Right - scrollBtnW, topY, scrollBtnW, scrollH);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollUpRect, "^", ScrollOffset > 0, HostOverlayPalette.AccentCyan, scale, IsFocused(focusedControlId, "Keybinds_ScrollUp"));
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _scrollDownRect, "v", ScrollOffset < maxOffset, HostOverlayPalette.AccentCyan, scale, IsFocused(focusedControlId, "Keybinds_ScrollDown"));
        _widgets.Add(new("Keybinds_ScrollUp", "Button", "Scroll Up", _scrollUpRect, true));
        _widgets.Add(new("Keybinds_ScrollDown", "Button", "Scroll Down", _scrollDownRect, true));

        bool warnBanner = IsCapturing || !string.IsNullOrWhiteSpace(LastConflictMessage);
        string statusText = !string.IsNullOrWhiteSpace(LastConflictMessage) ? LastConflictMessage!
            : IsCapturing ? $"Bind {CapturingDisplayName} [{colHeaders[Math.Clamp(CapturingColumnIndex, 0, 3)]}]: Input (Esc cancel, Del clear)"
            : $"{(tierStore is not null ? HostControlTierStore.ToDisplayLabel(tierStore.ActiveTier) + " | " : string.Empty)}{HostInputSchemas.FormatPresetLabel(schema)} | Click slot to rebind";
        HostOverlayLayoutHelpers.DrawBanner(canvas, new(bodyRect.X, topY + topH, bodyRect.Width, bannerH), statusText,
            warnBanner ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass,
            warnBanner ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border,
            warnBanner ? HostOverlayPalette.AccentWarning : HostOverlayPalette.TextMuted, scale);

        int y = topY + headerH;
        for (int r = 0; r < visibleRows && (ScrollOffset + r) < rows.Count; r++)
        {
            MatrixRow row = rows[ScrollOffset + r];
            if (row.IsSeparator || row.Slot is null)
            {
                HostOverlayLayoutHelpers.DrawBanner(canvas, new(bodyRect.X, y, bodyRect.Width, rowH), row.Title,
                    HostOverlayPalette.Surface2Glass, HostOverlayPalette.AccentCyan, HostOverlayPalette.AccentCyan, scale);
                y += rowH + rowGap;
                continue;
            }

            HostMultiBindSlot slot = row.Slot;
            bool rowActive = string.Equals(CapturingActionId, slot.ActionId, StringComparison.OrdinalIgnoreCase) && _capturingIsHostAction == row.IsHostAction;
            var labelRect = new Rectangle(bodyRect.X, y, actW, rowH);
            HostOverlayLayoutHelpers.DrawPanel(canvas, labelRect, rowActive ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass, rowActive ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border);
            (_, int th) = HostOverlayTextRenderer.MeasureText(slot.DisplayName, scale);
            HostOverlayTextRenderer.DrawText(canvas, labelRect.X + 6, labelRect.Y + Math.Max(2, (rowH - th) / 2),
                HostOverlayTextRenderer.FitText(slot.DisplayName, actW - 10, scale), HostOverlayPalette.TextPrimary, scale);

            int[] codes = [slot.Slot1, slot.Slot2, slot.Slot3, slot.GamepadCode];
            for (int c = 0; c < 4; c++)
            {
                bool cellActive = rowActive && CapturingColumnIndex == c;
                string id = $"Keybinds_Slot_{slot.ActionId}_{c}";
                var pillRect = new Rectangle(slotsStartX + (c * (slotColW + colGap)), y, slotColW, rowH);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, pillRect, cellActive ? "..." : HostMultiBindRegistry.FormatBindingLabel(codes[c]),
                    cellActive, HostOverlayPalette.AccentWarning, scale, IsFocused(focusedControlId, id));
                _slotTargets.Add(new(id, pillRect, slot.ActionId, c, row.IsHostAction));
                _widgets.Add(new(id, "Button", $"{slot.DisplayName} {colHeaders[c]}", pillRect, true));
            }
            y += rowH + rowGap;
        }

        _resetRect = new(bodyRect.X, bodyRect.Bottom - rowH - 2, Math.Min(230, bodyRect.Width), rowH);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, _resetRect, "Reset Defaults", false, HostOverlayPalette.AccentCyan, scale, IsFocused(focusedControlId, "Keybinds_ResetDefaults"));
        _widgets.Add(new("Keybinds_ResetDefaults", "Button", "Reset Defaults", _resetRect, true));
    }

    private static List<MatrixRow> BuildRows(HostMultiBindRegistry registry, IHostActionBindingSet? hostActions)
    {
        var list = new List<MatrixRow>(registry.Slots.Count + 10);
        foreach (HostMultiBindSlot slot in registry.Slots) list.Add(new(false, slot.DisplayName, slot, false));
        if (hostActions is not null && hostActions.Slots.Count > 0)
        {
            list.Add(new(true, "--- HOST ACTIONS ---", null, true));
            foreach (HostMultiBindSlot slot in hostActions.Slots) list.Add(new(false, slot.DisplayName, slot, true));
        }
        return list;
    }

    private static string ResolveSlotDisplayName(string actionId, bool isHost, HostMultiBindRegistry registry, IHostActionBindingSet? hostActions)
    {
        if (isHost && hostActions is not null && hostActions.TryGetSlot(actionId, out HostMultiBindSlot hs)) return hs.DisplayName;
        return registry.TryGetSlot(actionId, out HostMultiBindSlot gs) ? gs.DisplayName : actionId;
    }

    private static bool TryFindBoundSlotName(IReadOnlyList<HostMultiBindSlot> slots, int code, string? ignoreActionId, out string? ownerName)
    {
        foreach (HostMultiBindSlot s in slots)
        {
            if (ignoreActionId is not null && string.Equals(s.ActionId, ignoreActionId, StringComparison.OrdinalIgnoreCase)) continue;
            if (s.Slot1 == code || s.Slot2 == code || s.Slot3 == code || s.GamepadCode == code) { ownerName = s.DisplayName; return true; }
        }
        ownerName = null;
        return false;
    }

    private static bool IsFocused(string? focusedId, string id) =>
        !string.IsNullOrEmpty(focusedId) && string.Equals(focusedId, id, StringComparison.OrdinalIgnoreCase);

    private static bool MatchTarget(int x, int y, string? targetId, string expectedId, Rectangle bounds) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, expectedId, StringComparison.OrdinalIgnoreCase) : bounds.Contains(x, y);
}
