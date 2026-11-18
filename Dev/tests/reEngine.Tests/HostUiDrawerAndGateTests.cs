// @module: reEngine.Tests.HostUiDrawerAndGateTests
// @purpose: xUnit tests for 5-tab F1SettingsDrawerView, F3HudOverlayView, and FirstRunMediaGateView atop Foundation.Core.PixelOverlayCanvas.
// @depends-on: System, System.Collections.Generic, System.Linq, Foundation.Core, Xunit, reEngine.Host
// @exports: HostUiDrawerAndGateTests

using Foundation.Core;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;
using Xunit;

namespace reEngine.Tests;

public sealed class HostUiDrawerAndGateTests
{
    [Fact]
    public void F1SettingsDrawerView_RendersAllFiveTabsAndProcessesPointerClicks()
    {
        var canvas = new PixelOverlayCanvas(1280, 720);
        var mapper = new HostInputMapper();
        var descriptor = HostDrawerDescriptor.CreateDefault("MeadowValley BackToEarth", "reEngine.Host");
        var drawer = new F1SettingsDrawerView { IsOpen = true };

        drawer.Render(canvas, descriptor);
        IReadOnlyList<HostWidgetBounds> widgets = drawer.GetWidgets();

        Assert.Contains(widgets, w => w.ControlId == "F1Drawer");
        Assert.Contains(widgets, w => w.ControlId == "Tab_Video");
        Assert.Contains(widgets, w => w.ControlId == "Tab_Controls");
        Assert.Contains(widgets, w => w.ControlId == "Tab_Keybinds");
        Assert.Contains(widgets, w => w.ControlId == "Tab_System");
        Assert.Contains(widgets, w => w.ControlId == "Tab_DiscMedia");
        Assert.Contains(widgets, w => w.ControlId == "Drawer_CloseButton");

        Assert.True(drawer.TryHandleClick(0, 0, "Tab_Controls", mapper));
        Assert.Equal(HostDrawerTabId.Controls, drawer.ActiveTab);
        drawer.Render(canvas, descriptor);

        Assert.True(drawer.TryHandleClick(0, 0, "Drawer_ActionButton", mapper));
        Assert.Equal(HostInputSchemaKind.SchemaC, drawer.SelectedSchema);

        Assert.True(drawer.TryHandleClick(0, 0, "Tab_Keybinds", mapper));
        drawer.Render(canvas, descriptor);

        Assert.True(drawer.TryHandleClick(0, 0, "Tab_DiscMedia", mapper));
        Assert.Equal(HostDrawerTabId.DiscMedia, drawer.ActiveTab);

        Assert.True(drawer.TryHandleClick(0, 0, "Drawer_CloseButton", mapper));
        Assert.False(drawer.IsOpen);
    }

    [Fact]
    public void F3HudOverlayView_DrawsTelemetryOntoPixelOverlayCanvas()
    {
        var canvas = new PixelOverlayCanvas(800, 600);
        canvas.Clear(new RgbaColor(14, 14, 16, 255));
        var hud = new F3HudOverlayView { IsVisible = true };

        hud.Render(canvas, new F3HudTelemetry(60.0, 16.6, "OpenGl", 2, "AOT Primary", 0.5));
        Assert.NotNull(hud.TryBuildWidgetBounds());
        Assert.Contains(canvas.Buffer, b => b != 14 && b != 16 && b != 255);
    }

    [Fact]
    public void FirstRunMediaGateView_ExposesWidgetsAndDismissesOnContinueOrBrowse()
    {
        var canvas = new PixelOverlayCanvas(960, 540);
        var gate = new FirstRunMediaGateView
        {
            IsVisible = true,
            ExpectedSha256 = "abcdef0123456789",
            OnBrowseRequested = () => @"D:\Sandbox\data\rom\disc.cue"
        };
        gate.Render(canvas, HostDrawerDescriptor.CreateDefault());

        IReadOnlyList<HostWidgetBounds> widgets = gate.GetWidgets();
        Assert.Equal(2, widgets.Count);
        Assert.True(gate.TryHandleClick(0, 0, "MediaGate_BrowseButton"));
        Assert.False(gate.IsVisible);
        Assert.Contains("disc.cue", gate.VerifiedMediaPath, StringComparison.OrdinalIgnoreCase);

        gate.IsVisible = true;
        gate.Render(canvas, HostDrawerDescriptor.CreateDefault());
        Assert.True(gate.TryHandleClick(0, 0, "MediaGate_DemoBypassButton"));
        Assert.False(gate.IsVisible);
    }
}
