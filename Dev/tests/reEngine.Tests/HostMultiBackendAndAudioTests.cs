// @module: reEngine.Tests.HostMultiBackendAndAudioTests
// @purpose: Isolated v0.4.0 tests for 5-backend normalization, --require-backend Exit 6, ParallelCpu SHA-256 parity, hdpacks containment, audio driver pinning, and installer v0.4.0 upgrade preservation.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Rendering;
using reEngine.Installer;
using Xunit;

namespace reEngine.Tests;

public sealed class HostMultiBackendAndAudioTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_host_gpu_test_" + Guid.NewGuid().ToString("N")[..8]);

    public HostMultiBackendAndAudioTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void HostRenderBackendIds_NormalizesDirectXAliasesAndListsAllFiveWindowsBackends()
    {
        Assert.Equal(5, HostRenderBackendIds.AllWindowsBackends.Count);
        Assert.True(HostRenderBackendIds.TryNormalize("dx12", out string dx12));
        Assert.Equal("directx", dx12);
        Assert.True(HostRenderBackendIds.TryNormalize("D3D12", out string d3d12));
        Assert.Equal("directx", d3d12);
        Assert.True(HostRenderBackendIds.TryNormalize("DirectX12", out string directx12));
        Assert.Equal("directx", directx12);
        Assert.True(HostRenderBackendIds.TryNormalize("metal", out string metal));
        Assert.Equal("metal", metal);
        Assert.False(HostRenderBackendIds.TryNormalize("invalid-gpu", out _));
    }

    [Fact]
    public async Task HostBackendProbe_RequireUnavailableMetalReturnsExitCode6()
    {
        IReadOnlyList<HostBackendCapability> probed = HostBackendProbe.ProbeAll();
        Assert.Equal(6, probed.Count);
        Assert.True(probed.First(p => p.Kind == HostRenderBackendKind.ParallelCpu).IsAvailable);
        Assert.True(probed.First(p => p.Kind == HostRenderBackendKind.Software).IsAvailable);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(HostBackendProbe.Probe(HostRenderBackendKind.Metal).IsAvailable);
            var runner = new HostSessionRunner(320, 240);
            HostSessionResult result = await runner.RunAsync(
                new HostSessionOptions
                {
                    PreferredBackend = HostRenderBackendKind.Metal,
                    RequireBackend = true,
                    RequireVisibleWindow = false,
                    HeadlessFrameCount = 1
                },
                new NullCallbacks());
            Assert.Equal(6, result.ExitCode);
            Assert.Contains("metal", result.Summary, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ParallelCpu_OneX_ProducesByteForByteIdenticalSha256ToSoftware()
    {
        byte[] src = new byte[320 * 240 * 4];
        for (int i = 0; i < src.Length; i += 4)
        {
            int px = i / 4;
            src[i] = (byte)(px * 7);
            src[i + 1] = (byte)(px * 13);
            src[i + 2] = (byte)(px * 29);
            src[i + 3] = 255;
        }

        using var sw = new HostRgbaSurfaceBackend(640, 480, 1, HostRenderBackendIds.Software) { UseParallelBandBlit = false };
        using var par = new HostRgbaSurfaceBackend(640, 480, 1, HostRenderBackendIds.ParallelCpu) { UseParallelBandBlit = true };

        sw.BeginFrame(320, 240);
        par.BeginFrame(320, 240);
        sw.PresentRgba(320, 240, src);
        par.PresentRgba(320, 240, src);

        Assert.True(sw.TryCopyRgbaFrame(out int w1, out int h1, out byte[] swBytes));
        Assert.True(par.TryCopyRgbaFrame(out int w2, out int h2, out byte[] parBytes));
        Assert.Equal((w1, h1), (w2, h2));
        Assert.Equal(SHA256.HashData(swBytes), SHA256.HashData(parBytes));
    }

    [Fact]
    public void HdPackResolver_EnforcesContainmentAndResolvesClutRectPng()
    {
        string dataDir = Path.Combine(_tempRoot, "data");
        string packsDir = Path.Combine(dataDir, "hdpacks");
        Directory.CreateDirectory(packsDir);

        string pngPath = Path.Combine(packsDir, "a1b2c3d4_deadbeef.png");
        RgbaPngWriter.WritePng(pngPath, 1, 1, [255, 128, 64, 255]);

        HdPackResolver resolver = HdPackResolver.FromDataDirectory(dataDir, isEnabled: true);
        Assert.True(resolver.TryResolve("A1B2C3D4", "DEADBEEF", out HdTextureOverrideEntry? hit));
        Assert.NotNull(hit);
        Assert.Equal(Path.GetFullPath(pngPath), hit!.FullFilePath);

        string nestedSub = Path.Combine(packsDir, "nested");
        Directory.CreateDirectory(nestedSub);
        string subPng = Path.Combine(nestedSub, "11223344_55667788.png");
        RgbaPngWriter.WritePng(subPng, 1, 1, [10, 20, 30, 255]);
        HdPackResolver nestedRes = HdPackResolver.FromDataDirectory(dataDir, isEnabled: true);
        Assert.True(nestedRes.TryResolve("11223344", "55667788", out _));

        string escapedDir = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(escapedDir);
        Assert.Throws<InvalidOperationException>(() => new HdPackResolver(escapedDir, isEnabled: true, allowedContainmentRoot: dataDir));
    }

    [Fact]
    public void NativeAudioDriverSelector_PinsWasapiOnWindowsAndCapturesWavHeadless()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("wasapi", NativeAudioDriverSelector.SelectBestDriver());
        }
        Assert.Equal("pipewire", NativeAudioDriverSelector.SelectBestDriver("pipewire"));

        string wavPath = Path.Combine(_tempRoot, "audio", "capture.wav");
        using var pump = new Sdl3AudioPump(new HostAudioFormat(), wavPath, openNativeStream: false, audioDriverOverride: "wasapi");
        Assert.Equal("wasapi", pump.SelectedDriver);
        short[] samples = new short[1024];
        samples[0] = 1234;
        int written = pump.EnqueueInterleaved(samples);
        Assert.Equal(1024, written);
        pump.Dispose();
        Assert.True(File.Exists(wavPath));
        Assert.True(new FileInfo(wavPath).Length > 44);
    }

    [Fact]
    public void LegacyMigrationHelper_PreservesV040SettingsAndWritesBak()
    {
        string installDir = Path.Combine(_tempRoot, "install");
        string cfgDir = Path.Combine(installDir, "data", "config");
        Directory.CreateDirectory(cfgDir);
        string settingsPath = Path.Combine(cfgDir, "settings.json");

        var initial = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["installProfile"] = "full",
            ["backendId"] = "directx",
            ["isFullscreen"] = true,
            ["audioDriver"] = "wasapi",
            ["isHdTexturePackEnabled"] = true
        };
        File.WriteAllText(settingsPath, initial.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        LegacyMigrationHelper.PersistInstallProfileSettings(installDir, null, null, null);

        Assert.True(File.Exists(settingsPath + ".bak"));
        JsonObject? reloaded = JsonNode.Parse(File.ReadAllText(settingsPath, Encoding.UTF8)) as JsonObject;
        Assert.NotNull(reloaded);
        Assert.Equal("directx", reloaded!["backendId"]?.GetValue<string>());
        Assert.True(reloaded["isFullscreen"]?.GetValue<bool>());
        Assert.Equal("wasapi", reloaded["audioDriver"]?.GetValue<string>());
        Assert.True(reloaded["isHdTexturePackEnabled"]?.GetValue<bool>());
    }

    private sealed class NullCallbacks : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }
}
