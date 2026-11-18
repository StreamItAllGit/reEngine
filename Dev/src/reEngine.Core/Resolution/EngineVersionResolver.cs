// @module: reEngine.Core.Resolution.EngineVersionResolver
// @purpose: Semantic versioning, track/alias matching, .staging-.ready gating, and pinned-version side-by-side resolution.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Text.Json, Foundation.Core, reEngine.Core.Manifests, reEngine.Core.Updates
// @exports: SemVer, InstalledEngineCandidate, EngineResolutionResult, EngineTrackMatcher, EngineVersionResolver

using System.Globalization;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Manifests;
using reEngine.Core.Updates;

namespace reEngine.Core.Resolution;

/// <summary>
/// Universal Semantic Version (<c>MAJOR.MINOR.PATCH[-prerelease]</c>) supporting <c>-hf.&lt;n&gt;</c> and <c>-nightly.&lt;n&gt;</c>.
/// </summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<SemVer>
{
    /// <summary>Threshold SemVer requiring <c>.ready</c> staging readiness markers on v0.3.0+ engine installs.</summary>
    public static SemVer Track030Threshold { get; } = new(0, 3, 0);

    /// <summary>Attempts to parse a SemVer string (with optional leading <c>v</c>).</summary>
    public static bool TryParse(string? raw, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string trimmed = raw.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V')) trimmed = trimmed[1..];
        int plusIdx = trimmed.IndexOf('+');
        if (plusIdx >= 0) trimmed = trimmed[..plusIdx];

        string? prerelease = null;
        int dashIdx = trimmed.IndexOf('-');
        if (dashIdx >= 0)
        {
            prerelease = trimmed[(dashIdx + 1)..];
            trimmed = trimmed[..dashIdx];
            if (string.IsNullOrWhiteSpace(prerelease)) return false;
        }

        string[] parts = trimmed.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return false;
        }

        version = new SemVer(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>Compares two semantic versions per SemVer 2.0.0 precedence (stable &gt; prerelease).</summary>
    public int CompareTo(SemVer other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        bool thisStable = string.IsNullOrEmpty(Prerelease);
        bool otherStable = string.IsNullOrEmpty(other.Prerelease);
        if (thisStable && otherStable) return 0;
        if (thisStable) return 1;
        if (otherStable) return -1;

        string[] leftIds = Prerelease!.Split('.');
        string[] rightIds = other.Prerelease!.Split('.');
        int count = Math.Min(leftIds.Length, rightIds.Length);
        for (int i = 0; i < count; i++)
        {
            bool leftNum = int.TryParse(leftIds[i], NumberStyles.None, CultureInfo.InvariantCulture, out int lVal);
            bool rightNum = int.TryParse(rightIds[i], NumberStyles.None, CultureInfo.InvariantCulture, out int rVal);
            if (leftNum && rightNum)
            {
                int nCmp = lVal.CompareTo(rVal);
                if (nCmp != 0) return nCmp;
            }
            else if (leftNum != rightNum) return leftNum ? -1 : 1;
            else
            {
                int sCmp = string.Compare(leftIds[i], rightIds[i], StringComparison.OrdinalIgnoreCase);
                if (sCmp != 0) return sCmp;
            }
        }
        return leftIds.Length.CompareTo(rightIds.Length);
    }

    /// <summary>Formats the semantic version without a leading <c>v</c>.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(Prerelease) ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}

/// <summary>
/// Represents an installed side-by-side engine version under <c>engines\&lt;engineId&gt;\v&lt;Ver&gt;\</c>.
/// </summary>
public sealed record InstalledEngineCandidate(
    string EngineId,
    SemVer Version,
    string VersionFolderName,
    string EngineDirectory,
    string ExecutablePath);

/// <summary>
/// Result of resolving the highest compatible engine installation for a <c>.recomp</c> manifest.
/// </summary>
public sealed record EngineResolutionResult(
    bool Found,
    InstalledEngineCandidate? SelectedEngine,
    IReadOnlyList<InstalledEngineCandidate> DiscoveredCandidates,
    string DiagnosticMessage);

/// <summary>
/// Matches candidate engine versions against <c>compatibleEngineTrack</c> or <c>supportedEngineTracks</c>.
/// </summary>
public static class EngineTrackMatcher
{
    /// <summary>Evaluates whether <paramref name="candidate"/> matches <paramref name="compatibleEngineTrack"/>.</summary>
    public static bool IsTrackCompatible(string compatibleEngineTrack, SemVer candidate)
    {
        if (string.IsNullOrWhiteSpace(compatibleEngineTrack)) return false;
        string norm = compatibleEngineTrack.Trim();
        if (norm.StartsWith('v') || norm.StartsWith('V')) norm = norm[1..];

        string[] parts = norm.Split('.');
        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)) return false;
            if (IsWildcard(parts[1])) return candidate.Major == major;
            return int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) &&
                   candidate.Major == major && candidate.Minor == minor;
        }

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)) return false;
            if (IsWildcard(parts[1]) && IsWildcard(parts[2])) return candidate.Major == major;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) return false;
            if (IsWildcard(parts[2])) return candidate.Major == major && candidate.Minor == minor;
            return int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch) &&
                   candidate.Major == major && candidate.Minor == minor && candidate.Patch == patch;
        }

        return false;
    }

    private static bool IsWildcard(string token) =>
        string.Equals(token, "x", StringComparison.OrdinalIgnoreCase) || token == "*";
}

/// <summary>
/// Discovers installed side-by-side <c>re*</c> engines and resolves the highest compatible candidate.
/// </summary>
public sealed class EngineVersionResolver
{
    /// <summary>Enumerates ready side-by-side engines (plus alias equivalents when filtering) while skipping incomplete staging folders.</summary>
    public IReadOnlyList<InstalledEngineCandidate> DiscoverInstalledEngines(
        string enginesRootDir,
        string? engineIdFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);
        string fullRoot = Path.GetFullPath(enginesRootDir);
        if (!Directory.Exists(fullRoot)) return Array.Empty<InstalledEngineCandidate>();

        IReadOnlyList<string> allowedIds = string.IsNullOrWhiteSpace(engineIdFilter)
            ? Array.Empty<string>()
            : EngineAliasCatalog.GetEquivalentIds(engineIdFilter);

        var results = new List<InstalledEngineCandidate>();
        foreach (string engineDir in Directory.EnumerateDirectories(fullRoot))
        {
            string engineId = Path.GetFileName(engineDir);
            if (allowedIds.Count > 0 && !allowedIds.Contains(engineId, StringComparer.OrdinalIgnoreCase)) continue;

            foreach (string verDir in Directory.EnumerateDirectories(engineDir))
            {
                string folderName = Path.GetFileName(verDir);
                if (folderName.StartsWith(".staging-", StringComparison.OrdinalIgnoreCase)) continue;
                if (!SemVer.TryParse(folderName, out SemVer parsedVer)) continue;
                if (!IsReadyInstallation(verDir, parsedVer)) continue;

                string? exePath = LocateExecutable(verDir, engineId);
                if (exePath is null) continue;

                results.Add(new InstalledEngineCandidate(engineId, parsedVer, folderName, verDir, exePath));
            }
        }

        return results
            .OrderByDescending(c => !string.IsNullOrWhiteSpace(engineIdFilter) && string.Equals(c.EngineId, engineIdFilter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.EngineId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(c => c.Version)
            .ToList();
    }

    /// <summary>Resolves the highest compatible installed engine matching <paramref name="manifest"/> tracks and minimum version.</summary>
    public EngineResolutionResult ResolveForManifest(string enginesRootDir, RecompPackageManifest manifest) =>
        ResolveForManifest(enginesRootDir, manifest, settings: null);

    /// <summary>Resolves the highest compatible installed engine honoring pinned overrides, <c>supportedEngineTracks</c>, and aliases.</summary>
    public EngineResolutionResult ResolveForManifest(
        string enginesRootDir,
        RecompPackageManifest manifest,
        ReEngineSettings? settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);
        ArgumentNullException.ThrowIfNull(manifest);

        if (!SemVer.TryParse(manifest.EngineVersion, out SemVer minVersion))
        {
            return new EngineResolutionResult(false, null, Array.Empty<InstalledEngineCandidate>(), $"Invalid manifest engineVersion '{manifest.EngineVersion}'.");
        }

        IReadOnlyList<InstalledEngineCandidate> candidates = DiscoverInstalledEngines(enginesRootDir, manifest.EngineId);
        if (TryResolvePinned(candidates, manifest.EngineId, settings?.PinnedEngineVersions, out InstalledEngineCandidate? pinned))
        {
            return new EngineResolutionResult(true, pinned, candidates, $"Resolved pinned {pinned!.EngineId} v{pinned.Version}.");
        }

        IReadOnlyList<string> tracks = manifest.SupportedEngineTracks is { Count: > 0 }
            ? manifest.SupportedEngineTracks
            : [manifest.CompatibleEngineTrack];

        InstalledEngineCandidate? selected = null;
        string matchedTrack = manifest.CompatibleEngineTrack;
        foreach (string track in tracks)
        {
            selected = candidates
                .Where(c => EngineTrackMatcher.IsTrackCompatible(track, c.Version) && c.Version.CompareTo(minVersion) >= 0)
                .OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Version)
                .FirstOrDefault();
            if (selected is not null)
            {
                matchedTrack = track;
                break;
            }
        }

        if (selected is null)
        {
            return new EngineResolutionResult(
                false,
                null,
                candidates,
                $"No installed '{manifest.EngineId}' engine satisfies tracks [{string.Join(", ", tracks)}] and min '{minVersion}' under '{enginesRootDir}'.");
        }

        return new EngineResolutionResult(true, selected, candidates, $"Resolved {selected.EngineId} v{selected.Version} (track '{matchedTrack}', min '{minVersion}').");
    }

    private static bool TryResolvePinned(
        IReadOnlyList<InstalledEngineCandidate> candidates,
        string engineId,
        IDictionary<string, string>? pinnedMap,
        out InstalledEngineCandidate? pinned)
    {
        pinned = null;
        if (pinnedMap is null || pinnedMap.Count == 0) return false;
        foreach (string eqId in EngineAliasCatalog.GetEquivalentIds(engineId))
        {
            KeyValuePair<string, string> match = pinnedMap.FirstOrDefault(kvp => string.Equals(kvp.Key, eqId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Value) || !SemVer.TryParse(match.Value, out SemVer pinnedVer)) continue;
            pinned = candidates.FirstOrDefault(c => c.Version.CompareTo(pinnedVer) == 0);
            if (pinned is not null) return true;
        }
        return false;
    }

    private static bool IsReadyInstallation(string versionDir, SemVer version)
    {
        if (File.Exists(Path.Combine(versionDir, ".ready"))) return true;
        if (Directory.EnumerateFiles(versionDir, "*.part", SearchOption.TopDirectoryOnly).Any()) return false;
        return version.CompareTo(SemVer.Track030Threshold) < 0;
    }

    private static string? LocateExecutable(string versionDir, string engineId)
    {
        foreach (string id in EngineAliasCatalog.GetEquivalentIds(engineId))
        {
            string direct = Path.Combine(versionDir, $"{id}.exe");
            if (File.Exists(direct)) return direct;
            string appExe = Path.Combine(versionDir, "app", $"{id}.exe");
            if (File.Exists(appExe)) return appExe;
        }

        string manifestPath = Path.Combine(versionDir, "engine.manifest.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("entryExecutable", out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? rel = prop.GetString();
                if (!string.IsNullOrWhiteSpace(rel))
                {
                    string contained = AppEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(versionDir, rel.Trim())), versionDir);
                    if (File.Exists(contained)) return contained;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
        return null;
    }
}
