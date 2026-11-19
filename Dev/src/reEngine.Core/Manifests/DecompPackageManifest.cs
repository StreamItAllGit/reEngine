// @module: reEngine.Core.Manifests.DecompPackageManifest
// @purpose: Standard 18 .decomp package manifest model and platform decompilation provider contract.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization, System.Threading, System.Threading.Tasks, Foundation.Retro, reEngine.Core.Decomp
// @exports: DecompToolchainRequirements, DecompPackageManifest, DecompCleanRoomValidationResult, IDecompPlatformProvider

using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Retro;
using reEngine.Core.Decomp;

namespace reEngine.Core.Manifests;

/// <summary>
/// Toolchain prerequisites declared by a <c>.decomp</c> package manifest.
/// </summary>
public sealed class DecompToolchainRequirements
{
    /// <summary>Minimum headless Ghidra version required (e.g., <c>11.2</c>).</summary>
    [JsonPropertyName("minimumGhidraVersion")]
    public string MinimumGhidraVersion { get; set; } = "11.2";

    /// <summary>Whether an offline <c>jlink</c> OpenJDK runtime is required.</summary>
    [JsonPropertyName("requiresHeadlessJdk")]
    public bool RequiresHeadlessJdk { get; set; } = true;

    /// <summary>Optional platform-specific analyzer or loader extension identifiers.</summary>
    [JsonPropertyName("requiredExtensions")]
    public List<string> RequiredExtensions { get; set; } = new();
}

/// <summary>
/// Clean-room <c>.decomp</c> package manifest implementing <see cref="IDecompPackageManifest"/>.
/// </summary>
public sealed class DecompPackageManifest : IDecompPackageManifest
{
    /// <summary>Canonical lowercase-kebab-case module identifier.</summary>
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>Human-facing module display title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Target console/platform identifier (e.g., <c>psx</c>, <c>nes</c>, <c>n64</c>).</summary>
    [JsonPropertyName("platformId")]
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>Target platform engine identifier (e.g., <c>reStation</c>, <c>reNes</c>, <c>re64</c>).</summary>
    [JsonPropertyName("engineId")]
    public string EngineId { get; set; } = string.Empty;

    /// <summary>Minimum compatible engine SemVer required by this package.</summary>
    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = "0.1.0";

    /// <summary>Compatible engine track selector (e.g., <c>0.2.x</c>, <c>0.1.x</c>).</summary>
    [JsonPropertyName("compatibleEngineTrack")]
    public string CompatibleEngineTrack { get; set; } = string.Empty;

    /// <summary>Ordered list of compatible engine tracks supported by this decompilation package.</summary>
    [JsonPropertyName("supportedEngineTracks")]
    public List<string>? SupportedEngineTracks { get; set; }

    /// <summary>Optional relative assembly path or filename providing <see cref="IDecompPlatformProvider"/>.</summary>
    [JsonPropertyName("decompProviderAssembly")]
    public string? DecompProviderAssembly { get; set; }

    /// <summary>Preserved earned research tiers present in upstream <c>Corpus\tiers\</c> (<c>D1_Raw</c>, <c>D2_Structured</c>, <c>D3_Mapped</c>, canonical <c>D4_CleanSource</c> with <c>D4_FunctionalRewrite</c> legacy alias, <c>D5_ByteExact</c>).</summary>
    [JsonPropertyName("preservedTiers")]
    public List<string>? PreservedTiers { get; set; }

    /// <summary>Standard 18 D4 parity level (<c>none</c>, <c>D4a</c> subsystem parity, or <c>D4b</c> execution parity).</summary>
    [JsonPropertyName("d4ParityTier")]
    public string? D4ParityTier { get; set; }

    /// <summary>Standard 18 separate port/reimplementation axis tier (<c>none</c>, <c>P0</c>–<c>P5</c>).</summary>
    [JsonPropertyName("currentPortTier")]
    public string? CurrentPortTier { get; set; }

    /// <summary>Optional expected 64-char hex SHA-256 of verified user media for suite-tree disambiguation.</summary>
    [JsonPropertyName("expectedSha256")]
    public string? ExpectedSha256 { get; set; }

    /// <summary>Standard 18 SemVer of the <c>.decomp</c> container schema.</summary>
    [JsonPropertyName("decompSchemaVersion")]
    public string DecompSchemaVersion { get; set; } = "1.0.0";

    /// <summary>Target decompilation readiness tier (<c>none</c>, <c>D0</c>–<c>D5</c>).</summary>
    [JsonPropertyName("targetDecompTier")]
    public string TargetDecompTier { get; set; } = "D3";

    /// <summary>Goal decompilation tier (<c>none</c>, <c>D0</c>–<c>D5</c>).</summary>
    [JsonPropertyName("goalDecompTier")]
    public string GoalDecompTier { get; set; } = "D3";

    /// <summary>Optional semantic version of this <c>.decomp</c> package.</summary>
    [JsonPropertyName("packageVersion")]
    public string? PackageVersion { get; set; }

    /// <summary>Optional read-only upstream Tier R research repository path or identifier.</summary>
    [JsonPropertyName("upstreamResearchPath")]
    public string? UpstreamResearchPath { get; set; }

    /// <summary>Optional Ghidra project name used during offline headless analysis.</summary>
    [JsonPropertyName("ghidraProjectName")]
    public string? GhidraProjectName { get; set; }

    /// <summary>Offline toolchain requirements for executing <c>D0</c>–<c>D5</c> stages.</summary>
    [JsonPropertyName("toolchainRequirements")]
    public DecompToolchainRequirements? ToolchainRequirements { get; set; }

    /// <summary>Relative preload media directory inside the module root (defaults to <c>data/rom</c>).</summary>
    [JsonPropertyName("preloadRomDir")]
    public string PreloadRomDir { get; set; } = "data/rom";

    /// <summary>Optional clean-room recipe or script entrypoint relative to the package root.</summary>
    [JsonPropertyName("recipeEntryPoint")]
    public string? RecipeEntryPoint { get; set; }

    /// <summary>Forward-compatible unknown JSON properties preserved across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Result of validating that a decompilation output directory contains only clean-room artifacts.
/// </summary>
public sealed record DecompCleanRoomValidationResult(
    bool IsValid,
    IReadOnlyList<string> Violations,
    string Summary);

/// <summary>
/// Platform engine provider contract discovered under installed <c>engines\re&lt;Platform&gt;\v&lt;Ver&gt;\</c> directories.
/// </summary>
public interface IDecompPlatformProvider
{
    /// <summary>Canonical platform identifier handled by this provider (e.g., <c>psx</c>, <c>nes</c>).</summary>
    string PlatformId { get; }

    /// <summary>Human-readable display name of the platform provider.</summary>
    string DisplayName { get; }

    /// <summary>Minimum engine SemVer required to host this provider.</summary>
    string MinimumEngineVersion { get; }

    /// <summary>Executes the requested <c>D0</c>–<c>D5</c> decompilation pipeline stage.</summary>
    Task<DecompStageResult> RunDecompAsync(DecompRunOptions options, CancellationToken ct = default);

    /// <summary>Validates that generated outputs are clean-room compliant (zero retail ROM/BIOS/save-state dumps).</summary>
    DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory);
}
