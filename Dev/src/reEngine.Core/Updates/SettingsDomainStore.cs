// @module: reEngine.Core.Updates.SettingsDomainStore
// @purpose: Standard 19 split-domain settings store (core, video, audio) with quarantine, .bak/config_history recovery, and v1 -> v2 migration.
// @depends-on: System, System.Collections.Generic, System.IO, System.Linq, System.Text, System.Text.Json, System.Text.Json.Nodes, System.Text.Json.Serialization, Foundation.Core, reEngine.Core.Execution
// @exports: SettingsDomainStore

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Foundation.Core;
using reEngine.Core.Execution;

namespace reEngine.Core.Updates;

/// <summary>
/// Atomic Standard 19 split-domain settings store managing <c>settings.json</c>, <c>settings.video.json</c>, and <c>settings.audio.json</c> under <c>&lt;dataDir&gt;\config\</c>.
/// </summary>
public sealed class SettingsDomainStore : IReEngineSettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonNodeOptions NodeOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocOpts = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HashSet<string> VideoDomainKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "backendId", "resolutionScale", "preferredResolutionScale", "aspectRatioMode", "overscanCropPx",
        "isFullscreen", "displayMode", "monitorIndex", "isVsyncEnabled", "targetFps", "backgroundFpsCap",
        "isFastForwardEnabled", "isHdTexturePackEnabled", "hudAnchorMode", "videoFilterMode",
        "isTrueColor24BitEnabled", "isDitheringEnabled", "screenshotCaptureMode",
        "frameRateMode", "uiTextScale", "isFpsOverlayEnabled", "isCleanCaptureModeEnabled"
    };

    private static readonly HashSet<string> AudioDomainKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "audioDriver", "audioOutputDevice", "masterVolume", "isAudioMuted", "bgmVolume", "sfxVolume",
        "voiceVolume", "shouldMuteOnFocusLoss", "audioLowPassHz", "audioHighPassHz",
        "audioStereoWidthPercent", "audioResamplingMode", "fastForwardAudioMode"
    };

    private readonly string _corePath, _videoPath, _audioPath, _controlsPath;
    private readonly GranularSettingsManager _manager;

    /// <summary>Initializes a new <see cref="SettingsDomainStore"/> bound to <paramref name="dataDirectory"/>.</summary>
    /// <param name="dataDirectory">Application or module data directory root.</param>
    /// <param name="coreConfigPathOverride">Optional explicit rooted override path for <c>settings.json</c>.</param>
    public SettingsDomainStore(string dataDirectory, string? coreConfigPathOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory.Trim());
        _corePath = string.IsNullOrWhiteSpace(coreConfigPathOverride)
            ? Path.Combine(DataDirectory, "config", "settings.json")
            : Path.GetFullPath(coreConfigPathOverride.Trim());
        ConfigDirectory = Path.GetDirectoryName(_corePath) ?? Path.Combine(DataDirectory, "config");
        _videoPath = Path.Combine(ConfigDirectory, "settings.video.json");
        _audioPath = Path.Combine(ConfigDirectory, "settings.audio.json");
        _controlsPath = Path.Combine(ConfigDirectory, "settings.controls.json");
        QuarantineDirectory = Path.Combine(DataDirectory, "quarantine");
        _manager = new GranularSettingsManager(DataDirectory);
    }

    /// <inheritdoc />
    public string ConfigDirectory { get; }

    /// <summary>Gets the resolved data directory root.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the resolved quarantine directory (<c>&lt;dataDir&gt;\quarantine\</c>).</summary>
    public string QuarantineDirectory { get; }

    /// <inheritdoc />
    public ReEngineSettingsBundle LoadOrMigrate()
    {
        var diagnostics = new List<string>();
        bool hadCoreBefore = File.Exists(_corePath) || File.Exists(_corePath + ".bak") || _manager.GetHistorySnapshots("settings.json").Count > 0;
        bool coreMissing = !File.Exists(_corePath);
        JsonObject? coreObj = LoadOrRecoverJsonObject<ReEngineSettings>(_corePath, diagnostics, out bool coreRecovered);
        bool coreWasNull = coreObj is null;
        coreObj ??= new JsonObject(NodeOpts);

        bool videoExists = File.Exists(_videoPath);
        bool audioExists = File.Exists(_audioPath);
        int schemaVer = TryReadValue(coreObj, "schemaVersion", 1);
        bool hasLegacyDomainKeys = coreObj.Any(kv => VideoDomainKeys.Contains(kv.Key) || AudioDomainKeys.Contains(kv.Key));
        bool needsMigration = !coreWasNull && (schemaVer < 2 || (hasLegacyDomainKeys && (!videoExists || !audioExists)));
        bool wasMigrated = needsMigration && hadCoreBefore;

        if (needsMigration) SnapshotV1IntoHistory(coreObj, diagnostics);
        ReEngineVideoSettings video = needsMigration && !videoExists
            ? BuildVideoFromMigration(ExtractKeys(coreObj, VideoDomainKeys))
            : LoadOrCreateDomain(_videoPath, static () => new ReEngineVideoSettings(), ApplyVideoInvariants, diagnostics);
        ReEngineAudioSettings audio = needsMigration && !audioExists
            ? DeserializeObject(ExtractKeys(coreObj, AudioDomainKeys), static () => new ReEngineAudioSettings())
            : LoadOrCreateDomain(_audioPath, static () => new ReEngineAudioSettings(), ApplyAudioInvariants, diagnostics);

        StripKeys(coreObj, VideoDomainKeys);
        StripKeys(coreObj, AudioDomainKeys);
        ReEngineSettings core = DeserializeCoreFromCleanJson(coreObj);
        ApplyCoreInvariants(core);
        ApplyVideoInvariants(video);
        ApplyAudioInvariants(audio);
        if (needsMigration || coreRecovered || coreMissing || coreWasNull) SaveCore(core);
        if (needsMigration && !videoExists) SaveVideo(video);
        if (needsMigration && !audioExists) SaveAudio(audio);
        return new ReEngineSettingsBundle(core, video, audio, _controlsPath, wasMigrated, diagnostics);
    }

    /// <inheritdoc />
    public void SaveAll(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        SaveCore(bundle.Core);
        SaveVideo(bundle.Video);
        SaveAudio(bundle.Audio);
    }

    /// <inheritdoc />
    public void SaveCore(ReEngineSettings core)
    {
        ArgumentNullException.ThrowIfNull(core);
        ApplyCoreInvariants(core);
        JsonObject payload = JsonSerializer.SerializeToNode(core, JsonOptions)?.AsObject() ?? new JsonObject(NodeOpts);
        StripKeys(payload, VideoDomainKeys);
        StripKeys(payload, AudioDomainKeys);
        _manager.SaveAtomically(_corePath, payload, JsonOptions);
    }

    /// <inheritdoc />
    public void SaveVideo(ReEngineVideoSettings video)
    {
        ArgumentNullException.ThrowIfNull(video);
        ApplyVideoInvariants(video);
        _manager.SaveAtomically(_videoPath, video, JsonOptions);
    }

    /// <inheritdoc />
    public void SaveAudio(ReEngineAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ApplyAudioInvariants(audio);
        _manager.SaveAtomically(_audioPath, audio, JsonOptions);
    }

    private T LoadOrCreateDomain<T>(string path, Func<T> factory, Action<T> normalize, List<string> diagnostics)
    {
        JsonObject? obj = LoadOrRecoverJsonObject<T>(path, diagnostics, out bool recovered);
        T model = obj is null ? factory() : DeserializeObject(obj, factory);
        normalize(model);
        if (obj is null || recovered) _manager.SaveAtomically(path, model, JsonOptions, recordSnapshot: obj is null);
        return model;
    }

    private JsonObject? LoadOrRecoverJsonObject<T>(string primaryPath, List<string> diagnostics, out bool recovered)
    {
        recovered = false;
        string fileName = Path.GetFileName(primaryPath);
        if (File.Exists(primaryPath))
        {
            if (TryReadValidJsonObject<T>(primaryPath, out JsonObject? valid, out string? error)) return valid;
            diagnostics.Add($"Quarantined corrupt '{fileName}' to '{QuarantineCorruptFile(primaryPath)}' ({error}).");
        }

        string bakPath = primaryPath + ".bak";
        foreach (string candidate in new[] { bakPath }.Concat(_manager.GetHistorySnapshots(fileName)))
        {
            if (!File.Exists(candidate)) continue;
            if (TryReadValidJsonObject<T>(candidate, out JsonObject? recObj, out _))
            {
                recovered = true;
                diagnostics.Add($"Recovered '{fileName}' from '{(candidate == bakPath ? ".bak" : $"history snapshot '{Path.GetFileName(candidate)}'")}'.");
                return recObj;
            }
            if (candidate == bakPath) diagnostics.Add($"Backup '{Path.GetFileName(bakPath)}' was unreadable.");
        }

        if (diagnostics.Count > 0) diagnostics.Add($"Falling back to clean defaults for '{fileName}'.");
        return null;
    }

    private static bool TryReadValidJsonObject<T>(string path, out JsonObject? obj, out string? error)
    {
        obj = null;
        error = null;
        try
        {
            string raw = File.ReadAllText(path, Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(raw) &&
                JsonNode.Parse(raw, NodeOpts, DocOpts) is JsonObject parsed &&
                JsonSerializer.Deserialize<T>(raw, JsonOptions) is not null)
            {
                obj = parsed;
                return true;
            }
            error = "Empty or non-object JSON";
            return false;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private string QuarantineCorruptFile(string corruptPath)
    {
        Directory.CreateDirectory(QuarantineDirectory);
        string stem = Path.GetFileNameWithoutExtension(corruptPath);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string dest = Path.Combine(QuarantineDirectory, $"{stem}.corrupt_{stamp}.json");
        if (File.Exists(dest)) dest = Path.Combine(QuarantineDirectory, $"{stem}.corrupt_{stamp}_{Guid.NewGuid().ToString("N")[..4]}.json");
        File.Move(corruptPath, dest, overwrite: false);
        return dest;
    }

    private void SnapshotV1IntoHistory(JsonObject v1Obj, List<string> diagnostics)
    {
        Directory.CreateDirectory(_manager.ConfigHistoryDirectory);
        string histPath = Path.Combine(_manager.ConfigHistoryDirectory, $"settings_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}_v1.json");
        File.WriteAllText(histPath, v1Obj.ToJsonString(JsonOptions), Utf8NoBom);
        diagnostics.Add($"Migrated v1 settings.json -> v2 split domains (snapshot: '{Path.GetFileName(histPath)}').");
    }

    private static ReEngineVideoSettings BuildVideoFromMigration(JsonObject extractedVideo)
    {
        if (extractedVideo["resolutionScale"] is null && extractedVideo["preferredResolutionScale"] is JsonNode pref)
            extractedVideo["resolutionScale"] = pref.DeepClone();
        extractedVideo.Remove("preferredResolutionScale");
        if (extractedVideo["isFullscreen"] is null && extractedVideo["displayMode"] is JsonValue dm && dm.TryGetValue<string>(out string? mode) && !string.Equals(mode, "windowed", StringComparison.OrdinalIgnoreCase))
            extractedVideo["isFullscreen"] = true;
        return DeserializeObject(extractedVideo, static () => new ReEngineVideoSettings());
    }

    private static ReEngineSettings DeserializeCoreFromCleanJson(JsonObject coreObj)
    {
        bool hasExplicitJit = coreObj.ContainsKey("isJitEnabled");
        bool legacyJit = TryReadValue(coreObj, "shouldAllowJitBringup", false) || TryReadValue(coreObj, "jitBringup", false);
        ReEngineSettings core = DeserializeObject(coreObj, static () => new ReEngineSettings());
        if (!hasExplicitJit && legacyJit) { core.IsJitEnabled = true; core.ExecutionTierMode = ExecutionTierPolicy.JitBringup; }
        return core;
    }

    private static void ApplyCoreInvariants(ReEngineSettings core)
    {
        core.SchemaVersion = Math.Max(2, core.SchemaVersion); core.AppId = "reengine"; core.Domain = "core";
        core.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); core.PreferredResolutionScale = null;
        core.FastForwardMultiplier = double.IsFinite(core.FastForwardMultiplier) && core.FastForwardMultiplier > 0 ? Math.Clamp(core.FastForwardMultiplier, 2.0, 4.0) : 2.0;
        ExecutionTierPolicy.EnforcePersistenceInvariants(core);
    }

    private static void ApplyVideoInvariants(ReEngineVideoSettings video)
    {
        video.SchemaVersion = Math.Max(2, video.SchemaVersion); video.AppId = "reengine"; video.Domain = "video";
        video.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        video.OverscanCropPx ??= new OverscanCropPxSettings();
        video.ResolutionScale = video.ResolutionScale is 1 or 2 or 4 or 8 ? video.ResolutionScale : (video.ResolutionScale <= 1 ? 1 : video.ResolutionScale <= 2 ? 2 : video.ResolutionScale <= 4 ? 4 : 8);
        video.MonitorIndex = Math.Max(0, video.MonitorIndex); video.TargetFps = Math.Max(0, video.TargetFps); video.BackgroundFpsCap = Math.Max(0, video.BackgroundFpsCap);
        video.DisplayMode = video.IsFullscreen ? (string.Equals(video.DisplayMode?.Trim(), "exclusiveFullscreen", StringComparison.OrdinalIgnoreCase) ? "exclusiveFullscreen" : "borderlessFullscreen") : "windowed";
        video.FrameRateMode = string.Equals(video.FrameRateMode?.Trim(), "original", StringComparison.OrdinalIgnoreCase) ? "original" : "enhanced";
        video.UiTextScale = double.IsFinite(video.UiTextScale) && video.UiTextScale > 0.0 ? Math.Clamp(Math.Round(video.UiTextScale, 2), 0.75, 2.0) : 1.0;
    }

    private static void ApplyAudioInvariants(ReEngineAudioSettings audio)
    {
        audio.SchemaVersion = Math.Max(2, audio.SchemaVersion); audio.AppId = "reengine"; audio.Domain = "audio";
        audio.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        audio.MasterVolume = Clamp01(audio.MasterVolume, 0.85); audio.BgmVolume = Clamp01(audio.BgmVolume, 1.0);
        audio.SfxVolume = Clamp01(audio.SfxVolume, 1.0); audio.VoiceVolume = Clamp01(audio.VoiceVolume, 1.0);
        audio.AudioStereoWidthPercent = double.IsFinite(audio.AudioStereoWidthPercent) ? Math.Clamp(audio.AudioStereoWidthPercent, 0.0, 200.0) : 100.0;
        audio.AudioLowPassHz = Math.Max(0, audio.AudioLowPassHz); audio.AudioHighPassHz = Math.Max(0, audio.AudioHighPassHz);
    }

    private static double Clamp01(double value, double fallback) => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : fallback;
    private static T DeserializeObject<T>(JsonObject obj, Func<T> fallback) => obj.Deserialize<T>(JsonOptions) ?? fallback();
    private static T TryReadValue<T>(JsonObject obj, string name, T fallback) => obj[name] is JsonValue v && v.TryGetValue(out T? val) ? val : fallback;
    private static void StripKeys(JsonObject obj, HashSet<string> keys) { foreach (string key in obj.Select(kv => kv.Key).Where(keys.Contains).ToList()) obj.Remove(key); }

    private static JsonObject ExtractKeys(JsonObject source, HashSet<string> keys)
    {
        var extracted = new JsonObject(NodeOpts);
        foreach (string key in source.Select(kv => kv.Key).Where(keys.Contains).ToList()) { extracted[key] = source[key]?.DeepClone(); source.Remove(key); }
        return extracted;
    }
}
