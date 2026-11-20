// @module: reEngine.Host.Core.HostSessionParityHelper
// @purpose: Integrates v0.6.1 R08 parity (options sync, controls persistence, controller nav, non-quitting Esc, relative mouse, live backend switch, F3 telemetry, drop).
// @depends-on: System, Foundation.Core, reEngine.Host.Contracts, reEngine.Host.Input, reEngine.Host.Ui
// @exports: HostSessionParityHelper

using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Ui;

namespace reEngine.Host.Core;

internal static class HostSessionParityHelper
{
    public static string? ConfigureInitialState(
        HostSessionOptions options,
        HostInputMapper mapper,
        F1SettingsDrawerView drawer,
        FirstRunMediaGateView mediaGate,
        F3HudOverlayView hud,
        HostAotCompileOverlayView aotOverlay,
        HostRelativeMouseController relativeMouse,
        HostRenderBackendKind activeBackend)
    {
        drawer.UiTextScale = options.UiTextScale;
        drawer.FrameRateMode = options.FrameRateMode;
        drawer.TargetFps = options.TargetFps <= 0 ? 0 : (int)Math.Round(options.TargetFps);
        drawer.ControlMode = options.ControlMode;
        drawer.MouseSensitivity = options.MouseSensitivity;
        drawer.MenuControllerChord = options.MenuControllerChord;
        drawer.IsCleanCaptureModeEnabled = options.IsCleanCaptureModeEnabled;
        drawer.ShouldEnableRuntimeLogs = options.ShouldEnableRuntimeLogs;
        drawer.FastForwardMultiplier = options.FastForwardMultiplier;
        drawer.SelectedBackend = activeBackend;

        mapper.ControlTiers.ControlMode = options.ControlMode;
        mapper.ControlTiers.MouseSensitivity = options.MouseSensitivity;
        mapper.ControlTiers.MenuControllerChord = options.MenuControllerChord;

        if (!string.IsNullOrWhiteSpace(options.ControlsConfigPath))
            _ = mapper.ControlTiers.LoadFromFile(options.ControlsConfigPath);

        mapper.ActiveSchema = mapper.ControlTiers.ActiveSchema;
        mapper.ControlMode = mapper.ControlTiers.ControlMode;
        mapper.MouseSensitivity = mapper.ControlTiers.MouseSensitivity;
        mapper.MenuControllerChord = mapper.ControlTiers.MenuControllerChord;

        drawer.SelectedSchema = mapper.ActiveSchema;
        drawer.ControlMode = mapper.ControlMode;
        drawer.MouseSensitivity = mapper.MouseSensitivity;
        drawer.MenuControllerChord = mapper.MenuControllerChord;
        drawer.ControlTierStore = mapper.ControlTiers;

        if (options.OpenSettingsInitially) drawer.IsOpen = true;
        if (options.ShowFpsInitially)
        {
            drawer.IsFpsOverlayVisible = true;
            hud.IsVisible = true;
        }

        mediaGate.UiTextScale = drawer.UiTextScale;
        aotOverlay.UiTextScale = drawer.UiTextScale;
        hud.IsCleanCaptureModeEnabled = drawer.IsCleanCaptureModeEnabled;

        Func<string?> mediaPicker = options.OnBrowseMediaRequested ?? (() => HostNativeFileDialog.ShowSelectMediaDialog());
        Func<string?> modulePicker = options.OnBrowseModuleRequested ?? (() => HostNativeFileDialog.ShowSelectModuleDialog());
        mediaGate.OnBrowseRequested ??= mediaPicker;
        drawer.OnBrowseMediaRequested ??= mediaPicker;
        mediaGate.OnBrowseModuleRequested ??= modulePicker;
        drawer.OnBrowseModuleRequested ??= modulePicker;
        mediaGate.OnFileDropped ??= options.OnFileDropped;
        mediaGate.OnDemoCartRequested ??= options.OnDemoCartRequested;

        _ = relativeMouse.OnControlModeChanged(drawer.ControlMode);
        return mapper.ControlTiers.SerializeDirtyCheckJson();
    }

    public static void ApplyNonQuittingEscape(
        F1SettingsDrawerView drawer,
        HostInputMapper mapper,
        HostRelativeMouseController relativeMouse,
        ref bool swallowNextEscapeHotkey)
    {
        if (swallowNextEscapeHotkey)
        {
            swallowNextEscapeHotkey = false;
            return;
        }

        if (drawer.IsOpen && drawer.ActiveTab == HostDrawerTabId.Keybinds && drawer.KeybindsTab.IsCapturing)
        {
            drawer.KeybindsTab.ClearCapture();
            return;
        }

        if (drawer.IsOpen)
        {
            drawer.IsOpen = false;
            drawer.KeybindsTab.ClearCapture();
            _ = relativeMouse.ReleaseOnOverlayOpen();
            return;
        }

        if (mapper.IsEscapeBoundInPerGameModern()) return;
        _ = relativeMouse.TryReleaseOnEscape();
    }

    public static bool SyncLiveFrameState(
        HostSessionOptions options,
        F1SettingsDrawerView drawer,
        FirstRunMediaGateView mediaGate,
        F3HudOverlayView hud,
        HostAotCompileOverlayView aotOverlay,
        HostInputMapper mapper,
        HostInputFrame input,
        HostDrawerControllerNav controllerNav,
        HostRelativeMouseController relativeMouse,
        HostRgbaSurfaceBackend surface,
        Sdl3HostWindow? activeWindow,
        ref IHostGraphicsContext? activeGraphicsContext,
        ref HostRenderBackendKind activeBackend,
        ref bool prevWindowFocused,
        string? controlsConfigPath,
        ref string? lastControlsJson)
    {
        if (mediaGate.IsVisible) _ = controllerNav.TryProcessMediaGate(input, mediaGate);
        else if (drawer.IsOpen) _ = controllerNav.TryProcessFrame(input, drawer, mapper);

        mediaGate.UiTextScale = drawer.UiTextScale;
        aotOverlay.UiTextScale = drawer.UiTextScale;
        hud.IsCleanCaptureModeEnabled = drawer.IsCleanCaptureModeEnabled;
        hud.IsVisible = drawer.IsFpsOverlayVisible;

        mapper.ControlMode = drawer.ControlMode;
        mapper.MouseSensitivity = drawer.MouseSensitivity;
        mapper.MenuControllerChord = drawer.MenuControllerChord;
        _ = relativeMouse.OnControlModeChanged(drawer.ControlMode);

        if (drawer.IsOpen || mediaGate.IsVisible) _ = relativeMouse.ReleaseOnOverlayOpen();

        if (activeWindow is not null)
        {
            bool focused = activeWindow.IsWindowFocused;
            if (prevWindowFocused && !focused) _ = relativeMouse.ReleaseOnFocusLoss();
            prevWindowFocused = focused;

            if (drawer.SelectedBackend != activeBackend)
            {
                HostRenderBackendKind requested = drawer.SelectedBackend;
                bool switched = activeWindow.TrySwitchBackend(options, requested, out _, out string diag);
                activeBackend = activeWindow.ActiveBackendKind;
                drawer.SelectedBackend = activeBackend;
                drawer.FallbackBanner = (!switched || activeBackend != requested) ? diag : null;
                surface.UseParallelBandBlit = activeBackend == HostRenderBackendKind.ParallelCpu;
                activeGraphicsContext = activeWindow.GraphicsContext;
            }
        }

        if (!string.IsNullOrWhiteSpace(controlsConfigPath))
        {
            string currentJson = mapper.ControlTiers.SerializeDirtyCheckJson();
            if (!string.Equals(currentJson, lastControlsJson, StringComparison.Ordinal))
            {
                mapper.ControlTiers.SaveToFile(controlsConfigPath);
                lastControlsJson = currentJson;
            }
        }

        return drawer.QuitRequested;
    }

    public static void HandlePointerClickOutcome(
        bool consumedByOverlay,
        bool isRightClick,
        F1SettingsDrawerView drawer,
        FirstRunMediaGateView mediaGate,
        Sdl3HostWindow? activeWindow,
        HostRelativeMouseController relativeMouse)
    {
        if (consumedByOverlay || isRightClick) return;
        bool focused = activeWindow?.IsWindowFocused ?? true;
        _ = relativeMouse.TryCaptureOnViewportClick(drawer.ControlMode, drawer.IsOpen || mediaGate.IsVisible, focused);
    }

    public static void HandleFileDropped(string droppedPath, FirstRunMediaGateView mediaGate, F1SettingsDrawerView drawer)
    {
        if (string.IsNullOrWhiteSpace(droppedPath)) return;
        _ = mediaGate.HandleDroppedFile(droppedPath);
        drawer.EventCallbacks?.OnFileDropped(droppedPath);
    }

    public static double ResolveEffectiveBaseFps(F1SettingsDrawerView drawer) =>
        drawer.FrameRateMode == HostFrameRateMode.Original
            ? (drawer.TargetFps > 0 && drawer.TargetFps < 60 ? drawer.TargetFps : 60.0)
            : (drawer.TargetFps <= 0 ? 0.0 : drawer.TargetFps);

    public static F3HudTelemetry EnrichHudTelemetry(F3HudTelemetry telemetry, F1SettingsDrawerView drawer)
    {
        string badge = drawer.FrameRateMode == HostFrameRateMode.Original
            ? "[ORIG]"
            : (drawer.TargetFps <= 0 ? "[MAX]" : "[ENH]");
        return telemetry with
        {
            FrameRateModeBadge = badge,
            FastForwardMultiplier = drawer.FastForwardMultiplier,
            IsCleanCaptureModeEnabled = drawer.IsCleanCaptureModeEnabled,
            UiTextScale = drawer.UiTextScale
        };
    }
}
