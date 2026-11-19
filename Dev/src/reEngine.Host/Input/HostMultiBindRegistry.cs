// @module: reEngine.Host.Input.HostMultiBindRegistry
// @purpose: Standard 18 3-slot [1st, 2nd, 3rd] + gamepad binding registry, JSON persistence, and single-edge action tracking.
// @depends-on: System, System.Collections.Generic, System.Text.Json, reEngine.Host.Contracts
// @exports: HostMultiBindRegistry

using System.Text.Json;
using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Manages 3-slot simultaneous keyboard/mouse/gamepad action bindings (<c>modernActionBindings</c> /
/// <c>modernControllerBindings</c>) with Standard 18 serialization and single-edge action tracking.
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

    private sealed class BindingPayloadDto
    {
        public Dictionary<string, JsonElement>? ModernActionBindings { get; set; }
        public Dictionary<string, JsonElement>? ModernControllerBindings { get; set; }
    }

    private static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<int> EmptySet = new();
    private readonly List<ActionEntry> _ordered = new();
    private readonly Dictionary<string, ActionEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _justPressedActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prevHeldActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nonGamepadActions = new(StringComparer.OrdinalIgnoreCase);

    public HostMultiBindRegistry(HostInputSchemaKind initialSchema = HostInputSchemaKind.SchemaB) =>
        ResetToSchemaDefaults(initialSchema);

    public IReadOnlyList<HostMultiBindSlot> Slots
    {
        get
        {
            var result = new HostMultiBindSlot[_ordered.Count];
            for (int i = 0; i < _ordered.Count; i++) result[i] = _ordered[i].ToRecord();
            return result;
        }
    }

    public IReadOnlySet<string> ActiveActions => _activeActions;
    public IReadOnlySet<string> JustPressedActions => _justPressedActions;
    public bool IsActionActive(string actionId) => _activeActions.Contains(actionId);
    public bool IsActionJustPressed(string actionId) => _justPressedActions.Contains(actionId);
    internal bool IsActionFromNonGamepad(string actionId) => _nonGamepadActions.Contains(actionId);

    public void ResetToSchemaDefaults(HostInputSchemaKind schema)
    {
        _ordered.Clear();
        _byId.Clear();
        foreach (HostMultiBindSlot slot in HostInputSchemas.GetDefaultMultiBindSlots(schema)) RegisterAction(slot);
    }

    public void RegisterAction(HostMultiBindSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (string.IsNullOrWhiteSpace(slot.ActionId)) return;
        if (_byId.TryGetValue(slot.ActionId, out ActionEntry? existing))
        {
            existing.DisplayName = slot.DisplayName;
            existing.Slots[0] = NormalizeCode(slot.Slot1);
            existing.Slots[1] = NormalizeCode(slot.Slot2);
            existing.Slots[2] = NormalizeCode(slot.Slot3);
            existing.GamepadCodes[0] = NormalizeGamepadCode(slot.GamepadCode);
            return;
        }
        var entry = new ActionEntry(slot);
        _ordered.Add(entry);
        _byId[slot.ActionId] = entry;
    }

    public bool TryGetSlot(string actionId, out HostMultiBindSlot slot)
    {
        if (_byId.TryGetValue(actionId, out ActionEntry? entry)) { slot = entry.ToRecord(); return true; }
        slot = default!;
        return false;
    }

    public bool SetSlotBinding(string actionId, int columnIndex, int code)
    {
        if (!_byId.TryGetValue(actionId, out ActionEntry? entry) || columnIndex is < 0 or > 3) return false;
        if (columnIndex == 3) entry.GamepadCodes[0] = NormalizeGamepadCode(code);
        else entry.Slots[columnIndex] = NormalizeCode(code);
        return true;
    }

    public bool ClearSlotBinding(string actionId, int columnIndex) => SetSlotBinding(actionId, columnIndex, 0);

    public void EvaluateFrame(IReadOnlySet<int> heldScancodes, IReadOnlySet<int> latchedCodes, int mouseButtons, float wheelDelta, uint gamepadButtonsMask)
    {
        _activeActions.Clear();
        _justPressedActions.Clear();
        _nonGamepadActions.Clear();

        foreach (ActionEntry entry in _ordered)
        {
            bool heldNonGamepad = IsEntryHeld(entry, heldScancodes, mouseButtons, wheelDelta, 0u);
            bool heldNow = heldNonGamepad || IsEntryHeld(entry, EmptySet, 0, 0f, gamepadButtonsMask);
            bool tappedNow = IsEntryLatched(entry, latchedCodes);

            if (heldNow || tappedNow)
            {
                _activeActions.Add(entry.ActionId);
                if (heldNonGamepad || tappedNow) _nonGamepadActions.Add(entry.ActionId);
                if (tappedNow || !_prevHeldActions.Contains(entry.ActionId)) _justPressedActions.Add(entry.ActionId);
            }
            if (heldNow) _prevHeldActions.Add(entry.ActionId);
            else _prevHeldActions.Remove(entry.ActionId);
        }
    }

    public string SerializeJson(bool indented = true)
    {
        var actionMap = new Dictionary<string, int[]>(StringComparer.Ordinal);
        var padMap = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (ActionEntry entry in _ordered)
        {
            int[] kbm = TrimTrailingZeros(entry.Slots);
            if (kbm.Length > 0) actionMap[entry.ActionId] = kbm;
            int[] pad = TrimTrailingZeros(entry.GamepadCodes);
            if (pad.Length > 0) padMap[entry.ActionId] = pad;
        }
        return JsonSerializer.Serialize(new { modernActionBindings = actionMap, modernControllerBindings = padMap },
            new JsonSerializerOptions { WriteIndented = indented });
    }

    public bool DeserializeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            BindingPayloadDto? dto = JsonSerializer.Deserialize<BindingPayloadDto>(json, JsonCaseInsensitive);
            if (dto is null) return false;
            ApplyBindingSection(dto.ModernActionBindings, isGamepadSection: false);
            ApplyBindingSection(dto.ModernControllerBindings, isGamepadSection: true);
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static bool IsKeyboardCode(int code) => code is >= HostBindingCodes.MinScancode and <= HostBindingCodes.MaxScancode;
    public static bool IsMouseOrWheelCode(int code) => code is >= HostBindingCodes.MouseLeft and <= HostBindingCodes.WheelDown;
    public static bool IsGamepadCode(int code) => code is >= HostBindingCodes.GamepadMin and <= HostBindingCodes.GamepadMax;
    public static bool IsValidBindingCode(int code) => IsKeyboardCode(code) || IsMouseOrWheelCode(code) || IsGamepadCode(code);
    public static int NormalizeCode(int code) => IsValidBindingCode(code) ? code : 0;
    public static int NormalizeGamepadCode(int code) => IsGamepadCode(code) ? code : 0;
    public static string FormatBindingLabel(int code) => HostInputSchemas.FormatBindingLabel(code);

    public static int MouseButtonIndexToBindingCode(int buttonIndex) => buttonIndex switch
    {
        1 => HostBindingCodes.MouseLeft, 2 => HostBindingCodes.MouseMiddle, 3 => HostBindingCodes.MouseRight,
        4 => HostBindingCodes.Mouse4, 5 => HostBindingCodes.Mouse5, _ => 0
    };

    private void ApplyBindingSection(Dictionary<string, JsonElement>? section, bool isGamepadSection)
    {
        if (section is null) return;
        foreach ((string actionId, JsonElement value) in section)
        {
            if (!_byId.TryGetValue(actionId, out ActionEntry? entry))
            {
                entry = new ActionEntry(new HostMultiBindSlot(actionId, actionId, 0, 0, 0, 0));
                _ordered.Add(entry);
                _byId[actionId] = entry;
            }
            int[] target = isGamepadSection ? entry.GamepadCodes : entry.Slots;
            Array.Clear(target);
            ReadCodesInto(value, target, isGamepadSection);
        }
    }

    private static void ReadCodesInto(JsonElement value, int[] dest, bool gamepadOnly)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int scalar))
        {
            dest[0] = gamepadOnly ? NormalizeGamepadCode(scalar) : NormalizeCode(scalar);
            return;
        }
        if (value.ValueKind != JsonValueKind.Array) return;
        int idx = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (idx >= dest.Length) break;
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int code))
            {
                dest[idx] = gamepadOnly ? NormalizeGamepadCode(code) : NormalizeCode(code);
            }
            idx++;
        }
    }

    private static int[] TrimTrailingZeros(int[] source)
    {
        int last = -1;
        for (int i = source.Length - 1; i >= 0; i--) { if (source[i] != 0) { last = i; break; } }
        return last < 0 ? Array.Empty<int>() : source[..(last + 1)];
    }

    private static bool IsEntryHeld(ActionEntry entry, IReadOnlySet<int> heldScancodes, int mouseButtons, float wheelDelta, uint gamepadMask)
    {
        for (int i = 0; i < 3; i++)
        {
            if (IsCodeHeld(entry.Slots[i], heldScancodes, mouseButtons, wheelDelta, gamepadMask) ||
                IsCodeHeld(entry.GamepadCodes[i], heldScancodes, mouseButtons, wheelDelta, gamepadMask)) return true;
        }
        return false;
    }

    private static bool IsEntryLatched(ActionEntry entry, IReadOnlySet<int> latchedCodes)
    {
        for (int i = 0; i < 3; i++)
        {
            if ((entry.Slots[i] > 0 && latchedCodes.Contains(entry.Slots[i])) ||
                (entry.GamepadCodes[i] > 0 && latchedCodes.Contains(entry.GamepadCodes[i]))) return true;
        }
        return false;
    }

    private static bool IsCodeHeld(int code, IReadOnlySet<int> heldScancodes, int mouseButtons, float wheelDelta, uint gamepadMask) => code switch
    {
        <= 0 => false,
        >= HostBindingCodes.MinScancode and <= HostBindingCodes.MaxScancode => heldScancodes.Contains(code),
        HostBindingCodes.MouseLeft => (mouseButtons & (1 << 0)) != 0,
        HostBindingCodes.MouseMiddle => (mouseButtons & (1 << 1)) != 0,
        HostBindingCodes.MouseRight => (mouseButtons & (1 << 2)) != 0,
        HostBindingCodes.Mouse4 => (mouseButtons & (1 << 3)) != 0,
        HostBindingCodes.Mouse5 => (mouseButtons & (1 << 4)) != 0,
        HostBindingCodes.WheelUp => wheelDelta > 0f,
        HostBindingCodes.WheelDown => wheelDelta < 0f,
        _ => Sdl3GamepadProvider.IsGamepadCodePressed(gamepadMask, code)
    };
}
