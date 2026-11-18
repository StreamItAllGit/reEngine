// @module: reEngine.Core.Updates.UpdateModels
// @purpose: Standard 18/19 settings schema, update feed contracts, resumable .part metadata, and multi-engine sweep records.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: ReEngineSettings, UpdateFeedEntry, UpdateFeedManifest, UpdatePartMeta, UpdateCheckOutcome, MultiEngineUpdateSweepResult

using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Updates;

/// <summary>
/// Persisted <c>data\settings.json</c> schema following Standard 18 &amp; Standard 19.
/// </summary>
public sealed class ReEngineSettings
{
    /// <summary>Settings schema version (currently 1).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical application identifier.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "reengine";

    /// <summary>ISO-8601 UTC timestamp of the last save.</summary>
    [JsonPropertyName("updatedAtUtc")]
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Whether launch-time compatible update checks are enabled.</summary>
    [JsonPropertyName("shouldCheckUpdates")]
    public bool ShouldCheckUpdates { get; set; } = true;

    /// <summary>Whether compatible patch updates on the active track are automatically staged.</summary>
    [JsonPropertyName("shouldAutoStagePatches")]
    public bool ShouldAutoStagePatches { get; set; } = true;

    /// <summary>Optional feed URL or local feed file path used for update checks.</summary>
    [JsonPropertyName("lastUpdateFeedUrl")]
    public string? LastUpdateFeedUrl { get; set; }

    /// <summary>Optional override path to the <c>engines\</c> root directory.</summary>
    [JsonPropertyName("enginesDirectoryOverride")]
    public string? EnginesDirectoryOverride { get; set; }

    /// <summary>Legacy deprecated scale preference read only for migration to <see cref="ResolutionScale"/>.</summary>
    [JsonPropertyName("preferredResolutionScale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PreferredResolutionScale { get; set; }

    /// <summary>Canonical Standard 18 integer resolution scale setting (1..8).</summary>
    [JsonPropertyName("resolutionScale")]
    public int? ResolutionScale { get; set; } = 2;

    /// <summary>Canonical Standard 18 preferred render backend identifier (<c>opengl</c>, <c>vulkan</c>, <c>parallelCpu</c>, <c>software</c>).</summary>
    [JsonPropertyName("backendId")]
    public string BackendId { get; set; } = "opengl";

    /// <summary>Explicit per-engine pinned version overrides keyed by engine ID.</summary>
    [JsonPropertyName("pinnedEngineVersions")]
    public Dictionary<string, string>? PinnedEngineVersions { get; set; }

    /// <summary>Absolute or data-relative path to the latest resumable staged update package directory.</summary>
    [JsonPropertyName("stagedUpdatePath")]
    public string? StagedUpdatePath { get; set; }

    /// <summary>Saved manager window width.</summary>
    [JsonPropertyName("windowWidth")]
    public int? WindowWidth { get; set; }

    /// <summary>Saved manager window height.</summary>
    [JsonPropertyName("windowHeight")]
    public int? WindowHeight { get; set; }

    /// <summary>Preserves unknown forward-compatible settings properties across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Single engine release entry inside an update feed manifest.
/// </summary>
public sealed record UpdateFeedEntry(
    [property: JsonPropertyName("engineId")] string EngineId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256Hex")] string Sha256Hex,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("notes")] string? Notes = null);

/// <summary>
/// Update feed manifest listing available engine versions.
/// </summary>
public sealed record UpdateFeedManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc,
    [property: JsonPropertyName("engines")] IReadOnlyList<UpdateFeedEntry> Engines);

/// <summary>
/// Result of evaluating an update feed against an active engine version and compatibility track.
/// </summary>
public sealed record UpdateCheckOutcome(
    bool Checked,
    bool CompatiblePatchAvailable,
    bool CompatiblePatchStaged,
    string? StagedPatchPath,
    bool BreakingUpdateAvailable,
    string? BreakingVersion,
    string StatusMessage,
    bool NetworkUnavailable = false);

/// <summary>
/// Resumable download checkpoint sidecar persisted alongside <c>*.part</c> downloads (<c>*.part.meta.json</c>).
/// </summary>
public sealed record UpdatePartMeta(
    [property: JsonPropertyName("engineId")] string EngineId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("sha256Hex")] string Sha256Hex,
    [property: JsonPropertyName("expectedSizeBytes")] long ExpectedSizeBytes,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("updatedAtUtc")] string UpdatedAtUtc);

/// <summary>
/// Aggregate result of sweeping all distinct <c>(EngineId, Track)</c> targets installed under <c>engines\</c>.
/// </summary>
public sealed record MultiEngineUpdateSweepResult(
    bool SweepExecuted,
    bool NetworkUnavailable,
    IReadOnlyList<UpdateCheckOutcome> Outcomes,
    string SummaryMessage);
