// @module: reEngine.Tests.ManifestAndResolverTests
// @purpose: xUnit contract tests for .recomp JSON/ZIP parsing, Zip-Slip protection, data\rom preload discovery, and side-by-side track resolution.
// @depends-on: System, System.IO, System.IO.Compression, Xunit, reEngine.Core
// @exports: ManifestAndResolverTests

using System.IO.Compression;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using Xunit;

namespace reEngine.Tests;

public sealed class ManifestAndResolverTests : IDisposable
{
    private readonly string _tempRoot;

    public ManifestAndResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_manifest_tests_" + Guid.NewGuid().ToString("N"));
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
        catch { /* ignore temp cleanup races */ }
    }

    [Fact]
    public void ParseRecompJsonManifest_ReadsStandard18CamelCaseFieldsAndDefaults()
    {
        string moduleDir = Path.Combine(_tempRoot, "kings-field");
        Directory.CreateDirectory(moduleDir);
        string manifestPath = Path.Combine(moduleDir, "module.recomp.json");
        File.WriteAllText(manifestPath, """
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

        RecompPackageLoadResult loaded = RecompPackageReader.Load(manifestPath);

        Assert.Equal(1, loaded.Manifest.SchemaVersion);
        Assert.Equal("kings-field-slus-00158", loaded.Manifest.ModuleId);
        Assert.Equal("King's Field", loaded.Manifest.Title);
        Assert.Equal("0.1.3", loaded.Manifest.Version);
        Assert.Equal("reStation", loaded.Manifest.EngineId);
        Assert.Equal("0.1.0", loaded.Manifest.EngineVersion);
        Assert.Equal("0.1.x", loaded.Manifest.CompatibleEngineTrack);
        Assert.Equal("data/rom", loaded.Manifest.PreloadRomDir);
        Assert.False(loaded.PreloadMedia.DirectoryExists);
        Assert.Empty(loaded.PreloadMedia.DiscoveredMediaFiles);
    }

    [Fact]
    public void ParseRecompArchiveManifest_ExtractsManifestWithZipSlipGuard()
    {
        string validPkg = Path.Combine(_tempRoot, "valid.recomp");
        using (FileStream fs = File.Create(validPkg))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = zip.CreateEntry("module.recomp.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""
                {
                  "schemaVersion": 1,
                  "moduleId": "tekken-3",
                  "title": "Tekken 3",
                  "version": "0.1.0",
                  "engineId": "reStation",
                  "engineVersion": "0.1.0",
                  "compatibleEngineTrack": "0.1.x"
                }
                """);
        }

        RecompPackageLoadResult loaded = RecompPackageReader.Load(validPkg);
        Assert.Equal("tekken-3", loaded.Manifest.ModuleId);

        string maliciousPkg = Path.Combine(_tempRoot, "zipslip.recomp");
        using (FileStream fs = File.Create(maliciousPkg))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry badEntry = zip.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(badEntry.Open());
            writer.Write("evil");
        }

        Assert.Throws<InvalidOperationException>(() => RecompPackageReader.Load(maliciousPkg));
    }

    [Fact]
    public void DiscoverPreloadMedia_AutoDetectsFilesInDataRomAndPrioritizesDiscImages()
    {
        string moduleDir = Path.Combine(_tempRoot, "kf-preload");
        string romDir = Path.Combine(moduleDir, "data", "rom");
        Directory.CreateDirectory(romDir);
        string binPath = Path.Combine(romDir, "disc_track1.bin");
        string cuePath = Path.Combine(romDir, "disc.cue");
        File.WriteAllBytes(binPath, new byte[] { 1, 2, 3 });
        File.WriteAllText(cuePath, "FILE \"disc_track1.bin\" BINARY");

        string manifestPath = Path.Combine(moduleDir, "module.recomp.json");
        File.WriteAllText(manifestPath, """
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

        RecompPackageLoadResult loaded = RecompPackageReader.Load(manifestPath);

        Assert.True(loaded.PreloadMedia.DirectoryExists);
        Assert.Equal(2, loaded.PreloadMedia.DiscoveredMediaFiles.Count);
        Assert.Equal(Path.GetFullPath(cuePath), loaded.PreloadMedia.PrimaryMediaPath);
    }

    [Fact]
    public void EngineResolver_SelectsHighestVersionOnMatchingTrack_IgnoresNewerBreakingTrack()
    {
        string enginesDir = SeedEngineVersions("reStation", "v0.1.0", "v0.1.1", "v0.2.0");
        var manifest = new RecompPackageManifest
        {
            EngineId = "reStation",
            EngineVersion = "0.1.0",
            CompatibleEngineTrack = "0.1.x"
        };

        EngineResolutionResult result = new EngineVersionResolver().ResolveForManifest(enginesDir, manifest);

        Assert.True(result.Found);
        Assert.NotNull(result.SelectedEngine);
        Assert.Equal(new SemVer(0, 1, 1), result.SelectedEngine!.Version);
        Assert.Equal(3, result.DiscoveredCandidates.Count);
    }

    [Fact]
    public void EngineResolver_RejectsInstalledVersionBelowMinimumEngineVersion()
    {
        string enginesDir = SeedEngineVersions("reStation", "v0.1.0");
        var manifest = new RecompPackageManifest
        {
            EngineId = "reStation",
            EngineVersion = "0.1.2",
            CompatibleEngineTrack = "0.1.x"
        };

        EngineResolutionResult result = new EngineVersionResolver().ResolveForManifest(enginesDir, manifest);

        Assert.False(result.Found);
        Assert.Null(result.SelectedEngine);
    }

    [Fact]
    public void EngineResolver_SelectsNewerTrackWhenManifestTargetsBreakingTrack()
    {
        string enginesDir = SeedEngineVersions("reStation", "v0.1.0", "v0.1.1", "v0.2.0");
        var manifest = new RecompPackageManifest
        {
            EngineId = "reStation",
            EngineVersion = "0.2.0",
            CompatibleEngineTrack = "0.2.x"
        };

        EngineResolutionResult result = new EngineVersionResolver().ResolveForManifest(enginesDir, manifest);

        Assert.True(result.Found);
        Assert.NotNull(result.SelectedEngine);
        Assert.Equal(new SemVer(0, 2, 0), result.SelectedEngine!.Version);
    }

    private string SeedEngineVersions(string engineId, params string[] versionFolders)
    {
        string enginesRoot = Path.Combine(_tempRoot, "engines_" + Guid.NewGuid().ToString("N"));
        foreach (string ver in versionFolders)
        {
            string verDir = Path.Combine(enginesRoot, engineId, ver);
            Directory.CreateDirectory(verDir);
            File.WriteAllBytes(Path.Combine(verDir, $"{engineId}.exe"), Array.Empty<byte>());
        }
        return enginesRoot;
    }
}
