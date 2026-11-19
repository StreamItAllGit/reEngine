// @module: reEngine.App.RecompLaunchPlanBuilder
// @purpose: Builds isolated child process launch plans with 0.3.x backend/JIT/automation passthrough and 0.2.x fallback compatibility.
// @depends-on: System, System.Collections.Generic, System.Globalization, System.IO, System.Linq, System.Text.Json, System.Text.RegularExpressions, Foundation.Core, reEngine.Core
// @exports: ChildLaunchPlan, RecompLaunchPlanBuilder

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foundation.Core;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;

namespace reEngine.App;

internal sealed record ChildLaunchPlan(
    string ModuleDataDirectory,
    string ModuleConfigPath,
    string ModuleLogDirectory,
    IReadOnlyList<string> Arguments);

internal static partial class RecompLaunchPlanBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleSlugRegex();

    public static ChildLaunchPlan Build(
        RecompPackageLoadResult loaded,
        AppEnvironment appEnv,
        string? containmentRoot,
        RecompLaunchOptions? launchOptions = null,
        InstalledEngineCandidate? selectedEngine = null,
        Action<string, string>? log = null)
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

        if (opts.ResolutionScale is int scale && scale is 1 or 2 or 4 or 8)
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

        AppendV030Flags(args, opts, loaded.Manifest, selectedEngine, log);
        return new ChildLaunchPlan(moduleDataDir, moduleConfigPath, moduleLogDir, args);
    }

    private static readonly SemVer Track027Threshold = new(0, 2, 7);

    private static bool IsLegacyCompatibleBackend(string? backendId) =>
        string.Equals(backendId, HostRenderBackendIds.OpenGl, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(backendId, HostRenderBackendIds.Software, StringComparison.OrdinalIgnoreCase);

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

    private static void AppendV030Flags(
        List<string> args,
        RecompLaunchOptions opts,
        RecompPackageManifest manifest,
        InstalledEngineCandidate? selectedEngine,
        Action<string, string>? log)
    {
        bool supports030 = selectedEngine is null || Supports030Routing(selectedEngine);
        bool supports027 = selectedEngine is null || selectedEngine.Version.CompareTo(Track027Threshold) >= 0;
        string? rawBackend = opts.BackendId ?? manifest.BackendId;
        string? effectiveBackend = HostRenderBackendIds.TryNormalize(rawBackend, out string canonical) ? canonical : rawBackend;
        bool wantsJitBringup = opts.JitBringup || string.Equals(manifest.ExecutionTierMode, "jitBringup", StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows() &&
            string.Equals(effectiveBackend, HostRenderBackendIds.Metal, StringComparison.OrdinalIgnoreCase) &&
            opts.RequireBackend)
        {
            throw new BackendUnavailableException(HostRenderBackendIds.Metal, "Apple Metal backend is not supported on Windows.");
        }

        if (!supports030)
        {
            if (!string.IsNullOrWhiteSpace(effectiveBackend))
            {
                if (supports027 && IsLegacyCompatibleBackend(effectiveBackend) && !opts.RequireBackend)
                {
                    args.Add("--backend");
                    args.Add(effectiveBackend.ToLowerInvariant());
                }
                else
                {
                    if (opts.RequireBackend)
                    {
                        throw new BackendUnavailableException(effectiveBackend!,
                            $"Selected engine '{selectedEngine!.EngineId}' v{selectedEngine.Version} does not support required '--backend {effectiveBackend}' routing (requires >= 0.3.0).");
                    }
                    log?.Invoke("WARN", $"Omitting 0.3.0+ backend '{effectiveBackend}' for pre-0.3.0 child '{selectedEngine!.EngineId}' v{selectedEngine.Version}.");
                }
            }

            if (wantsJitBringup)
            {
                if (supports027 && !opts.RequireBackend && (string.IsNullOrWhiteSpace(effectiveBackend) || IsLegacyCompatibleBackend(effectiveBackend)))
                {
                    args.Add("--jit-bringup");
                }
                else
                {
                    log?.Invoke("WARN", $"Omitting 0.3.0+ flag --jit-bringup for pre-0.3.0 child '{selectedEngine!.EngineId}' v{selectedEngine.Version}.");
                }
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(effectiveBackend))
        {
            args.Add("--backend");
            args.Add(effectiveBackend);
        }
        if (opts.RequireBackend) args.Add("--require-backend");
        if (opts.Fullscreen) args.Add("--fullscreen");
        if (!string.IsNullOrWhiteSpace(opts.AudioDriverOverride))
        {
            args.Add("--audio-driver");
            args.Add(opts.AudioDriverOverride.Trim().ToLowerInvariant());
        }
        if (wantsJitBringup)
        {
            args.Add("--jit-bringup");
        }
        if (opts.AutomationPort is int port)
        {
            args.Add("--automation-port");
            args.Add(port.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(opts.AutomationToken))
            {
                args.Add("--automation-token");
                args.Add(opts.AutomationToken);
            }
        }
    }

    internal static bool Supports030Routing(InstalledEngineCandidate engine)
    {
        if (engine.Version.CompareTo(SemVer.Track030Threshold) >= 0) return true;
        string manifestPath = Path.Combine(engine.EngineDirectory, "engine.manifest.json");
        if (!File.Exists(manifestPath)) return false;

        try
        {
            EngineManifestModel? model = JsonSerializer.Deserialize<EngineManifestModel>(File.ReadAllText(manifestPath), JsonOptions);
            if (model?.SupportedEngineTracks?.Any(t => t.StartsWith("0.3", StringComparison.OrdinalIgnoreCase)) == true) return true;
            return SemVer.TryParse(model?.EffectiveVersion, out SemVer declared) && declared.CompareTo(SemVer.Track030Threshold) >= 0;
        }
        catch (Exception)
        {
            return false;
        }
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
