// @module: reEngine.Host.Core.HostSessionRuntimeBridge
// @purpose: Integrates BootCrashSentinel Safe Mode, ExecutionTierPolicy, MediaRamPreloader, Audio DSP/SoundTest sync, and F3 HUD telemetry into HostSessionRunner.
// @depends-on: System, System.IO, Foundation.Core, reEngine.Core.Execution, reEngine.Host.Audio, reEngine.Host.Contracts, reEngine.Host.Settings, reEngine.Host.Ui
// @exports: HostSessionRuntimeBridge

using Foundation.Core;
using reEngine.Core.Execution;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;
using reEngine.Host.Settings;
using reEngine.Host.Ui;

namespace reEngine.Host.Core;

internal sealed class HostSessionRuntimeBridge : IDisposable
{
    private BootCrashSentinel? _sentinel;
    private MediaRamPreloadResult? _preloadedMedia;
    private HostAudioMixerSettings? _lastSyncedAudio;
    private short[] _soundTestScratch = new short[2048];
    private string? _preloadDiagnostic;
    private bool _lastVsync;
    private Action<bool>? _safeModeCallback;

    public bool IsSafeModeActive { get; private set; }
    public string? DiagnosticNote => _preloadDiagnostic;

    public HostSessionOptions PrepareExecution(HostSessionOptions options, ref string? fallbackBanner)
    {
        ArgumentNullException.ThrowIfNull(options);
        HostSessionOptions effective = options;
        HostExecutionResilienceOptions? res = options.ExecutionResilience;
        _safeModeCallback = options.SafeModeCallback;

        BootCrashCheckResult? crash = null;
        if (!string.IsNullOrWhiteSpace(res?.DataDirectory))
        {
            _sentinel = new BootCrashSentinel(res.DataDirectory!);
            crash = _sentinel.BeginBoot(options.ModuleId);
        }

        if (crash?.IsSafeModeTriggered == true || res?.IsSafeModeActive == true)
        {
            effective = HostSettingsStore.ApplyInMemorySafeMode(effective, crash);
            IsSafeModeActive = true;
            fallbackBanner ??= crash?.IsSafeModeTriggered == true
                ? "Safe Mode active: previous launch failed before first frame; .re*/HD/unsafe GPU disabled."
                : "Safe Mode active: safe recovery override enabled; .re*/HD/unsafe GPU disabled.";
            if (crash?.IsSafeModeTriggered == true) _safeModeCallback?.Invoke(true);
        }
        else
        {
            IsSafeModeActive = false;
        }

        if (effective.ExecutionResilience is { } effRes)
        {
            ExecutionTierEvaluation eval = ExecutionTierPolicy.Evaluate(
                effRes.IsJitEnabled,
                effRes.ExecutionTierMode,
                ExecutionTierPolicy.IsCliQuarantinedMode(effRes.ExecutionTierMode) ? ExecutionTierScope.AllowCliOnly : ExecutionTierScope.PersistedSetting,
                isRawMediaBoot: false);
            if (!eval.IsAllowed)
            {
                throw new InvalidOperationException(eval.DiagnosticMessage ?? "Execution tier rejected by clean-room AOT policy.");
            }

            if (effRes.ShouldPreloadMediaToRam && !string.IsNullOrWhiteSpace(effRes.PreloadMediaPath))
            {
                string root = options.AllowedSnapshotRoot ?? Path.GetDirectoryName(effRes.PreloadMediaPath!) ?? effRes.DataDirectory ?? AppContext.BaseDirectory;
                _preloadedMedia = MediaRamPreloader.TryPreload(effRes.PreloadMediaPath!, root);
                if (_preloadedMedia.FallbackToStreaming)
                {
                    _preloadDiagnostic = _preloadedMedia.DiagnosticMessage;
                }
            }
        }

        _lastVsync = effective.IsVsyncEnabled;
        return effective;
    }

    public void ConfigureDrawer(F1SettingsDrawerView drawer, HostSessionOptions options, Sdl3AudioPump audioPump, HostSoundTestService soundTest)
    {
        drawer.DisplayMode = options.DisplayMode;
        drawer.IsVsyncEnabled = options.IsVsyncEnabled;
        drawer.HudAnchorMode = options.HudAnchorMode;
        drawer.VideoFilterMode = options.VideoFilterMode;
        drawer.IsTrueColor24BitEnabled = options.IsTrueColor24BitEnabled;
        drawer.IsDitheringEnabled = options.IsDitheringEnabled;
        drawer.ScreenshotCaptureMode = options.ScreenshotCaptureMode;
        drawer.AudioSettings = options.AudioSettings ?? new HostAudioMixerSettings();
        drawer.AudioOutputDevices = audioPump.AvailableOutputDevices;
        drawer.SoundTestService = soundTest;
        drawer.ActiveResilience = (options.ExecutionResilience ?? new HostExecutionResilienceOptions()) with { IsSafeModeActive = IsSafeModeActive };
        _lastSyncedAudio = drawer.AudioSettings;
    }

    public void ClearSentinelOnFirstFrame() => _ = _sentinel?.ClearOnFirstFrame();

    public void SyncWindowVsyncIfChanged(F1SettingsDrawerView drawer, Sdl3HostWindow? window)
    {
        if (window is null || drawer.IsVsyncEnabled == _lastVsync) return;
        _lastVsync = drawer.IsVsyncEnabled;
        window.SetVSyncEnabled(_lastVsync);
    }

    public void SyncSafeModeResetIfCleared(F1SettingsDrawerView drawer)
    {
        if (!IsSafeModeActive || drawer.ActiveResilience.IsSafeModeActive) return;
        IsSafeModeActive = false;
        _ = _sentinel?.Clear();
        _safeModeCallback?.Invoke(false);
    }

    public int PumpFrameAudio(
        Sdl3AudioPump audioPump,
        HostSoundTestService soundTest,
        F1SettingsDrawerView drawer,
        HostSessionOptions options,
        bool isWindowFocused,
        bool isFastForward,
        IHostSessionCallbacks callbacks,
        short[] audioBuffer)
    {
        if (!ReferenceEquals(drawer.AudioSettings, _lastSyncedAudio) && drawer.AudioSettings != _lastSyncedAudio)
        {
            audioPump.ApplySettings(drawer.AudioSettings);
            _lastSyncedAudio = drawer.AudioSettings;
        }

        audioPump.SetFocusAndFastForward(isWindowFocused, isFastForward, options.FastForwardMultiplier);
        int readSamples = callbacks.ReadAudioSamples(audioBuffer);
        if (soundTest.IsPlaying)
        {
            int targetCount = Math.Max(readSamples, Math.Min(audioBuffer.Length, (options.AudioFormat.SampleRate / 60) * options.AudioFormat.Channels));
            targetCount = (targetCount / 2) * 2;
            if (targetCount > 0)
            {
                if (_soundTestScratch.Length < targetCount) _soundTestScratch = new short[targetCount];
                int stCount = soundTest.RenderInterleavedStereo(_soundTestScratch.AsSpan(0, targetCount));
                if (readSamples <= 0)
                {
                    _soundTestScratch.AsSpan(0, stCount).CopyTo(audioBuffer);
                    readSamples = stCount;
                }
                else
                {
                    for (int i = 0; i < stCount && i < readSamples; i++)
                    {
                        audioBuffer[i] = (short)Math.Clamp(audioBuffer[i] + _soundTestScratch[i], short.MinValue, short.MaxValue);
                    }
                }
            }
        }
        return readSamples;
    }

    public F3HudTelemetry BuildHudTelemetry(
        HighResFramePacer pacer, Sdl3AudioPump audioPump, F1SettingsDrawerView drawer, HostDisplayMode displayMode, int connectedPads) =>
        new(pacer.CurrentFps, pacer.AverageFrameMs, drawer.SelectedBackend.ToString(), drawer.ResolutionScale, drawer.ExecutionTierLabel, audioPump.FillRatio)
        {
            AspectRatioLabel = HostAspectRatioModeCodec.ToDisplayLabel(drawer.AspectRatioMode),
            IsFastForwardEnabled = drawer.IsFastForwardEnabled,
            ConnectedPadCount = Math.Min(4, connectedPads),
            VideoFilterLabel = HostVideoFilterModeCodec.ToDisplayLabel(drawer.VideoFilterMode),
            DisplayModeLabel = HostDisplayModeCodec.ToDisplayLabel(displayMode),
            IsSafeModeActive = IsSafeModeActive || drawer.ActiveResilience.IsSafeModeActive,
            IsAudioMuted = drawer.AudioSettings.IsAudioMuted
        };

    public void Dispose()
    {
        _preloadedMedia?.Dispose();
        _preloadedMedia = null;
    }
}
