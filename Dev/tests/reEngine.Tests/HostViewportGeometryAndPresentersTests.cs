// @module: reEngine.Tests.HostViewportGeometryAndPresentersTests
// @purpose: Isolated v0.5.0 tests for HostViewportGeometry (5 aspect modes, overscan crop, inverse pointer) and 5-backend presenter aspect/FBO guards.
// @depends-on: System, System.Threading, System.Threading.Tasks, Xunit, reEngine.Host
// @exports: HostViewportGeometryAndPresentersTests

using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Rendering;
using Xunit;

namespace reEngine.Tests;

public sealed class HostViewportGeometryAndPresentersTests
{
    [Fact]
    public void ComputeDestinationRect_HonorsAllFiveModes_AndPreventsStretchOnResizedWindows()
    {
        HostViewportRect natPar = HostViewportGeometry.ComputeDestinationRect(256, 240, 1280, 720, HostAspectRatioMode.NativePar);
        Assert.Equal(720, natPar.Height);
        Assert.True(natPar.Width is >= 870 and <= 885); // 256/240 * 8/7 * 720 ~= 877
        Assert.Equal((1280 - natPar.Width) / 2, natPar.X);

        HostViewportRect fourByThree = HostViewportGeometry.ComputeDestinationRect(256, 240, 800, 800, HostAspectRatioMode.FourByThree);
        Assert.Equal(800, fourByThree.Width);
        Assert.Equal(600, fourByThree.Height);
        Assert.Equal(100, fourByThree.Y);

        HostViewportRect sqInt = HostViewportGeometry.ComputeDestinationRect(256, 240, 800, 800, HostAspectRatioMode.SquareInteger);
        Assert.Equal(768, sqInt.Width);
        Assert.Equal(720, sqInt.Height);

        HostViewportRect sqSmall = HostViewportGeometry.ComputeDestinationRect(256, 240, 200, 150, HostAspectRatioMode.SquareInteger);
        Assert.True(sqSmall.Width <= 200 && sqSmall.Height == 150 && sqSmall.Width > 0);

        HostViewportRect natFit = HostViewportGeometry.ComputeDestinationRect(256, 240, 1000, 500, HostAspectRatioMode.NativeFit);
        Assert.Equal(500, natFit.Height);
        Assert.True(Math.Abs((natFit.Width / (double)natFit.Height) - (256.0 / 240.0)) < 0.02);

        HostViewportRect horPlus = HostViewportGeometry.ComputeDestinationRect(256, 240, 800, 800, HostAspectRatioMode.HorPlus16By9);
        Assert.Equal(800, horPlus.Width);
        Assert.Equal(450, horPlus.Height);
    }

    [Fact]
    public void ComputeSourceCropRect_AndPointerMapping_ClampSafelyAndDetectMargins()
    {
        HostViewportRect crop = HostViewportGeometry.ComputeSourceCropRect(256, 240, new HostOverscanCrop(8, 8, 4, 4));
        Assert.Equal(new HostViewportRect(4, 8, 248, 224), crop);

        HostViewportRect extreme = HostViewportGeometry.ComputeSourceCropRect(64, 48, new HostOverscanCrop(200, 200, 200, 200));
        Assert.True(extreme.Width >= 1 && extreme.Height >= 1);

        var dest = new HostViewportRect(160, 0, 960, 720);
        Assert.False(HostViewportGeometry.TryMapWindowToContentNormalized(40, 360, dest, out float outX, out float outY));
        Assert.Equal(-1f, outX);
        Assert.Equal(-1f, outY);

        Assert.True(HostViewportGeometry.TryMapWindowToContentNormalized(160 + 480, 360, dest, out float midX, out float midY));
        Assert.InRange(midX, 0.49f, 0.51f);
        Assert.InRange(midY, 0.49f, 0.51f);
    }

    [Fact]
    public void SurfaceAndCpuPresenters_PreserveSurface0Margins_WithCropAndAspect()
    {
        using var surface = new HostRgbaSurfaceBackend(128, 96, 1);
        byte[] src = new byte[32 * 24 * 4];
        for (int i = 0; i < src.Length; i += 4) { src[i] = 220; src[i + 1] = 80; src[i + 2] = 40; src[i + 3] = 255; }

        surface.BeginFrame(32, 24);
        var crop = new HostViewportRect(2, 2, 28, 20);
        var dest = new HostViewportRect(32, 24, 56, 40);
        surface.PresentRgba(32, 24, src, dest, crop);

        Assert.True(surface.TryCopyRgbaFrame(out int w, out int h, out byte[] frame));
        Assert.Equal((byte)14, frame[0]);
        Assert.Equal((byte)14, frame[1]);
        Assert.Equal((byte)16, frame[2]);
        int centerIdx = ((44 * w) + 60) * 4;
        Assert.Equal((byte)220, frame[centerIdx]);
        Assert.Equal((byte)80, frame[centerIdx + 1]);
    }

    [Fact]
    public void SurfacePresentRgba_WithPublishSnapshotFalse_PreservesPriorCompositeSnapshotUntilPublish()
    {
        using var surface = new HostRgbaSurfaceBackend(64, 64, 1);
        byte[] overlayPx = new byte[16 * 16 * 4];
        for (int i = 0; i < overlayPx.Length; i += 4) { overlayPx[i] = 10; overlayPx[i + 1] = 200; overlayPx[i + 2] = 30; overlayPx[i + 3] = 255; }
        surface.PresentRgba(16, 16, overlayPx, new HostViewportRect(0, 0, 16, 16), new HostViewportRect(0, 0, 16, 16), publishSnapshot: true);

        byte[] midFramePx = new byte[16 * 16 * 4];
        for (int i = 0; i < midFramePx.Length; i += 4) { midFramePx[i] = 240; midFramePx[i + 1] = 20; midFramePx[i + 2] = 20; midFramePx[i + 3] = 255; }
        surface.PresentRgba(16, 16, midFramePx, new HostViewportRect(0, 0, 16, 16), new HostViewportRect(0, 0, 16, 16), publishSnapshot: false);

        Assert.True(surface.TryCopyRgbaFrame(out _, out _, out byte[] beforePublish));
        Assert.Equal((byte)10, beforePublish[0]);
        surface.PublishSnapshot();
        Assert.True(surface.TryCopyRgbaFrame(out _, out _, out byte[] afterPublish));
        Assert.Equal((byte)240, afterPublish[0]);
    }

    [Fact]
    public void VulkanAndD3d12StagingHelpers_WriteAspectDestRect_AndClearBorders()
    {
        byte[] src = new byte[4 * 4 * 4];
        for (int i = 0; i < src.Length; i += 4) { src[i] = 200; src[i + 1] = 100; src[i + 2] = 50; src[i + 3] = 255; }
        var crop = new HostViewportRect(0, 0, 4, 4);
        var dest = new HostViewportRect(4, 2, 4, 4);

        byte[] vkDst = new byte[12 * 8 * 4];
        VulkanNative.CopyRgbaToStagingSpan(4, 4, src, vkDst, 12, 8, swapRb: true, dest, crop);
        Assert.Equal((byte)16, vkDst[0]);
        Assert.Equal((byte)14, vkDst[1]);
        Assert.Equal((byte)14, vkDst[2]);
        int vkHit = ((2 * 12) + 4) * 4;
        Assert.Equal((byte)50, vkDst[vkHit]);
        Assert.Equal((byte)200, vkDst[vkHit + 2]);

        byte[] dxDst = new byte[12 * 8 * 4];
        D3d12Native.CopyRgbaToUploadSpan(4, 4, src, dxDst, 12, 8, 12 * 4, dest, crop);
        Assert.Equal((byte)14, dxDst[0]);
        int dxHit = ((2 * 12) + 4) * 4;
        Assert.Equal((byte)200, dxDst[dxHit]);
        Assert.Equal((byte)50, dxDst[dxHit + 2]);
    }

    [Fact]
    public void HardwarePresenters_GuardZeroSizedAndUninitializedPresent_Cleanly()
    {
        byte[] px = new byte[16 * 16 * 4];
        using var gl = new Gl33Presenter();
        using var vk = new VulkanPresenter();
        using var dx = new D3d12Presenter();
        using var cpu = new ParallelCpuPresenter(64, 64, 1);

        Assert.False(gl.TryInitialize(IntPtr.Zero, 0, 0, out _));
        Assert.False(vk.TryInitialize(IntPtr.Zero, 0, 0, out _));
        Assert.False(cpu.TryInitialize(IntPtr.Zero, 0, 0, out _));
        gl.PresentRgba(16, 16, px, new HostViewportRect(0, 0, 0, 0), new HostViewportRect(0, 0, 16, 16));
        vk.PresentRgba(16, 16, px, new HostViewportRect(0, 0, 0, 0), new HostViewportRect(0, 0, 16, 16));
        dx.PresentRgba(16, 16, px, new HostViewportRect(0, 0, 0, 0), new HostViewportRect(0, 0, 16, 16));

        Assert.False(gl.TryReadSurfaceRect(0, 0, 4, 4, new byte[64]));
        gl.FlushAndSyncForSnapshot();
    }

    [Fact]
    public async Task GraphicsContextReadbackDefaults_FailClosedOnAndOffThread()
    {
        using var gl = new Gl33Presenter();
        Assert.False(await Task.Run(() => gl.TryReadSurfaceRect(0, 0, 4, 4, new byte[64])));
        using IHostGraphicsContext ctx = new SpyGraphicsContext([]);
        Assert.False(ctx.TryReadSurfaceRect(0, 0, 4, 4, new byte[64]));
    }

    [Fact]
    public void DispatchQuickSave_FlushesGraphicsContextBeforeCallback()
    {
        var trace = new List<string>();
        using var ctx = new SpyGraphicsContext(trace);
        var callbacks = new SpyEventCallbacks(trace);

        HostSessionRunner.DispatchQuickSave(ctx, callbacks);
        Assert.Equal(["FlushAndSyncForSnapshot", "OnQuickSaveRequested"], trace);
    }

    private sealed class SpyGraphicsContext(List<string> trace) : IHostGraphicsContext
    {
        public HostGraphicsContextHandle Handle { get; } = new(HostRenderBackendKind.OpenGl, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        public string BackendId => HostRenderBackendIds.OpenGl;
        public bool IsHardwareAccelerated => true;
        public int OwnerThreadId => Environment.CurrentManagedThreadId;
        public void PresentComposite(int width, int height, ReadOnlySpan<byte> rgba) { }
        public void FlushAndSyncForSnapshot() => trace.Add("FlushAndSyncForSnapshot");
        public void Dispose() { }
    }

    private sealed class SpyEventCallbacks(List<string> trace) : IHostSessionEventCallbacks
    {
        public void OnQuickSaveRequested() => trace.Add("OnQuickSaveRequested");
    }

    [Fact]
    public async Task HostSessionRunner_ReleaseBlocksAutomation_WithExitCode2()
    {
        var runner = new HostSessionRunner();
        var result = await runner.RunAsync(
            new HostSessionOptions { HeadlessFrameCount = 1, RequireVisibleWindow = false, AutomationPort = 5199, AutomationBridgeFactory = null },
            new NoopCallbacks());
#if !REENGINE_ENABLE_AUTOMATION
        Assert.Equal(2, result.ExitCode);
#else
        Assert.True(result.ExitCode is 0 or 2);
#endif
    }

    private sealed class NoopCallbacks : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct) => ValueTask.CompletedTask;
        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }
}
