// @module: reEngine.Tests.HostMultiBindInputAndTouchTests
// @purpose: Isolated v0.4.0 tests for 3-slot multi-bind input, repeat suppression, Xbox/HID gamepads + rumble, ControlOptions, 5-col Keybinds, and touch overlays.

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostMultiBindInputAndTouchTests
{
    [Fact]
    public void HostMultiBindRegistry_SupportsThreeSlotsAndMouseWheelBindings()
    {
        var registry = new HostMultiBindRegistry(HostInputSchemaKind.SchemaB);
        Assert.True(registry.SetSlotBinding("south", 2, HostBindingCodes.WheelUp));
        Assert.True(registry.TryGetSlot("south", out HostMultiBindSlot slot));
        Assert.Equal(HostBindingCodes.WheelUp, slot.Slot3);

        string json = registry.SerializeJson();
        var reloaded = new HostMultiBindRegistry(HostInputSchemaKind.SchemaB);
        Assert.True(reloaded.DeserializeJson(json));
        Assert.True(reloaded.TryGetSlot("south", out HostMultiBindSlot restored));
        Assert.Equal(HostBindingCodes.WheelUp, restored.Slot3);

        reloaded.EvaluateFrame(new HashSet<int>(), new HashSet<int> { HostBindingCodes.WheelUp }, 0, 1.0f, 0u);
        Assert.True(reloaded.IsActionActive("south"));
        Assert.True(reloaded.IsActionJustPressed("south"));

        reloaded.EvaluateFrame(new HashSet<int>(), new HashSet<int>(), 0, 0f, 0u);
        Assert.False(reloaded.IsActionActive("south"));
    }

    [Fact]
    public void HostInputMapper_SuppressesKeyRepeatsAndIsolatesSingleEdgeAcrossSubTicks()
    {
        var mapper = new HostInputMapper { ActiveSchema = HostInputSchemaKind.SchemaB };

        mapper.SetScancodeState(HostScancodes.Z, true, isRepeat: true);
        HostInputFrame ignored = mapper.BuildFrame();
        Assert.False(mapper.IsActionActive("south"));
        Assert.Equal(HostGamepadButtons.None, ignored.DigitalButtons);

        mapper.SetScancodeState(HostScancodes.Z, true, isRepeat: false);
        HostInputFrame tick1 = mapper.BuildFrame();
        Assert.True(mapper.IsActionActive("south"));
        Assert.True(mapper.IsActionJustPressed("south"));
        Assert.True(tick1.DigitalButtons.HasFlag(HostGamepadButtons.South));

        HostInputFrame tick2 = mapper.BuildFrame();
        Assert.True(mapper.IsActionActive("south"));
        Assert.False(mapper.IsActionJustPressed("south"));
        Assert.True(tick2.DigitalButtons.HasFlag(HostGamepadButtons.South));
    }

    [Fact]
    public void Sdl3GamepadProvider_MapsButtonsDeadzoneTriggerThresholdAndClassification()
    {
        uint rawBits = (1u << 0) | (1u << Sdl3GamepadNative.ButtonDPadUp);
        HostExternalInputSnapshot snap = Sdl3GamepadProvider.BuildSnapshot(
            rawBits,
            rawLeftX: 1500,
            rawLeftY: short.MaxValue,
            rawRightX: 0,
            rawRightY: 0,
            rawLeftTrigger: 1000,
            rawRightTrigger: 22000,
            connected: true);

        Assert.True(snap.ControllerConnected);
        Assert.Equal(0f, snap.LeftX);
        Assert.Equal(1f, snap.LeftY, 2);
        Assert.True(snap.Buttons.HasFlag(HostGamepadButtons.South));
        Assert.True(snap.Buttons.HasFlag(HostGamepadButtons.DPadUp));
        Assert.True(snap.Buttons.HasFlag(HostGamepadButtons.RightTriggerClick));
        Assert.True(Sdl3GamepadProvider.IsGamepadCodePressed(snap.GamepadButtonsMask, HostBindingCodes.GamepadSouth));
        Assert.True(Sdl3GamepadProvider.IsGamepadCodePressed(snap.GamepadButtonsMask, HostBindingCodes.GamepadRT));
        Assert.Equal("Xbox One / Series", Sdl3GamepadProvider.ClassifyControllerFamily(3, "Xbox Series Controller", 0x045E));
        Assert.Equal("DualSense", Sdl3GamepadProvider.ClassifyControllerFamily(6, "Sony DualSense", 0x054C));
    }

    [Fact]
    public void ControlsTabView_RendersDynamicControlOptionsAndHandlesSelectionClick()
    {
        var canvas = new PixelOverlayCanvas(640, 480);
        var tab = new ControlsTabView();
        var mapper = new HostInputMapper();
        HostInputSchemaKind schema = HostInputSchemaKind.SchemaB;
        var options = new[]
        {
            new HostControlOptionDescriptor("controllerDpadMode", "D-Pad Behavior", ["Movement", "QuickAction", "Hybrid"], "Movement")
        };

        tab.Render(canvas, new Rectangle(20, 70, 380, 360), schema, options);
        HostWidgetBounds hybridWidget = tab.GetWidgets().First(w => w.ControlId == "Controls_Opt_controllerDpadMode_Hybrid");

        Assert.True(tab.TryHandleClick(
            hybridWidget.Bounds.X + 4,
            hybridWidget.Bounds.Y + 4,
            hybridWidget.ControlId,
            mapper,
            ref schema));
        Assert.Equal("Hybrid", tab.GetSelectedChoice("controllerDpadMode"));
    }

    [Fact]
    public void KeybindsTabView_RendersFiveColumnsAndCapturesAndClearsSlots()
    {
        var canvas = new PixelOverlayCanvas(900, 620);
        var registry = new HostMultiBindRegistry(HostInputSchemaKind.SchemaB);
        var view = new KeybindsTabView();
        var body = new Rectangle(120, 70, 700, 440);

        view.Render(canvas, body, HostInputSchemaKind.SchemaB, registry);
        IReadOnlyList<HostWidgetBounds> widgets = view.GetWidgets();
        Assert.Contains(widgets, w => w.ControlId.EndsWith("_0", StringComparison.Ordinal));
        Assert.Contains(widgets, w => w.ControlId.EndsWith("_1", StringComparison.Ordinal));
        Assert.Contains(widgets, w => w.ControlId.EndsWith("_2", StringComparison.Ordinal));
        Assert.Contains(widgets, w => w.ControlId.EndsWith("_3", StringComparison.Ordinal));

        HostWidgetBounds firstSlot3 = widgets.First(w => w.ControlId == "Keybinds_Slot_moveUp_2");
        Assert.True(view.TryHandleClick(firstSlot3.Bounds.X + 2, firstSlot3.Bounds.Y + 2, firstSlot3.ControlId, registry, HostInputSchemaKind.SchemaB));
        Assert.True(view.IsCapturing);
        Assert.True(view.TryCaptureInputCode(HostBindingCodes.WheelDown, registry));
        Assert.False(view.IsCapturing);
        Assert.True(registry.TryGetSlot("moveUp", out HostMultiBindSlot updated));
        Assert.Equal(HostBindingCodes.WheelDown, updated.Slot3);

        Assert.True(view.TryHandleClick(firstSlot3.Bounds.X + 2, firstSlot3.Bounds.Y + 2, firstSlot3.ControlId, registry, HostInputSchemaKind.SchemaB, isRightClick: true));
        Assert.True(registry.TryGetSlot("moveUp", out HostMultiBindSlot cleared));
        Assert.Equal(0, cleared.Slot3);
    }

    [Fact]
    public void HostTouchOverlayView_ComputesPortraitLandscapeAndDecodesTouchAndPinch()
    {
        using var touch = new HostTouchOverlayView(400, 800);
        Assert.True(touch.IsPortraitLayout);

        IReadOnlyList<HostWidgetBounds> portraitWidgets = touch.GetWidgets();
        HostWidgetBounds south = portraitWidgets.First(w => w.ControlId == "Touch_South");
        float normX = (south.Bounds.X + (south.Bounds.Width * 0.5f)) / 400f;
        float normY = (south.Bounds.Y + (south.Bounds.Height * 0.5f)) / 800f;

        _ = touch.ProcessTouchPoints([new HostTouchPoint(1L, normX, normY, 1f, IsPressed: true)], 400, 800);
        HostExternalInputSnapshot hit = touch.Poll();
        Assert.True(hit.Buttons.HasFlag(HostGamepadButtons.South));
        Assert.Equal(1, hit.TouchActiveCount);

        _ = touch.ProcessTouchPoints(
            [
                new HostTouchPoint(10L, 0.45f, 0.50f, 1f, IsPressed: true),
                new HostTouchPoint(11L, 0.55f, 0.50f, 1f, IsPressed: true)
            ],
            1280,
            720);
        Assert.False(touch.IsPortraitLayout);

        IReadOnlyList<HostTouchGestureEvent> gestures = touch.ProcessTouchPoints(
            [
                new HostTouchPoint(10L, 0.35f, 0.50f, 1f, IsPressed: true),
                new HostTouchPoint(11L, 0.65f, 0.50f, 1f, IsPressed: true)
            ],
            1280,
            720);
        Assert.Contains(gestures, g => string.Equals(g.GestureKind, "pinch", StringComparison.OrdinalIgnoreCase) && g.Scale > 1f);
    }

    [Fact]
    public async Task HostSessionRunner_LiveKeybindsCapture_BindsKeyAndCancelsOnEscapeWithoutClosingDrawer()
    {
        var runner = new reEngine.Host.Core.HostSessionRunner(960, 540);
        var mapperField = typeof(reEngine.Host.Core.HostSessionRunner).GetField("_inputMapper", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var mapper = (HostInputMapper)mapperField!.GetValue(runner)!;

        HostSessionResult res = await runner.RunAsync(
            new HostSessionOptions { RequireVisibleWindow = false, HeadlessFrameCount = 7 },
            new LiveCaptureTestCallbacks(runner));

        Assert.Equal(0, res.ExitCode);
        Assert.True(res.IsDrawerOpen, "Escape cancelling active capture must keep the F1 drawer open.");
        Assert.Equal(HostDrawerTabId.Keybinds, res.ActiveTab);
        Assert.True(mapper.Bindings.TryGetSlot("moveUp", out HostMultiBindSlot slot));
        Assert.Equal(HostScancodes.K, slot.Slot3);
    }

    private sealed class LiveCaptureTestCallbacks(reEngine.Host.Core.HostSessionRunner runner) : IHostSessionCallbacks
    {
        public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgba, int width, int height, CancellationToken ct)
        {
            switch (frameNumber)
            {
                case 0: runner.EnqueueKeyPress("F1"); break;
                case 1: runner.EnqueuePointerClick(0, 0, "Tab_Keybinds"); break;
                case 2: runner.EnqueuePointerClick(0, 0, "Keybinds_Slot_moveUp_2"); break;
                case 3: runner.EnqueueKeyPress("K"); break;
                case 4: runner.EnqueuePointerClick(0, 0, "Keybinds_Slot_moveUp_2"); break;
                case 5: runner.EnqueueKeyPress("ESC"); break;
            }
            return ValueTask.CompletedTask;
        }

        public int ReadAudioSamples(Span<short> interleavedStereoPcm) => 0;
    }
}
