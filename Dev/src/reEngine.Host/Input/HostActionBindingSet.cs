// @module: reEngine.Host.Input.HostActionBindingSet
// @purpose: Rebindable Standard 18 hostActionBindings manager with cross-tier conflict prevention, JSON persistence, and controller chord evaluation.
// @depends-on: System, System.Collections.Generic, System.Text.Json, reEngine.Host.Contracts
// @exports: HostActionBindingSet

using System.Text.Json;
using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Manages rebindable host recomp actions (<c>hostActionBindings</c>) cleanly separated from per-game bindings.
/// </summary>
public sealed class HostActionBindingSet : IHostActionBindingSet
{
    private sealed class ActionEntry(string actionId, string displayName, HostHotkeyFlags flag, int s1, int s2, int s3, int gp)
    {
        public string ActionId { get; } = actionId;
        public string DisplayName { get; } = displayName;
        public HostHotkeyFlags Flag { get; } = flag;
        public int[] Slots { get; } = [s1, s2, s3];
        public int GamepadCode { get; set; } = gp;
        public HostMultiBindSlot ToRecord() => new(ActionId, DisplayName, Slots[0], Slots[1], Slots[2], GamepadCode);
    }

    private static readonly IReadOnlySet<int> EmptySet = new HashSet<int>();
    private readonly List<ActionEntry> _entries = new();
    private readonly Dictionary<string, ActionEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prevHeldActions = new(StringComparer.OrdinalIgnoreCase);
    private bool _prevMenuChordHeld;

    /// <summary>Initializes the binding set with canonical Standard R08 §4.3 defaults.</summary>
    public HostActionBindingSet() => ResetToDefaults();

    /// <inheritdoc />
    public IReadOnlyList<HostMultiBindSlot> Slots
    {
        get { var res = new HostMultiBindSlot[_entries.Count]; for (int i = 0; i < _entries.Count; i++) res[i] = _entries[i].ToRecord(); return res; }
    }

    /// <inheritdoc />
    public bool ContainsAction(string actionId) => !string.IsNullOrWhiteSpace(actionId) && _byId.ContainsKey(actionId);

    /// <inheritdoc />
    public bool TryGetSlot(string actionId, out HostMultiBindSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(actionId) && _byId.TryGetValue(actionId, out ActionEntry? entry))
        { slot = entry.ToRecord(); return true; }
        slot = default!;
        return false;
    }

    /// <inheritdoc />
    public bool TrySetSlotBindingUnique(string actionId, int columnIndex, int code, out string? conflictReason) =>
        TrySetSlotBindingUnique(actionId, columnIndex, code, gameRegistry: null, out conflictReason);

    /// <summary>
    /// Assigns a binding code to <paramref name="actionId"/> column (<c>0..2</c> KB/M, <c>3</c> Gamepad)
    /// only if it does not collide within <c>hostActionBindings</c> or <paramref name="gameRegistry"/>.
    /// </summary>
    public bool TrySetSlotBindingUnique(
        string actionId, int columnIndex, int code, HostMultiBindRegistry? gameRegistry, out string? conflictReason)
    {
        conflictReason = null;
        if (string.IsNullOrWhiteSpace(actionId) || !_byId.TryGetValue(actionId, out ActionEntry? target) || columnIndex is < 0 or > 3)
            return false;

        int normalized = columnIndex == 3 ? HostMultiBindRegistry.NormalizeGamepadCode(code) : HostMultiBindRegistry.NormalizeCode(code);
        if (code > 0 && normalized == 0) { conflictReason = $"Binding code ({code}) is invalid for this column."; return false; }

        if (normalized > 0 && HasConflict(target, columnIndex, normalized, gameRegistry, out conflictReason))
            return false;

        if (columnIndex == 3) target.GamepadCode = normalized;
        else target.Slots[columnIndex] = normalized;
        return true;
    }

    /// <inheritdoc />
    public bool ClearSlotBinding(string actionId, int columnIndex)
    {
        if (string.IsNullOrWhiteSpace(actionId) || !_byId.TryGetValue(actionId, out ActionEntry? entry) || columnIndex is < 0 or > 3)
            return false;
        if (columnIndex == 3) entry.GamepadCode = 0; else entry.Slots[columnIndex] = 0;
        return true;
    }

    /// <inheritdoc />
    public void ResetToDefaults()
    {
        _entries.Clear();
        _byId.Clear();
        _prevHeldActions.Clear();
        _prevMenuChordHeld = false;
        AddDefault(HostActionBindingIds.ToggleMenu, "Pause / Menu", HostHotkeyFlags.ToggleDrawer, HostScancodes.F1, 0, 0, 0);
        AddDefault(HostActionBindingIds.ToggleHud, "Toggle HUD", HostHotkeyFlags.ToggleHud, HostScancodes.F3, 0, 0, 0);
        AddDefault(HostActionBindingIds.QuickSave, "Quick Save", HostHotkeyFlags.QuickSave, HostScancodes.F5, 0, 0, 0);
        AddDefault(HostActionBindingIds.QuickLoad, "Quick Load", HostHotkeyFlags.QuickLoad, HostScancodes.F9, 0, 0, 0);
        AddDefault(HostActionBindingIds.FastForward, "Fast Forward", HostHotkeyFlags.FastForward, HostScancodes.F6, HostScancodes.Grave, 0, 0);
        AddDefault(HostActionBindingIds.CutsceneSkip, "Skip Cutscene", HostHotkeyFlags.CutsceneSkip, HostScancodes.F7, 0, 0, 0);
        AddDefault(HostActionBindingIds.ToggleFullscreen, "Fullscreen", HostHotkeyFlags.ToggleFullscreen, HostScancodes.F11, 0, 0, 0);
        AddDefault(HostActionBindingIds.TakeScreenshot, "Screenshot", HostHotkeyFlags.TakeScreenshot | HostHotkeyFlags.DumpFrame, HostScancodes.F12, 0, 0, 0);
    }

    /// <inheritdoc />
    public bool IsCodeBound(int code)
    {
        if (code <= 0) return false;
        foreach (ActionEntry e in _entries)
        {
            if (e.GamepadCode == code || e.Slots[0] == code || e.Slots[1] == code || e.Slots[2] == code) return true;
        }
        return false;
    }

    /// <inheritdoc />
    public string FormatSummaryLabel(string actionId)
    {
        if (!TryGetSlot(actionId, out HostMultiBindSlot s)) return "---";
        int code = s.Slot1 > 0 ? s.Slot1 : s.Slot2 > 0 ? s.Slot2 : s.Slot3 > 0 ? s.Slot3 : s.GamepadCode;
        return code > 0 ? HostInputSchemas.FormatBindingLabel(code) : "---";
    }

    /// <inheritdoc />
    public void ExportJsonMap(Dictionary<string, int[]> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        foreach (ActionEntry e in _entries)
        {
            destination[e.ActionId] = HostMultiBindHelpers.TrimTrailingZeros([e.Slots[0], e.Slots[1], e.Slots[2], e.GamepadCode]);
        }
    }

    /// <inheritdoc />
    public void ImportJsonMap(Dictionary<string, JsonElement>? source)
    {
        if (source is null) return;
        foreach ((string key, JsonElement val) in source)
        {
            if (!_byId.TryGetValue(key, out ActionEntry? entry)) continue;
            Array.Clear(entry.Slots);
            entry.GamepadCode = 0;
            if (val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int scalar))
            {
                if (HostMultiBindRegistry.IsGamepadCode(scalar)) entry.GamepadCode = scalar;
                else entry.Slots[0] = HostMultiBindRegistry.NormalizeCode(scalar);
            }
            else if (val.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (JsonElement item in val.EnumerateArray())
                {
                    if (idx > 3) break;
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int code))
                    {
                        if (idx == 3) entry.GamepadCode = HostMultiBindRegistry.NormalizeGamepadCode(code);
                        else entry.Slots[idx] = HostMultiBindRegistry.NormalizeCode(code);
                    }
                    idx++;
                }
            }
        }
        _prevHeldActions.Clear();
        _prevMenuChordHeld = false;
    }

    /// <summary>
    /// Evaluates edge-triggered <see cref="HostHotkeyFlags"/> across all 8 host actions and <paramref name="menuChord"/>.
    /// </summary>
    public HostHotkeyFlags EvaluateHotkeys(
        IReadOnlySet<int> heldScancodes,
        IReadOnlySet<int> latchedCodes,
        int mouseButtons,
        float wheelDelta,
        uint gamepadMask,
        HostMenuControllerChord menuChord,
        out bool fastForwardHeld)
    {
        IReadOnlySet<int> held = heldScancodes ?? EmptySet;
        IReadOnlySet<int> latched = latchedCodes ?? EmptySet;
        HostHotkeyFlags flags = HostHotkeyFlags.None;
        fastForwardHeld = false;

        foreach (ActionEntry entry in _entries)
        {
            int[] gpBuf = [entry.GamepadCode, 0, 0];
            bool heldNow = HostMultiBindHelpers.IsAnyCodeHeld(entry.Slots, gpBuf, held, mouseButtons, wheelDelta, gamepadMask);
            bool latchedNow = HostMultiBindHelpers.IsAnyCodeLatched(entry.Slots, gpBuf, latched);
            if (string.Equals(entry.ActionId, HostActionBindingIds.FastForward, StringComparison.Ordinal) && (heldNow || latchedNow))
                fastForwardHeld = true;

            if ((heldNow || latchedNow) && (latchedNow || !_prevHeldActions.Contains(entry.ActionId)))
                flags |= entry.Flag;

            if (heldNow) _prevHeldActions.Add(entry.ActionId); else _prevHeldActions.Remove(entry.ActionId);
        }

        bool chordNow = IsMenuChordPressed(menuChord, gamepadMask, latched);
        if (chordNow && !_prevMenuChordHeld) flags |= HostHotkeyFlags.ToggleDrawer;
        _prevMenuChordHeld = IsMenuChordPressed(menuChord, gamepadMask, EmptySet);
        return flags;
    }

    private void AddDefault(string id, string label, HostHotkeyFlags flag, int s1, int s2, int s3, int gp)
    {
        var entry = new ActionEntry(id, label, flag, s1, s2, s3, gp);
        _entries.Add(entry);
        _byId[id] = entry;
    }

    private bool HasConflict(ActionEntry target, int columnIndex, int code, HostMultiBindRegistry? gameRegistry, out string? conflictReason)
    {
        foreach (ActionEntry e in _entries)
        {
            for (int c = 0; c < 3; c++)
            {
                if ((!ReferenceEquals(e, target) || c != columnIndex) && e.Slots[c] == code)
                { conflictReason = $"{HostInputSchemas.FormatBindingLabel(code)} already bound to {e.DisplayName}."; return true; }
            }
            if ((!ReferenceEquals(e, target) || columnIndex != 3) && e.GamepadCode == code)
            { conflictReason = $"{HostInputSchemas.FormatBindingLabel(code)} already bound to {e.DisplayName}."; return true; }
        }

        if (gameRegistry is not null)
        {
            foreach (HostMultiBindSlot gs in gameRegistry.Slots)
            {
                if (gs.Slot1 == code || gs.Slot2 == code || gs.Slot3 == code || gs.GamepadCode == code)
                { conflictReason = $"{HostInputSchemas.FormatBindingLabel(code)} already bound to {gs.DisplayName}."; return true; }
            }
            if (gameRegistry.IsCodeBound(code))
            { conflictReason = $"{HostInputSchemas.FormatBindingLabel(code)} already bound in active game tier."; return true; }
        }

        conflictReason = null;
        return false;
    }

    private static bool IsMenuChordPressed(HostMenuControllerChord chord, uint gamepadMask, IReadOnlySet<int> latched)
    {
        static bool Pressed(int code, uint mask, IReadOnlySet<int> lat) =>
            Sdl3GamepadProvider.IsGamepadCodePressed(mask, code) || lat.Contains(code);

        return chord switch
        {
            HostMenuControllerChord.L3PlusR3 => Pressed(HostBindingCodes.GamepadL3, gamepadMask, latched) && Pressed(HostBindingCodes.GamepadR3, gamepadMask, latched),
            HostMenuControllerChord.SelectPlusStart => Pressed(HostBindingCodes.GamepadSelect, gamepadMask, latched) && Pressed(HostBindingCodes.GamepadStart, gamepadMask, latched),
            HostMenuControllerChord.Guide => Pressed(HostBindingCodes.GamepadGuide, gamepadMask, latched),
            _ => false
        };
    }
}
