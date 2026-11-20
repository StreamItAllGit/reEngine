// @module: reEngine.Host.Ui.F1DrawerTabRenderers
// @purpose: Scrollable Video and Media tab renderers plus ExtraRows helpers for F1SettingsDrawerView.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Globalization, Foundation.Core, reEngine.Host.Contracts
// @exports: F1DrawerTabRenderers

using System.Drawing;
using System.Globalization;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

internal static class F1DrawerTabRenderers
{
    private static readonly (HostRenderBackendKind Kind, string Id, string Label)[] VideoBackends =
    [
        (HostRenderBackendKind.OpenGl, "OpenGl", "OpenGL"),
        (HostRenderBackendKind.Vulkan, "Vulkan", "Vulkan"),
        (HostRenderBackendKind.DirectX, "DirectX", "DirectX"),
        (HostRenderBackendKind.ParallelCpu, "ParallelCpu", "ParallelCpu"),
        (HostRenderBackendKind.Software, "Software", "Software")
    ];

    private static readonly int[] Scales = [1, 2, 4, 8];
    private static readonly int[] TargetFpsPresets = [30, 60, 120, 144, 240, 0];

    public static void RenderVideoTab(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, List<DrawerControl> controls,
        ref int scrollOffset, out int maxScroll, out Rectangle cyclePrimaryRect)
    {
        cyclePrimaryRect = Rectangle.Empty;
        var layout = new HostDrawerScrollLayout(body, view.UiTextScale, view.FocusedControlId);

        layout.AddText($"Backend: {FormatBackendName(view.SelectedBackend)}", HostOverlayPalette.TextPrimary);
        layout.AddColumns(controls,
            BuildBackendCol(VideoBackends[0], view),
            BuildBackendCol(VideoBackends[1], view),
            BuildBackendCol(VideoBackends[2], view));
        layout.AddColumns(controls,
            BuildBackendCol(VideoBackends[3], view),
            BuildBackendCol(VideoBackends[4], view));

        layout.AddText($"Resolution Scale: {view.ResolutionScale}x", HostOverlayPalette.TextPrimary);
        var scaleCols = new (string, string, bool, bool, RgbaColor, Action<bool>, Action<Rectangle>?)[Scales.Length];
        for (int i = 0; i < Scales.Length; i++)
        {
            int sc = Scales[i];
            scaleCols[i] = ($"Video_Scale_{sc}x", $"{sc}x", view.ResolutionScale == sc, false, HostOverlayPalette.AccentCyan,
                _ => { view.ResolutionScale = sc; view.EventCallbacks?.OnResolutionScaleChanged(sc); }, null);
        }
        layout.AddColumns(controls, scaleCols);

        string frLabel = $"Framerate: {HostFrameRateModeCodec.ToDisplayLabel(view.FrameRateMode)}";
        layout.AddPill("Video_FrameRateMode", frLabel, view.FrameRateMode == HostFrameRateMode.Enhanced, HostOverlayPalette.AccentCyan, isRight =>
        {
            view.FrameRateMode = isRight
                ? HostFrameRateMode.Enhanced
                : (view.FrameRateMode == HostFrameRateMode.Enhanced ? HostFrameRateMode.Original : HostFrameRateMode.Enhanced);
            view.EventCallbacks?.OnFrameRateModeChanged(view.FrameRateMode);
        }, controls);

        string fpsTitle = view.TargetFps <= 0 ? "Target FPS: Max" : $"Target FPS: {view.TargetFps}";
        layout.AddText(fpsTitle, HostOverlayPalette.TextPrimary);
        var fpsCols = new (string, string, bool, bool, RgbaColor, Action<bool>, Action<Rectangle>?)[TargetFpsPresets.Length];
        for (int i = 0; i < TargetFpsPresets.Length; i++)
        {
            int fps = TargetFpsPresets[i];
            string lbl = fps == 0 ? "Max" : fps.ToString(CultureInfo.InvariantCulture);
            fpsCols[i] = ($"Video_TargetFps_{fps}", lbl, view.TargetFps == fps, false, HostOverlayPalette.AccentCyan,
                _ => { view.TargetFps = fps; view.EventCallbacks?.OnTargetFpsChanged(fps); }, null);
        }
        layout.AddColumns(controls, fpsCols);

        string aspectLabel = $"Aspect: {HostAspectRatioModeCodec.ToDisplayLabel(view.AspectRatioMode)}";
        bool overscanOn = IsOverscanEnabled(view.OverscanCropPx);
        string overscanLabel = overscanOn ? $"Overscan: {FormatOverscan(view.OverscanCropPx)}" : "Overscan: 0px";
        layout.AddColumns(controls,
            ("Video_AspectRatio", aspectLabel, view.AspectRatioMode != HostAspectRatioMode.NativePar, false, HostOverlayPalette.AccentCyan, isRight =>
            {
                view.AspectRatioMode = isRight ? HostAspectRatioMode.NativePar : HostAspectRatioModeCodec.CycleNext(view.AspectRatioMode);
                view.EventCallbacks?.OnAspectRatioChanged(view.AspectRatioMode);
            }, null),
            ("Video_Toggle_Overscan", overscanLabel, overscanOn, false, HostOverlayPalette.AccentCyan, isRight =>
            {
                view.OverscanCropPx = isRight ? HostOverscanCrop.None : CycleOverscan(view.OverscanCropPx);
                view.EventCallbacks?.OnOverscanCropChanged(view.OverscanCropPx);
            }, null));

        layout.AddColumns(controls,
            ("Video_Toggle_Vsync", "VSync", view.IsVsyncEnabled, true, HostOverlayPalette.AccentCyan, _ =>
            {
                view.IsVsyncEnabled = !view.IsVsyncEnabled;
                view.EventCallbacks?.OnVsyncChanged(view.IsVsyncEnabled);
            }, null),
            ("Video_Toggle_Fullscreen", "Fullscreen", view.IsFullscreen, true, HostOverlayPalette.AccentCyan, _ =>
            {
                view.IsFullscreen = !view.IsFullscreen;
                view.EventCallbacks?.OnFullscreenChanged(view.IsFullscreen);
            }, null));

        layout.AddToggle("Video_Toggle_FpsHud", "FPS Overlay (F3)", view.IsFpsOverlayVisible, HostOverlayPalette.AccentCyan,
            _ => view.IsFpsOverlayVisible = !view.IsFpsOverlayVisible, controls);

        string shotLabel = view.ScreenshotCaptureMode == HostScreenshotCaptureMode.FilteredComposite ? "F12: Filtered" : "F12: Clean";
        layout.AddColumns(controls,
            ("Video_Toggle_HdPacks", "HD Packs", view.IsHdTexturePackEnabled, true, HostOverlayPalette.AccentSuccess,
                _ => view.IsHdTexturePackEnabled = !view.IsHdTexturePackEnabled, null),
            ("Video_Toggle_FastForward", "Fast-Forward", view.IsFastForwardEnabled, true, HostOverlayPalette.AccentWarning, _ =>
            {
                view.IsFastForwardEnabled = !view.IsFastForwardEnabled;
                view.EventCallbacks?.OnFastForwardChanged(view.IsFastForwardEnabled);
            }, null));

        layout.AddPill("Video_ShotMode", shotLabel, view.ScreenshotCaptureMode == HostScreenshotCaptureMode.FilteredComposite, HostOverlayPalette.AccentCyan, _ =>
        {
            view.ScreenshotCaptureMode = view.ScreenshotCaptureMode == HostScreenshotCaptureMode.CleanNative
                ? HostScreenshotCaptureMode.FilteredComposite
                : HostScreenshotCaptureMode.CleanNative;
            view.EventCallbacks?.OnScreenshotModeChanged(view.ScreenshotCaptureMode);
        }, controls);

        double clampedUiScale = HostOverlayTextRenderer.ClampUiTextScale(view.UiTextScale);
        string scaleText = string.Create(CultureInfo.InvariantCulture, $"{clampedUiScale:0.00}x");
        layout.AddStepper("Video_UiTextScale", "Video_UiTextScale_Dec", "Video_UiTextScale_Inc",
            "UI Text Scale", scaleText, HostOverlayPalette.AccentCyan,
            isRight => SetUiScale(view, isRight ? 1.0 : StepUiScale(view.UiTextScale, decrease: false)),
            _ => SetUiScale(view, StepUiScale(view.UiTextScale, decrease: true)),
            _ => SetUiScale(view, StepUiScale(view.UiTextScale, decrease: false)),
            controls);

        string filterLabel = $"Filter: {HostVideoFilterModeCodec.ToDisplayLabel(view.VideoFilterMode)}";
        layout.AddPill("Video_FilterMode", filterLabel, view.VideoFilterMode != HostVideoFilterMode.Nearest, HostOverlayPalette.AccentCyan, isRight =>
        {
            view.VideoFilterMode = isRight ? HostVideoFilterMode.Nearest : HostVideoFilterModeCodec.CycleNext(view.VideoFilterMode);
            view.EventCallbacks?.OnVideoFilterChanged(view.VideoFilterMode);
        }, controls);

        layout.AddColumns(controls,
            ("Video_Toggle_TrueColor", "24-Bit", view.IsTrueColor24BitEnabled, true, HostOverlayPalette.AccentCyan, _ =>
            {
                view.IsTrueColor24BitEnabled = !view.IsTrueColor24BitEnabled;
                view.EventCallbacks?.OnTrueColorChanged(view.IsTrueColor24BitEnabled);
            }, null),
            ("Video_Toggle_Dither", "Dither", view.IsDitheringEnabled, true, HostOverlayPalette.AccentCyan, _ =>
            {
                view.IsDitheringEnabled = !view.IsDitheringEnabled;
                view.EventCallbacks?.OnDitheringChanged(view.IsDitheringEnabled);
            }, null));

        if (!string.IsNullOrWhiteSpace(view.FallbackBanner))
        {
            layout.AddBanner("Video_FallbackBanner", view.FallbackBanner!, HostOverlayPalette.WarnFill, HostOverlayPalette.AccentWarning, HostOverlayPalette.AccentWarning, controls);
        }

        HostDrawerScrollLayout.AppendExtraRows(layout, HostDrawerTabId.Video, descriptor.ExtraRows, view, controls);
        _ = layout.Render(canvas, "VIDEO & SCALING", "Video", ref scrollOffset, out maxScroll, controls, d => view.ScrollActiveTab(d));
    }

    public static void RenderDiscMediaTab(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, List<DrawerControl> controls,
        ref int scrollOffset, out int maxScroll)
    {
        var layout = new HostDrawerScrollLayout(body, view.UiTextScale, view.FocusedControlId);
        layout.AddText($"Media: {descriptor.MediaLabel}", HostOverlayPalette.TextPrimary);
        layout.AddText("Drag & drop media or .recomp onto window:", HostOverlayPalette.TextMuted);
        layout.AddText(".cue, .bin, .iso, .chd, .nes, .fds, .7z, .zip, .recomp", HostOverlayPalette.TextMuted);
        layout.AddPill("Media_ChangeMedia", "Change Media...", true, HostOverlayPalette.AccentCyan, _ =>
        {
            string? picked = view.OnBrowseMediaRequested?.Invoke();
            view.EventCallbacks?.OnChangeMediaRequested(picked);
        }, controls);
        layout.AddPill("Media_LoadModule", "Load .recomp...", false, HostOverlayPalette.AccentCyan, _ =>
        {
            string? picked = view.OnBrowseModuleRequested?.Invoke();
            view.EventCallbacks?.OnLoadModuleRequested(picked);
        }, controls);

        HostDrawerScrollLayout.AppendExtraRows(layout, HostDrawerTabId.DiscMedia, descriptor.ExtraRows, view, controls);
        _ = layout.Render(canvas, "DISC / MEDIA", "Media", ref scrollOffset, out maxScroll, controls, d => view.ScrollActiveTab(d));
    }

    private static (string, string, bool, bool, RgbaColor, Action<bool>, Action<Rectangle>?) BuildBackendCol(
        (HostRenderBackendKind Kind, string Id, string Label) b, F1SettingsDrawerView view) =>
        ($"Video_Backend_{b.Id}", b.Label, view.SelectedBackend == b.Kind, false, HostOverlayPalette.AccentCyan, _ =>
        {
            view.SelectedBackend = b.Kind;
            view.EventCallbacks?.OnBackendChanged(b.Kind);
        }, null);

    private static string FormatBackendName(HostRenderBackendKind kind) =>
        kind == HostRenderBackendKind.OpenGl ? "OpenGL" : kind.ToString();

    private static void SetUiScale(F1SettingsDrawerView view, double next)
    {
        double clamped = HostOverlayTextRenderer.ClampUiTextScale(next);
        view.UiTextScale = clamped;
        view.EventCallbacks?.OnUiTextScaleChanged(clamped);
    }

    private static double StepUiScale(double current, bool decrease)
    {
        double clamped = HostOverlayTextRenderer.ClampUiTextScale(current);
        double next = decrease ? Math.Round(clamped - 0.25, 2) : Math.Round(clamped + 0.25, 2);
        return HostOverlayTextRenderer.ClampUiTextScale(next);
    }

    private static HostOverscanCrop CycleOverscan(HostOverscanCrop crop) => crop.Top switch
    {
        <= 0 => new HostOverscanCrop(8, 8, 8, 8),
        < 16 => new HostOverscanCrop(16, 16, 16, 16),
        _ => HostOverscanCrop.None
    };

    private static bool IsOverscanEnabled(HostOverscanCrop crop) =>
        crop.Top > 0 || crop.Bottom > 0 || crop.Left > 0 || crop.Right > 0;

    private static string FormatOverscan(HostOverscanCrop crop) =>
        crop.Top == crop.Bottom && crop.Top == crop.Left && crop.Top == crop.Right
            ? $"{crop.Top}px"
            : $"{crop.Top}/{crop.Bottom}/{crop.Left}/{crop.Right}";
}
