// @module: reEngine.App.RecompLaunchCoordinator
// @purpose: Orchestrates .recomp manifest loading, data\rom preload discovery, track-safe engine resolution, per-module isolation, and guarded launch.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Linq, System.Text.RegularExpressions, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core
// @exports: ChildLaunchPlan, RecompLaunchCoordinator

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Foundation.Core;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed record ChildLaunchPlan(
    string ModuleDataDirectory,
    string ModuleConfigPath,
    string ModuleLogDirectory,
    IReadOnlyList<string> Arguments);

internal sealed partial class RecompLaunchCoordinator
{
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly Action<string, string> _log;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleSlugRegex();

    public RecompLaunchCoordinator(UpdateCoordinator updateCoordinator, Action<string, string> log)
    {
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettings settings,
        AppEnvironment appEnv,
        string? containmentRoot,
        CancellationToken ct) =>
        LaunchAsync(packagePath, enginesDir, settings, appEnv, containmentRoot, new RecompLaunchOptions(), ct);

    public async Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettings settings,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions,
        CancellationToken ct)
    {
        string fullPkgPath = Path.GetFullPath(packagePath);
        if (!string.IsNullOrWhiteSpace(containmentRoot))
        {
            AppEnvironment.AssertPathContained(fullPkgPath, containmentRoot);
        }

        RecompPackageLoadResult loaded = RecompPackageReader.Load(fullPkgPath);
        ChildLaunchPlan plan = BuildChildLaunchPlan(loaded, appEnv, containmentRoot, launchOptions);

        _log("INFO", $"Loaded module '{loaded.Manifest.ModuleId}' v{loaded.Manifest.Version} (engine={loaded.Manifest.EngineId}, track={loaded.Manifest.CompatibleEngineTrack}).");
        _log("INFO", $"Preload ROM directory '{loaded.PreloadMedia.PreloadDirectory}' exists={loaded.PreloadMedia.DirectoryExists}, files={loaded.PreloadMedia.DiscoveredMediaFiles.Count}, primary='{loaded.PreloadMedia.PrimaryMediaPath ?? "none"}'.");

        var resolver = new EngineVersionResolver();
        EngineResolutionResult resolution = resolver.ResolveForManifest(enginesDir, loaded.Manifest);
        if (!resolution.Found || resolution.SelectedEngine is null)
        {
            _log("ERROR", resolution.DiagnosticMessage);
            Console.Error.WriteLine(resolution.DiagnosticMessage);
            return 1;
        }

        _log("INFO", resolution.DiagnosticMessage);
        Console.WriteLine(resolution.DiagnosticMessage);
        Console.WriteLine($"Preload media detected: {loaded.PreloadMedia.DiscoveredMediaFiles.Count} file(s) -> {loaded.PreloadMedia.PrimaryMediaPath ?? "none"}");

        await TryNonBlockingUpdateCheckAsync(settings, resolution.SelectedEngine, loaded.Manifest.CompatibleEngineTrack, containmentRoot, ct)
            .ConfigureAwait(false);

        if (appEnv.DryRun)
        {
            string argsSummary = string.Join(' ', plan.Arguments.Select(QuoteArg));
            string drySummary = $"[DRY-RUN] Would launch '{resolution.SelectedEngine.ExecutablePath}' for '{fullPkgPath}' (preload='{loaded.PreloadMedia.PrimaryMediaPath ?? "none"}', args='{argsSummary}').";
            _log("INFO", drySummary);
            Console.WriteLine(drySummary);
            return 0;
        }

        return await SpawnEngineAsync(resolution.SelectedEngine, plan, ct).ConfigureAwait(false);
    }

    internal static ChildLaunchPlan BuildChildLaunchPlan(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions = null)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(appEnv);

        RecompLaunchOptions opts = launchOptions ?? new RecompLaunchOptions();
        string moduleId = loaded.Manifest.ModuleId?.Trim() ?? string.Empty;
        if (!ModuleSlugRegex().IsMatch(moduleId))
        {
            throw new InvalidOperationException(
                $"Fail-Closed Containment Violation: invalid moduleId '{loaded.Manifest.ModuleId}'; expected lowercase slug matching ^[a-z0-9][a-z0-9-]*$.");
        }

        string? effectiveBoundary = !string.IsNullOrWhiteSpace(containmentRoot)
            ? containmentRoot
            : (IsIsolatedTestEnv() ? appEnv.DataDirectory : null);

        string moduleDataDir = Path.GetFullPath(Path.Combine(appEnv.DataDirectory, "modules", moduleId));
        if (effectiveBoundary is not null)
        {
            moduleDataDir = AppEnvironment.AssertPathContained(moduleDataDir, effectiveBoundary);
        }

        string moduleConfigPath = Path.Combine(moduleDataDir, "settings.json");
        if (!string.IsNullOrWhiteSpace(opts.ConfigPathOverride))
        {
            string candidateConfig = Path.GetFullPath(opts.ConfigPathOverride.Trim());
            if (IsPathInsideDirectory(candidateConfig, moduleDataDir))
            {
                moduleConfigPath = candidateConfig;
            }
        }
        if (effectiveBoundary is not null)
        {
            moduleConfigPath = AppEnvironment.AssertPathContained(moduleConfigPath, effectiveBoundary);
        }

        string moduleLogDir = !string.IsNullOrWhiteSpace(opts.LogDirectoryOverride)
            ? Path.GetFullPath(opts.LogDirectoryOverride.Trim())
            : Path.Combine(moduleDataDir, "logs");
        if (effectiveBoundary is not null)
        {
            moduleLogDir = AppEnvironment.AssertPathContained(moduleLogDir, effectiveBoundary);
        }

        string? fixturePath = ResolveOptionalContainedPath(opts.FixturePath, effectiveBoundary);
        string? screenshotPath = ResolveOptionalContainedPath(opts.DumpScreenshotPath, effectiveBoundary);
        string? audioPath = ResolveOptionalContainedPath(opts.DumpAudioPath, effectiveBoundary);

        var args = new List<string>
        {
            "--module", loaded.PackagePath
        };
        if (!string.IsNullOrWhiteSpace(loaded.PreloadMedia.PrimaryMediaPath))
        {
            args.Add("--preload-media");
            args.Add(loaded.PreloadMedia.PrimaryMediaPath);
        }
        if (opts.ShouldWaitForExit) args.Add("--wait");
        if (opts.TrayOnly || appEnv.TrayOnly) args.Add("--tray-only");
        if (appEnv.DryRun) args.Add("--dry-run");

        args.Add("--data-dir");
        args.Add(moduleDataDir);
        args.Add("--config");
        args.Add(moduleConfigPath);
        args.Add("--log-dir");
        args.Add(moduleLogDir);

        if (opts.ShouldEnableRuntimeLogs) args.Add("--enable-logs");
        if (opts.Frames is int frames)
        {
            args.Add("--frames");
            args.Add(frames.ToString(CultureInfo.InvariantCulture));
        }
        if (fixturePath is not null)
        {
            args.Add("--fixture");
            args.Add(fixturePath);
        }
        if (screenshotPath is not null)
        {
            args.Add("--dump-screenshot");
            args.Add(screenshotPath);
        }
        if (audioPath is not null)
        {
            args.Add("--dump-audio");
            args.Add(audioPath);
        }

        return new ChildLaunchPlan(moduleDataDir, moduleConfigPath, moduleLogDir, args);
    }

    private async Task TryNonBlockingUpdateCheckAsync(
        ReEngineSettings settings,
        InstalledEngineCandidate selectedEngine,
        string compatibleTrack,
        string? containmentRoot,
        CancellationToken ct)
    {
        if (!settings.ShouldCheckUpdates || string.IsNullOrWhiteSpace(settings.LastUpdateFeedUrl))
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            UpdateCheckOutcome outcome = await _updateCoordinator.CheckAndStageAsync(
                settings,
                selectedEngine.EngineId,
                selectedEngine.Version,
                compatibleTrack,
                settings.LastUpdateFeedUrl,
                allowedLocalFeedRoot: containmentRoot,
                ct: timeoutCts.Token).ConfigureAwait(false);
            _log("INFO", $"Launch update check: {outcome.StatusMessage}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _log("WARN", $"Non-blocking update check skipped: {ex.Message}");
        }
    }

    private async Task<int> SpawnEngineAsync(
        InstalledEngineCandidate engine,
        ChildLaunchPlan plan,
        CancellationToken ct)
    {
        Directory.CreateDirectory(plan.ModuleDataDirectory);
        Directory.CreateDirectory(plan.ModuleLogDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = engine.ExecutablePath,
            WorkingDirectory = engine.EngineDirectory,
            UseShellExecute = false
        };
        foreach (string arg in plan.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var guard = new JobObjectGuard();
        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            _log("ERROR", $"Failed to start engine executable '{engine.ExecutablePath}'.");
            return 1;
        }
        guard.TryAttachProcess(proc);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return proc.ExitCode;
    }

    private static string? ResolveOptionalContainedPath(string? rawPath, string? boundary)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }
        string fullPath = Path.GetFullPath(rawPath.Trim());
        return boundary is not null ? AppEnvironment.AssertPathContained(fullPath, boundary) : fullPath;
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootDir)
    {
        string fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteArg(string arg) =>
        arg.IndexOfAny([' ', '"', '\t']) >= 0 ? $"\"{arg}\"" : arg;
}
