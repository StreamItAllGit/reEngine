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
    /// <summary>Schema A: 3D RPG / Action-Adventure baseline.</summary>
    SchemaA = 0,

    /// <summary>Schema B: 2D / Top-Down RPG &amp; Simulation baseline.</summary>
    SchemaB = 1,

    /// <summary>Schema C: Platformer / Dual-Stick / C-Button baseline.</summary>
    SchemaC = 2
}

/// <summary>
/// Single action-to-input binding descriptor displayed in summary keybind tables.
/// </summary>
/// <param name="ActionName">Human-readable action label.</param>
/// <param name="PrimaryKey">Primary keyboard/mouse display label.</param>
/// <param name="AlternateInput">Alternate or gamepad display label.</param>
public sealed record HostKeybindEntry(string ActionName, string PrimaryKey, string AlternateInput);

/// <summary>
/// Provides normalized Schema A/B/C profiles, label formatting, legacy entries, scancode mapping, and 3-slot multi-bind defaults.
/// </summary>
public static class HostInputSchemas
{
    private static readonly Dictionary<int, string> SpecialCodeLabels = new()
    {
        [HostBindingCodes.MouseLeft] = "LClick", [HostBindingCodes.MouseRight] = "RClick", [HostBindingCodes.MouseMiddle] = "MClick",
        [HostBindingCodes.Mouse4] = "Mouse4", [HostBindingCodes.Mouse5] = "Mouse5",
        [HostBindingCodes.WheelUp] = "WheelUp", [HostBindingCodes.WheelDown] = "WheelDn",
        [39] = "0", [40] = "Enter", [41] = "Esc", [42] = "Bksp", [43] = "Tab", [44] = "Space",
        [47] = "[", [48] = "]", [53] = "Grave",
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

    private static readonly Dictionary<string, int> KeyNameToScancode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = HostScancodes.A, ["B"] = HostScancodes.B, ["C"] = HostScancodes.C, ["D"] = HostScancodes.D,
        ["E"] = HostScancodes.E, ["F"] = HostScancodes.F, ["G"] = HostScancodes.G, ["H"] = HostScancodes.H,
        ["I"] = HostScancodes.I, ["J"] = HostScancodes.J, ["K"] = HostScancodes.K, ["L"] = HostScancodes.L,
        ["M"] = HostScancodes.M, ["N"] = HostScancodes.N, ["O"] = HostScancodes.O, ["P"] = HostScancodes.P,
        ["Q"] = HostScancodes.Q, ["R"] = HostScancodes.R, ["S"] = HostScancodes.S, ["T"] = HostScancodes.T,
        ["U"] = HostScancodes.U, ["V"] = HostScancodes.V, ["W"] = HostScancodes.W, ["X"] = HostScancodes.X,
        ["Y"] = HostScancodes.Y, ["Z"] = HostScancodes.Z,
        ["0"] = HostScancodes.Num0, ["1"] = HostScancodes.Num1, ["2"] = HostScancodes.Num2, ["3"] = HostScancodes.Num3,
        ["4"] = HostScancodes.Num4, ["5"] = HostScancodes.Num5, ["6"] = HostScancodes.Num6, ["7"] = HostScancodes.Num7,
        ["8"] = HostScancodes.Num8, ["9"] = HostScancodes.Num9,
        ["UP"] = HostScancodes.Up, ["DOWN"] = HostScancodes.Down, ["LEFT"] = HostScancodes.Left, ["RIGHT"] = HostScancodes.Right,
        ["ENTER"] = HostScancodes.Return, ["RETURN"] = HostScancodes.Return, ["TAB"] = HostScancodes.Tab,
        ["SPACE"] = HostScancodes.Space, ["ESC"] = HostScancodes.Escape, ["ESCAPE"] = HostScancodes.Escape,
        ["DEL"] = HostScancodes.Delete, ["DELETE"] = HostScancodes.Delete, ["BACKSPACE"] = HostScancodes.Backspace,
        ["PGUP"] = HostScancodes.PageUp, ["PAGEUP"] = HostScancodes.PageUp,
        ["PGDN"] = HostScancodes.PageDown, ["PAGEDOWN"] = HostScancodes.PageDown,
        ["["] = HostScancodes.LeftBracket, ["LBRACKET"] = HostScancodes.LeftBracket,
        ["]"] = HostScancodes.RightBracket, ["RBRACKET"] = HostScancodes.RightBracket,
        ["GRAVE"] = HostScancodes.Grave, ["`"] = HostScancodes.Grave, ["TILDE"] = HostScancodes.Grave, ["OEM_3"] = HostScancodes.Grave,
        ["LCTRL"] = HostScancodes.LCtrl, ["CTRL"] = HostScancodes.LCtrl, ["CONTROL"] = HostScancodes.LCtrl, ["RCTRL"] = HostScancodes.RCtrl,
        ["LSHIFT"] = HostScancodes.LShift, ["SHIFT"] = HostScancodes.LShift, ["RSHIFT"] = HostScancodes.RShift,
        ["LALT"] = HostScancodes.LAlt, ["ALT"] = HostScancodes.LAlt, ["RALT"] = HostScancodes.RAlt,
        ["F1"] = HostScancodes.F1, ["F2"] = HostScancodes.F2, ["F3"] = HostScancodes.F3, ["F4"] = HostScancodes.F4,
        ["F5"] = HostScancodes.F5, ["F6"] = HostScancodes.F6, ["F7"] = HostScancodes.F7, ["F8"] = HostScancodes.F8,
        ["F9"] = HostScancodes.F9, ["F10"] = HostScancodes.F10, ["F11"] = HostScancodes.F11, ["F12"] = HostScancodes.F12,
        ["GUIDE"] = HostBindingCodes.GamepadGuide
    };

    /// <summary>Formats an expressive control profile label without exposing raw Schema A/B/C identifiers.</summary>
    public static string FormatPresetLabel(HostInputSchemaKind kind) => kind switch
    {
        HostInputSchemaKind.SchemaA => "3D Action / Adventure",
        HostInputSchemaKind.SchemaC => "Platformer / Dual-Stick",
        _ => "2D RPG / Simulation"
    };

    /// <summary>Parses a schema identifier string into <see cref="HostInputSchemaKind"/>.</summary>
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

    /// <summary>Formats <paramref name="kind"/> as its canonical single-letter short code (<c>A</c>, <c>B</c>, or <c>C</c>).</summary>
    public static string ToShortCode(HostInputSchemaKind kind) => kind switch
    {
        HostInputSchemaKind.SchemaA => "A",
        HostInputSchemaKind.SchemaC => "C",
        _ => "B"
    };

    /// <summary>Cycles to the next schema in order (<c>A -> B -> C -> A</c>).</summary>
    public static HostInputSchemaKind CycleNext(HostInputSchemaKind current) => current switch
    {
        HostInputSchemaKind.SchemaA => HostInputSchemaKind.SchemaB,
        HostInputSchemaKind.SchemaB => HostInputSchemaKind.SchemaC,
        _ => HostInputSchemaKind.SchemaA
    };

    /// <summary>Translates a key name token (e.g., <c>"F5"</c>, <c>"Grave"</c>, <c>"Enter"</c>) to its SDL3 scancode.</summary>
    public static bool TryMapKeyNameToScancode(string? keyName, out int scancode)
    {
        scancode = 0;
        return !string.IsNullOrWhiteSpace(keyName) && KeyNameToScancode.TryGetValue(keyName.Trim(), out scancode);
    }

    /// <summary>Formats an integer Standard 18 binding code into a concise human-readable UI badge.</summary>
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

    /// <summary>Returns summary keybind entries for the requested schema.</summary>
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
            new("Face (S/E/W)", "Z / X / C", "Gamepad A/B/X/Y"),
            new("Shoulders / Triggers", "Q / E / 1 / 3", "LB / RB / LT / RT"),
            new("Drawer / HUD / Shot", "F1 / F3 / F12", "Host Overlay")
        ]
    };

    /// <summary>Returns conflict-free default 3-slot + gamepad bindings for <paramref name="schema"/>.</summary>
    public static IReadOnlyList<HostMultiBindSlot> GetDefaultMultiBindSlots(HostInputSchemaKind schema)
    {
        bool arrowsOnMove = schema != HostInputSchemaKind.SchemaC;
        return
        [
            new("moveUp", "Move Up", HostScancodes.W, arrowsOnMove ? HostScancodes.Up : 0, 0, HostBindingCodes.GamepadDPadUp),
            new("moveDown", "Move Down", HostScancodes.S, arrowsOnMove ? HostScancodes.Down : 0, 0, HostBindingCodes.GamepadDPadDown),
            new("moveLeft", "Move Left", HostScancodes.A, arrowsOnMove ? HostScancodes.Left : 0, 0, HostBindingCodes.GamepadDPadLeft),
            new("moveRight", "Move Right", HostScancodes.D, arrowsOnMove ? HostScancodes.Right : 0, 0, HostBindingCodes.GamepadDPadRight),
            new("south", "South / Accept", schema == HostInputSchemaKind.SchemaC ? HostScancodes.K : HostScancodes.Z, 0, 0, HostBindingCodes.GamepadSouth),
            new("east", "East / Cancel", schema == HostInputSchemaKind.SchemaC ? 0 : HostScancodes.X, 0, 0, HostBindingCodes.GamepadEast),
            new("west", "West / Action", schema switch { HostInputSchemaKind.SchemaA => 0, HostInputSchemaKind.SchemaC => HostScancodes.L, _ => HostScancodes.C }, 0, 0, HostBindingCodes.GamepadWest),
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
            new("camRight", "Camera Right", schema == HostInputSchemaKind.SchemaC ? HostScancodes.Right : 0, 0, 0, 0)
        ];
    }
}

internal static class HostScancodes
{
    public const int A = 4, B = 5, C = 6, D = 7, E = 8, F = 9, G = 10, H = 11, I = 12, J = 13, K = 14, L = 15, M = 16;
    public const int N = 17, O = 18, P = 19, Q = 20, R = 21, S = 22, T = 23, U = 24, V = 25, W = 26, X = 27, Y = 28, Z = 29;
    public const int Num1 = 30, Num2 = 31, Num3 = 32, Num4 = 33, Num5 = 34, Num6 = 35, Num7 = 36, Num8 = 37, Num9 = 38, Num0 = 39;
    public const int Return = 40, Escape = 41, Backspace = 42, Tab = 43, Space = 44;
    public const int LeftBracket = 47, RightBracket = 48, Grave = 53;
    public const int F1 = 58, F2 = 59, F3 = 60, F4 = 61, F5 = 62, F6 = 63, F7 = 64, F8 = 65, F9 = 66, F10 = 67, F11 = 68, F12 = 69;
    public const int PageUp = 75, Delete = 76, PageDown = 78;
    public const int Right = 79, Left = 80, Down = 81, Up = 82;
    public const int LCtrl = 224, LShift = 225, LAlt = 226, RCtrl = 228, RShift = 229, RAlt = 230;
}
