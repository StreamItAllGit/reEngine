// @module: reEngine.App.Program
// @purpose: Entry point for reEngine CLI router, fail-closed sandbox containment guard, and dark WinForms manager UI.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Runtime.InteropServices, System.Text.Json, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, reEngine.Core
// @exports: Program

using System.Runtime.InteropServices;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    private static int Main(string[] args)
    {
        string[] rawArgs = args ?? Array.Empty<string>();
        if (rawArgs.Length > 0 && OperatingSystem.IsWindows() &&
            !Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            _ = AttachConsole(AttachParentProcess);
        }

        if (CliArgumentHelper.HasFlag(rawArgs, "--version") || CliArgumentHelper.HasFlag(rawArgs, "-v"))
        {
            Console.WriteLine(ReEnginePackagingConstants.Version);
            return 0;
        }

        if (CliArgumentHelper.HasFlag(rawArgs, "--help") ||
            CliArgumentHelper.HasFlag(rawArgs, "-h") ||
            CliArgumentHelper.HasFlag(rawArgs, "/?") ||
            CliArgumentHelper.HasFlag(rawArgs, "help"))
        {
            PrintUsage();
            return 0;
        }

        var sandbox = new SandboxEnvironment("reengine", rawArgs, defaultConfigFileName: "settings.json");
        var logLines = new Queue<string>(100);
        var logSink = new CappedSharedLogSink(Path.Combine(sandbox.LogDirectory, "reengine.log"));

        string[] SnapshotRecentLogs()
        {
            lock (logLines) return logLines.ToArray();
        }

        void Log(string level, string msg)
        {
            string line = ReEngineTelemetryFormatter.FormatLogLine(level, msg);
            lock (logLines)
            {
                if (logLines.Count >= 100) logLines.Dequeue();
                logLines.Enqueue(line);
            }
            logSink.AppendLine(line);
        }

        CrashBundleService.RegisterGlobalHandlers(
            sandbox.LogDirectory,
            ReEnginePackagingConstants.AppId,
            ReEnginePackagingConstants.Version,
            SnapshotRecentLogs);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return RunAsync(rawArgs, sandbox, Log, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Log("WARN", "Operation cancelled by user/signal.");
            Console.Error.WriteLine("reEngine cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Log("ERROR", ex.Message);
            CrashBundleService.TryWriteCrashBundle(
                sandbox.LogDirectory,
                ReEnginePackagingConstants.AppId,
                ReEnginePackagingConstants.Version,
                ex,
                SnapshotRecentLogs());
            Console.Error.WriteLine($"reEngine error: {ex.Message}");
            return MapExceptionToExitCode(ex);
        }
    }

    internal static int MapExceptionToExitCode(Exception ex) => ex switch
    {
        OperationCanceledException => 130,
        ArgumentException or InvalidDataException or JsonException => 2,
        InvalidOperationException ioe when IsContainmentViolation(ioe.Message) => 2,
        _ => 1
    };

    private static bool IsContainmentViolation(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        (message.Contains("Fail-Closed Sandbox Violation", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("Fail-Closed Containment Violation", StringComparison.OrdinalIgnoreCase));

    private static async Task<int> RunAsync(
        string[] args,
        SandboxEnvironment sandbox,
        Action<string, string> log,
        CancellationToken ct)
    {
        var updateCoordinator = new UpdateCoordinator(sandbox.DataDirectory, sandbox.ConfigFilePath);
        ReEngineSettings settings = updateCoordinator.LoadSettings().Value;

        string? cliEnginesDir = RecompLaunchOptions.GetOptionValue(args, "--engines-dir", requiredIfPresent: true);
        string enginesDir = ResolveEnginesDir(cliEnginesDir, settings.EnginesDirectoryOverride, sandbox);

        string? sandboxBoundary = null;
        if (sandbox.TestMode || sandbox.IsContainedSandbox)
        {
            sandboxBoundary = ResolveSandboxBoundary(sandbox);
            EnforceSandboxContainment(sandbox, enginesDir, sandboxBoundary);
        }

        IFileAssociationStore assocStore = sandbox.TestMode || sandbox.IsContainedSandbox
            ? new VirtualRegistryAssociationStore(sandbox.DataDirectory, sandboxBoundary)
            : new WindowsRegistryAssociationStore(testMode: false);

        string? openTarget = RecompLaunchOptions.GetOptionValue(args, "--open", requiredIfPresent: true)
            ?? RecompLaunchOptions.GetPositionalArg(args, 0);
        bool registerAssoc = CliArgumentHelper.HasFlag(args, "--register-assoc");
        bool checkUpdates = CliArgumentHelper.HasFlag(args, "--check-updates");
        bool strictUpdates = CliArgumentHelper.HasFlag(args, "--strict");
        string? feedOverride = RecompLaunchOptions.GetOptionValue(args, "--feed", requiredIfPresent: true);

        if (registerAssoc)
        {
            string exeForAssoc = ResolveLauncherExecutablePath();
            FileAssociationRecord record = assocStore.RegisterRecompAssociation(exeForAssoc);
            string modeTag = sandbox.TestMode || sandbox.IsContainedSandbox ? "VIRTUALIZED" : "HKCU";
            log("INFO", $"Registered .recomp association ({modeTag}): {record.CommandTemplate}");
            Console.WriteLine($"Registered .recomp association ({modeTag}): {record.CommandTemplate}");
            if (sandbox.Headless || (string.IsNullOrWhiteSpace(openTarget) && !checkUpdates && !sandbox.VisibleTest))
            {
                return 0;
            }
        }

        if (checkUpdates)
        {
            return await RunUpdateCheckCliAsync(
                updateCoordinator, settings, enginesDir, feedOverride, sandboxBoundary, strictUpdates, log, ct)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(openTarget))
        {
            RecompLaunchOptions launchOptions = RecompLaunchOptions.FromArgs(args, sandbox);
            var launcher = new RecompLaunchCoordinator(updateCoordinator, log);
            return await launcher.LaunchAsync(openTarget, enginesDir, settings, sandbox, sandboxBoundary, launchOptions, ct)
                .ConfigureAwait(false);
        }

        if (sandbox.Headless)
        {
            return RunHeadlessDiagnostics(enginesDir, settings, assocStore, sandbox, log);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new ReEngineMainForm(
            sandbox,
            enginesDir,
            updateCoordinator,
            assocStore,
            log,
            visibleTestShowcase: sandbox.VisibleTest,
            sandboxBoundary: sandboxBoundary);
        Application.Run(form);
        return form.ExitCode;
    }

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
        hb.ReportProgress(2, 3, "Inspecting .recomp association", forceEmit: true);
        FileAssociationRecord? assoc = assocStore.ReadRecompAssociation();
        string summary = $"Headless sweep OK | testMode={sandbox.TestMode} | engines={discovered.Count} | assoc={(assoc is not null ? "ACTIVE" : "NONE")} | autoCheck={settings.ShouldCheckUpdates}";
        hb.Complete(3, summary);
        log("INFO", summary);
        Console.WriteLine(summary);
        return 0;
    }

    private static string ResolveEnginesDir(string? cliEnginesDir, string? settingsOverride, SandboxEnvironment sandbox)
    {
        if (!string.IsNullOrWhiteSpace(cliEnginesDir))
        {
            return Path.GetFullPath(cliEnginesDir.Trim());
        }
        if (!string.IsNullOrWhiteSpace(settingsOverride))
        {
            return Path.GetFullPath(settingsOverride.Trim());
        }
        if (sandbox.TestMode || sandbox.IsContainedSandbox)
        {
            return Path.Combine(sandbox.DataDirectory, "engines");
        }
        string root = ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(testMode: false);
        return ReEngineBootstrapHelper.ResolveEnginesRootDir(root);
    }

    private static string ResolveSandboxBoundary(SandboxEnvironment sandbox)
    {
        string fullData = Path.GetFullPath(sandbox.DataDirectory);
        string? cursor = fullData;
        while (!string.IsNullOrWhiteSpace(cursor))
        {
            if (string.Equals(Path.GetFileName(cursor), "Sandbox", StringComparison.OrdinalIgnoreCase))
            {
                return cursor;
            }
            cursor = Path.GetDirectoryName(cursor);
        }

        if (sandbox.TestMode)
        {
            string? parent = Path.GetDirectoryName(fullData);
            if (!string.IsNullOrWhiteSpace(parent) &&
                string.Equals(Path.GetFileName(fullData), "data", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetPathRoot(parent)?.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                return parent;
            }
        }
        return fullData;
    }

    private static void EnforceSandboxContainment(SandboxEnvironment sandbox, string enginesDir, string sandboxBoundary)
    {
        SandboxEnvironment.AssertPathContained(sandbox.DataDirectory, sandboxBoundary);
        SandboxEnvironment.AssertPathContained(sandbox.ConfigFilePath, sandboxBoundary);
        SandboxEnvironment.AssertPathContained(sandbox.LogDirectory, sandboxBoundary);
        SandboxEnvironment.AssertPathContained(enginesDir, sandboxBoundary);
    }

    private static string ResolveLauncherExecutablePath()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parentDir = Path.GetDirectoryName(baseDir);
        if (string.Equals(Path.GetFileName(baseDir), "app", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parentDir))
        {
            string rootExe = Path.Combine(parentDir, "reEngine.exe");
            if (File.Exists(rootExe)) return rootExe;
        }
        return Environment.ProcessPath ?? Path.Combine(baseDir, "reEngine.exe");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("reEngine - Zero-Daemon .recomp Suite Router");
        Console.WriteLine("Usage: reEngine.exe [--open <path> | <file.recomp>] [--wait] [--register-assoc] [--check-updates [--strict]] [--feed <urlOrPath>]");
        Console.WriteLine("                    [--engines-dir <dir>] [--data-dir <dir>] [--log-dir <dir>] [--config <path>] [--test-mode] [--dry-run] [--headless] [--visible-test]");
        Console.WriteLine("                    [--enable-logs] [--frames <n>] [--fixture <path>] [--dump-screenshot <path>] [--dump-audio <path>] [--input-script <path>]");
        Console.WriteLine("                    [--version] [--help]");
    }
}
