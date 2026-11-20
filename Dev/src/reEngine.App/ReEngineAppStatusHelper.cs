// @module: reEngine.App.ReEngineAppStatusHelper
// @purpose: Inspects .decomp toolchain dispatch readiness, <dataDir>\cache\ AOT cache metrics, and installProfile settings.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Text.Json, reEngine.Core.Resolution, reEngine.Core.Updates
// @exports: AotCacheStatusSummary, DecompDispatchStatusSummary, ReEngineAppStatusHelper

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using reEngine.Core.Decomp;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed record AotCacheStatusSummary(
    string CacheDirectory,
    int AssemblyCount,
    long TotalBytes)
{
    public string FormatSummary()
    {
        double mb = TotalBytes / (1024.0 * 1024.0);
        return $"AOT Cache: {AssemblyCount} assembly(ies) ({mb.ToString("0.00", CultureInfo.InvariantCulture)} MB)";
    }
}

internal sealed record DecompDispatchStatusSummary(
    bool ToolchainAvailable,
    string ToolchainDirectory,
    int InstalledEngineCount,
    string InstallProfile)
{
    public string FormatPillText() => ToolchainAvailable ? "DECOMP: FULL" : "DECOMP: ROUTER";

    public string FormatSummary() =>
        ToolchainAvailable
            ? $".decomp Dispatch: Ready (Offline Ghidra toolchain present, {InstalledEngineCount} engine(s))"
            : $".decomp Dispatch: Router-only (profile={InstallProfile}; {InstalledEngineCount} engine(s))";
}

internal static class ReEngineAppStatusHelper
{
    public const string InstallProfileKey = "installProfile";
    public const string JitBringupKey = "shouldAllowJitBringup";

    public static bool GetJitBringup(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsJitEnabled) return true;
        if (settings.ExtensionData is not null)
        {
            if (TryReadBool(settings.ExtensionData, JitBringupKey, out bool v)) return v;
            if (TryReadBool(settings.ExtensionData, "jitBringup", out bool legacy)) return legacy;
        }
        return false;
    }

    public static void SetJitBringup(ReEngineSettings settings, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.IsJitEnabled = enabled;
        if (!enabled)
        {
            settings.ExecutionTierMode = "staticAotOnly";
        }
        else if (string.IsNullOrWhiteSpace(settings.ExecutionTierMode) ||
                 string.Equals(settings.ExecutionTierMode, "staticAotOnly", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(settings.ExecutionTierMode, "interpreterOracle", StringComparison.OrdinalIgnoreCase))
        {
            settings.ExecutionTierMode = "jitBringup";
        }

        settings.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        settings.ExtensionData[JitBringupKey] = JsonSerializer.SerializeToElement(enabled);
        if (!enabled) settings.ExtensionData.Remove("jitBringup");
    }

    private static bool TryReadBool(IDictionary<string, JsonElement> data, string key, out bool value)
    {
        if (data.TryGetValue(key, out JsonElement el) &&
            el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }
        value = false;
        return false;
    }

    public static string GetInstallProfile(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ExtensionData is not null &&
            settings.ExtensionData.TryGetValue(InstallProfileKey, out JsonElement el) &&
            el.ValueKind == JsonValueKind.String)
        {
            string? raw = el.GetString();
            if (string.Equals(raw, "full", StringComparison.OrdinalIgnoreCase))
            {
                return "full";
            }
        }
        return "lightweight";
    }

    public static void SetInstallProfile(ReEngineSettings settings, string? profile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalized = string.Equals(profile?.Trim(), "full", StringComparison.OrdinalIgnoreCase)
            ? "full"
            : "lightweight";

        settings.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        settings.ExtensionData[InstallProfileKey] = JsonSerializer.SerializeToElement(normalized);
    }

    public static AotCacheStatusSummary InspectAotCache(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string cacheDir = Path.Combine(Path.GetFullPath(dataDirectory), "cache");
        if (!Directory.Exists(cacheDir))
        {
            return new AotCacheStatusSummary(cacheDir, 0, 0);
        }

        int count = 0;
        long bytes = 0;
        foreach (string dll in Directory.EnumerateFiles(cacheDir, "*.Generated.dll", SearchOption.AllDirectories))
        {
            count++;
            try
            {
                bytes += new FileInfo(dll).Length;
            }
            catch (IOException ex)
            {
                Trace.WriteLine($"Skipped locked AOT assembly size check for '{dll}': {ex.Message}");
            }
        }

        return new AotCacheStatusSummary(cacheDir, count, bytes);
    }

    public static string ResolveAppInstallRoot()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(baseDir), "app", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(baseDir);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                return parent;
            }
        }

        return baseDir;
    }

    public static string? TryReadSettingPath(ReEngineSettings settings, string key)
    {
        if (settings.ExtensionData is null || !settings.ExtensionData.TryGetValue(key, out JsonElement elem))
        {
            return null;
        }

        if (elem.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? raw = elem.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
        return Path.GetFullPath(expanded);
    }

    public static DecompDispatchStatusSummary InspectDecompStatus(
        string enginesDir,
        ReEngineSettings settings,
        int? precomputedEngineCount = null)
    {
        string? explicitSuiteToolchain = null;
        string? suiteSetting = TryReadSettingPath(settings, "suiteInstallDir");
        if (!string.IsNullOrWhiteSpace(suiteSetting))
        {
            string suiteCandidate = Path.Combine(suiteSetting, "toolchain");
            if (HasNonEmptyToolchainDir(suiteCandidate))
            {
                explicitSuiteToolchain = suiteCandidate;
            }
        }

        string? reEngineSetting = TryReadSettingPath(settings, "reEngineInstallDir");
        string installRoot = !string.IsNullOrWhiteSpace(reEngineSetting) ? reEngineSetting : ResolveAppInstallRoot();
        string toolchainDir = explicitSuiteToolchain
            ?? OfflineToolchainLocator.ResolveToolchainDirectory(installRoot);

        if (!HasNonEmptyToolchainDir(toolchainDir) && !string.IsNullOrWhiteSpace(enginesDir))
        {
            string? enginesParent = Path.GetDirectoryName(
                Path.GetFullPath(enginesDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(enginesParent) &&
                !string.Equals(enginesParent, Path.GetPathRoot(enginesParent), StringComparison.OrdinalIgnoreCase))
            {
                string siblingCandidate = Path.Combine(enginesParent, "toolchain");
                if (HasNonEmptyToolchainDir(siblingCandidate))
                {
                    toolchainDir = siblingCandidate;
                }
            }
        }

        bool hasToolchain = HasNonEmptyToolchainDir(toolchainDir) &&
                            !Directory.Exists(Path.Combine(toolchainDir, "ghidra", "GPL"));

        int engineCount = precomputedEngineCount ??
                          new EngineVersionResolver().DiscoverInstalledEngines(enginesDir).Count;

        return new DecompDispatchStatusSummary(
            ToolchainAvailable: hasToolchain,
            ToolchainDirectory: toolchainDir,
            InstalledEngineCount: engineCount,
            InstallProfile: GetInstallProfile(settings));
    }

    private static bool HasNonEmptyToolchainDir(string toolchainDir) =>
        Directory.Exists(toolchainDir) && Directory.EnumerateFileSystemEntries(toolchainDir).Any();
}
