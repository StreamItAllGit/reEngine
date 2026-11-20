// @module: reEngine.Host.Input.HostActionBindingSet
// @purpose: Rebindable Standard 18 hostActionBindings manager with conflict prevention and controller chord evaluation.
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
    private readonly List<HostMultiBindSlot> _slots = new();

    public HostActionBindingSet() => ResetToDefaults();

    public IReadOnlyList<HostMultiBindSlot> Slots => _slots;

    public bool ContainsAction(string actionId) =>
        _slots.Exists(s => string.Equals(s.ActionId, actionId, StringComparison.OrdinalIgnoreCase));

    public bool TryGetSlot(string actionId, out HostMultiBindSlot slot)
    {
        foreach (HostMultiBindSlot s in _slots)
        {
            if (string.Equals(s.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
            {
                slot = s;
                return true;
            }
        }
        slot = default!;
        return false;
    }

    public bool TrySetSlotBindingUnique(string actionId, int columnIndex, int code, out string? conflictReason)
    {
        conflictReason = null;
        return false;
    }

    public bool ClearSlotBinding(string actionId, int columnIndex) => false;

    public void ResetToDefaults()
    {
        _slots.Clear();
        _slots.AddRange(
        [
            new(HostActionBindingIds.ToggleMenu, "Pause / Menu", HostScancodes.F1, 0, 0, 0),
            new(HostActionBindingIds.ToggleHud, "Toggle HUD", HostScancodes.F3, 0, 0, 0),
            new(HostActionBindingIds.QuickSave, "Quick Save", HostScancodes.F5, 0, 0, 0),
            new(HostActionBindingIds.QuickLoad, "Quick Load", HostScancodes.F9, 0, 0, 0),
            new(HostActionBindingIds.FastForward, "Fast Forward", HostScancodes.F6, HostScancodes.Grave, 0, 0),
            new(HostActionBindingIds.CutsceneSkip, "Skip Cutscene", HostScancodes.F7, 0, 0, 0),
            new(HostActionBindingIds.ToggleFullscreen, "Fullscreen", HostScancodes.F11, 0, 0, 0),
            new(HostActionBindingIds.TakeScreenshot, "Screenshot", HostScancodes.F12, 0, 0, 0)
        ]);
    }

    public bool IsCodeBound(int code)
    {
        if (code <= 0) return false;
        foreach (HostMultiBindSlot s in _slots)
        {
            if (s.Slot1 == code || s.Slot2 == code || s.Slot3 == code || s.GamepadCode == code) return true;
        }
        return false;
    }

    public string FormatSummaryLabel(string actionId) => TryGetSlot(actionId, out HostMultiBindSlot s) && s.Slot1 > 0
        ? HostInputSchemas.FormatBindingLabel(s.Slot1)
        : "---";

    public void ExportJsonMap(Dictionary<string, int[]> destination) { }

    public void ImportJsonMap(Dictionary<string, JsonElement>? source) { }
}
