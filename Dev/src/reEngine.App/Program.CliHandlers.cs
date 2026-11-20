// @module: reEngine.App.Program.CliHandlers
// @purpose: CLI handlers (--check-updates, --verify, --doctor, --host-demo visible SDL3 host, --tray-only, decomp) for Program.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core, reEngine.Host
// @exports: Program (partial: RunUpdateCheckCliAsync, RunTrayDiagnostics, RunDecompCliAsync, RunHostDemoSessionAsync)

using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Decomp;
using reEngine.Core.Execution;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Settings;

namespace reEngine.App;

internal static partial class Program
{
    private static async Task<int> RunUpdateCheckCliAsync(
        UpdateCoordinator coordinator,
        ReEngineSettings settings,
        string enginesDir,
        string? feedOverride,
        string? containmentRoot,
        bool strict,
        Action<string, string> log,
        CancellationToken ct)
    {
        var hb = new HeartbeatLogger("reEngine UpdateCheck", line => log("HEARTBEAT", line), Console.Out);
        hb.ReportProgress(0, 2, "Discovering installed engines", forceEmit: true);
        IReadOnlyList<InstalledEngineCandidate> engines = new EngineVersionResolver().DiscoverInstalledEngines(enginesDir);

        MultiEngineUpdateSweepResult sweep = await coordinator.CheckAllInstalledEnginesAsync(
            settings, engines, feedOverride, allowedLocalFeedRoot: containmentRoot, ct: ct).ConfigureAwait(false);

        hb.Complete(2, sweep.SummaryMessage);
        if (sweep.NetworkUnavailable)
        {
            log("WARN", sweep.SummaryMessage);
            if (strict) { Console.Error.WriteLine(sweep.SummaryMessage); return 4; }
            Console.WriteLine(sweep.SummaryMessage);
            return 0;
        }

        log("INFO", sweep.SummaryMessage);
        Console.WriteLine(sweep.SummaryMessage);
        return 0;
    }

    private static int RunTrayDiagnostics(
        string enginesDir,
        ReEngineSettings settings,
        IFileAssociationStore assocStore,
        AppEnvironment appEnv,
        bool isIsolatedTest,
        string? verifyModule,
        Action<string, string> log)
    {
        var hb = new HeartbeatLogger("reEngine Diagnostics", line => log("HEARTBEAT", line), Console.Out);
        hb.ReportProgress(1, 3, "Inspecting side-by-side engines", forceEmit: true);
        IReadOnlyList<InstalledEngineCandidate> discovered = new EngineVersionResolver().DiscoverInstalledEngines(enginesDir, verifyModule);
        hb.ReportProgress(2, 3, "Inspecting .recomp/.decomp association & AOT cache", forceEmit: true);
        FileAssociationRecord? assoc = assocStore.ReadRecompAssociation();
        DecompDispatchStatusSummary decomp = ReEngineAppStatusHelper.InspectDecompStatus(enginesDir, settings, discovered.Count);
        AotCacheStatusSummary aot = ReEngineAppStatusHelper.InspectAotCache(appEnv.DataDirectory);
        string summary = $"Diagnostics sweep OK | isolated={isIsolatedTest} | profile={decomp.InstallProfile} | toolchain={decomp.ToolchainAvailable} | aotAssemblies={aot.AssemblyCount} | engines={discovered.Count} | assoc={(assoc is not null ? "ACTIVE" : "NONE")} | autoCheck={settings.ShouldCheckUpdates}";
        hb.Complete(3, summary);
        log("INFO", summary);
        Console.WriteLine(summary);
        return 0;
    }

    private static int RunVerifyModuleCli(
        string target,
        string enginesDir,
        ReEngineSettings settings,
        IFileAssociationStore assocStore,
        AppEnvironment appEnv,
        bool isIsolatedTest,
        RecompLaunchOptions launchOpts,
        string? containmentRoot,
        Action<string, string> log)
    {
        if (!IsPackageVerifyTarget(target))
        {
            return RunTrayDiagnostics(enginesDir, settings, assocStore, appEnv, isIsolatedTest, target, log);
        }

        string? suiteRoot = SuiteMediaTreeWalker.DeriveSuiteRootDirectory(enginesDir, appEnv.DataDirectory, containmentRoot);
        PackageVerificationReport report = new PackageVerificationService().Verify(
            target,
            enginesDir,
            appEnv.DataDirectory,
            suiteRoot,
            requireMedia: launchOpts.RequireMedia,
            settings: settings,
            containmentRoot: containmentRoot);

        if (launchOpts.Json)
        {
            Console.WriteLine(report.ToJson(indented: true));
        }
        else
        {
            string summary = FormatVerificationSummary(report, target);
            if (report.IsPassed) Console.WriteLine(summary);
            else Console.Error.WriteLine(summary);
            foreach (string w in report.Warnings) Console.WriteLine($"WARN: {w}");
            foreach (string v in report.Violations) Console.Error.WriteLine($"VIOLATION: {v}");
        }

        log(report.IsPassed ? "INFO" : "ERROR", $"Verified '{target}': passed={report.IsPassed}, exitCode={report.ExitCode}, mediaReady={report.HasMediaReady}.");
        return report.ExitCode;
    }

    private static async Task<int> RunDecompCliAsync(
        string[] args,
        string enginesDir,
        AppEnvironment appEnv,
        string? containmentRoot,
        Action<string, string> log,
        CancellationToken ct)
    {
        string? modulePath = RecompLaunchOptions.GetOptionValue(args, "--module", requiredIfPresent: true)
            ?? RecompLaunchOptions.GetPositionalArg(args, 1);
        if (string.IsNullOrWhiteSpace(modulePath)) throw new ArgumentException("Missing --module <path> for 'decomp' subcommand.");

        string fullModulePath = Path.GetFullPath(modulePath);
        string slug = Path.GetFileNameWithoutExtension(fullModulePath);
        string? outputOverride = RecompLaunchOptions.GetOptionValue(args, "--output-dir", requiredIfPresent: true);
        string outputDir = string.IsNullOrWhiteSpace(outputOverride)
            ? Path.Combine(appEnv.DataDirectory, "decomp-output", string.IsNullOrWhiteSpace(slug) ? "module" : slug)
            : Path.GetFullPath(outputOverride);
        if (!string.IsNullOrWhiteSpace(containmentRoot)) AppEnvironment.AssertPathContained(outputDir, containmentRoot);

        string? stageRaw = RecompLaunchOptions.GetOptionValue(args, "--stage", requiredIfPresent: true);
        DecompStage stage = DecompStage.D3;
        if (!string.IsNullOrWhiteSpace(stageRaw) && !Enum.TryParse(stageRaw, ignoreCase: true, out stage))
            throw new ArgumentException($"Invalid --stage '{stageRaw}'; expected D0..D5.");

        string? mediaPath = RecompLaunchOptions.GetOptionValue(args, "--media-path", requiredIfPresent: true);
        Dictionary<string, string>? platformArgs = null;
        if (!string.IsNullOrWhiteSpace(mediaPath) || !string.IsNullOrWhiteSpace(containmentRoot))
        {
            platformArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                string fullMedia = Path.GetFullPath(mediaPath.Trim());
                if (!string.IsNullOrWhiteSpace(containmentRoot)) AppEnvironment.AssertPathContained(fullMedia, containmentRoot);
                platformArgs["discPath"] = fullMedia;
            }
            if (!string.IsNullOrWhiteSpace(containmentRoot)) platformArgs["containmentRoot"] = containmentRoot;
        }

        var options = new DecompRunOptions(
            PackagePath: fullModulePath,
            OutputDirectory: outputDir,
            DataDirectory: appEnv.DataDirectory,
            TargetStage: stage,
            EngineDirectoryOverride: RecompLaunchOptions.GetOptionValue(args, "--engine-dir", requiredIfPresent: true),
            ToolchainDirectory: RecompLaunchOptions.GetOptionValue(args, "--toolchain-dir", requiredIfPresent: true),
            DryRun: appEnv.DryRun,
            Headless: true,
            PlatformArguments: platformArgs);

        DecompDispatchResult result = await new DecompPipelineCoordinator().DispatchAsync(options, enginesDir, ct).ConfigureAwait(false);
        if (result.UnbuiltEngineError is not null)
        {
            string formatted = result.UnbuiltEngineError.FormatMessage();
            log("ERROR", formatted);
            Console.Error.WriteLine(formatted);
            return 3;
        }
        if (!result.Success) { log("ERROR", result.DiagnosticMessage); Console.Error.WriteLine(result.DiagnosticMessage); return 1; }

        log("INFO", result.DiagnosticMessage);
        Console.WriteLine(result.DiagnosticMessage);
        return 0;
    }

    private static async Task<int> RunHostDemoSessionAsync(
        RecompLaunchOptions opts,
        AppEnvironment appEnv,
        string? containmentRoot,
        Action<string, string> log,
        CancellationToken ct,
        ReEngineSettingsBundle? settingsBundle = null,
        IReEngineSettingsStore? settingsStore = null)
    {
        IReEngineSettingsStore effectiveStore = settingsStore ?? new HostSettingsStore(appEnv.DataDirectory, appEnv.ConfigFilePath);
        ReEngineSettingsBundle bundle = settingsBundle ?? effectiveStore.LoadOrMigrate();
        ReEngineSettings core = bundle.Core;
        ReEngineVideoSettings video = bundle.Video;
        ReEngineAudioSettings audio = bundle.Audio;

        if (opts.SafeModeReset)
        {
            _ = new BootCrashSentinel(appEnv.DataDirectory).Clear();
            if (core.IsSafeModeActive)
            {
                core.IsSafeModeActive = false;
                effectiveStore.SaveCore(core);
                log("INFO", "Safe Mode cleared before host-demo launch (--safe-mode-reset).");
            }
        }

        string effectiveBackendId = !string.IsNullOrWhiteSpace(opts.BackendId) ? opts.BackendId : bundle.Video.BackendId;
        HostRenderBackendKind preferredKind = DemoCartSessionHelper.ParseHostBackendKind(effectiveBackendId);
        int effectiveScale = opts.ResolutionScale ?? bundle.Video.ResolutionScale;
        string executionTier = RecompLaunchFlagRouter.ResolveEffectiveExecutionTier(opts, manifest: null, core);
        string verLabel = $"reEngine.Host v{ReEnginePackagingConstants.Version}";
        string title = opts.DemoCart ? "reEngine Clean-Room Demo / Test Cart" : "reEngine Visible Host Demo";
        var descriptor = new HostDrawerDescriptor(title, verLabel, executionTier, effectiveBackendId, effectiveScale, "B");
        var runner = new HostSessionRunner(1280, 720, descriptor);
        HostSessionOptions hostOpts = DemoCartSessionHelper.BuildSessionOptions(
            opts, appEnv, containmentRoot, bundle, effectiveStore, descriptor, preferredKind, verLabel);

        log("INFO", $"Starting HostSessionRunner (demoCart={hostOpts.IsDemoCartMode}, backend={preferredKind}, displayMode={hostOpts.DisplayMode}, filter={hostOpts.VideoFilterMode}, aspect={hostOpts.AspectRatioMode}, tier={executionTier}, safeMode={hostOpts.ExecutionResilience?.IsSafeModeActive}, automationPort={hostOpts.AutomationPort?.ToString() ?? "off"}).");
        HostSessionResult res = await runner.RunAsync(hostOpts, new DemoHostCallbacks(), msg => log("HEARTBEAT", msg), ct).ConfigureAwait(false);
        if (res.ExitCode != 0) Console.Error.WriteLine(res.Summary);
        else Console.WriteLine(res.Summary);
        return res.ExitCode;
    }
}
