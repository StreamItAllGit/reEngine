// @module: reEngine.Core.Updates.UpdateCoordinator
// @purpose: Schema-versioned settings persistence and non-blocking multi-engine track-aware update sweep with 2s manifest polling.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Net, System.Net.Http, System.Net.Sockets, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution
// @exports: UpdateCoordinator

using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Resolution;

namespace reEngine.Core.Updates;

/// <summary>
/// Coordinates schema-versioned settings persistence and decoupled track-safe engine update staging.
/// </summary>
public sealed class UpdateCoordinator
{
    private static readonly TimeSpan ManifestPollTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly GranularSettingsManager _settingsManager;
    private readonly string _settingsPath;
    private readonly string _dataDirectory;

    /// <summary>Initializes an update coordinator bound to <paramref name="dataDirectory"/> and optional <paramref name="configPath"/>.</summary>
    public UpdateCoordinator(string dataDirectory, string? configPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _settingsManager = new GranularSettingsManager(_dataDirectory);
        _settingsPath = string.IsNullOrWhiteSpace(configPath)
            ? _settingsManager.ResolveSettingsPath("settings.json")
            : Path.GetFullPath(configPath);
    }

    /// <summary>Loads or self-heals persisted <c>ReEngineSettings</c>.</summary>
    public SettingsLoadOutcome<ReEngineSettings> LoadSettings() =>
        _settingsManager.LoadOrSelfHeal(_settingsPath, static () => new ReEngineSettings());

    /// <summary>Atomically persists updated <paramref name="settings"/>.</summary>
    public void SaveSettings(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
        settings.AppId = string.IsNullOrWhiteSpace(settings.AppId) ? "reengine" : settings.AppId;
        settings.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        _settingsManager.SaveAtomically(_settingsPath, settings);
    }

    /// <summary>Checks <paramref name="feedSource"/> for compatible patches on <paramref name="compatibleEngineTrack"/> and stages updates cleanly.</summary>
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
        if (!settings.ShouldCheckUpdates) return new UpdateCheckOutcome(false, false, false, null, false, null, "Update checks disabled in settings.");

        string? source = !string.IsNullOrWhiteSpace(feedSource) ? feedSource.Trim() : settings.LastUpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(source)) return new UpdateCheckOutcome(true, false, false, null, false, null, "No update feed configured; skipping remote check.");

        UpdateFeedManifest manifest = await FetchFeedManifestAsync(source, customHandler, allowedLocalFeedRoot, ct).ConfigureAwait(false);
        return await EvaluateAndStageAsync(settings, manifest, engineId, installedVersion, compatibleEngineTrack, customHandler, ct).ConfigureAwait(false);
    }

    /// <summary>Sweeps every installed engine track against <paramref name="feedSource"/> using a 2-second manifest poll timeout.</summary>
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
        if (!settings.ShouldCheckUpdates) return new MultiEngineUpdateSweepResult(false, false, Array.Empty<UpdateCheckOutcome>(), "Update checks disabled in settings.");

        string? source = !string.IsNullOrWhiteSpace(feedSource) ? feedSource.Trim() : settings.LastUpdateFeedUrl;
        if (string.IsNullOrWhiteSpace(source)) return new MultiEngineUpdateSweepResult(false, false, Array.Empty<UpdateCheckOutcome>(), "No update feed configured; skipping remote check.");

        UpdateFeedManifest manifest;
        try
        {
            manifest = await FetchFeedManifestAsync(source, customHandler, allowedLocalFeedRoot, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException httpEx) when (IsClientHttpError(httpEx))
        {
            return new MultiEngineUpdateSweepResult(true, false, Array.Empty<UpdateCheckOutcome>(), $"Update feed HTTP error ({(int?)httpEx.StatusCode}): {httpEx.Message}");
        }
        catch (Exception ex) when (IsTransientFeedOrNetworkException(ex, ct))
        {
            return new MultiEngineUpdateSweepResult(false, true, Array.Empty<UpdateCheckOutcome>(), $"Update check skipped (offline/unavailable): {ex.Message}");
        }

        List<(string EngineId, SemVer Version, string Track)> targets = BuildSweepTargets(installedEngines);
        var outcomes = new List<UpdateCheckOutcome>(targets.Count);
        bool anyNetworkUnavailable = false;

        foreach ((string engineId, SemVer version, string track) in targets)
        {
            try
            {
                outcomes.Add(await EvaluateAndStageAsync(settings, manifest, engineId, version, track, customHandler, ct).ConfigureAwait(false));
            }
            catch (HttpRequestException httpEx) when (IsClientHttpError(httpEx))
            {
                outcomes.Add(new UpdateCheckOutcome(true, true, false, null, false, null, $"Patch download HTTP error ({(int?)httpEx.StatusCode}): {httpEx.Message}", false));
            }
            catch (Exception ex) when (IsTransientFeedOrNetworkException(ex, ct))
            {
                anyNetworkUnavailable = true;
                outcomes.Add(new UpdateCheckOutcome(false, true, false, null, false, null, $"Patch staging skipped (offline/unavailable): {ex.Message}", true));
            }
        }

        return new MultiEngineUpdateSweepResult(true, anyNetworkUnavailable, outcomes, BuildSweepSummary(targets, outcomes));
    }

    private async Task<UpdateFeedManifest> FetchFeedManifestAsync(
        string source,
        HttpMessageHandler? customHandler,
        string? allowedLocalFeedRoot,
        CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            UpdatePackageApplier.AssertSafeRemoteUrl(uri, allowLoopback: customHandler is not null || IsLoopbackAllowed());
            string cachePath = Path.Combine(_dataDirectory, "cache", "update_feed.json");
            var transferOpts = new SafeTransferOptions { JobId = "reengine-feed", ConnectTimeout = ManifestPollTimeout, ReadStallTimeout = ManifestPollTimeout, ResponseHeadersTimeout = ManifestPollTimeout, MaxRetries = 1 };
            SafeTransferResult feedRes = await SafeNetworkTransfer.DownloadFileAsync(uri, cachePath, transferOpts, customHandler, ct).ConfigureAwait(false);
            string raw = await File.ReadAllTextAsync(feedRes.FinalFilePath, Encoding.UTF8, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<UpdateFeedManifest>(raw, JsonOptions) ?? new UpdateFeedManifest(1, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), Array.Empty<UpdateFeedEntry>());
        }

        string localPath = Path.GetFullPath(source);
        if (!string.IsNullOrWhiteSpace(allowedLocalFeedRoot)) localPath = AppEnvironment.AssertPathContained(localPath, allowedLocalFeedRoot);
        if (!File.Exists(localPath)) throw new FileNotFoundException($"Update feed file not found: '{localPath}'.", localPath);
        return JsonSerializer.Deserialize<UpdateFeedManifest>(await File.ReadAllTextAsync(localPath, Encoding.UTF8, ct).ConfigureAwait(false), JsonOptions)
            ?? new UpdateFeedManifest(1, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), Array.Empty<UpdateFeedEntry>());
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
            if (!EngineAliasCatalog.AreEquivalent(entry.EngineId, engineId) || !SemVer.TryParse(entry.Version, out SemVer ver) || ver.CompareTo(installedVersion) <= 0) continue;
            if (EngineTrackMatcher.IsTrackCompatible(compatibleEngineTrack, ver))
            {
                if (bestPatch is null || ver.CompareTo(bestPatchVer) > 0) { bestPatch = entry; bestPatchVer = ver; }
            }
            else if (bestBreaking is null || ver.CompareTo(bestBreakingVer) > 0) { bestBreaking = entry; bestBreakingVer = ver; }
        }

        string? stagedPath = null;
        bool staged = false;
        if (bestPatch is not null && settings.ShouldAutoStagePatches)
        {
            stagedPath = await StageCompatiblePatchAsync(settings, bestPatch, bestPatchVer, customHandler, ct).ConfigureAwait(false);
            staged = true;
        }

        string? breakingVerStr = bestBreaking is not null ? bestBreakingVer.ToString() : null;
        return new UpdateCheckOutcome(true, bestPatch is not null, staged, stagedPath, bestBreaking is not null, breakingVerStr, BuildStatusMessage(bestPatch is not null, staged, bestPatchVer, bestBreaking is not null, breakingVerStr, compatibleEngineTrack));
    }

    private async Task<string> StageCompatiblePatchAsync(
        ReEngineSettings settings,
        UpdateFeedEntry patch,
        SemVer patchVersion,
        HttpMessageHandler? customHandler,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(patch.DownloadUrl, UriKind.Absolute, out Uri? downloadUri))
        {
            throw new InvalidOperationException($"Invalid patch downloadUrl '{patch.DownloadUrl}'.");
        }

        string fileName = Path.GetFileName(downloadUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = $"{patch.EngineId}-v{patchVersion}.zip";

        string stageDir = Path.Combine(_dataDirectory, "staged_updates", patch.EngineId, $".staging-{patchVersion}");
        string partPath = Path.Combine(stageDir, fileName + ".part");
        var meta = new UpdatePartMeta(patch.EngineId, patchVersion.ToString(), downloadUri.ToString(), patch.Sha256Hex, patch.FileSizeBytes, 0, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        await UpdatePackageApplier.DownloadResumablePartAsync(downloadUri, partPath, meta, customHandler, customHandler is not null || IsLoopbackAllowed(), ct: ct).ConfigureAwait(false);

        string verifiedZipPath = Path.Combine(stageDir, fileName);
        File.Move(partPath, verifiedZipPath, overwrite: true);
        settings.StagedUpdatePath = verifiedZipPath;
        return verifiedZipPath;
    }

    private static List<(string EngineId, SemVer Version, string Track)> BuildSweepTargets(IReadOnlyList<InstalledEngineCandidate> installed) =>
        installed.Count == 0
            ? new List<(string, SemVer, string)> { ("reStation", new SemVer(0, 1, 0), "0.1.x") }
            : installed.GroupBy(c => (EngineId: c.EngineId.Trim().ToUpperInvariant(), Track: $"{c.Version.Major}.{c.Version.Minor}.x"))
                .Select(g => { InstalledEngineCandidate top = g.OrderByDescending(c => c.Version).First(); return (top.EngineId, top.Version, $"{top.Version.Major}.{top.Version.Minor}.x"); })
                .OrderBy(t => t.EngineId, StringComparer.OrdinalIgnoreCase).ThenByDescending(t => t.Version).ToList();

    private static bool IsClientHttpError(HttpRequestException ex) => ex.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError;
    private static bool IsTransientFeedOrNetworkException(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or SocketException or IOException or TimeoutException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested);
    private static bool IsLoopbackAllowed() => string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase);

    private static string BuildSweepSummary(IReadOnlyList<(string EngineId, SemVer Version, string Track)> targets, IReadOnlyList<UpdateCheckOutcome> outcomes) =>
        outcomes.Count == 0 ? "Up to date." : outcomes.Count == 1 ? outcomes[0].StatusMessage : string.Join(" | ", targets.Select((t, i) => $"{t.EngineId} ({t.Track}): {outcomes[i].StatusMessage}"));

    private static string BuildStatusMessage(bool patchAvail, bool staged, SemVer patchVer, bool breakingAvail, string? breakingVer, string track) =>
        patchAvail && breakingAvail ? $"{(staged ? $"Compatible patch v{patchVer} staged" : $"Compatible patch v{patchVer} available")}; breaking engine update v{breakingVer} available (existing {track} track preserved)." :
        patchAvail ? (staged ? $"Compatible patch v{patchVer} downloaded and SHA-256 verified." : $"Compatible patch v{patchVer} available (auto-staging disabled).") :
        breakingAvail ? $"Breaking engine update v{breakingVer} available; existing {track} track preserved." : "Up to date.";
}
