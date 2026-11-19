// @module: reEngine.Host.Input.HostInputSchemas
// @purpose: Console-agnostic Schema A/B/C input binding definitions, scancodes, formatting, and default 3-slot multi-bind profiles.
// @depends-on: System, System.Collections.Generic, reEngine.Host.Contracts
// @exports: HostInputSchemaKind, HostKeybindEntry, HostInputSchemas

using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Standardized multi-generation host input layout schemas.
/// </summary>
public enum HostInputSchemaKind
{
    SchemaA = 0,
    SchemaB = 1,
    SchemaC = 2
}

/// <summary>
/// Single action-to-input binding descriptor displayed in summary keybind tables.
/// </summary>
public sealed record HostKeybindEntry(string ActionName, string PrimaryKey, string AlternateInput);

/// <summary>
/// Provides normalized Schema A/B/C profiles, label formatting, legacy entries, and 3-slot multi-bind defaults.
/// </summary>
public static class HostInputSchemas
{
    private static readonly Dictionary<int, string> SpecialCodeLabels = new()
    {
        [HostBindingCodes.MouseLeft] = "LClick", [HostBindingCodes.MouseRight] = "RClick", [HostBindingCodes.MouseMiddle] = "MClick",
        [HostBindingCodes.Mouse4] = "Mouse4", [HostBindingCodes.Mouse5] = "Mouse5",
        [HostBindingCodes.WheelUp] = "WheelUp", [HostBindingCodes.WheelDown] = "WheelDn",
        [39] = "0", [40] = "Enter", [41] = "Esc", [42] = "Bksp", [43] = "Tab", [44] = "Space",
        [75] = "PgUp", [76] = "Del", [78] = "PgDn", [79] = "Right", [80] = "Left", [81] = "Down", [82] = "Up",
        [224] = "LCtrl", [225] = "LShift", [226] = "LAlt", [228] = "RCtrl", [229] = "RShift", [230] = "RAlt",
        [HostBindingCodes.GamepadSouth] = "Pad A", [HostBindingCodes.GamepadEast] = "Pad B",
        [HostBindingCodes.GamepadWest] = "Pad X", [HostBindingCodes.GamepadNorth] = "Pad Y",
        [HostBindingCodes.GamepadSelect] = "Back", [2006] = "Guide", [HostBindingCodes.GamepadStart] = "Start",
        [HostBindingCodes.GamepadL3] = "L3", [HostBindingCodes.GamepadR3] = "R3",
        [HostBindingCodes.GamepadLB] = "LB", [HostBindingCodes.GamepadRB] = "RB",
        [HostBindingCodes.GamepadDPadUp] = "D-Up", [HostBindingCodes.GamepadDPadDown] = "D-Dn",
        [HostBindingCodes.GamepadDPadLeft] = "D-Lt", [HostBindingCodes.GamepadDPadRight] = "D-Rt",
        [HostBindingCodes.GamepadLT] = "LT", [HostBindingCodes.GamepadRT] = "RT", [2020] = "Touch"
    };

    public static HostInputSchemaKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return HostInputSchemaKind.SchemaB;
        return raw.Trim().ToUpperInvariant() switch
        {
            "A" or "SCHEMAA" => HostInputSchemaKind.SchemaA,
            "C" or "SCHEMAC" => HostInputSchemaKind.SchemaC,
            _ => HostInputSchemaKind.SchemaB
        };
    }

    public static string ToShortCode(HostInputSchemaKind kind) => kind switch
    {
        HostInputSchemaKind.SchemaA => "A",
        HostInputSchemaKind.SchemaC => "C",
        _ => "B"
    };

    public static HostInputSchemaKind CycleNext(HostInputSchemaKind current) => current switch
    {
        HostInputSchemaKind.SchemaA => HostInputSchemaKind.SchemaB,
        HostInputSchemaKind.SchemaB => HostInputSchemaKind.SchemaC,
        _ => HostInputSchemaKind.SchemaA
    };

    public static string FormatBindingLabel(int code)
    {
        if (code <= 0) return "---";
        if (SpecialCodeLabels.TryGetValue(code, out string? label)) return label;
        if (code is >= 4 and <= 29) return ((char)('A' + code - 4)).ToString();
        if (code is >= 30 and <= 38) return ((char)('1' + code - 30)).ToString();
        if (code is >= 58 and <= 69) return $"F{code - 57}";
        if (code is >= HostBindingCodes.GamepadMin and <= HostBindingCodes.GamepadMax) return $"Pad#{code - HostBindingCodes.GamepadMin}";
        return $"Key#{code}";
    }

    public static IReadOnlyList<HostKeybindEntry> GetBindings(HostInputSchemaKind schema) => schema switch
    {
        HostInputSchemaKind.SchemaA =>
        [
            new("D-Pad / Move", "Arrows / WASD", "Gamepad D-Pad"),
            new("Action South / East", "Z / X", "Gamepad A / B"),
            new("Start / Select", "Enter / Tab", "Start / Back"),
            new("Toggle Drawer / HUD", "F1 / F3", "L3 + R3")
        ],
        HostInputSchemaKind.SchemaC =>
        [
            new("Analog Stick", "WASD", "Left Stick"),
            new("C-Buttons / Aux", "Arrows / IJKL", "Right Stick"),
            new("A / B / Z Trigger", "K / L / Space", "South / West / LT"),
            new("Shoulders (L/R)", "Q / E", "LB / RB")
        ],
        _ =>
        [
            new("Move / Analog", "WASD / Arrows", "Left Stick"),
            new("Face (S/E/W/N)", "Z / X / A / S", "Gamepad A/B/X/Y"),
            new("Shoulders / Triggers", "Q / E / 1 / 3", "LB / RB / LT / RT"),
            new("Drawer / HUD / Shot", "F1 / F3 / F12", "Host Overlay")
        ]
    };

    public static IReadOnlyList<HostMultiBindSlot> GetDefaultMultiBindSlots(HostInputSchemaKind schema) =>
    [
        new("moveUp", "Move Up", HostScancodes.W, HostScancodes.Up, 0, HostBindingCodes.GamepadDPadUp),
        new("moveDown", "Move Down", HostScancodes.S, HostScancodes.Down, 0, HostBindingCodes.GamepadDPadDown),
        new("moveLeft", "Move Left", HostScancodes.A, HostScancodes.Left, 0, HostBindingCodes.GamepadDPadLeft),
        new("moveRight", "Move Right", HostScancodes.D, HostScancodes.Right, 0, HostBindingCodes.GamepadDPadRight),
        new("south", "South / Accept", schema == HostInputSchemaKind.SchemaC ? HostScancodes.K : HostScancodes.Z, 0, 0, HostBindingCodes.GamepadSouth),
        new("east", "East / Cancel", schema == HostInputSchemaKind.SchemaC ? 0 : HostScancodes.X, 0, 0, HostBindingCodes.GamepadEast),
        new("west", "West / Action", schema switch { HostInputSchemaKind.SchemaA => 0, HostInputSchemaKind.SchemaC => HostScancodes.L, _ => HostScancodes.A }, 0, 0, HostBindingCodes.GamepadWest),
        new("north", "North / Aux", 0, 0, 0, HostBindingCodes.GamepadNorth),
        new("leftShoulder", "Left Bumper", schema == HostInputSchemaKind.SchemaA ? 0 : HostScancodes.Q, 0, 0, HostBindingCodes.GamepadLB),
        new("rightShoulder", "Right Bumper", schema == HostInputSchemaKind.SchemaA ? 0 : HostScancodes.E, 0, 0, HostBindingCodes.GamepadRB),
        new("leftTrigger", "Left Trigger", schema switch { HostInputSchemaKind.SchemaA => 0, HostInputSchemaKind.SchemaC => HostScancodes.Space, _ => HostScancodes.Num1 }, 0, 0, HostBindingCodes.GamepadLT),
        new("rightTrigger", "Right Trigger", schema == HostInputSchemaKind.SchemaB ? HostScancodes.Num3 : 0, 0, 0, HostBindingCodes.GamepadRT),
        new("start", "Start / Menu", HostScancodes.Return, 0, 0, HostBindingCodes.GamepadStart),
        new("select", "Select / Map", HostScancodes.Tab, 0, 0, HostBindingCodes.GamepadSelect),
        new("leftStickClick", "Stick L3", 0, 0, 0, HostBindingCodes.GamepadL3),
        new("rightStickClick", "Stick R3", 0, 0, 0, HostBindingCodes.GamepadR3),
        new("camUp", "Camera Up", schema == HostInputSchemaKind.SchemaC ? HostScancodes.Up : 0, 0, 0, 0),
        new("camDown", "Camera Down", schema == HostInputSchemaKind.SchemaC ? HostScancodes.Down : 0, 0, 0, 0),
        new("camLeft", "Camera Left", schema == HostInputSchemaKind.SchemaC ? HostScancodes.Left : 0, 0, 0, 0),
        new("camRight", "Camera Right", schema == HostInputSchemaKind.SchemaC ? HostScancodes.Right : 0, 0, 0, 0),
        new("quickSave", "Quick Save", HostScancodes.F5, 0, 0, 0),
        new("quickLoad", "Quick Load", HostScancodes.F9, 0, 0, 0)
    ];
}

internal static class HostScancodes
{
    public const int A = 4, B = 5, C = 6, D = 7, E = 8, F = 9, G = 10, H = 11, I = 12, J = 13, K = 14, L = 15, M = 16;
    public const int N = 17, P = 19, Q = 20, R = 21, S = 22, W = 26, X = 27, Z = 29;
    public const int Num1 = 30, Num3 = 32;
    public const int Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44;
    public const int F1 = 58, F3 = 60, F5 = 62, F9 = 66, F12 = 69;
    public const int PageUp = 75, Delete = 76, PageDown = 78;
    public const int Right = 79, Left = 80, Down = 81, Up = 82;
    public const int LCtrl = 224, LShift = 225, LAlt = 226, RCtrl = 228, RShift = 229, RAlt = 230;
}
