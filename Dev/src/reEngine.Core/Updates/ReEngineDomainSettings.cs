// @module: reEngine.Core.Updates.ReEngineDomainSettings
// @purpose: Standard 18/19 v2 split settings models (settings.video.json, settings.audio.json), bundle, and IReEngineSettingsStore contract.
// @depends-on: System, System.Collections.Generic, System.Text.Json, System.Text.Json.Serialization
// @exports: ReEngineVideoSettings, ReEngineAudioSettings, ReEngineSettingsBundle, IReEngineSettingsStore

using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Updates;

/// <summary>
/// Persisted <c>data\config\settings.video.json</c> domain schema following Standard 18 &amp; Standard 19.
/// </summary>
public sealed class ReEngineVideoSettings
{
    /// <summary>Video settings schema version (currently 2).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Canonical application identifier.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "reengine";

    /// <summary>Standard 19 settings domain identifier (<c>"video"</c>).</summary>
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "video";

    /// <summary>ISO-8601 UTC timestamp of the last save.</summary>
    [JsonPropertyName("updatedAtUtc")]
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Canonical render backend identifier (<c>opengl</c>, <c>vulkan</c>, <c>directx</c>, <c>parallelCpu</c>, <c>software</c>).</summary>
    [JsonPropertyName("backendId")]
    public string BackendId { get; set; } = "opengl";

    /// <summary>Canonical integer internal resolution scale (1, 2, 4, or 8).</summary>
    [JsonPropertyName("resolutionScale")]
    public int ResolutionScale { get; set; } = 2;

    /// <summary>Canonical aspect ratio presentation mode.</summary>
    [JsonPropertyName("aspectRatioMode")]
    public string AspectRatioMode { get; set; } = "nativePar";

    /// <summary>Integer edge overscan crop trim counts in source pixels.</summary>
    [JsonPropertyName("overscanCropPx")]
    public OverscanCropPxSettings OverscanCropPx { get; set; } = new();

    /// <summary>Whether fullscreen presentation is enabled.</summary>
    [JsonPropertyName("isFullscreen")]
    public bool IsFullscreen { get; set; }

    /// <summary>Host windowing mode (<c>windowed</c>, <c>borderlessFullscreen</c>, <c>exclusiveFullscreen</c>).</summary>
    [JsonPropertyName("displayMode")]
    public string DisplayMode { get; set; } = "windowed";

    /// <summary>Zero-indexed target OS monitor index.</summary>
    [JsonPropertyName("monitorIndex")]
    public int MonitorIndex { get; set; }

    /// <summary>Whether display swapchain vertical sync is enabled.</summary>
    [JsonPropertyName("isVsyncEnabled")]
    public bool IsVsyncEnabled { get; set; }

    /// <summary>Target presentation frame rate (<c>0</c> = unlocked).</summary>
    [JsonPropertyName("targetFps")]
    public int TargetFps { get; set; } = 60;

    /// <summary>Optional background FPS cap when window loses focus (<c>0</c> = inherit/uncapped).</summary>
    [JsonPropertyName("backgroundFpsCap")]
    public int BackgroundFpsCap { get; set; }

    /// <summary>Whether runtime fast-forward is enabled.</summary>
    [JsonPropertyName("isFastForwardEnabled")]
    public bool IsFastForwardEnabled { get; set; }

    /// <summary>Whether path-contained <c>hdpacks\</c> texture overrides are enabled.</summary>
    [JsonPropertyName("isHdTexturePackEnabled")]
    public bool IsHdTexturePackEnabled { get; set; }

    /// <summary>Widescreen HUD alignment policy (<c>safeFourByThree</c>, <c>screenEdges</c>, <c>custom</c>).</summary>
    [JsonPropertyName("hudAnchorMode")]
    public string HudAnchorMode { get; set; } = "safeFourByThree";

    /// <summary>Live host presentation shader/filter preset (<c>nearest</c>, <c>bilinear</c>, <c>sharpBilinear</c>, <c>scanlines</c>, <c>crtLite</c>, <c>lcdGrid</c>, <c>xbrz</c>, <c>ntscComposite</c>).</summary>
    [JsonPropertyName("videoFilterMode")]
    public string VideoFilterMode { get; set; } = "nearest";

    /// <summary>Whether 24/32-bit internal RGBA composition is enabled across 2D and 3D paths.</summary>
    [JsonPropertyName("isTrueColor24BitEnabled")]
    public bool IsTrueColor24BitEnabled { get; set; } = true;

    /// <summary>Whether classic 15-bit quantization with 4x4 Bayer ordered dithering is enabled.</summary>
    [JsonPropertyName("isDitheringEnabled")]
    public bool IsDitheringEnabled { get; set; }

    /// <summary>Screenshot capture source for <c>F12</c> (<c>cleanNative</c> or <c>filteredComposite</c>).</summary>
    [JsonPropertyName("screenshotCaptureMode")]
    public string ScreenshotCaptureMode { get; set; } = "cleanNative";

    /// <summary>Preserves unknown forward-compatible video properties across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Persisted <c>data\config\settings.audio.json</c> domain schema following Standard 18 &amp; Standard 19.
/// </summary>
public sealed class ReEngineAudioSettings
{
    /// <summary>Audio settings schema version (currently 2).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Canonical application identifier.</summary>
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "reengine";

    /// <summary>Standard 19 settings domain identifier (<c>"audio"</c>).</summary>
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "audio";

    /// <summary>ISO-8601 UTC timestamp of the last save.</summary>
    [JsonPropertyName("updatedAtUtc")]
    public string UpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Optional native OS audio backend override (<c>wasapi</c>, <c>pipewire</c>, etc.; <see langword="null"/> = auto).</summary>
    [JsonPropertyName("audioDriver")]
    public string? AudioDriver { get; set; }

    /// <summary>Optional explicit OS audio playback device name (<see langword="null"/> = system default).</summary>
    [JsonPropertyName("audioOutputDevice")]
    public string? AudioOutputDevice { get; set; }

    /// <summary>Normalized master audio gain (<c>0.0</c>.. <c>1.0</c>).</summary>
    [JsonPropertyName("masterVolume")]
    public double MasterVolume { get; set; } = 0.85;

    /// <summary>One-click master audio mute toggle preserving <see cref="MasterVolume"/>.</summary>
    [JsonPropertyName("isAudioMuted")]
    public bool IsAudioMuted { get; set; }

    /// <summary>Normalized background music category gain (<c>0.0</c>.. <c>1.0</c>).</summary>
    [JsonPropertyName("bgmVolume")]
    public double BgmVolume { get; set; } = 1.0;

    /// <summary>Normalized sound effects category gain (<c>0.0</c>.. <c>1.0</c>).</summary>
    [JsonPropertyName("sfxVolume")]
    public double SfxVolume { get; set; } = 1.0;

    /// <summary>Normalized voice/dialogue category gain (<c>0.0</c>.. <c>1.0</c>).</summary>
    [JsonPropertyName("voiceVolume")]
    public double VoiceVolume { get; set; } = 1.0;

    /// <summary>Whether audio mutes smoothly when the host window loses focus.</summary>
    [JsonPropertyName("shouldMuteOnFocusLoss")]
    public bool ShouldMuteOnFocusLoss { get; set; }

    /// <summary>Optional low-pass filter cutoff frequency in Hz (<c>0</c> = disabled).</summary>
    [JsonPropertyName("audioLowPassHz")]
    public int AudioLowPassHz { get; set; }

    /// <summary>Optional high-pass filter cutoff frequency in Hz (<c>0</c> = disabled).</summary>
    [JsonPropertyName("audioHighPassHz")]
    public int AudioHighPassHz { get; set; }

    /// <summary>Mid-side stereo width percentage (<c>0.0</c>.. <c>200.0</c>, default <c>100.0</c>).</summary>
    [JsonPropertyName("audioStereoWidthPercent")]
    public double AudioStereoWidthPercent { get; set; } = 100.0;

    /// <summary>Host audio stream resampling quality (<c>linear</c> or <c>sincCubic</c>).</summary>
    [JsonPropertyName("audioResamplingMode")]
    public string AudioResamplingMode { get; set; } = "sincCubic";

    /// <summary>Fast-forward audio clamping policy (<c>attenuatedLowPass</c>, <c>muted</c>, or <c>normal</c>).</summary>
    [JsonPropertyName("fastForwardAudioMode")]
    public string FastForwardAudioMode { get; set; } = "attenuatedLowPass";

    /// <summary>Preserves unknown forward-compatible audio properties across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Unified in-memory view over Standard 19 <c>settings.json</c>, <c>settings.video.json</c>, and <c>settings.audio.json</c>.
/// </summary>
public sealed record ReEngineSettingsBundle(
    ReEngineSettings Core,
    ReEngineVideoSettings Video,
    ReEngineAudioSettings Audio,
    string? ControlsConfigPath = null,
    bool WasMigratedFromV1 = false,
    IReadOnlyList<string>? Diagnostics = null);

/// <summary>
/// Atomic Standard 19 split-domain settings store implemented by <c>SettingsDomainStore</c> / <c>HostSettingsStore</c>.
/// </summary>
public interface IReEngineSettingsStore
{
    /// <summary>Resolved canonical <c>data\config</c> directory path.</summary>
    string ConfigDirectory { get; }

    /// <summary>Loads all domains with <c>v1 -&gt; v2</c> migration and quarantine/backup/history self-healing.</summary>
    ReEngineSettingsBundle LoadOrMigrate();

    /// <summary>Atomically saves core, video, and audio domains cleanly without splitting state.</summary>
    void SaveAll(ReEngineSettingsBundle bundle);

    /// <summary>Atomically saves core router/execution settings.</summary>
    void SaveCore(ReEngineSettings core);

    /// <summary>Atomically saves video settings.</summary>
    void SaveVideo(ReEngineVideoSettings video);

    /// <summary>Atomically saves audio settings.</summary>
    void SaveAudio(ReEngineAudioSettings audio);
}
