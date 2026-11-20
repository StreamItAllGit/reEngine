using Foundation.Core;
using reEngine.App;
using reEngine.Host.Contracts;
using reEngine.Host.Core;
using reEngine.Host.Input;
using Xunit;

namespace reEngine.Tests;

public sealed class HostNonQuittingEscMouseCaptureAndHotkeysTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"reengine-v061-t2-{Guid.NewGuid():N}");

    public HostNonQuittingEscMouseCaptureAndHotkeysTests() => Directory.CreateDirectory(_tempRoot);

    private sealed class EscAndQuitProbeCallbacks : IHostSessionCallbacks
    {
        private readonly HostSessionRunner _runner;
        private readonly Action<long> _onFrame;
        public EscAndQuitProbeCallbacks(HostSessionRunner runner, Action<long> onFrame) { _runner = runner; _onFrame = onFrame; }

        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
        {
            _onFrame(frameNumber);
            if (frameNumber == 0) _runner.EnqueueKeyPress("F1");
            else if (frameNumber == 2) _runner.EnqueueKeyPress("ESC");
            else if (frameNumber == 4) _runner.EnqueueKeyPress("ESC");
            else if (frameNumber == 6) _runner.EnqueueKeyPress("F1");
            else if (frameNumber == 8) _runner.EnqueuePointerClick(0, 0, "Tab_System");
            else if (frameNumber == 10) _runner.EnqueuePointerClick(0, 0, "System_QuitGame");
            return ValueTask.CompletedTask;
        }

        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }

    [Fact]
    public async Task HostSessionRunner_NeverQuitsOnEscape_AndExitsCleanlyOnSystemQuitGame()
    {
        var runner = new HostSessionRunner();
        long maxFrame = -1;
        string controlsPath = Path.Combine(_tempRoot, "settings.controls.json");
        new HostControlTierStore(HostInputSchemaKind.SchemaB).SaveToFile(controlsPath);
        DateTime baselineWriteUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(controlsPath, baselineWriteUtc);
        var opts = new HostSessionOptions
        {
            RequireVisibleWindow = false, PreferredBackend = HostRenderBackendKind.Software,
            ControlsConfigPath = controlsPath, ControlMode = HostControlMode.ModernWasdMouse
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        HostSessionResult result = await runner.RunAsync(opts, new EscAndQuitProbeCallbacks(runner, f => maxFrame = f), null, cts.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.True(maxFrame >= 10, $"Expected frame loop to continue past closed-drawer Esc on frame 4; reached frame {maxFrame}");
        Assert.Equal(baselineWriteUtc, File.GetLastWriteTimeUtc(controlsPath));
    }

    [Fact]
    public void HostRelativeMouseController_And_Mapper_EnforceEscReleaseUnboundTabFfAndSens()
    {
        bool nativeState = false;
        var mouse = new HostRelativeMouseController(b => nativeState = b);
        Assert.True(mouse.TryCaptureOnViewportClick(HostControlMode.ModernWasdMouse, isOverlayOpen: false, isWindowFocused: true));
        Assert.True(nativeState);
        Assert.True(mouse.ReleaseOnOverlayOpen());
        Assert.False(nativeState);
        Assert.True(mouse.TryCaptureOnViewportClick(HostControlMode.ModernWasdMouse, false, true));
        Assert.True(mouse.ReleaseOnFocusLoss());
        Assert.False(mouse.IsCaptured);
        Assert.True(mouse.TryCaptureOnViewportClick(HostControlMode.ModernWasdMouse, false, true));
        Assert.True(mouse.TryReleaseOnEscape());
        Assert.False(mouse.IsCaptured);

        var mapper = new HostInputMapper { MouseSensitivity = 2.5 };
        mapper.AccumulateMouse(4f, -2f, 0);
        HostInputFrame sensFrame = mapper.BuildFrame();
        Assert.Equal(10f, sensFrame.MouseDeltaX, 2);

        mapper.ControlTiers.ActiveRegistry.ClearSlotBinding("select", 0);
        mapper.SetScancodeState(HostScancodes.Tab, true);
        HostInputFrame ffHeld = mapper.BuildFrame();
        Assert.True(ffHeld.IsFastForwardHeld);
        mapper.SetScancodeState(HostScancodes.Tab, false);

        Assert.True(mapper.ControlTiers.ActiveRegistry.TrySetSlotBindingUnique("select", 0, HostScancodes.Tab, out _));
        mapper.SetScancodeState(HostScancodes.Tab, true);
        Assert.False(mapper.BuildFrame().IsFastForwardHeld);
    }

    [Fact]
    public void HostActionBindingSet_And_ControlTierStore_PersistMenuChordCrossConflictsAndLegacyMigration()
    {
        var store = new HostControlTierStore(HostInputSchemaKind.SchemaB)
        {
            ControlMode = HostControlMode.ClassicTank, MouseSensitivity = 1.75, MenuControllerChord = HostMenuControllerChord.Guide
        };
        Assert.False(store.TryRebindHostAction(HostActionBindingIds.QuickSave, 0, HostScancodes.W, out string? conflict));
        Assert.NotNull(conflict);
        Assert.True(store.TryRebindHostAction(HostActionBindingIds.QuickSave, 0, HostScancodes.F8, out _));

        string controlsPath = Path.Combine(_tempRoot, "settings.controls.json");
        store.SaveToFile(controlsPath);
        string raw = File.ReadAllText(controlsPath);
        Assert.Contains("hostActionBindings", raw, StringComparison.Ordinal);
        Assert.Contains("menuControllerChord", raw, StringComparison.Ordinal);

        var reloaded = new HostControlTierStore();
        Assert.True(reloaded.LoadFromFile(controlsPath));
        Assert.Equal(HostControlMode.ClassicTank, reloaded.ControlMode);
        Assert.Equal(1.75, reloaded.MouseSensitivity);
        Assert.Equal(HostMenuControllerChord.Guide, reloaded.MenuControllerChord);
        Assert.True(reloaded.HostActions.TryGetSlot(HostActionBindingIds.QuickSave, out HostMultiBindSlot qs) && qs.Slot1 == HostScancodes.F8);

        var mapper = new HostInputMapper();
        mapper.HostActions.EvaluateHotkeys(new HashSet<int>(), new HashSet<int> { HostBindingCodes.GamepadGuide }, 0, 0f, 0u, HostMenuControllerChord.Guide, out _);
        HostInputFrame chordFrame = mapper.BuildFrame();
        Assert.False(chordFrame.IsFastForwardHeld);
        HostHotkeyFlags guideFlags = mapper.HostActions.EvaluateHotkeys(new HashSet<int>(), new HashSet<int> { HostBindingCodes.GamepadGuide }, 0, 0f, 0u, HostMenuControllerChord.Guide, out _);
        Assert.True(guideFlags.HasFlag(HostHotkeyFlags.ToggleDrawer));
    }

    [Fact]
    public async Task DemoHostCallbacks_ProducesSilentIdleAudio_And_SoftInputChime()
    {
        var demo = new DemoHostCallbacks();
        byte[] fb = new byte[320 * 240 * 4];
        short[] idlePcm = new short[512];
        await demo.OnFrameTickAsync(0, new HostInputFrame(0, HostGamepadButtons.None), fb, 320, 240, CancellationToken.None);
        int idleRead = demo.ReadAudioSamples(idlePcm);
        Assert.True(idleRead > 0);
        Assert.All(idlePcm[..idleRead], sample => Assert.Equal((short)0, sample));

        await demo.OnFrameTickAsync(1, new HostInputFrame(1, HostGamepadButtons.South), fb, 320, 240, CancellationToken.None);
        short[] chimePcm = new short[512];
        int chimeRead = demo.ReadAudioSamples(chimePcm);
        Assert.True(chimeRead > 0);
        Assert.Contains(chimePcm[..chimeRead], s => s != 0);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }
}
