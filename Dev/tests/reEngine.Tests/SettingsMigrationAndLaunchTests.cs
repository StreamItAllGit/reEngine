// @module: reEngine.Tests.SettingsMigrationAndLaunchTests
// @purpose: xUnit regression tests for Bug #2 dual-root settings migration, Program.Bootstrap, and RecompLaunchPlanBuilder v0.3 vs v0.2 routing.
// @depends-on: System, System.IO, System.Text.Json, Foundation.Core, Xunit, reEngine.App, reEngine.Core
// @exports: SettingsMigrationAndLaunchTests

using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using Foundation.Core;
using reEngine.App;
using reEngine.App.Ui;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using Xunit;

namespace reEngine.Tests;

public sealed class SettingsMigrationAndLaunchTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_mig_launch_" + Guid.NewGuid().ToString("N"));

    public SettingsMigrationAndLaunchTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"Temp cleanup skipped: {ex.Message}"); }
    }

    [Fact]
    public void SettingsMigrationHelper_MergesDualRootJsonIntoConfigSettingsAndPreservesUnknownKeys()
    {
        string installRoot = Path.Combine(_tempRoot, "reEngineInstall");
        string dataDir = Path.Combine(installRoot, "data");
        string appDataDir = Path.Combine(installRoot, "app", "data");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(appDataDir);

        string oldRootSettings = Path.Combine(dataDir, "settings.json");
        string newerAppSettings = Path.Combine(appDataDir, "settings.json");
        File.WriteAllText(oldRootSettings, """{ "schemaVersion": 1, "preferredResolutionScale": 4, "customRootOnly": "keep1" }""");
        File.SetLastWriteTimeUtc(oldRootSettings, DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(newerAppSettings, """{ "schemaVersion": 1, "backendId": "vulkan", "customAppNew": "keep2" }""");

        string targetConfig = Path.Combine(dataDir, "config", "settings.json");
        SettingsMigrationResult res = SettingsMigrationHelper.MigrateIfNeeded(installRoot, dataDir, targetConfig, allowAppDataProbe: true);

        Assert.True(res.Migrated);
        Assert.True(File.Exists(targetConfig));
        Assert.True(File.Exists(oldRootSettings + ".migrated.bak"));
        Assert.True(File.Exists(newerAppSettings + ".migrated.bak"));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(dataDir, "config_history"), "legacy_*.json"));

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(targetConfig));
        Assert.Equal("vulkan", doc.RootElement.GetProperty("backendId").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("resolutionScale").GetInt32());
        Assert.Equal("keep1", doc.RootElement.GetProperty("customRootOnly").GetString());
        Assert.Equal("keep2", doc.RootElement.GetProperty("customAppNew").GetString());
    }

    [Fact]
    public void RecompLaunchPlanBuilder_ForwardsV030FlagsAndSkipsThemGracefullyForPre030Children()
    {
        string pkgDir = Path.Combine(_tempRoot, "mod");
        Directory.CreateDirectory(pkgDir);
        string pkgPath = Path.Combine(pkgDir, "module.recomp.json");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "crowndepths-forsakenisle",
              "title": "CrownDepths",
              "version": "0.2.0",
              "engineId": "reStation",
              "engineVersion": "0.2.0",
              "compatibleEngineTrack": "0.2.x"
            }
            """);

        RecompPackageLoadResult loaded = RecompPackageReader.Load(pkgPath);
        string dataDir = Path.Combine(_tempRoot, "data");
        string[] rawCli = ["--data-dir", dataDir, "--backend", "vulkan", "--jit-bringup", "--automation-port", "5120"];
        var appEnv = new AppEnvironment("reengine", rawCli);
        RecompLaunchOptions opts = RecompLaunchOptions.FromArgs(rawCli, appEnv);

        var child030 = new InstalledEngineCandidate("reStation", new SemVer(0, 3, 0), "v0.3.0", _tempRoot, "reStation.exe");
        ChildLaunchPlan plan030 = RecompLaunchPlanBuilder.Build(loaded, appEnv, _tempRoot, opts, child030);
        Assert.Contains("--backend", plan030.Arguments);
        Assert.Contains("vulkan", plan030.Arguments);
        Assert.Contains("--jit-bringup", plan030.Arguments);
        Assert.Contains("--automation-port", plan030.Arguments);

        var child028 = new InstalledEngineCandidate("reStation", new SemVer(0, 2, 8), "v0.2.8", _tempRoot, "reStation.exe");
        ChildLaunchPlan plan028Unsupported = RecompLaunchPlanBuilder.Build(loaded, appEnv, _tempRoot, opts, child028);
        Assert.DoesNotContain("--backend", plan028Unsupported.Arguments);
        Assert.DoesNotContain("--jit-bringup", plan028Unsupported.Arguments);
        Assert.DoesNotContain("--automation-port", plan028Unsupported.Arguments);

        string[] supportedCli = ["--data-dir", dataDir, "--backend", "opengl", "--scale", "4", "--jit-bringup"];
        var supportedEnv = new AppEnvironment("reengine", supportedCli);
        RecompLaunchOptions supportedOpts = RecompLaunchOptions.FromArgs(supportedCli, supportedEnv);
        Assert.Equal(4, supportedOpts.ResolutionScale);
        ChildLaunchPlan plan028Supported = RecompLaunchPlanBuilder.Build(loaded, supportedEnv, _tempRoot, supportedOpts, child028);
        Assert.Contains("--backend", plan028Supported.Arguments);
        Assert.Contains("opengl", plan028Supported.Arguments);
        Assert.Contains("--scale", plan028Supported.Arguments);
        Assert.Contains("4", plan028Supported.Arguments);
        Assert.Contains("--jit-bringup", plan028Supported.Arguments);

        Assert.Throws<ArgumentException>(() =>
            RecompLaunchOptions.FromArgs(["--data-dir", dataDir, "--scale", "3"], appEnv));

        RecompLaunchOptions strictOpts = opts with { RequireBackend = true };
        Assert.Throws<InvalidOperationException>(() =>
            RecompLaunchPlanBuilder.Build(loaded, appEnv, _tempRoot, strictOpts, child028));
    }

    [Fact]
    public async Task RecompLaunchCoordinator_DetachedInteractiveLaunch_DoesNotKillDetachedChildOnLauncherExit()
    {
        string pkgDir = Path.Combine(_tempRoot, "detached_mod");
        string engDir = Path.Combine(_tempRoot, "engines", "reStation", "v0.2.8");
        string dataDir = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(pkgDir);
        Directory.CreateDirectory(engDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "config"));

        File.WriteAllBytes(Path.Combine(engDir, "reStation.exe"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(engDir, ".ready"), "{}");

        string pkgPath = Path.Combine(pkgDir, "sample.recomp");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "sample-detached-module",
              "title": "Sample Detached Module",
              "version": "0.2.0",
              "engineId": "reStation",
              "engineVersion": "0.2.8",
              "compatibleEngineTrack": "0.2.x"
            }
            """);

        string pidFile = Path.Combine(_tempRoot, "grandchild.pid");
        string cfgPath = Path.Combine(dataDir, "config", "settings.json");
        var appEnv = new AppEnvironment("reengine", ["--data-dir", dataDir, "--config", cfgPath]);
        var updateCoordinator = new UpdateCoordinator(appEnv.DataDirectory, appEnv.ConfigFilePath);
        var settings = new ReEngineSettings { ShouldCheckUpdates = false };

        var coordinator = new RecompLaunchCoordinator(updateCoordinator, (_, _) => { })
        {
            StartInfoOverride = (_, _) =>
            {
                string escPid = pidFile.Replace("'", "''");
                return new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"$c = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile','-NonInteractive','-WindowStyle','Hidden','-Command','Start-Sleep -Seconds 10' -WindowStyle Hidden -PassThru; [IO.File]::WriteAllText('{escPid}', [string]$c.Id); exit 0\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
        };

        int? detachedPid = null;
        try
        {
            var opts = new RecompLaunchOptions { ShouldWaitForExit = false, BackendId = "opengl", ResolutionScale = 4 };
            int exitCode = await coordinator.LaunchAsync(pkgPath, Path.Combine(_tempRoot, "engines"), settings, appEnv, _tempRoot, opts, CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(pidFile), "Detached launcher stub should have recorded grandchild PID before exiting 0.");
            detachedPid = int.Parse(File.ReadAllText(pidFile).Trim());

            await Task.Delay(150);
            using Process grandchild = Process.GetProcessById(detachedPid.Value);
            Assert.False(grandchild.HasExited, "JobObjectGuard must NOT terminate detached child processes during interactive UI launches.");
        }
        finally
        {
            if (detachedPid is int pid)
            {
                try
                {
                    using Process orphan = Process.GetProcessById(pid);
                    if (!orphan.HasExited) orphan.Kill(entireProcessTree: true);
                }
                catch { /* already exited */ }
            }
        }
    }

    [Fact]
    public void LibraryShelfPanel_ApplySettings_PreservesResolutionScaleBackendAndJitWithoutClobbering()
    {
        Exception? staException = null;
        var t = new Thread(() =>
        {
            try
            {
                var settings = new ReEngineSettings
                {
                    BackendId = "opengl",
                    ResolutionScale = 4
                };
                ReEngineAppStatusHelper.SetJitBringup(settings, true);

                int callbackCount = 0;
                using var panel = new LibraryShelfPanel(
                    onOpenPackage: () => { },
                    onSelectionChanged: (b, s, j) =>
                    {
                        callbackCount++;
                        settings.BackendId = b;
                        settings.ResolutionScale = s;
                        ReEngineAppStatusHelper.SetJitBringup(settings, j);
                    });

                panel.ApplySettings(settings);

                Assert.Equal(0, callbackCount);
                Assert.Equal(4, settings.ResolutionScale);
                Assert.Equal(4, panel.SelectedResolutionScale);
                Assert.Equal("opengl", panel.SelectedBackend);
                Assert.True(panel.JitBringupCheckBox.Checked);
                Assert.Equal(new[] { "opengl", "software" }, panel.BackendCombo.Items.Cast<string>().ToArray());
                Assert.False(panel.HeaderLabel.UseMnemonic);
            }
            catch (Exception ex)
            {
                staException = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        if (staException is not null)
        {
            throw staException;
        }
    }
}
