using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostParityUiScaleMediaGateAndControllerNavTests
{
    [Fact]
    public void HostOverlayTextRenderer_ScalesDeterministicallyWithin075And200()
    {
        Assert.Equal(0.75, HostOverlayTextRenderer.ClampUiTextScale(0.2));
        Assert.Equal(2.0, HostOverlayTextRenderer.ClampUiTextScale(4.0));
        (int Width, int Height) base1x = HostOverlayTextRenderer.MeasureText("Video Settings", 1.0);
        (int Width, int Height) large = HostOverlayTextRenderer.MeasureText("Video Settings", 2.0);
        Assert.True(base1x.Height >= 10);
        Assert.True(large.Width > base1x.Width);
        Assert.True(HostOverlayTextRenderer.GetRowHeight(1.5) > HostOverlayTextRenderer.GetRowHeight(1.0));
    }

    [Fact]
    public void ControlsAndKeybindsTabs_ContainZeroRawSchemaStringsAndExposeFullV061Controls()
    {
        var canvas = new PixelOverlayCanvas(1280, 720);
        var mapper = new HostInputMapper();
        var descriptor = HostDrawerDescriptor.CreateDefault("Parity", "reEngine.Host");
        var drawer = new F1SettingsDrawerView { IsOpen = true, UiTextScale = 1.25 };

        drawer.ActiveTab = HostDrawerTabId.Controls;
        drawer.Render(canvas, descriptor);
        IReadOnlyList<HostWidgetBounds> ctrlWidgets = drawer.GetWidgets();
        Assert.All(ctrlWidgets, w => Assert.DoesNotContain("Schema ", w.Label, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ctrlWidgets, w => w.ControlId == "Controls_Profile_ModernPc" && w.Label == "Modern PC (Per-Action)");
        Assert.Contains(ctrlWidgets, w => w.ControlId == "Controls_Toggle_ModernWasdMouse");
        Assert.Contains(ctrlWidgets, w => w.ControlId == "Controls_MouseSens_Inc");
        Assert.Contains(ctrlWidgets, w => w.ControlId == "Controls_MenuChord");

        Assert.True(drawer.TryHandleClick(0, 0, "Controls_MouseSens_Inc", mapper));
        Assert.Equal(1.25, drawer.MouseSensitivity);
        Assert.True(drawer.TryHandleClick(0, 0, "Controls_Toggle_ModernWasdMouse", mapper));
        Assert.Equal(HostControlMode.ClassicTank, drawer.ControlMode);

        drawer.ControlTierStore = mapper.ControlTiers;
        drawer.ActiveTab = HostDrawerTabId.Keybinds;
        drawer.Render(canvas, descriptor);
        Assert.True(drawer.ScrollActiveTab(20));
        drawer.Render(canvas, descriptor);
        IReadOnlyList<HostWidgetBounds> keyWidgets = drawer.GetWidgets();
        Assert.All(keyWidgets, w => Assert.DoesNotContain("Schema ", w.Label, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keyWidgets, w => w.ControlId.Contains("toggleMenu", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(keyWidgets, w => w.ControlId.Contains("quickSave", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VideoAndSystemTabs_SupportRightClickResetsUiScaleStepperAndQuitGame()
    {
        var canvas = new PixelOverlayCanvas(1280, 720);
        var mapper = new HostInputMapper();
        var descriptor = HostDrawerDescriptor.CreateDefault();
        var drawer = new F1SettingsDrawerView
        {
            IsOpen = true,
            AspectRatioMode = HostAspectRatioMode.HorPlus21By9,
            OverscanCropPx = new HostOverscanCrop(8, 8, 8, 8),
            TargetFps = 60
        };
        drawer.Render(canvas, descriptor);

        Assert.True(drawer.TryHandleClick(0, 0, "Video_Scale_8x", mapper));
        Assert.Equal(8, drawer.ResolutionScale);
        Assert.True(drawer.TryHandleClick(0, 0, "Video_AspectRatio", mapper, isRightClick: true));
        Assert.Equal(HostAspectRatioMode.NativePar, drawer.AspectRatioMode);
        Assert.True(drawer.TryHandleClick(0, 0, "Video_Toggle_Overscan", mapper, isRightClick: true));
        Assert.Equal(HostOverscanCrop.None, drawer.OverscanCropPx);
        Assert.True(drawer.TryHandleClick(0, 0, "Video_UiTextScale_Inc", mapper));
        Assert.Equal(1.25, drawer.UiTextScale);

        Assert.True(drawer.TryHandleClick(0, 0, "Tab_System", mapper));
        drawer.Render(canvas, descriptor);
        Assert.True(drawer.TryHandleClick(0, 0, "System_ToggleCleanCapture", mapper));
        Assert.True(drawer.IsCleanCaptureModeEnabled);
        Assert.True(drawer.TryHandleClick(0, 0, "System_QuitGame", mapper));
        Assert.True(drawer.QuitRequested);
    }

    [Fact]
    public async Task MediaGate_DragDrop_AotOverlayPump_F3HudBadges_And_ControllerNav_WorkEndToEnd()
    {
        var canvas = new PixelOverlayCanvas(1280, 720);
        var gate = new FirstRunMediaGateView { IsVisible = true, OnFileDropped = p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "Bad extension" : null };
        Assert.False(gate.HandleDroppedFile(@"C:\roms\bad.txt"));
        Assert.NotNull(gate.ErrorMessage);
        Assert.True(gate.HandleDroppedFile(@"C:\roms\game.cue"));
        Assert.False(gate.IsVisible);

        var aot = new HostAotCompileOverlayView();
        int pumped = 0;
        int compiled = await aot.RunWithResponsivePumpAsync(async (report, _) =>
        {
            report(new HostAotCompileState(true, "Compiling IR...", 0.5, "50%"));
            await Task.Delay(35);
            return 7;
        }, _ =>
        {
            pumped++;
            aot.Render(canvas, HostDrawerDescriptor.CreateDefault());
        });
        Assert.Equal(7, compiled);
        Assert.True(pumped >= 1);
        Assert.False(aot.State.IsCompiling);

        var hud = new F3HudOverlayView { IsVisible = true };
        hud.Render(canvas, new F3HudTelemetry(120, 8.3, "Vulkan", 2, "AOT Primary", 0.5) { FrameRateModeBadge = "[MAX]", IsFastForwardEnabled = true, FastForwardMultiplier = 3.0, IsCleanCaptureModeEnabled = false });
        Assert.NotNull(hud.TryBuildWidgetBounds());
        hud.Render(canvas, new F3HudTelemetry(120, 8.3, "Vulkan", 2, "AOT Primary", 0.5) { IsCleanCaptureModeEnabled = true });
        Assert.Null(hud.TryBuildWidgetBounds());

        var nav = new HostDrawerControllerNav();
        var mapper = new HostInputMapper();
        var drawer = new F1SettingsDrawerView { IsOpen = true, ActiveTab = HostDrawerTabId.Video };
        drawer.Render(canvas, HostDrawerDescriptor.CreateDefault());
        Assert.True(nav.TryProcessFrame(new HostInputFrame(1, HostGamepadButtons.RightShoulder), drawer, mapper));
        Assert.Equal(HostDrawerTabId.Audio, drawer.ActiveTab);
        Assert.True(nav.TryProcessFrame(new HostInputFrame(2, HostGamepadButtons.East), drawer, mapper));
        Assert.False(drawer.IsOpen);
    }
}
