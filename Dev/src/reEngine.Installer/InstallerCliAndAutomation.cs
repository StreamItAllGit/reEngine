// @module: reEngine.Installer.InstallerCliAndAutomation
// @purpose: CLI argument sanitization, host/automation option parsing, and Debug-only AutomationLoopbackServer hook for reEngine.Installer.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Runtime.CompilerServices, System.Runtime.InteropServices, Foundation.Installer, reEngine.Core.Bootstrap
// @exports: InstallerCliAndAutomation

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation.Installer;
using reEngine.Core.Bootstrap;
#if REENGINE_ENABLE_AUTOMATION
using System.Security.Cryptography;
using Foundation.Automation;
using Foundation.Core;
#endif

namespace reEngine.Installer;

internal static class InstallerCliAndAutomation
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static void AttachParentConsoleIfInteractive(string[]? args)
    {
        if (args is { Length: > 0 } && OperatingSystem.IsWindows() && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
            _ = AttachConsole(AttachParentProcess);
    }

    public static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyPostInstallActions(
        string installDir, string? profileId, string? suiteDir, string? reEngineDir,
        bool migrateLegacy, bool confirmLegacyCleanup, bool isolatedTest, string? dataDir, bool silent)
    {
        if (!Directory.Exists(installDir)) return;

        string enginesRoot = ReEngineWizardPathNormalizer.ResolvePreferredEnginesRootDir(
            !string.IsNullOrWhiteSpace(reEngineDir) ? reEngineDir : installDir, suiteDir);
        Directory.CreateDirectory(enginesRoot);

        if (migrateLegacy)
        {
            LegacyMigrationResult migration = LegacyMigrationHelper.TryMigrateLegacyInstall(
                installDir, isolatedTest, dataDir, silent, dryRun: false, confirmLegacyCleanup);
            Console.WriteLine(migration.Summary);
        }
        LegacyMigrationHelper.PersistInstallProfileSettings(installDir, profileId, suiteDir, reEngineDir);
    }

    public static bool HasAutomationPortFlag(string[]? args) =>
        args is not null && args.Any(raw =>
        {
            string trimmed = (raw ?? string.Empty).Trim();
            return trimmed.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase);
        });

    public static string[] SanitizeArgs(string[]? args) =>
        args is null
            ? Array.Empty<string>()
            : args.Where(a => !string.IsNullOrWhiteSpace(a) &&
                              !a.Trim().StartsWith("--autostart", StringComparison.OrdinalIgnoreCase) &&
                              !a.Trim().StartsWith("--auto-start", StringComparison.OrdinalIgnoreCase))
                  .ToArray();

    public static string[] ExtractAutomationArgs(string[] clean, out int? automationPort, out string? automationToken)
    {
        automationPort = null;
        automationToken = null;
        var forwarded = new List<string>(clean.Length);
        for (int i = 0; i < clean.Length; i++)
        {
            string trimmed = (clean[i] ?? string.Empty).Trim();
            if (trimmed.Equals("--automation-port", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("--automation-port=", StringComparison.OrdinalIgnoreCase))
            {
                int eq = trimmed.IndexOf('=');
                string raw = eq >= 0 ? trimmed[(eq + 1)..] : (i + 1 < clean.Length ? (clean[++i] ?? string.Empty) : throw new ArgumentException("--automation-port requires a port value."));
                if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort) || parsedPort is < 0 or > 65535)
                    throw new ArgumentException($"--automation-port expects an integer in 0..65535, got '{raw}'.");
                automationPort = parsedPort;
                continue;
            }
            if (trimmed.Equals("--automation-token", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("--automation-token=", StringComparison.OrdinalIgnoreCase))
            {
                int eq = trimmed.IndexOf('=');
                string rawTok = eq >= 0 ? trimmed[(eq + 1)..] : (i + 1 < clean.Length ? (clean[++i] ?? string.Empty) : throw new ArgumentException("--automation-token requires a non-empty value."));
                if (string.IsNullOrWhiteSpace(rawTok))
                    throw new ArgumentException("--automation-token requires a non-empty value.");
                automationToken = rawTok.Trim();
                continue;
            }
            forwarded.Add(clean[i]);
        }
        return forwarded.ToArray();
    }

    public static string[] ExtractHostArgs(
        string[] args, out string profileId, out string? suiteDir, out string? reEngineDir,
        out bool migrateLegacy, out bool confirmLegacyCleanup, out bool silent,
        out bool dryRun, out bool verifyOnly, out bool uninstall, out string? installDir, out string? dataDir)
    {
        string[] clean = SanitizeArgs(args);
        profileId = ReEnginePackagingConstants.DefaultInstallProfile;
        suiteDir = null; reEngineDir = null; installDir = null; dataDir = null;
        migrateLegacy = false; confirmLegacyCleanup = false;
        silent = false; dryRun = false; verifyOnly = false; uninstall = false;

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
                case "--profile" when i + 1 < clean.Length: profileId = LegacyMigrationHelper.NormalizeProfileId(clean[i + 1]); break;
                case "--suite-dir" when i + 1 < clean.Length: suiteDir = clean[i + 1]; break;
                case "--reengine-dir" when i + 1 < clean.Length: reEngineDir = clean[i + 1]; break;
                case "--install-dir" or "--target-dir" when i + 1 < clean.Length: installDir = clean[i + 1]; break;
                case "--data-dir" when i + 1 < clean.Length: dataDir = clean[i + 1]; break;
                case "--migrate-legacy": migrateLegacy = true; break;
                case "--silent" or "--quiet": silent = true; break;
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

#if REENGINE_ENABLE_AUTOMATION
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDisposable? TryAttachWizardAutomation(
        InstallerWizardForm wizard, int? automationPort, string? automationToken, InstallerRuntimeContext ctx) =>
        automationPort is { } port ? WizardAutomationHook.Attach(wizard, port, automationToken, ctx) : null;

    private sealed class WizardAutomationHook : IDisposable
    {
        private readonly AutomationLoopbackServer _server;
        private bool _disposed;

        private WizardAutomationHook(AutomationLoopbackServer server) => _server = server;

        public static WizardAutomationHook Attach(InstallerWizardForm wizard, int port, string? token, InstallerRuntimeContext ctx)
        {
            ArgumentNullException.ThrowIfNull(wizard);
            ArgumentNullException.ThrowIfNull(ctx);

            ApplyControlNames(wizard);
            string? envToken = Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN");
            string effectiveToken = !string.IsNullOrWhiteSpace(token)
                ? token.Trim()
                : (!string.IsNullOrWhiteSpace(envToken) ? envToken.Trim() : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());

            var opts = new AutomationServerOptions(port, effectiveToken, ReEnginePackagingConstants.AppId, AllowedSnapshotRoot: null);
            SandboxResolution effectiveSandbox = ctx.IsSandboxActive ? ctx.Sandbox : SandboxResolution.Inactive;
            var server = new AutomationLoopbackServer(opts, wizard, renderProvider: null, sandbox: effectiveSandbox);
            var hook = new WizardAutomationHook(server);

            wizard.Load += (_, _) =>
            {
                if (hook._disposed || server.IsListening) return;
                server.Start();
                ctx.WriteLog("AUTOMATION", $"Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
                Console.WriteLine($"AUTOMATION: Wizard loopback listening on 127.0.0.1:{server.AutomationPort}");
            };
            wizard.FormClosed += (_, _) => hook.Dispose();
            return hook;
        }

        private static void ApplyControlNames(InstallerWizardForm wizard)
        {
            wizard.Name = "InstallerWizardRoot";
            wizard.PrimaryButton.Name = "Wizard_PrimaryButton";
            wizard.CancelButton.Name = "Wizard_CancelButton";
            wizard.BrowseButton.Name = "Wizard_BrowseButton";
            wizard.DetailsToggleButton.Name = "Wizard_DetailsToggle";
            wizard.InstallDirTextBox.Name = "Wizard_InstallDir";
            wizard.SecondaryDirTextBox.Name = "Wizard_SecondaryDir";
            wizard.LightweightProfileRadioButton.Name = "Wizard_ProfileLightweight";
            wizard.FullProfileRadioButton.Name = "Wizard_ProfileFull";
            wizard.ShortcutsCheckBox.Name = "Wizard_Shortcuts";
            wizard.LaunchAfterCheckBox.Name = "Wizard_LaunchAfter";
            wizard.MigrateLegacyCheckBox.Name = "Wizard_MigrateLegacy";
            wizard.StatusLabel.Name = "Wizard_StatusLabel";
            wizard.LogTextBox.Name = "Wizard_LogTextBox";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _server.Dispose();
        }
    }
#else
    public static IDisposable? TryAttachWizardAutomation(
        InstallerWizardForm wizard, int? automationPort, string? automationToken, InstallerRuntimeContext ctx)
    {
        _ = wizard; _ = automationPort; _ = automationToken; _ = ctx;
        return null;
    }
#endif
}
