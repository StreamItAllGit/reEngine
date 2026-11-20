// @module: reEngine.App.SettingsMigrationHelper
// @purpose: Dual-root v1 settings migration, Standard 19 bundle loading/normalization, and display/execution invariant enforcement.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text, System.Text.Json, Foundation.Core, reEngine.Core, reEngine.Host.Contracts
// @exports: SettingsMigrationResult, SettingsMigrationHelper

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App;

internal sealed record SettingsMigrationResult(
    bool Migrated,
    string TargetConfigPath,
    IReadOnlyList<string> ArchivedSources,
    string Summary);

internal static class SettingsMigrationHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static SettingsMigrationResult MigrateIfNeeded(
        string installRoot,
        string dataDirectory,
        string targetConfigPath,
        bool allowAppDataProbe = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetConfigPath);

        string fullTarget = Path.GetFullPath(targetConfigPath);
        string fullDataDir = Path.GetFullPath(dataDirectory);
        if (File.Exists(fullTarget))
        {
            return new SettingsMigrationResult(false, fullTarget, Array.Empty<string>(), "Canonical config already exists.");
        }

        var candidates = new List<string>();
        string rootLegacy = Path.Combine(fullDataDir, "settings.json");
        if (File.Exists(rootLegacy) && !string.Equals(rootLegacy, fullTarget, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(rootLegacy);
        }

        if (allowAppDataProbe && !string.IsNullOrWhiteSpace(installRoot))
        {
            string appLegacy = Path.Combine(Path.GetFullPath(installRoot), "app", "data", "settings.json");
            if (File.Exists(appLegacy) && !string.Equals(appLegacy, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(appLegacy);
            }
        }

        if (candidates.Count == 0)
        {
            return new SettingsMigrationResult(false, fullTarget, Array.Empty<string>(), "No legacy split settings found.");
        }

        List<string> ordered = candidates
            .OrderBy(p => File.GetLastWriteTimeUtc(p))
            .ToList();

        var merged = new JsonObject();
        var archived = new List<string>(ordered.Count);
        string historyDir = Path.Combine(fullDataDir, "config_history");
        Directory.CreateDirectory(historyDir);

        foreach (string src in ordered)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(src, Encoding.UTF8)) is JsonObject obj)
                {
                    foreach (KeyValuePair<string, JsonNode?> kv in obj)
                    {
                        merged[kv.Key] = kv.Value?.DeepClone();
                    }
                }

                string suffix = Guid.NewGuid().ToString("N")[..6];
                string backupName = $"legacy_{Path.GetFileNameWithoutExtension(src)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.json";
                File.Copy(src, Path.Combine(historyDir, backupName), overwrite: true);

                string migratedBak = src + ".migrated.bak";
                File.Move(src, migratedBak, overwrite: true);
                archived.Add(migratedBak);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                System.Diagnostics.Trace.WriteLine($"Skipping corrupt legacy settings '{src}': {ex.Message}");
            }
        }

        if (merged.Count == 0)
        {
            return new SettingsMigrationResult(false, fullTarget, archived, "Legacy settings files could not be parsed.");
        }

        if (merged["resolutionScale"] is null && merged["preferredResolutionScale"] is JsonNode prefNode)
        {
            merged["resolutionScale"] = prefNode.DeepClone();
        }
        merged.Remove("preferredResolutionScale");
        merged["updatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        ReEngineSettings? typed = merged.Deserialize<ReEngineSettings>(JsonOptions);
        var manager = new GranularSettingsManager(fullDataDir);
        manager.SaveAtomically(fullTarget, typed ?? new ReEngineSettings());

        return new SettingsMigrationResult(true, fullTarget, archived, $"Merged {ordered.Count} legacy settings file(s) into '{fullTarget}'.");
    }

    public static (SettingsMigrationResult Migration, ReEngineSettingsBundle Bundle) MigrateAndLoadBundle(
        string installRoot,
        string dataDirectory,
        string targetConfigPath,
        IReEngineSettingsStore store,
        bool allowAppDataProbe = true)
    {
        ArgumentNullException.ThrowIfNull(store);
        SettingsMigrationResult mig = MigrateIfNeeded(installRoot, dataDirectory, targetConfigPath, allowAppDataProbe);
        ReEngineSettingsBundle bundle = store.LoadOrMigrate();
        return (mig, bundle);
    }

    public static void NormalizeCore(ReEngineSettings core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (!core.IsJitEnabled && ReEngineAppStatusHelper.GetJitBringup(core)) core.IsJitEnabled = true;
        if (!core.IsJitEnabled)
        {
            core.ExecutionTierMode = "staticAotOnly";
        }
        else if (string.IsNullOrWhiteSpace(core.ExecutionTierMode) ||
                 string.Equals(core.ExecutionTierMode, "staticAotOnly", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(core.ExecutionTierMode, "interpreterOracle", StringComparison.OrdinalIgnoreCase))
        {
            core.ExecutionTierMode = "jitBringup";
        }
        core.FastForwardMultiplier = Math.Clamp(
            double.IsFinite(core.FastForwardMultiplier) && core.FastForwardMultiplier > 0.0 ? core.FastForwardMultiplier : 2.0,
            2.0, 4.0);
        core.SchemaVersion = Math.Max(2, core.SchemaVersion);
        core.AppId = "reengine";
        core.Domain = "core";
        core.PreferredResolutionScale = null;
        core.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    public static void NormalizeVideo(ReEngineVideoSettings video)
    {
        ArgumentNullException.ThrowIfNull(video);
        video.BackendId = HostRenderBackendIds.TryNormalize(video.BackendId, out string b) ? b : HostRenderBackendIds.OpenGl;
        video.ResolutionScale = video.ResolutionScale is 1 or 2 or 4 or 8 ? video.ResolutionScale : 2;
        video.AspectRatioMode = HostAspectRatioModeCodec.ToCanonicalId(HostAspectRatioModeCodec.Parse(video.AspectRatioMode));
        video.OverscanCropPx ??= new OverscanCropPxSettings();
        video.DisplayMode = video.IsFullscreen
            ? (string.Equals(video.DisplayMode, "exclusiveFullscreen", StringComparison.OrdinalIgnoreCase)
                ? "exclusiveFullscreen" : "borderlessFullscreen")
            : "windowed";
        video.MonitorIndex = Math.Max(0, video.MonitorIndex);
        video.TargetFps = Math.Max(0, video.TargetFps);
        video.BackgroundFpsCap = Math.Max(0, video.BackgroundFpsCap);
        video.HudAnchorMode = HostHudAnchorModeCodec.ToCanonicalId(HostHudAnchorModeCodec.Parse(video.HudAnchorMode));
        video.VideoFilterMode = HostVideoFilterModeCodec.ToCanonicalId(HostVideoFilterModeCodec.Parse(video.VideoFilterMode));
        video.ScreenshotCaptureMode = HostScreenshotCaptureModeCodec.ToCanonicalId(HostScreenshotCaptureModeCodec.Parse(video.ScreenshotCaptureMode));
        video.SchemaVersion = Math.Max(2, video.SchemaVersion);
        video.AppId = "reengine";
        video.Domain = "video";
        video.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    public static void NormalizeAudio(ReEngineAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        audio.AudioDriver = string.IsNullOrWhiteSpace(audio.AudioDriver) ||
                            string.Equals(audio.AudioDriver.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? null : audio.AudioDriver.Trim().ToLowerInvariant();
        audio.AudioOutputDevice = string.IsNullOrWhiteSpace(audio.AudioOutputDevice) ? null : audio.AudioOutputDevice.Trim();
        audio.MasterVolume = ClampUnit(audio.MasterVolume, 0.85);
        audio.BgmVolume = ClampUnit(audio.BgmVolume, 1.0);
        audio.SfxVolume = ClampUnit(audio.SfxVolume, 1.0);
        audio.VoiceVolume = ClampUnit(audio.VoiceVolume, 1.0);
        audio.AudioLowPassHz = Math.Max(0, audio.AudioLowPassHz);
        audio.AudioHighPassHz = Math.Max(0, audio.AudioHighPassHz);
        audio.AudioStereoWidthPercent = Math.Clamp(
            double.IsFinite(audio.AudioStereoWidthPercent) ? audio.AudioStereoWidthPercent : 100.0, 0.0, 200.0);
        audio.AudioResamplingMode = HostAudioResamplingModeCodec.ToCanonicalId(HostAudioResamplingModeCodec.Parse(audio.AudioResamplingMode));
        audio.FastForwardAudioMode = HostFastForwardAudioModeCodec.ToCanonicalId(HostFastForwardAudioModeCodec.Parse(audio.FastForwardAudioMode));
        audio.SchemaVersion = Math.Max(2, audio.SchemaVersion);
        audio.AppId = "reengine";
        audio.Domain = "audio";
        audio.UpdatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }

    public static ReEngineSettingsBundle BuildBundleFromLegacyCore(ReEngineSettings core, string? controlsPath = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        int resolvedScale = core.PreferredResolutionScale is int pref && pref is 1 or 2 or 4 or 8 && core.ResolutionScale is null or 2
            ? pref
            : (core.ResolutionScale ?? 2);
        var video = new ReEngineVideoSettings
        {
            BackendId = core.BackendId,
            ResolutionScale = resolvedScale,
            AspectRatioMode = core.AspectRatioMode ?? "nativePar",
            OverscanCropPx = core.OverscanCropPx ?? new OverscanCropPxSettings(),
            IsFullscreen = core.IsFullscreen,
            DisplayMode = core.IsFullscreen ? "borderlessFullscreen" : "windowed",
            IsFastForwardEnabled = core.IsFastForwardEnabled ?? false,
            IsHdTexturePackEnabled = core.IsHdTexturePackEnabled
        };
        var audio = new ReEngineAudioSettings { AudioDriver = core.AudioDriver };
        NormalizeVideo(video);
        NormalizeAudio(audio);
        return new ReEngineSettingsBundle(core, video, audio, controlsPath);
    }

    public static void SyncCoreLegacyMirror(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ReEngineSettings c = bundle.Core;
        ReEngineVideoSettings v = bundle.Video;
        c.BackendId = v.BackendId;
        c.ResolutionScale = v.ResolutionScale;
        c.PreferredResolutionScale = null;
        c.AspectRatioMode = v.AspectRatioMode;
        c.OverscanCropPx = v.OverscanCropPx;
        c.IsFullscreen = v.IsFullscreen;
        c.IsFastForwardEnabled = v.IsFastForwardEnabled;
        c.IsHdTexturePackEnabled = v.IsHdTexturePackEnabled;
        c.AudioDriver = bundle.Audio.AudioDriver;
    }

    private static double ClampUnit(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : fallback;
}
