// @module: reEngine.App.Program.CliHandlers
// @purpose: CLI handlers (--check-updates, --verify, --doctor, --host-demo visible SDL3 host, --tray-only, decomp) for Program.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core, reEngine.Host
// @exports: Program (partial: RunUpdateCheckCliAsync, RunTrayDiagnostics, RunDecompCliAsync, RunHostDemoSessionAsync)

using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

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

        var options = new DecompRunOptions(
            PackagePath: fullModulePath,
            OutputDirectory: outputDir,
            DataDirectory: appEnv.DataDirectory,
            TargetStage: stage,
            EngineDirectoryOverride: RecompLaunchOptions.GetOptionValue(args, "--engine-dir", requiredIfPresent: true),
            ToolchainDirectory: RecompLaunchOptions.GetOptionValue(args, "--toolchain-dir", requiredIfPresent: true),
            DryRun: appEnv.DryRun,
            Headless: true);

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
        CancellationToken ct)
    {
        var descriptor = HostDrawerDescriptor.CreateDefault("reEngine Visible Host Demo", "reEngine.Host v0.3.0");
        var runner = new HostSessionRunner(1280, 720, descriptor);
        var hostOpts = new HostSessionOptions
        {
            WindowTitle = "reEngine.Host v0.3.0 — Live Clean-Room Session",
            WindowWidth = 1280,
            WindowHeight = 720,
            ContentWidth = 320,
            ContentHeight = 240,
            TargetFps = 60.0,
            HeadlessFrameCount = opts.Frames,
            DumpFramePath = opts.DumpScreenshotPath,
            DumpAudioPath = opts.DumpAudioPath,
            AutomationPort = opts.AutomationPort,
            AutomationToken = opts.AutomationToken,
            AllowedSnapshotRoot = containmentRoot ?? appEnv.DataDirectory,
            RequireVisibleWindow = true,
            ShowMediaGateInitially = opts.PreloadMediaFlag,
            DrawerDescriptor = descriptor
        };

        log("INFO", $"Starting HostSessionRunner (visible={hostOpts.RequireVisibleWindow}, automationPort={hostOpts.AutomationPort?.ToString() ?? "off"}).");
        HostSessionResult res = await runner.RunAsync(hostOpts, new DemoHostCallbacks(), msg => log("HEARTBEAT", msg), ct).ConfigureAwait(false);
        Console.WriteLine(res.Summary);
        return res.ExitCode;
    }

    private sealed class DemoHostCallbacks : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
        {
            int shift = (int)(frameNumber % 256);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = ((y * width) + x) * 4;
                    bool checker = ((x / 16) + (y / 16)) % 2 == 0;
                    rgbaFramebuffer[idx] = checker ? (byte)((24 + shift) & 0x7F) : (byte)18;
                    rgbaFramebuffer[idx + 1] = checker ? (byte)110 : (byte)42;
                    rgbaFramebuffer[idx + 2] = checker ? (byte)168 : (byte)84;
                    rgbaFramebuffer[idx + 3] = 255;
                }
            }
            return ValueTask.CompletedTask;
        }

        public int ReadAudioSamples(Span<short> interleavedStereoPcm)
        {
            interleavedStereoPcm.Clear();
            return Math.Min(interleavedStereoPcm.Length, 1024);
        }
    }
}
