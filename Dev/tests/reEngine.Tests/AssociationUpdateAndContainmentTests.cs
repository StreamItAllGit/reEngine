// @module: reEngine.Tests.AssociationUpdateAndContainmentTests
// @purpose: xUnit contract tests for virtualized associations, track-safe updates, settings round-trip, AllowAutoStart = false, and fail-closed containment.
// @depends-on: System, System.Collections.Generic, System.IO, System.Net, System.Net.Http, System.Security.Cryptography, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Xunit, reEngine.Core
// @exports: AssociationUpdateAndContainmentTests

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using Xunit;

namespace reEngine.Tests;

public sealed class AssociationUpdateAndContainmentTests : IDisposable
{
    private readonly string _tempRoot;

    public AssociationUpdateAndContainmentTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_assoc_tests_" + Guid.NewGuid().ToString("N"));
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
    public void VirtualRegistryAssociationStore_PersistsToVirtualRegistryJsonWithoutTouchingHostRegistry()
    {
        string dataDir = Path.Combine(_tempRoot, "isolated_data");
        var store = new VirtualRegistryAssociationStore(dataDir, _tempRoot);
        string dummyExe = Path.Combine(_tempRoot, "reEngine.exe");

        FileAssociationRecord written = store.RegisterRecompAssociation(dummyExe);
        FileAssociationRecord? readBack = store.ReadRecompAssociation();

        Assert.True(File.Exists(store.RegistryFilePath));
        Assert.NotNull(readBack);
        Assert.Equal(".recomp", readBack!.Extension);
        Assert.Equal("reEngine.RecompModule", readBack.ProgId);
        Assert.Equal(written.CommandTemplate, readBack.CommandTemplate);
        Assert.True(store.UnregisterRecompAssociation());
        Assert.Null(store.ReadRecompAssociation());
    }

    [Fact]
    public void VirtualRegistryAssociationStore_RegistersReadsAndUnregistersDecompAssociation_CoexistingWithRecomp()
    {
        string dataDir = Path.Combine(_tempRoot, "isolated_decomp_assoc");
        var store = new VirtualRegistryAssociationStore(dataDir, _tempRoot);
        string dummyExe = Path.Combine(_tempRoot, "reEngine.exe");

        FileAssociationRecord recompRec = store.RegisterRecompAssociation(dummyExe);
        FileAssociationRecord decompRec = store.RegisterDecompAssociation(dummyExe);

        FileAssociationRecord? readRecomp = store.ReadRecompAssociation();
        FileAssociationRecord? readDecomp = store.ReadDecompAssociation();

        Assert.NotNull(readRecomp);
        Assert.NotNull(readDecomp);
        Assert.Equal(".decomp", readDecomp!.Extension);
        Assert.Equal("reEngine.DecompModule", readDecomp.ProgId);
        Assert.Equal($"\"{Path.GetFullPath(dummyExe)}\" decomp --module \"%1\"", decompRec.CommandTemplate);
        Assert.Equal(decompRec.CommandTemplate, readDecomp.CommandTemplate);
        Assert.Equal(recompRec.CommandTemplate, readRecomp!.CommandTemplate);

        Assert.True(store.UnregisterDecompAssociation());
        Assert.Null(store.ReadDecompAssociation());
        Assert.NotNull(store.ReadRecompAssociation());
        Assert.True(store.UnregisterRecompAssociation());
        Assert.False(File.Exists(store.RegistryFilePath));
    }

    [Fact]
    public void WindowsRegistryAssociationStore_ThrowsWhenHostMutationsSuppressed()
    {
        Assert.Throws<InvalidOperationException>(() => new WindowsRegistryAssociationStore(suppressHostMutations: true));
    }

    [Fact]
    public async Task UpdateCoordinator_AutoStagesCompatiblePatchWithSha256Verification()
    {
        string dataDir = Path.Combine(_tempRoot, "updates_data");
        byte[] patchBytes = Encoding.UTF8.GetBytes("reStation-v0.1.1-patch-payload");
        string sha256 = Convert.ToHexString(SHA256.HashData(patchBytes)).ToLowerInvariant();
        string feedJson = JsonSerializer.Serialize(new UpdateFeedManifest(
            1,
            "2026-11-13T12:00:00.000Z",
            new[]
            {
                new UpdateFeedEntry("reStation", "0.1.1", "https://updates.example.test/reStation-0.1.1.zip", sha256, patchBytes.Length)
            }));

        using var handler = new StubHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://updates.example.test/feed.json"] = Encoding.UTF8.GetBytes(feedJson),
            ["https://updates.example.test/reStation-0.1.1.zip"] = patchBytes
        });

        var coordinator = new UpdateCoordinator(dataDir);
        var settings = new ReEngineSettings { ShouldCheckUpdates = true, ShouldAutoStagePatches = true };

        UpdateCheckOutcome outcome = await coordinator.CheckAndStageAsync(
            settings,
            "reStation",
            new SemVer(0, 1, 0),
            "0.1.x",
            "https://updates.example.test/feed.json",
            customHandler: handler);

        Assert.True(outcome.Checked);
        Assert.True(outcome.CompatiblePatchAvailable);
        Assert.True(outcome.CompatiblePatchStaged);
        Assert.False(outcome.BreakingUpdateAvailable);
        Assert.NotNull(outcome.StagedPatchPath);
        Assert.True(File.Exists(outcome.StagedPatchPath!));
        Assert.Equal(patchBytes, await File.ReadAllBytesAsync(outcome.StagedPatchPath!));
    }

    [Fact]
    public async Task UpdateCoordinator_AlertsWithoutOverwritingOnBreakingMinorOrMajorUpdate()
    {
        string dataDir = Path.Combine(_tempRoot, "breaking_data");
        string feedJson = JsonSerializer.Serialize(new UpdateFeedManifest(
            1,
            "2026-11-13T12:00:00.000Z",
            new[]
            {
                new UpdateFeedEntry("reStation", "0.2.0", "https://updates.example.test/reStation-0.2.0.zip", new string('a', 64), 1024)
            }));

        using var handler = new StubHttpMessageHandler(new Dictionary<string, byte[]>
        {
            ["https://updates.example.test/feed.json"] = Encoding.UTF8.GetBytes(feedJson)
        });

        var coordinator = new UpdateCoordinator(dataDir);
        var settings = new ReEngineSettings { ShouldCheckUpdates = true, ShouldAutoStagePatches = true };

        UpdateCheckOutcome outcome = await coordinator.CheckAndStageAsync(
            settings,
            "reStation",
            new SemVer(0, 1, 0),
            "0.1.x",
            "https://updates.example.test/feed.json",
            customHandler: handler);

        Assert.True(outcome.Checked);
        Assert.False(outcome.CompatiblePatchAvailable);
        Assert.False(outcome.CompatiblePatchStaged);
        Assert.Null(outcome.StagedPatchPath);
        Assert.True(outcome.BreakingUpdateAvailable);
        Assert.Equal("0.2.0", outcome.BreakingVersion);
        Assert.Contains("0.1.x", outcome.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateCoordinator_RespectsShouldCheckUpdatesFalse()
    {
        string dataDir = Path.Combine(_tempRoot, "disabled_updates");
        var coordinator = new UpdateCoordinator(dataDir);
        var settings = new ReEngineSettings { ShouldCheckUpdates = false, LastUpdateFeedUrl = "https://updates.example.test/feed.json" };

        UpdateCheckOutcome outcome = await coordinator.CheckAndStageAsync(
            settings,
            "reStation",
            new SemVer(0, 1, 0),
            "0.1.x");

        Assert.False(outcome.Checked);
        Assert.False(outcome.CompatiblePatchAvailable);
        Assert.False(outcome.BreakingUpdateAvailable);
    }

    [Fact]
    public void ReEngineSettings_PreservesUnknownExtensionDataAcrossRoundTrip()
    {
        string dataDir = Path.Combine(_tempRoot, "settings_roundtrip");
        Directory.CreateDirectory(dataDir);
        string settingsPath = Path.Combine(dataDir, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "schemaVersion": 1,
              "appId": "reengine",
              "shouldCheckUpdates": false,
              "shouldAutoStagePatches": true,
              "futureExperimentalFlag": "keep-me"
            }
            """);

        var coordinator = new UpdateCoordinator(dataDir, settingsPath);
        ReEngineSettings loaded = coordinator.LoadSettings().Value;
        loaded.ResolutionScale = 4;
        coordinator.SaveSettings(loaded);

        string savedJson = File.ReadAllText(settingsPath);
        Assert.Contains("futureExperimentalFlag", savedJson, StringComparison.Ordinal);
        Assert.Contains("keep-me", savedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("preferredResolutionScale", savedJson, StringComparison.Ordinal);
        ReEngineSettings reloaded = coordinator.LoadSettings().Value;
        Assert.False(reloaded.ShouldCheckUpdates);
        Assert.Equal(4, reloaded.ResolutionScale);
    }

    [Fact]
    public void ReEnginePackagingConstants_EnforcesAllowAutoStartFalse_AndZeroPiiDefaultPath()
    {
        Assert.False(ReEnginePackagingConstants.AllowAutoStart);
        Assert.Equal("0.6.1", ReEnginePackagingConstants.Version);
        Assert.Equal("StreamItAll", ReEnginePackagingConstants.StudioName);
        Assert.Equal("reEngine", ReEnginePackagingConstants.SuiteName);
        Assert.Equal("StreamItAll\\reEngine\\reEngine", ReEnginePackagingConstants.UmbrellaSubpath);
        Assert.Equal("lightweight", ReEnginePackagingConstants.DefaultInstallProfile);
        Assert.Equal("#38BDF8", ReEnginePackagingConstants.BrandAccentHex);

        string prevEnv = Environment.GetEnvironmentVariable("REENGINE_HOME") ?? string.Empty;
        try
        {
            Environment.SetEnvironmentVariable("REENGINE_HOME", null);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData",
                    "Local");
            }

            string expectedSuite = Path.GetFullPath(Path.Combine(localAppData, "Programs", "StreamItAll", "reEngine"));
            string expectedDefault = Path.GetFullPath(Path.Combine(expectedSuite, "reEngine"));
            string resolved = ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(isolatedTest: false);
            string resolvedSuite = ReEngineBootstrapHelper.ResolveDefaultSuiteInstallDir(isolatedTest: false);

            Assert.Equal(expectedDefault, resolved);
            Assert.Equal(expectedSuite, resolvedSuite);
            Assert.Equal(Path.Combine(expectedSuite, "toolchain"), ReEngineBootstrapHelper.ResolveToolchainDir(resolved));
            Assert.Equal(Path.Combine(expectedSuite, "engines"), ReEngineBootstrapHelper.ResolveEnginesRootDir(resolved));

            // Peer suite siblings (..\toolchain, ..\engines) win even if direct subdirectories exist.
            string peerSuite = Path.Combine(_tempRoot, "peer_suite");
            string peerReEngine = Path.Combine(peerSuite, "reEngine");
            string peerToolchain = Path.Combine(peerSuite, "toolchain");
            string peerEngines = Path.Combine(peerSuite, "engines");
            Directory.CreateDirectory(peerToolchain);
            Directory.CreateDirectory(peerEngines);
            Directory.CreateDirectory(Path.Combine(peerReEngine, "toolchain"));
            Directory.CreateDirectory(Path.Combine(peerReEngine, "engines"));
            Assert.Equal(peerToolchain, ReEngineBootstrapHelper.ResolveToolchainDir(peerReEngine));
            Assert.Equal(peerEngines, ReEngineBootstrapHelper.ResolveEnginesRootDir(peerReEngine));

            // Isolated portable installs fall back to .\toolchain and .\engines when peers do not exist.
            string portableRoot = Path.Combine(_tempRoot, "portable_suite");
            string portableReEngine = Path.Combine(portableRoot, "reEngine");
            string portableToolchain = Path.Combine(portableReEngine, "toolchain");
            string portableEngines = Path.Combine(portableReEngine, "engines");
            Directory.CreateDirectory(portableToolchain);
            Directory.CreateDirectory(portableEngines);
            Assert.Equal(portableToolchain, ReEngineBootstrapHelper.ResolveToolchainDir(portableReEngine));
            Assert.Equal(portableEngines, ReEngineBootstrapHelper.ResolveEnginesRootDir(portableReEngine));
        }
        finally
        {
            Environment.SetEnvironmentVariable("REENGINE_HOME", string.IsNullOrEmpty(prevEnv) ? null : prevEnv);
        }
    }

    [Fact]
    public void AppEnvironment_RejectsHostPathTraversalOutsideIsolatedRoot()
    {
        string isolatedDir = Path.Combine(_tempRoot, "isolated");
        Directory.CreateDirectory(isolatedDir);

        string contained = ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(
            explicitDir: Path.Combine(isolatedDir, "reEngine"),
            isolatedDataDir: isolatedDir,
            isolatedTest: true);
        Assert.Equal(Path.Combine(isolatedDir, "reEngine"), contained);

        string escapedTarget = Path.GetFullPath(Path.Combine(_tempRoot, "outside_host"));
        Assert.Throws<InvalidOperationException>(() => AppEnvironment.AssertPathContained(escapedTarget, isolatedDir));
        Assert.Throws<InvalidOperationException>(() => ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(
            explicitDir: escapedTarget,
            isolatedDataDir: isolatedDir,
            isolatedTest: true));
        Assert.Throws<InvalidOperationException>(() => ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir(
            isolatedDataDir: null,
            isolatedTest: true));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> _routes;

        public StubHttpMessageHandler(IReadOnlyDictionary<string, byte[]> routes)
        {
            _routes = routes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            if (_routes.TryGetValue(url, out byte[]? body))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body)
                };
                resp.Content.Headers.ContentLength = body.Length;
                return Task.FromResult(resp);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
