// @module: reEngine.Tests.UpdateSweepAndTelemetryTests
// @purpose: xUnit contract tests for multi-engine track sweep, offline socket resilience vs fail-closed containment, heartbeat hygiene, and crash recentLogs.
// @depends-on: System, System.Collections.Generic, System.IO, System.Net, System.Net.Http, System.Net.Sockets, System.Security.Cryptography, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Xunit, reEngine.Core
// @exports: UpdateSweepAndTelemetryTests

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Bootstrap;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using Xunit;

namespace reEngine.Tests;

public sealed class UpdateSweepAndTelemetryTests : IDisposable
{
    private readonly string _tempRoot;

    public UpdateSweepAndTelemetryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_sweep_tests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task CheckAllInstalledEnginesAsync_SweepsEveryTrack_AndStagesPatchDespiteNewerBreakingTrack()
    {
        string enginesRoot = Path.Combine(_tempRoot, "engines");
        SeedDummyEngine(enginesRoot, "reStation", "v0.1.0");
        SeedDummyEngine(enginesRoot, "reStation", "v0.2.0");
        SeedDummyEngine(enginesRoot, "re64", "v0.1.0");

        IReadOnlyList<InstalledEngineCandidate> installed = new EngineVersionResolver().DiscoverInstalledEngines(enginesRoot);
        byte[] patchBytes = Encoding.UTF8.GetBytes("reStation-0.1.1-patch");
        string sha256 = Convert.ToHexString(SHA256.HashData(patchBytes)).ToLowerInvariant();
        string feedJson = JsonSerializer.Serialize(new UpdateFeedManifest(
            1,
            "2026-11-14T02:00:00.000Z",
            new[]
            {
                new UpdateFeedEntry("reStation", "0.1.1", "https://updates.example.test/reStation-0.1.1.zip", sha256, patchBytes.Length)
            }));

        using var handler = new RouteStubHandler(url => url switch
        {
            "https://updates.example.test/feed.json" => OkBytes(Encoding.UTF8.GetBytes(feedJson)),
            "https://updates.example.test/reStation-0.1.1.zip" => OkBytes(patchBytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var coordinator = new UpdateCoordinator(Path.Combine(_tempRoot, "data"));
        var settings = new ReEngineSettings { ShouldCheckUpdates = true, ShouldAutoStagePatches = true };

        MultiEngineUpdateSweepResult sweep = await coordinator.CheckAllInstalledEnginesAsync(
            settings,
            installed,
            "https://updates.example.test/feed.json",
            customHandler: handler);

        Assert.True(sweep.SweepExecuted);
        Assert.False(sweep.NetworkUnavailable);
        Assert.Equal(3, sweep.Outcomes.Count);
        Assert.Contains(sweep.Outcomes, o => o.CompatiblePatchStaged && o.StagedPatchPath is not null && File.Exists(o.StagedPatchPath));
    }

    [Fact]
    public async Task CheckAllInstalledEnginesAsync_DegradesGracefullyOnNetworkFault_ButThrowsOnContainmentEscape()
    {
        string isolatedRoot = Path.Combine(_tempRoot, "isolated");
        string dataDir = Path.Combine(isolatedRoot, "data");
        Directory.CreateDirectory(dataDir);

        using var failingHandler = new RouteStubHandler(_ =>
            throw new HttpRequestException("Connection reset", new SocketException((int)SocketError.ConnectionReset)));

        var coordinator = new UpdateCoordinator(dataDir);
        var settings = new ReEngineSettings { ShouldCheckUpdates = true, ShouldAutoStagePatches = true };
        var installed = new List<InstalledEngineCandidate>
        {
            new("reStation", new SemVer(0, 1, 0), "v0.1.0", Path.Combine(dataDir, "engines", "reStation", "v0.1.0"), "reStation.exe")
        };

        MultiEngineUpdateSweepResult offlineResult = await coordinator.CheckAllInstalledEnginesAsync(
            settings,
            installed,
            "http://127.0.0.1:5931/feed.json",
            customHandler: failingHandler);

        Assert.False(offlineResult.SweepExecuted);
        Assert.True(offlineResult.NetworkUnavailable);
        Assert.Empty(offlineResult.Outcomes);
        Assert.Contains("offline/unavailable", offlineResult.SummaryMessage, StringComparison.OrdinalIgnoreCase);

        string outsideFeed = Path.Combine(_tempRoot, "outside_feed.json");
        File.WriteAllText(outsideFeed, "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.CheckAllInstalledEnginesAsync(
                settings,
                installed,
                outsideFeed,
                allowedLocalFeedRoot: isolatedRoot));
    }

    [Fact]
    public void TelemetryFormatterAndCrashBundle_EmitSingleHeartbeatPrefixAndPopulateRecentLogs()
    {
        var lines = new List<string>();
        var hb = new HeartbeatLogger("reEngine UpdateCheck", line => lines.Add(ReEngineTelemetryFormatter.FormatLogLine("HEARTBEAT", line)));
        hb.ReportProgress(1, 2, "Checking tracks", forceEmit: true);

        Assert.Single(lines);
        Assert.Contains($"[v{ReEnginePackagingConstants.Version}]", lines[0], StringComparison.Ordinal);
        Assert.Contains($"[PID:{Environment.ProcessId}/", lines[0], StringComparison.Ordinal);
        Assert.Contains("[HEARTBEAT] reEngine UpdateCheck:", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("[HEARTBEAT] [HEARTBEAT]", lines[0], StringComparison.Ordinal);

        string logDir = Path.Combine(_tempRoot, "logs");
        string? bundlePath = CrashBundleService.TryWriteCrashBundle(
            logDir,
            ReEnginePackagingConstants.AppId,
            ReEnginePackagingConstants.Version,
            new InvalidOperationException("Simulated probe failure"),
            lines);

        Assert.NotNull(bundlePath);
        Assert.True(File.Exists(bundlePath!));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(bundlePath!));
        JsonElement recent = doc.RootElement.GetProperty("recentLogs");
        Assert.Equal(1, recent.GetArrayLength());
        Assert.Equal(lines[0], recent[0].GetString());
    }

    private static void SeedDummyEngine(string enginesRoot, string engineId, string verFolder)
    {
        string dir = Path.Combine(enginesRoot, engineId, verFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, $"{engineId}.exe"), Array.Empty<byte>());
    }

    private static HttpResponseMessage OkBytes(byte[] bytes)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        resp.Content.Headers.ContentLength = bytes.Length;
        return resp;
    }

    private sealed class RouteStubHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _responder;

        public RouteStubHandler(Func<string, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request.RequestUri?.ToString() ?? string.Empty));
    }
}
