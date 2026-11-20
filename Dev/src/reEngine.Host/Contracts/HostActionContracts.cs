// @module: reEngine.Host.Contracts.HostActionContracts
// @purpose: Shared v0.6.1 input mode, controller menu chord, and rebindable host action contracts across Input and UI slices.
// @depends-on: System, System.Collections.Generic, System.Text.Json
// @exports: HostControlMode, HostControlModeCodec, HostMenuControllerChord, HostMenuControllerChordCodec, HostActionBindingIds, IHostActionBindingSet

using System.Text.Json;

namespace reEngine.Host.Contracts;

/// <summary>Primary PC/console camera and movement mode (Standard 18: <c>controlMode</c>).</summary>
public enum HostControlMode
{
    ModernWasdMouse = 0,
    ClassicTank = 1
}

/// <summary>Converts and formats <see cref="HostControlMode"/> values per Standard 18.</summary>
public static class HostControlModeCodec
{
    public static HostControlMode Parse(string? raw) =>
        string.Equals(raw?.Trim(), "classicTank", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(raw?.Trim(), "classic", StringComparison.OrdinalIgnoreCase)
            ? HostControlMode.ClassicTank
            : HostControlMode.ModernWasdMouse;

    public static string ToCanonicalId(HostControlMode mode) =>
        mode == HostControlMode.ClassicTank ? "classicTank" : "modernWasdMouse";

    public static string ToDisplayLabel(HostControlMode mode) =>
        mode == HostControlMode.ClassicTank ? "Classic Tank" : "Modern WASD + Mouse";
}

/// <summary>Customizable controller chord opening the overlay menu (Standard 18: <c>menuControllerChord</c>).</summary>
public enum HostMenuControllerChord
{
    L3PlusR3 = 0,
    SelectPlusStart = 1,
    Guide = 2,
    None = 3
}

/// <summary>Converts, cycles, and evaluates <see cref="HostMenuControllerChord"/> values.</summary>
public static class HostMenuControllerChordCodec
{
    public static HostMenuControllerChord Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return HostMenuControllerChord.L3PlusR3;
        return raw.Trim().ToLowerInvariant() switch
        {
            "select+start" or "selectstart" => HostMenuControllerChord.SelectPlusStart,
            "guide" => HostMenuControllerChord.Guide,
            "none" or "off" => HostMenuControllerChord.None,
            _ => HostMenuControllerChord.L3PlusR3
        };
    }

    public static string ToCanonicalId(HostMenuControllerChord chord) => chord switch
    {
        HostMenuControllerChord.SelectPlusStart => "select+start",
        HostMenuControllerChord.Guide => "guide",
        HostMenuControllerChord.None => "none",
        _ => "l3+r3"
    };

    public static string ToDisplayLabel(HostMenuControllerChord chord) => chord switch
    {
        HostMenuControllerChord.SelectPlusStart => "Select + Start",
        HostMenuControllerChord.Guide => "Guide",
        HostMenuControllerChord.None => "None",
        _ => "L3 + R3"
    };

    public static HostMenuControllerChord CycleNext(HostMenuControllerChord current) =>
        (HostMenuControllerChord)(((int)current + 1) % 4);
}

/// <summary>Canonical Standard 18 action keys stored inside <c>hostActionBindings</c>.</summary>
public static class HostActionBindingIds
{
    public const string ToggleMenu = "toggleMenu";
    public const string ToggleHud = "toggleHud";
    public const string QuickSave = "quickSave";
    public const string FastForward = "fastForward";
    public const string CutsceneSkip = "cutsceneSkip";
    public const string QuickLoad = "quickLoad";
    public const string ToggleFullscreen = "toggleFullscreen";
    public const string TakeScreenshot = "takeScreenshot";

    public static readonly IReadOnlyList<string> All =
    [
        ToggleMenu,
        ToggleHud,
        QuickSave,
        QuickLoad,
        FastForward,
        CutsceneSkip,
        ToggleFullscreen,
        TakeScreenshot
    ];
}

/// <summary>
/// Cross-slice contract for rebindable host recomp actions (<c>hostActionBindings</c>).
/// </summary>
public interface IHostActionBindingSet
{
    IReadOnlyList<HostMultiBindSlot> Slots { get; }
    bool ContainsAction(string actionId);
    bool TryGetSlot(string actionId, out HostMultiBindSlot slot);
    bool TrySetSlotBindingUnique(string actionId, int columnIndex, int code, out string? conflictReason);
    bool ClearSlotBinding(string actionId, int columnIndex);
    void ResetToDefaults();
    bool IsCodeBound(int code);
    string FormatSummaryLabel(string actionId);
    void ExportJsonMap(Dictionary<string, int[]> destination);
    void ImportJsonMap(Dictionary<string, JsonElement>? source);
}
