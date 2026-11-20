// @module: reEngine.Core.Updates.UpdateModels
// @purpose: Standard 18/19 settings schema, update feed contracts, resumable .part metadata, and multi-engine sweep records.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: ReEngineSettings, UpdateFeedEntry, UpdateFeedManifest, UpdatePartMeta, UpdateCheckOutcome, MultiEngineUpdateSweepResult

using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Updates;

/// <summary>
/// Per-edge integer pixel overscan crop bounds (Standard 18: <c>overscanCropPx</c>).
/// </summary>
public sealed class OverscanCropPxSettings
{
    /// <summary>Top edge overscan crop in source pixels.</summary>
    [JsonPropertyName("top")]
    public int Top { get; set; }

    /// <summary>Bottom edge overscan crop in source pixels.</summary>
    [JsonPropertyName("bottom")]
    public int Bottom { get; set; }

    /// <summary>Left edge overscan crop in source pixels.</summary>
    [JsonPropertyName("left")]
    public int Left { get; set; }

    /// <summary>Right edge overscan crop in source pixels.</summary>
    [JsonPropertyName("right")]
    public int Right { get; set; }
}

/// <summary>
/// Persisted <c>data\settings.json</c> schema following Standard 18 &amp; Standard 19.
/// </summary>
public sealed class ReEngineSettings
{
    /// <summary>Settings schema version (1 on legacy files, 2 on split-domain configs).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical application identifier.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "reengine";

    /// <summary>Standard 19 settings domain identifier (<c>"core"</c>).</summary>
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "core";

    /// <summary>ISO-8601 UTC timestamp of the last save.</summary>
    [JsonPropertyName("updatedAtUtc")]
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Whether Tier 3 runtime JIT fallback is permitted alongside static AOT.</summary>
    [JsonPropertyName("isJitEnabled")]
    public bool IsJitEnabled { get; set; }

    /// <summary>Preferred execution tier mode (<c>staticAotOnly</c>, <c>aotWithJitFallback</c>, <c>aotWithThreadedIr</c>, <c>jitBringup</c>, quarantined <c>interpreterOracle</c>).</summary>
    [JsonPropertyName("executionTierMode")]
    public string ExecutionTierMode { get; set; } = "staticAotOnly";

    /// <summary>Whether verified BYO ROM/disc media is preloaded into RAM when host headroom permits.</summary>
    [JsonPropertyName("shouldPreloadMediaToRam")]
    public bool ShouldPreloadMediaToRam { get; set; }

    /// <summary>Whether fast-boot skipping of console splash animations is enabled.</summary>
    [JsonPropertyName("isFastBootEnabled")]
    public bool IsFastBootEnabled { get; set; }

    /// <summary>Whether crash-recovery Safe Mode is currently active.</summary>
    [JsonPropertyName("isSafeModeActive")]
    public bool IsSafeModeActive { get; set; }

    /// <summary>Whether Form1 2048-byte data sector fast-loading is enabled.</summary>
    [JsonPropertyName("shouldFastLoadData")]
    public bool ShouldFastLoadData { get; set; } = true;

    /// <summary>Whether verbose runtime session diagnostic logging is enabled (Standard 18: <c>shouldEnableRuntimeLogs</c>).</summary>
    [JsonPropertyName("shouldEnableRuntimeLogs")]
    public bool ShouldEnableRuntimeLogs { get; set; }

    /// <summary>Fast-forward speed multiplier clamped to <c>2.0..4.0</c>.</summary>
    [JsonPropertyName("fastForwardMultiplier")]
    public double FastForwardMultiplier { get; set; } = 2.0;

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

    /// <summary>Canonical Standard 18 preferred render backend identifier (<c>opengl</c>, <c>vulkan</c>, <c>directx</c>, <c>parallelCpu</c>, <c>software</c>).</summary>
    [JsonPropertyName("backendId")]
    public string BackendId { get; set; } = "opengl";

    /// <summary>Canonical Standard 18 aspect ratio presentation mode (<c>nativePar</c>, <c>fourByThree</c>, <c>squareInteger</c>, <c>nativeFit</c>, <c>horPlus16By9</c>).</summary>
    [JsonPropertyName("aspectRatioMode")]
    public string? AspectRatioMode { get; set; } = "nativePar";

    /// <summary>Canonical Standard 18 pixel edge overscan crop settings.</summary>
    [JsonPropertyName("overscanCropPx")]
    public OverscanCropPxSettings? OverscanCropPx { get; set; } = new();

    /// <summary>Canonical Standard 18 runtime fast-forward flag.</summary>
    [JsonPropertyName("isFastForwardEnabled")]
    public bool? IsFastForwardEnabled { get; set; } = false;

    /// <summary>Whether fullscreen host presentation is enabled by default (Standard 18: <c>isFullscreen</c>).</summary>
    [JsonPropertyName("isFullscreen")]
    public bool IsFullscreen { get; set; }

    /// <summary>Optional explicit OS native audio driver override (e.g., <c>wasapi</c>, <c>pipewire</c>, <c>coreaudio</c>; <see langword="null"/> = auto).</summary>
    [JsonPropertyName("audioDriver")]
    public string? AudioDriver { get; set; }

    /// <summary>Whether path-contained <c>hdpacks\</c> texture overrides are enabled (Standard 18: <c>isHdTexturePackEnabled</c>).</summary>
    [JsonPropertyName("isHdTexturePackEnabled")]
    public bool IsHdTexturePackEnabled { get; set; }

    /// <summary>Bounded graceful child-tree shutdown timeout in milliseconds during synchronous <c>--wait</c> teardown.</summary>
    [JsonPropertyName("shutdownTimeoutMs")]
    public int? ShutdownTimeoutMs { get; set; } = 3000;

    /// <summary>Explicit per-module or per-engine pinned version overrides (<c>{ "&lt;moduleId|engineId&gt;": "&lt;SemVer|latest|bundled|target&gt;" }</c>).</summary>
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
