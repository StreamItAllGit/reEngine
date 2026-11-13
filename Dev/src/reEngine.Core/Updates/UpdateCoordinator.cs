// @module: reEngine.Core.Updates.UpdateCoordinator
// @purpose: Schema-versioned settings persistence and non-blocking track-aware update checking with 6-pillar SafeNetworkTransfer staging.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution
// @exports: ReEngineSettings, UpdateFeedEntry, UpdateFeedManifest, UpdateCheckOutcome, UpdateCoordinator

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;
using reEngine.Core.Resolution;

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

    /// <summary>Optional feed URL or local sandbox feed path used for update checks.</summary>
    [JsonPropertyName("lastUpdateFeedUrl")]
    public string? LastUpdateFeedUrl { get; set; }

    /// <summary>Optional override path to the <c>engines\</c> root directory.</summary>
    [JsonPropertyName("enginesDirectoryOverride")]
    public string? EnginesDirectoryOverride { get; set; }

    /// <summary>Optional global pass-through resolution scale preference (1..8).</summary>
    [JsonPropertyName("preferredResolutionScale")]
    public int PreferredResolutionScale { get; set; } = 2;

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
/// Result of evaluating an update feed against the active engine version and compatibility track.
/// </summary>
public sealed record UpdateCheckOutcome(
    bool Checked,
    bool CompatiblePatchAvailable,
    bool CompatiblePatchStaged,
    string? StagedPatchPath,
    bool BreakingUpdateAvailable,
    string? BreakingVersion,
    string StatusMessage);

/// <summary>
/// Coordinates schema-versioned settings persistence and track-safe engine update checks/staging.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly GranularSettingsManager _settingsManager;
    private readonly string _settingsPath;
    private readonly string _dataDirectory;

    /// <summary>
    /// Initializes an update coordinator bound to <paramref name="dataDirectory"/> and <paramref name="configPath"/>.
    /// </summary>
    public UpdateCoordinator(string dataDirectory, string? configPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _settingsManager = new GranularSettingsManager(_dataDirectory);
        _settingsPath = string.IsNullOrWhiteSpace(configPath)
            ? _settingsManager.ResolveSettingsPath("settings.json")
            : Path.GetFullPath(configPath);
    }

    /// <summary>Loads <c>settings.json</c> with automatic backup self-healing.</summary>
    public SettingsLoadOutcome<ReEngineSettings> LoadSettings() =>
        _settingsManager.LoadOrSelfHeal(_settingsPath, static () => new ReEngineSettings());

    /// <summary>Persists <paramref name="settings"/> atomically with timestamp update.</summary>
    public void SaveSettings(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
        settings.AppId = string.IsNullOrWhiteSpace(settings.AppId) ? "reengine" : settings.AppId;
        settings.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        _settingsManager.SaveAtomically(_settingsPath, settings);
    }

    /// <summary>
    /// Checks <paramref name="feedSource"/> for compatible track patches (auto-staging when enabled)
    /// and newer breaking releases (alert-only, never overwriting existing tracks).
    /// </summary>
    public async Task<UpdateCheckOutcome> CheckAndStageAsync(
        ReEngineSettings settings,
        string engineId,
        SemVer installedVersion,
        string compatibleEngineTrack,
        string? feedSource = null,
        HttpMessageHandler? customHandler = null,
        string? allowedLocalFeedRoot = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);

        if (!settings.ShouldCheckUpdates)
        {
            return new UpdateCheckOutcome(false, false, false, null, false, null, "Update checks disabled in settings.");
        }

        string? source = !string.IsNullOrWhiteSpace(feedSource) ? feedSource.Trim() : settings.LastUpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(source))
        {
            return new UpdateCheckOutcome(true, false, false, null, false, null, "No update feed configured; skipping remote check.");
        }

        UpdateFeedManifest manifest = await FetchFeedManifestAsync(source, customHandler, allowedLocalFeedRoot, ct).ConfigureAwait(false);
        return await EvaluateAndStageAsync(settings, manifest, engineId, installedVersion, compatibleEngineTrack, customHandler, ct)
            .ConfigureAwait(false);
    }

    private async Task<UpdateFeedManifest> FetchFeedManifestAsync(
        string source,
        HttpMessageHandler? customHandler,
        string? allowedLocalFeedRoot,
        CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            string cachePath = Path.Combine(_dataDirectory, "cache", "update_feed.json");
            var transferOpts = new SafeTransferOptions
            {
                JobId = "reengine-feed",
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ReadStallTimeout = TimeSpan.FromSeconds(30),
                MaxRetries = 2
            };
            SafeTransferResult feedRes = await SafeNetworkTransfer.DownloadFileAsync(uri, cachePath, transferOpts, customHandler, ct)
                .ConfigureAwait(false);
            string raw = await File.ReadAllTextAsync(feedRes.FinalFilePath, Encoding.UTF8, ct).ConfigureAwait(false);
            return DeserializeFeed(raw);
        }

        string localPath = Path.GetFullPath(source);
        if (!string.IsNullOrWhiteSpace(allowedLocalFeedRoot))
        {
            localPath = SandboxEnvironment.AssertPathContained(localPath, allowedLocalFeedRoot);
        }
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException($"Update feed file not found: '{localPath}'.", localPath);
        }
        string localJson = await File.ReadAllTextAsync(localPath, Encoding.UTF8, ct).ConfigureAwait(false);
        return DeserializeFeed(localJson);
    }

    private async Task<UpdateCheckOutcome> EvaluateAndStageAsync(
        ReEngineSettings settings,
        UpdateFeedManifest manifest,
        string engineId,
        SemVer installedVersion,
        string compatibleEngineTrack,
        HttpMessageHandler? customHandler,
        CancellationToken ct)
    {
        UpdateFeedEntry? bestPatch = null;
        SemVer bestPatchVer = default;
        UpdateFeedEntry? bestBreaking = null;
        SemVer bestBreakingVer = default;

        foreach (UpdateFeedEntry entry in manifest.Engines)
        {
            if (!string.Equals(entry.EngineId, engineId, StringComparison.OrdinalIgnoreCase) ||
                !SemVer.TryParse(entry.Version, out SemVer entryVer) ||
                entryVer.CompareTo(installedVersion) <= 0)
            {
                continue;
            }

            if (EngineTrackMatcher.IsTrackCompatible(compatibleEngineTrack, entryVer))
            {
                if (bestPatch is null || entryVer.CompareTo(bestPatchVer) > 0)
                {
                    bestPatch = entry;
                    bestPatchVer = entryVer;
                }
            }
            else if (bestBreaking is null || entryVer.CompareTo(bestBreakingVer) > 0)
            {
                bestBreaking = entry;
                bestBreakingVer = entryVer;
            }
        }

        string? stagedPath = null;
        bool staged = false;
        if (bestPatch is not null && settings.ShouldAutoStagePatches)
        {
            stagedPath = await StageCompatiblePatchAsync(bestPatch, bestPatchVer, customHandler, ct).ConfigureAwait(false);
            staged = true;
        }

        bool patchAvail = bestPatch is not null;
        bool breakingAvail = bestBreaking is not null;
        string? breakingVerStr = bestBreaking is not null ? bestBreakingVer.ToString() : null;
        string status = BuildStatusMessage(patchAvail, staged, bestPatchVer, breakingAvail, breakingVerStr, compatibleEngineTrack);

        return new UpdateCheckOutcome(true, patchAvail, staged, stagedPath, breakingAvail, breakingVerStr, status);
    }

    private async Task<string> StageCompatiblePatchAsync(
        UpdateFeedEntry patch,
        SemVer patchVersion,
        HttpMessageHandler? customHandler,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(patch.DownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
            (!downloadUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !downloadUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Patch downloadUrl '{patch.DownloadUrl}' must be an absolute HTTP/HTTPS URI.");
        }

        string fileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{patch.EngineId}-v{patchVersion}.zip";
        }

        string stageDir = Path.Combine(_dataDirectory, "staged_updates", patch.EngineId, $"v{patchVersion}");
        Directory.CreateDirectory(stageDir);
        string destPath = Path.Combine(stageDir, fileName);

        var opts = new SafeTransferOptions
        {
            JobId = $"patch-{patch.EngineId}-{patchVersion}",
            ExpectedSizeBytes = patch.FileSizeBytes > 0 ? patch.FileSizeBytes : null,
            ExpectedSha256Hex = patch.Sha256Hex,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ReadStallTimeout = TimeSpan.FromSeconds(30),
            MaxRetries = 2
        };

        SafeTransferResult res = await SafeNetworkTransfer.DownloadFileAsync(downloadUri, destPath, opts, customHandler, ct)
            .ConfigureAwait(false);
        return res.FinalFilePath;
    }

    private static UpdateFeedManifest DeserializeFeed(string rawJson) =>
        JsonSerializer.Deserialize<UpdateFeedManifest>(rawJson, JsonOptions)
        ?? new UpdateFeedManifest(1, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), Array.Empty<UpdateFeedEntry>());

    private static string BuildStatusMessage(
        bool patchAvailable,
        bool patchStaged,
        SemVer patchVer,
        bool breakingAvailable,
        string? breakingVer,
        string track)
    {
        if (patchAvailable && breakingAvailable)
        {
            string patchText = patchStaged ? $"Compatible patch v{patchVer} staged" : $"Compatible patch v{patchVer} available";
            return $"{patchText}; breaking engine update v{breakingVer} available (existing {track} track preserved).";
        }
        if (patchAvailable)
        {
            return patchStaged
                ? $"Compatible patch v{patchVer} downloaded and SHA-256 verified."
                : $"Compatible patch v{patchVer} available (auto-staging disabled).";
        }
        if (breakingAvailable)
        {
            return $"Breaking engine update v{breakingVer} available; existing {track} track preserved.";
        }
        return "Up to date.";
    }
}
