// @module: reEngine.App.SettingsMigrationHelper
// @purpose: Merges split-brain legacy data\settings.json and app\data\settings.json atomically into data\config\settings.json.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text, System.Text.Json, Foundation.Core, reEngine.Core.Updates
// @exports: SettingsMigrationResult, SettingsMigrationHelper

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundation.Core;
using reEngine.Core.Updates;

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
}
