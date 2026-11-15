// @module: reEngine.Tests.InstallerAndAppStatusTests
// @purpose: xUnit contract tests for installer profiles (--profile Lightweight|Full), --suite-dir/--reengine-dir, AllowAutoStart = false, opt-in --migrate-legacy, and ReEngineAppStatusHelper.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, Foundation.Installer, Xunit, reEngine.App, reEngine.Core, reEngine.Installer
// @exports: InstallerAndAppStatusTests

using System.Text.Json;
using Foundation.Installer;
using reEngine.App;
using reEngine.Core.Updates;
using reEngine.Installer;
using Xunit;

namespace reEngine.Tests;

public sealed class InstallerAndAppStatusTests : IDisposable
{
    private readonly string _tempRoot;

    public InstallerAndAppStatusTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_installer_status_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { /* ignore transient temp cleanup lock */ }
    }

    [Fact]
    public void InstallerProgram_EnforcesAllowAutoStartFalse_AndParsesProfilesAndSuitePickers()
    {
        InstallerAppDescriptor descriptor = reEngine.Installer.Program.CreateDescriptor();
        Assert.False(descriptor.AllowAutoStart);
        Assert.Equal("StreamItAll", descriptor.StudioName);
        Assert.Equal("reEngine", descriptor.SuiteName);
        Assert.Equal("StreamItAll\\reEngine\\reEngine", descriptor.UmbrellaSubpath);
        Assert.Equal("lightweight", descriptor.DefaultProfile);

        InstallerThemeOptions theme = reEngine.Installer.Program.CreateThemeOptions();
        Assert.False(theme.AllowAutoStart);
        Assert.False(theme.BootstrapSharedSuiteDaemon);
        Assert.True(theme.AllowLegacyMigration);
        Assert.NotNull(theme.SecondaryPathPicker);
        Assert.Equal("--suite-dir", theme.SecondaryPathPicker!.FlagName);

        IReadOnlyList<InstallerProfileOption> profiles = reEngine.Installer.Program.CreateAvailableProfiles();
        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, p => p.Id == "lightweight" && p.Kind == InstallerProfileKind.Lightweight && p.IsDefault);
        Assert.Contains(profiles, p => p.Id == "full" && p.Kind == InstallerProfileKind.Full && !p.IsDefault);

        string customSuite = Path.Combine(_tempRoot, "custom-suite");
        string customReEngine = Path.Combine(customSuite, "reEngine");
        string[] rawArgs =
        [
            "--autostart",
            "--profile", "Full",
            "--suite-dir", customSuite,
            "--reengine-dir", customReEngine,
            "--migrate-legacy",
            "--confirm-legacy-cleanup",
            "--test-mode",
            "--silent",
            "--headless"
        ];

        string[] forwarded = reEngine.Installer.Program.ExtractHostArgs(
            rawArgs,
            out string profileId,
            out string? suiteDir,
            out string? reEngineDir,
            out bool migrateLegacy,
            out bool confirmCleanup,
            out bool testMode,
            out bool silent,
            out bool headless,
            out bool dryRun,
            out bool verifyOnly,
            out bool uninstall,
            out string? installDir,
            out string? dataDir);

        Assert.Equal("full", profileId);
        Assert.Equal(customSuite, suiteDir);
        Assert.Equal(customReEngine, reEngineDir);
        Assert.True(migrateLegacy);
        Assert.True(confirmCleanup);
        Assert.True(testMode);
        Assert.True(silent);
        Assert.True(headless);
        Assert.False(dryRun);
        Assert.False(verifyOnly);
        Assert.False(uninstall);
        Assert.Equal(Path.GetFullPath(customReEngine), installDir);
        Assert.Null(dataDir);
        Assert.Contains("--install-dir", forwarded, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(customReEngine), forwarded, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("--autostart", forwarded, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("--confirm-legacy-cleanup", forwarded, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("--migrate-legacy", forwarded, StringComparer.OrdinalIgnoreCase);

        string[] suiteOnlyForwarded = reEngine.Installer.Program.ExtractHostArgs(
            ["--suite-dir", customSuite, "--silent"],
            out _,
            out string? suiteOnlyDir,
            out string? reEngineOnlyDir,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out string? derivedInstallDir,
            out _);

        string expectedDerivedReEngine = Path.Combine(Path.GetFullPath(customSuite), "reEngine");
        Assert.Equal(customSuite, suiteOnlyDir);
        Assert.Null(reEngineOnlyDir);
        Assert.Equal(expectedDerivedReEngine, derivedInstallDir);
        Assert.Contains("--install-dir", suiteOnlyForwarded, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(expectedDerivedReEngine, suiteOnlyForwarded, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("lightweight", LegacyMigrationHelper.NormalizeProfileId("Lightweight"));
        Assert.Equal("full", LegacyMigrationHelper.NormalizeProfileId("FULL"));
        Assert.Throws<ArgumentException>(() => LegacyMigrationHelper.NormalizeProfileId("invalid-profile"));
    }

    [Fact]
    public void LegacyMigrationHelper_PreservesDataRom_AndGatesStrandedV011CleanupOnConfirmation()
    {
        string sandboxDir = Path.Combine(_tempRoot, "Sandbox");
        string legacyRoot = Path.Combine(sandboxDir, "legacy-programs", "reEngine");
        string targetInstallDir = Path.Combine(sandboxDir, "installed-reEngine");

        string legacyData = Path.Combine(legacyRoot, "data");
        string legacyRom = Path.Combine(legacyData, "rom");
        string legacyEngineApp = Path.Combine(legacyRoot, "engines", "reStation", "v0.1.1", "app");
        string legacyCrashes = Path.Combine(legacyRoot, "logs", "crashes");
        Directory.CreateDirectory(legacyRom);
        Directory.CreateDirectory(legacyEngineApp);
        Directory.CreateDirectory(legacyCrashes);

        File.WriteAllText(Path.Combine(legacyData, "settings.json"), """{ "schemaVersion": 1, "shouldCheckUpdates": false }""");
        File.WriteAllBytes(Path.Combine(legacyRom, "disc.cue"), [0x46, 0x49, 0x4C, 0x45]);
        string strandedAppFile = Path.Combine(legacyEngineApp, "retail_dummy.bin");
        string strandedCrashFile = Path.Combine(legacyCrashes, "crash_2026-11-14.json");
        File.WriteAllBytes(strandedAppFile, [0x01]);
        File.WriteAllText(strandedCrashFile, "{}");

        LegacyMigrationHelper.PersistInstallProfileSettings(targetInstallDir, "Full", sandboxDir, targetInstallDir);
        string persistedJson = File.ReadAllText(Path.Combine(targetInstallDir, "data", "settings.json"));
        using (JsonDocument doc = JsonDocument.Parse(persistedJson))
        {
            Assert.Equal("full", doc.RootElement.GetProperty("installProfile").GetString());
            Assert.Equal(Path.GetFullPath(sandboxDir), doc.RootElement.GetProperty("suiteInstallDir").GetString());
            Assert.Equal(Path.GetFullPath(targetInstallDir), doc.RootElement.GetProperty("reEngineInstallDir").GetString());
        }

        // Remove settings.json briefly to test migration copy from legacy install
        File.Delete(Path.Combine(targetInstallDir, "data", "settings.json"));

        LegacyMigrationResult firstPass = LegacyMigrationHelper.TryMigrateLegacyInstall(
            targetInstallDir,
            testMode: true,
            sandboxDataDir: sandboxDir,
            silent: true,
            headless: true,
            dryRun: false,
            confirmCleanupFlag: false);

        Assert.True(firstPass.MigratedSettings);
        Assert.Equal(1, firstPass.PreservedRomFiles);
        Assert.True(File.Exists(Path.Combine(targetInstallDir, "data", "rom", "disc.cue")));
        Assert.Equal(2, firstPass.StrandedArtifactsFound.Count);
        Assert.False(firstPass.StrandedArtifactsCleaned);
        Assert.True(Directory.Exists(legacyEngineApp));
        Assert.True(File.Exists(strandedCrashFile));

        LegacyMigrationResult confirmedPass = LegacyMigrationHelper.TryMigrateLegacyInstall(
            targetInstallDir,
            testMode: true,
            sandboxDataDir: sandboxDir,
            silent: true,
            headless: true,
            dryRun: false,
            confirmCleanupFlag: true);

        Assert.True(confirmedPass.StrandedArtifactsCleaned);
        Assert.False(Directory.Exists(legacyEngineApp));
        Assert.False(File.Exists(strandedCrashFile));
        Assert.True(File.Exists(Path.Combine(targetInstallDir, "data", "rom", "disc.cue")));
    }

    [Fact]
    public void ReEngineAppStatusHelper_TracksInstallProfileAotCacheAndDecompReadiness()
    {
        var settings = new ReEngineSettings
        {
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["customKey"] = JsonSerializer.SerializeToElement("keep")
            }
        };

        Assert.Equal("lightweight", ReEngineAppStatusHelper.GetInstallProfile(settings));
        ReEngineAppStatusHelper.SetInstallProfile(settings, "Full");
        Assert.Equal("full", ReEngineAppStatusHelper.GetInstallProfile(settings));
        Assert.Equal("keep", settings.ExtensionData["customKey"].GetString());
        ReEngineAppStatusHelper.SetInstallProfile(settings, "lightweight");
        Assert.Equal("lightweight", ReEngineAppStatusHelper.GetInstallProfile(settings));

        string dataDir = Path.Combine(_tempRoot, "app_data");
        AotCacheStatusSummary emptyCache = ReEngineAppStatusHelper.InspectAotCache(dataDir);
        Assert.Equal(0, emptyCache.AssemblyCount);
        Assert.Equal(0, emptyCache.TotalBytes);

        string cacheEntryDir = Path.Combine(dataDir, "cache", "kings-field", "abc123");
        Directory.CreateDirectory(cacheEntryDir);
        File.WriteAllBytes(Path.Combine(cacheEntryDir, "kings-field.Generated.dll"), new byte[1024]);
        File.WriteAllText(Path.Combine(cacheEntryDir, "ignored.json"), "{}");

        AotCacheStatusSummary populatedCache = ReEngineAppStatusHelper.InspectAotCache(dataDir);
        Assert.Equal(1, populatedCache.AssemblyCount);
        Assert.Equal(1024, populatedCache.TotalBytes);
        Assert.Contains("1 assembly(ies)", populatedCache.FormatSummary(), StringComparison.Ordinal);

        string enginesDir = Path.Combine(_tempRoot, "engines");
        string reStationDir = Path.Combine(enginesDir, "reStation", "v0.2.0");
        Directory.CreateDirectory(reStationDir);
        File.WriteAllBytes(Path.Combine(reStationDir, "reStation.exe"), Array.Empty<byte>());

        DecompDispatchStatusSummary status = ReEngineAppStatusHelper.InspectDecompStatus(enginesDir, settings);
        Assert.False(status.ToolchainAvailable);
        Assert.Equal("DECOMP: ROUTER", status.FormatPillText());
        Assert.Equal(1, status.InstalledEngineCount);
        Assert.Equal("lightweight", status.InstallProfile);
        Assert.Contains("profile=lightweight", status.FormatSummary(), StringComparison.Ordinal);

        // Peer ..\toolchain under suiteInstallDir enables DECOMP: FULL unless unstripped ghidra\GPL\ exists.
        string suiteRoot = Path.Combine(_tempRoot, "suite_status");
        string suiteEnginesDir = Path.Combine(suiteRoot, "engines");
        string suiteToolchainDir = Path.Combine(suiteRoot, "toolchain");
        Directory.CreateDirectory(Path.Combine(suiteEnginesDir, "reStation", "v0.2.0"));
        File.WriteAllBytes(Path.Combine(suiteEnginesDir, "reStation", "v0.2.0", "reStation.exe"), Array.Empty<byte>());
        Directory.CreateDirectory(suiteToolchainDir);
        File.WriteAllText(Path.Combine(suiteToolchainDir, "THIRDPARTY_LICENSES.txt"), "Apache-2.0\n");

        DecompDispatchStatusSummary peerFullStatus = ReEngineAppStatusHelper.InspectDecompStatus(suiteEnginesDir, settings);
        Assert.True(peerFullStatus.ToolchainAvailable);
        Assert.Equal(suiteToolchainDir, peerFullStatus.ToolchainDirectory);
        Assert.Equal("DECOMP: FULL", peerFullStatus.FormatPillText());

        Directory.CreateDirectory(Path.Combine(suiteToolchainDir, "ghidra", "GPL"));
        DecompDispatchStatusSummary gplBlockedStatus = ReEngineAppStatusHelper.InspectDecompStatus(suiteEnginesDir, settings);
        Assert.False(gplBlockedStatus.ToolchainAvailable);
        Assert.Equal("DECOMP: ROUTER", gplBlockedStatus.FormatPillText());
    }
}
