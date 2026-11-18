// @module: reEngine.Tests.DecompProviderAndD4ValidatorTests
// @purpose: xUnit regression tests for app\reStation.Decomp.dll probing (Bug #1), SeriesMap alias decomp dispatch, and D4 strictness checks.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, Xunit, reEngine.Core
// @exports: DecompProviderAndD4ValidatorTests

using System.IO.Compression;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using Xunit;

namespace reEngine.Tests;

public sealed class DecompProviderAndD4ValidatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_d4_tests_" + Guid.NewGuid().ToString("N"));

    public DecompProviderAndD4ValidatorTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"Temp cleanup skipped: {ex.Message}"); }
    }

    [Fact]
    public void BuildProviderProbeList_PrioritizesExplicitAndDecompAssembliesWithAliases()
    {
        var manifest = new DecompPackageManifest
        {
            EngineId = "reSlate",
            PlatformId = "psx",
            DecompProviderAssembly = "custom/MyProvider.Decomp.dll"
        };
        var engine = new InstalledEngineCandidate("reStation", new SemVer(0, 3, 0), "v0.3.0", _tempRoot, Path.Combine(_tempRoot, "reStation.exe"));

        IReadOnlyList<string> probes = DecompPipelineCoordinator.BuildProviderProbeList(manifest, engine);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempRoot, "custom", "MyProvider.Decomp.dll")), probes[0]);
        Assert.Contains(Path.GetFullPath(Path.Combine(_tempRoot, "app", "reStation.Decomp.dll")), probes);
        Assert.Contains(Path.GetFullPath(Path.Combine(_tempRoot, "reSlate.Decomp.dll")), probes);
    }

    [Fact]
    public void DecompD4StrictnessValidator_AcceptsMeadowValleyLegacyReportAndRejectsSuppressedOrCBody()
    {
        const string meadowValleyJson = """
            {
              "moduleName": "MeadowValley-BackToEarth-SlateOne-04",
              "tier": "D4",
              "compilerErrorCount": 0,
              "compilerWarningCount": 0,
              "compilerFlags": "-Wall -Wextra -Wno-unused-parameter",
              "craftsmanshipTellCount": 0,
              "zeroWarningsVerified": true,
              "zeroDecompilerTellsVerified": true
            }
            """;

        D4StrictnessValidationResult validRes = DecompD4StrictnessValidator.ValidateD4BuildReportJson(meadowValleyJson);
        Assert.True(validRes.IsValid);

        const string suppressedJson = """
            {
              "moduleName": "BadModule",
              "compilerErrorCount": 0,
              "compilerWarningCount": 0,
              "compilerFlags": ["-Wall", "-Wno-everything"],
              "craftsmanshipTellCount": 0,
              "isZeroWarningsVerified": true,
              "isZeroDecompilerTellsVerified": true
            }
            """;

        D4StrictnessValidationResult badFlags = DecompD4StrictnessValidator.ValidateD4BuildReportJson(suppressedJson);
        Assert.False(badFlags.IsValid);
        Assert.Contains(badFlags.Violations, v => v.Contains("-Wno-everything", StringComparison.Ordinal));
    }

    [Fact]
    public void DecompD4StrictnessValidator_AuditsWorkspaceTiersAndRejectsCBodyInSchemasAndDecompZip()
    {
        string workspace = Path.Combine(_tempRoot, "ws");
        foreach (string tier in DecompD4StrictnessValidator.CanonicalCorpusTiers)
        {
            Directory.CreateDirectory(Path.Combine(workspace, "Corpus", "tiers", tier));
        }
        string rewriteDir = Path.Combine(workspace, "Dev", "schemas", "d4_rewrites");
        Directory.CreateDirectory(rewriteDir);
        File.WriteAllText(Path.Combine(rewriteDir, "clean.json"), """{ "symbol": "InitGame", "signature": "void InitGame(void)" }""");

        D4StrictnessValidationResult cleanWs = DecompD4StrictnessValidator.ValidateWorkspace(workspace);
        Assert.True(cleanWs.IsValid);
        Assert.Equal(5, cleanWs.DiscoveredTiers.Count);

        File.WriteAllText(Path.Combine(rewriteDir, "leaked.json"), """{ "symbol": "Leak", "cBody": "void Leak(int x) { return; }" }""");
        D4StrictnessValidationResult dirtyWs = DecompD4StrictnessValidator.ValidateWorkspace(workspace);
        Assert.False(dirtyWs.IsValid);

        string badZip = Path.Combine(_tempRoot, "bad.decomp");
        using (FileStream fs = File.Create(badZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry e = zip.CreateEntry("rewrites/func.c");
            using var w = new StreamWriter(e.Open());
            w.Write("int32_t Func80010000(int x) { return x + 1; }");
        }

        D4StrictnessValidationResult badArchive = DecompD4StrictnessValidator.ValidateDecompArchiveCleanRoom(badZip);
        Assert.False(badArchive.IsValid);
    }

    [Fact]
    public async Task ProviderLoadContext_UnifiesSharedContractsWhenCoreDllAndDepsJsonSitNextToPlugin()
    {
        string enginesRoot = Path.Combine(_tempRoot, "engines_alc");
        string verDir = Path.Combine(enginesRoot, "reStation", "v0.3.0");
        string pluginAppDir = Path.Combine(verDir, "app");
        Directory.CreateDirectory(pluginAppDir);
        File.WriteAllBytes(Path.Combine(verDir, "reStation.exe"), [1]);
        File.WriteAllText(Path.Combine(verDir, ".ready"), "{}");

        string sourceTestsDll = typeof(DecompProviderAndD4ValidatorTests).Assembly.Location;
        string sourceCoreDll = typeof(DecompPipelineCoordinator).Assembly.Location;
        string targetPluginDll = Path.Combine(pluginAppDir, "reSlate.Decomp.dll");
        File.Copy(sourceTestsDll, targetPluginDll, overwrite: true);
        File.Copy(sourceCoreDll, Path.Combine(pluginAppDir, "reEngine.Core.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(pluginAppDir, "reSlate.Decomp.deps.json"), """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0", "signature": "" },
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "reSlate.Decomp/1.0.0": {
                    "dependencies": { "reEngine.Core": "0.3.0" },
                    "runtime": { "reSlate.Decomp.dll": {} }
                  },
                  "reEngine.Core/0.3.0": {
                    "runtime": { "reEngine.Core.dll": {} }
                  }
                }
              },
              "libraries": {
                "reSlate.Decomp/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                "reEngine.Core/0.3.0": { "type": "project", "serviceable": false, "sha512": "" }
              }
            }
            """);

        string pkgPath = Path.Combine(_tempRoot, "MeadowValley-BackToEarth-SlateOne-04.decomp");
        File.WriteAllText(pkgPath, """
            {
              "moduleId": "meadowvalley-backtoearth",
              "title": "MeadowValley Test",
              "platformId": "psx-dynamic-alc",
              "engineId": "reSlate",
              "engineVersion": "0.3.0",
              "compatibleEngineTrack": "0.3.x",
              "decompSchemaVersion": "1.0.0",
              "targetDecompTier": "D4",
              "goalDecompTier": "D4"
            }
            """);

        string outDir = Path.Combine(_tempRoot, "alc_out");
        string dataDir = Path.Combine(_tempRoot, "alc_data");
        var coordinator = new DecompPipelineCoordinator();
        DecompDispatchResult res = await coordinator.DispatchAsync(
            new DecompRunOptions(pkgPath, outDir, dataDir, DecompStage.D4),
            enginesRoot);

        Assert.True(res.Success, res.DiagnosticMessage);
        Assert.NotNull(res.StageResult);
        Assert.True(res.StageResult!.Success);
        Assert.Contains("DynamicTestDecompProvider", res.DiagnosticMessage, StringComparison.Ordinal);
    }

    public sealed class DynamicTestDecompProvider : IDecompPlatformProvider
    {
        public string PlatformId => "psx-dynamic-alc";
        public string DisplayName => "Dynamic ALC Verification Provider";
        public string MinimumEngineVersion => "0.3.0";

        public Task<DecompStageResult> RunDecompAsync(DecompRunOptions options, CancellationToken ct = default)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            string marker = Path.Combine(options.OutputDirectory, "dynamic-alc-ok.json");
            File.WriteAllText(marker, """{ "status": "ok" }""");
            return Task.FromResult(new DecompStageResult(
                options.TargetStage,
                true,
                "DynamicTestDecompProvider executed cleanly via unified ALC.",
                options.OutputDirectory,
                [marker],
                TimeSpan.FromMilliseconds(1)));
        }

        public DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory) =>
            new(true, Array.Empty<string>(), "Clean-room dynamic ALC output verified.");
    }
}
