// @module: reEngine.Core.Execution.SessionRecoveryLocator
// @purpose: Scans path-contained module data and save roots (skipping symlinks) for the newest pre-crash checkpoint, quicksave, or Save Vault backup.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core
// @exports: SessionRecoveryCandidate, SessionRecoveryLocator

using Foundation.Core;

namespace reEngine.Core.Execution;

/// <summary>
/// Result of scanning module data and save folders for the newest pre-crash recovery artifact.
/// </summary>
/// <param name="Found"><see langword="true"/> when a recovery candidate was located.</param>
/// <param name="FilePath">Full contained path to the newest recovery artifact, or <see langword="null"/>.</param>
/// <param name="Kind">Artifact classification (<c>checkpoint</c>, <c>quickResume</c>, <c>quickSave</c>, <c>saveVaultBackup</c>, <c>saveFile</c>, or <c>none</c>).</param>
/// <param name="ModifiedAtUtc">Last-write UTC timestamp of the located recovery artifact.</param>
/// <param name="DiagnosticMessage">Human-readable summary describing the scan outcome.</param>
public sealed record SessionRecoveryCandidate(
    bool Found,
    string? FilePath,
    string Kind,
    DateTime? ModifiedAtUtc,
    string DiagnosticMessage);

/// <summary>
/// Locates the closest pre-crash play-state recovery artifact under <c>&lt;moduleDataDir&gt;</c> and optional <c>&lt;saveDir&gt;</c>.
/// </summary>
public static class SessionRecoveryLocator
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".sav", ".mcr", ".srm", ".json", ".state" };
    private static readonly HashSet<string> IgnoredTopSubdirectories = new(StringComparer.OrdinalIgnoreCase)
        { "config", "config_history", "quarantine", "cache", "logs", "screenshots", "hdpacks" };

    /// <summary>
    /// Scans <paramref name="moduleDataDir"/> and optional <paramref name="saveDir"/> for the newest pre-crash save or checkpoint.
    /// </summary>
    /// <param name="moduleDataDir">Isolated module data directory root.</param>
    /// <param name="saveDir">Optional external save directory override.</param>
    /// <param name="containmentRoot">Optional sandbox containment root enforced fail-closed.</param>
    public static SessionRecoveryCandidate FindClosestRecovery(
        string moduleDataDir,
        string? saveDir = null,
        string? containmentRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleDataDir);
        string? boundary = string.IsNullOrWhiteSpace(containmentRoot) ? null : Path.GetFullPath(containmentRoot.Trim());
        string moduleRoot = ContainRoot(moduleDataDir, boundary);
        var roots = new List<string> { moduleRoot };
        if (!string.IsNullOrWhiteSpace(saveDir))
        {
            string saveRoot = ContainRoot(saveDir, boundary);
            if (!string.Equals(saveRoot, moduleRoot, StringComparison.OrdinalIgnoreCase)) roots.Add(saveRoot);
        }

        string? bestPath = null;
        string bestKind = "none";
        DateTime bestUtc = DateTime.MinValue;
        int bestPriority = -1;

        foreach (string root in roots)
        {
            foreach ((string filePath, string kind, DateTime modifiedUtc, int priority) in ScanRoot(root))
            {
                bool isNewer = bestPath is null || modifiedUtc > bestUtc ||
                    (modifiedUtc == bestUtc && (priority > bestPriority ||
                    (priority == bestPriority && StringComparer.OrdinalIgnoreCase.Compare(filePath, bestPath) < 0)));
                if (!isNewer) continue;
                bestPath = filePath; bestKind = kind; bestUtc = modifiedUtc; bestPriority = priority;
            }
        }

        return bestPath is null
            ? new SessionRecoveryCandidate(false, null, "none", null, "No pre-crash checkpoint, quicksave, or save backup found.")
            : new SessionRecoveryCandidate(true, bestPath, bestKind, bestUtc, $"Found {bestKind} recovery artifact '{Path.GetFileName(bestPath)}' ({bestUtc:yyyy-MM-ddTHH:mm:ssZ}).");
    }

    private static string ContainRoot(string raw, string? boundary)
    {
        string full = Path.GetFullPath(raw.Trim());
        return boundary is not null ? AppEnvironment.AssertPathContained(full, boundary) : full;
    }

    private static IEnumerable<(string FilePath, string Kind, DateTime ModifiedUtc, int Priority)> ScanRoot(string rootDir)
    {
        if (!Directory.Exists(rootDir) || IsReparsePoint(new DirectoryInfo(rootDir))) yield break;
        var stack = new Stack<string>();
        stack.Push(rootDir);
        while (stack.Count > 0)
        {
            string currentDir = stack.Pop();
            foreach (string subDir in SafeEnumerate(currentDir, dirs: true))
            {
                if (string.Equals(currentDir, rootDir, StringComparison.OrdinalIgnoreCase) && IgnoredTopSubdirectories.Contains(Path.GetFileName(subDir))) continue;
                var dirInfo = new DirectoryInfo(subDir);
                if (!IsReparsePoint(dirInfo)) stack.Push(AppEnvironment.AssertPathContained(dirInfo.FullName, rootDir));
            }
            foreach (string file in SafeEnumerate(currentDir, dirs: false))
            {
                var info = new FileInfo(file);
                if (IsReparsePoint(info) || !SupportedExtensions.Contains(info.Extension)) continue;
                string contained = AppEnvironment.AssertPathContained(info.FullName, rootDir);
                string rel = Path.GetRelativePath(rootDir, contained).Replace('/', '\\');
                if (!IsMatchingArtifact(info.Name, rel)) continue;
                (string kind, int prio) = ClassifyArtifact(info.Name, rel);
                yield return (contained, kind, info.LastWriteTimeUtc, prio);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string dir, bool dirs)
    {
        try { return dirs ? Directory.EnumerateDirectories(dir) : Directory.EnumerateFiles(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static bool IsMatchingArtifact(string name, string normRel) =>
        name.StartsWith("quicksave", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("checkpoint", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("quickresume", StringComparison.OrdinalIgnoreCase) ||
        normRel.StartsWith("backups\\", StringComparison.OrdinalIgnoreCase) ||
        normRel.StartsWith("saves\\", StringComparison.OrdinalIgnoreCase) ||
        normRel.Contains("\\backups\\", StringComparison.OrdinalIgnoreCase) ||
        normRel.Contains("\\saves\\", StringComparison.OrdinalIgnoreCase);

    private static (string Kind, int Priority) ClassifyArtifact(string name, string normRel)
    {
        if (name.StartsWith("checkpoint", StringComparison.OrdinalIgnoreCase)) return ("checkpoint", 4);
        if (name.StartsWith("quickresume", StringComparison.OrdinalIgnoreCase)) return ("quickResume", 3);
        if (name.StartsWith("quicksave", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".state", StringComparison.OrdinalIgnoreCase)) return ("quickSave", 2);
        if (normRel.Contains("backups\\", StringComparison.OrdinalIgnoreCase) || name.Contains("backup", StringComparison.OrdinalIgnoreCase)) return ("saveVaultBackup", 1);
        return ("saveFile", 0);
    }

    private static bool IsReparsePoint(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
}
