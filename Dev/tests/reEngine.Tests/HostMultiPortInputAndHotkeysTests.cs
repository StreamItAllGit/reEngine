// @module: reEngine.Tests.HostMultiPortInputAndHotkeysTests
// @purpose: Isolated v0.5.0 tests for 4-port SDL3 gamepad snapshots, Standard 33 3-tier persistence/conflicts, Menu isolation, and interactive F1/hotkeys.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, Xunit, reEngine.Host
// @exports: HostMultiPortInputAndHotkeysTests

using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostMultiPortInputAndHotkeysTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "reengine_input_v050_" + Guid.NewGuid().ToString("N"));

    public HostMultiPortInputAndHotkeysTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore cleanup races */ }
    }

    [Fact]
    public void Sdl3GamepadProvider_BuildsFourIndependentPortSnapshots()
    {
        var ports = new HostGamepadPortSnapshot[4];
        for (int p = 0; p < 4; p++)
        {
            uint mask = 1u << (p == 0 ? 0 : p == 1 ? 1 : p == 2 ? 2 : 3);
            ports[p] = Sdl3GamepadProvider.BuildPortSnapshot(p, mask, (short)(p * 8000), 0, 0, 0, 0, 20000, connected: true);
        }

        Assert.Equal(0, ports[0].PortIndex);
        Assert.True(ports[0].Buttons.HasFlag(HostGamepadButtons.South));
        Assert.True(ports[1].Buttons.HasFlag(HostGamepadButtons.East));
        Assert.True(ports[2].Buttons.HasFlag(HostGamepadButtons.West));
        Assert.True(ports[3].Buttons.HasFlag(HostGamepadButtons.North | HostGamepadButtons.RightTriggerClick));

        var frame = new HostInputFrame(1, HostGamepadButtons.South, padPorts: ports, pointerContentX: 0.25f, pointerContentY: 0.75f, isPointerInsideContent: true);
        Assert.Equal(4, frame.PadPorts.Count);
        Assert.Equal(0.25f, frame.PointerContentX);
        Assert.True(frame.IsPointerInsideContent);
    }

    [Fact]
    public void HostControlTierStore_PersistsThreeTiers_AndBlocksCollisions()
    {
        var store = new HostControlTierStore(HostInputSchemaKind.SchemaB);
        Assert.False(store.TryRebindAction(HostControlTierMode.PerGameModern, "south", 0, HostScancodes.W, out HostSlotConflict? conflict));
        Assert.NotNull(conflict);
        Assert.True(store.TryRebindAction(HostControlTierMode.PerGameController, "north", 0, HostScancodes.I, out _));
        Assert.False(store.TryRebindAction(HostControlTierMode.GlobalController, "south", 3, HostBindingCodes.GamepadEast, out HostSlotConflict? padConflict));
        Assert.NotNull(padConflict);
        Assert.True(store.TryRebindAction(HostControlTierMode.GlobalController, "south", 3, HostBindingCodes.GamepadL3 + 10, out _));

        store.ActiveTier = HostControlTierMode.PerGameController;
        store.SaveToConfigDirectory(_tempDir);

        var reloaded = new HostControlTierStore();
        Assert.True(reloaded.LoadFromConfigDirectory(_tempDir));
        Assert.Equal(HostControlTierMode.PerGameController, reloaded.ActiveTier);
        Assert.True(reloaded.PerGameController.TryGetSlot("north", out HostMultiBindSlot northSlot));
        Assert.Equal(HostScancodes.I, northSlot.Slot1);
    }

    [Fact]
    public void HostInputMapper_IsolatesMenuDpadAndLeftStick_AndTracksHotkeysAndPointer()
    {
        var mapper = new HostInputMapper { IsMenuOpen = true };
        mapper.Bindings.ClearSlotBinding("moveRight", 0);
        mapper.Bindings.ClearSlotBinding("moveRight", 1);
        mapper.SetPointerContentPosition(0.5f, 0.25f, true);
        mapper.InjectSyntheticKey("F5");
        mapper.InjectSyntheticKey("F9");
        mapper.InjectSyntheticKey("F11");
        mapper.InjectSyntheticKey("Return", ["Alt"]);
        mapper.InjectSyntheticKey("F7");
        mapper.InjectSyntheticKey("Grave");

        HostInputFrame frame = mapper.BuildFrame(HostGamepadButtons.None, extLeftX: 0.75f, extLeftY: -0.60f);
        Assert.True(frame.DigitalButtons.HasFlag(HostGamepadButtons.DPadRight));
        Assert.True(frame.DigitalButtons.HasFlag(HostGamepadButtons.DPadUp));
        Assert.True((frame.HotkeyFlags & (uint)HostHotkeyFlags.QuickSave) != 0);
        Assert.True((frame.HotkeyFlags & (uint)HostHotkeyFlags.QuickLoad) != 0);
        Assert.True((frame.HotkeyFlags & (uint)HostHotkeyFlags.ToggleFullscreen) != 0);
        Assert.True((frame.HotkeyFlags & (uint)HostHotkeyFlags.CutsceneSkip) != 0);
        Assert.True((frame.HotkeyFlags & (uint)HostHotkeyFlags.FastForward) != 0);
        Assert.True(frame.IsCutsceneSkipPressed);
        Assert.Equal(0.5f, frame.PointerContentX);
        Assert.True(frame.IsPointerInsideContent);
    }

    [Fact]
    public void F1SettingsDrawerView_InvokesLiveCallbacks_ForVideoAndInteractiveExtraRows()
    {
        var callbacks = new RecordingCallbacks();
        var canvas = new PixelOverlayCanvas(1280, 720);
        var drawer = new F1SettingsDrawerView { IsOpen = true, ActiveTab = HostDrawerTabId.Video, EventCallbacks = callbacks };
        var descriptor = new HostDrawerDescriptor(
            "Test Module", "reCart", "AOT Primary", "opengl", 2, "B",
            ExtraRows: [new HostDrawerRowDescriptor("CustomToggle", HostDrawerTabId.Video, "Mapper Reset", "Ready", IsInteractive: true)],
            ControlOptions: [new HostControlOptionDescriptor("ctrlMode", "Movement", ["Classic", "Modern"], "Classic")]);

        var mapper = new HostInputMapper();
        drawer.Render(canvas, descriptor, mapper.Bindings);

        Assert.True(drawer.TryHandleClick(0, 0, "Video_AspectRatio", mapper));
        Assert.True(drawer.TryHandleClick(0, 0, "Video_Toggle_Overscan", mapper));
        Assert.True(drawer.TryHandleClick(0, 0, "Video_Toggle_Fullscreen", mapper));
        Assert.True(drawer.TryHandleClick(0, 0, "Video_Toggle_FastForward", mapper));
        Assert.True(drawer.TryHandleClick(0, 0, "ExtraRow_CustomToggle", mapper, isRightClick: true));

        drawer.ActiveTab = HostDrawerTabId.System;
        drawer.Render(canvas, descriptor, mapper.Bindings);
        Assert.True(drawer.TryHandleClick(0, 0, "System_SkipCutscene", mapper));

        Assert.Equal(HostAspectRatioMode.FourByThree, callbacks.Aspect);
        Assert.True(callbacks.Fullscreen);
        Assert.True(callbacks.FastForward);
        Assert.Equal(1, callbacks.CutsceneSkips);
        Assert.Equal(("CustomToggle", true), callbacks.LastExtraRow);
    }

    private sealed class RecordingCallbacks : IHostSessionEventCallbacks
    {
        public HostAspectRatioMode Aspect { get; private set; }
        public HostOverscanCrop Crop { get; private set; }
        public bool Fullscreen { get; private set; }
        public bool FastForward { get; private set; }
        public int CutsceneSkips { get; private set; }
        public (string Id, bool Secondary) LastExtraRow { get; private set; }
        public void OnAspectRatioChanged(HostAspectRatioMode mode) => Aspect = mode;
        public void OnOverscanCropChanged(HostOverscanCrop crop) => Crop = crop;
        public void OnFullscreenChanged(bool isFullscreen) => Fullscreen = isFullscreen;
        public void OnFastForwardChanged(bool isEnabled) => FastForward = isEnabled;
        public void OnCutsceneSkipRequested() => CutsceneSkips++;
        public void OnExtraRowActivated(string rowId, bool isSecondaryAction) => LastExtraRow = (rowId, isSecondaryAction);
    }
}
