// @module: reEngine.Tests.HostExecutionResilienceAndSettingsSplitTests
// @purpose: xUnit tests for BootCrashSentinel, MediaRamPreloader, ExecutionTierPolicy, SettingsDomainStore v1->v2 split + quarantine, and Safe Mode.
// @depends-on: System, System.IO, System.Text.Json, reEngine.Core, reEngine.Host, Xunit
// @exports: HostExecutionResilienceAndSettingsSplitTests

using System.Text.Json;
using System.Text.Json.Nodes;
using reEngine.Core.Execution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;
using reEngine.Host.Settings;
using Xunit;

namespace reEngine.Tests;

public sealed class HostExecutionResilienceAndSettingsSplitTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_t1_" + Guid.NewGuid().ToString("N"));

    public HostExecutionResilienceAndSettingsSplitTests() => Directory.CreateDirectory(_tempRoot);

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(ex.Message); }
    }

    [Fact]
    public void BootCrashSentinel_DetectsDeadPidTriggersSafeMode_AndClearsOnFirstFrame()
    {
        string dataDir = Path.Combine(_tempRoot, "sentinel_data");
        Directory.CreateDirectory(dataDir);
        var sentinel = new BootCrashSentinel(dataDir);

        BootCrashCheckResult clean = sentinel.BeginBoot("crown-depths");
        Assert.False(clean.IsSafeModeTriggered);
        Assert.False(clean.HadPreviousSentinel);
        Assert.True(File.Exists(sentinel.SentinelPath));

        // Same live process does not false-trigger Safe Mode.
        BootCrashCheckResult liveCheck = sentinel.InspectExisting();
        Assert.True(liveCheck.HadPreviousSentinel);
        Assert.True(liveCheck.IsPreviousProcessAlive);
        Assert.False(liveCheck.IsSafeModeTriggered);

        // Simulated crash from dead PID triggers Safe Mode with cache rebuild guidance.
        File.WriteAllText(sentinel.SentinelPath, """{ "pid": 99999997, "moduleId": "crown-depths", "startedAtUtc": "2026-01-01T00:00:00Z" }""");
        BootCrashCheckResult crashed = sentinel.InspectExisting();
        Assert.True(crashed.IsSafeModeTriggered);
        Assert.True(crashed.HadPreviousSentinel);
        Assert.False(crashed.IsPreviousProcessAlive);
        Assert.Contains("cache", crashed.CacheRebuildGuidance ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // Explicit --safe-mode-reset suppresses Safe Mode on the stale dead-PID sentinel.
        BootCrashCheckResult resetBypass = sentinel.InspectExisting(wasSafeModeReset: true);
        Assert.False(resetBypass.IsSafeModeTriggered);

        Assert.True(sentinel.ClearOnFirstFrame());
        Assert.False(File.Exists(sentinel.SentinelPath));
    }

    [Fact]
    public void MediaRamPreloader_EnforcesMemoryHeadroomContainmentAndStreamingFallback()
    {
        const long OneGiB = 1024L * 1024 * 1024;
        Assert.True(MediaRamPreloader.IsSafeToPreload(256L * 1024 * 1024, OneGiB));
        Assert.False(MediaRamPreloader.IsSafeToPreload(600L * 1024 * 1024, OneGiB));
        Assert.False(MediaRamPreloader.IsSafeToPreload(800L * 1024 * 1024, OneGiB));
        // 3200 MiB on 4 GiB leaves 896 MiB (> 512 MiB headroom), yet exceeds 75% (3072 MiB).
        Assert.False(MediaRamPreloader.IsSafeToPreload(3200L * 1024 * 1024, 4L * OneGiB));

        string romRoot = Path.Combine(_tempRoot, "rom_root");
        Directory.CreateDirectory(romRoot);
        string discPath = Path.Combine(romRoot, "sample.bin");
        byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        File.WriteAllBytes(discPath, payload);

        using MediaRamPreloadResult ok = MediaRamPreloader.TryPreload(discPath, romRoot, availablePhysicalBytesOverride: 2L * OneGiB);
        Assert.True(ok.Preloaded);
        Assert.False(ok.FallbackToStreaming);
        Assert.NotNull(ok.Buffer);
        Assert.Equal(payload, ok.Buffer!.AsSpan().ToArray());

        using MediaRamPreloadResult lowRam = MediaRamPreloader.TryPreload(discPath, romRoot, availablePhysicalBytesOverride: 256L * 1024 * 1024);
        Assert.False(lowRam.Preloaded);
        Assert.True(lowRam.FallbackToStreaming);
        Assert.Contains("[WARN]", lowRam.DiagnosticMessage, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            MediaRamPreloader.TryPreload(Path.Combine(romRoot, "..", "outside.bin"), romRoot, OneGiB));
    }

    [Fact]
    public void ExecutionTierPolicy_EnforcesStaticAot_QuarantinesOracleToCli_AndBlocksRawBootWithoutJit()
    {
        ExecutionTierEvaluation persistedOracle = ExecutionTierPolicy.Evaluate(
            isJitEnabled: false,
            requestedMode: ExecutionTierPolicy.InterpreterOracle,
            scope: ExecutionTierScope.PersistedSetting);
        Assert.False(persistedOracle.IsAllowed);
        Assert.Equal(ExecutionTierPolicy.StaticAotOnly, persistedOracle.EffectiveMode);

        ExecutionTierEvaluation cliOracle = ExecutionTierPolicy.Evaluate(
            isJitEnabled: false,
            requestedMode: ExecutionTierPolicy.InterpreterOracle,
            scope: ExecutionTierScope.AllowCliOnly);
        Assert.True(cliOracle.IsAllowed);
        Assert.True(cliOracle.IsInterpreterOracleQuarantined);
        Assert.Equal(ExecutionTierPolicy.InterpreterOracle, cliOracle.EffectiveMode);

        ExecutionTierEvaluation rawBlocked = ExecutionTierPolicy.Evaluate(
            isJitEnabled: false,
            requestedMode: ExecutionTierPolicy.StaticAotOnly,
            scope: ExecutionTierScope.PersistedSetting,
            isRawMediaBoot: true);
        Assert.False(rawBlocked.IsAllowed);
        Assert.Equal(ExecutionTierPolicy.StaticAotOnly, ExecutionTierPolicy.NormalizeForPersistence(false, ExecutionTierPolicy.JitBringup));
    }

    [Fact]
    public void SettingsDomainStore_MigratesV1ToV2_QuarantinesCorruptJson_AndRecoversFromBackup()
    {
        string dataDir = Path.Combine(_tempRoot, "cfg_data");
        string cfgDir = Path.Combine(dataDir, "config");
        Directory.CreateDirectory(cfgDir);
        string corePath = Path.Combine(cfgDir, "settings.json");

        File.WriteAllText(corePath, """
        {
          "schemaVersion": 1,
          "preferredResolutionScale": 4,
          "backendId": "vulkan",
          "aspectRatioMode": "horPlus21By9",
          "masterVolume": 0.42,
          "audioLowPassHz": 4800,
          "customExtensionFlag": "preserve-me"
        }
        """);

        var store = new SettingsDomainStore(dataDir);
        ReEngineSettingsBundle migrated = store.LoadOrMigrate();
        Assert.True(migrated.WasMigratedFromV1);
        Assert.Equal(2, migrated.Core.SchemaVersion);
        Assert.Equal(4, migrated.Video.ResolutionScale);
        Assert.Equal("vulkan", migrated.Video.BackendId);
        Assert.Equal(0.42, migrated.Audio.MasterVolume, 3);
        Assert.Equal(4800, migrated.Audio.AudioLowPassHz);
        Assert.True(File.Exists(Path.Combine(cfgDir, "settings.video.json")));
        Assert.True(File.Exists(Path.Combine(cfgDir, "settings.audio.json")));
        Assert.True(Directory.GetFiles(Path.Combine(dataDir, "config_history"), "*_v1.json").Length >= 1);

        JsonObject coreDoc = JsonNode.Parse(File.ReadAllText(corePath))!.AsObject();
        Assert.Null(coreDoc["backendId"]);
        Assert.Null(coreDoc["masterVolume"]);
        Assert.Equal("preserve-me", coreDoc["customExtensionFlag"]?.GetValue<string>());

        // Save once so .bak / history exists, then corrupt primary settings.video.json.
        migrated.Video.ResolutionScale = 8;
        store.SaveVideo(migrated.Video);
        string videoPath = Path.Combine(cfgDir, "settings.video.json");
        File.WriteAllText(videoPath, "{ corrupt-json !!!");

        ReEngineSettingsBundle recovered = store.LoadOrMigrate();
        Assert.True(Directory.GetFiles(store.QuarantineDirectory, "settings.video.corrupt_*.json").Length >= 1);
        Assert.Equal("vulkan", recovered.Video.BackendId);
        Assert.Contains(recovered.Diagnostics ?? [], d => d.Contains("Quarantined", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HostSettingsStore_AppliesInMemorySafeModeWithoutMutatingDiskConfig()
    {
        string dataDir = Path.Combine(_tempRoot, "safe_mode_data");
        var hostStore = new HostSettingsStore(dataDir);
        ReEngineSettingsBundle bundle = hostStore.LoadOrMigrate();
        bundle.Video.BackendId = "vulkan";
        bundle.Video.IsHdTexturePackEnabled = true;
        bundle.Core.ShouldPreloadMediaToRam = true;
        hostStore.SaveAll(bundle);

        var baseOpts = new HostSessionOptions
        {
            PreferredBackend = HostRenderBackendKind.Vulkan,
            IsHdTexturePackEnabled = true,
            ExecutionResilience = hostStore.ToExecutionResilienceOptions(bundle.Core)
        };
        Assert.Equal(HostRenderBackendKind.Vulkan, baseOpts.PreferredBackend);
        Assert.True(baseOpts.IsHdTexturePackEnabled);

        HostSessionOptions safeOpts = HostSettingsStore.ApplyInMemorySafeMode(
            baseOpts,
            new BootCrashCheckResult { IsSafeModeTriggered = true });
        Assert.Equal(HostRenderBackendKind.OpenGl, safeOpts.PreferredBackend);
        Assert.False(safeOpts.IsHdTexturePackEnabled);
        Assert.True(safeOpts.ExecutionResilience?.IsSafeModeActive);

        // Disk config remains untouched while Safe Mode is active in memory.
        ReEngineSettingsBundle reloaded = hostStore.LoadOrMigrate();
        Assert.Equal("vulkan", reloaded.Video.BackendId);
        Assert.True(reloaded.Video.IsHdTexturePackEnabled);

        ReEngineSettingsBundle cleared = hostStore.ClearSafeMode(reloaded);
        Assert.False(cleared.Core.IsSafeModeActive);
    }

    [Fact]
    public void HostSessionRuntimeBridge_EnforcesPersistedSafeModeWithoutSentinel_AndClearsOnDrawerReset()
    {
        string dataDir = Path.Combine(_tempRoot, "persisted_safe_data");
        Directory.CreateDirectory(dataDir);
        bool? callbackValue = null;
        string? banner = null;

        using var bridge = new reEngine.Host.Core.HostSessionRuntimeBridge();
        var input = new HostSessionOptions
        {
            PreferredBackend = HostRenderBackendKind.Vulkan,
            IsHdTexturePackEnabled = true,
            SafeModeCallback = active => callbackValue = active,
            ExecutionResilience = new HostExecutionResilienceOptions
            {
                DataDirectory = dataDir,
                ModuleId = "host-demo",
                IsSafeModeActive = true
            }
        };

        HostSessionOptions effective = bridge.PrepareExecution(input, ref banner);
        Assert.True(bridge.IsSafeModeActive);
        Assert.Equal(HostRenderBackendKind.OpenGl, effective.PreferredBackend);
        Assert.False(effective.IsHdTexturePackEnabled);
        Assert.Contains("Safe Mode", banner ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Null(callbackValue);

        var drawer = new reEngine.Host.Ui.F1SettingsDrawerView
        {
            ActiveResilience = effective.ExecutionResilience! with { IsSafeModeActive = false }
        };
        bridge.SyncSafeModeResetIfCleared(drawer);
        Assert.False(bridge.IsSafeModeActive);
        Assert.False(callbackValue);
        bridge.ClearSentinelOnFirstFrame();
        Assert.False(File.Exists(Path.Combine(dataDir, BootCrashSentinel.SentinelFileName)));
    }
}
