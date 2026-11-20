// @module: reEngine.App.RecompLaunchCoordinator
// @purpose: Orchestrates .recomp manifest loading, data\rom preload discovery, track/alias engine resolution, and guarded child launch.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core, reEngine.Host.Contracts
// @exports: RecompLaunchCoordinator

using System.Diagnostics;
using Foundation.Core;
using reEngine.Core.Execution;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Settings;

namespace reEngine.App;

internal sealed record EngineCrashFallbackPrompt(
    string ModuleId,
    InstalledEngineCandidate FailedEngine,
    InstalledEngineCandidate FallbackEngine,
    int ExitCode,
    SessionRecoveryCandidate RecoveryCandidate,
    string Message);

internal sealed class RecompLaunchCoordinator
{
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly Action<string, string> _log;
    private readonly IReEngineSettingsStore? _settingsStore;

    internal Func<InstalledEngineCandidate, ChildLaunchPlan, ProcessStartInfo>? StartInfoOverride { get; init; }
    internal Func<EngineCrashFallbackPrompt, bool>? CrashFallbackPrompter { get; init; }
    internal string? BundledEnginesDirectoryOverride { get; init; }

    public RecompLaunchCoordinator(
        UpdateCoordinator updateCoordinator,
        Action<string, string> log,
        IReEngineSettingsStore? settingsStore = null)
    {
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _settingsStore = settingsStore;
    }

    public Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettings settings,
        AppEnvironment appEnv,
        string? containmentRoot,
        CancellationToken ct) =>
        LaunchAsync(packagePath, enginesDir, settings, appEnv, containmentRoot, new RecompLaunchOptions(), ct);

    public Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettings settings,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(appEnv);
        ReEngineSettingsBundle bundle = ResolveBundleForLegacyCaller(settings, appEnv);
        return LaunchAsync(packagePath, enginesDir, bundle, appEnv, containmentRoot, launchOptions, ct);
    }

    public Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettingsBundle settingsBundle,
        AppEnvironment appEnv,
        string? containmentRoot,
        CancellationToken ct) =>
        LaunchAsync(packagePath, enginesDir, settingsBundle, appEnv, containmentRoot, new RecompLaunchOptions(), ct);

    public async Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettingsBundle settingsBundle,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settingsBundle);
        ArgumentNullException.ThrowIfNull(appEnv);

        ReEngineSettings settings = settingsBundle.Core;
        string fullPkgPath = Path.GetFullPath(packagePath);
        if (!string.IsNullOrWhiteSpace(containmentRoot))
        {
            AppEnvironment.AssertPathContained(fullPkgPath, containmentRoot);
        }

        if (launchOptions?.SafeModeReset == true)
        {
            _ = new BootCrashSentinel(appEnv.DataDirectory).Clear();
            if (settings.IsSafeModeActive)
            {
                settings.IsSafeModeActive = false;
                (_settingsStore ?? new HostSettingsStore(appEnv.DataDirectory, appEnv.ConfigFilePath)).SaveCore(settings);
                _log("INFO", "Safe Mode cleared before module launch (--safe-mode-reset).");
            }
        }

        RecompPackageLoadResult loaded = RecompPackageReader.Load(fullPkgPath);
        _log("INFO", $"Loaded module '{loaded.Manifest.ModuleId}' v{loaded.Manifest.Version} (engine={loaded.Manifest.EngineId}, track={loaded.Manifest.CompatibleEngineTrack}).");
        _log("INFO", $"Preload ROM directory '{loaded.PreloadMedia.PreloadDirectory}' exists={loaded.PreloadMedia.DirectoryExists}, files={loaded.PreloadMedia.DiscoveredMediaFiles.Count}, primary='{loaded.PreloadMedia.PrimaryMediaPath ?? "none"}'.");

        string? bundledEnginesDir = ResolveBundledEnginesDir(loaded.ModuleRootDirectory, containmentRoot);
        var resolver = new EngineVersionResolver();
        EngineResolutionResult resolution = resolver.ResolveForManifest(enginesDir, loaded.Manifest, settings, bundledEnginesDir);
        if (!resolution.Found || resolution.SelectedEngine is null)
        {
            _log("ERROR", resolution.DiagnosticMessage);
            Console.Error.WriteLine(resolution.DiagnosticMessage);
            return 1;
        }

        (_, bool effectiveFullscreen) = RecompLaunchFlagRouter.ResolveDisplayMode(
            launchOptions ?? new RecompLaunchOptions(), settingsBundle.Video, settings);
        string? effectiveAudioDriver = !string.IsNullOrWhiteSpace(launchOptions?.AudioDriverOverride)
            ? launchOptions!.AudioDriverOverride
            : (settingsBundle.Audio.AudioDriver ?? settings.AudioDriver);

        RecompLaunchOptions effectiveOpts = (launchOptions ?? new RecompLaunchOptions()) with
        {
            Fullscreen = effectiveFullscreen,
            AudioDriverOverride = effectiveAudioDriver
        };
        ChildLaunchPlan plan = RecompLaunchPlanBuilder.Build(
            loaded, appEnv, containmentRoot, effectiveOpts, resolution.SelectedEngine, _log, settingsBundle);
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

        int shutdownTimeoutMs = effectiveOpts.ShutdownTimeoutMs ?? settings.ShutdownTimeoutMs ?? 3000;
        int exitCode = await SpawnEngineAsync(
            resolution.SelectedEngine,
            plan,
            waitForChildExit: effectiveOpts.ShouldWaitForExit,
            shutdownTimeoutMs,
            ct).ConfigureAwait(false);

        if (exitCode is 0 or 130 or 6) return exitCode;

        return await TryCrashFallbackRelaunchAsync(
            resolver, loaded, enginesDir, bundledEnginesDir, resolution.SelectedEngine, plan,
            effectiveOpts, settingsBundle, appEnv, containmentRoot, shutdownTimeoutMs, exitCode, ct).ConfigureAwait(false);
    }

    internal static ChildLaunchPlan BuildChildLaunchPlan(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions = null,
        ReEngineSettingsBundle? settingsBundle = null) =>
        RecompLaunchPlanBuilder.Build(loaded, appEnv, containmentRoot, launchOptions, selectedEngine: null, log: null, settingsBundle: settingsBundle);

    private ReEngineSettingsBundle ResolveBundleForLegacyCaller(ReEngineSettings settings, AppEnvironment appEnv)
    {
        IReEngineSettingsStore store = _settingsStore ?? new HostSettingsStore(appEnv.DataDirectory, appEnv.ConfigFilePath);
        string videoPath = Path.Combine(store.ConfigDirectory, "settings.video.json");
        string audioPath = Path.Combine(store.ConfigDirectory, "settings.audio.json");
        if (File.Exists(videoPath) || File.Exists(audioPath))
        {
            ReEngineSettingsBundle diskBundle = store.LoadOrMigrate();
            return diskBundle with { Core = settings };
        }
        return SettingsMigrationHelper.BuildBundleFromLegacyCore(settings, Path.Combine(store.ConfigDirectory, "settings.controls.json"));
    }

    private async Task TryNonBlockingUpdateCheckAsync(
        ReEngineSettings settings,
        InstalledEngineCandidate selectedEngine,
        string compatibleTrack,
        string? containmentRoot,
        CancellationToken ct)
    {
        if (!settings.ShouldCheckUpdates || string.IsNullOrWhiteSpace(settings.LastUpdateFeedUrl)) return;

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
        bool waitForChildExit,
        int shutdownTimeoutMs,
        CancellationToken ct)
    {
        Directory.CreateDirectory(plan.ModuleDataDirectory);
        Directory.CreateDirectory(plan.ModuleLogDirectory);

        ProcessStartInfo psi;
        if (StartInfoOverride is not null)
        {
            psi = StartInfoOverride(engine, plan);
        }
        else
        {
            psi = new ProcessStartInfo { FileName = engine.ExecutablePath, WorkingDirectory = engine.EngineDirectory, UseShellExecute = false };
            foreach (string arg in plan.Arguments) psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            _log("ERROR", $"Failed to start engine executable '{engine.ExecutablePath}'.");
            return 1;
        }

        if (waitForChildExit)
        {
            using var guard = new JobObjectGuard();
            guard.TryAttachProcess(proc);
            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                return proc.ExitCode;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await GracefulProcessTreeShutdown.RequestShutdownAsync(proc, shutdownTimeoutMs, _log).ConfigureAwait(false);
                throw;
            }
        }

        // Interactive / detached launch: never bind a kill-on-close JobObject because launcher stubs
        // (e.g. reStation.exe -> app\reStation.Player.exe) exit 0 immediately after spawning the game window.
        Task waitTask = proc.WaitForExitAsync(ct);
        Task completed = await Task.WhenAny(waitTask, Task.Delay(1500, ct)).ConfigureAwait(false);
        if (ReferenceEquals(completed, waitTask))
        {
            await waitTask.ConfigureAwait(false);
            return proc.ExitCode;
        }

        _log("INFO", $"Launched '{Path.GetFileName(engine.ExecutablePath)}' (PID {proc.Id}) interactively.");
        return 0;
    }

    private async Task<int> TryCrashFallbackRelaunchAsync(
        EngineVersionResolver resolver, RecompPackageLoadResult loaded, string enginesDir, string? bundledEnginesDir,
        InstalledEngineCandidate failedEngine, ChildLaunchPlan originalPlan, RecompLaunchOptions effectiveOpts,
        ReEngineSettingsBundle settingsBundle, AppEnvironment appEnv, string? containmentRoot, int shutdownTimeoutMs, int exitCode, CancellationToken ct)
    {
        EngineResolutionResult fallback = resolver.TryResolveBundledOrTargetFallback(enginesDir, loaded.Manifest, failedEngine.Version, bundledEnginesDir);
        if (!fallback.Found || fallback.SelectedEngine is null)
        {
            _log("WARN", $"Engine '{failedEngine.EngineId}' v{failedEngine.Version} exited abnormally (code {exitCode}); {fallback.DiagnosticMessage}");
            return exitCode;
        }

        SessionRecoveryCandidate recovery = SessionRecoveryLocator.FindClosestRecovery(originalPlan.ModuleDataDirectory, effectiveOpts.SaveDirOverride, containmentRoot);
        string recoveryNote = recovery.Found
            ? $" Closest recovery point: {recovery.Kind} '{Path.GetFileName(recovery.FilePath)}' ({recovery.ModifiedAtUtc:yyyy-MM-dd HH:mm:ss} UTC)."
            : string.Empty;
        string msg = $"{failedEngine.EngineId} v{failedEngine.Version} exited unexpectedly (code {exitCode}).{Environment.NewLine}{Environment.NewLine}" +
                     $"Relaunch '{loaded.Manifest.Title}' in this session on fallback {fallback.SelectedEngine.EngineId} v{fallback.SelectedEngine.Version}?{recoveryNote}";
        var prompt = new EngineCrashFallbackPrompt(loaded.Manifest.ModuleId, failedEngine, fallback.SelectedEngine, exitCode, recovery, msg);

        if (!ApproveCrashFallback(prompt, effectiveOpts, appEnv)) return exitCode;

        _log("INFO", $"Relaunching '{loaded.Manifest.ModuleId}' on fallback {fallback.SelectedEngine.EngineId} v{fallback.SelectedEngine.Version} ({fallback.DiagnosticMessage}).");
        if (recovery.Found) _log("INFO", $"Recovery artifact detected: {recovery.DiagnosticMessage}");
        ChildLaunchPlan fallbackPlan = RecompLaunchPlanBuilder.Build(loaded, appEnv, containmentRoot, effectiveOpts, fallback.SelectedEngine, _log, settingsBundle);
        return await SpawnEngineAsync(fallback.SelectedEngine, fallbackPlan, effectiveOpts.ShouldWaitForExit, shutdownTimeoutMs, ct).ConfigureAwait(false);
    }

    private bool ApproveCrashFallback(EngineCrashFallbackPrompt prompt, RecompLaunchOptions opts, AppEnvironment appEnv)
    {
        if (CrashFallbackPrompter is not null) return CrashFallbackPrompter(prompt);
        if (opts.Headless || opts.Json || appEnv.DryRun || IsIsolatedTestEnv() || !Environment.UserInteractive)
        {
            _log("INFO", $"Crash fallback prompt suppressed in non-interactive mode (exit={prompt.ExitCode}, fallback={prompt.FallbackEngine.EngineId} v{prompt.FallbackEngine.Version}).");
            return false;
        }
        return System.Windows.Forms.MessageBox.Show(
            prompt.Message, "reEngine — Engine Crash Recovery",
            System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Warning) == System.Windows.Forms.DialogResult.Yes;
    }

    private string? ResolveBundledEnginesDir(string moduleRootDir, string? containmentRoot)
    {
        string? candidate = !string.IsNullOrWhiteSpace(BundledEnginesDirectoryOverride)
            ? Path.GetFullPath(BundledEnginesDirectoryOverride.Trim())
            : Path.Combine(moduleRootDir, "engines");
        if (!Directory.Exists(candidate)) return null;
        return !string.IsNullOrWhiteSpace(containmentRoot) ? AppEnvironment.AssertPathContained(candidate, containmentRoot) : candidate;
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
