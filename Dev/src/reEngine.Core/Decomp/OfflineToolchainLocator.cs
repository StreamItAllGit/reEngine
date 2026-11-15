// @module: reEngine.Core.Decomp.OfflineToolchainLocator
// @purpose: Offline Ghidra + jlink OpenJDK + THIRDPARTY_LICENSES.txt locator and Full vs Lightweight profile validator with zero runtime network calls.
// @depends-on: System, System.Collections.Generic, System.IO, reEngine.Core.Manifests
// @exports: ReEngineInstallProfile, OfflineToolchainStatus, OfflineToolchainLocator

using reEngine.Core.Manifests;

namespace reEngine.Core.Decomp;

/// <summary>
/// Installation footprint profile determining whether offline decompilation tools are bundled.
/// </summary>
public enum ReEngineInstallProfile
{
    /// <summary>Compact router-only footprint without bundled Ghidra or OpenJDK.</summary>
    Lightweight = 0,

    /// <summary>Full offline bundle including stripped Apache-2.0 Ghidra, jlink OpenJDK, and THIRDPARTY_LICENSES.txt.</summary>
    Full = 1
}

/// <summary>
/// Resolved status of the offline <c>toolchain\</c> directory.
/// </summary>
public sealed record OfflineToolchainStatus(
    bool IsAvailable,
    ReEngineInstallProfile Profile,
    string ToolchainDirectory,
    string? GhidraDirectory,
    string? AnalyzeHeadlessPath,
    string? JdkDirectory,
    string? JavaExecutablePath,
    string? ThirdPartyLicensesPath,
    bool LicensesPresent,
    bool GplDirectoryStripped,
    string DiagnosticMessage);

/// <summary>
/// Locates and validates the offline <c>toolchain\</c> (<c>ghidra\</c> + <c>jdk\</c> + <c>THIRDPARTY_LICENSES.txt</c>)
/// with zero runtime internet calls.
/// </summary>
public static class OfflineToolchainLocator
{
    /// <summary>Default maximum footprint budget for the <c>Full</c> offline toolchain in megabytes.</summary>
    public const int MaxToolchainBudgetMb = 160;

    /// <summary>Canonical license notice filename stored alongside the offline toolchain.</summary>
    public const string ThirdPartyLicensesFileName = "THIRDPARTY_LICENSES.txt";

    /// <summary>
    /// Resolves the <c>toolchain\</c> directory from an explicit override, an install root, or its suite parent.
    /// </summary>
    public static string ResolveToolchainDirectory(string baseOrInstallDir, string? explicitToolchainDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitToolchainDir))
        {
            return Path.GetFullPath(explicitToolchainDir.Trim());
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseOrInstallDir);
        string fullBase = Path.GetFullPath(baseOrInstallDir.Trim());
        if (string.Equals(Path.GetFileName(fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                "toolchain", StringComparison.OrdinalIgnoreCase))
        {
            return fullBase;
        }

        string trimmedBase = fullBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string direct = Path.Combine(trimmedBase, "toolchain");
        string? parent = Path.GetDirectoryName(trimmedBase);
        string? suiteToolchain = !string.IsNullOrWhiteSpace(parent) &&
            !string.Equals(parent, Path.GetPathRoot(parent), StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(parent, "toolchain")
            : null;

        if (!string.IsNullOrWhiteSpace(suiteToolchain) && Directory.Exists(suiteToolchain))
        {
            return suiteToolchain;
        }

        if (Directory.Exists(direct))
        {
            return direct;
        }

        return suiteToolchain ?? direct;
    }

    /// <summary>
    /// Validates offline toolchain availability for the active <paramref name="profile"/> without network access.
    /// </summary>
    public static OfflineToolchainStatus Locate(
        string baseOrInstallDir,
        ReEngineInstallProfile profile = ReEngineInstallProfile.Full,
        string? explicitToolchainDir = null,
        DecompToolchainRequirements? requirements = null)
    {
        string toolchainDir = ResolveToolchainDirectory(baseOrInstallDir, explicitToolchainDir);
        bool dirExists = Directory.Exists(toolchainDir);

        if (profile == ReEngineInstallProfile.Lightweight && string.IsNullOrWhiteSpace(explicitToolchainDir) && !dirExists)
        {
            return new OfflineToolchainStatus(
                IsAvailable: false,
                Profile: profile,
                ToolchainDirectory: toolchainDir,
                GhidraDirectory: null,
                AnalyzeHeadlessPath: null,
                JdkDirectory: null,
                JavaExecutablePath: null,
                ThirdPartyLicensesPath: null,
                LicensesPresent: false,
                GplDirectoryStripped: true,
                DiagnosticMessage:
                    "Lightweight profile active: offline Ghidra + OpenJDK toolchain is not bundled. " +
                    "Re-run installer with --profile Full or pass --toolchain-dir <path> for offline D2 decompilation.");
        }

        string ghidraDir = Path.Combine(toolchainDir, "ghidra");
        string jdkDir = Path.Combine(toolchainDir, "jdk");
        string licensesPath = Path.Combine(toolchainDir, ThirdPartyLicensesFileName);

        string? analyzeHeadless = FindFirstExisting(
            Path.Combine(ghidraDir, "support", "analyzeHeadless.bat"),
            Path.Combine(ghidraDir, "support", "analyzeHeadless"));
        string? javaExe = FindFirstExisting(
            Path.Combine(jdkDir, "bin", "java.exe"),
            Path.Combine(jdkDir, "bin", "java"));

        bool licensesPresent = File.Exists(licensesPath);
        bool gplStripped = !Directory.Exists(Path.Combine(ghidraDir, "GPL"));
        bool requiresJdk = requirements?.RequiresHeadlessJdk ?? true;

        var missing = new List<string>();
        if (!dirExists) missing.Add($"toolchain directory '{toolchainDir}'");
        if (analyzeHeadless is null) missing.Add("ghidra\\support\\analyzeHeadless(.bat)");
        if (requiresJdk && javaExe is null) missing.Add("jdk\\bin\\java(.exe)");
        if (!licensesPresent) missing.Add(ThirdPartyLicensesFileName);

        if (missing.Count > 0 || !gplStripped)
        {
            string gplNote = gplStripped ? string.Empty : " Detected unstripped ghidra\\GPL\\ directory.";
            string msg = missing.Count > 0
                ? $"Offline toolchain incomplete under '{toolchainDir}'; missing: {string.Join(", ", missing)}.{gplNote}"
                : $"Offline toolchain failed clean Apache-2.0 check under '{toolchainDir}':{gplNote}";

            return new OfflineToolchainStatus(
                IsAvailable: false,
                Profile: profile,
                ToolchainDirectory: toolchainDir,
                GhidraDirectory: Directory.Exists(ghidraDir) ? ghidraDir : null,
                AnalyzeHeadlessPath: analyzeHeadless,
                JdkDirectory: Directory.Exists(jdkDir) ? jdkDir : null,
                JavaExecutablePath: javaExe,
                ThirdPartyLicensesPath: licensesPresent ? licensesPath : null,
                LicensesPresent: licensesPresent,
                GplDirectoryStripped: gplStripped,
                DiagnosticMessage: msg);
        }

        return new OfflineToolchainStatus(
            IsAvailable: true,
            Profile: profile,
            ToolchainDirectory: toolchainDir,
            GhidraDirectory: ghidraDir,
            AnalyzeHeadlessPath: analyzeHeadless,
            JdkDirectory: jdkDir,
            JavaExecutablePath: javaExe,
            ThirdPartyLicensesPath: licensesPath,
            LicensesPresent: true,
            GplDirectoryStripped: true,
            DiagnosticMessage: $"Offline toolchain verified at '{toolchainDir}' (Ghidra headless + jlink OpenJDK + {ThirdPartyLicensesFileName}).");
    }

    private static string? FindFirstExisting(params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
