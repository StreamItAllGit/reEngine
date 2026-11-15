// @module: reEngine.App.Program.CliHandlers
// @purpose: CLI handler routines (--check-updates, --headless diagnostics, decomp --module) for Program.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core
// @exports: Program (partial: RunUpdateCheckCliAsync, RunHeadlessDiagnostics, RunDecompCliAsync)

using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal static partial class Program
{
    private static async Task<int> RunUpdateCheckCliAsync(
        UpdateCoordinator coordinator,
        ReEngineSettings settings,
        string enginesDir,
        string? feedOverride,
        string? sandboxBoundary,
        bool strict,
        Action<string, string> log,
        CancellationToken ct)
    {
        var hb = new HeartbeatLogger("reEngine UpdateCheck", line => log("HEARTBEAT", line), Console.Out);
        hb.ReportProgress(0, 2, "Discovering installed engines", forceEmit: true);
        var resolver = new EngineVersionResolver();
        IReadOnlyList<InstalledEngineCandidate> engines = resolver.DiscoverInstalledEngines(enginesDir);

        MultiEngineUpdateSweepResult sweep = await coordinator.CheckAllInstalledEnginesAsync(
            settings,
            engines,
            feedOverride,
            allowedLocalFeedRoot: sandboxBoundary,
            ct: ct).ConfigureAwait(false);

        hb.Complete(2, sweep.SummaryMessage);
        if (sweep.NetworkUnavailable)
        {
            log("WARN", sweep.SummaryMessage);
            if (strict)
            {
                Console.Error.WriteLine(sweep.SummaryMessage);
                return 4;
            }
            Console.WriteLine(sweep.SummaryMessage);
            return 0;
        }

        log("INFO", sweep.SummaryMessage);
        Console.WriteLine(sweep.SummaryMessage);
        return 0;
    }

    private static int RunHeadlessDiagnostics(
        string enginesDir,
        ReEngineSettings settings,
        IFileAssociationStore assocStore,
        SandboxEnvironment sandbox,
        Action<string, string> log)
    {
        var hb = new HeartbeatLogger("reEngine Headless", line => log("HEARTBEAT", line), Console.Out);
        hb.ReportProgress(1, 3, "Inspecting side-by-side engines", forceEmit: true);
        var resolver = new EngineVersionResolver();
        IReadOnlyList<InstalledEngineCandidate> discovered = resolver.DiscoverInstalledEngines(enginesDir);
        hb.ReportProgress(2, 3, "Inspecting .recomp/.decomp association & AOT cache", forceEmit: true);
        FileAssociationRecord? assoc = assocStore.ReadRecompAssociation();
        DecompDispatchStatusSummary decomp = ReEngineAppStatusHelper.InspectDecompStatus(enginesDir, settings, discovered.Count);
        AotCacheStatusSummary aot = ReEngineAppStatusHelper.InspectAotCache(sandbox.DataDirectory);
        string summary = $"Headless sweep OK | testMode={sandbox.TestMode} | profile={decomp.InstallProfile} | toolchain={decomp.ToolchainAvailable} | aotAssemblies={aot.AssemblyCount} | engines={discovered.Count} | assoc={(assoc is not null ? "ACTIVE" : "NONE")} | autoCheck={settings.ShouldCheckUpdates}";
        hb.Complete(3, summary);
        log("INFO", summary);
        Console.WriteLine(summary);
        return 0;
    }

    private static async Task<int> RunDecompCliAsync(
        string[] args,
        string enginesDir,
        SandboxEnvironment sandbox,
        string? sandboxBoundary,
        Action<string, string> log,
        CancellationToken ct)
    {
        string? modulePath = RecompLaunchOptions.GetOptionValue(args, "--module", requiredIfPresent: true)
            ?? RecompLaunchOptions.GetPositionalArg(args, 1);
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            throw new ArgumentException("Missing --module <path> for 'decomp' subcommand.");
        }

        string fullModulePath = Path.GetFullPath(modulePath);
        string slug = Path.GetFileNameWithoutExtension(fullModulePath);
        string? outputOverride = RecompLaunchOptions.GetOptionValue(args, "--output-dir", requiredIfPresent: true);
        string outputDir = string.IsNullOrWhiteSpace(outputOverride)
            ? Path.Combine(sandbox.DataDirectory, "decomp-output", string.IsNullOrWhiteSpace(slug) ? "module" : slug)
            : Path.GetFullPath(outputOverride);
        if ((sandbox.TestMode || sandbox.IsContainedSandbox) && !string.IsNullOrWhiteSpace(sandboxBoundary))
        {
            SandboxEnvironment.AssertPathContained(outputDir, sandboxBoundary);
        }

        string? stageRaw = RecompLaunchOptions.GetOptionValue(args, "--stage", requiredIfPresent: true);
        DecompStage stage = DecompStage.D3;
        if (!string.IsNullOrWhiteSpace(stageRaw) && !Enum.TryParse(stageRaw, ignoreCase: true, out stage))
        {
            throw new ArgumentException($"Invalid --stage '{stageRaw}'; expected D0..D5.");
        }

        var options = new DecompRunOptions(
            PackagePath: fullModulePath,
            OutputDirectory: outputDir,
            DataDirectory: sandbox.DataDirectory,
            TargetStage: stage,
            EngineDirectoryOverride: RecompLaunchOptions.GetOptionValue(args, "--engine-dir", requiredIfPresent: true),
            ToolchainDirectory: RecompLaunchOptions.GetOptionValue(args, "--toolchain-dir", requiredIfPresent: true),
            DryRun: sandbox.DryRun,
            Headless: sandbox.Headless || CliArgumentHelper.HasFlag(args, "--headless"));

        var coordinator = new DecompPipelineCoordinator();
        DecompDispatchResult result = await coordinator.DispatchAsync(options, enginesDir, ct).ConfigureAwait(false);
        if (result.UnbuiltEngineError is not null)
        {
            string formatted = result.UnbuiltEngineError.FormatMessage();
            log("ERROR", formatted);
            Console.Error.WriteLine(formatted);
            return 3;
        }

        if (!result.Success)
        {
            log("ERROR", result.DiagnosticMessage);
            Console.Error.WriteLine(result.DiagnosticMessage);
            return 1;
        }

        log("INFO", result.DiagnosticMessage);
        Console.WriteLine(result.DiagnosticMessage);
        return 0;
    }
}
