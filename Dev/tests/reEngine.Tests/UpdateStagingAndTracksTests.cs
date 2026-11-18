// @module: reEngine.Tests.UpdateStagingAndTracksTests
// @purpose: xUnit tests for SSRF blocking, .part resume, .staging->.ready promotion, pinned rollback, and SeriesMap engine aliases.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Net, System.Net.Http, System.Net.Http.Headers, System.Security.Cryptography, System.Text, System.Threading.Tasks, Xunit, reEngine.Core
// @exports: UpdateStagingAndTracksTests

using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using Xunit;

namespace reEngine.Tests;

public sealed class UpdateStagingAndTracksTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_upd_tracks_" + Guid.NewGuid().ToString("N"));

    public UpdateStagingAndTracksTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"Temp cleanup skipped: {ex.Message}"); }
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://192.168.1.10/feed.json")]
    [InlineData("http://10.0.0.5/patch.zip")]
    [InlineData("http://172.16.4.2/patch.zip")]
    [InlineData("http://[fe80::1]/feed.json")]
    [InlineData("http://[fd00:ec2::254]/latest")]
    [InlineData("http://127.0.0.1:8080/feed.json")]
    public void UpdatePackageApplier_BlocksSsrfPrivateAndLinkLocalTargets(string badUrl)
    {
        var uri = new Uri(badUrl);
        Assert.Throws<InvalidOperationException>(() => UpdatePackageApplier.AssertSafeRemoteUrl(uri, allowLoopback: false));
    }

    [Fact]
    public async Task UpdatePackageApplier_ResumesPartialRangeDownloadAndPromotesAtomically()
    {
        string payloadZip = Path.Combine(_tempRoot, "payload.zip");
        using (FileStream fs = File.Create(payloadZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            ZipArchiveEntry exe = zip.CreateEntry("reStation.exe");
            using var w = new StreamWriter(exe.Open());
            w.Write("REENGINE_MOCK_PAYLOAD_V030");
        }

        byte[] fullBytes = File.ReadAllBytes(payloadZip);
        string sha = Convert.ToHexString(SHA256.HashData(fullBytes)).ToLowerInvariant();
        int splitOffset = fullBytes.Length / 2;
        string partPath = Path.Combine(_tempRoot, "dl", "pkg.zip.part");
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        File.WriteAllBytes(partPath, fullBytes[..splitOffset]);
        var meta = new UpdatePartMeta("reStation", "0.3.0", "https://cdn.example.test/pkg.zip", sha, fullBytes.Length, splitOffset, "2026-11-18T00:00:00.000Z");
        File.WriteAllText(partPath + ".meta.json", System.Text.Json.JsonSerializer.Serialize(meta));

        long? seenRangeStart = null;
        using var handler = new StubHandler(req =>
        {
            seenRangeStart = req.Headers.Range?.Ranges.FirstOrDefault()?.From;
            long start = seenRangeStart ?? 0;
            byte[] slice = fullBytes[(int)start..];
            var resp = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(slice) };
            resp.Content.Headers.ContentLength = slice.Length;
            if (start > 0) resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, fullBytes.Length - 1, fullBytes.Length);
            return resp;
        });

        string verifiedPart = await UpdatePackageApplier.DownloadResumablePartAsync(new Uri("https://cdn.example.test/pkg.zip"), partPath, meta, handler);
        Assert.Equal(splitOffset, seenRangeStart);
        Assert.Equal(fullBytes.Length, new FileInfo(verifiedPart).Length);

        string enginesRoot = Path.Combine(_tempRoot, "engines");
        string oldDir = Path.Combine(enginesRoot, "reStation", "v0.2.1");
        Directory.CreateDirectory(oldDir);
        File.WriteAllBytes(Path.Combine(oldDir, "reStation.exe"), [1]);

        string finalDir = UpdatePackageApplier.StageAndPromoteArchive(enginesRoot, "reStation", "0.3.0", verifiedPart);
        Assert.True(File.Exists(Path.Combine(finalDir, ".ready")));
        Assert.True(File.Exists(Path.Combine(oldDir, "reStation.exe")));
    }

    [Fact]
    public void EngineVersionResolver_ResolvesSeriesMapAliases_IgnoresStaging_AndHonorsPins()
    {
        string enginesRoot = Path.Combine(_tempRoot, "engines_alias");
        string v021 = Path.Combine(enginesRoot, "reStation", "v0.2.1");
        string v030 = Path.Combine(enginesRoot, "reStation", "v0.3.0");
        string staging = Path.Combine(enginesRoot, "reStation", ".staging-0.3.1");
        string unready031 = Path.Combine(enginesRoot, "reStation", "v0.3.1");
        Directory.CreateDirectory(v021);
        Directory.CreateDirectory(v030);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(unready031);

        File.WriteAllBytes(Path.Combine(v021, "reStation.exe"), [1]);
        File.WriteAllBytes(Path.Combine(v030, "reStation.exe"), [1]);
        File.WriteAllText(Path.Combine(v030, ".ready"), "{}");
        File.WriteAllBytes(Path.Combine(staging, "reStation.exe"), [1]);
        File.WriteAllBytes(Path.Combine(unready031, "reStation.exe"), [1]);

        var codedManifest = new RecompPackageManifest
        {
            EngineId = "reSlate",
            EngineVersion = "0.2.0",
            CompatibleEngineTrack = "0.2.x",
            SupportedEngineTracks = ["0.3.x", "0.2.x"]
        };

        var resolver = new EngineVersionResolver();
        EngineResolutionResult autoResult = resolver.ResolveForManifest(enginesRoot, codedManifest);
        Assert.True(autoResult.Found);
        Assert.Equal(new SemVer(0, 3, 0), autoResult.SelectedEngine!.Version);
        Assert.DoesNotContain(autoResult.DiscoveredCandidates, c => c.Version == new SemVer(0, 3, 1));

        var pinnedSettings = new ReEngineSettings { PinnedEngineVersions = new Dictionary<string, string> { ["reSlate"] = "0.2.1" } };
        EngineResolutionResult pinnedResult = resolver.ResolveForManifest(enginesRoot, codedManifest, pinnedSettings);
        Assert.True(pinnedResult.Found);
        Assert.Equal(new SemVer(0, 2, 1), pinnedResult.SelectedEngine!.Version);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(_fn(request));
    }
}
