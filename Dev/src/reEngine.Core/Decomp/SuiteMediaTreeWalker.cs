// @module: reEngine.Core.Decomp.SuiteMediaTreeWalker
// @purpose: Containment-safe suite-tree BYO media discovery, deterministic ranking/ambiguity detection, PS-X EXE sniffing, and .cue/.bin staging.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Security.Cryptography, System.Text, System.Text.RegularExpressions, System.Threading, System.Threading.Tasks, Foundation.Core
// @exports: SuiteMediaCandidate, SuiteMediaDiscoveryResult, StagedMediaResult, SuiteMediaTreeWalker

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Foundation.Core;

namespace reEngine.Core.Decomp;

/// <summary>Discovered BYO disc image, ROM, or PS-X executable candidate with ranking metadata.</summary>
public sealed record SuiteMediaCandidate(
    string FilePath, string GameDirectoryRoot, string FileExtension, long FileSizeBytes,
    int ExtensionPriority, bool IsModuleLocal, bool IsPsxExecutable,
    bool MatchesModuleSlug, bool MatchesExpectedSha256, string? Sha256Hex = null);

/// <summary>Result of walking module and suite trees for unambiguous or promptable BYO media.</summary>
public sealed record SuiteMediaDiscoveryResult(
    bool Found, bool IsAmbiguous, string? PrimaryMediaPath,
    SuiteMediaCandidate? SelectedCandidate, IReadOnlyList<SuiteMediaCandidate> Candidates, string DiagnosticMessage);

/// <summary>Result of staging selected media (and any <c>.cue</c> companion binaries) into <c>data\rom\</c>.</summary>
public sealed record StagedMediaResult(string PrimaryTargetPath, IReadOnlyList<string> StagedFiles);

/// <summary>Safely discovers and stages BYO disc/ROM media across module and suite root trees.</summary>
public static partial class SuiteMediaTreeWalker
{
    private const int MaxRecursionDepth = 5;
    private static readonly byte[] PsxExeMagic = Encoding.ASCII.GetBytes("PS-X EXE");
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        { "toolchain", "engines", "logs", "cache", ".git", "Archive", "Builds", "Releases", "backups", "config_history" };
    private static readonly HashSet<string> NonMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".txt", ".md", ".xml", ".yaml", ".yml", ".hash", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".log", ".state", ".recomp", ".decomp", ".dll", ".pdb", ".cs", ".c", ".h", ".cpp", ".bak", ".tmp", ".part", ".ini", ".cfg", ".sbi", ".sav", ".srm" };
    private static readonly Dictionary<string, int> ExtensionPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cue"] = 0, [".chd"] = 1, [".iso"] = 2, [".gcm"] = 3, [".rvz"] = 3, [".n64"] = 3, [".z64"] = 3, [".v64"] = 3,
        [".sfc"] = 3, [".smc"] = 3, [".nes"] = 3, [".gb"] = 3, [".gbc"] = 3, [".gba"] = 3, [".gen"] = 3, [".smd"] = 3,
        [".sms"] = 3, [".gg"] = 3, [".nds"] = 3, [".7z"] = 4, [".zip"] = 4, [".img"] = 4, [".bin"] = 5
    };

    [GeneratedRegex(@"^\s*FILE\s+(?:""(?<quoted>[^""]+)""|(?<bare>\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CueFileLineRegex();

    private sealed record WalkState(string? ModuleId, string? ExpectedSha, string? Boundary,
        HashSet<string> ScannedDirs, HashSet<string> SeenFiles, List<SuiteMediaCandidate> Candidates);

    /// <summary>Derives the suite root directory from <paramref name="enginesRootDir"/> or <paramref name="dataDirectory"/> within optional containment.</summary>
    public static string? DeriveSuiteRootDirectory(string? enginesRootDir, string? dataDirectory, string? containmentRoot = null)
    {
        string? boundary = string.IsNullOrWhiteSpace(containmentRoot) ? null : Path.GetFullPath(containmentRoot);
        if (!string.IsNullOrWhiteSpace(enginesRootDir) && GetNonVolumeParent(enginesRootDir) is string engParent && TryContain(engParent, boundary) is string safeEng)
            return safeEng;
        if (string.IsNullOrWhiteSpace(dataDirectory)) return null;
        string fullData = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(fullData), "data", StringComparison.OrdinalIgnoreCase) &&
            GetNonVolumeParent(fullData) is string dataParent && TryContain(dataParent, boundary) is string safeParent)
            return safeParent;
        return TryContain(fullData, boundary);
    }

    /// <summary>Discovers and deterministically ranks BYO media across module and suite root directories.</summary>
    public static SuiteMediaDiscoveryResult Discover(
        string? moduleRootDir, string? suiteRootDir, string? moduleId = null,
        string? expectedSha256 = null, string? preloadRomDir = null, string? containmentRoot = null)
    {
        string? boundary = string.IsNullOrWhiteSpace(containmentRoot) ? null : Path.GetFullPath(containmentRoot);
        string? modRoot = string.IsNullOrWhiteSpace(moduleRootDir) ? null : Contain(moduleRootDir.Trim(), boundary);
        string? suiteRoot = string.IsNullOrWhiteSpace(suiteRootDir) ? null : Contain(suiteRootDir.Trim(), boundary);
        var state = new WalkState(moduleId, string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256.Trim().ToLowerInvariant(),
            boundary, new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new());

        if (IsSafeDir(modRoot))
        {
            if (!string.IsNullOrWhiteSpace(preloadRomDir))
            {
                string rel = preloadRomDir.Trim();
                if (Path.IsPathRooted(rel)) throw new InvalidOperationException($"Fail-Closed Containment Violation: preloadRomDir '{rel}' must be relative.");
                string preloadDir = Contain(Path.Combine(modRoot!, rel.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)), modRoot!);
                ScanRoot(preloadDir, modRoot!, modRoot!, true, true, false, state);
            }
            ScanGameSubfolders(modRoot!, modRoot!, modRoot!, true, true, state);
        }

        if (IsSafeDir(suiteRoot))
        {
            string gamesDir = Path.Combine(suiteRoot!, "games");
            if (!string.IsNullOrWhiteSpace(moduleId) && Path.Combine(gamesDir, moduleId.Trim()) is string slugDir && HasSafeAncestors(slugDir, suiteRoot!))
                ScanGameSubfolders(slugDir, suiteRoot!, slugDir, false, true, state);
            if (IsSafeDir(gamesDir))
                foreach (string gameDir in Directory.EnumerateDirectories(gamesDir))
                    if (!ExcludedDirectories.Contains(Path.GetFileName(gameDir)) && !IsReparsePoint(gameDir))
                        ScanGameSubfolders(gameDir, suiteRoot!, gameDir, false, IsSlugMatch(Path.GetFileName(gameDir), moduleId), state);

            foreach (string root in new[] { Path.Combine(suiteRoot!, "data", "rom"), Path.Combine(suiteRoot!, "gamedata") })
                ScanRoot(root, suiteRoot!, root, false, false, true, state);
        }

        return RankCandidates(state.Candidates, moduleId);
    }

    /// <summary>Stages <paramref name="sourceMediaPath"/> (plus any <c>.cue</c> <c>FILE</c> companions) into <paramref name="targetRomDirectory"/>.</summary>
    public static async Task<StagedMediaResult> StageMediaIntoRomDirectoryAsync(
        string sourceMediaPath, string targetRomDirectory, string? containmentRoot = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRomDirectory);
        ct.ThrowIfCancellationRequested();

        string? boundary = string.IsNullOrWhiteSpace(containmentRoot) ? null : Path.GetFullPath(containmentRoot);
        string fullSource = Contain(sourceMediaPath, boundary);
        string fullTargetDir = Contain(targetRomDirectory, boundary);
        if (!File.Exists(fullSource)) throw new FileNotFoundException($"Source media file not found: '{fullSource}'.", fullSource);
        if (IsReparsePoint(fullSource) || IsReparsePoint(fullTargetDir))
            throw new InvalidOperationException($"Fail-Closed Containment Violation: symlink/reparse path disallowed ('{fullSource}' -> '{fullTargetDir}').");

        Directory.CreateDirectory(fullTargetDir);
        string primaryDest = Contain(Path.Combine(fullTargetDir, Path.GetFileName(fullSource)), fullTargetDir);
        await CopyIfDistinctAsync(fullSource, primaryDest, ct).ConfigureAwait(false);
        var staged = new List<string> { primaryDest };

        if (Path.GetExtension(fullSource).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            string sourceDir = Path.GetDirectoryName(fullSource) ?? throw new InvalidOperationException($"Cannot resolve directory for '{fullSource}'.");
            var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in await File.ReadAllLinesAsync(fullSource, ct).ConfigureAwait(false))
            {
                Match m = CueFileLineRegex().Match(line);
                if (!m.Success) continue;
                string rawRef = (m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value).Trim();
                if (string.IsNullOrWhiteSpace(rawRef)) continue;
                if (Path.IsPathRooted(rawRef) || rawRef.Contains(':', StringComparison.Ordinal))
                    throw new InvalidOperationException($"Fail-Closed Containment Violation: .cue FILE reference '{rawRef}' cannot be rooted.");
                string companionSrc = Contain(Contain(Path.Combine(sourceDir, rawRef), sourceDir), boundary);
                if (!copied.Add(companionSrc)) continue;
                if (!File.Exists(companionSrc)) throw new FileNotFoundException($"Missing .cue companion file '{companionSrc}'.", companionSrc);
                if (IsReparsePoint(companionSrc)) throw new InvalidOperationException($"Fail-Closed Containment Violation: symlink/reparse .cue companion '{companionSrc}' is disallowed.");
                string companionDest = Contain(Path.Combine(fullTargetDir, Path.GetRelativePath(sourceDir, companionSrc)), fullTargetDir);
                if (Path.GetDirectoryName(companionDest) is string destSubDir) Directory.CreateDirectory(destSubDir);
                await CopyIfDistinctAsync(companionSrc, companionDest, ct).ConfigureAwait(false);
                staged.Add(companionDest);
            }
        }
        return new StagedMediaResult(primaryDest, staged);
    }

    private static SuiteMediaDiscoveryResult RankCandidates(List<SuiteMediaCandidate> raw, string? moduleId)
    {
        if (raw.Count == 0) return new(false, false, null, null, Array.Empty<SuiteMediaCandidate>(), "No compatible BYO ROM, disc image, or PS-X EXE found.");
        List<SuiteMediaCandidate> ordered = raw.OrderByDescending(c => c.IsModuleLocal).ThenByDescending(c => c.MatchesExpectedSha256)
            .ThenByDescending(c => c.MatchesModuleSlug).ThenBy(c => c.ExtensionPriority).ThenBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase).ToList();

        if (ordered[0].IsModuleLocal) return new(true, false, ordered[0].FilePath, ordered[0], ordered, $"Selected module-local media '{ordered[0].FilePath}'.");
        if (ordered[0].MatchesExpectedSha256) return new(true, false, ordered[0].FilePath, ordered[0], ordered, $"Matched expected SHA-256 media '{ordered[0].FilePath}'.");

        List<SuiteMediaCandidate> slugged = ordered.Where(c => c.MatchesModuleSlug).ToList();
        if (slugged.Count > 0)
        {
            int slugGroups = slugged.Select(c => c.GameDirectoryRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return slugGroups == 1
                ? new(true, false, slugged[0].FilePath, slugged[0], ordered, $"Matched module slug '{moduleId}' media '{slugged[0].FilePath}'.")
                : new(false, true, null, null, slugged, $"Ambiguous media: {slugGroups} directories match slug '{moduleId}'.");
        }

        int distinctGroups = ordered.Select(c => c.GameDirectoryRoot).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return distinctGroups > 1
            ? new(false, true, null, null, ordered, $"Ambiguous suite media: found candidates in {distinctGroups} distinct directories; select a disc/ROM image.")
            : new(true, false, ordered[0].FilePath, ordered[0], ordered, $"Selected unambiguous suite media '{ordered[0].FilePath}'.");
    }

    private static void ScanGameSubfolders(string gameRoot, string boundaryAncestor, string group, bool isLocal, bool slugMatch, WalkState state)
    {
        foreach (string leaf in new[] { "rom", "gamedata" })
            ScanRoot(Path.Combine(gameRoot, "data", leaf), boundaryAncestor, group, isLocal, slugMatch, false, state);
    }

    private static void ScanRoot(string rootDir, string boundaryAncestor, string defaultGroup, bool isLocal, bool defaultSlug, bool groupBySubdir, WalkState state)
    {
        string fullRoot = Contain(rootDir, state.Boundary);
        if (!state.ScannedDirs.Add(fullRoot) || !Directory.Exists(fullRoot) || !HasSafeAncestors(fullRoot, boundaryAncestor)) return;
        WalkDirectory(fullRoot, fullRoot, defaultGroup, isLocal, defaultSlug, groupBySubdir, 0, state);
    }

    private static void WalkDirectory(string currentDir, string walkRoot, string defaultGroup, bool isLocal, bool defaultSlug, bool groupBySubdir, int depth, WalkState state)
    {
        if (depth > MaxRecursionDepth || IsReparsePoint(currentDir)) return;
        foreach (string file in Directory.EnumerateFiles(currentDir))
        {
            if (IsReparsePoint(file)) continue;
            string fullFile = Contain(file, state.Boundary);
            if (state.SeenFiles.Contains(fullFile)) continue;

            string ext = Path.GetExtension(fullFile).ToLowerInvariant();
            bool isPsx = false;
            if (!ExtensionPriority.TryGetValue(ext, out int prio))
            {
                if (NonMediaExtensions.Contains(ext) || !HasPsxExeMagic(fullFile)) continue;
                isPsx = true; prio = 3;
            }
            state.SeenFiles.Add(fullFile);
            string? sha = state.ExpectedSha is not null ? ComputeSha256Hex(fullFile) : null;
            bool shaMatch = sha is not null && string.Equals(sha, state.ExpectedSha, StringComparison.OrdinalIgnoreCase);
            string group = groupBySubdir ? ResolveSubdirGroup(walkRoot, fullFile, defaultGroup) : defaultGroup;
            bool slugMatch = defaultSlug || (groupBySubdir && !string.Equals(group, defaultGroup, StringComparison.OrdinalIgnoreCase) && IsSlugMatch(Path.GetFileName(group), state.ModuleId));
            state.Candidates.Add(new SuiteMediaCandidate(fullFile, group, ext, new FileInfo(fullFile).Length, prio, isLocal, isPsx, slugMatch, shaMatch, sha));
        }
        if (depth >= MaxRecursionDepth) return;
        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
            if (!ExcludedDirectories.Contains(Path.GetFileName(subDir)) && !IsReparsePoint(subDir))
                WalkDirectory(subDir, walkRoot, defaultGroup, isLocal, defaultSlug, groupBySubdir, depth + 1, state);
    }

    private static string ResolveSubdirGroup(string walkRoot, string fullFile, string fallback)
    {
        string rel = Path.GetRelativePath(walkRoot, fullFile);
        int sep = rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return sep > 0 ? Path.Combine(walkRoot, rel[..sep]) : fallback;
    }

    private static bool HasPsxExeMagic(string filePath)
    {
        try { Span<byte> buf = stackalloc byte[8]; using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return fs.Read(buf) == 8 && buf.SequenceEqual(PsxExeMagic); }
        catch { return false; }
    }

    private static string? ComputeSha256Hex(string filePath)
    {
        try { using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant(); }
        catch { return null; }
    }

    private static async Task CopyIfDistinctAsync(string src, string dest, CancellationToken ct)
    {
        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return;
        await using var inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var outStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await inStream.CopyToAsync(outStream, ct).ConfigureAwait(false);
    }

    private static string Contain(string path, string? boundary) =>
        boundary is not null ? AppEnvironment.AssertPathContained(Path.GetFullPath(path), boundary) : Path.GetFullPath(path);

    private static string? TryContain(string path, string? boundary)
    {
        try { return Contain(path, boundary); } catch (InvalidOperationException) { return null; }
    }

    private static bool HasSafeAncestors(string targetDir, string stopAncestor)
    {
        string? cur = Path.GetFullPath(targetDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string stop = Path.GetFullPath(stopAncestor).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrEmpty(cur))
        {
            if (Directory.Exists(cur) && IsReparsePoint(cur)) return false;
            if (string.Equals(cur, stop, StringComparison.OrdinalIgnoreCase)) return true;
            if (ExcludedDirectories.Contains(Path.GetFileName(cur))) return false;
            cur = Path.GetDirectoryName(cur);
        }
        return true;
    }

    private static bool IsSafeDir(string? path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !IsReparsePoint(path);
    private static bool IsReparsePoint(string path) => (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string? GetNonVolumeParent(string path)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string? root = string.IsNullOrWhiteSpace(parent) ? null : Path.GetPathRoot(parent)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(parent) && !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ? parent : null;
    }

    private static bool IsSlugMatch(string folderName, string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(folderName)) return false;
        if (string.Equals(folderName.Trim(), moduleId.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        static string Strip(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        string a = Strip(folderName);
        return a.Length > 0 && string.Equals(a, Strip(moduleId), StringComparison.Ordinal);
    }
}
