// @module: reEngine.Host.Contracts.HostAudioAndExecutionContracts
// @purpose: Audio DSP mixer, Sound Test jukebox, execution resilience, and screenshot outcome contracts for reEngine.Host.
// @depends-on: System, System.Collections.Generic
// @exports: HostFastForwardAudioMode, HostFastForwardAudioModeCodec, HostAudioResamplingMode, HostAudioResamplingModeCodec, HostAudioFilterPreset, HostAudioFilterPresetCodec, HostAudioDeviceInfo, HostAudioMixerSettings, HostSoundTestTrack, IHostSoundTestProvider, HostExecutionResilienceOptions, HostScreenshotResult

namespace reEngine.Host.Contracts;

/// <summary>Fast-forward audio clamping policy (Standard 18: <c>fastForwardAudioMode</c>).</summary>
public enum HostFastForwardAudioMode
{
    AttenuatedLowPass = 0,
    Muted = 1,
    Normal = 2
}

/// <summary>Normalizes and cycles <see cref="HostFastForwardAudioMode"/>.</summary>
public static class HostFastForwardAudioModeCodec
{
    public static HostFastForwardAudioMode Parse(string? raw) =>
        TryParseStrict(raw, out HostFastForwardAudioMode mode) ? mode : HostFastForwardAudioMode.AttenuatedLowPass;

    public static bool TryParseStrict(string? raw, out HostFastForwardAudioMode mode)
    {
        mode = HostFastForwardAudioMode.AttenuatedLowPass;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "attenuatedlowpass" or "attenuated" or "clamp": mode = HostFastForwardAudioMode.AttenuatedLowPass; return true;
            case "muted" or "mute": mode = HostFastForwardAudioMode.Muted; return true;
            case "normal" or "passthrough": mode = HostFastForwardAudioMode.Normal; return true;
            default: return false;
        }
    }

    public static string ToCanonicalId(HostFastForwardAudioMode mode) => mode switch
    {
        HostFastForwardAudioMode.Muted => "muted",
        HostFastForwardAudioMode.Normal => "normal",
        _ => "attenuatedLowPass"
    };

    public static string ToDisplayLabel(HostFastForwardAudioMode mode) => mode switch
    {
        HostFastForwardAudioMode.Muted => "Muted",
        HostFastForwardAudioMode.Normal => "Normal",
        _ => "Atten LP"
    };

    public static HostFastForwardAudioMode CycleNext(HostFastForwardAudioMode current) =>
        (HostFastForwardAudioMode)(((int)current + 1) % 3);
}

/// <summary>Host audio stream resampling quality (Standard 18: <c>audioResamplingMode</c>).</summary>
public enum HostAudioResamplingMode
{
    Linear = 0,
    SincCubic = 1
}

/// <summary>Normalizes and toggles <see cref="HostAudioResamplingMode"/>.</summary>
public static class HostAudioResamplingModeCodec
{
    public static HostAudioResamplingMode Parse(string? raw) =>
        TryParseStrict(raw, out HostAudioResamplingMode mode) ? mode : HostAudioResamplingMode.SincCubic;

    public static bool TryParseStrict(string? raw, out HostAudioResamplingMode mode)
    {
        mode = HostAudioResamplingMode.SincCubic;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "linear": mode = HostAudioResamplingMode.Linear; return true;
            case "sinccubic" or "cubic" or "sinc": mode = HostAudioResamplingMode.SincCubic; return true;
            default: return false;
        }
    }

    public static string ToCanonicalId(HostAudioResamplingMode mode) =>
        mode == HostAudioResamplingMode.Linear ? "linear" : "sincCubic";

    public static string ToDisplayLabel(HostAudioResamplingMode mode) =>
        mode == HostAudioResamplingMode.Linear ? "Linear" : "Sinc Cubic";
}

/// <summary>One-click 2nd-order Butterworth audio filter presets in the Audio tab.</summary>
public enum HostAudioFilterPreset
{
    Off = 0,
    CrtWarm = 1,
    HandheldSpeaker = 2
}

/// <summary>Maps biquad LP/HP presets to cutoff frequencies in Hz.</summary>
public static class HostAudioFilterPresetCodec
{
    public static (int LowPassHz, int HighPassHz) ToCutoffsHz(HostAudioFilterPreset preset) => preset switch
    {
        HostAudioFilterPreset.CrtWarm => (4800, 90),
        HostAudioFilterPreset.HandheldSpeaker => (5500, 260),
        _ => (0, 0)
    };

    public static HostAudioFilterPreset DetectPreset(int lowPassHz, int highPassHz)
    {
        if (lowPassHz <= 0 && highPassHz <= 0) return HostAudioFilterPreset.Off;
        if (lowPassHz == 4800 && highPassHz == 90) return HostAudioFilterPreset.CrtWarm;
        if (lowPassHz == 5500 && highPassHz == 260) return HostAudioFilterPreset.HandheldSpeaker;
        return HostAudioFilterPreset.Off;
    }

    public static HostAudioFilterPreset Parse(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "crtwarm" or "crt" => HostAudioFilterPreset.CrtWarm,
        "handheldspeaker" or "handheld" => HostAudioFilterPreset.HandheldSpeaker,
        _ => HostAudioFilterPreset.Off
    };

    public static string ToDisplayLabel(HostAudioFilterPreset preset) => preset switch
    {
        HostAudioFilterPreset.CrtWarm => "CRT Warm",
        HostAudioFilterPreset.HandheldSpeaker => "Handheld",
        _ => "Off / Raw"
    };

    public static HostAudioFilterPreset CycleNext(HostAudioFilterPreset current) =>
        (HostAudioFilterPreset)(((int)current + 1) % 3);
}

/// <summary>Enumerated SDL3 audio playback output device metadata.</summary>
public sealed record HostAudioDeviceInfo(uint DeviceId, string Name, bool IsDefault);

/// <summary>Immutable snapshot of active audio DSP and mixer settings.</summary>
public sealed record HostAudioMixerSettings
{
    public string? AudioOutputDevice { get; init; }
    public double MasterVolume { get; init; } = 0.85;
    public bool IsAudioMuted { get; init; }
    public double BgmVolume { get; init; } = 1.0;
    public double SfxVolume { get; init; } = 1.0;
    public double VoiceVolume { get; init; } = 1.0;
    public bool ShouldMuteOnFocusLoss { get; init; }
    public int AudioLowPassHz { get; init; }
    public int AudioHighPassHz { get; init; }
    public double AudioStereoWidthPercent { get; init; } = 100.0;
    public HostAudioResamplingMode ResamplingMode { get; init; } = HostAudioResamplingMode.SincCubic;
    public HostFastForwardAudioMode FastForwardAudioMode { get; init; } = HostFastForwardAudioMode.AttenuatedLowPass;
}

/// <summary>Descriptor for a selectable Sound Test / BGM audition track.</summary>
public sealed record HostSoundTestTrack(string TrackId, string Title, string? ComposerOrTag = null, int DurationSeconds = 30);

/// <summary>Clean-room Sound Test audition provider exposed by child platform engines or host demo.</summary>
public interface IHostSoundTestProvider
{
    IReadOnlyList<HostSoundTestTrack> GetTracks();
    void PlayTrack(string trackId);
    void Stop();
}

/// <summary>Runtime execution resilience and crash-recovery configuration for <c>HostSessionRunner</c>.</summary>
public sealed record HostExecutionResilienceOptions
{
    public string? DataDirectory { get; init; }
    public string ModuleId { get; init; } = "host-demo";
    public bool IsJitEnabled { get; init; }
    public string ExecutionTierMode { get; init; } = "staticAotOnly";
    public bool ShouldPreloadMediaToRam { get; init; }
    public string? PreloadMediaPath { get; init; }
    public bool IsFastBootEnabled { get; init; }
    public bool IsSafeModeActive { get; init; }
    public bool ShouldFastLoadData { get; init; } = true;
    public double FastForwardMultiplier { get; init; } = 2.0;
    public bool AllowRawMediaFallback { get; init; }
}

/// <summary>Result returned by <c>HostScreenshotService</c> on <c>F12</c> capture.</summary>
public sealed record HostScreenshotResult(
    bool Succeeded,
    string? FilePath,
    HostScreenshotCaptureMode CaptureMode,
    int Width,
    int Height,
    string DiagnosticMessage);
