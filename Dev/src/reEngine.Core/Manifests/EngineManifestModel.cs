// @module: reEngine.Core.Manifests.EngineManifestModel
// @purpose: Standard 18 engine.manifest.json schema model for installed side-by-side platform engines.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: EngineManifestModel

using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Manifests;

/// <summary>
/// Canonical Standard 18 schema for an installed engine's <c>engine.manifest.json</c> descriptor.
/// </summary>
public sealed class EngineManifestModel
{
    /// <summary>Manifest schema version (currently 1).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical platform engine identifier (e.g., <c>reStation</c> or coded alias <c>reSlate</c>).</summary>
    [JsonPropertyName("engineId")]
    public string EngineId { get; set; } = string.Empty;

    /// <summary>Installed engine display name.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>Installed engine semantic version string.</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>Alternate engine semantic version property accepted on v0.2+/v0.3 manifests.</summary>
    [JsonPropertyName("engineVersion")]
    public string? EngineVersion { get; set; }

    /// <summary>Primary engine track selector (e.g., <c>0.3.x</c>).</summary>
    [JsonPropertyName("compatibleEngineTrack")]
    public string? CompatibleEngineTrack { get; set; }

    /// <summary>Ordered compatibility tracks supported by this engine build.</summary>
    [JsonPropertyName("supportedEngineTracks")]
    public List<string>? SupportedEngineTracks { get; set; }

    /// <summary>Target console/platform slug (e.g., <c>psx</c>, <c>nes</c>, <c>n64</c>).</summary>
    [JsonPropertyName("platformId")]
    public string? PlatformId { get; set; }

    /// <summary>Relative path to the primary engine entry executable.</summary>
    [JsonPropertyName("entryExecutable")]
    public string? EntryExecutable { get; set; }

    /// <summary>Relative path to the <c>IDecompPlatformProvider</c> assembly for <c>D0</c>–<c>D5</c> dispatch.</summary>
    [JsonPropertyName("decompProviderAssembly")]
    public string? DecompProviderAssembly { get; set; }

    /// <summary>Preferred execution tier mode (<c>aotPrimary</c>, <c>jitBringup</c>, <c>threadedIr</c>).</summary>
    [JsonPropertyName("executionTierMode")]
    public string? ExecutionTierMode { get; set; }

    /// <summary>Default render backend identifier (<c>opengl</c>, <c>vulkan</c>, <c>parallelCpu</c>, <c>software</c>).</summary>
    [JsonPropertyName("backendId")]
    public string? BackendId { get; set; }

    /// <summary>Default pass-through resolution scale factor (1..8).</summary>
    [JsonPropertyName("resolutionScale")]
    public int? ResolutionScale { get; set; }

    /// <summary>Forward-compatible unknown JSON properties preserved across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Resolves the effective semantic version string regardless of legacy field spelling.</summary>
    [JsonIgnore]
    public string? EffectiveVersion =>
        !string.IsNullOrWhiteSpace(EngineVersion) ? EngineVersion : Version;
}
