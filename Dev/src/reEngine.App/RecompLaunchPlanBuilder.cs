// @module: reEngine.App.RecompLaunchPlanBuilder
// @purpose: Builds isolated child process launch plans delegating protocol-gated passthrough flags to RecompLaunchFlagRouter.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Text.Json, System.Text.RegularExpressions, Foundation.Core, reEngine.Core
// @exports: ChildLaunchPlan, RecompLaunchPlanBuilder

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foundation.Core;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed record ChildLaunchPlan(
    string ModuleDataDirectory,
    string ModuleConfigPath,
    string ModuleLogDirectory,
    IReadOnlyList<string> Arguments);

internal static partial class RecompLaunchPlanBuilder
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleSlugRegex();

    public static ChildLaunchPlan Build(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions = null,
        InstalledEngineCandidate? selectedEngine = null,
        Action<string, string>? log = null,
        ReEngineSettings? settings = null)
    {
        ReEngineSettingsBundle? bundle = settings is null ? null : SettingsMigrationHelper.BuildBundleFromLegacyCore(settings);
        return Build(loaded, appEnv, containmentRoot, launchOptions, selectedEngine, log, bundle);
    }

    public static ChildLaunchPlan Build(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions,
        InstalledEngineCandidate? selectedEngine,
        Action<string, string>? log,
        ReEngineSettingsBundle? settingsBundle)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(appEnv);

        RecompLaunchOptions opts = launchOptions ?? new RecompLaunchOptions();
        string moduleId = loaded.Manifest.ModuleId?.Trim() ?? string.Empty;
        if (!ModuleSlugRegex().IsMatch(moduleId))
        {
            throw new InvalidOperationException(
                $"Fail-Closed Containment Violation: invalid moduleId '{loaded.Manifest.ModuleId}'; expected lowercase slug matching ^[a-z0-9][a-z0-9-]*$.");
        }

        string? effectiveBoundary = !string.IsNullOrWhiteSpace(containmentRoot)
            ? containmentRoot
            : (IsIsolatedTestEnv() ? appEnv.DataDirectory : null);

        string moduleDataDir = Path.GetFullPath(Path.Combine(appEnv.DataDirectory, "modules", moduleId));
        if (effectiveBoundary is not null) moduleDataDir = AppEnvironment.AssertPathContained(moduleDataDir, effectiveBoundary);

        string moduleConfigPath = Path.Combine(moduleDataDir, "settings.json");
        if (!string.IsNullOrWhiteSpace(opts.ConfigPathOverride))
        {
            string candidateConfig = Path.GetFullPath(opts.ConfigPathOverride.Trim());
            if (IsPathInsideDirectory(candidateConfig, moduleDataDir)) moduleConfigPath = candidateConfig;
        }
        if (effectiveBoundary is not null) moduleConfigPath = AppEnvironment.AssertPathContained(moduleConfigPath, effectiveBoundary);

        string moduleLogDir = !string.IsNullOrWhiteSpace(opts.LogDirectoryOverride)
            ? Path.GetFullPath(opts.LogDirectoryOverride.Trim())
            : Path.Combine(moduleDataDir, "logs");
        if (effectiveBoundary is not null) moduleLogDir = AppEnvironment.AssertPathContained(moduleLogDir, effectiveBoundary);

        string? fixturePath = ResolveOptionalContainedPath(opts.FixturePath, effectiveBoundary);
        string? screenshotPath = ResolveOptionalContainedPath(opts.DumpScreenshotPath, effectiveBoundary);
        string? audioPath = ResolveOptionalContainedPath(opts.DumpAudioPath, effectiveBoundary);
        string? saveDir = ResolveOptionalContainedPath(opts.SaveDirOverride, effectiveBoundary);
        string? preloadMediaPath = ResolvePreloadMediaPath(loaded, appEnv, opts, selectedEngine, effectiveBoundary);

        var args = new List<string> { "--module", loaded.PackagePath };
        if (!string.IsNullOrWhiteSpace(preloadMediaPath))
        {
            args.Add("--preload-media");
            args.Add(preloadMediaPath);
        }
        if (opts.ShouldWaitForExit) args.Add("--wait");
        if (opts.TrayOnly || appEnv.TrayOnly) args.Add("--tray-only");
        if (appEnv.DryRun) args.Add("--dry-run");

        args.Add("--data-dir"); args.Add(moduleDataDir);
        args.Add("--config"); args.Add(moduleConfigPath);
        args.Add("--log-dir"); args.Add(moduleLogDir);

        int? effectiveScale = opts.ResolutionScale ?? settingsBundle?.Video.ResolutionScale ?? settingsBundle?.Core.ResolutionScale;
        if (effectiveScale is int scale && scale is 1 or 2 or 4 or 8)
        {
            args.Add("--scale");
            args.Add(scale.ToString(CultureInfo.InvariantCulture));
        }

        if (saveDir is not null) { args.Add("--save-dir"); args.Add(saveDir); }
        if (opts.ShouldEnableRuntimeLogs) args.Add("--enable-logs");
        if (opts.Frames is int frames) { args.Add("--frames"); args.Add(frames.ToString(CultureInfo.InvariantCulture)); }
        if (fixturePath is not null) { args.Add("--fixture"); args.Add(fixturePath); }
        if (screenshotPath is not null) { args.Add("--dump-screenshot"); args.Add(screenshotPath); }
        if (audioPath is not null) { args.Add("--dump-audio"); args.Add(audioPath); }

        RecompLaunchFlagRouter.AppendRoutedFlags(args, opts, loaded.Manifest, selectedEngine, settingsBundle, log);
        return new ChildLaunchPlan(moduleDataDir, moduleConfigPath, moduleLogDir, args);
    }

    internal static bool Supports030Routing(InstalledEngineCandidate engine) =>
        RecompLaunchFlagRouter.Supports030Routing(engine);

    internal static bool Supports060Routing(InstalledEngineCandidate engine) =>
        RecompLaunchFlagRouter.Supports060Routing(engine);

    internal static bool Supports061Routing(InstalledEngineCandidate engine) =>
        RecompLaunchFlagRouter.Supports061Routing(engine);

    private static string? ResolvePreloadMediaPath(
        RecompPackageLoadResult loaded, AppEnvironment appEnv, RecompLaunchOptions opts,
        InstalledEngineCandidate? selectedEngine, string? boundary)
    {
        if (!string.IsNullOrWhiteSpace(opts.MediaPathOverride))
            return ResolveOptionalContainedPath(opts.MediaPathOverride, boundary);
        if (!string.IsNullOrWhiteSpace(loaded.PreloadMedia.PrimaryMediaPath))
            return loaded.PreloadMedia.PrimaryMediaPath;

        string? moduleRoot = Path.GetDirectoryName(loaded.PackagePath);
        string? engineFamilyDir = selectedEngine is not null ? Path.GetDirectoryName(selectedEngine.EngineDirectory) : null;
        string? enginesRoot = engineFamilyDir is not null ? Path.GetDirectoryName(engineFamilyDir) : null;
        if (enginesRoot is not null && !string.Equals(Path.GetFileName(enginesRoot), "engines", StringComparison.OrdinalIgnoreCase))
        {
            enginesRoot = null;
        }
        string? suiteRoot = SuiteMediaTreeWalker.DeriveSuiteRootDirectory(enginesRoot, appEnv.DataDirectory, boundary);
        string? expectedSha = loaded.Manifest.ExtensionData is not null &&
                              loaded.Manifest.ExtensionData.TryGetValue("expectedSha256", out JsonElement shaEl) &&
                              shaEl.ValueKind == JsonValueKind.String
            ? shaEl.GetString()
            : null;
        SuiteMediaDiscoveryResult walk = SuiteMediaTreeWalker.Discover(
            moduleRoot, suiteRoot, loaded.Manifest.ModuleId, expectedSha, loaded.Manifest.PreloadRomDir, boundary);
        return walk.Found ? walk.PrimaryMediaPath : null;
    }

    private static string? ResolveOptionalContainedPath(string? rawPath, string? boundary)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        string fullPath = Path.GetFullPath(rawPath.Trim());
        return boundary is not null ? AppEnvironment.AssertPathContained(fullPath, boundary) : fullPath;
    }

    private static bool IsPathInsideDirectory(string candidatePath, string rootDir)
    {
        string fullCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIsolatedTestEnv()
    {
        string? value = Environment.GetEnvironmentVariable("APP_ISOLATED_TEST");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
