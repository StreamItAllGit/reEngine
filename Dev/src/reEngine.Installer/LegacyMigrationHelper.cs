// @module: reEngine.Installer.LegacyMigrationHelper
// @purpose: Opt-in --migrate-legacy handler preserving data\rom\ and offering human-gated cleanup for stranded v0.1.1 artifacts.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, System.Windows.Forms, Foundation.Core, reEngine.Core.Bootstrap
// @exports: LegacyMigrationResult, LegacyMigrationHelper

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Foundation.Core;
using reEngine.Core.Bootstrap;

namespace reEngine.Installer;

internal sealed record LegacyMigrationResult(
    bool MigratedSettings,
    int PreservedRomFiles,
    IReadOnlyList<string> StrandedArtifactsFound,
    bool StrandedArtifactsCleaned,
    string Summary);

internal static class LegacyMigrationHelper
{
    public static string NormalizeProfileId(string? rawProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile))
        {
            return ReEnginePackagingConstants.DefaultInstallProfile;
        }

        string trimmed = rawProfile.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "lightweight" => "lightweight",
            "full" => "full",
            _ => throw new ArgumentException($"Invalid --profile '{rawProfile}'. Expected 'Lightweight' or 'Full'.")
        };
    }

    public static string? ResolveLegacyRoot(bool isolatedTest, string? isolatedDataDir)
    {
        if (isolatedTest)
        {
            if (string.IsNullOrWhiteSpace(isolatedDataDir))
            {
                return null;
            }

            string fullIsolatedRoot = Path.GetFullPath(isolatedDataDir);
            string candidate = Path.Combine(fullIsolatedRoot, "legacy-programs", ReEnginePackagingConstants.DisplayName);
            return AppEnvironment.AssertPathContained(candidate, fullIsolatedRoot);
        }

        string? envLegacy = Environment.GetEnvironmentVariable("REENGINE_LEGACY_DIR");
        if (!string.IsNullOrWhiteSpace(envLegacy))
        {
            return Path.GetFullPath(envLegacy.Trim());
        }

        string driveRoot = $"{(char)68}:{Path.DirectorySeparatorChar}";
        return Path.GetFullPath(Path.Combine(driveRoot, "Programs", ReEnginePackagingConstants.DisplayName));
    }

    public static bool TryReadExistingInstallSettings(
        string? installDir, out string? profileId, out string? suiteDir, out string? reEngineDir)
    {
        profileId = null;
        suiteDir = null;
        reEngineDir = null;
        if (string.IsNullOrWhiteSpace(installDir)) return false;

        string sourcePath = ResolveSettingsPaths(installDir.Trim(), out _);
        JsonObject? root = TryLoadSettingsObject(sourcePath);
        if (root is null) return false;

        if (TryGetString(root, "installProfile", out string rawProfile))
        {
            string trimmed = rawProfile.Trim().ToLowerInvariant();
            if (trimmed is "lightweight" or "full") profileId = trimmed;
        }
        if (TryGetString(root, "suiteInstallDir", out string rawSuite)) suiteDir = Path.GetFullPath(rawSuite.Trim());
        if (TryGetString(root, "reEngineInstallDir", out string rawRe)) reEngineDir = Path.GetFullPath(rawRe.Trim());
        return profileId is not null || suiteDir is not null || reEngineDir is not null;
    }

    public static void PersistInstallProfileSettings(
        string installDir, string? profileId, string? suiteDir, string? reEngineDir)
    {
        string sourceToRead = ResolveSettingsPaths(installDir, out string settingsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        bool hasExisting = File.Exists(sourceToRead);
        JsonObject root = TryLoadSettingsObject(sourceToRead) ?? new JsonObject();

        SetCanonicalNode(root, "schemaVersion", 1);
        SetCanonicalNode(root, "appId", ReEnginePackagingConstants.AppId);
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            SetCanonicalNode(root, "installProfile", NormalizeProfileId(profileId));
        }
        else if (TryGetString(root, "installProfile", out string existingProfile))
        {
            string lower = existingProfile.Trim().ToLowerInvariant();
            SetCanonicalNode(root, "installProfile", lower is "lightweight" or "full" ? lower : existingProfile.Trim());
        }
        else
        {
            SetCanonicalNode(root, "installProfile", ReEnginePackagingConstants.DefaultInstallProfile);
        }

        if (!string.IsNullOrWhiteSpace(suiteDir)) SetCanonicalNode(root, "suiteInstallDir", Path.GetFullPath(suiteDir));
        if (!string.IsNullOrWhiteSpace(reEngineDir)) SetCanonicalNode(root, "reEngineInstallDir", Path.GetFullPath(reEngineDir));
        SetCanonicalNode(root, "updatedAtUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

        string tmpPath = settingsPath + ".tmp";
        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(tmpPath, json, new UTF8Encoding(false));
        if (hasExisting) File.Copy(sourceToRead, settingsPath + ".bak", overwrite: true);
        File.Move(tmpPath, settingsPath, overwrite: true);
    }

    private static string ResolveSettingsPaths(string installDir, out string canonicalSettingsPath)
    {
        string fullInstall = Path.GetFullPath(installDir);
        canonicalSettingsPath = Path.Combine(fullInstall, "data", "config", "settings.json");
        string legacyFlatPath = Path.Combine(fullInstall, "data", "settings.json");
        return File.Exists(canonicalSettingsPath) ? canonicalSettingsPath : legacyFlatPath;
    }

    private static JsonObject? TryLoadSettingsObject(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    private static bool TryGetString(JsonObject root, string key, out string value)
    {
        foreach (KeyValuePair<string, JsonNode?> kv in root)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) &&
                kv.Value is JsonValue jv && jv.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                value = s;
                return true;
            }
        }
        value = string.Empty;
        return false;
    }

    private static void SetCanonicalNode(JsonObject root, string key, JsonNode value)
    {
        foreach (string stale in root.Where(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) && kv.Key != key).Select(kv => kv.Key).ToList())
        {
            root.Remove(stale);
        }
        root[key] = value;
    }

    public static LegacyMigrationResult TryMigrateLegacyInstall(
        string targetInstallDir, bool isolatedTest, string? isolatedDataDir, bool silent, bool dryRun, bool confirmCleanupFlag)
    {
        string? legacyRoot = ResolveLegacyRoot(isolatedTest, isolatedDataDir);
        if (string.IsNullOrWhiteSpace(legacyRoot) || !Directory.Exists(legacyRoot))
        {
            return new LegacyMigrationResult(false, 0, Array.Empty<string>(), false, "No legacy installation found.");
        }

        bool migratedSettings = false;
        int preservedRoms = 0;
        string targetDataDir = Path.Combine(Path.GetFullPath(targetInstallDir), "data");

        if (!dryRun)
        {
            string targetConfigDir = Path.Combine(targetDataDir, "config");
            Directory.CreateDirectory(targetConfigDir);
            string legacySettings = ResolveSettingsPaths(legacyRoot, out _);
            string targetSettings = Path.Combine(targetConfigDir, "settings.json");
            if (File.Exists(legacySettings) && !File.Exists(targetSettings))
            {
                File.Copy(legacySettings, targetSettings, overwrite: false);
                migratedSettings = true;
            }

            string legacyRomDir = Path.Combine(legacyRoot, "data", "rom");
            if (Directory.Exists(legacyRomDir))
            {
                string targetRomDir = Path.Combine(targetDataDir, "rom");
                foreach (string srcFile in Directory.EnumerateFiles(legacyRomDir, "*", SearchOption.AllDirectories))
                {
                    string dstFile = Path.Combine(targetRomDir, Path.GetRelativePath(legacyRomDir, srcFile));
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                    if (!File.Exists(dstFile)) { File.Copy(srcFile, dstFile, overwrite: false); preservedRoms++; }
                }
            }
        }

        List<string> stranded = FindStrandedV011Artifacts(legacyRoot);
        bool cleaned = false;
        if (stranded.Count > 0 && !dryRun)
        {
            bool allowCleanup = false;
            if (!silent && !isolatedTest && OperatingSystem.IsWindows())
            {
                DialogResult choice = MessageBox.Show(
                    $"Found {stranded.Count} stranded legacy v0.1.1 artifact(s) under '{legacyRoot}'.\n\n" +
                    "Would you like to remove these stranded v0.1.1 artifacts now? (Your data\\rom\\ files are preserved.)",
                    "reEngine — Human-Gated Legacy Cleanup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                allowCleanup = choice == DialogResult.Yes;
            }
            else if (confirmCleanupFlag) allowCleanup = true;

            if (allowCleanup)
            {
                foreach (string item in stranded)
                {
                    if (Directory.Exists(item)) Directory.Delete(item, recursive: true);
                    else if (File.Exists(item)) File.Delete(item);
                }
                cleaned = true;
            }
        }

        string summary = $"Legacy migration from '{legacyRoot}': settings={(migratedSettings ? "migrated" : "unchanged")}, " +
                         $"romsPreserved={preservedRoms}, strandedFound={stranded.Count}, strandedCleaned={cleaned}.";
        return new LegacyMigrationResult(migratedSettings, preservedRoms, stranded, cleaned, summary);
    }

    private static List<string> FindStrandedV011Artifacts(string legacyRoot)
    {
        var stranded = new List<string>();
        string legacyEngineApp = Path.Combine(legacyRoot, "engines", "reStation", "v0.1.1", "app");
        if (Directory.Exists(legacyEngineApp)) stranded.Add(legacyEngineApp);

        string legacyCrashDir = Path.Combine(legacyRoot, "logs", "crashes");
        if (Directory.Exists(legacyCrashDir)) stranded.AddRange(Directory.EnumerateFiles(legacyCrashDir, "crash_*.json", SearchOption.TopDirectoryOnly));
        return stranded;
    }
}
