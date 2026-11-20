// @module: reEngine.Host.Input.HostMultiBindRegistry
// @purpose: Standard 18 3-slot [1st, 2nd, 3rd] + gamepad binding registry, JSON persistence, conflict validation, and single-edge action tracking.
// @depends-on: System, System.Collections.Generic, System.Text.Json, reEngine.Host.Contracts
// @exports: HostMultiBindRegistry

using System.Text.Json;
using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Manages 3-slot simultaneous keyboard/mouse/gamepad action bindings (<c>modernActionBindings</c> /
/// <c>modernControllerBindings</c>) with Standard 18 serialization, conflict detection, and single-edge action tracking.
/// </summary>
public sealed class HostMultiBindRegistry
{
    private sealed class ActionEntry
    {
        public string ActionId { get; }
        public string DisplayName { get; set; }
        public int[] Slots { get; } = new int[3];
        public int[] GamepadCodes { get; } = new int[3];

        public ActionEntry(HostMultiBindSlot slot)
        {
            ActionId = slot.ActionId;
            DisplayName = slot.DisplayName;
            Slots[0] = NormalizeCode(slot.Slot1);
            Slots[1] = NormalizeCode(slot.Slot2);
            Slots[2] = NormalizeCode(slot.Slot3);
            GamepadCodes[0] = NormalizeGamepadCode(slot.GamepadCode);
        }

        public HostMultiBindSlot ToRecord() => new(ActionId, DisplayName, Slots[0], Slots[1], Slots[2], GamepadCodes[0]);
    }

    private static readonly uint DpadMaskBits =
        Sdl3GamepadProvider.ToGamepadMaskBit(HostBindingCodes.GamepadDPadUp) |
        Sdl3GamepadProvider.ToGamepadMaskBit(HostBindingCodes.GamepadDPadDown) |
        Sdl3GamepadProvider.ToGamepadMaskBit(HostBindingCodes.GamepadDPadLeft) |
        Sdl3GamepadProvider.ToGamepadMaskBit(HostBindingCodes.GamepadDPadRight);

    private static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<int> EmptySet = new();
    private readonly List<ActionEntry> _ordered = new();
    private readonly Dictionary<string, ActionEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _justPressedActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prevHeldActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nonGamepadActions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes the registry with conflict-free defaults for <paramref name="initialSchema"/>.</summary>
    public HostMultiBindRegistry(HostInputSchemaKind initialSchema = HostInputSchemaKind.SchemaB) => ResetToSchemaDefaults(initialSchema);

    /// <summary>Gets all ordered action binding rows in this registry.</summary>
    public IReadOnlyList<HostMultiBindSlot> Slots
    {
        get { var res = new HostMultiBindSlot[_ordered.Count]; for (int i = 0; i < _ordered.Count; i++) res[i] = _ordered[i].ToRecord(); return res; }
    }

    /// <summary>Gets the set of currently active action identifiers.</summary>
    public IReadOnlySet<string> ActiveActions => _activeActions;

    /// <summary>Gets the set of action identifiers that transitioned to active on this frame.</summary>
    public IReadOnlySet<string> JustPressedActions => _justPressedActions;

    /// <summary>Returns whether <paramref name="actionId"/> is active on the current frame.</summary>
    public bool IsActionActive(string actionId) => _activeActions.Contains(actionId);

    /// <summary>Returns whether <paramref name="actionId"/> was newly pressed on the current frame.</summary>
    public bool IsActionJustPressed(string actionId) => _justPressedActions.Contains(actionId);

    internal bool IsActionFromNonGamepad(string actionId) => _nonGamepadActions.Contains(actionId);

    /// <summary>Resets all bindings to the default multi-bind layout for <paramref name="schema"/>.</summary>
    public void ResetToSchemaDefaults(HostInputSchemaKind schema)
    {
        _ordered.Clear();
        _byId.Clear();
        foreach (HostMultiBindSlot slot in HostInputSchemas.GetDefaultMultiBindSlots(schema)) RegisterAction(slot);
    }

    /// <summary>Registers or replaces an action row in the registry.</summary>
    public void RegisterAction(HostMultiBindSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (string.IsNullOrWhiteSpace(slot.ActionId)) return;
        if (_byId.TryGetValue(slot.ActionId, out ActionEntry? existing))
        {
            existing.DisplayName = slot.DisplayName;
            existing.Slots[0] = NormalizeCode(slot.Slot1); existing.Slots[1] = NormalizeCode(slot.Slot2); existing.Slots[2] = NormalizeCode(slot.Slot3);
            existing.GamepadCodes[0] = NormalizeGamepadCode(slot.GamepadCode); existing.GamepadCodes[1] = 0; existing.GamepadCodes[2] = 0;
            return;
        }
        var entry = new ActionEntry(slot);
        _ordered.Add(entry);
        _byId[slot.ActionId] = entry;
    }

    /// <summary>Attempts to retrieve the current slot snapshot for <paramref name="actionId"/>.</summary>
    public bool TryGetSlot(string actionId, out HostMultiBindSlot slot)
    {
        if (_byId.TryGetValue(actionId, out ActionEntry? entry)) { slot = entry.ToRecord(); return true; }
        slot = default!;
        return false;
    }

    /// <summary>Assigns a binding code to the specified action column (<c>0..2</c> KB/M, <c>3</c> Gamepad).</summary>
    public bool SetSlotBinding(string actionId, int columnIndex, int code)
    {
        if (!_byId.TryGetValue(actionId, out ActionEntry? entry) || columnIndex is < 0 or > 3) return false;
        if (columnIndex == 3) { entry.GamepadCodes[0] = NormalizeGamepadCode(code); entry.GamepadCodes[1] = 0; entry.GamepadCodes[2] = 0; }
        else entry.Slots[columnIndex] = NormalizeCode(code);
        return true;
    }

    /// <summary>Assigns a binding code only if it does not collide with another slot in the same tier.</summary>
    public bool TrySetSlotBindingUnique(string actionId, int columnIndex, int code, out HostSlotConflict? conflict)
    {
        conflict = null;
        if (!_byId.TryGetValue(actionId, out ActionEntry? target) || columnIndex is < 0 or > 3) return false;
        int normalized = columnIndex == 3 ? NormalizeGamepadCode(code) : NormalizeCode(code);
        if (code > 0 && normalized == 0) return false;
        if (normalized > 0 && FindConflict(target, actionId, columnIndex, normalized, out conflict)) return false;
        return SetSlotBinding(actionId, columnIndex, normalized);
    }

    /// <summary>Clears the binding code at <paramref name="columnIndex"/> for <paramref name="actionId"/>.</summary>
    public bool ClearSlotBinding(string actionId, int columnIndex) => SetSlotBinding(actionId, columnIndex, 0);

    /// <summary>Returns all duplicate binding collisions across visible and hidden slots in this registry.</summary>
    public IReadOnlyList<HostSlotConflict> ValidateUniqueSlots()
    {
        var conflicts = new List<HostSlotConflict>();
        var seen = new Dictionary<int, (string ActionId, string DisplayName)>();
        foreach (ActionEntry entry in _ordered)
        {
            for (int c = 0; c < 3; c++) RecordIfDuplicate(entry, c, entry.Slots[c], seen, conflicts);
            for (int g = 0; g < 3; g++) RecordIfDuplicate(entry, 3 + g, entry.GamepadCodes[g], seen, conflicts);
        }
        return conflicts;
    }

    /// <summary>Detects invisible gamepad slots (<c>[1+]</c>) and duplicate binding conflicts across the registry.</summary>
    public IReadOnlyList<HostSlotConflict> DetectHiddenSlotConflicts()
    {
        var conflicts = new List<HostSlotConflict>();
        foreach (ActionEntry entry in _ordered)
            for (int g = 1; g < 3; g++)
                if (entry.GamepadCodes[g] > 0)
                    conflicts.Add(new(entry.ActionId, entry.ActionId, 3 + g, entry.GamepadCodes[g], $"Hidden gamepad slot [{g}] ({FormatBindingLabel(entry.GamepadCodes[g])}) is invisible in UI."));

        foreach (HostSlotConflict dup in ValidateUniqueSlots())
            if (!conflicts.Exists(c => c.ActionId == dup.ActionId && c.SlotIndex == dup.SlotIndex && c.BindingCode == dup.BindingCode)) conflicts.Add(dup);
        return conflicts;
    }

    /// <summary>Clears all hidden <c>[1+]</c> gamepad slots and returns how many codes were removed.</summary>
    public int ClearHiddenGamepadSlots()
    {
        int cleared = 0;
        foreach (ActionEntry entry in _ordered) for (int g = 1; g < 3; g++) if (entry.GamepadCodes[g] != 0) { entry.GamepadCodes[g] = 0; cleared++; }
        return cleared;
    }

    /// <summary>Evaluates active and edge-triggered actions for the current input frame.</summary>
    public void EvaluateFrame(IReadOnlySet<int> heldScancodes, IReadOnlySet<int> latchedCodes, int mouseButtons, float wheelDelta, uint gamepadButtonsMask) =>
        EvaluateFrame(heldScancodes, latchedCodes, mouseButtons, wheelDelta, gamepadButtonsMask, isolateMenuDpad: false);

    internal void EvaluateFrame(IReadOnlySet<int> heldScancodes, IReadOnlySet<int> latchedCodes, int mouseButtons, float wheelDelta, uint gamepadButtonsMask, bool isolateMenuDpad)
    {
        _activeActions.Clear(); _justPressedActions.Clear(); _nonGamepadActions.Clear();
        foreach (ActionEntry entry in _ordered)
        {
            uint gpMask = isolateMenuDpad && !entry.ActionId.StartsWith("move", StringComparison.OrdinalIgnoreCase) ? gamepadButtonsMask & ~DpadMaskBits : gamepadButtonsMask;
            bool heldNonGp = HostMultiBindHelpers.IsAnyCodeHeld(entry.Slots, entry.GamepadCodes, heldScancodes, mouseButtons, wheelDelta, 0u);
            bool heldNow = heldNonGp || HostMultiBindHelpers.IsAnyCodeHeld(entry.Slots, entry.GamepadCodes, EmptySet, 0, 0f, gpMask);
            bool tappedNow = HostMultiBindHelpers.IsAnyCodeLatched(entry.Slots, entry.GamepadCodes, latchedCodes);
            if (heldNow || tappedNow)
            {
                _activeActions.Add(entry.ActionId);
                if (heldNonGp || tappedNow) _nonGamepadActions.Add(entry.ActionId);
                if (tappedNow || !_prevHeldActions.Contains(entry.ActionId)) _justPressedActions.Add(entry.ActionId);
            }
            if (heldNow) _prevHeldActions.Add(entry.ActionId); else _prevHeldActions.Remove(entry.ActionId);
        }
    }

    /// <summary>Serializes registry bindings into Standard 18 JSON.</summary>
    public string SerializeJson(bool indented = true)
    {
        ExportBindings(out Dictionary<string, int[]> actionMap, out Dictionary<string, int[]> padMap);
        return JsonSerializer.Serialize(new { modernActionBindings = actionMap, modernControllerBindings = padMap }, new JsonSerializerOptions { WriteIndented = indented });
    }

    /// <summary>Deserializes registry bindings from Standard 18 JSON.</summary>
    public bool DeserializeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            HostMultiBindHelpers.BindingPayloadDto? dto = JsonSerializer.Deserialize<HostMultiBindHelpers.BindingPayloadDto>(json, JsonCaseInsensitive);
            if (dto is null) return false;
            ImportBindings(dto.ModernActionBindings, dto.ModernControllerBindings, dto.KeyBindings);
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal void ExportBindings(out Dictionary<string, int[]> actionMap, out Dictionary<string, int[]> padMap)
    {
        actionMap = new(StringComparer.Ordinal); padMap = new(StringComparer.Ordinal);
        foreach (ActionEntry e in _ordered)
        {
            int[] kbm = HostMultiBindHelpers.TrimTrailingZeros(e.Slots); if (kbm.Length > 0) actionMap[e.ActionId] = kbm;
            int[] pad = HostMultiBindHelpers.TrimTrailingZeros(e.GamepadCodes); if (pad.Length > 0) padMap[e.ActionId] = pad;
        }
    }

    internal void ImportBindings(Dictionary<string, JsonElement>? actionSection, Dictionary<string, JsonElement>? padSection, Dictionary<string, JsonElement>? keyBindings)
    {
        if (keyBindings is not null)
        {
            foreach ((string id, JsonElement val) in keyBindings)
            {
                if (val.ValueKind != JsonValueKind.Number || !val.TryGetInt32(out int code)) continue;
                ActionEntry entry = GetOrCreateEntry(id);
                if (IsGamepadCode(code)) entry.GamepadCodes[0] = code; else if (IsValidBindingCode(code)) entry.Slots[0] = code;
            }
        }
        ApplySection(actionSection, isGamepad: false);
        ApplySection(padSection, isGamepad: true);
    }

    /// <summary>Returns whether <paramref name="code"/> is a valid SDL keyboard scancode (<c>4..511</c>).</summary>
    public static bool IsKeyboardCode(int code) => code is >= HostBindingCodes.MinScancode and <= HostBindingCodes.MaxScancode;

    /// <summary>Returns whether <paramref name="code"/> is a valid mouse button or wheel code (<c>1001..1007</c>).</summary>
    public static bool IsMouseOrWheelCode(int code) => code is >= HostBindingCodes.MouseLeft and <= HostBindingCodes.WheelDown;

    /// <summary>Returns whether <paramref name="code"/> is a valid gamepad code (<c>2001..2032</c>).</summary>
    public static bool IsGamepadCode(int code) => code is >= HostBindingCodes.GamepadMin and <= HostBindingCodes.GamepadMax;

    /// <summary>Returns whether <paramref name="code"/> lies within any supported Standard 18 binding band.</summary>
    public static bool IsValidBindingCode(int code) => IsKeyboardCode(code) || IsMouseOrWheelCode(code) || IsGamepadCode(code);

    /// <summary>Clamps an invalid binding code to <c>0</c> while preserving valid KB/M/Gamepad codes.</summary>
    public static int NormalizeCode(int code) => IsValidBindingCode(code) ? code : 0;

    /// <summary>Clamps a non-gamepad binding code to <c>0</c> while preserving valid gamepad codes.</summary>
    public static int NormalizeGamepadCode(int code) => IsGamepadCode(code) ? code : 0;

    /// <summary>Formats a Standard 18 integer binding code for UI display.</summary>
    public static string FormatBindingLabel(int code) => HostInputSchemas.FormatBindingLabel(code);

    /// <summary>Converts an SDL mouse button index (<c>1..5</c>) to a Standard 18 binding code.</summary>
    public static int MouseButtonIndexToBindingCode(int buttonIndex) => buttonIndex switch
    {
        1 => HostBindingCodes.MouseLeft, 2 => HostBindingCodes.MouseMiddle, 3 => HostBindingCodes.MouseRight,
        4 => HostBindingCodes.Mouse4, 5 => HostBindingCodes.Mouse5, _ => 0
    };

    private ActionEntry GetOrCreateEntry(string id)
    {
        if (_byId.TryGetValue(id, out ActionEntry? e)) return e;
        e = new ActionEntry(new HostMultiBindSlot(id, id, 0, 0, 0, 0));
        _ordered.Add(e); _byId[id] = e;
        return e;
    }

    private void ApplySection(Dictionary<string, JsonElement>? section, bool isGamepad)
    {
        if (section is null) return;
        foreach ((string id, JsonElement val) in section)
        {
            ActionEntry entry = GetOrCreateEntry(id);
            int[] target = isGamepad ? entry.GamepadCodes : entry.Slots;
            Array.Clear(target);
            HostMultiBindHelpers.ReadCodesInto(val, target, isGamepad);
        }
    }

    private bool FindConflict(ActionEntry target, string actionId, int columnIndex, int code, out HostSlotConflict? conflict)
    {
        foreach (ActionEntry e in _ordered)
        {
            for (int c = 0; c < 3; c++)
            {
                if ((!ReferenceEquals(e, target) || c != columnIndex) && e.Slots[c] == code)
                { conflict = new(actionId, e.ActionId, columnIndex, code, $"{FormatBindingLabel(code)} already bound to {e.DisplayName}."); return true; }
            }
            for (int g = 0; g < 3; g++)
            {
                if ((!ReferenceEquals(e, target) || columnIndex != 3 || g != 0) && e.GamepadCodes[g] == code)
                { conflict = new(actionId, e.ActionId, columnIndex, code, $"{FormatBindingLabel(code)} already bound to {e.DisplayName}."); return true; }
            }
        }
        conflict = null;
        return false;
    }

    private static void RecordIfDuplicate(ActionEntry entry, int slotIdx, int code, Dictionary<int, (string ActionId, string DisplayName)> seen, List<HostSlotConflict> conflicts)
    {
        if (code <= 0) return;
        if (seen.TryGetValue(code, out (string ActionId, string DisplayName) first)) { conflicts.Add(new(entry.ActionId, first.ActionId, slotIdx, code, $"{FormatBindingLabel(code)} collides between {first.DisplayName} and {entry.DisplayName}.")); return; }
        seen[code] = (entry.ActionId, entry.DisplayName);
    }
}
