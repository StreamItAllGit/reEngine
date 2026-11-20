using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Foundation.Core;
using reEngine.App;
using reEngine.Core.Execution;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Launcher;
using Xunit;

namespace reEngine.Tests;

public sealed class EngineResolutionV061AndCrashFallbackTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reengine-v061-t1-{Guid.NewGuid():N}");

    public EngineResolutionV061AndCrashFallbackTests() => Directory.CreateDirectory(_tempRoot);

    [Fact]
    public void EngineCompatibilityEvaluator_EvaluatesExactWildcardAndRangeRules()
    {
        var candidate = new SemVer(0, 5, 1);
        Assert.True(EngineCompatibilityEvaluator.IsVersionIncompatible(candidate, ["0.4.0", ">=0.5.0 <0.5.2"]));
        Assert.False(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 5, 2), [">=0.5.0 <0.5.2"]));
        Assert.True(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 2, 7), ["0.2.x"]));
        Assert.True(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 9, 0), ["0.*"]));
        Assert.True(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 3, 1), ["<=0.3.1"]));
        Assert.False(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 4, 0), [">0.4.0"]));
        Assert.True(EngineCompatibilityEvaluator.IsVersionIncompatible(new SemVer(0, 4, 0), ["!=0.5.0"]));
    }

    [Fact]
    public void EngineVersionResolver_EnforcesR01Section33PinPrecedenceAndLatestRanking()
    {
        string enginesDir = Path.Combine(_tempRoot, "engines");
        SeedEngine(enginesDir, "reStation", "v0.1.0");
        SeedEngine(enginesDir, "reStation", "v0.2.0-alpha.1");
        SeedEngine(enginesDir, "reStation", "v0.2.0");
        SeedEngine(enginesDir, "reStation", "v0.2.0-hf.1");
        string bundledDir = Path.Combine(_tempRoot, "bundled");
        SeedEngine(bundledDir, "reStation", "v0.1.0");

        var manifest = new RecompPackageManifest
        {
            ModuleId = "demo-mod", EngineId = "reSlate", EngineVersion = "0.1.0",
            IncompatibleEngineVersions = ["0.2.0-hf.1"]
        };
        var resolver = new EngineVersionResolver();

        EngineResolutionResult latest = resolver.ResolveForManifest(enginesDir, manifest, new ReEngineSettings(), bundledDir);
        Assert.True(latest.Found);
        Assert.Equal("0.2.0", latest.SelectedEngine!.Version.ToString());

        var modPin = new ReEngineSettings
        {
            PinnedEngineVersions = new(StringComparer.OrdinalIgnoreCase) { ["reStation"] = "0.1.0", ["demo-mod"] = "bundled" }
        };
        EngineResolutionResult modRes = resolver.ResolveForManifest(enginesDir, manifest, modPin, bundledDir);
        Assert.True(modRes.Found);
        Assert.Contains("bundled", modRes.SelectedEngine!.EngineDirectory, StringComparison.OrdinalIgnoreCase);

        var latestPin = new ReEngineSettings
        {
            PinnedEngineVersions = new(StringComparer.OrdinalIgnoreCase) { ["reSlate"] = "0.1.0", ["demo-mod"] = "latest" }
        };
        EngineResolutionResult skipPin = resolver.ResolveForManifest(enginesDir, manifest, latestPin, bundledDir);
        Assert.Equal("0.2.0", skipPin.SelectedEngine!.Version.ToString());

        var hfManifest = new RecompPackageManifest
        {
            ModuleId = "demo-mod", EngineId = "reSlate", EngineVersion = "0.1.0"
        };
        EngineResolutionResult hfRes = resolver.ResolveForManifest(enginesDir, hfManifest, new ReEngineSettings());
        Assert.Equal("0.2.0-hf.1", hfRes.SelectedEngine!.Version.ToString());
    }

    [Fact]
    public void SessionRecoveryLocator_SelectsNewestArtifactAndSkipsIgnoredOrReparseDirs()
    {
        string modData = Path.Combine(_tempRoot, "data", "modules", "demo-mod");
        string saveDir = Path.Combine(_tempRoot, "saves");
        Directory.CreateDirectory(Path.Combine(modData, "config"));
        Directory.CreateDirectory(Path.Combine(modData, "backups"));
        Directory.CreateDirectory(saveDir);

        File.WriteAllText(Path.Combine(modData, "config", "checkpoint.json"), "{}");
        string older = Path.Combine(modData, "backups", "slot0.sav");
        File.WriteAllText(older, "old");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-10));

        string newer = Path.Combine(saveDir, "quicksave_1.state");
        File.WriteAllText(newer, "new");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-1));

        SessionRecoveryCandidate hit = SessionRecoveryLocator.FindClosestRecovery(modData, saveDir, _tempRoot);
        Assert.True(hit.Found);
        Assert.Equal("quickSave", hit.Kind);
        Assert.Equal(Path.GetFullPath(newer), Path.GetFullPath(hit.FilePath!));
    }

    [Fact]
    public async Task RecompLaunchCoordinator_PromptFirstCrashFallbackRelaunchesAndRespectsDecline()
    {
        string enginesDir = Path.Combine(_tempRoot, "engines");
        SeedEngine(enginesDir, "reStation", "v0.1.0");
        SeedEngine(enginesDir, "reStation", "v0.2.0");
        string modPkg = SeedRecompPackage(Path.Combine(_tempRoot, "modroot"), "kf-crash", "reStation", "0.1.0");
        var appEnv = new AppEnvironment("reengine", ["--data-dir", Path.Combine(_tempRoot, "data")]);

        var launchedVersions = new List<string>();
        bool promptSawRecovery = false;
        Directory.CreateDirectory(Path.Combine(_tempRoot, "data", "modules", "kf-crash"));
        File.WriteAllText(Path.Combine(_tempRoot, "data", "modules", "kf-crash", "checkpoint.sav"), "cp");

        var coordAccept = new RecompLaunchCoordinator(new UpdateCoordinator(_tempRoot), (_, _) => { })
        {
            CrashFallbackPrompter = p => { promptSawRecovery = p.RecoveryCandidate.Found; return true; },
            StartInfoOverride = (eng, _) =>
            {
                launchedVersions.Add(eng.Version.ToString());
                return new ProcessStartInfo("cmd.exe", eng.Version.Minor == 2 ? "/c exit /b 42" : "/c exit /b 0") { UseShellExecute = false, CreateNoWindow = true };
            }
        };

        int okExit = await coordAccept.LaunchAsync(modPkg, enginesDir, new ReEngineSettings { ShouldCheckUpdates = false }, appEnv, _tempRoot, new RecompLaunchOptions { ShouldWaitForExit = true, Headless = false }, CancellationToken.None);
        Assert.Equal(0, okExit);
        Assert.True(promptSawRecovery);
        Assert.Equal(["0.2.0", "0.1.0"], launchedVersions);

        var coordDecline = new RecompLaunchCoordinator(new UpdateCoordinator(_tempRoot), (_, _) => { })
        {
            CrashFallbackPrompter = _ => false,
            StartInfoOverride = (_, _) => new ProcessStartInfo("cmd.exe", "/c exit /b 42") { UseShellExecute = false, CreateNoWindow = true }
        };
        int declinedExit = await coordDecline.LaunchAsync(modPkg, enginesDir, new ReEngineSettings { ShouldCheckUpdates = false }, appEnv, _tempRoot, new RecompLaunchOptions { ShouldWaitForExit = true, Headless = false }, CancellationToken.None);
        Assert.Equal(42, declinedExit);
    }

    [Fact]
    public void LauncherArgsHelper_And_FlagRouter_EnforceR08ParityAndJobGuard()
    {
        Assert.Equal(RecompLaunchOptions.ValueOptions.Count, LauncherArgsHelper.ValueOptions.Count);
        foreach (string v in RecompLaunchOptions.ValueOptions) Assert.Contains(v, LauncherArgsHelper.ValueOptions);

        Assert.False(LauncherArgsHelper.ShouldWaitForChild(["--demo-cart"]));
        Assert.True(LauncherArgsHelper.ShouldWaitForChild(["--demo-cart", "--frames", "5"]));
        string[] normed = LauncherArgsHelper.NormalizePathArguments(["--require-backend", "vulkan", "--rom", @"sub\disk.cue", "mod.recomp"], _tempRoot);
        Assert.Equal("vulkan", normed[1]);
        Assert.Equal(Path.Combine(_tempRoot, "sub", "disk.cue"), normed[3]);
        Assert.Equal(Path.Combine(_tempRoot, "mod.recomp"), normed[4]);

        var parsed = RecompLaunchOptions.FromArgs(["--require-backend", "vulkan", "--window", "--open-settings", "--show-fps", "--force-zero-rwx", "--fast-forward", "--overscan", "8", "--fps-mode", "original", "--ui-text-scale", "1.5"], new AppEnvironment("reengine", ["--data-dir", Path.Combine(_tempRoot, "d2")]));
        Assert.True(parsed.RequireBackend && parsed.Window && parsed.OpenSettings && parsed.ShowFps && parsed.ForceZeroRwx && parsed.FastForward == true);
        Assert.Equal(1.5, parsed.UiTextScale);

        var args061 = new List<string>();
        RecompLaunchFlagRouter.AppendRoutedFlags(args061, parsed, new RecompPackageManifest { EngineId = "reStation", EngineVersion = "0.6.1" }, new InstalledEngineCandidate("reStation", new SemVer(0, 6, 1), "v0.6.1", _tempRoot, "e.exe"), null, null);
        Assert.Contains("--open-settings", args061);
        Assert.Contains("--ui-text-scale", args061);
        using var guard = new LauncherJobObjectGuard();
        Assert.False(guard.TryAttachProcess(null!));
    }

    private void SeedEngine(string root, string id, string folder)
    {
        string dir = Path.Combine(root, id, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ".ready"), "ready");
        File.WriteAllText(Path.Combine(dir, $"{id}.exe"), "exe");
    }

    private static string SeedRecompPackage(string modRoot, string moduleId, string engineId, string minVer)
    {
        string appDir = Path.Combine(modRoot, "app");
        Directory.CreateDirectory(appDir);
        string pkgPath = Path.Combine(appDir, $"{moduleId}.recomp");
        using ZipArchive zip = ZipFile.Open(pkgPath, ZipArchiveMode.Create);
        ZipArchiveEntry entry = zip.CreateEntry("module.recomp.json");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, moduleId, title = moduleId, engineId, engineVersion = minVer, compatibleEngineTrack = "0.1.x" }));
        return pkgPath;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }
}
