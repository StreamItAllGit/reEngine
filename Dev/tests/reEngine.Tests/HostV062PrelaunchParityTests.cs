// @module: reEngine.Tests.HostV062PrelaunchParityTests
// @purpose: Regression tests for v0.6.2 fixes: F3 HUD invariant width, synthetic tap auto-release, Browse error banners, live AOT/MediaGate state, and HostSettingsStore round-trip persistence.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Updates, reEngine.Host, Xunit
// @exports: HostV062PrelaunchParityTests

using Foundation.Core;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Input;
using reEngine.Host.Settings;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostV062PrelaunchParityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_062_" + Guid.NewGuid().ToString("N"));

    public HostV062PrelaunchParityTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    [Fact]
    public void F3HudOverlayView_MaintainsInvariantPillWidthAcrossDynamicMetricChanges()
    {
        var canvas = new PixelOverlayCanvas(1280, 720);
        var hud = new F3HudOverlayView { IsVisible = true };
        var widths = new HashSet<int>();

        F3HudTelemetry[] samples =
        [
            new(5.2, 192.30, "OpenGl", 2, "AOT Primary", 0.02) { FrameRateModeBadge = "[ENH]" },
            new(59.9, 16.67, "OpenGl", 2, "AOT Primary", 0.48) { FrameRateModeBadge = "[ENH]" },
            new(144.0, 6.94, "OpenGl", 2, "AOT Primary", 1.00) { FrameRateModeBadge = "[ORIG]" },
            new(60.0, 16.60, "OpenGl", 2, "AOT Primary", 0.00) { FrameRateModeBadge = "[MAX]", IsAudioMuted = true }
        ];

        foreach (F3HudTelemetry sample in samples)
        {
            hud.Render(canvas, sample);
            widths.Add(hud.CurrentBounds.Width);
        }

        Assert.Single(widths);
    }

    [Fact]
    public void HostInputMapper_AutoReleasesSyntheticNonHotkeyTapAfterHoldFrames()
    {
        var mapper = new HostInputMapper();
        mapper.InjectSyntheticKey("Return");

        HostInputFrame frame1 = mapper.BuildFrame();
        Assert.True(frame1.DigitalButtons.HasFlag(HostGamepadButtons.Start));
        Assert.True(mapper.IsActionJustPressed("start"));

        HostInputFrame frame2 = mapper.BuildFrame();
        Assert.True(frame2.DigitalButtons.HasFlag(HostGamepadButtons.Start));
        Assert.False(mapper.IsActionJustPressed("start"));

        HostInputFrame frame3 = mapper.BuildFrame();
        Assert.False(frame3.DigitalButtons.HasFlag(HostGamepadButtons.Start));

        mapper.InjectSyntheticKey("Return");
        HostInputFrame frame4 = mapper.BuildFrame();
        Assert.True(frame4.DigitalButtons.HasFlag(HostGamepadButtons.Start));
        Assert.True(mapper.IsActionJustPressed("start"));
    }

    [Fact]
    public void FirstRunMediaGateView_SurfacesValidationErrorBannerWhenBrowsePicksRejectedFile()
    {
        var gate = new FirstRunMediaGateView
        {
            IsVisible = true,
            OnBrowseRequested = () => @"D:\Sandbox\rom\wrong_cart.nes",
            OnBrowseModuleRequested = () => @"D:\Sandbox\modules\wrong_pkg.recomp",
            OnFileDropped = path => $"Rejected '{Path.GetFileName(path)}' (SHA-256 mismatch)"
        };

        Assert.True(gate.TryHandleClick(0, 0, "MediaGate_BrowseButton"));
        Assert.True(gate.IsVisible);
        Assert.Contains("SHA-256 mismatch", gate.ErrorMessage, StringComparison.Ordinal);

        Assert.True(gate.TryHandleClick(0, 0, "MediaGate_SelectModuleButton"));
        Assert.True(gate.IsVisible);
        Assert.Contains("wrong_pkg.recomp", gate.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostSessionRunner_ThreadSafeOverlaysAndSettingsPersistenceRoundTripAcrossRestarts()
    {
        string dataDir = Path.Combine(_tempRoot, "persisted_data");
        var descriptor = HostDrawerDescriptor.CreateDefault("Parity Game", "reCart");

        var runner1 = new HostSessionRunner(1280, 720, descriptor);
        runner1.SetAotCompileState(new HostAotCompileState(true, "Preparing Native Module...", 0.35, "Lifting banks..."));
        runner1.SetMediaGateState(false, statusMessage: "Verified");

        var callbacks1 = new RecordingHeadlessCallbacks(runner1, mutateOnFrame: 2);
        HostSessionResult res1 = await runner1.RunAsync(new HostSessionOptions
        {
            HeadlessFrameCount = 5,
            RequireVisibleWindow = false,
            SettingsDataDirectory = dataDir,
            PreferredBackend = HostRenderBackendKind.Software
        }, callbacks1);
        Assert.Equal(0, res1.ExitCode);
        Assert.True(callbacks1.SawAotWidget);

        var settingsStore = new HostSettingsStore(dataDir);
        ReEngineSettingsBundle saved = settingsStore.LoadOrMigrate();
        Assert.Equal(4, saved.Video.ResolutionScale);
        Assert.True(saved.Video.IsFpsOverlayEnabled);
        Assert.True(saved.Audio.IsAudioMuted);

        var runner2 = new HostSessionRunner(1280, 720, descriptor);
        var callbacks2 = new RecordingHeadlessCallbacks(runner2, mutateOnFrame: -1);
        HostSessionResult res2 = await runner2.RunAsync(new HostSessionOptions
        {
            HeadlessFrameCount = 3,
            RequireVisibleWindow = false,
            SettingsDataDirectory = dataDir,
            PreferredBackend = HostRenderBackendKind.Software
        }, callbacks2);
        Assert.Equal(0, res2.ExitCode);
        Assert.True(res2.IsHudVisible);
    }

    private sealed class RecordingHeadlessCallbacks(HostSessionRunner runner, long mutateOnFrame) : IHostSessionCallbacks
    {
        public bool SawAotWidget { get; private set; }

        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
        {
            IReadOnlyList<HostWidgetBounds> widgets = runner.GetOverlayWidgets();
            if (frameNumber == 0 && widgets.Any(w => w.ControlId == "AotOverlay_Card")) SawAotWidget = true;
            if (mutateOnFrame >= 0)
            {
                if (frameNumber == 0)
                {
                    runner.EnqueueKeyPress("F1");
                }
                else if (frameNumber == 1)
                {
                    runner.EnqueuePointerClick(0, 0, "Video_Scale_4x");
                    runner.EnqueuePointerClick(0, 0, "Video_Toggle_FpsHud");
                    runner.EnqueuePointerClick(0, 0, "Tab_Audio");
                }
                else if (frameNumber == 2)
                {
                    runner.EnqueuePointerClick(0, 0, "Audio_Mute");
                    runner.SetAotCompileState(new HostAotCompileState(false, "Ready", 1.0));
                }
            }
            return ValueTask.CompletedTask;
        }

        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }
}
