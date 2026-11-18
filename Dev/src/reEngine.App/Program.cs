// @module: reEngine.App.Program
// @purpose: Entry point for reEngine CLI router, fail-closed path containment, dual-root settings migration, and visible UI/automation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Runtime.InteropServices, System.Text.Json, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, reEngine.Core
// @exports: Program

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
#if REENGINE_ENABLE_AUTOMATION
using Foundation.Automation;
#endif

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
        if (rawArgs.Length > 0 && OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
        {
            _ = AttachConsole(AttachParentProcess);
        }

#if !REENGINE_ENABLE_AUTOMATION
        if (rawArgs.Any(a => a.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) ||
                             a.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Automation loopback is disabled in Release builds (ExitCode = 2).");
            return 2;
        }
#endif

        if (CliArgumentHelper.HasFlag(rawArgs, "--version") || CliArgumentHelper.HasFlag(rawArgs, "-v"))
        {
            Console.WriteLine(ReEnginePackagingConstants.Version);
            return 0;
        }
        if (CliArgumentHelper.HasFlag(rawArgs, "--help") || CliArgumentHelper.HasFlag(rawArgs, "-h") ||
            CliArgumentHelper.HasFlag(rawArgs, "/?") || CliArgumentHelper.HasFlag(rawArgs, "help"))
        {
            PrintUsage();
            return 0;
        }

        string[] effectiveArgs = ProgramBootstrapHelper.NormalizeBootstrapArgs(rawArgs);
        var appEnv = new AppEnvironment("reengine", effectiveArgs, defaultConfigFileName: "settings.json");
        var logLines = new Queue<string>(100);
        var logSink = new CappedSharedLogSink(Path.Combine(appEnv.LogDirectory, "reengine.log"));

        string[] SnapshotRecentLogs() { lock (logLines) return logLines.ToArray(); }
        void Log(string level, string msg)
        {
            string line = ReEngineTelemetryFormatter.FormatLogLine(level, msg);
            lock (logLines) { if (logLines.Count >= 100) logLines.Dequeue(); logLines.Enqueue(line); }
            logSink.AppendLine(line);
        }

        CrashBundleService.RegisterGlobalHandlers(appEnv.LogDirectory, ReEnginePackagingConstants.AppId, ReEnginePackagingConstants.Version, SnapshotRecentLogs);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return RunAsync(effectiveArgs, appEnv, Log, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Log("WARN", "Operation cancelled by user/signal.");
            return 130;
        }
        catch (Exception ex)
        {
            Log("ERROR", ex.Message);
            CrashBundleService.TryWriteCrashBundle(appEnv.LogDirectory, ReEnginePackagingConstants.AppId, ReEnginePackagingConstants.Version, ex, SnapshotRecentLogs());
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
        (message.Contains("Fail-Closed Isolation Violation", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("Fail-Closed Containment Violation", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("Path Traversal Guard", StringComparison.OrdinalIgnoreCase));

    internal static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunAsync(string[] args, AppEnvironment appEnv, Action<string, string> log, CancellationToken ct)
    {
        bool isIsolatedTest = IsIsolatedTestEnv();
        SettingsMigrationResult mig = SettingsMigrationHelper.MigrateIfNeeded(
            ReEngineAppStatusHelper.ResolveAppInstallRoot(), appEnv.DataDirectory, appEnv.ConfigFilePath, allowAppDataProbe: !isIsolatedTest);
        if (mig.Migrated) log("INFO", mig.Summary);

        var updateCoordinator = new UpdateCoordinator(appEnv.DataDirectory, appEnv.ConfigFilePath);
        ReEngineSettings settings = updateCoordinator.LoadSettings().Value;
        string? containmentRoot = isIsolatedTest ? ResolveContainmentRoot(appEnv) : null;
        string enginesDir = ResolveEnginesDir(RecompLaunchOptions.GetOptionValue(args, "--engines-dir", requiredIfPresent: true), settings, appEnv, containmentRoot);
        if (containmentRoot is not null) EnforceContainment(appEnv, enginesDir, containmentRoot);

        IFileAssociationStore assocStore = isIsolatedTest || !OperatingSystem.IsWindows()
            ? new VirtualRegistryAssociationStore(appEnv.DataDirectory, containmentRoot ?? appEnv.DataDirectory)
            : new WindowsRegistryAssociationStore(suppressHostMutations: false);

        RecompLaunchOptions launchOpts = RecompLaunchOptions.FromArgs(args, appEnv);
        string? openTarget = RecompLaunchOptions.GetOptionValue(args, "--open", requiredIfPresent: true)
            ?? RecompLaunchOptions.GetPositionalArg(args, 0);

        if (CliArgumentHelper.HasFlag(args, "--register-assoc"))
        {
            string exeForAssoc = ResolveLauncherExecutablePath();
            FileAssociationRecord rec = assocStore.RegisterRecompAssociation(exeForAssoc);
            _ = assocStore.RegisterDecompAssociation(exeForAssoc);
            log("INFO", $"Registered .recomp & .decomp associations: {rec.CommandTemplate}");
            Console.WriteLine($"Registered .recomp & .decomp associations: {rec.CommandTemplate}");
            if (launchOpts.TrayOnly || (string.IsNullOrWhiteSpace(openTarget) && !CliArgumentHelper.HasFlag(args, "--check-updates") && !launchOpts.Verify && !launchOpts.Doctor))
                return 0;
        }

        if (string.Equals(openTarget, "decomp", StringComparison.OrdinalIgnoreCase))
            return await RunDecompCliAsync(args, enginesDir, appEnv, containmentRoot, log, ct).ConfigureAwait(false);
        if (CliArgumentHelper.HasFlag(args, "--check-updates"))
            return await RunUpdateCheckCliAsync(updateCoordinator, settings, enginesDir, RecompLaunchOptions.GetOptionValue(args, "--feed", requiredIfPresent: true), containmentRoot, CliArgumentHelper.HasFlag(args, "--strict"), log, ct).ConfigureAwait(false);
        if (launchOpts.HostDemo)
            return await RunHostDemoSessionAsync(launchOpts, appEnv, containmentRoot, log, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(openTarget))
            return await new RecompLaunchCoordinator(updateCoordinator, log).LaunchAsync(openTarget, enginesDir, settings, appEnv, containmentRoot, launchOpts, ct).ConfigureAwait(false);

        bool explicitWindow = CliArgumentHelper.HasFlag(args, "--window") ||
                              string.Equals(RecompLaunchOptions.GetOptionValue(args, "--ui"), "desktop", StringComparison.OrdinalIgnoreCase) ||
                              launchOpts.AutomationPort.HasValue;
        if (launchOpts.Verify || launchOpts.Doctor || launchOpts.VerifyModule is not null || launchOpts.TrayOnly || (isIsolatedTest && !explicitWindow))
            return RunTrayDiagnostics(enginesDir, settings, assocStore, appEnv, isIsolatedTest, launchOpts.VerifyModule, log);

        return RunVisibleMainForm(args, appEnv, enginesDir, updateCoordinator, assocStore, containmentRoot, log);
    }

    private static int RunVisibleMainForm(string[] args, AppEnvironment appEnv, string enginesDir, UpdateCoordinator updateCoordinator, IFileAssociationStore assocStore, string? containmentRoot, Action<string, string> log)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form = new ReEngineMainForm(appEnv, enginesDir, updateCoordinator, assocStore, log, containmentRoot);

#if REENGINE_ENABLE_AUTOMATION
        AutomationLoopbackServer? autoServer = null;
        form.Shown += (_, _) =>
        {
            autoServer = AutomationCliHelper.TryCreateFromArgs("reengine", args, form, allowedSnapshotRoot: containmentRoot ?? appEnv.DataDirectory);
            autoServer?.Start();
        };
        form.FormClosed += (_, _) => autoServer?.Dispose();
#endif

        Application.Run(form);
        return form.ExitCode;
    }

    private static string ResolveEnginesDir(string? cliEnginesDir, ReEngineSettings settings, AppEnvironment appEnv, string? containmentRoot)
    {
        if (!string.IsNullOrWhiteSpace(cliEnginesDir)) return Path.GetFullPath(cliEnginesDir.Trim());
        if (!string.IsNullOrWhiteSpace(settings.EnginesDirectoryOverride)) return Path.GetFullPath(settings.EnginesDirectoryOverride.Trim());
        string? suiteDir = ReEngineAppStatusHelper.TryReadSettingPath(settings, "suiteInstallDir");
        string? reEngineDir = ReEngineAppStatusHelper.TryReadSettingPath(settings, "reEngineInstallDir");
        string appRoot = ReEngineAppStatusHelper.ResolveAppInstallRoot();

        if (containmentRoot is not null)
        {
            string? suiteCand = !string.IsNullOrWhiteSpace(suiteDir) ? Path.Combine(suiteDir, "engines") : (!string.IsNullOrWhiteSpace(reEngineDir) ? ReEngineBootstrapHelper.ResolveEnginesRootDir(reEngineDir) : null);
            if (!string.IsNullOrWhiteSpace(suiteCand) && IsPathWithinBoundary(suiteCand, containmentRoot)) return suiteCand;
            string localCand = ReEngineBootstrapHelper.ResolveEnginesRootDir(appRoot);
            if (Directory.Exists(localCand) && IsPathWithinBoundary(localCand, containmentRoot)) return localCand;
            return Path.Combine(appEnv.DataDirectory, "engines");
        }

        if (!string.IsNullOrWhiteSpace(suiteDir)) return Path.Combine(suiteDir, "engines");
        if (!string.IsNullOrWhiteSpace(reEngineDir)) return ReEngineBootstrapHelper.ResolveEnginesRootDir(reEngineDir);
        string installedCand = ReEngineBootstrapHelper.ResolveEnginesRootDir(appRoot);
        if (Directory.Exists(installedCand) || File.Exists(Path.Combine(appRoot, "reEngine.exe")) || File.Exists(Path.Combine(appRoot, "version.txt"))) return installedCand;
        return ReEngineBootstrapHelper.ResolveEnginesRootDir(ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(isolatedTest: false));
    }

    private static bool IsPathWithinBoundary(string candidate, string boundary)
    {
        string c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string b = Path.GetFullPath(boundary).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(c, b, StringComparison.OrdinalIgnoreCase) || c.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveContainmentRoot(AppEnvironment appEnv)
    {
        string fullData = Path.GetFullPath(appEnv.DataDirectory);
        string? parent = Path.GetDirectoryName(fullData);
        return (!string.IsNullOrWhiteSpace(parent) && string.Equals(Path.GetFileName(fullData), "data", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetPathRoot(parent)?.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            ? parent : fullData;
    }

    private static void EnforceContainment(AppEnvironment appEnv, string enginesDir, string containmentRoot)
    {
        AppEnvironment.AssertPathContained(appEnv.DataDirectory, containmentRoot);
        AppEnvironment.AssertPathContained(appEnv.ConfigFilePath, containmentRoot);
        AppEnvironment.AssertPathContained(appEnv.LogDirectory, containmentRoot);
        AppEnvironment.AssertPathContained(enginesDir, containmentRoot);
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
        Console.WriteLine("Usage: reEngine.exe [--open <path> | <file.recomp>] [--backend opengl|vulkan|parallelCpu|software] [--scale 1|2|4|8] [--jit-bringup] [--wait] [--register-assoc] [--check-updates [--strict]] [--verify] [--doctor] [--host-demo]");
        Console.WriteLine("       reEngine.exe decomp --module <file.decomp> [--stage D0..D5] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>] [--data-dir <dir>] [--log-dir <dir>] [--config <path>] [--dry-run] [--window] [--version] [--help]");
    }
}
