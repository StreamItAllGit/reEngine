// @module: reEngine.Tests.LauncherAndLaunchPlanTests
// @purpose: xUnit contract tests for standalone LauncherArgsHelper sync-wait/path normalization and RecompLaunchCoordinator isolation/passthrough.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, Xunit, reEngine.App, reEngine.Core, reEngine.Launcher
// @exports: LauncherAndLaunchPlanTests

using Foundation.Core;
using reEngine.App;
using reEngine.Core.Manifests;
using reEngine.Launcher;
using Xunit;

namespace reEngine.Tests;

public sealed class LauncherAndLaunchPlanTests : IDisposable
{
    private readonly string _tempRoot;

    public LauncherAndLaunchPlanTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_launch_tests_" + Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
            // Ignore transient cleanup lock in temp test root.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore transient cleanup race in temp test root.
        }
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--port", "15100", "--ui", "desktop" }, false)]
    [InlineData(new[] { "--port=15100", "--ui=desktop", "--window" }, false)]
    [InlineData(new[] { "--wait" }, true)]
    [InlineData(new[] { "--test-mode", "--open", "sample.recomp" }, true)]
    [InlineData(new[] { "--open=sample.recomp" }, true)]
    [InlineData(new[] { "--check-updates" }, true)]
    [InlineData(new[] { "--register-assoc" }, true)]
    [InlineData(new[] { "--visible-test" }, true)]
    [InlineData(new[] { "--test-mode" }, true)]
    [InlineData(new[] { "--port", "15100", "game.recomp" }, true)]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "--version" }, true)]
    public void LauncherArgsHelper_ShouldWaitForChild_HandlesSyncFlagsAndValueOptions(string[] args, bool expected)
    {
        Assert.Equal(expected, LauncherArgsHelper.ShouldWaitForChild(args));
    }

    [Fact]
    public void LauncherArgsHelper_NormalizePathArguments_ResolvesCallerRelativePathsAndPreservesHttpAndNonPaths()
    {
        string callerCwd = Path.Combine(_tempRoot, "caller");
        Directory.CreateDirectory(callerCwd);

        string[] input =
        [
            "--test-mode",
            "--port", "15100",
            "--ui", "desktop",
            "--frames", "12",
            "--feed", "http://127.0.0.1:15931/feed.json",
            "--open", @".\pkg\module.recomp.json",
            @"--fixture=.\fixtures\room1.json",
            @"local_positional.recomp"
        ];

        string[] normalized = LauncherArgsHelper.NormalizePathArguments(input, callerCwd);

        Assert.Equal("--test-mode", normalized[0]);
        Assert.Equal("15100", normalized[2]);
        Assert.Equal("desktop", normalized[4]);
        Assert.Equal("12", normalized[6]);
        Assert.Equal("http://127.0.0.1:15931/feed.json", normalized[8]);
        Assert.Equal(Path.GetFullPath(Path.Combine(callerCwd, "pkg", "module.recomp.json")), normalized[10]);
        Assert.Equal("--fixture=" + Path.GetFullPath(Path.Combine(callerCwd, "fixtures", "room1.json")), normalized[11]);
        Assert.Equal(Path.GetFullPath(Path.Combine(callerCwd, "local_positional.recomp")), normalized[12]);

        string[] localFeedArgs = ["--feed", @".\feeds\local.json"];
        string[] normalizedLocalFeed = LauncherArgsHelper.NormalizePathArguments(localFeedArgs, callerCwd);
        Assert.Equal(Path.GetFullPath(Path.Combine(callerCwd, "feeds", "local.json")), normalizedLocalFeed[1]);
    }

    [Fact]
    public void RecompLaunchCoordinator_BuildChildLaunchPlan_IsolatesModuleStorageAndForwardsFlags()
    {
        string sandboxRoot = Path.Combine(_tempRoot, "Sandbox");
        string dataDir = Path.Combine(sandboxRoot, "data", "audit_v011");
        string modDir = Path.Combine(dataDir, "mod_kf");
        string romDir = Path.Combine(modDir, "data", "rom");
        Directory.CreateDirectory(romDir);

        string cuePath = Path.Combine(romDir, "kf.cue");
        File.WriteAllText(cuePath, "FILE \"kf.bin\" BINARY");
        string pkgPath = Path.Combine(modDir, "module.recomp.json");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "kings-field-slus-00158",
              "title": "King's Field",
              "version": "0.1.3",
              "engineId": "reStation",
              "engineVersion": "0.1.0",
              "compatibleEngineTrack": "0.1.x"
            }
            """);

        string fixturePath = Path.Combine(dataDir, "sample_fixture.json");
        string screenshotPath = Path.Combine(sandboxRoot, "logs", "shot.png");
        string audioPath = Path.Combine(sandboxRoot, "logs", "audio.wav");
        string scriptPath = Path.Combine(dataDir, "input.txt");
        string customLogDir = Path.Combine(sandboxRoot, "logs", "audit_v011");
        string suiteConfig = Path.Combine(dataDir, "settings.json");

        string[] cliArgs =
        [
            "--test-mode",
            "--open", pkgPath,
            "--wait",
            "--headless",
            "--enable-logs",
            "--frames", "15",
            "--fixture", fixturePath,
            "--dump-screenshot", screenshotPath,
            "--dump-audio", audioPath,
            "--input-script", scriptPath,
            "--data-dir", dataDir,
            "--config", suiteConfig,
            "--log-dir", customLogDir
        ];

        var sandbox = new SandboxEnvironment("reengine", cliArgs);
        RecompLaunchOptions opts = RecompLaunchOptions.FromArgs(cliArgs, sandbox);
        RecompPackageLoadResult loaded = RecompPackageReader.Load(pkgPath);

        ChildLaunchPlan plan = RecompLaunchCoordinator.BuildChildLaunchPlan(loaded, sandbox, sandboxRoot, opts);

        string expectedModData = Path.Combine(dataDir, "modules", "kings-field-slus-00158");
        Assert.Equal(expectedModData, plan.ModuleDataDirectory);
        Assert.Equal(Path.Combine(expectedModData, "settings.json"), plan.ModuleConfigPath);
        Assert.Equal(Path.GetFullPath(customLogDir), plan.ModuleLogDirectory);

        Assert.Contains("--module", plan.Arguments);
        Assert.Contains("--preload-media", plan.Arguments);
        Assert.Contains(Path.GetFullPath(cuePath), plan.Arguments);
        Assert.Contains("--wait", plan.Arguments);
        Assert.Contains("--test-mode", plan.Arguments);
        Assert.Contains("--headless", plan.Arguments);
        Assert.Contains("--enable-logs", plan.Arguments);
        Assert.Contains("--frames", plan.Arguments);
        Assert.Contains("15", plan.Arguments);
        Assert.Contains("--fixture", plan.Arguments);
        Assert.Contains(Path.GetFullPath(fixturePath), plan.Arguments);
        Assert.Contains("--dump-screenshot", plan.Arguments);
        Assert.Contains("--dump-audio", plan.Arguments);
        Assert.Contains("--input-script", plan.Arguments);
    }

    [Fact]
    public void RecompLaunchCoordinator_BuildChildLaunchPlan_RejectsEscapingFixtureAndUnsafeModuleId()
    {
        string sandboxRoot = Path.Combine(_tempRoot, "Sandbox");
        string dataDir = Path.Combine(sandboxRoot, "data");
        Directory.CreateDirectory(dataDir);

        string pkgPath = Path.Combine(dataDir, "module.recomp.json");
        File.WriteAllText(pkgPath, """
            {
              "schemaVersion": 1,
              "moduleId": "valid-module",
              "title": "Valid",
              "version": "0.1.0",
              "engineId": "reStation",
              "engineVersion": "0.1.0",
              "compatibleEngineTrack": "0.1.x"
            }
            """);

        string outsideFixture = Path.Combine(_tempRoot, "outside_host.json");
        string[] escapingArgs = ["--test-mode", "--data-dir", dataDir, "--fixture", outsideFixture];
        var sandbox = new SandboxEnvironment("reengine", escapingArgs);
        RecompLaunchOptions escapingOpts = RecompLaunchOptions.FromArgs(escapingArgs, sandbox);
        RecompPackageLoadResult loaded = RecompPackageReader.Load(pkgPath);

        Assert.Throws<InvalidOperationException>(() =>
            RecompLaunchCoordinator.BuildChildLaunchPlan(loaded, sandbox, sandboxRoot, escapingOpts));

        var badManifest = new RecompPackageManifest
        {
            ModuleId = "../escape",
            EngineId = "reStation",
            EngineVersion = "0.1.0",
            CompatibleEngineTrack = "0.1.x"
        };
        var badLoaded = new RecompPackageLoadResult(badManifest, pkgPath, dataDir, new PreloadRomDiscoveryResult(dataDir, false, Array.Empty<string>(), null));
        Assert.Throws<InvalidOperationException>(() =>
            RecompLaunchCoordinator.BuildChildLaunchPlan(badLoaded, sandbox, sandboxRoot, new RecompLaunchOptions()));
    }
}
