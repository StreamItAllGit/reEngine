// @module: reEngine.Host.Settings.HostSettingsStore
// @purpose: Host adapter over Standard 19 SettingsDomainStore mapping video/audio contracts, live host updates, and in-memory Safe Mode recovery.
// @depends-on: System, reEngine.Core.Execution, reEngine.Core.Updates, reEngine.Host.Contracts
// @exports: HostSettingsStore

using reEngine.Core.Execution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.Host.Settings;

/// <summary>
/// Host-layer facade implementing <see cref="IReEngineSettingsStore"/> over <see cref="SettingsDomainStore"/> with live contract mapping.
/// </summary>
public sealed class HostSettingsStore : IReEngineSettingsStore
{
    private readonly SettingsDomainStore _domainStore;

    /// <summary>Initializes a new <see cref="HostSettingsStore"/> bound to <paramref name="dataDirectory"/>.</summary>
    /// <param name="dataDirectory">Application or module data directory root.</param>
    /// <param name="coreConfigPathOverride">Optional explicit rooted override path for <c>settings.json</c>.</param>
    public HostSettingsStore(string dataDirectory, string? coreConfigPathOverride = null)
    {
        _domainStore = new SettingsDomainStore(dataDirectory, coreConfigPathOverride);
    }

    /// <inheritdoc />
    public string ConfigDirectory => _domainStore.ConfigDirectory;

    /// <summary>Gets the resolved data directory root.</summary>
    public string DataDirectory => _domainStore.DataDirectory;

    /// <summary>Gets the underlying <see cref="SettingsDomainStore"/> instance.</summary>
    public SettingsDomainStore DomainStore => _domainStore;

    /// <inheritdoc />
    public ReEngineSettingsBundle LoadOrMigrate() => _domainStore.LoadOrMigrate();

    /// <inheritdoc />
    public void SaveAll(ReEngineSettingsBundle bundle) => _domainStore.SaveAll(bundle);

    /// <inheritdoc />
    public void SaveCore(ReEngineSettings core) => _domainStore.SaveCore(core);

    /// <inheritdoc />
    public void SaveVideo(ReEngineVideoSettings video) => _domainStore.SaveVideo(video);

    /// <inheritdoc />
    public void SaveAudio(ReEngineAudioSettings audio) => _domainStore.SaveAudio(audio);

    /// <summary>Maps persisted <see cref="ReEngineAudioSettings"/> into an immutable <see cref="HostAudioMixerSettings"/> snapshot.</summary>
    public HostAudioMixerSettings ToHostAudioMixerSettings(ReEngineAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return new HostAudioMixerSettings
        {
            AudioOutputDevice = string.IsNullOrWhiteSpace(audio.AudioOutputDevice) ? null : audio.AudioOutputDevice.Trim(),
            MasterVolume = Clamp01(audio.MasterVolume, 0.85),
            IsAudioMuted = audio.IsAudioMuted,
            BgmVolume = Clamp01(audio.BgmVolume, 1.0),
            SfxVolume = Clamp01(audio.SfxVolume, 1.0),
            VoiceVolume = Clamp01(audio.VoiceVolume, 1.0),
            ShouldMuteOnFocusLoss = audio.ShouldMuteOnFocusLoss,
            AudioLowPassHz = Math.Max(0, audio.AudioLowPassHz),
            AudioHighPassHz = Math.Max(0, audio.AudioHighPassHz),
            AudioStereoWidthPercent = double.IsFinite(audio.AudioStereoWidthPercent)
                ? Math.Clamp(audio.AudioStereoWidthPercent, 0.0, 200.0)
                : 100.0,
            ResamplingMode = HostAudioResamplingModeCodec.Parse(audio.AudioResamplingMode),
            FastForwardAudioMode = HostFastForwardAudioModeCodec.Parse(audio.FastForwardAudioMode)
        };
    }

    /// <summary>Maps persisted <see cref="ReEngineVideoSettings"/> and <see cref="ReEngineSettings"/> into <see cref="HostVideoPresentationState"/>.</summary>
    public HostVideoPresentationState ToHostVideoState(ReEngineVideoSettings video, ReEngineSettings core)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(core);
        HostDisplayMode displayMode = video.IsFullscreen
            ? (HostDisplayModeCodec.Parse(video.DisplayMode) == HostDisplayMode.ExclusiveFullscreen
                ? HostDisplayMode.ExclusiveFullscreen
                : HostDisplayMode.BorderlessFullscreen)
            : HostDisplayMode.Windowed;

        double ffMult = double.IsFinite(core.FastForwardMultiplier) && core.FastForwardMultiplier > 0
            ? Math.Clamp(core.FastForwardMultiplier, 2.0, 4.0)
            : 2.0;

        return new HostVideoPresentationState
        {
            DisplayMode = displayMode,
            MonitorIndex = Math.Max(0, video.MonitorIndex),
            IsVsyncEnabled = video.IsVsyncEnabled,
            TargetFps = Math.Max(0, video.TargetFps),
            BackgroundFpsCap = Math.Max(0, video.BackgroundFpsCap),
            FastForwardMultiplier = ffMult,
            HudAnchorMode = HostHudAnchorModeCodec.Parse(video.HudAnchorMode),
            VideoFilterMode = HostVideoFilterModeCodec.Parse(video.VideoFilterMode),
            IsTrueColor24BitEnabled = video.IsTrueColor24BitEnabled,
            IsDitheringEnabled = video.IsDitheringEnabled,
            ScreenshotCaptureMode = HostScreenshotCaptureModeCodec.Parse(video.ScreenshotCaptureMode),
            IsFmvActive = false
        };
    }

    /// <summary>Builds <see cref="HostExecutionResilienceOptions"/> from persisted core settings and optional Safe Mode state.</summary>
    public HostExecutionResilienceOptions ToExecutionResilienceOptions(
        ReEngineSettings core,
        string? moduleId = null,
        bool isSafeModeTriggered = false)
    {
        ArgumentNullException.ThrowIfNull(core);
        string effectiveMode = ExecutionTierPolicy.NormalizeForPersistence(core.IsJitEnabled, core.ExecutionTierMode);
        return new HostExecutionResilienceOptions
        {
            DataDirectory = _domainStore.DataDirectory,
            ModuleId = string.IsNullOrWhiteSpace(moduleId) ? "host-demo" : moduleId.Trim(),
            IsJitEnabled = core.IsJitEnabled,
            ExecutionTierMode = effectiveMode,
            ShouldPreloadMediaToRam = core.ShouldPreloadMediaToRam,
            IsFastBootEnabled = core.IsFastBootEnabled,
            IsSafeModeActive = core.IsSafeModeActive || isSafeModeTriggered,
            ShouldFastLoadData = core.ShouldFastLoadData,
            FastForwardMultiplier = double.IsFinite(core.FastForwardMultiplier) && core.FastForwardMultiplier > 0
                ? Math.Clamp(core.FastForwardMultiplier, 2.0, 4.0)
                : 2.0
        };
    }

    /// <summary>
    /// Applies host runtime changes onto <paramref name="bundle"/>, enforces domain invariants, and optionally persists atomically.
    /// </summary>
    public ReEngineSettingsBundle ApplyHostChanges(
        ReEngineSettingsBundle bundle,
        HostVideoPresentationState? videoState = null,
        HostAudioMixerSettings? audioState = null,
        bool? isFullscreen = null,
        string? backendId = null,
        int? resolutionScale = null,
        HostAspectRatioMode? aspectRatioMode = null,
        HostOverscanCrop? overscanCrop = null,
        bool? isFastForwardEnabled = null,
        bool? isHdTexturePackEnabled = null,
        string? audioDriver = null,
        bool? isJitEnabled = null,
        string? executionTierMode = null,
        bool? shouldPreloadMediaToRam = null,
        bool? isFastBootEnabled = null,
        bool? isSafeModeActive = null,
        bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ApplyVideoUpdates(bundle.Video, bundle.Core, videoState, isFullscreen, backendId, resolutionScale, aspectRatioMode, overscanCrop, isFastForwardEnabled, isHdTexturePackEnabled);
        ApplyAudioUpdates(bundle.Audio, audioState, audioDriver);
        ApplyCoreUpdates(bundle.Core, isJitEnabled, executionTierMode, shouldPreloadMediaToRam, isFastBootEnabled, isSafeModeActive);
        if (persist) _domainStore.SaveAll(bundle);
        return bundle;
    }

    /// <summary>Clears any uncleared crash sentinel and resets persisted <c>isSafeModeActive</c> to <c>false</c>.</summary>
    public ReEngineSettingsBundle ClearSafeMode()
    {
        ReEngineSettingsBundle bundle = _domainStore.LoadOrMigrate();
        return ClearSafeMode(bundle, persist: true);
    }

    /// <summary>Clears any uncleared crash sentinel and resets <c>isSafeModeActive</c> on <paramref name="bundle"/>.</summary>
    public ReEngineSettingsBundle ClearSafeMode(ReEngineSettingsBundle bundle, bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _ = new BootCrashSentinel(_domainStore.DataDirectory).Clear();
        bundle.Core.IsSafeModeActive = false;
        if (persist) _domainStore.SaveCore(bundle.Core);
        return bundle;
    }

    /// <summary>
    /// Applies Safe Mode overrides strictly in memory (disabling HD packs and unsafe GPU backends without touching saved disk config).
    /// </summary>
    public static HostSessionOptions ApplyInMemorySafeMode(HostSessionOptions options, BootCrashCheckResult? crashCheck = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool triggered = crashCheck?.IsSafeModeTriggered == true || options.ExecutionResilience?.IsSafeModeActive == true;
        if (!triggered) return options;

        HostRenderBackendKind safeBackend = options.PreferredBackend is HostRenderBackendKind.Vulkan or HostRenderBackendKind.DirectX
            ? HostRenderBackendKind.OpenGl
            : options.PreferredBackend;

        HostExecutionResilienceOptions resilience = (options.ExecutionResilience ?? new HostExecutionResilienceOptions()) with
        {
            IsSafeModeActive = true
        };

        return options with
        {
            IsHdTexturePackEnabled = false,
            PreferredBackend = safeBackend,
            ExecutionResilience = resilience
        };
    }

    private static void ApplyVideoUpdates(
        ReEngineVideoSettings video, ReEngineSettings core, HostVideoPresentationState? state,
        bool? isFullscreen, string? backendId, int? resolutionScale, HostAspectRatioMode? aspectRatioMode,
        HostOverscanCrop? overscanCrop, bool? isFastForwardEnabled, bool? isHdTexturePackEnabled)
    {
        if (state is not null)
        {
            video.DisplayMode = HostDisplayModeCodec.ToCanonicalId(state.DisplayMode);
            video.IsFullscreen = state.DisplayMode != HostDisplayMode.Windowed;
            video.MonitorIndex = Math.Max(0, state.MonitorIndex);
            video.IsVsyncEnabled = state.IsVsyncEnabled;
            video.TargetFps = Math.Max(0, state.TargetFps);
            video.BackgroundFpsCap = Math.Max(0, state.BackgroundFpsCap);
            video.HudAnchorMode = HostHudAnchorModeCodec.ToCanonicalId(state.HudAnchorMode);
            video.VideoFilterMode = HostVideoFilterModeCodec.ToCanonicalId(state.VideoFilterMode);
            video.IsTrueColor24BitEnabled = state.IsTrueColor24BitEnabled;
            video.IsDitheringEnabled = state.IsDitheringEnabled;
            video.ScreenshotCaptureMode = HostScreenshotCaptureModeCodec.ToCanonicalId(state.ScreenshotCaptureMode);
            if (double.IsFinite(state.FastForwardMultiplier) && state.FastForwardMultiplier > 0)
                core.FastForwardMultiplier = Math.Clamp(state.FastForwardMultiplier, 2.0, 4.0);
        }

        if (isFullscreen.HasValue) video.IsFullscreen = isFullscreen.Value;
        video.DisplayMode = video.IsFullscreen
            ? (string.Equals(video.DisplayMode, "exclusiveFullscreen", StringComparison.OrdinalIgnoreCase) ? "exclusiveFullscreen" : "borderlessFullscreen")
            : "windowed";

        if (!string.IsNullOrWhiteSpace(backendId)) video.BackendId = backendId.Trim();
        if (resolutionScale is int sc && sc is 1 or 2 or 4 or 8) video.ResolutionScale = sc;
        if (aspectRatioMode.HasValue) video.AspectRatioMode = HostAspectRatioModeCodec.ToCanonicalId(aspectRatioMode.Value);
        if (overscanCrop is HostOverscanCrop crop)
            video.OverscanCropPx = new OverscanCropPxSettings { Top = Math.Max(0, crop.Top), Bottom = Math.Max(0, crop.Bottom), Left = Math.Max(0, crop.Left), Right = Math.Max(0, crop.Right) };
        if (isFastForwardEnabled.HasValue) video.IsFastForwardEnabled = isFastForwardEnabled.Value;
        if (isHdTexturePackEnabled.HasValue) video.IsHdTexturePackEnabled = isHdTexturePackEnabled.Value;
    }

    private static void ApplyAudioUpdates(ReEngineAudioSettings audio, HostAudioMixerSettings? state, string? audioDriver)
    {
        if (state is not null)
        {
            audio.AudioOutputDevice = string.IsNullOrWhiteSpace(state.AudioOutputDevice) ? null : state.AudioOutputDevice.Trim();
            audio.MasterVolume = Clamp01(state.MasterVolume, 0.85);
            audio.IsAudioMuted = state.IsAudioMuted;
            audio.BgmVolume = Clamp01(state.BgmVolume, 1.0);
            audio.SfxVolume = Clamp01(state.SfxVolume, 1.0);
            audio.VoiceVolume = Clamp01(state.VoiceVolume, 1.0);
            audio.ShouldMuteOnFocusLoss = state.ShouldMuteOnFocusLoss;
            audio.AudioLowPassHz = Math.Max(0, state.AudioLowPassHz);
            audio.AudioHighPassHz = Math.Max(0, state.AudioHighPassHz);
            audio.AudioStereoWidthPercent = double.IsFinite(state.AudioStereoWidthPercent) ? Math.Clamp(state.AudioStereoWidthPercent, 0.0, 200.0) : 100.0;
            audio.AudioResamplingMode = HostAudioResamplingModeCodec.ToCanonicalId(state.ResamplingMode);
            audio.FastForwardAudioMode = HostFastForwardAudioModeCodec.ToCanonicalId(state.FastForwardAudioMode);
        }

        if (audioDriver is not null) audio.AudioDriver = string.IsNullOrWhiteSpace(audioDriver) ? null : audioDriver.Trim().ToLowerInvariant();
    }

    private static void ApplyCoreUpdates(
        ReEngineSettings core, bool? isJitEnabled, string? executionTierMode,
        bool? shouldPreloadMediaToRam, bool? isFastBootEnabled, bool? isSafeModeActive)
    {
        if (isJitEnabled.HasValue) core.IsJitEnabled = isJitEnabled.Value;
        if (!string.IsNullOrWhiteSpace(executionTierMode)) core.ExecutionTierMode = executionTierMode;
        ExecutionTierPolicy.EnforcePersistenceInvariants(core);
        if (shouldPreloadMediaToRam.HasValue) core.ShouldPreloadMediaToRam = shouldPreloadMediaToRam.Value;
        if (isFastBootEnabled.HasValue) core.IsFastBootEnabled = isFastBootEnabled.Value;
        if (isSafeModeActive.HasValue) core.IsSafeModeActive = isSafeModeActive.Value;
    }

    private static double Clamp01(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : fallback;
}
