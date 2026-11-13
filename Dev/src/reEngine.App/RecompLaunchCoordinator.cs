// @module: reEngine.App.RecompLaunchCoordinator
// @purpose: Orchestrates .recomp manifest loading, data\rom preload discovery, track-safe engine resolution, non-blocking update check, and guarded launch.
// @depends-on: System, System.Diagnostics, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core
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

    public RecompLaunchCoordinator(UpdateCoordinator updateCoordinator, Action<string, string> log)
    {
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<int> LaunchAsync(
        string packagePath,
        string enginesDir,
        ReEngineSettings settings,
        SandboxEnvironment sandbox,
        string? sandboxBoundary,
        CancellationToken ct)
    {
        string fullPkgPath = Path.GetFullPath(packagePath);
        if ((sandbox.TestMode || sandbox.IsContainedSandbox) && !string.IsNullOrWhiteSpace(sandboxBoundary))
        {
            SandboxEnvironment.AssertPathContained(fullPkgPath, sandboxBoundary);
        }

        RecompPackageLoadResult loaded = RecompPackageReader.Load(fullPkgPath);
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

        await TryNonBlockingUpdateCheckAsync(settings, resolution.SelectedEngine, loaded.Manifest.CompatibleEngineTrack, sandboxBoundary, ct)
            .ConfigureAwait(false);

        if (sandbox.DryRun)
        {
            string drySummary = $"[DRY-RUN] Would launch '{resolution.SelectedEngine.ExecutablePath}' for '{fullPkgPath}' (preload='{loaded.PreloadMedia.PrimaryMediaPath ?? "none"}').";
            _log("INFO", drySummary);
            Console.WriteLine(drySummary);
            return 0;
        }

        return await SpawnEngineAsync(resolution.SelectedEngine, loaded, sandbox, ct).ConfigureAwait(false);
    }

    private async Task TryNonBlockingUpdateCheckAsync(
        ReEngineSettings settings,
        InstalledEngineCandidate selectedEngine,
        string compatibleTrack,
        string? sandboxBoundary,
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
                allowedLocalFeedRoot: sandboxBoundary,
                ct: timeoutCts.Token).ConfigureAwait(false);
            _log("INFO", $"Launch update check: {outcome.StatusMessage}");
        }
        catch (Exception ex)
        {
            _log("WARN", $"Non-blocking update check skipped: {ex.Message}");
        }
    }

    private async Task<int> SpawnEngineAsync(
        InstalledEngineCandidate engine,
        RecompPackageLoadResult loaded,
        SandboxEnvironment sandbox,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = engine.ExecutablePath,
            WorkingDirectory = engine.EngineDirectory,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("--module");
        psi.ArgumentList.Add(loaded.PackagePath);
        if (!string.IsNullOrWhiteSpace(loaded.PreloadMedia.PrimaryMediaPath))
        {
            psi.ArgumentList.Add("--preload-media");
            psi.ArgumentList.Add(loaded.PreloadMedia.PrimaryMediaPath);
        }
        if (sandbox.TestMode)
        {
            psi.ArgumentList.Add("--test-mode");
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(sandbox.DataDirectory);
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
}
