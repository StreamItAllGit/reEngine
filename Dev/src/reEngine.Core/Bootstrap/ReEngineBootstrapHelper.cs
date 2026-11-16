// @module: reEngine.Core.Bootstrap.ReEngineBootstrapHelper
// @purpose: Reusable zero-PII self-bootstrapping helper consumed by re* and <Game>Recomp installers.
// @depends-on: System, System.Diagnostics, System.IO, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution
// @exports: ReEngineBootstrapHelper

using System.Diagnostics;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Resolution;

namespace reEngine.Core.Bootstrap;

/// <summary>
/// Reusable SDK helper exported via <c>SharedLibs\reEngine</c> so child <c>re*</c> and <c>&lt;Game&gt;Recomp</c>
/// installers can deploy/upgrade <c>reEngine</c> idempotently and install side-by-side platform engines.
/// </summary>
public static class ReEngineBootstrapHelper
{
    /// <summary>
    /// Resolves the target <c>reEngine</c> installation root while enforcing fail-closed containment under
    /// <paramref name="isolatedTest"/> / <c>APP_ISOLATED_TEST=1</c> and zero literal host paths in compiled binaries.
    /// </summary>
    public static string ResolveDefaultReEngineInstallDir(
        string? explicitDir = null,
        string? isolatedDataDir = null,
        bool isolatedTest = false)
    {
        if (isolatedTest)
        {
            if (string.IsNullOrWhiteSpace(isolatedDataDir))
            {
                throw new InvalidOperationException(
                    "Fail-Closed Isolation Violation: isolated test mode requires isolatedDataDir when resolving reEngine install root.");
            }

            string fullIsolatedRoot = Path.GetFullPath(isolatedDataDir);
            string candidate = string.IsNullOrWhiteSpace(explicitDir)
                ? Path.Combine(fullIsolatedRoot, ReEnginePackagingConstants.DisplayName)
                : Path.GetFullPath(explicitDir);

            return AppEnvironment.AssertPathContained(candidate, fullIsolatedRoot);
        }

        if (!string.IsNullOrWhiteSpace(isolatedDataDir))
        {
            string fullIsolatedRoot = Path.GetFullPath(isolatedDataDir);
            string candidate = string.IsNullOrWhiteSpace(explicitDir)
                ? Path.Combine(fullIsolatedRoot, ReEnginePackagingConstants.DisplayName)
                : Path.GetFullPath(explicitDir);
            return AppEnvironment.AssertPathContained(candidate, fullIsolatedRoot);
        }

        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return Path.GetFullPath(explicitDir);
        }

        string? envHome = Environment.GetEnvironmentVariable("REENGINE_HOME");
        if (!string.IsNullOrWhiteSpace(envHome))
        {
            return Path.GetFullPath(envHome.Trim());
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Local");
        }

        return Path.GetFullPath(Path.Combine(
            localAppData,
            "Programs",
            ReEnginePackagingConstants.StudioName,
            ReEnginePackagingConstants.SuiteName,
            ReEnginePackagingConstants.DisplayName));
    }

    /// <summary>
    /// Resolves the canonical suite umbrella directory (<c>%LOCALAPPDATA%\Programs\StreamItAll\reEngine\</c>)
    /// while enforcing fail-closed containment under <paramref name="isolatedTest"/> / <c>APP_ISOLATED_TEST=1</c>.
    /// </summary>
    public static string ResolveDefaultSuiteInstallDir(
        string? explicitSuiteDir = null,
        string? isolatedDataDir = null,
        bool isolatedTest = false)
    {
        if (isolatedTest)
        {
            if (string.IsNullOrWhiteSpace(isolatedDataDir))
            {
                throw new InvalidOperationException(
                    "Fail-Closed Isolation Violation: isolated test mode requires isolatedDataDir when resolving suite root.");
            }

            string fullIsolatedRoot = Path.GetFullPath(isolatedDataDir);
            string candidate = string.IsNullOrWhiteSpace(explicitSuiteDir)
                ? Path.Combine(fullIsolatedRoot, ReEnginePackagingConstants.SuiteName)
                : Path.GetFullPath(explicitSuiteDir);

            return AppEnvironment.AssertPathContained(candidate, fullIsolatedRoot);
        }

        if (!string.IsNullOrWhiteSpace(isolatedDataDir))
        {
            string fullIsolatedRoot = Path.GetFullPath(isolatedDataDir);
            string candidate = string.IsNullOrWhiteSpace(explicitSuiteDir)
                ? Path.Combine(fullIsolatedRoot, ReEnginePackagingConstants.SuiteName)
                : Path.GetFullPath(explicitSuiteDir);
            return AppEnvironment.AssertPathContained(candidate, fullIsolatedRoot);
        }

        if (!string.IsNullOrWhiteSpace(explicitSuiteDir))
        {
            return Path.GetFullPath(explicitSuiteDir);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "Local");
        }

        return Path.GetFullPath(Path.Combine(
            localAppData,
            "Programs",
            ReEnginePackagingConstants.StudioName,
            ReEnginePackagingConstants.SuiteName));
    }

    /// <summary>
    /// Returns the offline Ghidra + OpenJDK <c>toolchain\</c> sibling directory under <c>suiteInstallDir</c>
    /// (falling back to <paramref name="reEngineInstallDir"/>\toolchain for isolated portable runs).
    /// </summary>
    public static string ResolveToolchainDir(string reEngineInstallDir) =>
        ResolveSuiteSiblingDir(reEngineInstallDir, "toolchain");

    /// <summary>
    /// Returns the side-by-side <c>engines\</c> sibling directory under <c>suiteInstallDir</c>
    /// (falling back to <paramref name="reEngineInstallDir"/>\engines for isolated portable runs).
    /// </summary>
    public static string ResolveEnginesRootDir(string reEngineInstallDir) =>
        ResolveSuiteSiblingDir(reEngineInstallDir, "engines");

    private static string ResolveSuiteSiblingDir(string reEngineInstallDir, string siblingFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reEngineInstallDir);
        string fullInstallDir = Path.GetFullPath(reEngineInstallDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string direct = Path.Combine(fullInstallDir, siblingFolder);

        string? parentDir = Path.GetDirectoryName(fullInstallDir);
        if (!string.IsNullOrWhiteSpace(parentDir) &&
            !string.Equals(parentDir, Path.GetPathRoot(parentDir), StringComparison.OrdinalIgnoreCase))
        {
            string peer = Path.Combine(parentDir, siblingFolder);
            if (Directory.Exists(peer))
            {
                return peer;
            }

            if (!Directory.Exists(direct))
            {
                return peer;
            }
        }

        return direct;
    }

    /// <summary>
    /// Returns the immutable side-by-side engine folder path:
    /// <c>&lt;reEngineInstallDir&gt;\engines\&lt;engineId&gt;\v&lt;version&gt;</c>.
    /// </summary>
    public static string BuildEngineVersionInstallDir(string reEngineInstallDir, string engineId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reEngineInstallDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        string cleanVer = version.Trim().TrimStart('v', 'V');
        return Path.Combine(ResolveEnginesRootDir(reEngineInstallDir), engineId.Trim(), $"v{cleanVer}");
    }

    /// <summary>
    /// Ensures <c>reEngine</c> is installed at <paramref name="targetReEngineDir"/> at or above the packaged SDK version,
    /// invoking <paramref name="bundledInstallerPath"/> silently via <see cref="JobObjectGuard"/> when needed.
    /// </summary>
    public static async Task<ReEngineBootstrapResult> EnsureReEngineInstalledAsync(
        string? bundledInstallerPath,
        string? targetReEngineDir,
        bool registerAssociation,
        bool isolatedTest,
        string? isolatedDataDir,
        CancellationToken ct = default)
    {
        bool effectiveIsolation = isolatedTest || IsIsolatedTestEnv();
        string installDir = ResolveDefaultReEngineInstallDir(targetReEngineDir, isolatedDataDir, effectiveIsolation);
        string enginesDir = ResolveEnginesRootDir(installDir);

        SemVer.TryParse(ReEnginePackagingConstants.Version, out SemVer targetVersion);
        SemVer? installedVersion = ReadInstalledVersion(installDir);
        bool needsInstall = installedVersion is null || installedVersion.Value.CompareTo(targetVersion) < 0;
        bool installedOrUpgraded = false;

        if (needsInstall)
        {
            if (string.IsNullOrWhiteSpace(bundledInstallerPath) || !File.Exists(bundledInstallerPath))
            {
                return new ReEngineBootstrapResult(
                    false,
                    installDir,
                    enginesDir,
                    false,
                    false,
                    $"Bundled reEngine installer not found at '{bundledInstallerPath}'.");
            }

            int installerExit = await RunInstallerAsync(
                bundledInstallerPath, installDir, effectiveIsolation, isolatedDataDir, ct).ConfigureAwait(false);
            if (installerExit != 0)
            {
                return new ReEngineBootstrapResult(
                    false,
                    installDir,
                    enginesDir,
                    false,
                    false,
                    $"Bundled reEngine installer exited with code {installerExit}.");
            }
            installedOrUpgraded = true;
        }

        bool assocRegistered = false;
        if (registerAssociation)
        {
            assocRegistered = await TryRegisterAssociationAsync(installDir, effectiveIsolation, isolatedDataDir, ct).ConfigureAwait(false);
        }

        string summary = installedOrUpgraded
            ? $"Installed/upgraded reEngine v{ReEnginePackagingConstants.Version} at '{installDir}'."
            : $"Verified reEngine v{installedVersion} at '{installDir}'.";

        return new ReEngineBootstrapResult(true, installDir, enginesDir, installedOrUpgraded, assocRegistered, summary);
    }

    internal static SemVer? ReadInstalledVersion(string installDir)
    {
        string appVersionTxt = Path.Combine(installDir, "app", "version.txt");
        if (TryReadVersionTextFile(appVersionTxt, out SemVer ver))
        {
            return ver;
        }

        foreach (string manifestCandidate in new[]
        {
            Path.Combine(installDir, "app", "install-manifest.json"),
            Path.Combine(installDir, ".install_manifest.json")
        })
        {
            if (TryReadManifestVersion(manifestCandidate, out ver))
            {
                return ver;
            }
        }

        string rootVersionTxt = Path.Combine(installDir, "version.txt");
        return TryReadVersionTextFile(rootVersionTxt, out ver) ? ver : null;
    }

    private static bool TryReadVersionTextFile(string path, out SemVer version)
    {
        version = default;
        if (!File.Exists(path)) return false;
        try
        {
            return SemVer.TryParse(File.ReadAllText(path).Trim(), out version);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadManifestVersion(string path, out SemVer version)
    {
        version = default;
        if (!File.Exists(path)) return false;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("version", out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                return SemVer.TryParse(el.GetString(), out version);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunInstallerAsync(
        string installerPath,
        string installDir,
        bool isolatedTest,
        string? isolatedDataDir,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(installerPath),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--no-launch");
        psi.ArgumentList.Add("--no-shortcuts");
        psi.ArgumentList.Add("--install-dir");
        psi.ArgumentList.Add(installDir);
        if (isolatedTest)
        {
            psi.Environment["APP_ISOLATED_TEST"] = "1";
        }
        if (!string.IsNullOrWhiteSpace(isolatedDataDir))
        {
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(Path.GetFullPath(isolatedDataDir));
        }

        return await RunGuardedProcessAsync(psi, ct).ConfigureAwait(false);
    }

    private static async Task<bool> TryRegisterAssociationAsync(
        string installDir,
        bool isolatedTest,
        string? isolatedDataDir,
        CancellationToken ct)
    {
        string exe = Path.Combine(installDir, "reEngine.exe");
        if (!File.Exists(exe))
        {
            exe = Path.Combine(installDir, "app", "reEngine.exe");
        }
        if (!File.Exists(exe))
        {
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--register-assoc");
        if (isolatedTest)
        {
            psi.Environment["APP_ISOLATED_TEST"] = "1";
        }
        if (!string.IsNullOrWhiteSpace(isolatedDataDir))
        {
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(Path.GetFullPath(isolatedDataDir));
        }

        return await RunGuardedProcessAsync(psi, ct).ConfigureAwait(false) == 0;
    }

    private static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunGuardedProcessAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var guard = new JobObjectGuard();
        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            return 1;
        }
        guard.TryAttachProcess(proc);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }
}
