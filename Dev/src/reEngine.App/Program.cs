// @module: reEngine.App.Program
// @purpose: Entry point for reEngine CLI router, fail-closed sandbox containment guard, and dark WinForms manager UI.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Runtime.InteropServices, System.Text.Json, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, reEngine.Core
// @exports: Program

using System.Runtime.InteropServices;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal static partial class Program
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
        string enginesDir = ResolveEnginesDir(cliEnginesDir, settings, sandbox);

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
            FileAssociationRecord decompRecord = assocStore.RegisterDecompAssociation(exeForAssoc);
            string modeTag = sandbox.TestMode || sandbox.IsContainedSandbox ? "VIRTUALIZED" : "HKCU";
            log("INFO", $"Registered .recomp & .decomp associations ({modeTag}): {record.CommandTemplate} | {decompRecord.CommandTemplate}");
            Console.WriteLine($"Registered .recomp & .decomp associations ({modeTag}): {record.CommandTemplate}");
            if (sandbox.Headless || (string.IsNullOrWhiteSpace(openTarget) && !checkUpdates && !sandbox.VisibleTest))
            {
                return 0;
            }
        }

        if (string.Equals(openTarget, "decomp", StringComparison.OrdinalIgnoreCase))
        {
            return await RunDecompCliAsync(args, enginesDir, sandbox, sandboxBoundary, log, ct)
                .ConfigureAwait(false);
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

    private static string ResolveEnginesDir(string? cliEnginesDir, ReEngineSettings settings, SandboxEnvironment sandbox)
    {
        if (!string.IsNullOrWhiteSpace(cliEnginesDir))
        {
            return Path.GetFullPath(cliEnginesDir.Trim());
        }
        if (!string.IsNullOrWhiteSpace(settings.EnginesDirectoryOverride))
        {
            return Path.GetFullPath(settings.EnginesDirectoryOverride.Trim());
        }

        string? suiteDir = ReEngineAppStatusHelper.TryReadSettingPath(settings, "suiteInstallDir");
        string? reEngineDir = ReEngineAppStatusHelper.TryReadSettingPath(settings, "reEngineInstallDir");
        string appRoot = ReEngineAppStatusHelper.ResolveAppInstallRoot();

        if (sandbox.TestMode || sandbox.IsContainedSandbox)
        {
            string boundary = ResolveSandboxBoundary(sandbox);
            string? suiteCandidate = !string.IsNullOrWhiteSpace(suiteDir)
                ? Path.Combine(suiteDir, "engines")
                : (!string.IsNullOrWhiteSpace(reEngineDir) ? ReEngineBootstrapHelper.ResolveEnginesRootDir(reEngineDir) : null);
            if (!string.IsNullOrWhiteSpace(suiteCandidate) && IsPathWithinBoundary(suiteCandidate, boundary))
            {
                return suiteCandidate;
            }

            string localCandidate = ReEngineBootstrapHelper.ResolveEnginesRootDir(appRoot);
            if (Directory.Exists(localCandidate) && IsPathWithinBoundary(localCandidate, boundary))
            {
                return localCandidate;
            }

            return Path.Combine(sandbox.DataDirectory, "engines");
        }

        if (!string.IsNullOrWhiteSpace(suiteDir))
        {
            return Path.Combine(suiteDir, "engines");
        }
        if (!string.IsNullOrWhiteSpace(reEngineDir))
        {
            return ReEngineBootstrapHelper.ResolveEnginesRootDir(reEngineDir);
        }

        string installedCandidate = ReEngineBootstrapHelper.ResolveEnginesRootDir(appRoot);
        if (Directory.Exists(installedCandidate) ||
            File.Exists(Path.Combine(appRoot, "reEngine.exe")) ||
            File.Exists(Path.Combine(appRoot, "version.txt")))
        {
            return installedCandidate;
        }

        string root = ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(testMode: false);
        return ReEngineBootstrapHelper.ResolveEnginesRootDir(root);
    }

    private static bool IsPathWithinBoundary(string candidatePath, string boundaryPath)
    {
        string fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullBoundary = Path.GetFullPath(boundaryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullBoundary, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullBoundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

    internal static string ResolveLauncherExecutablePath()
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
        Console.WriteLine("reEngine - Zero-Daemon .recomp & .decomp Suite Router");
        Console.WriteLine("Usage: reEngine.exe [--open <path> | <file.recomp>] [--wait] [--register-assoc] [--check-updates [--strict]] [--feed <urlOrPath>]");
        Console.WriteLine("       reEngine.exe decomp --module <file.decomp> [--stage D0..D5] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>]");
        Console.WriteLine("                    [--engines-dir <dir>] [--data-dir <dir>] [--log-dir <dir>] [--config <path>] [--test-mode] [--dry-run] [--headless] [--visible-test]");
        Console.WriteLine("                    [--enable-logs] [--frames <n>] [--fixture <path>] [--dump-screenshot <path>] [--dump-audio <path>] [--input-script <path>]");
        Console.WriteLine("                    [--version] [--help]");
    }
}
