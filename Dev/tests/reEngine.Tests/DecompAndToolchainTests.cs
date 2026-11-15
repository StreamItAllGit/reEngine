// @module: reEngine.Tests.DecompAndToolchainTests
// @purpose: xUnit contract tests for DecompPackageManifest, DecompPipelineCoordinator 3-part unbuilt-engine diagnostics, clean-room checks, and OfflineToolchainLocator.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Threading, System.Threading.Tasks, Xunit, reEngine.Core
// @exports: DecompAndToolchainTests

using System.IO.Compression;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;
using Xunit;

namespace reEngine.Tests;

public sealed class DecompAndToolchainTests : IDisposable
{
    private readonly string _tempRoot;

    public DecompAndToolchainTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_decomp_tests_" + Guid.NewGuid().ToString("N"));
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
    public void DecompPackageManifest_ParsesSampleRe64FixtureAndStandard18CamelCaseKeys()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-re64.decomp");
        Assert.True(File.Exists(fixturePath), $"Expected fixture at '{fixturePath}'.");

        DecompPackageManifest manifest = DecompPipelineCoordinator.ParseManifest(fixturePath);

        Assert.Equal("sample-n64-decomp", manifest.ModuleId);
        Assert.Equal("Sample Nintendo 64 Clean-Room Decomp Module", manifest.Title);
        Assert.Equal("n64", manifest.PlatformId);
        Assert.Equal("re64", manifest.EngineId);
        Assert.Equal("0.1.0", manifest.EngineVersion);
        Assert.Equal("0.1.x", manifest.CompatibleEngineTrack);
        Assert.Equal("1.0.0", manifest.DecompSchemaVersion);
        Assert.Equal("D3", manifest.TargetDecompTier);
        Assert.Equal("D3", manifest.GoalDecompTier);
        Assert.Equal("0.1.0", manifest.PackageVersion);
        Assert.Equal("data/rom", manifest.PreloadRomDir);
        Assert.NotNull(manifest.ToolchainRequirements);
        Assert.Equal("11.2", manifest.ToolchainRequirements!.MinimumGhidraVersion);
        Assert.True(manifest.ToolchainRequirements.RequiresHeadlessJdk);
        Assert.Empty(manifest.ToolchainRequirements.RequiredExtensions);
    }

    [Fact]
    public void DecompPackageManifest_ParsesZipArchiveAndRejectsZipSlipTraversal()
    {
        string validDecomp = Path.Combine(_tempRoot, "valid.decomp");
        using (FileStream fs = File.Create(validDecomp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("module.decomp.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""
                {
                  "moduleId": "kf-decomp",
                  "title": "King's Field Clean-Room Decomp",
                  "platformId": "psx",
                  "engineId": "reStation",
                  "engineVersion": "0.2.0",
                  "compatibleEngineTrack": "0.2.x",
                  "decompSchemaVersion": "1.0.0",
                  "targetDecompTier": "D3",
                  "goalDecompTier": "D4"
                }
                """);
        }

        DecompPackageManifest parsed = DecompPipelineCoordinator.ParseManifest(validDecomp);
        Assert.Equal("kf-decomp", parsed.ModuleId);
        Assert.Equal("psx", parsed.PlatformId);
        Assert.Equal("reStation", parsed.EngineId);
        Assert.Equal("D4", parsed.GoalDecompTier);

        string zipSlipDecomp = Path.Combine(_tempRoot, "zipslip.decomp");
        using (FileStream fs = File.Create(zipSlipDecomp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("../escape.decomp.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{ "engineId": "reStation" }""");
        }

        Assert.Throws<InvalidOperationException>(() => DecompPipelineCoordinator.ParseManifest(zipSlipDecomp));
    }

    [Fact]
    public async Task DecompPipelineCoordinator_DispatchAsync_EmitsThreePartErrorForSampleRe64UnbuiltEngine()
    {
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-re64.decomp");
        string enginesRoot = Path.Combine(_tempRoot, "engines");
        string psxEngineDir = Path.Combine(enginesRoot, "reStation", "v0.2.0");
        Directory.CreateDirectory(psxEngineDir);
        File.WriteAllBytes(Path.Combine(psxEngineDir, "reStation.exe"), Array.Empty<byte>());

        string outDir = Path.Combine(_tempRoot, "out");
        string dataDir = Path.Combine(_tempRoot, "data");
        var coordinator = new DecompPipelineCoordinator();
        var options = new DecompRunOptions(fixturePath, outDir, dataDir, DecompStage.D3);

        DecompDispatchResult result = await coordinator.DispatchAsync(options, enginesRoot);

        Assert.False(result.Success);
        Assert.Null(result.ResolvedEngine);
        Assert.NotNull(result.UnbuiltEngineError);
        Assert.Equal("re64", result.UnbuiltEngineError!.EngineId);
        Assert.Equal("0.1.x", result.UnbuiltEngineError.CompatibleEngineTrack);
        Assert.Equal("0.1.0", result.UnbuiltEngineError.MinimumEngineVersion);
        Assert.Contains("[1/3 What Failed]", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("[2/3 Missing]", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("[3/3 Remediation]", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("re64", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("0.1.x", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("v0.1.0", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("--engine-dir", result.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecompPipelineCoordinator_DispatchAsync_DelegatesToProviderAndEnforcesCleanRoomOutput()
    {
        string pkgPath = Path.Combine(_tempRoot, "psx-module.decomp");
        File.WriteAllText(pkgPath, """
            {
              "moduleId": "psx-sample",
              "title": "PSX Sample",
              "platformId": "psx",
              "engineId": "reStation",
              "engineVersion": "0.2.0",
              "compatibleEngineTrack": "0.2.x",
              "decompSchemaVersion": "1.0.0",
              "targetDecompTier": "D3",
              "goalDecompTier": "D3"
            }
            """);

        string enginesRoot = Path.Combine(_tempRoot, "engines");
        string psxDir = Path.Combine(enginesRoot, "reStation", "v0.2.0");
        Directory.CreateDirectory(psxDir);
        File.WriteAllBytes(Path.Combine(psxDir, "reStation.exe"), Array.Empty<byte>());

        string cleanOutDir = Path.Combine(_tempRoot, "clean_out");
        string dirtyOutDir = Path.Combine(_tempRoot, "dirty_out");
        string dataDir = Path.Combine(_tempRoot, "data");

        var cleanProvider = new StubDecompProvider("psx", emitForbiddenArtifact: false);
        var cleanCoordinator = new DecompPipelineCoordinator([cleanProvider]);
        DecompDispatchResult cleanResult = await cleanCoordinator.DispatchAsync(
            new DecompRunOptions(pkgPath, cleanOutDir, dataDir, DecompStage.D3),
            enginesRoot);

        Assert.True(cleanResult.Success);
        Assert.NotNull(cleanResult.ResolvedEngine);
        Assert.NotNull(cleanResult.CleanRoomValidation);
        Assert.True(cleanResult.CleanRoomValidation!.IsValid);

        var dirtyProvider = new StubDecompProvider("psx", emitForbiddenArtifact: true);
        var dirtyCoordinator = new DecompPipelineCoordinator([dirtyProvider]);
        DecompDispatchResult dirtyResult = await dirtyCoordinator.DispatchAsync(
            new DecompRunOptions(pkgPath, dirtyOutDir, dataDir, DecompStage.D3),
            enginesRoot);

        Assert.False(dirtyResult.Success);
        Assert.NotNull(dirtyResult.CleanRoomValidation);
        Assert.False(dirtyResult.CleanRoomValidation!.IsValid);
        Assert.Contains(dirtyResult.CleanRoomValidation.Violations, v => v.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dirtyResult.CleanRoomValidation.Violations, v => v.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OfflineToolchainLocator_DistinguishesLightweightProfileAndValidatesFullBundle()
    {
        string lightweightInstall = Path.Combine(_tempRoot, "reEngine_lightweight");
        Directory.CreateDirectory(lightweightInstall);

        OfflineToolchainStatus lightweightStatus = OfflineToolchainLocator.Locate(
            lightweightInstall,
            ReEngineInstallProfile.Lightweight);

        Assert.False(lightweightStatus.IsAvailable);
        Assert.Equal(ReEngineInstallProfile.Lightweight, lightweightStatus.Profile);
        Assert.Contains("--profile Full", lightweightStatus.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("--toolchain-dir", lightweightStatus.DiagnosticMessage, StringComparison.Ordinal);

        string fullInstall = Path.Combine(_tempRoot, "reEngine_full");
        string toolchainDir = Path.Combine(fullInstall, "toolchain");
        string supportDir = Path.Combine(toolchainDir, "ghidra", "support");
        string jdkBinDir = Path.Combine(toolchainDir, "jdk", "bin");
        Directory.CreateDirectory(supportDir);
        Directory.CreateDirectory(jdkBinDir);

        File.WriteAllText(Path.Combine(supportDir, "analyzeHeadless.bat"), "@echo off");
        File.WriteAllBytes(Path.Combine(jdkBinDir, "java.exe"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(toolchainDir, OfflineToolchainLocator.ThirdPartyLicensesFileName), "Apache-2.0 + GPLv2+CE");

        OfflineToolchainStatus validFull = OfflineToolchainLocator.Locate(
            fullInstall,
            ReEngineInstallProfile.Full);

        Assert.True(validFull.IsAvailable);
        Assert.True(validFull.LicensesPresent);
        Assert.True(validFull.GplDirectoryStripped);
        Assert.NotNull(validFull.AnalyzeHeadlessPath);
        Assert.NotNull(validFull.JavaExecutablePath);

        // Peer ..\toolchain sibling resolution when install root is suite\reEngine
        string peerSuite = Path.Combine(_tempRoot, "suite_peer");
        string peerReEngine = Path.Combine(peerSuite, "reEngine");
        string peerToolchain = Path.Combine(peerSuite, "toolchain");
        Directory.CreateDirectory(peerReEngine);
        Directory.CreateDirectory(Path.Combine(peerToolchain, "ghidra", "support"));
        Directory.CreateDirectory(Path.Combine(peerToolchain, "jdk", "bin"));
        File.WriteAllText(Path.Combine(peerToolchain, "ghidra", "support", "analyzeHeadless.bat"), "@echo off");
        File.WriteAllBytes(Path.Combine(peerToolchain, "jdk", "bin", "java.exe"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(peerToolchain, OfflineToolchainLocator.ThirdPartyLicensesFileName), "Apache-2.0");

        OfflineToolchainStatus peerStatus = OfflineToolchainLocator.Locate(peerReEngine, ReEngineInstallProfile.Full);
        Assert.True(peerStatus.IsAvailable);
        Assert.Equal(peerToolchain, peerStatus.ToolchainDirectory);

        string gplLeakDir = Path.Combine(toolchainDir, "ghidra", "GPL");
        Directory.CreateDirectory(gplLeakDir);
        OfflineToolchainStatus gplFailed = OfflineToolchainLocator.Locate(fullInstall, ReEngineInstallProfile.Full);

        Assert.False(gplFailed.IsAvailable);
        Assert.False(gplFailed.GplDirectoryStripped);
        Assert.Contains("GPL", gplFailed.DiagnosticMessage, StringComparison.Ordinal);
    }

    private sealed class StubDecompProvider : IDecompPlatformProvider
    {
        private readonly bool _emitForbiddenArtifact;

        public StubDecompProvider(string platformId, bool emitForbiddenArtifact)
        {
            PlatformId = platformId;
            _emitForbiddenArtifact = emitForbiddenArtifact;
        }

        public string PlatformId { get; }
        public string DisplayName => "Stub PSX Provider";
        public string MinimumEngineVersion => "0.2.0";

        public Task<DecompStageResult> RunDecompAsync(DecompRunOptions options, CancellationToken ct = default)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            string cleanFile = Path.Combine(options.OutputDirectory, "symbols.json");
            File.WriteAllText(cleanFile, """{ "schemaVersion": 1 }""");
            var files = new List<string> { cleanFile };

            if (_emitForbiddenArtifact)
            {
                string genFile = Path.Combine(options.OutputDirectory, "Lifted.g.cs");
                string romFile = Path.Combine(options.OutputDirectory, "retail.bin");
                File.WriteAllText(genFile, "// generated");
                File.WriteAllBytes(romFile, [0x01, 0x02]);
                files.Add(genFile);
                files.Add(romFile);
            }

            return Task.FromResult(new DecompStageResult(
                options.TargetStage,
                true,
                "Stub decomp stage completed.",
                options.OutputDirectory,
                files,
                TimeSpan.FromMilliseconds(5)));
        }

        public DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory) =>
            new(true, Array.Empty<string>(), "Stub clean-room check passed.");
    }
}
