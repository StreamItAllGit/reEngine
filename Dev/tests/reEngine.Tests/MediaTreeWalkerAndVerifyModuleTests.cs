// @module: reEngine.Tests.MediaTreeWalkerAndVerifyModuleTests
// @purpose: Isolated v0.4.0 tests for D4_CleanSource canonical validator, SuiteMediaTreeWalker containment/ranking/.cue staging, and PackageVerificationService JSON/exit codes.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using reEngine.Core.Decomp;
using Xunit;

namespace reEngine.Tests;

public sealed class MediaTreeWalkerAndVerifyModuleTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_walker_verify_" + Guid.NewGuid().ToString("N")[..8]);

    public MediaTreeWalkerAndVerifyModuleTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void D4StrictnessValidator_UsesD4CleanSourceCanonicalAndAliasesLegacyWithoutDoubleCount()
    {
        Assert.Equal(5, DecompD4StrictnessValidator.CanonicalCorpusTiers.Length);
        Assert.Contains("D4_CleanSource", DecompD4StrictnessValidator.CanonicalCorpusTiers);
        Assert.DoesNotContain("D4_FunctionalRewrite", DecompD4StrictnessValidator.CanonicalCorpusTiers);
        Assert.Equal("D4_CleanSource", DecompD4StrictnessValidator.LegacyCorpusTierAliases["D4_FunctionalRewrite"]);

        string ws = Path.Combine(_tempRoot, "ws");
        foreach (string tier in new[] { "D1_Raw", "D2_Structured", "D3_Mapped", "D4_CleanSource", "D4_FunctionalRewrite", "D5_ByteExact" })
        {
            Directory.CreateDirectory(Path.Combine(ws, "Corpus", "tiers", tier));
        }

        D4StrictnessValidationResult res = DecompD4StrictnessValidator.ValidateWorkspace(ws);
        Assert.True(res.IsValid);
        Assert.Equal(5, res.DiscoveredTiers.Count);
        Assert.Single(res.DiscoveredTiers, t => string.Equals(t, "D4_CleanSource", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuiteMediaTreeWalker_RanksShaSlugAndFlagsMultiGameAmbiguity()
    {
        string suiteRoot = Path.Combine(_tempRoot, "suite");
        string modRoot = Path.Combine(_tempRoot, "downloads", "slug-game");
        string gameA = Path.Combine(suiteRoot, "games", "slug-game", "data", "rom");
        string gameB = Path.Combine(suiteRoot, "games", "other-game", "data", "rom");
        Directory.CreateDirectory(modRoot);
        Directory.CreateDirectory(gameA);
        Directory.CreateDirectory(gameB);

        byte[] romBytes = [0x50, 0x53, 0x2D, 0x58, 0x20, 0x45, 0x58, 0x45, 0x01, 0x02];
        string sha = Convert.ToHexString(SHA256.HashData(romBytes)).ToLowerInvariant();
        string slugIso = Path.Combine(gameA, "game_a.iso");
        string otherIso = Path.Combine(gameB, "game_b.iso");
        File.WriteAllBytes(slugIso, romBytes);
        File.WriteAllBytes(otherIso, [0x00, 0x01, 0x02]);

        SuiteMediaDiscoveryResult matched = SuiteMediaTreeWalker.Discover(modRoot, suiteRoot, "slug-game", sha, containmentRoot: _tempRoot);
        Assert.True(matched.Found);
        Assert.False(matched.IsAmbiguous);
        Assert.Equal(Path.GetFullPath(slugIso), matched.PrimaryMediaPath);

        SuiteMediaDiscoveryResult ambiguous = SuiteMediaTreeWalker.Discover(modRoot, suiteRoot, "unmatched-id", expectedSha256: null, containmentRoot: _tempRoot);
        Assert.False(ambiguous.Found);
        Assert.True(ambiguous.IsAmbiguous);
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    [Fact]
    public async Task SuiteMediaTreeWalker_StagesCueCompanionsAndBlocksTraversal()
    {
        string srcDir = Path.Combine(_tempRoot, "src");
        string romDir = Path.Combine(_tempRoot, "mod", "data", "rom");
        Directory.CreateDirectory(srcDir);

        string cuePath = Path.Combine(srcDir, "disc1.cue");
        string binPath = Path.Combine(srcDir, "disc1.bin");
        File.WriteAllBytes(binPath, [1, 2, 3, 4]);
        File.WriteAllText(cuePath, "FILE \"disc1.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n");

        StagedMediaResult staged = await SuiteMediaTreeWalker.StageMediaIntoRomDirectoryAsync(cuePath, romDir, _tempRoot);
        Assert.Equal(2, staged.StagedFiles.Count);
        Assert.True(File.Exists(Path.Combine(romDir, "disc1.cue")));
        Assert.True(File.Exists(Path.Combine(romDir, "disc1.bin")));

        string evilCue = Path.Combine(srcDir, "evil.cue");
        File.WriteAllText(evilCue, "FILE \"..\\..\\outside.bin\" BINARY\r\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SuiteMediaTreeWalker.StageMediaIntoRomDirectoryAsync(evilCue, romDir, _tempRoot));
    }

    [Fact]
    public void PackageVerificationService_VerifiesRecompDecompStandard18JsonAndZipSlip()
    {
        string enginesDir = Path.Combine(_tempRoot, "engines");
        string engDir = Path.Combine(enginesDir, "reStation", "v0.3.1");
        Directory.CreateDirectory(engDir);
        File.WriteAllBytes(Path.Combine(engDir, "reStation.exe"), []);
        File.WriteAllText(Path.Combine(engDir, ".ready"), "{}");

        string modDir = Path.Combine(_tempRoot, "mods", "alpha");
        Directory.CreateDirectory(modDir);
        string recompPath = Path.Combine(modDir, "alpha.recomp");
        File.WriteAllText(recompPath, """
            {
              "schemaVersion": 1,
              "moduleId": "alpha",
              "title": "Alpha",
              "version": "0.1.0",
              "engineId": "reStation",
              "engineVersion": "0.3.1",
              "compatibleEngineTrack": "0.3.x"
            }
            """);

        var svc = new PackageVerificationService();
        PackageVerificationReport okNoMedia = svc.Verify(recompPath, enginesDir, Path.Combine(_tempRoot, "data"), requireMedia: false, containmentRoot: _tempRoot);
        Assert.True(okNoMedia.IsPassed);
        Assert.Equal(0, okNoMedia.ExitCode);
        Assert.False(okNoMedia.HasMediaReady);

        using JsonDocument doc = JsonDocument.Parse(okNoMedia.ToJson());
        Assert.True(doc.RootElement.TryGetProperty("isPassed", out _));
        Assert.True(doc.RootElement.TryGetProperty("generatedAtUtc", out _));
        Assert.True(doc.RootElement.TryGetProperty("durationMs", out _));
        Assert.True(doc.RootElement.TryGetProperty("exitCode", out _));

        PackageVerificationReport strictMissing = svc.Verify(recompPath, enginesDir, Path.Combine(_tempRoot, "data"), requireMedia: true, containmentRoot: _tempRoot);
        Assert.False(strictMissing.IsPassed);
        Assert.Equal(1, strictMissing.ExitCode);

        string decompMissingEng = Path.Combine(modDir, "beta.decomp");
        File.WriteAllText(decompMissingEng, """
            {
              "schemaVersion": 1,
              "moduleId": "beta",
              "title": "Beta",
              "platformId": "n64",
              "engineId": "re64",
              "engineVersion": "0.1.0",
              "compatibleEngineTrack": "0.1.x",
              "targetDecompTier": "D4"
            }
            """);
        PackageVerificationReport decompFail = svc.Verify(decompMissingEng, enginesDir, containmentRoot: _tempRoot);
        Assert.False(decompFail.IsPassed);
        Assert.Equal(3, decompFail.ExitCode);

        string zipSlipPkg = Path.Combine(modDir, "slip.recomp");
        using (ZipArchive zip = ZipFile.Open(zipSlipPkg, ZipArchiveMode.Create))
        {
            ZipArchiveEntry e = zip.CreateEntry("../../escape.bin");
            using Stream s = e.Open();
            s.WriteByte(1);
        }
        PackageVerificationReport slipRes = svc.Verify(zipSlipPkg, enginesDir, containmentRoot: _tempRoot);
        Assert.False(slipRes.IsPassed);
        Assert.Equal(2, slipRes.ExitCode);

        string outsideDir = Path.Combine(_tempRoot, "escaped_outside", "data", "rom");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllBytes(Path.Combine(outsideDir, "secret.iso"), [9, 9, 9]);
        SuiteMediaDiscoveryResult escapeWalk = SuiteMediaTreeWalker.Discover(modDir, _tempRoot, "../../escaped_outside", null, containmentRoot: null);
        Assert.False(escapeWalk.Candidates.Any(c => c.MatchesModuleSlug), "Traversal moduleId must never match outside games\\.");

        string badPreload = Path.Combine(modDir, "badpreload.decomp");
        File.WriteAllText(badPreload, """
            {
              "schemaVersion": 1,
              "moduleId": "bad-preload",
              "title": "Bad Preload",
              "platformId": "psx",
              "engineId": "reStation",
              "engineVersion": "0.3.1",
              "compatibleEngineTrack": "0.3.x",
              "preloadRomDir": "../../escaped_outside"
            }
            """);
        PackageVerificationReport preloadFail = svc.Verify(badPreload, enginesDir);
        Assert.False(preloadFail.IsPassed);
        Assert.Equal(2, preloadFail.ExitCode);
    }
}
