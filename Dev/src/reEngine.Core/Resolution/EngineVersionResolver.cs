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

    /// <summary>Compares two semantic versions using ecosystem precedence (<c>-hf.&lt;n&gt;</c> &gt; stable &gt; other prereleases).</summary>
    public int CompareTo(SemVer other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;
        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;
        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        (int leftRank, int leftHf) = ClassifySuffix(Prerelease);
        (int rightRank, int rightHf) = ClassifySuffix(other.Prerelease);
        cmp = leftRank.CompareTo(rightRank);
        if (cmp != 0) return cmp;
        if (leftRank == 2) return leftHf.CompareTo(rightHf);
        if (leftRank == 1) return 0;

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

    private static (int Rank, int HotfixNumber) ClassifySuffix(string? prerelease)
    {
        if (string.IsNullOrEmpty(prerelease)) return (1, 0);
        if (prerelease.StartsWith("hf.", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(prerelease.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int hfNumber) &&
            hfNumber > 0)
        {
            return (2, hfNumber);
        }
        return (0, 0);
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
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Safely reads and deserializes <c>engine.manifest.json</c> from <paramref name="versionDir"/> if present and valid.</summary>
    public static EngineManifestModel? TryReadEngineManifest(string versionDir)
    {
        if (string.IsNullOrWhiteSpace(versionDir)) return null;
        string manifestPath = Path.Combine(versionDir, "engine.manifest.json");
        if (!File.Exists(manifestPath)) return null;
        try
        {
            return JsonSerializer.Deserialize<EngineManifestModel>(File.ReadAllText(manifestPath), ManifestJsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

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

    /// <summary>Resolves the highest compatible installed engine matching <paramref name="manifest"/> minimum version and compatibility rules.</summary>
    public EngineResolutionResult ResolveForManifest(string enginesRootDir, RecompPackageManifest manifest) =>
        ResolveForManifest(enginesRootDir, manifest, settings: null);

    /// <summary>Resolves the highest compatible installed engine honoring pinned overrides, compatibility exclusions, and aliases.</summary>
    public EngineResolutionResult ResolveForManifest(
        string enginesRootDir,
        RecompPackageManifest manifest,
        ReEngineSettings? settings) =>
        ResolveForManifest(enginesRootDir, manifest, settings, bundledEnginesDir: null, excludedVersions: null);

    /// <summary>
    /// Resolves an installed <c>re*</c> engine following R01 §3.3 precedence:
    /// per-module pin -&gt; per-engine alias pin -&gt; latest compatible <c>.ready</c> release -&gt; bundled fallback.
    /// </summary>
    /// <param name="enginesRootDir">Root directory containing side-by-side <c>&lt;engineId&gt;\v&lt;Ver&gt;\</c> installations.</param>
    /// <param name="manifest">Loaded <c>.recomp</c> package manifest.</param>
    /// <param name="settings">Optional suite settings supplying <c>pinnedEngineVersions</c>.</param>
    /// <param name="bundledEnginesDir">Optional module-bundled engines root for <c>"bundled"</c> pins or fallback.</param>
    /// <param name="excludedVersions">Optional set of versions excluded after prior boot crashes.</param>
    public EngineResolutionResult ResolveForManifest(
        string enginesRootDir,
        RecompPackageManifest manifest,
        ReEngineSettings? settings,
        string? bundledEnginesDir = null,
        IReadOnlySet<SemVer>? excludedVersions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!SemVer.TryParse(manifest.EngineVersion, out SemVer minVersion))
            return new EngineResolutionResult(false, null, Array.Empty<InstalledEngineCandidate>(), $"Invalid manifest engineVersion '{manifest.EngineVersion}'.");

        IReadOnlyList<InstalledEngineCandidate> candidates = DiscoverInstalledEngines(enginesRootDir, manifest.EngineId);
        if (TryResolvePinned(candidates, manifest, minVersion, settings?.PinnedEngineVersions, bundledEnginesDir, excludedVersions, out InstalledEngineCandidate? pinned, out string pinDesc))
            return new EngineResolutionResult(true, pinned, candidates.Count > 0 ? candidates : [pinned!], pinDesc);

        InstalledEngineCandidate? selected = candidates
            .Where(c => c.Version.CompareTo(minVersion) >= 0 &&
                        !EngineCompatibilityEvaluator.IsVersionIncompatible(c.Version, manifest.IncompatibleEngineVersions) &&
                        !IsVersionExcluded(c.Version, excludedVersions))
            .OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.Version)
            .FirstOrDefault();
        if (selected is not null)
            return new EngineResolutionResult(true, selected, candidates, $"Resolved {selected.EngineId} v{selected.Version} (min '{minVersion}').");

        if (TrySelectBundledCandidate(bundledEnginesDir, manifest, minVersion, excludedVersions, out InstalledEngineCandidate? bundled))
            return new EngineResolutionResult(true, bundled, candidates.Count > 0 ? candidates : [bundled!], $"Resolved bundled fallback {bundled!.EngineId} v{bundled.Version} (min '{minVersion}').");

        return new EngineResolutionResult(false, null, candidates, $"No installed '{manifest.EngineId}' engine satisfies min '{minVersion}' (excluding incompatible/crashed builds) under '{enginesRootDir}'.");
    }

    /// <summary>
    /// Attempts to resolve a bundled runtime or exact declared target <c>engineVersion</c> fallback after <paramref name="failedVersion"/> crashes.
    /// </summary>
    /// <param name="enginesRootDir">Root directory containing installed side-by-side platform engines.</param>
    /// <param name="manifest">Loaded <c>.recomp</c> package manifest.</param>
    /// <param name="failedVersion">Engine version that crashed or exited abnormally.</param>
    /// <param name="bundledEnginesDir">Optional module-bundled engines root inspected before installed target fallback.</param>
    public EngineResolutionResult TryResolveBundledOrTargetFallback(
        string enginesRootDir,
        RecompPackageManifest manifest,
        SemVer failedVersion,
        string? bundledEnginesDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);
        ArgumentNullException.ThrowIfNull(manifest);
        if (!SemVer.TryParse(manifest.EngineVersion, out SemVer minVersion))
            return new EngineResolutionResult(false, null, Array.Empty<InstalledEngineCandidate>(), $"Invalid manifest engineVersion '{manifest.EngineVersion}'.");

        HashSet<SemVer> excluded = [failedVersion];
        if (TrySelectBundledCandidate(bundledEnginesDir, manifest, minVersion, excluded, out InstalledEngineCandidate? bundled))
            return new EngineResolutionResult(true, bundled, [bundled!], $"Resolved bundled fallback {bundled!.EngineId} v{bundled.Version} after v{failedVersion} failed.");

        IReadOnlyList<InstalledEngineCandidate> candidates = DiscoverInstalledEngines(enginesRootDir, manifest.EngineId);
        InstalledEngineCandidate? target = candidates
            .Where(c => c.Version.CompareTo(minVersion) == 0 &&
                        c.Version.CompareTo(failedVersion) != 0 &&
                        !EngineCompatibilityEvaluator.IsVersionIncompatible(c.Version, manifest.IncompatibleEngineVersions))
            .OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return target is not null
            ? new EngineResolutionResult(true, target, candidates, $"Resolved target fallback {target.EngineId} v{target.Version} after v{failedVersion} failed.")
            : new EngineResolutionResult(false, null, candidates, $"No bundled or target v{minVersion} fallback available for '{manifest.EngineId}' after v{failedVersion} failed.");
    }

    private bool TryResolvePinned(
        IReadOnlyList<InstalledEngineCandidate> candidates, RecompPackageManifest manifest, SemVer minVersion,
        IDictionary<string, string>? pinnedMap, string? bundledEnginesDir, IReadOnlySet<SemVer>? excludedVersions,
        out InstalledEngineCandidate? pinned, out string diagnostic)
    {
        pinned = null; diagnostic = string.Empty;
        if (!TryFindPinEntry(manifest, pinnedMap, out string pinKey, out string pinValue)) return false;
        if (string.Equals(pinValue, "latest", StringComparison.OrdinalIgnoreCase)) return false;

        if (string.Equals(pinValue, "bundled", StringComparison.OrdinalIgnoreCase))
        {
            if (TrySelectBundledCandidate(bundledEnginesDir, manifest, minVersion, excludedVersions, out pinned))
            {
                diagnostic = $"Resolved pinned bundled {pinned!.EngineId} v{pinned.Version} (pin='{pinKey}').";
                return true;
            }
            pinned = FindExactCandidate(candidates, manifest.EngineId, minVersion, excludedVersions);
            if (pinned is not null) { diagnostic = $"Resolved pinned bundled-target {pinned.EngineId} v{pinned.Version} (pin='{pinKey}')."; return true; }
            return false;
        }

        if (string.Equals(pinValue, "target", StringComparison.OrdinalIgnoreCase))
        {
            pinned = FindExactCandidate(candidates, manifest.EngineId, minVersion, excludedVersions);
            if (pinned is not null) { diagnostic = $"Resolved pinned target {pinned.EngineId} v{pinned.Version} (pin='{pinKey}')."; return true; }
            return false;
        }

        if (SemVer.TryParse(pinValue, out SemVer pinnedVer))
        {
            pinned = FindExactCandidate(candidates, manifest.EngineId, pinnedVer, excludedVersions);
            if (pinned is not null) { diagnostic = $"Resolved pinned {pinned.EngineId} v{pinned.Version} (pin='{pinKey}')."; return true; }
        }
        return false;
    }

    private static bool TryFindPinEntry(RecompPackageManifest manifest, IDictionary<string, string>? pinnedMap, out string pinKey, out string pinValue)
    {
        pinKey = string.Empty; pinValue = string.Empty;
        if (pinnedMap is null || pinnedMap.Count == 0) return false;
        IEnumerable<string> lookupOrder = string.IsNullOrWhiteSpace(manifest.ModuleId)
            ? EngineAliasCatalog.GetEquivalentIds(manifest.EngineId)
            : new[] { manifest.ModuleId.Trim() }.Concat(EngineAliasCatalog.GetEquivalentIds(manifest.EngineId));

        foreach (string keyCandidate in lookupOrder)
        {
            KeyValuePair<string, string> match = pinnedMap.FirstOrDefault(kvp => string.Equals(kvp.Key, keyCandidate, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(match.Value)) continue;
            pinKey = match.Key; pinValue = match.Value.Trim();
            return true;
        }
        return false;
    }

    private bool TrySelectBundledCandidate(
        string? bundledEnginesDir, RecompPackageManifest manifest, SemVer minVersion, IReadOnlySet<SemVer>? excludedVersions, out InstalledEngineCandidate? selected)
    {
        selected = null;
        if (string.IsNullOrWhiteSpace(bundledEnginesDir) || !Directory.Exists(bundledEnginesDir)) return false;

        List<InstalledEngineCandidate> valid = DiscoverInstalledEngines(bundledEnginesDir, manifest.EngineId)
            .Where(c => !IsVersionExcluded(c.Version, excludedVersions) &&
                        !EngineCompatibilityEvaluator.IsVersionIncompatible(c.Version, manifest.IncompatibleEngineVersions))
            .ToList();
        if (valid.Count == 0) return false;

        selected = valid.Where(c => c.Version.CompareTo(minVersion) == 0).OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
            ?? valid.Where(c => c.Version.CompareTo(minVersion) >= 0).OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase)).ThenByDescending(c => c.Version).FirstOrDefault()
            ?? valid.OrderByDescending(c => string.Equals(c.EngineId, manifest.EngineId, StringComparison.OrdinalIgnoreCase)).ThenByDescending(c => c.Version).FirstOrDefault();
        return selected is not null;
    }

    private static InstalledEngineCandidate? FindExactCandidate(IReadOnlyList<InstalledEngineCandidate> candidates, string engineId, SemVer targetVersion, IReadOnlySet<SemVer>? excludedVersions) =>
        IsVersionExcluded(targetVersion, excludedVersions)
            ? null
            : candidates.Where(c => c.Version.CompareTo(targetVersion) == 0).OrderByDescending(c => string.Equals(c.EngineId, engineId, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

    private static bool IsVersionExcluded(SemVer version, IReadOnlySet<SemVer>? excludedVersions)
    {
        if (excludedVersions is null || excludedVersions.Count == 0) return false;
        if (excludedVersions.Contains(version)) return true;
        foreach (SemVer ex in excludedVersions) { if (ex.CompareTo(version) == 0) return true; }
        return false;
    }

    private static bool IsReadyInstallation(string versionDir, SemVer version)
    {
        if (File.Exists(Path.Combine(versionDir, ".ready"))) return true;
        if (Directory.EnumerateFiles(versionDir, "*.part", SearchOption.TopDirectoryOnly).Any()) return false;
        if (version.CompareTo(SemVer.Track030Threshold) >= 0) return false;

        EngineManifestModel? manifest = TryReadEngineManifest(versionDir);
        if (manifest is null) return true;

        bool requiresReadyByProtocol =
            (SemVer.TryParse(manifest.HostProtocolVersion, out SemVer proto) && proto.CompareTo(SemVer.Track030Threshold) >= 0) ||
            (SemVer.TryParse(manifest.MinSuiteVersion, out SemVer minSuite) && minSuite.CompareTo(SemVer.Track030Threshold) >= 0);
        return !requiresReadyByProtocol;
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

        EngineManifestModel? manifest = TryReadEngineManifest(versionDir);
        string? entryRel = manifest?.EntryExecutable;
        if (string.IsNullOrWhiteSpace(entryRel)) return null;
        try
        {
            string contained = AppEnvironment.AssertPathContained(
                Path.GetFullPath(Path.Combine(versionDir, entryRel.Trim())),
                versionDir);
            return File.Exists(contained) ? contained : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
