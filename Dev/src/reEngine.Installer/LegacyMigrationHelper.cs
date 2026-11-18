// @module: reEngine.Installer.LegacyMigrationHelper
// @purpose: Opt-in --migrate-legacy handler preserving data\rom\ and offering human-gated cleanup for stranded v0.1.1 artifacts.
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, System.Windows.Forms, Foundation.Core, reEngine.Core.Bootstrap
// @exports: LegacyMigrationResult, LegacyMigrationHelper

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
        string? installDir,
        out string? profileId,
        out string? suiteDir,
        out string? reEngineDir)
    {
        profileId = null;
        suiteDir = null;
        reEngineDir = null;
        if (string.IsNullOrWhiteSpace(installDir))
        {
            return false;
        }

        string fullInstall = Path.GetFullPath(installDir.Trim());
        string configSettings = Path.Combine(fullInstall, "data", "config", "settings.json");
        string legacyFlat = Path.Combine(fullInstall, "data", "settings.json");
        string path = File.Exists(configSettings) ? configSettings : legacyFlat;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return false;
            }

            if (root["installProfile"]?.GetValue<string>() is { } rawProfile && !string.IsNullOrWhiteSpace(rawProfile))
            {
                string trimmed = rawProfile.Trim().ToLowerInvariant();
                if (trimmed is "lightweight" or "full")
                {
                    profileId = trimmed;
                }
            }

            if (root["suiteInstallDir"]?.GetValue<string>() is { } rawSuite && !string.IsNullOrWhiteSpace(rawSuite))
            {
                suiteDir = Path.GetFullPath(rawSuite.Trim());
            }

            if (root["reEngineInstallDir"]?.GetValue<string>() is { } rawRe && !string.IsNullOrWhiteSpace(rawRe))
            {
                reEngineDir = Path.GetFullPath(rawRe.Trim());
            }

            return profileId is not null || suiteDir is not null || reEngineDir is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void PersistInstallProfileSettings(
        string installDir,
        string? profileId,
        string? suiteDir,
        string? reEngineDir)
    {
        string configDir = Path.Combine(Path.GetFullPath(installDir), "data", "config");
        Directory.CreateDirectory(configDir);
        string settingsPath = Path.Combine(configDir, "settings.json");

        string legacyFlatPath = Path.Combine(Path.GetFullPath(installDir), "data", "settings.json");
        string sourceToRead = File.Exists(settingsPath) ? settingsPath : legacyFlatPath;
        JsonObject root = File.Exists(sourceToRead)
            ? JsonNode.Parse(File.ReadAllText(sourceToRead)) as JsonObject ?? new JsonObject()
            : new JsonObject();

        root["schemaVersion"] = 1;
        root["appId"] = ReEnginePackagingConstants.AppId;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            root["installProfile"] = NormalizeProfileId(profileId);
        }
        else if (root["installProfile"] is null || string.IsNullOrWhiteSpace(root["installProfile"]?.ToString()))
        {
            root["installProfile"] = ReEnginePackagingConstants.DefaultInstallProfile;
        }

        if (!string.IsNullOrWhiteSpace(suiteDir))
        {
            root["suiteInstallDir"] = Path.GetFullPath(suiteDir);
        }
        if (!string.IsNullOrWhiteSpace(reEngineDir))
        {
            root["reEngineInstallDir"] = Path.GetFullPath(reEngineDir);
        }
        root["updatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        string tmpPath = settingsPath + ".tmp";
        File.WriteAllText(tmpPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmpPath, settingsPath, overwrite: true);
    }

    public static LegacyMigrationResult TryMigrateLegacyInstall(
        string targetInstallDir,
        bool isolatedTest,
        string? isolatedDataDir,
        bool silent,
        bool dryRun,
        bool confirmCleanupFlag)
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
            string legacyConfigSettings = Path.Combine(legacyRoot, "data", "config", "settings.json");
            string legacyFlatSettings = Path.Combine(legacyRoot, "data", "settings.json");
            string? legacySettings = File.Exists(legacyConfigSettings) ? legacyConfigSettings : (File.Exists(legacyFlatSettings) ? legacyFlatSettings : null);
            string targetSettings = Path.Combine(targetConfigDir, "settings.json");
            if (legacySettings is not null && !File.Exists(targetSettings))
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
                    string rel = Path.GetRelativePath(legacyRomDir, srcFile);
                    string dstFile = Path.Combine(targetRomDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                    if (!File.Exists(dstFile))
                    {
                        File.Copy(srcFile, dstFile, overwrite: false);
                        preservedRoms++;
                    }
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
                    "reEngine — Human-Gated Legacy Cleanup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                allowCleanup = choice == DialogResult.Yes;
            }
            else if (confirmCleanupFlag)
            {
                allowCleanup = true;
            }

            if (allowCleanup)
            {
                foreach (string item in stranded)
                {
                    if (Directory.Exists(item))
                    {
                        Directory.Delete(item, recursive: true);
                    }
                    else if (File.Exists(item))
                    {
                        File.Delete(item);
                    }
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
        if (Directory.Exists(legacyEngineApp))
        {
            stranded.Add(legacyEngineApp);
        }

        string legacyCrashDir = Path.Combine(legacyRoot, "logs", "crashes");
        if (Directory.Exists(legacyCrashDir))
        {
            foreach (string crash in Directory.EnumerateFiles(legacyCrashDir, "crash_*.json", SearchOption.TopDirectoryOnly))
            {
                stranded.Add(crash);
            }
        }

        return stranded;
    }
}
