// @module: reEngine.Host.Input.HostInputSchemas
// @purpose: Console-agnostic Schema A/B/C input binding definitions and default keybind descriptors for reEngine.Host.
// @depends-on: System, System.Collections.Generic
// @exports: HostInputSchemaKind, HostKeybindEntry, HostInputSchemas

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
/// Single action-to-input binding descriptor displayed in the F1 Keybinds tab.
/// </summary>
public sealed record HostKeybindEntry(string ActionName, string PrimaryKey, string AlternateInput);

/// <summary>
/// Provides normalized Schema A/B/C profiles and human-readable keybind tables.
/// </summary>
public static class HostInputSchemas
{
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
}
