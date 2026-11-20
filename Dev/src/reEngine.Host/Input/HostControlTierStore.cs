// @module: reEngine.Host.Input.HostControlTierStore
// @purpose: Standard 33 3-tier control persistence (globalController, perGameController, perGameModern) for settings.controls.json with conflict validation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, reEngine.Host.Contracts
// @exports: HostControlTierMode, HostSlotConflict, HostControlTierStore

using System.Text.Json;
using System.Text.Json.Serialization;
using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Standard 33 three-tier input profile selector (<c>inputProfileMode</c>).
/// </summary>
public enum HostControlTierMode
{
    /// <summary>Tier 1: Global engine-wide 1:1 customizable controller and KB/M baseline.</summary>
    GlobalController = 0,

    /// <summary>Tier 2: Per-game 1:1 customizable console controller and KB/M bindings.</summary>
    PerGameController = 1,

    /// <summary>Tier 3: Per-game modern semantic PC KB/M + simultaneous gamepad bindings (default).</summary>
    PerGameModern = 2
}

/// <summary>
/// Describes a duplicate binding collision or hidden non-visible gamepad slot detected in a control tier.
/// </summary>
/// <param name="ActionId">Target action identifier.</param>
/// <param name="ConflictingActionId">Action identifier holding the conflicting binding.</param>
/// <param name="SlotIndex">Zero-based slot column (<c>0..2</c> KB/M, <c>3</c> visible gamepad, <c>4+</c> hidden gamepad).</param>
/// <param name="BindingCode">Conflicting Standard 18 integer binding code.</param>
/// <param name="Reason">Human-readable conflict explanation.</param>
public sealed record HostSlotConflict(
    string ActionId,
    string ConflictingActionId,
    int SlotIndex,
    int BindingCode,
    string Reason);

/// <summary>
/// Persists and validates all three Standard 33 rebindable control tiers in <c>settings.controls.json</c>.
/// </summary>
public sealed class HostControlTierStore
{
    private sealed class TierSectionDto
    {
        public Dictionary<string, JsonElement>? ModernActionBindings { get; set; }
        public Dictionary<string, JsonElement>? ModernControllerBindings { get; set; }
        public Dictionary<string, JsonElement>? KeyBindings { get; set; }
    }

    private sealed class ControlsPayloadDto
    {
        public int? SchemaVersion { get; set; }
        public string? AppId { get; set; }
        public string? Domain { get; set; }
        public string? InputProfileMode { get; set; }
        public string? ActiveSchema { get; set; }
        public TierSectionDto? GlobalController { get; set; }
        public TierSectionDto? PerGameController { get; set; }
        public TierSectionDto? PerGameModern { get; set; }
        public Dictionary<string, JsonElement>? ModernActionBindings { get; set; }
        public Dictionary<string, JsonElement>? ModernControllerBindings { get; set; }
        [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    /// <summary>Canonical configuration filename (<c>settings.controls.json</c>).</summary>
    public const string DefaultFileName = "settings.controls.json";

    private static readonly JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions JsonCompact = new() { WriteIndented = false };

    /// <summary>Initializes all three control tiers using defaults from <paramref name="initialSchema"/>.</summary>
    public HostControlTierStore(HostInputSchemaKind initialSchema = HostInputSchemaKind.SchemaB)
    {
        ActiveSchema = initialSchema;
        GlobalController = new HostMultiBindRegistry(initialSchema);
        PerGameController = new HostMultiBindRegistry(initialSchema);
        PerGameModern = new HostMultiBindRegistry(initialSchema);
    }

    /// <summary>Gets or sets the active Standard 33 input profile tier.</summary>
    public HostControlTierMode ActiveTier { get; set; } = HostControlTierMode.PerGameModern;

    /// <summary>Gets or sets the baseline schema used when resetting tier defaults.</summary>
    public HostInputSchemaKind ActiveSchema { get; set; }

    /// <summary>Gets or sets the active PC/console control mode (<c>controlMode</c>).</summary>
    public HostControlMode ControlMode { get; set; } = HostControlMode.ModernWasdMouse;

    /// <summary>Gets or sets mouse look sensitivity multiplier (<c>mouseSensitivity</c>).</summary>
    public double MouseSensitivity { get; set; } = 1.0;

    /// <summary>Gets or sets the controller chord opening the overlay menu (<c>menuControllerChord</c>).</summary>
    public HostMenuControllerChord MenuControllerChord { get; set; } = HostMenuControllerChord.L3PlusR3;

    /// <summary>Gets the rebindable host action bindings (<c>hostActionBindings</c>).</summary>
    public HostActionBindingSet HostActions { get; } = new();

    /// <summary>Gets Tier 1 (<c>globalController</c>) rebindable KB/M + Gamepad registry.</summary>
    public HostMultiBindRegistry GlobalController { get; }

    /// <summary>Gets Tier 2 (<c>perGameController</c>) rebindable KB/M + Gamepad registry.</summary>
    public HostMultiBindRegistry PerGameController { get; }

    /// <summary>Gets Tier 3 (<c>perGameModern</c>) rebindable KB/M + Gamepad registry.</summary>
    public HostMultiBindRegistry PerGameModern { get; }

    /// <summary>Gets the registry corresponding to <see cref="ActiveTier"/>.</summary>
    public HostMultiBindRegistry ActiveRegistry => GetRegistry(ActiveTier);

    /// <summary>Returns the <see cref="HostMultiBindRegistry"/> associated with <paramref name="tier"/>.</summary>
    public HostMultiBindRegistry GetRegistry(HostControlTierMode tier) => tier switch
    {
        HostControlTierMode.GlobalController => GlobalController,
        HostControlTierMode.PerGameController => PerGameController,
        _ => PerGameModern
    };

    /// <summary>Resets a single tier to conflict-free defaults for <paramref name="schema"/>.</summary>
    public void ResetTierToDefaults(HostControlTierMode tier, HostInputSchemaKind schema) =>
        GetRegistry(tier).ResetToSchemaDefaults(schema);

    /// <summary>Resets all three tiers to conflict-free defaults for <paramref name="schema"/>.</summary>
    public void ResetAllToDefaults(HostInputSchemaKind schema)
    {
        ActiveSchema = schema;
        GlobalController.ResetToSchemaDefaults(schema);
        PerGameController.ResetToSchemaDefaults(schema);
        PerGameModern.ResetToSchemaDefaults(schema);
    }

    /// <summary>Rebinds an action slot within <paramref name="tier"/> only if no duplicate/hidden conflict exists.</summary>
    public bool TryRebindAction(HostControlTierMode tier, string actionId, int columnIndex, int bindingCode, out HostSlotConflict? conflict) =>
        GetRegistry(tier).TrySetSlotBindingUnique(actionId, columnIndex, bindingCode, out conflict);

    /// <summary>Validates that no non-zero binding code collides across multiple slots in <paramref name="tier"/>.</summary>
    public IReadOnlyList<HostSlotConflict> ValidateUniqueSlotsInTier(HostControlTierMode tier) =>
        GetRegistry(tier).ValidateUniqueSlots();

    /// <summary>Detects hidden non-visible gamepad slots (<c>[1+]</c>) and collisions within <paramref name="tier"/>.</summary>
    public IReadOnlyList<HostSlotConflict> DetectHiddenSlotConflicts(HostControlTierMode tier) =>
        GetRegistry(tier).DetectHiddenSlotConflicts();

    /// <summary>Detects hidden non-visible gamepad slots (<c>[1+]</c>) and collisions across all three tiers.</summary>
    public IReadOnlyList<HostSlotConflict> DetectHiddenSlotConflicts()
    {
        var all = new List<HostSlotConflict>();
        all.AddRange(GlobalController.DetectHiddenSlotConflicts());
        all.AddRange(PerGameController.DetectHiddenSlotConflicts());
        all.AddRange(PerGameModern.DetectHiddenSlotConflicts());
        return all;
    }

    /// <summary>Clears any hidden <c>[1+]</c> gamepad slots across all three tiers and returns the count cleared.</summary>
    public int ClearHiddenGamepadSlots() =>
        GlobalController.ClearHiddenGamepadSlots() +
        PerGameController.ClearHiddenGamepadSlots() +
        PerGameModern.ClearHiddenGamepadSlots();

    /// <summary>Serializes all three Standard 33 control tiers into Standard 18 <c>settings.controls.json</c> format.</summary>
    public string SerializeJson(bool indented = true)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["appId"] = "reengine",
            ["domain"] = "controls",
            ["inputProfileMode"] = ToCanonicalTierId(ActiveTier),
            ["activeSchema"] = HostInputSchemas.ToShortCode(ActiveSchema),
            ["updatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["globalController"] = ExportSection(GlobalController),
            ["perGameController"] = ExportSection(PerGameController),
            ["perGameModern"] = ExportSection(PerGameModern)
        };
        return JsonSerializer.Serialize(payload, indented ? JsonIndented : JsonCompact);
    }

    /// <summary>Deserializes all three Standard 33 control tiers from JSON, preserving defaults for missing sections.</summary>
    public bool DeserializeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            ControlsPayloadDto? dto = JsonSerializer.Deserialize<ControlsPayloadDto>(json, JsonCaseInsensitive);
            if (dto is null) return false;
            if (!string.IsNullOrWhiteSpace(dto.InputProfileMode)) ActiveTier = ParseTierMode(dto.InputProfileMode);
            if (!string.IsNullOrWhiteSpace(dto.ActiveSchema)) ActiveSchema = HostInputSchemas.Parse(dto.ActiveSchema);

            ApplySection(GlobalController, dto.GlobalController);
            ApplySection(PerGameController, dto.PerGameController);
            ApplySection(PerGameModern, dto.PerGameModern);

            if (dto.ModernActionBindings is not null || dto.ModernControllerBindings is not null)
            {
                ActiveRegistry.ImportBindings(dto.ModernActionBindings, dto.ModernControllerBindings, keyBindings: null);
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Atomically writes <c>settings.controls.json</c> to <paramref name="filePath"/>.</summary>
    public void SaveToFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string full = Path.GetFullPath(filePath);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tempPath = full + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, SerializeJson(indented: true));
        File.Move(tempPath, full, overwrite: true);
    }

    /// <summary>Loads <c>settings.controls.json</c> from <paramref name="filePath"/> if present and valid.</summary>
    public bool LoadFromFile(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath) && DeserializeJson(File.ReadAllText(filePath));

    /// <summary>Saves <see cref="DefaultFileName"/> inside <paramref name="configDirectory"/>.</summary>
    public void SaveToConfigDirectory(string configDirectory) =>
        SaveToFile(Path.Combine(configDirectory, DefaultFileName));

    /// <summary>Loads <see cref="DefaultFileName"/> from <paramref name="configDirectory"/> if present and valid.</summary>
    public bool LoadFromConfigDirectory(string configDirectory) =>
        !string.IsNullOrWhiteSpace(configDirectory) && LoadFromFile(Path.Combine(configDirectory, DefaultFileName));

    /// <summary>Parses a Standard 18 <c>inputProfileMode</c> string or UI label into <see cref="HostControlTierMode"/>.</summary>
    public static HostControlTierMode ParseTierMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return HostControlTierMode.PerGameModern;
        return raw.Trim().ToLowerInvariant() switch
        {
            "globalcontroller" or "global" or "global 1:1" => HostControlTierMode.GlobalController,
            "pergamecontroller" or "game" or "game 1:1" => HostControlTierMode.PerGameController,
            _ => HostControlTierMode.PerGameModern
        };
    }

    /// <summary>Converts <paramref name="mode"/> to its canonical Standard 18 <c>inputProfileMode</c> string.</summary>
    public static string ToCanonicalTierId(HostControlTierMode mode) => mode switch
    {
        HostControlTierMode.GlobalController => "globalController",
        HostControlTierMode.PerGameController => "perGameController",
        _ => "perGameModern"
    };

    /// <summary>Formats <paramref name="mode"/> as a concise UI pill label.</summary>
    public static string ToDisplayLabel(HostControlTierMode mode) => mode switch
    {
        HostControlTierMode.GlobalController => "Global 1:1",
        HostControlTierMode.PerGameController => "Game 1:1",
        _ => "Modern PC"
    };

    /// <summary>Cycles to the next Standard 33 control tier.</summary>
    public static HostControlTierMode CycleNextTier(HostControlTierMode current) => current switch
    {
        HostControlTierMode.GlobalController => HostControlTierMode.PerGameController,
        HostControlTierMode.PerGameController => HostControlTierMode.PerGameModern,
        _ => HostControlTierMode.GlobalController
    };

    private static Dictionary<string, object> ExportSection(HostMultiBindRegistry registry)
    {
        registry.ExportBindings(out Dictionary<string, int[]> actionMap, out Dictionary<string, int[]> padMap);
        return new Dictionary<string, object>
        {
            ["modernActionBindings"] = actionMap,
            ["modernControllerBindings"] = padMap
        };
    }

    private static void ApplySection(HostMultiBindRegistry registry, TierSectionDto? section)
    {
        if (section is null) return;
        registry.ImportBindings(section.ModernActionBindings, section.ModernControllerBindings, section.KeyBindings);
    }
}
