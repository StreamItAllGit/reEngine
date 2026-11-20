// @module: reEngine.Host.Contracts.HostSessionExtensionsContracts
// @purpose: Aspect ratio modes, overscan crop, 4-port gamepad snapshots, automation bridge, and live F1/hotkey callback contracts.
// @depends-on: System, System.Collections.Generic
// @exports: HostAspectRatioMode, HostAspectRatioModeCodec, HostOverscanCrop, HostViewportRect, HostGamepadPortSnapshot, IHostAutomationBridge, IHostSessionEventCallbacks

namespace reEngine.Host.Contracts;

/// <summary>
/// Canonical host presentation aspect ratio modes (Standard 18: <c>aspectRatioMode</c>).
/// </summary>
public enum HostAspectRatioMode
{
    NativePar = 0,
    FourByThree = 1,
    SquareInteger = 2,
    NativeFit = 3,
    HorPlus16By9 = 4,
    HorPlus21By9 = 5
}

/// <summary>
/// Normalizes and formats <see cref="HostAspectRatioMode"/> values per Standard 18.
/// </summary>
public static class HostAspectRatioModeCodec
{
    public static HostAspectRatioMode Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return HostAspectRatioMode.NativePar;
        return raw.Trim().ToLowerInvariant() switch
        {
            "fourbythree" or "4:3" or "4x3" => HostAspectRatioMode.FourByThree,
            "squareinteger" or "integer" or "1:1" => HostAspectRatioMode.SquareInteger,
            "nativefit" or "fit" => HostAspectRatioMode.NativeFit,
            "horplus16by9" or "16:9" or "widescreen" => HostAspectRatioMode.HorPlus16By9,
            "horplus21by9" or "21:9" or "ultrawide" => HostAspectRatioMode.HorPlus21By9,
            _ => HostAspectRatioMode.NativePar
        };
    }

    public static bool TryParseStrict(string? raw, out HostAspectRatioMode mode)
    {
        mode = HostAspectRatioMode.NativePar;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "nativepar":
            case "native":
                mode = HostAspectRatioMode.NativePar;
                return true;
            case "fourbythree":
            case "4:3":
                mode = HostAspectRatioMode.FourByThree;
                return true;
            case "squareinteger":
            case "integer":
                mode = HostAspectRatioMode.SquareInteger;
                return true;
            case "nativefit":
                mode = HostAspectRatioMode.NativeFit;
                return true;
            case "horplus16by9":
            case "16:9":
                mode = HostAspectRatioMode.HorPlus16By9;
                return true;
            case "horplus21by9":
            case "21:9":
            case "ultrawide":
                mode = HostAspectRatioMode.HorPlus21By9;
                return true;
            default:
                return false;
        }
    }

    public static string ToCanonicalId(HostAspectRatioMode mode) => mode switch
    {
        HostAspectRatioMode.FourByThree => "fourByThree",
        HostAspectRatioMode.SquareInteger => "squareInteger",
        HostAspectRatioMode.NativeFit => "nativeFit",
        HostAspectRatioMode.HorPlus16By9 => "horPlus16By9",
        HostAspectRatioMode.HorPlus21By9 => "horPlus21By9",
        _ => "nativePar"
    };

    public static string ToDisplayLabel(HostAspectRatioMode mode) => mode switch
    {
        HostAspectRatioMode.FourByThree => "4:3",
        HostAspectRatioMode.SquareInteger => "Square Int",
        HostAspectRatioMode.NativeFit => "Native Fit",
        HostAspectRatioMode.HorPlus16By9 => "16:9 Hor+",
        HostAspectRatioMode.HorPlus21By9 => "21:9 Hor+",
        _ => "Native PAR"
    };

    public static HostAspectRatioMode CycleNext(HostAspectRatioMode current) => current switch
    {
        HostAspectRatioMode.NativePar => HostAspectRatioMode.FourByThree,
        HostAspectRatioMode.FourByThree => HostAspectRatioMode.SquareInteger,
        HostAspectRatioMode.SquareInteger => HostAspectRatioMode.NativeFit,
        HostAspectRatioMode.NativeFit => HostAspectRatioMode.HorPlus16By9,
        HostAspectRatioMode.HorPlus16By9 => HostAspectRatioMode.HorPlus21By9,
        _ => HostAspectRatioMode.NativePar
    };
}

/// <summary>
/// Pixel edge overscan trim counts (Standard 18: <c>overscanCropPx</c>).
/// </summary>
public readonly record struct HostOverscanCrop(int Top, int Bottom, int Left, int Right)
{
    public static HostOverscanCrop None => default;

    public HostOverscanCrop ClampForContent(int contentWidth, int contentHeight)
    {
        int w = Math.Max(1, contentWidth);
        int h = Math.Max(1, contentHeight);
        int top = Math.Clamp(Top, 0, Math.Max(0, h - 2));
        int bottom = Math.Clamp(Bottom, 0, Math.Max(0, h - top - 1));
        int left = Math.Clamp(Left, 0, Math.Max(0, w - 2));
        int right = Math.Clamp(Right, 0, Math.Max(0, w - left - 1));
        return new HostOverscanCrop(top, bottom, left, right);
    }
}

/// <summary>
/// Integer rectangular region in pixel coordinates for viewport blitting and inverse pointer mapping.
/// </summary>
public readonly record struct HostViewportRect(int X, int Y, int Width, int Height);

/// <summary>
/// Per-port SDL3 gamepad snapshot carried on <see cref="HostInputFrame.PadPorts"/> (Ports 0..3).
/// </summary>
public readonly record struct HostGamepadPortSnapshot(
    int PortIndex,
    bool IsConnected,
    HostGamepadButtons Buttons,
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    float TriggerLeft,
    float TriggerRight,
    uint RawButtonsMask);

/// <summary>
/// Decoupled automation loopback bridge interface injected solely by Debug/Test host processes.
/// </summary>
public interface IHostAutomationBridge : IDisposable
{
    void Start(IHostAutomationTarget target, int port, string? token, string? allowedSnapshotRoot);
    void DrainPendingCommands();
    void SyncWidgets(IReadOnlyList<HostWidgetBounds> widgets);
}

/// <summary>
/// Live session event callbacks invoked when <c>F1</c> controls, scaling, or host hotkeys change state.
/// </summary>
public interface IHostSessionEventCallbacks
{
    void OnAspectRatioChanged(HostAspectRatioMode mode) { }
    void OnOverscanCropChanged(HostOverscanCrop crop) { }
    void OnFullscreenChanged(bool isFullscreen) { }
    void OnFastForwardChanged(bool isEnabled) { }
    void OnCutsceneSkipRequested() { }
    void OnQuickSaveRequested() { }
    void OnQuickLoadRequested() { }
    void OnExtraRowActivated(string rowId, bool isSecondaryAction) { }
    void OnControlOptionChanged(string optionId, string selectedChoice) { }
    void OnDisplayModeChanged(HostDisplayMode mode) { }
    void OnMonitorIndexChanged(int monitorIndex) { }
    void OnVsyncChanged(bool isEnabled) { }
    void OnVideoFilterChanged(HostVideoFilterMode mode) { }
    void OnTrueColorChanged(bool enabled) { }
    void OnDitheringChanged(bool enabled) { }
    void OnHudAnchorChanged(HostHudAnchorMode mode) { }
    void OnScreenshotModeChanged(HostScreenshotCaptureMode mode) { }
    void OnScreenshotCaptured(HostScreenshotResult result) { }
    void OnAudioSettingsChanged(HostAudioMixerSettings settings) { }
    void OnAudioDeviceChanged(string? deviceName) { }
    void OnExecutionTierChanged(string executionTierMode, bool isJitEnabled) { }
    void OnPreloadMediaToRamChanged(bool enabled) { }
    void OnFastBootChanged(bool enabled) { }
    void OnSafeModeResetRequested() { }
    void OnSoundTestActionRequested(string action, string? trackId = null) { }
}
