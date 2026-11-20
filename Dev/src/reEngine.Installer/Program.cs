// @module: reEngine.Installer.Program
// @purpose: 4-pillar zero-PII single-EXE installer entry point with AssemblyResolve, profiles, secondary path picker, and AllowAutoStart = false.
// @depends-on: System, System.Collections.Concurrent, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.CompilerServices, System.Windows.Forms, Foundation.Core, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: Program

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Foundation.Core;
using Foundation.Installer;
using reEngine.Core.Bootstrap;

namespace reEngine.Installer;

internal static class Program
{
    private static readonly string[] EmbeddedAssemblies =
#if REENGINE_ENABLE_AUTOMATION
        ["Foundation.Core", "Foundation.Installer", "Foundation.Retro", "reEngine.Core", "Foundation.Automation", "Foundation.UI"];
#else
        ["Foundation.Core", "Foundation.Installer", "Foundation.Retro", "reEngine.Core"];
#endif
    private static readonly ConcurrentDictionary<string, Assembly> ResolvedAssemblies = new(StringComparer.OrdinalIgnoreCase);

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            string? name = new AssemblyName(e.Name).Name;
            if (string.IsNullOrWhiteSpace(name) || !EmbeddedAssemblies.Contains(name, StringComparer.OrdinalIgnoreCase))
                return null;
            if (ResolvedAssemblies.TryGetValue(name, out Assembly? cached))
                return cached;

            using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(name + ".dll.gz");
            if (gzStream is null) return null;
            using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            decompressor.CopyTo(ms);
            return ResolvedAssemblies.GetOrAdd(name, Assembly.Load(ms.ToArray()));
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int RunMain(string[] args)
    {
#if !REENGINE_ENABLE_AUTOMATION
        if (InstallerCliAndAutomation.HasAutomationPortFlag(args))
        {
            InstallerCliAndAutomation.AttachParentConsoleIfInteractive(args);
            Console.Error.WriteLine("Automation loopback is disabled in Release builds (ExitCode = 2).");
            return 2;
        }
#endif

        InstallerThemeOptions theme = CreateThemeOptions();
        InstallerAppDescriptor descriptor = CreateDescriptor(theme);
        string[] cleanArgs = SanitizeArgs(args);

        int? automationPort;
        string? automationToken, suiteDir, reEngineDir, installDir, dataDir;
        bool migrateLegacy, confirmLegacyCleanup, silent, dryRun, verifyOnly, uninstall;
        string[] hostArgs;
        try
        {
            string[] withoutAutomation = InstallerCliAndAutomation.ExtractAutomationArgs(cleanArgs, out automationPort, out automationToken);
            hostArgs = ExtractHostArgs(withoutAutomation, out _, out suiteDir, out reEngineDir,
                out migrateLegacy, out confirmLegacyCleanup, out silent, out dryRun, out verifyOnly,
                out uninstall, out installDir, out dataDir);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Setup argument error: {ex.Message}");
            return 2;
        }

        InstallerCliOptions cli;
        try { cli = InstallerEngine.ParseCliArgs(hostArgs, descriptor); }
        catch (ArgumentException) { return StandardInstallerHost.Run(descriptor, hostArgs, typeof(Program).Assembly); }

        bool isIsolatedTest = InstallerCliAndAutomation.IsIsolatedTestEnv();
        bool forceAutomatedWizard =
#if REENGINE_ENABLE_AUTOMATION
            automationPort.HasValue;
#else
            false;
#endif
        bool isInteractiveGui = !cli.ShowVersion && !cli.ShowHelp && !cli.Silent && !cli.DryRun && !cli.VerifyOnly &&
                                ((!isIsolatedTest && string.IsNullOrWhiteSpace(cli.InstallDir)) || forceAutomatedWizard) &&
                                OperatingSystem.IsWindows();

        if (isInteractiveGui)
        {
            try
            {
                return RunInteractiveWizard(descriptor, theme, cli, suiteDir, reEngineDir,
                    migrateLegacy, confirmLegacyCleanup, automationPort, automationToken);
            }
            catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
            {
                Console.Error.WriteLine(ex.Message);
                return SandboxEnvironmentSeam.ContainmentExitCode;
            }
        }

        int exitCode = StandardInstallerHost.Run(descriptor, hostArgs, typeof(Program).Assembly);
        if (exitCode == 0 && !cli.ShowVersion && !cli.ShowHelp && !verifyOnly && !uninstall && !dryRun)
        {
            string resolvedInstallDir = ResolveEffectiveInstallDir(descriptor, cli, installDir, dataDir, isIsolatedTest);
            LegacyMigrationHelper.TryReadExistingInstallSettings(
                resolvedInstallDir, out string? existingProfile, out string? existingSuiteDir, out string? existingReEngineDir);
            string? effectiveProfile = !string.IsNullOrWhiteSpace(cli.ProfileId)
                ? LegacyMigrationHelper.NormalizeProfileId(cli.ProfileId)
                : existingProfile;
            string? effectiveSuiteDir = !string.IsNullOrWhiteSpace(suiteDir)
                ? Path.GetFullPath(suiteDir.Trim())
                : (!string.IsNullOrWhiteSpace(existingSuiteDir)
                    ? existingSuiteDir
                    : ResolveEffectiveSuiteDir(null, resolvedInstallDir));
            string effectiveReEngineDir = !string.IsNullOrWhiteSpace(reEngineDir)
                ? Path.GetFullPath(reEngineDir.Trim())
                : (!string.IsNullOrWhiteSpace(existingReEngineDir) ? existingReEngineDir : resolvedInstallDir);
            InstallerCliAndAutomation.ApplyPostInstallActions(resolvedInstallDir, effectiveProfile, effectiveSuiteDir, effectiveReEngineDir,
                migrateLegacy, confirmLegacyCleanup, isIsolatedTest, dataDir, silent);
        }

        return exitCode;
    }

    private static int RunInteractiveWizard(
        InstallerAppDescriptor descriptor, InstallerThemeOptions theme, InstallerCliOptions cli,
        string? suiteDir, string? reEngineDir, bool migrateLegacy, bool confirmLegacyCleanup,
        int? automationPort, string? automationToken)
    {
        var ctx = new InstallerRuntimeContext(cli.DataDir, cli.ConfigPath, cli.LogDir, cli.DryRun, descriptor.EnvPrefix);
        var guiEngine = new InstallerEngine(descriptor, ctx, typeof(Program).Assembly);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        string resolvedInitialInstallDir = !string.IsNullOrWhiteSpace(cli.InstallDir)
            ? Path.GetFullPath(cli.InstallDir)
            : guiEngine.ResolveDefaultInstallDirectory();
        LegacyMigrationHelper.TryReadExistingInstallSettings(
            resolvedInitialInstallDir, out string? savedProfile, out string? savedSuiteDir, out string? savedReEngineDir);

        string initialProfile = !string.IsNullOrWhiteSpace(cli.ProfileId)
            ? LegacyMigrationHelper.NormalizeProfileId(cli.ProfileId)
            : (!string.IsNullOrWhiteSpace(savedProfile) ? savedProfile : theme.DefaultProfileId);
        SecondaryPathPickerOptions? picker = theme.SecondaryPathPicker;
        string? inferredSuiteDir = Directory.Exists(resolvedInitialInstallDir)
            ? ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(resolvedInitialInstallDir, savedSuiteDir)
            : null;
        string? initialSecondary = !string.IsNullOrWhiteSpace(suiteDir)
            ? suiteDir
            : (!string.IsNullOrWhiteSpace(reEngineDir)
                ? reEngineDir
                : (!string.IsNullOrWhiteSpace(inferredSuiteDir)
                    ? inferredSuiteDir
                    : (!string.IsNullOrWhiteSpace(savedReEngineDir) ? savedReEngineDir : picker?.DefaultPath)));
        string? secondaryFlag = !string.IsNullOrWhiteSpace(reEngineDir) && string.IsNullOrWhiteSpace(suiteDir)
            ? "--reengine-dir"
            : picker?.FlagName;

        using var wizard = new InstallerWizardForm(
            engine: guiEngine, ctx: ctx, initialInstallDir: resolvedInitialInstallDir,
            externalPayloadPath: cli.PayloadPath, isUninstallMode: cli.Uninstall,
            initialCreateShortcuts: !cli.NoShortcuts, initialAutoStart: theme.AllowAutoStart && cli.AutoStart,
            initialLaunchAfter: !cli.NoLaunch, initialPurgeData: cli.PurgeData,
            initialProfileId: initialProfile, initialSecondaryDir: initialSecondary,
            secondaryFlagName: secondaryFlag, initialMigrateLegacy: theme.AllowLegacyMigration);

        if (theme.AllowLegacyMigration && !cli.Uninstall)
            wizard.MigrateLegacyCheckBox.Checked = migrateLegacy || (ctx.ConfigOverride?.MigrateLegacy ?? false);

        ReEngineWizardPathNormalizer.WireWizardAutoSortHandlers(wizard);
        using IDisposable? automationHook = InstallerCliAndAutomation.TryAttachWizardAutomation(
            wizard, automationPort, automationToken, ctx);
        Application.Run(wizard);
        if (wizard.ExitCode == 0 && !cli.Uninstall)
        {
            string chosenInstallDir = string.IsNullOrWhiteSpace(wizard.InstallDirTextBox.Text)
                ? theme.DefaultInstallDir
                : ReEngineWizardPathNormalizer.NormalizeWizardReEngineDir(wizard.InstallDirTextBox.Text);
            string chosenProfile = LegacyMigrationHelper.NormalizeProfileId(wizard.SelectedProfileId);
            string chosenSecondary = wizard.SecondaryInstallDir.Trim();
            string chosenReEngineDir = string.Equals(secondaryFlag, "--reengine-dir", StringComparison.OrdinalIgnoreCase) &&
                                       !string.IsNullOrWhiteSpace(chosenSecondary)
                ? ReEngineWizardPathNormalizer.NormalizeWizardReEngineDir(chosenSecondary)
                : chosenInstallDir;
            string? chosenSuiteDir = string.Equals(secondaryFlag, "--reengine-dir", StringComparison.OrdinalIgnoreCase)
                ? ResolveEffectiveSuiteDir(suiteDir, chosenReEngineDir)
                : (!string.IsNullOrWhiteSpace(chosenSecondary)
                    ? Path.GetFullPath(chosenSecondary)
                    : ResolveEffectiveSuiteDir(suiteDir, chosenInstallDir));

            InstallerCliAndAutomation.ApplyPostInstallActions(chosenInstallDir, chosenProfile, chosenSuiteDir, chosenReEngineDir,
                wizard.MigrateLegacyChecked, confirmLegacyCleanup, isolatedTest: ctx.IsIsolatedTestProcess, dataDir: cli.DataDir,
                silent: false);
        }

        return wizard.ExitCode;
    }

    internal static InstallerAppDescriptor CreateDescriptor() => CreateDescriptor(CreateThemeOptions());

    private static InstallerAppDescriptor CreateDescriptor(InstallerThemeOptions theme) => new(
        ProductName: ReEnginePackagingConstants.DisplayName, DisplayName: theme.DisplayName,
        Publisher: theme.StudioName, Version: theme.Version, MainExecutableRelativePath: "reEngine.exe",
        Subtitle: theme.Subtitle, EmbeddedPayloadResourceName: "reEngine.Installer.Embedded.payload.zip",
        BrandAccentHex: theme.BrandAccentHex, AllowAutoStart: theme.AllowAutoStart,
        StudioName: theme.StudioName, SuiteName: theme.SuiteName,
        UmbrellaSubpath: theme.UmbrellaSubpath, DefaultProfile: theme.DefaultProfileId);

    internal static IReadOnlyList<InstallerProfileOption> CreateAvailableProfiles() =>
    [
        new InstallerProfileOption("lightweight", InstallerProfileKind.Lightweight, "Lightweight",
            "Core .decomp / .recomp router & engine manager (< 5 MB).", IsDefault: true),
        new InstallerProfileOption("full", InstallerProfileKind.Full, "Full (Offline Toolchain)",
            "Includes offline Apache-2.0 headless Ghidra + OpenJDK toolchain (<= 160 MB).", IsDefault: false)
    ];

    internal static SecondaryPathPickerOptions CreateSecondaryPathPicker() => new(
        FlagName: "--suite-dir", Label: "Suite Root Directory:",
        DefaultPath: ReEngineBootstrapHelper.ResolveDefaultSuiteInstallDir(),
        Placeholder: "%LOCALAPPDATA%\\Programs\\StreamItAll\\reEngine",
        Tooltip: "Optional companion suite directory for shared engines and games (--suite-dir / --reengine-dir).");

    internal static InstallerThemeOptions CreateThemeOptions() => new()
    {
        AppId = ReEnginePackagingConstants.AppId,
        DisplayName = ReEnginePackagingConstants.DisplayName,
        Version = ReEnginePackagingConstants.Version,
        Subtitle = "Zero-Daemon .decomp / .recomp Router and Side-by-Side Engine Manager",
        BrandAccentHex = ReEnginePackagingConstants.BrandAccentHex,
        DefaultInstallDir = ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(),
        AllowAutoStart = ReEnginePackagingConstants.AllowAutoStart,
        BootstrapSharedSuiteDaemon = false,
        StudioName = ReEnginePackagingConstants.StudioName,
        SuiteName = ReEnginePackagingConstants.SuiteName,
        UmbrellaSubpath = ReEnginePackagingConstants.UmbrellaSubpath,
        AvailableProfiles = CreateAvailableProfiles(),
        DefaultProfileId = ReEnginePackagingConstants.DefaultInstallProfile,
        SecondaryPathPicker = CreateSecondaryPathPicker(),
        AllowLegacyMigration = true
    };

    internal static string[] SanitizeArgs(string[]? args) => InstallerCliAndAutomation.SanitizeArgs(args);

    internal static string[] ExtractHostArgs(
        string[] args, out string profileId, out string? suiteDir, out string? reEngineDir,
        out bool migrateLegacy, out bool confirmLegacyCleanup, out bool silent,
        out bool dryRun, out bool verifyOnly, out bool uninstall, out string? installDir, out string? dataDir) =>
        InstallerCliAndAutomation.ExtractHostArgs(args, out profileId, out suiteDir, out reEngineDir,
            out migrateLegacy, out confirmLegacyCleanup, out silent, out dryRun, out verifyOnly,
            out uninstall, out installDir, out dataDir);

    private static string ResolveEffectiveInstallDir(
        InstallerAppDescriptor descriptor, InstallerCliOptions cli, string? installDir, string? dataDir, bool isolatedTest)
    {
        if (!string.IsNullOrWhiteSpace(installDir)) return Path.GetFullPath(installDir);
        if (isolatedTest && !string.IsNullOrWhiteSpace(dataDir)) return Path.GetFullPath(Path.Combine(dataDir, "installed-app"));
        var ctx = new InstallerRuntimeContext(cli.DataDir, cli.ConfigPath, cli.LogDir, cli.DryRun, descriptor.EnvPrefix);
        return new InstallerEngine(descriptor, ctx, typeof(Program).Assembly).ResolveDefaultInstallDirectory();
    }

    private static string? ResolveEffectiveSuiteDir(string? explicitSuiteDir, string installDir) =>
        ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(installDir, explicitSuiteDir);
}
