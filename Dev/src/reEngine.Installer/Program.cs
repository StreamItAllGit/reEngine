// @module: reEngine.Installer.Program
// @purpose: 4-pillar zero-PII single-EXE installer entry point with AssemblyResolve, profiles, secondary path picker, and AllowAutoStart = false.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.CompilerServices, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: Program

using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Foundation.Installer;
using reEngine.Core.Bootstrap;

namespace reEngine.Installer;

internal static class Program
{
    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += static (_, e) =>
        {
            string? name = new AssemblyName(e.Name).Name;
            if (name is "Foundation.Core" or "Foundation.Installer" or "Foundation.Retro" or "reEngine.Core")
            {
                using Stream? gzStream = typeof(Program).Assembly.GetManifestResourceStream(name + ".dll.gz");
                if (gzStream is not null)
                {
                    using var decompressor = new GZipStream(gzStream, CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    decompressor.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
    }

    [STAThread]
    private static int Main(string[] args) => RunMain(args ?? Array.Empty<string>());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunMain(string[] args)
    {
        InstallerThemeOptions theme = CreateThemeOptions();
        InstallerAppDescriptor descriptor = CreateDescriptor(theme);
        string[] hostArgs = ExtractHostArgs(args, out string profileId, out string? suiteDir, out string? reEngineDir,
            out bool migrateLegacy, out bool confirmLegacyCleanup, out bool silent,
            out bool dryRun, out bool verifyOnly, out bool uninstall,
            out string? installDir, out string? dataDir);

        InstallerCliOptions? cli = null;
        try
        {
            cli = InstallerEngine.ParseCliArgs(hostArgs, descriptor);
        }
        catch (ArgumentException)
        {
            return StandardInstallerHost.Run(descriptor, hostArgs, typeof(Program).Assembly);
        }

        bool isIsolatedTest = IsIsolatedTestEnv();
        bool isInteractiveGui = !cli.ShowVersion &&
                                !cli.ShowHelp &&
                                !cli.Silent &&
                                !isIsolatedTest &&
                                !cli.DryRun &&
                                !cli.VerifyOnly &&
                                string.IsNullOrWhiteSpace(cli.InstallDir) &&
                                OperatingSystem.IsWindows();

        if (isInteractiveGui)
        {
            return RunInteractiveWizard(descriptor, theme, cli, suiteDir, reEngineDir, migrateLegacy, confirmLegacyCleanup);
        }

        int exitCode = StandardInstallerHost.Run(descriptor, hostArgs, typeof(Program).Assembly);
        if (exitCode == 0 && !verifyOnly && !uninstall && !dryRun)
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
            ApplyPostInstallActions(resolvedInstallDir, effectiveProfile, effectiveSuiteDir, effectiveReEngineDir,
                migrateLegacy, confirmLegacyCleanup, isIsolatedTest, dataDir, silent);
        }

        return exitCode;
    }

    private static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static int RunInteractiveWizard(
        InstallerAppDescriptor descriptor,
        InstallerThemeOptions theme,
        InstallerCliOptions cli,
        string? suiteDir,
        string? reEngineDir,
        bool migrateLegacy,
        bool confirmLegacyCleanup)
    {
        var ctx = new InstallerRuntimeContext(
            cliDataDir: cli.DataDir,
            cliConfigPath: cli.ConfigPath,
            cliLogDir: cli.LogDir,
            dryRun: cli.DryRun,
            envPrefix: descriptor.EnvPrefix);

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
            engine: guiEngine,
            ctx: ctx,
            initialInstallDir: resolvedInitialInstallDir,
            externalPayloadPath: cli.PayloadPath,
            isUninstallMode: cli.Uninstall,
            initialCreateShortcuts: !cli.NoShortcuts,
            initialAutoStart: theme.AllowAutoStart && cli.AutoStart,
            initialLaunchAfter: !cli.NoLaunch,
            initialPurgeData: cli.PurgeData,
            initialProfileId: initialProfile,
            initialSecondaryDir: initialSecondary,
            secondaryFlagName: secondaryFlag,
            initialMigrateLegacy: theme.AllowLegacyMigration);

        if (theme.AllowLegacyMigration && !cli.Uninstall)
        {
            wizard.MigrateLegacyCheckBox.Checked = migrateLegacy || (ctx.ConfigOverride?.MigrateLegacy ?? false);
        }

        ReEngineWizardPathNormalizer.WireWizardAutoSortHandlers(wizard);
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

            ApplyPostInstallActions(chosenInstallDir, chosenProfile, chosenSuiteDir, chosenReEngineDir,
                wizard.MigrateLegacyChecked, confirmLegacyCleanup, isolatedTest: ctx.IsIsolatedTestProcess, dataDir: cli.DataDir,
                silent: false);
        }

        return wizard.ExitCode;
    }

    private static void ApplyPostInstallActions(
        string installDir,
        string? profileId,
        string? suiteDir,
        string? reEngineDir,
        bool migrateLegacy,
        bool confirmLegacyCleanup,
        bool isolatedTest,
        string? dataDir,
        bool silent)
    {
        if (!Directory.Exists(installDir))
        {
            return;
        }

        string enginesRoot = ReEngineWizardPathNormalizer.ResolvePreferredEnginesRootDir(
            !string.IsNullOrWhiteSpace(reEngineDir) ? reEngineDir : installDir,
            suiteDir);
        Directory.CreateDirectory(enginesRoot);

        if (migrateLegacy)
        {
            LegacyMigrationResult migration = LegacyMigrationHelper.TryMigrateLegacyInstall(
                installDir, isolatedTest, dataDir, silent, dryRun: false, confirmLegacyCleanup);
            Console.WriteLine(migration.Summary);
        }

        LegacyMigrationHelper.PersistInstallProfileSettings(installDir, profileId, suiteDir, reEngineDir);
    }

    internal static InstallerAppDescriptor CreateDescriptor() => CreateDescriptor(CreateThemeOptions());

    private static InstallerAppDescriptor CreateDescriptor(InstallerThemeOptions theme) => new(
        ProductName: ReEnginePackagingConstants.DisplayName,
        DisplayName: theme.DisplayName,
        Publisher: theme.StudioName,
        Version: theme.Version,
        MainExecutableRelativePath: "reEngine.exe",
        Subtitle: theme.Subtitle,
        EmbeddedPayloadResourceName: "reEngine.Installer.Embedded.payload.zip",
        BrandAccentHex: theme.BrandAccentHex,
        AllowAutoStart: theme.AllowAutoStart,
        StudioName: theme.StudioName,
        SuiteName: theme.SuiteName,
        UmbrellaSubpath: theme.UmbrellaSubpath,
        DefaultProfile: theme.DefaultProfileId);

    internal static IReadOnlyList<InstallerProfileOption> CreateAvailableProfiles() =>
    [
        new InstallerProfileOption(
            Id: "lightweight",
            Kind: InstallerProfileKind.Lightweight,
            Label: "Lightweight",
            Description: "Core .decomp / .recomp router & engine manager (< 5 MB).",
            IsDefault: true),
        new InstallerProfileOption(
            Id: "full",
            Kind: InstallerProfileKind.Full,
            Label: "Full (Offline Toolchain)",
            Description: "Includes offline Apache-2.0 headless Ghidra + OpenJDK toolchain (<= 160 MB).",
            IsDefault: false)
    ];

    internal static SecondaryPathPickerOptions CreateSecondaryPathPicker() => new(
        FlagName: "--suite-dir",
        Label: "Suite Root Directory:",
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

    internal static string[] SanitizeArgs(string[]? args) =>
        args is null
            ? Array.Empty<string>()
            : args.Where(a => !string.Equals(a?.Trim(), "--autostart", StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static string[] ExtractHostArgs(
        string[] args,
        out string profileId,
        out string? suiteDir,
        out string? reEngineDir,
        out bool migrateLegacy,
        out bool confirmLegacyCleanup,
        out bool silent,
        out bool dryRun,
        out bool verifyOnly,
        out bool uninstall,
        out string? installDir,
        out string? dataDir)
    {
        string[] clean = SanitizeArgs(args);
        profileId = ReEnginePackagingConstants.DefaultInstallProfile;
        suiteDir = null;
        reEngineDir = null;
        migrateLegacy = false;
        confirmLegacyCleanup = false;
        silent = false;
        dryRun = false;
        verifyOnly = false;
        uninstall = false;
        installDir = null;
        dataDir = null;

        var forwarded = new List<string>(clean.Length);
        for (int i = 0; i < clean.Length; i++)
        {
            string arg = clean[i].Trim();
            if (string.Equals(arg, "--confirm-legacy-cleanup", StringComparison.OrdinalIgnoreCase))
            {
                confirmLegacyCleanup = true;
                continue;
            }

            forwarded.Add(clean[i]);
            switch (arg.ToLowerInvariant())
            {
                case "--profile" when i + 1 < clean.Length:
                    profileId = LegacyMigrationHelper.NormalizeProfileId(clean[i + 1]);
                    break;
                case "--suite-dir" when i + 1 < clean.Length:
                    suiteDir = clean[i + 1];
                    break;
                case "--reengine-dir" when i + 1 < clean.Length:
                    reEngineDir = clean[i + 1];
                    break;
                case "--install-dir":
                case "--target-dir":
                    if (i + 1 < clean.Length) installDir = clean[i + 1];
                    break;
                case "--data-dir":
                    if (i + 1 < clean.Length) dataDir = clean[i + 1];
                    break;
                case "--migrate-legacy": migrateLegacy = true; break;
                case "--silent":
                case "--quiet": silent = true; break;
                case "--dry-run": dryRun = true; break;
                case "--verify": verifyOnly = true; break;
                case "--uninstall": uninstall = true; break;
            }
        }

        if (string.IsNullOrWhiteSpace(installDir))
        {
            if (!string.IsNullOrWhiteSpace(reEngineDir))
            {
                installDir = Path.GetFullPath(reEngineDir.Trim());
                forwarded.Add("--install-dir");
                forwarded.Add(installDir);
            }
            else if (!string.IsNullOrWhiteSpace(suiteDir))
            {
                installDir = Path.Combine(Path.GetFullPath(suiteDir.Trim()), ReEnginePackagingConstants.DisplayName);
                forwarded.Add("--install-dir");
                forwarded.Add(installDir);
            }
        }

        return forwarded.ToArray();
    }

    private static string ResolveEffectiveInstallDir(
        InstallerAppDescriptor descriptor,
        InstallerCliOptions cli,
        string? installDir,
        string? dataDir,
        bool isolatedTest)
    {
        if (!string.IsNullOrWhiteSpace(installDir))
        {
            return Path.GetFullPath(installDir);
        }
        if (isolatedTest && !string.IsNullOrWhiteSpace(dataDir))
        {
            return Path.GetFullPath(Path.Combine(dataDir, "installed-app"));
        }
        var ctx = new InstallerRuntimeContext(cli.DataDir, cli.ConfigPath, cli.LogDir, cli.DryRun, descriptor.EnvPrefix);
        return new InstallerEngine(descriptor, ctx, typeof(Program).Assembly).ResolveDefaultInstallDirectory();
    }

    private static string? ResolveEffectiveSuiteDir(string? explicitSuiteDir, string installDir) =>
        ReEngineWizardPathNormalizer.ResolvePreferredSuiteInstallDir(installDir, explicitSuiteDir);
}
