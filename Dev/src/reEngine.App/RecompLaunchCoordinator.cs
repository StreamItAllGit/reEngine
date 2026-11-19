// @module: reEngine.App.RecompLaunchCoordinator
// @purpose: Orchestrates .recomp manifest loading, data\rom preload discovery, track/alias engine resolution, and guarded child launch.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.Linq, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core
// @exports: RecompLaunchCoordinator

using System.Diagnostics;
using Foundation.Core;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed class RecompLaunchCoordinator
{
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly Action<string, string> _log;

    internal Func<InstalledEngineCandidate, ChildLaunchPlan, ProcessStartInfo>? StartInfoOverride { get; init; }

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
        _log("INFO", $"Loaded module '{loaded.Manifest.ModuleId}' v{loaded.Manifest.Version} (engine={loaded.Manifest.EngineId}, track={loaded.Manifest.CompatibleEngineTrack}).");
        _log("INFO", $"Preload ROM directory '{loaded.PreloadMedia.PreloadDirectory}' exists={loaded.PreloadMedia.DirectoryExists}, files={loaded.PreloadMedia.DiscoveredMediaFiles.Count}, primary='{loaded.PreloadMedia.PrimaryMediaPath ?? "none"}'.");

        var resolver = new EngineVersionResolver();
        EngineResolutionResult resolution = resolver.ResolveForManifest(enginesDir, loaded.Manifest, settings);
        if (!resolution.Found || resolution.SelectedEngine is null)
        {
            _log("ERROR", resolution.DiagnosticMessage);
            Console.Error.WriteLine(resolution.DiagnosticMessage);
            return 1;
        }

        RecompLaunchOptions effectiveOpts = (launchOptions ?? new RecompLaunchOptions()) with
        {
            Fullscreen = (launchOptions?.Fullscreen == true) || settings.IsFullscreen,
            AudioDriverOverride = !string.IsNullOrWhiteSpace(launchOptions?.AudioDriverOverride) ? launchOptions!.AudioDriverOverride : settings.AudioDriver
        };
        ChildLaunchPlan plan = RecompLaunchPlanBuilder.Build(loaded, appEnv, containmentRoot, effectiveOpts, resolution.SelectedEngine, _log);
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

        return await SpawnEngineAsync(
            resolution.SelectedEngine,
            plan,
            waitForChildExit: effectiveOpts.ShouldWaitForExit,
            shutdownTimeoutMs: effectiveOpts.ShutdownTimeoutMs ?? settings.ShutdownTimeoutMs ?? 3000,
            ct).ConfigureAwait(false);
    }

    internal static ChildLaunchPlan BuildChildLaunchPlan(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions = null) =>
        RecompLaunchPlanBuilder.Build(loaded, appEnv, containmentRoot, launchOptions);

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

    private static string QuoteArg(string arg) =>
        arg.IndexOfAny([' ', '"', '\t']) >= 0 ? $"\"{arg}\"" : arg;
}
