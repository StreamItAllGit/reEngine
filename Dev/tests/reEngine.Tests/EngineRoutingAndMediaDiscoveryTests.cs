// @module: reEngine.Tests.EngineRoutingAndMediaDiscoveryTests
// @purpose: Isolated v0.5.0 tests for 3-way EngineAliasCatalog, hostProtocolVersion routing & .ready gates, .fds parity, and Launcher --aspect-mode.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, Xunit, reEngine.App, reEngine.Core, reEngine.Launcher
// @exports: EngineRoutingAndMediaDiscoveryTests

using Foundation.Core;
using reEngine.App;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Launcher;
using Xunit;

namespace reEngine.Tests;

public sealed class EngineRoutingAndMediaDiscoveryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_route_v050_" + Guid.NewGuid().ToString("N"));

    public EngineRoutingAndMediaDiscoveryTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch { /* ignore cleanup races */ }
    }

    [Fact]
    public void EngineAliasCatalog_ResolvesThreeWayEquivalence_ForReCart()
    {
        IReadOnlyList<string> eq = EngineAliasCatalog.GetEquivalentIds("reCart");
        Assert.Contains("reCart", eq, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reCartOne", eq, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("reNes", eq, StringComparer.OrdinalIgnoreCase);
        Assert.True(EngineAliasCatalog.AreEquivalent("reCart", "reNes"));
        Assert.True(EngineAliasCatalog.AreEquivalent("reCartOne", "reCart"));
        Assert.True(EngineAliasCatalog.AreEquivalent("reSlate", "reStation"));
    }

    [Fact]
    public void ResolverAndPlanBuilder_HonorHostProtocolVersion_ForSub030Engines()
    {
        string enginesDir = Path.Combine(_tempRoot, "engines");
        string verDir = Path.Combine(enginesDir, "reCart", "v0.1.0");
        Directory.CreateDirectory(verDir);
        File.WriteAllBytes(Path.Combine(verDir, "reCart.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(verDir, "engine.manifest.json"), """
            {
              "schemaVersion": 2,
              "engineId": "reCart",
              "legacyEngineIds": ["reCartOne", "reNes"],
              "version": "0.1.0",
              "hostProtocolVersion": "0.5.0",
              "minSuiteVersion": "0.5.0",
              "supportedEngineTracks": ["0.1.x"]
            }
            """);

        var resolver = new EngineVersionResolver();
        Assert.Empty(resolver.DiscoverInstalledEngines(enginesDir, "reNes")); // requires .ready because hostProtocolVersion >= 0.3.0

        File.WriteAllText(Path.Combine(verDir, ".ready"), "{}");
        IReadOnlyList<InstalledEngineCandidate> ready = resolver.DiscoverInstalledEngines(enginesDir, "reNes");
        Assert.Single(ready);

        string modDir = Path.Combine(_tempRoot, "mod");
        Directory.CreateDirectory(modDir);
        string manifestPath = Path.Combine(modDir, "module.recomp.json");
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "moduleId": "ironframe-outerfortress-cart-01",
              "title": "IronFrame OuterFortress",
              "version": "0.1.0",
              "engineId": "reCart",
              "engineVersion": "0.1.0",
              "compatibleEngineTrack": "0.1.x"
            }
            """);

        RecompPackageLoadResult loaded = RecompPackageReader.Load(manifestPath);
        var appEnv = new AppEnvironment("reengine", ["--data-dir", Path.Combine(_tempRoot, "data")], defaultConfigFileName: "settings.json");
        var opts = RecompLaunchOptions.FromArgs(["--backend", "vulkan", "--fullscreen", "--audio-driver", "wasapi", "--aspect-mode", "fourByThree"], appEnv);
        ChildLaunchPlan plan = RecompLaunchPlanBuilder.Build(loaded, appEnv, _tempRoot, opts, ready[0]);

        Assert.Contains("--backend", plan.Arguments);
        Assert.Contains("vulkan", plan.Arguments);
        Assert.Contains("--fullscreen", plan.Arguments);
        Assert.Contains("--audio-driver", plan.Arguments);
        Assert.Contains("--aspect-mode", plan.Arguments);
        Assert.Contains("fourByThree", plan.Arguments);
    }

    [Fact]
    public void FdsExtension_IsDiscoveredAndPrioritized_WhileRejectedFromCleanRoomOutputs()
    {
        string modDir = Path.Combine(_tempRoot, "fdsmod");
        string romDir = Path.Combine(modDir, "data", "rom");
        Directory.CreateDirectory(romDir);
        File.WriteAllBytes(Path.Combine(romDir, "disk.fds"), [0x46, 0x44, 0x53]);
        string manifestPath = Path.Combine(modDir, "module.recomp.json");
        File.WriteAllText(manifestPath, """
            {
              "schemaVersion": 1,
              "moduleId": "fds-sample",
              "title": "FDS Sample",
              "version": "0.1.0",
              "engineId": "reCart",
              "engineVersion": "0.1.0",
              "compatibleEngineTrack": "0.1.x"
            }
            """);

        RecompPackageLoadResult loaded = RecompPackageReader.Load(manifestPath);
        Assert.EndsWith("disk.fds", loaded.PreloadMedia.PrimaryMediaPath, StringComparison.OrdinalIgnoreCase);

        SuiteMediaDiscoveryResult walked = SuiteMediaTreeWalker.Discover(modDir, _tempRoot, "fds-sample", containmentRoot: _tempRoot);
        Assert.True(walked.Found);
        Assert.EndsWith("disk.fds", walked.PrimaryMediaPath, StringComparison.OrdinalIgnoreCase);

        string outDir = Path.Combine(_tempRoot, "decomp-out");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "leaked.fds"), [0x01]);
        DecompCleanRoomValidationResult check = DecompPipelineCoordinator.ValidateCleanRoomOutputDirectory(outDir);
        Assert.False(check.IsValid);
    }

    [Fact]
    public void LauncherArgsHelper_PreservesAspectModeValueVerbatim_AndInstallerBlocksReleaseAutomation()
    {
        string[] normalized = LauncherArgsHelper.NormalizePathArguments(["--open", "game.recomp", "--aspect-mode", "fourByThree"], _tempRoot);
        int idx = Array.IndexOf(normalized, "--aspect-mode");
        Assert.True(idx >= 0);
        Assert.Equal("fourByThree", normalized[idx + 1]);

        Assert.True(reEngine.Installer.InstallerCliAndAutomation.HasAutomationPortFlag(["--automation-port", "5199"]));
#if !REENGINE_ENABLE_AUTOMATION
        Assert.Equal(2, reEngine.Installer.Program.RunMain(["--automation-port", "5199"]));
#endif
    }

    [Fact]
    public void SemVerHotfixPrecedence_RanksHfAboveStable_WhilePreservingReadyGate()
    {
        SemVer[] ordered = [new(0, 3, 0, "alpha.1"), new(0, 3, 0, "beta.1"), new(0, 3, 0, "rc.1"), new(0, 3, 0), new(0, 3, 0, "hf.1"), new(0, 3, 0, "hf.2"), new(0, 3, 0, "hf.3"), new(0, 3, 0, "hf.10")];
        for (int i = 0; i < ordered.Length - 1; i++)
        {
            Assert.True(ordered[i].CompareTo(ordered[i + 1]) < 0, $"Expected {ordered[i]} < {ordered[i + 1]}");
            Assert.True(ordered[i + 1].CompareTo(ordered[i]) > 0, $"Expected {ordered[i + 1]} > {ordered[i]}");
        }

        string enginesDir = Path.Combine(_tempRoot, "hf_engines");
        string v030Dir = Path.Combine(enginesDir, "reSlate", "v0.3.0");
        string v030Hf3Dir = Path.Combine(enginesDir, "reSlate", "v0.3.0-hf.3");
        Directory.CreateDirectory(v030Dir);
        Directory.CreateDirectory(v030Hf3Dir);
        File.WriteAllBytes(Path.Combine(v030Dir, "reSlate.exe"), [0x4D, 0x5A]);
        File.WriteAllBytes(Path.Combine(v030Hf3Dir, "reSlate.exe"), [0x4D, 0x5A]);
        File.WriteAllText(Path.Combine(v030Dir, ".ready"), "{}");

        var resolver = new EngineVersionResolver();
        string manifestPath = Path.Combine(_tempRoot, "hf_mod", "module.recomp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, """
            { "schemaVersion": 1, "moduleId": "sample-psx", "title": "Sample PSX", "version": "0.3.3", "engineId": "reSlate", "engineVersion": "0.3.0", "compatibleEngineTrack": "0.3.x" }
            """);
        RecompPackageManifest manifest = RecompPackageReader.Load(manifestPath).Manifest;

        Assert.Equal("0.3.0", resolver.ResolveForManifest(enginesDir, manifest).SelectedEngine!.Version.ToString());
        File.WriteAllText(Path.Combine(v030Hf3Dir, ".ready"), "{}");
        Assert.Equal("0.3.0-hf.3", resolver.ResolveForManifest(enginesDir, manifest).SelectedEngine!.Version.ToString());
    }

    [Fact]
    public async Task UpdateCoordinator_DetectsHotfixUpdatesAboveStableAndPriorHotfixes()
    {
        string feedPath = Path.Combine(_tempRoot, "hf_feed.json");
        File.WriteAllText(feedPath, """
            { "schemaVersion": 1, "generatedAtUtc": "2026-11-20T00:00:00Z", "engines": [ { "engineId": "reSlate", "version": "0.3.0-hf.3", "downloadUrl": "https://example.invalid/hf3.zip", "sha256Hex": "0000000000000000000000000000000000000000000000000000000000000000", "fileSizeBytes": 128 } ] }
            """);
        var updater = new UpdateCoordinator(Path.Combine(_tempRoot, "upd_data"));
        var settings = new ReEngineSettings { ShouldCheckUpdates = true, ShouldAutoStagePatches = false };
        Assert.True((await updater.CheckAndStageAsync(settings, "reSlate", new SemVer(0, 3, 0), "0.3.x", feedPath)).CompatiblePatchAvailable);
        Assert.True((await updater.CheckAndStageAsync(settings, "reSlate", new SemVer(0, 3, 0, "hf.2"), "0.3.x", feedPath)).CompatiblePatchAvailable);
    }
}
