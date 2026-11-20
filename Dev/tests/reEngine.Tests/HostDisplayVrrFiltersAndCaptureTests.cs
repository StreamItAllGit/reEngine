// @module: reEngine.Tests.HostDisplayVrrFiltersAndCaptureTests
// @purpose: xUnit tests for VRR frame pacing resets, 21:9/FMV geometry, 8 video filters, 15-bit Bayer dithering, and F12 screenshot capture.
// @depends-on: System, System.IO, Foundation.Core, reEngine.Host, Xunit
// @exports: HostDisplayVrrFiltersAndCaptureTests

using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Rendering;
using Xunit;

namespace reEngine.Tests;

public sealed class HostDisplayVrrFiltersAndCaptureTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_t2_" + Guid.NewGuid().ToString("N"));

    public HostDisplayVrrFiltersAndCaptureTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    [Fact]
    public void HostFramePacingController_TracksEffectiveFpsAndResetsPacerOnCadenceChange()
    {
        Assert.Equal(60.0, HostFramePacingController.ComputeEffectiveTargetFps(60, 15, 3.0, false, true));
        Assert.Equal(180.0, HostFramePacingController.ComputeEffectiveTargetFps(60, 15, 3.0, true, true));
        Assert.Equal(240.0, HostFramePacingController.ComputeEffectiveTargetFps(60, 15, 9.0, true, true));
        Assert.Equal(15.0, HostFramePacingController.ComputeEffectiveTargetFps(60, 15, 3.0, true, false));

        using var pacer = new HighResFramePacer(60);
        var ctrl = new HostFramePacingController();

        _ = ctrl.SyncPacer(pacer, 60, 15, 3.0, false, true);
        long afterFirst = ctrl.CadenceResetCount;
        Assert.Equal(60.0, ctrl.EffectiveTargetFps);

        // Identical inputs do not increment reset count.
        _ = ctrl.SyncPacer(pacer, 60, 15, 3.0, false, true);
        Assert.Equal(afterFirst, ctrl.CadenceResetCount);

        // Fast-forward transition and background transition reset pacer cadence.
        _ = ctrl.SyncPacer(pacer, 60, 15, 3.0, true, true);
        Assert.Equal(afterFirst + 1, ctrl.CadenceResetCount);
        Assert.Equal(180.0, ctrl.EffectiveTargetFps);

        _ = ctrl.SyncPacer(pacer, 60, 15, 3.0, true, false);
        Assert.Equal(afterFirst + 2, ctrl.CadenceResetCount);
        Assert.Equal(15.0, ctrl.EffectiveTargetFps);
    }

    [Fact]
    public void HostViewportGeometry_SupportsHorPlus21By9_CenteredFmv4By3_AndHudAnchoring()
    {
        HostViewportRect wide21By9 = HostViewportGeometry.ComputeDestinationRect(320, 240, 1920, 810, HostAspectRatioMode.HorPlus21By9);
        HostViewportRect fmvCenter = HostViewportGeometry.ComputeDestinationRect(320, 240, 1920, 810, HostAspectRatioMode.HorPlus21By9, null, isFmvActive: true);

        Assert.True(wide21By9.Width > fmvCenter.Width);
        Assert.Equal(1080, fmvCenter.Width);
        Assert.Equal(810, fmvCenter.Height);
        Assert.Equal(420, fmvCenter.X);

        HostViewportRect safeHud = HostViewportGeometry.ComputeHudAnchorRect(wide21By9, 1920, 810, 300, 120, HostHudAnchorMode.SafeFourByThree);
        HostViewportRect edgeHud = HostViewportGeometry.ComputeHudAnchorRect(wide21By9, 1920, 810, 300, 120, HostHudAnchorMode.ScreenEdges);
        Assert.Equal(420, safeHud.X);
        Assert.Equal(0, edgeHud.X);
    }

    [Fact]
    public void HostVideoFilterPipeline_SupportsAll8Presets_CrtApertureAlias_AndBayerDither()
    {
        Assert.True(HostVideoFilterModeCodec.TryParseStrict("crt-aperture", out HostVideoFilterMode alias));
        Assert.Equal(HostVideoFilterMode.CrtLite, alias);
        Assert.True(HostVideoFilterPipeline.IsZeroCopyFastPath(HostVideoFilterMode.Nearest, true, false));
        Assert.False(HostVideoFilterPipeline.IsZeroCopyFastPath(HostVideoFilterMode.Nearest, false, false));
        Assert.False(HostVideoFilterPipeline.IsZeroCopyFastPath(HostVideoFilterMode.Scanlines, true, false));

        const int w = 16;
        const int h = 16;
        byte[] src = BuildGradientRgba(w, h);
        byte[] dither15 = new byte[src.Length];
        HostVideoFilterPipeline.ApplyColorQuantizationAndDither(src, dither15, w, h, isTrueColor24BitEnabled: false, isDitheringEnabled: true);
        Assert.NotEqual(src, dither15);

        var pipeline = new HostVideoFilterPipeline();
        foreach (HostVideoFilterMode mode in Enum.GetValues<HostVideoFilterMode>())
        {
            ReadOnlySpan<byte> output = pipeline.Process(
                src, w, h, w * 2, h * 2, mode,
                isTrueColor24BitEnabled: true, isDitheringEnabled: false, frameIndex: 1,
                out int outW, out int outH);
            Assert.Equal(w * 2, outW);
            Assert.Equal(h * 2, outH);
            Assert.Equal(outW * outH * 4, output.Length);
            Assert.Contains(output.ToArray(), static b => b != 0);
        }
    }

    [Fact]
    public async Task PresenterReadback_ScreenshotService_AndHeadlessRunner_CaptureValidPngs()
    {
        byte[] clean = BuildGradientRgba(64, 48);
        byte[] filtered = BuildGradientRgba(128, 96);

        using var presenter = new ParallelCpuPresenter(128, 96, 1);
        presenter.PresentRgba(64, 48, clean);
        presenter.FlushAndSyncForSnapshot();
        byte[] rect = new byte[16 * 16 * 4];
        Assert.True(presenter.TryReadSurfaceRect(32, 24, 16, 16, rect));
        Assert.Contains(rect, static b => b != 0);

        string shotDir = Path.Combine(_tempRoot, "screenshots");
        var svc = new HostScreenshotService();
        HostScreenshotResult cleanRes = svc.Capture(HostScreenshotCaptureMode.CleanNative, clean, 64, 48, filtered, 128, 96, shotDir, _tempRoot, "test-mod");
        HostScreenshotResult filtRes = svc.Capture(HostScreenshotCaptureMode.FilteredComposite, clean, 64, 48, filtered, 128, 96, shotDir, _tempRoot, "test-mod");

        Assert.True(cleanRes.Succeeded && File.Exists(cleanRes.FilePath));
        Assert.Equal(64, cleanRes.Width);
        Assert.True(filtRes.Succeeded && File.Exists(filtRes.FilePath));
        Assert.Equal(128, filtRes.Width);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(filtRes.FilePath!)[..4]);

        string dumpPath = Path.Combine(_tempRoot, "headless_dump.png");
        var runner = new HostSessionRunner();
        HostSessionResult res = await runner.RunAsync(new HostSessionOptions
        {
            PreferredBackend = HostRenderBackendKind.ParallelCpu,
            HeadlessFrameCount = 2,
            VideoFilterMode = HostVideoFilterMode.CrtLite,
            ScreenshotCaptureMode = HostScreenshotCaptureMode.FilteredComposite,
            DumpFramePath = dumpPath,
            AllowedSnapshotRoot = _tempRoot,
            ExecutionResilience = new HostExecutionResilienceOptions { DataDirectory = Path.Combine(_tempRoot, "run_data"), ModuleId = "unit-mod" }
        }, new NullHostCallbacks());

        Assert.Equal(0, res.ExitCode);
        Assert.True(File.Exists(dumpPath));
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], File.ReadAllBytes(dumpPath)[..4]);
    }

    private static byte[] BuildGradientRgba(int w, int h)
    {
        byte[] buf = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = ((y * w) + x) * 4;
                buf[i] = (byte)((x * 255) / Math.Max(1, w - 1));
                buf[i + 1] = (byte)((y * 255) / Math.Max(1, h - 1));
                buf[i + 2] = 160;
                buf[i + 3] = 255;
            }
        return buf;
    }

    private sealed class NullHostCallbacks : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
        {
            for (int i = 0; i < rgbaFramebuffer.Length; i += 4)
            { rgbaFramebuffer[i] = 48; rgbaFramebuffer[i + 1] = 180; rgbaFramebuffer[i + 2] = 240; rgbaFramebuffer[i + 3] = 255; }
            return ValueTask.CompletedTask;
        }
        public int ReadAudioSamples(Span<short> pcmInterleaved) => 0;
    }
}
