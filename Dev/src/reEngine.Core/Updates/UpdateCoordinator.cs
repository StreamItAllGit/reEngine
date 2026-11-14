// @module: reEngine.Core.Updates.UpdateCoordinator
// @purpose: Schema-versioned settings persistence and non-blocking multi-engine track-aware update sweep with SafeNetworkTransfer staging.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Net.Sockets, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution
// @exports: UpdateCoordinator

using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Resolution;

namespace reEngine.Core.Updates;

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

    /// <summary>
    /// Sweeps every distinct <c>(EngineId, Track)</c> target across <paramref name="installedEngines"/>,
    /// fetching the feed manifest once and degrading transient network/socket/JSON faults cleanly.
    /// </summary>
    public async Task<MultiEngineUpdateSweepResult> CheckAllInstalledEnginesAsync(
        ReEngineSettings settings,
        IReadOnlyList<InstalledEngineCandidate> installedEngines,
        string? feedSource = null,
        HttpMessageHandler? customHandler = null,
        string? allowedLocalFeedRoot = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(installedEngines);

        if (!settings.ShouldCheckUpdates)
        {
            return new MultiEngineUpdateSweepResult(
                SweepExecuted: false,
                NetworkUnavailable: false,
                Array.Empty<UpdateCheckOutcome>(),
                "Update checks disabled in settings.");
        }

        string? source = !string.IsNullOrWhiteSpace(feedSource) ? feedSource.Trim() : settings.LastUpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(source))
        {
            return new MultiEngineUpdateSweepResult(
                SweepExecuted: false,
                NetworkUnavailable: false,
                Array.Empty<UpdateCheckOutcome>(),
                "No update feed configured; skipping remote check.");
        }

        UpdateFeedManifest manifest;
        try
        {
            manifest = await FetchFeedManifestAsync(source, customHandler, allowedLocalFeedRoot, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientFeedOrNetworkException(ex, ct))
        {
            return new MultiEngineUpdateSweepResult(
                SweepExecuted: false,
                NetworkUnavailable: true,
                Array.Empty<UpdateCheckOutcome>(),
                $"Update check skipped (offline/unavailable): {ex.Message}");
        }

        List<(string EngineId, SemVer Version, string Track)> targets = BuildSweepTargets(installedEngines);
        var outcomes = new List<UpdateCheckOutcome>(targets.Count);
        bool anyNetworkUnavailable = false;

        foreach ((string engineId, SemVer version, string track) in targets)
        {
            try
            {
                UpdateCheckOutcome outcome = await EvaluateAndStageAsync(
                    settings, manifest, engineId, version, track, customHandler, ct).ConfigureAwait(false);
                outcomes.Add(outcome);
            }
            catch (Exception ex) when (IsTransientFeedOrNetworkException(ex, ct))
            {
                anyNetworkUnavailable = true;
                outcomes.Add(new UpdateCheckOutcome(
                    Checked: false,
                    CompatiblePatchAvailable: true,
                    CompatiblePatchStaged: false,
                    StagedPatchPath: null,
                    BreakingUpdateAvailable: false,
                    BreakingVersion: null,
                    StatusMessage: $"Patch staging skipped (offline/unavailable): {ex.Message}",
                    NetworkUnavailable: true));
            }
        }

        return new MultiEngineUpdateSweepResult(
            SweepExecuted: true,
            NetworkUnavailable: anyNetworkUnavailable,
            outcomes,
            BuildSweepSummary(targets, outcomes));
    }

    private static List<(string EngineId, SemVer Version, string Track)> BuildSweepTargets(
        IReadOnlyList<InstalledEngineCandidate> installedEngines)
    {
        if (installedEngines.Count == 0)
        {
            return new List<(string, SemVer, string)> { ("reStation", new SemVer(0, 1, 0), "0.1.x") };
        }

        return installedEngines
            .GroupBy(
                c => (EngineId: c.EngineId.Trim().ToUpperInvariant(), Track: $"{c.Version.Major}.{c.Version.Minor}.x"))
            .Select(g =>
            {
                InstalledEngineCandidate top = g.OrderByDescending(c => c.Version).First();
                return (top.EngineId, top.Version, $"{top.Version.Major}.{top.Version.Minor}.x");
            })
            .OrderBy(t => t.EngineId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(t => t.Version)
            .ToList();
    }

    private static bool IsTransientFeedOrNetworkException(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or SocketException or IOException or TimeoutException or JsonException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private static string BuildSweepSummary(
        IReadOnlyList<(string EngineId, SemVer Version, string Track)> targets,
        IReadOnlyList<UpdateCheckOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return "Up to date.";
        }
        if (outcomes.Count == 1)
        {
            return outcomes[0].StatusMessage;
        }

        return string.Join(" | ", targets.Select((t, idx) => $"{t.EngineId} ({t.Track}): {outcomes[idx].StatusMessage}"));
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
