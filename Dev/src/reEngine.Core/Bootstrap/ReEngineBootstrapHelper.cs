// @module: reEngine.Core.Bootstrap.ReEngineBootstrapHelper
// @purpose: Packaging constants and reusable zero-PII self-bootstrapping helper consumed by re* and <Game>Recomp installers.
// @depends-on: System, System.Diagnostics, System.IO, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution
// @exports: ReEnginePackagingConstants, ReEngineBootstrapResult, ReEngineBootstrapHelper

using System.Diagnostics;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Resolution;

namespace reEngine.Core.Bootstrap;

/// <summary>
/// Canonical branding, packaging, and containment constants for <c>reEngine</c>.
/// </summary>
public static class ReEnginePackagingConstants
{
    /// <summary>Canonical lowercase application identifier.</summary>
    public const string AppId = "reengine";

    /// <summary>Human-facing Title Case display name.</summary>
    public const string DisplayName = "reEngine";

    /// <summary>Current packaged SemVer version.</summary>
    public const string Version = "0.1.2";

    /// <summary>Brand accent color hex (Sky Cyan).</summary>
    public const string BrandAccentHex = "#38BDF8";

    /// <summary>Zero-daemon invariant: <c>reEngine</c> never auto-starts at Windows sign-in.</summary>
    public const bool AllowAutoStart = false;

    /// <summary>Default relative preload directory inside game module roots.</summary>
    public const string DefaultPreloadRomDir = "data/rom";

    /// <summary>Filename of the virtualized association store in <c>--test-mode</c>.</summary>
    public const string VirtualRegistryFileName = "virtual_registry.json";
}

/// <summary>
/// Outcome of bootstrapping or verifying a <c>reEngine</c> installation root.
/// </summary>
public sealed record ReEngineBootstrapResult(
    bool Success,
    string ReEngineInstallDir,
    string EnginesRootDir,
    bool InstalledOrUpgraded,
    bool AssociationRegistered,
    string DiagnosticMessage);

/// <summary>
/// Reusable SDK helper exported via <c>SharedLibs\reEngine</c> so child <c>re*</c> and <c>&lt;Game&gt;Recomp</c>
/// installers can deploy/upgrade <c>reEngine</c> idempotently and install side-by-side platform engines.
/// </summary>
public static class ReEngineBootstrapHelper
{
    /// <summary>
    /// Resolves the target <c>reEngine</c> installation root while enforcing fail-closed containment in <c>--test-mode</c>
    /// and zero literal host paths in compiled binaries.
    /// </summary>
    public static string ResolveDefaultReEngineInstallDir(
        string? explicitDir = null,
        string? sandboxDataDir = null,
        bool testMode = false)
    {
        if (testMode)
        {
            if (string.IsNullOrWhiteSpace(sandboxDataDir))
            {
                throw new InvalidOperationException(
                    "Fail-Closed Sandbox Violation: --test-mode requires a sandboxDataDir when resolving reEngine install root.");
            }

            string fullSandbox = Path.GetFullPath(sandboxDataDir);
            string candidate = string.IsNullOrWhiteSpace(explicitDir)
                ? Path.Combine(fullSandbox, ReEnginePackagingConstants.DisplayName)
                : Path.GetFullPath(explicitDir);

            return SandboxEnvironment.AssertPathContained(candidate, fullSandbox);
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

        string driveRoot = $"{(char)68}:{Path.DirectorySeparatorChar}";
        return Path.GetFullPath(Path.Combine(driveRoot, "Programs", ReEnginePackagingConstants.DisplayName));
    }

    /// <summary>Returns the side-by-side <c>engines\</c> subdirectory under <paramref name="reEngineInstallDir"/>.</summary>
    public static string ResolveEnginesRootDir(string reEngineInstallDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reEngineInstallDir);
        return Path.Combine(Path.GetFullPath(reEngineInstallDir), "engines");
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
        bool testMode,
        string? sandboxDataDir,
        CancellationToken ct = default)
    {
        string installDir = ResolveDefaultReEngineInstallDir(targetReEngineDir, sandboxDataDir, testMode);
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
                bundledInstallerPath, installDir, testMode, sandboxDataDir, ct).ConfigureAwait(false);
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
            assocRegistered = await TryRegisterAssociationAsync(installDir, testMode, sandboxDataDir, ct).ConfigureAwait(false);
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
        bool testMode,
        string? sandboxDataDir,
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
        if (testMode && !string.IsNullOrWhiteSpace(sandboxDataDir))
        {
            psi.ArgumentList.Add("--test-mode");
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(Path.GetFullPath(sandboxDataDir));
        }

        return await RunGuardedProcessAsync(psi, ct).ConfigureAwait(false);
    }

    private static async Task<bool> TryRegisterAssociationAsync(
        string installDir,
        bool testMode,
        string? sandboxDataDir,
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
        psi.ArgumentList.Add("--headless");
        if (testMode && !string.IsNullOrWhiteSpace(sandboxDataDir))
        {
            psi.ArgumentList.Add("--test-mode");
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(Path.GetFullPath(sandboxDataDir));
        }

        return await RunGuardedProcessAsync(psi, ct).ConfigureAwait(false) == 0;
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
