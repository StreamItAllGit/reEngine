// @module: reEngine.Tests.SyncWaitShutdownAndDecompUiTests
// @purpose: Isolated v0.4.0 tests for LauncherArgsHelper sync/dual-mode verify normalization, LaunchPlanBuilder flags, bounded <= 3s WM_CLOSE flush, and .decomp UI routing.

using System.Diagnostics;
using Foundation.Core;
using reEngine.App;
using reEngine.App.Ui;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Launcher;
using Xunit;

namespace reEngine.Tests;

public sealed class SyncWaitShutdownAndDecompUiTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_wait_ui_test_" + Guid.NewGuid().ToString("N")[..8]);

    public SyncWaitShutdownAndDecompUiTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void LauncherArgsHelper_DetectsSyncFlagsAndNormalizesVerifyModuleSelectively()
    {
        Assert.True(LauncherArgsHelper.ShouldWaitForChild(["--verify-module", "reSlate"]));
        Assert.True(LauncherArgsHelper.ShouldWaitForChild(["--headless"]));
        Assert.True(LauncherArgsHelper.ShouldWaitForChild(["--json"]));

        string cwd = Path.Combine(_tempRoot, "sub");
        Directory.CreateDirectory(cwd);
        string[] normalized = LauncherArgsHelper.NormalizePathArguments(
            ["--verify-module", "rel\\mod.recomp", "--media-path", "disc.cue"], cwd);
        Assert.Equal(Path.Combine(cwd, "rel", "mod.recomp"), normalized[1]);
        Assert.Equal(Path.Combine(cwd, "disc.cue"), normalized[3]);

        string[] inlineNormalized = LauncherArgsHelper.NormalizePathArguments(
            ["--verify-module=game.decomp"], cwd);
        Assert.Equal($"--verify-module={Path.Combine(cwd, "game.decomp")}", inlineNormalized[0]);

        string[] slugPreserved = LauncherArgsHelper.NormalizePathArguments(
            ["--verify-module", "reSlate"], cwd);
        Assert.Equal("reSlate", slugPreserved[1]);
        Assert.False(Program.IsPackageVerifyTarget("reSlate"));
        Assert.True(Program.IsPackageVerifyTarget("rel\\mod.recomp"));
    }

    [Fact]
    public void RecompLaunchPlanBuilder_ForwardsFullscreenSaveDirAudioPreloadFallbackAndExit6()
    {
        string suiteRoot = _tempRoot;
        string dataDir = Path.Combine(suiteRoot, "data");
        string romDir = Path.Combine(suiteRoot, "games", "unit-game", "data", "rom");
        string modDir = Path.Combine(suiteRoot, "modules");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(romDir);
        Directory.CreateDirectory(modDir);

        string isoPath = Path.Combine(romDir, "unit-game.iso");
        File.WriteAllBytes(isoPath, [1, 2, 3]);
        string pkgPath = Path.Combine(modDir, "unit-game.recomp");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "unit-game",
              "title": "Unit Game",
              "version": "0.1.0",
              "engineId": "reStation",
              "engineVersion": "0.3.1",
              "compatibleEngineTrack": "0.3.x"
            }
            """);

        RecompPackageLoadResult loaded = RecompPackageReader.Load(pkgPath);
        var appEnv = new AppEnvironment("reengine", ["--data-dir", dataDir]);
        var engine = new InstalledEngineCandidate("reStation", new SemVer(0, 3, 1), "v0.3.1", Path.Combine(suiteRoot, "engines", "reStation", "v0.3.1"), "reStation.exe");

        string saveDir = Path.Combine(suiteRoot, "saves");
        RecompLaunchOptions opts = RecompLaunchOptions.FromArgs(
            ["--fullscreen", "--save-dir", saveDir, "--backend", "dx12", "--audio-driver", "wasapi", "--shutdown-timeout-ms", "2500"],
            appEnv);
        Assert.Equal(2500, opts.ShutdownTimeoutMs);
        Assert.Null(RecompLaunchOptions.FromArgs([], appEnv).ShutdownTimeoutMs);

        ChildLaunchPlan plan = RecompLaunchPlanBuilder.Build(loaded, appEnv, suiteRoot, opts, engine);
        Assert.Contains("--fullscreen", plan.Arguments);
        Assert.Contains("--save-dir", plan.Arguments);
        Assert.Contains(Path.GetFullPath(saveDir), plan.Arguments);
        Assert.Contains("--backend", plan.Arguments);
        Assert.Contains("directx", plan.Arguments);
        Assert.Contains("--audio-driver", plan.Arguments);
        Assert.Contains("wasapi", plan.Arguments);
        Assert.Contains(Path.GetFullPath(isoPath), plan.Arguments);

        if (OperatingSystem.IsWindows())
        {
            RecompLaunchOptions metalStrict = opts with { BackendId = "metal", RequireBackend = true };
            Assert.Throws<BackendUnavailableException>(() =>
                RecompLaunchPlanBuilder.Build(loaded, appEnv, suiteRoot, metalStrict, engine));
        }
    }

    [Fact]
    public async Task RecompLaunchCoordinator_WaitCancelSendsWmCloseToFlushSaveWithinTimeout()
    {
        string engDir = Path.Combine(_tempRoot, "engines", "reStation", "v0.3.1");
        string dataDir = Path.Combine(_tempRoot, "data");
        string saveDir = Path.Combine(_tempRoot, "saves");
        Directory.CreateDirectory(engDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "config"));
        Directory.CreateDirectory(saveDir);
        File.WriteAllBytes(Path.Combine(engDir, "reStation.exe"), []);
        File.WriteAllText(Path.Combine(engDir, ".ready"), "{}");

        string pkgPath = Path.Combine(_tempRoot, "graceful.recomp");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "graceful-save-test",
              "title": "Graceful Save Test",
              "version": "0.1.0",
              "engineId": "reStation",
              "engineVersion": "0.3.1",
              "compatibleEngineTrack": "0.3.x"
            }
            """);

        string readyFile = Path.Combine(_tempRoot, "ready.flag");
        string flushedSaveFile = Path.Combine(saveDir, "flushed_memcard.sav");
        string psScript = Path.Combine(_tempRoot, "child_window.ps1");
        File.WriteAllText(psScript, $$"""
            Add-Type -AssemblyName System.Windows.Forms
            $f = New-Object System.Windows.Forms.Form
            $f.Width = 120; $f.Height = 80; $f.ShowInTaskbar = $false; $f.Opacity = 0
            $f.Add_Shown({ [IO.File]::WriteAllText('{{readyFile.Replace("'", "''")}}', 'READY') })
            $f.Add_FormClosing({ [IO.File]::WriteAllText('{{flushedSaveFile.Replace("'", "''")}}', 'FLUSHED_OK') })
            [System.Windows.Forms.Application]::Run($f)
            """);

        var appEnv = new AppEnvironment("reengine", ["--data-dir", dataDir, "--config", Path.Combine(dataDir, "config", "settings.json")]);
        var coordinator = new RecompLaunchCoordinator(new UpdateCoordinator(dataDir, appEnv.ConfigFilePath), (_, _) => { })
        {
            StartInfoOverride = (_, _) => new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -File \"{psScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        using var cts = new CancellationTokenSource();
        var opts = new RecompLaunchOptions { ShouldWaitForExit = true, ShutdownTimeoutMs = 3000, SaveDirOverride = saveDir };
        Task<int> runTask = coordinator.LaunchAsync(pkgPath, Path.Combine(_tempRoot, "engines"), new ReEngineSettings { ShouldCheckUpdates = false }, appEnv, _tempRoot, opts, cts.Token);

        for (int i = 0; i < 60 && !File.Exists(readyFile); i++) await Task.Delay(100);
        Assert.True(File.Exists(readyFile), "Test WinForms child window did not reach Shown state.");

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        Assert.True(File.Exists(flushedSaveFile), "WM_CLOSE graceful teardown must flush saves before termination.");
        Assert.Equal("FLUSHED_OK", File.ReadAllText(flushedSaveFile));
    }

    [Fact]
    public void ProgramVerify_InteractiveDecompRouting_AndBringUpShelfPreload()
    {
        var appEnv = new AppEnvironment("reengine", ["--data-dir", _tempRoot]);
        Assert.False(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions { Headless = true }, appEnv, false, false, false));
        Assert.False(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions(), appEnv, true, false, false));
        Assert.False(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions(), appEnv, false, false, isOutputRedirected: true));
        Assert.True(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions(), appEnv, false, false, false));
        Assert.True(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions(), appEnv, true, explicitWindow: true, false));
        Assert.True(Program.ShouldOpenInteractiveDecompUi(new RecompLaunchOptions(), appEnv, true, explicitWindow: true, isOutputRedirected: true));

        Exception? staEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var shelf = new BringUpShelfPanel();
                var manifest = new DecompPackageManifest { ModuleId = "demo-mod", Title = "Demo Mod", EngineId = "reStation", TargetDecompTier = "D4" };
                shelf.SetLoadedDecomp(Path.Combine(_tempRoot, "demo.decomp"), manifest, "disc.cue", "Ready");
                Assert.Equal("D4", shelf.SelectedStage);
                Assert.True(shelf.RunStageButton.Enabled);
                Assert.True(shelf.LocateMediaButton.Enabled);
            }
            catch (Exception ex) { staEx = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (staEx is not null) throw staEx;
    }
}
