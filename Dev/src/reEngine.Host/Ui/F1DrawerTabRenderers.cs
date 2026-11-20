// @module: reEngine.Host.Ui.F1DrawerTabRenderers
// @purpose: Internal tab renderers (Video, System, DiscMedia, ExtraRows) keeping F1SettingsDrawerView concise under 250 SLOC.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts
// @exports: DrawerControl, F1DrawerTabRenderers

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

internal sealed record DrawerControl(string ControlId, string ControlType, string Label, Rectangle Bounds, Action<bool> OnClick);

internal static class F1DrawerTabRenderers
{
    private static readonly (HostRenderBackendKind Kind, string Label)[] VideoBackends =
    [
        (HostRenderBackendKind.OpenGl, "OpenGl"),
        (HostRenderBackendKind.Vulkan, "Vulkan"),
        (HostRenderBackendKind.DirectX, "DirectX"),
        (HostRenderBackendKind.ParallelCpu, "ParallelCpu"),
        (HostRenderBackendKind.Software, "Software")
    ];

    public static void RenderVideoTab(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, List<DrawerControl> controls, out Rectangle cyclePrimaryRect)
    {
        canvas.DrawText(body.X, body.Y, "VIDEO & SCALING", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 14, $"Backend: {view.SelectedBackend}", HostOverlayPalette.TextPrimary, 1);
        const int gap = 4;
        int colW3 = Math.Max(60, (body.Width - (gap * 2)) / 3), colW2 = Math.Max(80, (body.Width - gap) / 2);
        for (int i = 0; i < VideoBackends.Length; i++)
        {
            (HostRenderBackendKind kind, string label) = VideoBackends[i];
            int row = i < 3 ? 0 : 1, col = i < 3 ? i : i - 3, w = row == 0 ? colW3 : colW2;
            var rect = new Rectangle(body.X + (col * (w + gap)), body.Y + 28 + (row * 26), w, 22);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, label, view.SelectedBackend == kind, HostOverlayPalette.AccentCyan);
            controls.Add(new($"Video_Backend_{label}", "Button", label, rect, _ => view.SelectedBackend = kind));
        }

        canvas.DrawText(body.X, body.Y + 82, $"Resolution Scale: {view.ResolutionScale}x", HostOverlayPalette.TextPrimary, 1);
        int[] scales = [1, 2, 4];
        for (int i = 0; i < scales.Length; i++)
        {
            int sc = scales[i];
            var rect = new Rectangle(body.X + (i * (colW3 + gap)), body.Y + 96, colW3, 22);
            HostOverlayLayoutHelpers.DrawPillButton(canvas, rect, $"{sc}x", view.ResolutionScale == sc, HostOverlayPalette.AccentCyan);
            controls.Add(new($"Video_Scale_{sc}x", "Button", $"{sc}x", rect, _ => view.ResolutionScale = sc));
        }

        cyclePrimaryRect = new Rectangle(body.X, body.Y + 124, Math.Min(220, body.Width), 22);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, cyclePrimaryRect, "Cycle Scale (1x/2x/4x)", true, HostOverlayPalette.AccentCyan);

        string aspectLabel = $"Aspect: {HostAspectRatioModeCodec.ToDisplayLabel(view.AspectRatioMode)}";
        var aspectRect = new Rectangle(body.X, body.Y + 152, body.Width, 22);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, aspectRect, aspectLabel, true, HostOverlayPalette.AccentCyan);
        controls.Add(new("Video_AspectRatio", "Button", aspectLabel, aspectRect, _ =>
        {
            view.AspectRatioMode = HostAspectRatioModeCodec.CycleNext(view.AspectRatioMode);
            view.EventCallbacks?.OnAspectRatioChanged(view.AspectRatioMode);
        }));

        bool overscanOn = IsOverscanEnabled(view.OverscanCropPx);
        string overscanLabel = overscanOn ? $"Overscan Crop: {FormatOverscan(view.OverscanCropPx)}" : "Overscan Crop: 0px";
        AddToggle(canvas, new(body.X, body.Y + 178, body.Width, 22), "Video_Toggle_Overscan", overscanLabel, overscanOn, HostOverlayPalette.AccentCyan, controls, _ =>
        {
            view.OverscanCropPx = IsOverscanEnabled(view.OverscanCropPx) ? HostOverscanCrop.None : new HostOverscanCrop(8, 8, 8, 8);
            view.EventCallbacks?.OnOverscanCropChanged(view.OverscanCropPx);
        });

        AddToggle(canvas, new(body.X, body.Y + 204, body.Width, 22), "Video_Toggle_Fullscreen", "Fullscreen", view.IsFullscreen, HostOverlayPalette.AccentCyan, controls, _ =>
        {
            view.IsFullscreen = !view.IsFullscreen;
            view.EventCallbacks?.OnFullscreenChanged(view.IsFullscreen);
        });

        AddToggle(canvas, new(body.X, body.Y + 230, body.Width, 22), "Video_Toggle_FastForward", "Fast-Forward", view.IsFastForwardEnabled, HostOverlayPalette.AccentWarning, controls, _ =>
        {
            view.IsFastForwardEnabled = !view.IsFastForwardEnabled;
            view.EventCallbacks?.OnFastForwardChanged(view.IsFastForwardEnabled);
        });

        AddToggle(canvas, new(body.X, body.Y + 256, body.Width, 22), "Video_Toggle_HdPacks", "HD Packs", view.IsHdTexturePackEnabled, HostOverlayPalette.AccentSuccess, controls, _ =>
            view.IsHdTexturePackEnabled = !view.IsHdTexturePackEnabled);

        int nextY = body.Y + 284;
        if (!string.IsNullOrWhiteSpace(view.FallbackBanner) && nextY + 24 <= body.Bottom)
        {
            var bannerRect = new Rectangle(body.X, nextY, body.Width, 24);
            HostOverlayLayoutHelpers.DrawBanner(canvas, bannerRect, view.FallbackBanner!, HostOverlayPalette.WarnFill, HostOverlayPalette.AccentWarning, HostOverlayPalette.AccentWarning);
            controls.Add(new("Video_FallbackBanner", "Label", view.FallbackBanner!, bannerRect, static _ => { }));
            nextY += 30;
        }
        RenderExtraRows(canvas, body, nextY, HostDrawerTabId.Video, descriptor.ExtraRows, view, controls);
    }

    public static void RenderSystemTab(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, List<DrawerControl> controls, out Rectangle cyclePrimaryRect)
    {
        canvas.DrawText(body.X, body.Y, "SYSTEM & EXECUTION TIER", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 18, $"Tier Mode: {view.ExecutionTierLabel}", HostOverlayPalette.TextPrimary, 1);
        canvas.DrawText(body.X, body.Y + 34, "Runtime: Static Recompilation Host", HostOverlayPalette.TextMuted, 1);
        canvas.DrawText(body.X, body.Y + 50, HostOverlayLayoutHelpers.FitText($"Module: {descriptor.ModuleTitle}", body.Width, 1), HostOverlayPalette.TextMuted, 1);

        cyclePrimaryRect = new Rectangle(body.X, body.Y + 70, Math.Min(220, body.Width), 22);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, cyclePrimaryRect, "Cycle Tier Mode", false, HostOverlayPalette.AccentCyan);

        var skipRect = new Rectangle(body.X, body.Y + 98, Math.Min(220, body.Width), 22);
        HostOverlayLayoutHelpers.DrawPillButton(canvas, skipRect, "Skip Cutscene", false, HostOverlayPalette.AccentWarning);
        controls.Add(new("System_SkipCutscene", "Button", "Skip Cutscene", skipRect, _ => view.EventCallbacks?.OnCutsceneSkipRequested()));

        RenderExtraRows(canvas, body, body.Y + 128, HostDrawerTabId.System, descriptor.ExtraRows, view, controls);
    }

    public static void RenderDiscMediaTab(
        PixelOverlayCanvas canvas, Rectangle body, F1SettingsDrawerView view,
        HostDrawerDescriptor descriptor, List<DrawerControl> controls)
    {
        canvas.DrawText(body.X, body.Y, "DISC / MEDIA", HostOverlayPalette.AccentCyan, 1);
        canvas.DrawText(body.X, body.Y + 22, HostOverlayLayoutHelpers.FitText($"Media: {descriptor.MediaLabel}", body.Width, 1), HostOverlayPalette.TextPrimary, 1);
        canvas.DrawText(body.X, body.Y + 40, "Storage: gitignored data\\rom\\", HostOverlayPalette.TextMuted, 1);
        RenderExtraRows(canvas, body, body.Y + 66, HostDrawerTabId.DiscMedia, descriptor.ExtraRows, view, controls);
    }

    public static void RenderExtraRows(
        PixelOverlayCanvas canvas, Rectangle body, int startY, HostDrawerTabId tab,
        IReadOnlyList<HostDrawerRowDescriptor>? rows, F1SettingsDrawerView view, List<DrawerControl> controls)
    {
        if (rows is null) return;
        int y = startY;
        foreach (HostDrawerRowDescriptor row in rows)
        {
            if (row.Tab != tab) continue;
            string text = string.IsNullOrWhiteSpace(row.Value) ? row.Label : $"{row.Label}: {row.Value}";
            if (row.IsInteractive)
            {
                if (y + 22 > body.Bottom) continue;
                var rowRect = new Rectangle(body.X, y, body.Width, 22);
                HostOverlayLayoutHelpers.DrawPillButton(canvas, rowRect, text, false, HostOverlayPalette.AccentCyan);
                HostDrawerRowDescriptor captured = row;
                controls.Add(new($"ExtraRow_{row.RowId}", "Button", text, rowRect, isRight => view.EventCallbacks?.OnExtraRowActivated(captured.RowId, isRight)));
                y += 26;
            }
            else
            {
                if (y + 16 > body.Bottom) continue;
                canvas.DrawText(body.X, y, HostOverlayLayoutHelpers.FitText(text, body.Width, 1), HostOverlayPalette.TextMuted, 1);
                y += 16;
            }
        }
    }

    private static void AddToggle(
        PixelOverlayCanvas canvas, Rectangle rect, string id, string label, bool val, RgbaColor accent,
        List<DrawerControl> controls, Action<bool> onClick)
    {
        HostOverlayLayoutHelpers.DrawToggleRow(canvas, rect, label, val, accent);
        controls.Add(new(id, "CheckBox", label, rect, onClick));
    }

    private static bool IsOverscanEnabled(HostOverscanCrop crop) =>
        crop.Top > 0 || crop.Bottom > 0 || crop.Left > 0 || crop.Right > 0;

    private static string FormatOverscan(HostOverscanCrop crop) =>
        crop.Top == crop.Bottom && crop.Top == crop.Left && crop.Top == crop.Right
            ? $"{crop.Top}px"
            : $"{crop.Top}/{crop.Bottom}/{crop.Left}/{crop.Right}";
}
