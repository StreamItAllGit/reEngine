// @module: reEngine.Host.Core.HostSessionOverlayAndSettingsHelper
// @purpose: Thread-safe live AOT/MediaGate/Descriptor state queueing plus Standard 19 HostSettingsStore startup hydration and dirty-save persistence.
// @depends-on: System, System.Globalization, reEngine.Core.Resolution, reEngine.Core.Updates, reEngine.Host.Contracts, reEngine.Host.Rendering, reEngine.Host.Settings, reEngine.Host.Ui
// @exports: HostSessionOverlayAndSettingsHelper

using System.Globalization;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Rendering;
using reEngine.Host.Settings;
using reEngine.Host.Ui;

namespace reEngine.Host.Core;

internal sealed class HostSessionOverlayAndSettingsHelper
{
    private sealed record PendingMediaGateState(bool IsVisible, string? ExpectedSha256, string? StatusMessage, string? ErrorMessage);

    private readonly object _sync = new();
    private HostAotCompileState? _pendingAotState;
    private bool _hasPendingAotState;
    private PendingMediaGateState? _pendingMediaGate;
    private HostDrawerDescriptor? _pendingDescriptor;
    private HostSettingsStore? _settingsStore;
    private ReEngineSettingsBundle? _settingsBundle;
    private string? _lastSettingsFingerprint;

    public void QueueAotCompileState(HostAotCompileState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_sync)
        {
            _pendingAotState = state;
            _hasPendingAotState = true;
        }
    }

    public void QueueMediaGateState(bool isVisible, string? expectedSha256, string? statusMessage, string? errorMessage)
    {
        lock (_sync)
        {
            _pendingMediaGate = new PendingMediaGateState(isVisible, expectedSha256, statusMessage, errorMessage);
        }
    }

    public void QueueDrawerDescriptor(HostDrawerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_sync)
        {
            _pendingDescriptor = descriptor;
        }
    }

    public HostSessionOptions HydrateInitialState(HostSessionOptions options, F1SettingsDrawerView drawer, HostRgbaSurfaceBackend surface)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InitialAotCompileState is { } initAot)
        {
            lock (_sync)
            {
                if (!_hasPendingAotState)
                {
                    _pendingAotState = initAot;
                    _hasPendingAotState = true;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(options.SettingsDataDirectory)) return options;

        _settingsStore = new HostSettingsStore(options.SettingsDataDirectory, options.SettingsConfigPathOverride);
        _settingsBundle = _settingsStore.LoadOrMigrate();
        ReEngineVideoSettings vid = _settingsBundle.Video;
        HostVideoPresentationState v = _settingsStore.ToHostVideoState(vid, _settingsBundle.Core);
        HostAudioMixerSettings a = _settingsStore.ToHostAudioMixerSettings(_settingsBundle.Audio);

        HostRenderBackendKind backend = !options.PreferExplicitBackend && HostBackendProbe.TryParseBackendKind(vid.BackendId, out HostRenderBackendKind savedBackend)
            ? savedBackend
            : options.PreferredBackend;

        int scale = !options.PreferExplicitScale && vid.ResolutionScale is 1 or 2 or 4 or 8
            ? vid.ResolutionScale
            : options.DrawerDescriptor.ResolutionScale;
        drawer.ResolutionScale = scale;
        surface.ScaleFactor = scale;

        HostAspectRatioMode aspect = !options.PreferExplicitAspectRatio && HostAspectRatioModeCodec.TryParseStrict(vid.AspectRatioMode, out HostAspectRatioMode savedAspect)
            ? savedAspect
            : options.AspectRatioMode;

        HostOverscanCrop overscan = options.OverscanCropPx == HostOverscanCrop.None && vid.OverscanCropPx is { } sc
            ? new HostOverscanCrop(Math.Max(0, sc.Top), Math.Max(0, sc.Bottom), Math.Max(0, sc.Left), Math.Max(0, sc.Right))
            : options.OverscanCropPx;

        HostExecutionResilienceOptions? resilience = options.ExecutionResilience is not null
            ? options.ExecutionResilience with
            {
                ShouldPreloadMediaToRam = options.ExecutionResilience.ShouldPreloadMediaToRam || _settingsBundle.Core.ShouldPreloadMediaToRam,
                IsFastBootEnabled = options.ExecutionResilience.IsFastBootEnabled || _settingsBundle.Core.IsFastBootEnabled,
                IsSafeModeActive = options.ExecutionResilience.IsSafeModeActive || _settingsBundle.Core.IsSafeModeActive,
                FastForwardMultiplier = v.FastForwardMultiplier
            }
            : null;

        bool fullscreen = options.IsFullscreen || v.DisplayMode != HostDisplayMode.Windowed;
        HostDisplayMode dispMode = fullscreen
            ? (v.DisplayMode == HostDisplayMode.ExclusiveFullscreen ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen)
            : HostDisplayMode.Windowed;

        HostDrawerDescriptor updatedDesc = options.DrawerDescriptor with
        {
            ResolutionScale = scale,
            BackendLabel = HostBackendProbe.GetCanonicalBackendId(backend)
        };

        return options with
        {
            PreferredBackend = backend,
            AspectRatioMode = aspect,
            OverscanCropPx = overscan,
            IsFullscreen = fullscreen,
            DisplayMode = dispMode,
            MonitorIndex = options.MonitorIndex != 0 ? options.MonitorIndex : v.MonitorIndex,
            BackgroundFpsCap = options.BackgroundFpsCap != 0 ? options.BackgroundFpsCap : v.BackgroundFpsCap,
            IsVsyncEnabled = options.IsVsyncEnabled || v.IsVsyncEnabled,
            TargetFps = v.TargetFps >= 0 ? v.TargetFps : options.TargetFps,
            FastForwardMultiplier = v.FastForwardMultiplier,
            HudAnchorMode = v.HudAnchorMode,
            VideoFilterMode = v.VideoFilterMode,
            IsTrueColor24BitEnabled = v.IsTrueColor24BitEnabled,
            IsDitheringEnabled = v.IsDitheringEnabled,
            ScreenshotCaptureMode = v.ScreenshotCaptureMode,
            FrameRateMode = v.FrameRateMode,
            UiTextScale = v.UiTextScale,
            ShowFpsInitially = options.ShowFpsInitially || v.IsFpsOverlayEnabled,
            IsCleanCaptureModeEnabled = options.IsCleanCaptureModeEnabled || v.IsCleanCaptureModeEnabled,
            IsFastForwardEnabled = options.IsFastForwardEnabled || vid.IsFastForwardEnabled,
            IsHdTexturePackEnabled = options.IsHdTexturePackEnabled || vid.IsHdTexturePackEnabled,
            ShouldEnableRuntimeLogs = options.ShouldEnableRuntimeLogs || _settingsBundle.Core.ShouldEnableRuntimeLogs,
            AudioDriverOverride = options.AudioDriverOverride ?? _settingsBundle.Audio.AudioDriver,
            AudioSettings = options.AudioSettings ?? a,
            ExecutionResilience = resilience,
            DrawerDescriptor = updatedDesc
        };
    }

    public static void ApplyInitialMediaGateError(HostSessionOptions options, FirstRunMediaGateView mediaGate)
    {
        if (string.IsNullOrWhiteSpace(options.InitialMediaGateError)) return;
        mediaGate.ErrorMessage = options.InitialMediaGateError;
        mediaGate.StatusMessage = options.InitialMediaGateError;
    }

    public void CaptureBaseline(F1SettingsDrawerView drawer, HostDisplayMode displayMode, HostRenderBackendKind backendKind)
    {
        if (_settingsStore is null) return;
        _lastSettingsFingerprint = BuildFingerprint(drawer, displayMode, backendKind);
    }

    public void ApplyPendingOverlays(HostAotCompileOverlayView aotOverlay, FirstRunMediaGateView mediaGate, ref HostDrawerDescriptor activeDescriptor)
    {
        HostAotCompileState? aot = null;
        bool hasAot = false;
        PendingMediaGateState? mg = null;
        HostDrawerDescriptor? desc = null;
        lock (_sync)
        {
            if (_hasPendingAotState)
            {
                aot = _pendingAotState;
                _hasPendingAotState = false;
                hasAot = true;
            }
            if (_pendingMediaGate is not null)
            {
                mg = _pendingMediaGate;
                _pendingMediaGate = null;
            }
            if (_pendingDescriptor is not null)
            {
                desc = _pendingDescriptor;
                _pendingDescriptor = null;
            }
        }

        if (hasAot && aot is not null) aotOverlay.State = aot;
        if (mg is not null)
        {
            mediaGate.IsVisible = mg.IsVisible;
            if (mg.ExpectedSha256 is not null) mediaGate.ExpectedSha256 = mg.ExpectedSha256;
            if (mg.StatusMessage is not null) mediaGate.StatusMessage = mg.StatusMessage;
            mediaGate.ErrorMessage = mg.ErrorMessage;
        }
        if (desc is not null) activeDescriptor = desc;
    }

    public void PersistIfDirty(F1SettingsDrawerView drawer, HostDisplayMode displayMode, HostRenderBackendKind backendKind, bool isSafeModeActive)
    {
        if (_settingsStore is null || _settingsBundle is null || isSafeModeActive) return;
        string fp = BuildFingerprint(drawer, displayMode, backendKind);
        if (string.Equals(fp, _lastSettingsFingerprint, StringComparison.Ordinal)) return;

        HostDisplayMode effectiveDisplay = drawer.IsFullscreen
            ? (displayMode == HostDisplayMode.ExclusiveFullscreen ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen)
            : HostDisplayMode.Windowed;

        var videoState = new HostVideoPresentationState
        {
            DisplayMode = effectiveDisplay,
            MonitorIndex = _settingsBundle.Video.MonitorIndex,
            IsVsyncEnabled = drawer.IsVsyncEnabled,
            TargetFps = drawer.TargetFps,
            BackgroundFpsCap = _settingsBundle.Video.BackgroundFpsCap,
            FastForwardMultiplier = drawer.FastForwardMultiplier,
            HudAnchorMode = drawer.HudAnchorMode,
            VideoFilterMode = drawer.VideoFilterMode,
            IsTrueColor24BitEnabled = drawer.IsTrueColor24BitEnabled,
            IsDitheringEnabled = drawer.IsDitheringEnabled,
            ScreenshotCaptureMode = drawer.ScreenshotCaptureMode,
            FrameRateMode = drawer.FrameRateMode,
            UiTextScale = drawer.UiTextScale,
            IsFpsOverlayEnabled = drawer.IsFpsOverlayVisible,
            IsCleanCaptureModeEnabled = drawer.IsCleanCaptureModeEnabled
        };

        _settingsStore.ApplyHostChanges(
            _settingsBundle,
            videoState: videoState,
            audioState: drawer.AudioSettings,
            isFullscreen: drawer.IsFullscreen,
            backendId: HostBackendProbe.GetCanonicalBackendId(backendKind),
            resolutionScale: drawer.ResolutionScale,
            aspectRatioMode: drawer.AspectRatioMode,
            overscanCrop: drawer.OverscanCropPx,
            isFastForwardEnabled: drawer.IsFastForwardEnabled,
            isHdTexturePackEnabled: drawer.IsHdTexturePackEnabled,
            shouldPreloadMediaToRam: drawer.ActiveResilience.ShouldPreloadMediaToRam,
            isFastBootEnabled: drawer.ActiveResilience.IsFastBootEnabled,
            shouldEnableRuntimeLogs: drawer.ShouldEnableRuntimeLogs,
            persist: true);
        _lastSettingsFingerprint = fp;
    }

    private static string BuildFingerprint(F1SettingsDrawerView d, HostDisplayMode dm, HostRenderBackendKind bk)
    {
        HostAudioMixerSettings a = d.AudioSettings;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{bk}|{d.ResolutionScale}|{d.AspectRatioMode}|{d.OverscanCropPx}|{d.IsFullscreen}|{dm}|{d.IsVsyncEnabled}|{d.TargetFps}|{d.FrameRateMode}|{d.HudAnchorMode}|{d.VideoFilterMode}|{d.IsTrueColor24BitEnabled}|{d.IsDitheringEnabled}|{d.ScreenshotCaptureMode}|{d.UiTextScale:F2}|{d.IsFpsOverlayVisible}|{d.IsCleanCaptureModeEnabled}|{d.IsFastForwardEnabled}|{d.FastForwardMultiplier:F2}|{d.IsHdTexturePackEnabled}|{d.ShouldEnableRuntimeLogs}|{d.ActiveResilience.ShouldPreloadMediaToRam}|{d.ActiveResilience.IsFastBootEnabled}|{a.AudioOutputDevice}|{a.MasterVolume:F3}|{a.IsAudioMuted}|{a.BgmVolume:F3}|{a.SfxVolume:F3}|{a.VoiceVolume:F3}|{a.ShouldMuteOnFocusLoss}|{a.AudioLowPassHz}|{a.AudioHighPassHz}|{a.AudioStereoWidthPercent:F1}|{a.ResamplingMode}|{a.FastForwardAudioMode}");
    }
}
