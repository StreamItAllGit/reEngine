// @module: reEngine.App.AppFallbackSettingsStore
// @purpose: Temporary Standard 19 IReEngineSettingsStore atop Foundation.Core.GranularSettingsManager until Slice A HostSettingsStore merges.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, reEngine.Core.Updates
// @exports: AppFallbackSettingsStore

using Foundation.Core;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed class AppFallbackSettingsStore : IReEngineSettingsStore
{
    private readonly GranularSettingsManager _manager;
    private readonly string _corePath;
    private readonly string _videoPath;
    private readonly string _audioPath;
    private readonly string _controlsPath;

    public string ConfigDirectory { get; }

    public AppFallbackSettingsStore(string dataDirectory, string? coreConfigPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string fullDataDir = Path.GetFullPath(dataDirectory);
        ConfigDirectory = Path.Combine(fullDataDir, "config");
        _corePath = string.IsNullOrWhiteSpace(coreConfigPath)
            ? Path.Combine(ConfigDirectory, "settings.json")
            : Path.GetFullPath(coreConfigPath);
        _videoPath = Path.Combine(ConfigDirectory, "settings.video.json");
        _audioPath = Path.Combine(ConfigDirectory, "settings.audio.json");
        _controlsPath = Path.Combine(ConfigDirectory, "settings.controls.json");
        _manager = new GranularSettingsManager(fullDataDir);
    }

    public ReEngineSettingsBundle LoadOrMigrate()
    {
        Directory.CreateDirectory(ConfigDirectory);
        var diagnostics = new List<string>();

        SettingsLoadOutcome<ReEngineSettings> coreOutcome = _manager.LoadOrSelfHeal(
            _corePath,
            static () => new ReEngineSettings { SchemaVersion = 2, Domain = "core" });
        if (!string.IsNullOrWhiteSpace(coreOutcome.DiagnosticWarning)) diagnostics.Add(coreOutcome.DiagnosticWarning);

        ReEngineSettings core = coreOutcome.Value;
        bool wasV1 = coreOutcome.LoadedFromPrimary && core.SchemaVersion < 2;
        if (wasV1 && File.Exists(_corePath))
        {
            SnapshotPreMigrationV1Core(_corePath, _manager.ConfigHistoryDirectory);
        }

        bool videoMissing = !File.Exists(_videoPath);
        bool audioMissing = !File.Exists(_audioPath);
        ReEngineSettingsBundle legacyDerived = SettingsMigrationHelper.BuildBundleFromLegacyCore(core, _controlsPath);

        SettingsLoadOutcome<ReEngineVideoSettings> videoOutcome = _manager.LoadOrSelfHeal(
            _videoPath, () => legacyDerived.Video);
        if (!string.IsNullOrWhiteSpace(videoOutcome.DiagnosticWarning)) diagnostics.Add(videoOutcome.DiagnosticWarning);

        SettingsLoadOutcome<ReEngineAudioSettings> audioOutcome = _manager.LoadOrSelfHeal(
            _audioPath, () => legacyDerived.Audio);
        if (!string.IsNullOrWhiteSpace(audioOutcome.DiagnosticWarning)) diagnostics.Add(audioOutcome.DiagnosticWarning);

        ReEngineVideoSettings video = videoOutcome.Value;
        ReEngineAudioSettings audio = audioOutcome.Value;
        SettingsMigrationHelper.NormalizeCore(core);
        SettingsMigrationHelper.NormalizeVideo(video);
        SettingsMigrationHelper.NormalizeAudio(audio);

        var bundle = new ReEngineSettingsBundle(
            core,
            video,
            audio,
            _controlsPath,
            WasMigratedFromV1: wasV1,
            Diagnostics: diagnostics);
        SettingsMigrationHelper.SyncCoreLegacyMirror(bundle);

        if (wasV1 || videoMissing || audioMissing)
        {
            SaveAll(bundle);
            if (wasV1) diagnostics.Add("Migrated v1 settings.json into v2 core/video/audio domain files.");
        }

        return bundle;
    }

    public void SaveAll(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        SettingsMigrationHelper.SyncCoreLegacyMirror(bundle);
        SaveCore(bundle.Core);
        SaveVideo(bundle.Video);
        SaveAudio(bundle.Audio);
    }

    public void SaveCore(ReEngineSettings core)
    {
        ArgumentNullException.ThrowIfNull(core);
        SettingsMigrationHelper.NormalizeCore(core);
        _manager.SaveAtomically(_corePath, core);
    }

    public void SaveVideo(ReEngineVideoSettings video)
    {
        ArgumentNullException.ThrowIfNull(video);
        SettingsMigrationHelper.NormalizeVideo(video);
        _manager.SaveAtomically(_videoPath, video);
    }

    public void SaveAudio(ReEngineAudioSettings audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        SettingsMigrationHelper.NormalizeAudio(audio);
        _manager.SaveAtomically(_audioPath, audio);
    }

    private static void SnapshotPreMigrationV1Core(string corePath, string historyDir)
    {
        try
        {
            Directory.CreateDirectory(historyDir);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string dest = Path.Combine(historyDir, $"settings_v1_{stamp}_{Guid.NewGuid().ToString("N")[..6]}.json");
            File.Copy(corePath, dest, overwrite: true);
        }
        catch (IOException)
        {
            // Snapshot failure must not abort settings load/migration.
        }
    }
}
