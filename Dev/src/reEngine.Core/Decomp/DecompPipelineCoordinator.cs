// @module: reEngine.Core.Decomp.DecompPipelineCoordinator
// @purpose: Universal D0-D5 decompilation orchestrator with SeriesMap alias resolution and deterministic AssemblyDependencyResolver provider discovery.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Runtime.Loader, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Manifests, reEngine.Core.Resolution
// @exports: DecompDispatchResult, DecompPipelineCoordinator

using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Foundation.Core;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;

namespace reEngine.Core.Decomp;

/// <summary>
/// Outcome of dispatching a <c>.decomp</c> module through <see cref="DecompPipelineCoordinator"/>.
/// </summary>
public sealed record DecompDispatchResult(
    bool Success,
    DecompPackageManifest? Manifest,
    InstalledEngineCandidate? ResolvedEngine,
    DecompUnbuiltEngineError? UnbuiltEngineError,
    DecompStageResult? StageResult,
    DecompCleanRoomValidationResult? CleanRoomValidation,
    string DiagnosticMessage);

/// <summary>
/// Universal <c>D0</c>–<c>D5</c> orchestrator resolving <c>re*</c> engines/aliases and <see cref="IDecompPlatformProvider"/> plugins.
/// </summary>
public sealed class DecompPipelineCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly HashSet<string> ForbiddenCleanRoomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".iso", ".cue", ".chd", ".img", ".n64", ".z64", ".v64", ".nes", ".fds", ".sfc", ".smc", ".gb", ".gbc", ".gba", ".gcm", ".rvz", ".sav", ".srm", ".state", ".rom"
    };

    private readonly EngineVersionResolver _resolver = new();
    private readonly IReadOnlyList<IDecompPlatformProvider> _registeredProviders;

    /// <summary>Initializes a new coordinator with optional in-process <see cref="IDecompPlatformProvider"/> instances.</summary>
    public DecompPipelineCoordinator(IEnumerable<IDecompPlatformProvider>? providers = null)
    {
        _registeredProviders = providers?.ToList() ?? [];
    }

    /// <summary>Parses a JSON or ZIP <c>.decomp</c> package manifest with fail-closed Zip-Slip protection.</summary>
    public static DecompPackageManifest ParseManifest(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        string fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException($"Decompilation package not found: '{fullPath}'.", fullPath);

        string json = IsZipArchive(fullPath) ? ReadManifestJsonFromZip(fullPath) : File.ReadAllText(fullPath);
        DecompPackageManifest manifest = JsonSerializer.Deserialize<DecompPackageManifest>(json, JsonOptions)
            ?? throw new InvalidDataException($"Unable to deserialize .decomp manifest '{fullPath}'.");
        if (string.IsNullOrWhiteSpace(manifest.EngineId)) throw new InvalidDataException($"Manifest '{fullPath}' is missing required 'engineId'.");
        if (string.IsNullOrWhiteSpace(manifest.ModuleId)) manifest.ModuleId = Path.GetFileNameWithoutExtension(fullPath);
        return manifest;
    }

    /// <summary>Resolves the platform engine and executes or dry-runs the requested <c>D0</c>–<c>D5</c> stage.</summary>
    public async Task<DecompDispatchResult> DispatchAsync(DecompRunOptions options, string enginesRootDir, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);

        DecompPackageManifest manifest = ParseManifest(options.PackagePath);
        InstalledEngineCandidate? engine = ResolveEngine(manifest, enginesRootDir, options.EngineDirectoryOverride);
        if (engine is null)
        {
            DecompUnbuiltEngineError err = DecompUnbuiltEngineError.Create(manifest.EngineId, manifest.CompatibleEngineTrack, manifest.EngineVersion, manifest.ModuleId);
            return new DecompDispatchResult(false, manifest, null, err, null, null, err.FormatMessage());
        }

        var probeDiagnostics = new List<string>();
        IDecompPlatformProvider? provider = ResolveProvider(manifest, engine, probeDiagnostics);
        if (provider is null)
        {
            string trace = probeDiagnostics.Count > 0 ? $" Probe trace: {string.Join(" | ", probeDiagnostics)}" : string.Empty;
            if (options.DryRun)
            {
                var dryStage = new DecompStageResult(options.TargetStage, true, $"[DRY-RUN] Resolved '{engine.EngineId}' v{engine.Version} at '{engine.EngineDirectory}' for '{manifest.ModuleId}'.{trace}", options.OutputDirectory, Array.Empty<string>(), TimeSpan.Zero);
                return new DecompDispatchResult(true, manifest, engine, null, dryStage, null, dryStage.Message);
            }

            return new DecompDispatchResult(false, manifest, engine, null, null, null, $"Resolved engine '{engine.EngineId}' v{engine.Version} at '{engine.EngineDirectory}', but no IDecompPlatformProvider for platform '{manifest.PlatformId}' was found.{trace}");
        }

        DecompRunOptions effectiveOptions = EnrichOptionsWithSuiteMedia(options, manifest, enginesRootDir);
        DecompStageResult stageResult = await provider.RunDecompAsync(effectiveOptions, ct).ConfigureAwait(false);
        if (!stageResult.Success) return new DecompDispatchResult(false, manifest, engine, null, stageResult, null, stageResult.Message);

        string? outDir = stageResult.OutputDirectory ?? options.OutputDirectory;
        DecompCleanRoomValidationResult? validation = null;
        if (!string.IsNullOrWhiteSpace(outDir) && Directory.Exists(outDir))
        {
            DecompCleanRoomValidationResult providerCheck = provider.ValidateCleanRoomOutput(outDir);
            DecompCleanRoomValidationResult coreCheck = ValidateCleanRoomOutputDirectory(outDir);
            var combined = providerCheck.Violations.Concat(coreCheck.Violations).Distinct().ToList();
            bool isClean = providerCheck.IsValid && coreCheck.IsValid && combined.Count == 0;
            string summary = isClean ? providerCheck.Summary : $"Clean-room validation failed ({combined.Count} violation(s)): {string.Join("; ", combined)}";
            validation = new DecompCleanRoomValidationResult(isClean, combined, summary);
            if (!isClean) return new DecompDispatchResult(false, manifest, engine, null, stageResult, validation, summary);
        }

        return new DecompDispatchResult(true, manifest, engine, null, stageResult, validation, stageResult.Message);
    }

    /// <summary>Validates that <paramref name="outputDirectory"/> contains zero retail ROMs, save states, or pre-lifted assemblies.</summary>
    public static DecompCleanRoomValidationResult ValidateCleanRoomOutputDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string fullOut = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(fullOut)) return new DecompCleanRoomValidationResult(true, Array.Empty<string>(), "Output directory does not exist yet.");

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fullOut, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (ForbiddenCleanRoomExtensions.Contains(Path.GetExtension(file)) ||
                name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".Generated.dll", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(Path.GetRelativePath(fullOut, file));
            }
        }
        return violations.Count == 0
            ? new DecompCleanRoomValidationResult(true, Array.Empty<string>(), "Clean-room output verified.")
            : new DecompCleanRoomValidationResult(false, violations, $"Found {violations.Count} non-clean-room artifact(s).");
    }

    private static DecompRunOptions EnrichOptionsWithSuiteMedia(DecompRunOptions options, DecompPackageManifest manifest, string enginesRootDir)
    {
        IReadOnlyDictionary<string, string>? args = options.PlatformArguments;
        if (!string.IsNullOrWhiteSpace(GetPlatformArg(args, "discPath")) ||
            !string.IsNullOrWhiteSpace(GetPlatformArg(args, "exePath")) ||
            !string.IsNullOrWhiteSpace(GetPlatformArg(args, "gamedataDir")))
            return options;

        string? containmentRoot = GetPlatformArg(args, "containmentRoot");
        string fullPackage = Path.GetFullPath(options.PackagePath);
        string moduleRoot = Directory.Exists(fullPackage) ? fullPackage : RecompPackageReader.ResolveModuleRootDirectory(fullPackage);
        string? suiteRoot = SuiteMediaTreeWalker.DeriveSuiteRootDirectory(enginesRootDir, options.DataDirectory, containmentRoot);

        SuiteMediaDiscoveryResult media = SuiteMediaTreeWalker.Discover(
            moduleRoot, suiteRoot, manifest.ModuleId, manifest.ExpectedSha256, manifest.PreloadRomDir, containmentRoot);
        if (!media.Found && !media.IsAmbiguous && !string.IsNullOrWhiteSpace(options.DataDirectory) &&
            !string.Equals(Path.GetFullPath(options.DataDirectory), suiteRoot, StringComparison.OrdinalIgnoreCase))
        {
            media = SuiteMediaTreeWalker.Discover(
                moduleRoot, options.DataDirectory, manifest.ModuleId, manifest.ExpectedSha256, manifest.PreloadRomDir, containmentRoot);
        }
        if (!media.Found || media.SelectedCandidate is null) return options;

        var cloned = args is not null
            ? new Dictionary<string, string>(args, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        cloned[media.SelectedCandidate.IsPsxExecutable ? "exePath" : "discPath"] = media.SelectedCandidate.FilePath;
        return options with { PlatformArguments = cloned };
    }

    private static string? GetPlatformArg(IReadOnlyDictionary<string, string>? args, string key) =>
        args is null ? null : (args.TryGetValue(key, out string? v) ? v : args.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value);

    private InstalledEngineCandidate? ResolveEngine(DecompPackageManifest manifest, string enginesRootDir, string? engineDirOverride)
    {
        if (!string.IsNullOrWhiteSpace(engineDirOverride))
        {
            string fullOverride = Path.GetFullPath(engineDirOverride.Trim());
            if (!Directory.Exists(fullOverride)) return null;
            SemVer.TryParse(manifest.EngineVersion, out SemVer overrideVer);
            string exe = Path.Combine(fullOverride, $"{manifest.EngineId}.exe");
            if (!File.Exists(exe)) exe = Path.Combine(fullOverride, "app", $"{manifest.EngineId}.exe");
            return new InstalledEngineCandidate(manifest.EngineId, overrideVer, Path.GetFileName(fullOverride), fullOverride, exe);
        }

        IReadOnlyList<InstalledEngineCandidate> candidates = _resolver.DiscoverInstalledEngines(enginesRootDir, manifest.EngineId);
        if (candidates.Count == 0) return null;

        SemVer.TryParse(manifest.EngineVersion, out SemVer minVer);
        IReadOnlyList<string> tracks = manifest.SupportedEngineTracks is { Count: > 0 }
            ? manifest.SupportedEngineTracks
            : (string.IsNullOrWhiteSpace(manifest.CompatibleEngineTrack) ? Array.Empty<string>() : [manifest.CompatibleEngineTrack]);

        return tracks.Count == 0
            ? candidates.Where(c => c.Version.CompareTo(minVer) >= 0).OrderByDescending(c => c.Version).FirstOrDefault()
            : tracks.SelectMany(track => candidates.Where(c => c.Version.CompareTo(minVer) >= 0 && EngineTrackMatcher.IsTrackCompatible(track, c.Version)))
                .OrderByDescending(c => c.Version).FirstOrDefault();
    }

    private IDecompPlatformProvider? ResolveProvider(DecompPackageManifest manifest, InstalledEngineCandidate engine, List<string> diagnostics)
    {
        IDecompPlatformProvider? registered = _registeredProviders.FirstOrDefault(p => string.Equals(p.PlatformId, manifest.PlatformId, StringComparison.OrdinalIgnoreCase));
        if (registered is not null) return registered;

        foreach (string probePath in BuildProviderProbeList(manifest, engine))
        {
            if (!File.Exists(probePath)) { diagnostics.Add($"miss:{Path.GetFileName(probePath)}"); continue; }
            try
            {
                var alc = new ProviderLoadContext(probePath);
                Assembly asm = alc.LoadFromAssemblyPath(probePath);
                foreach (Type t in asm.GetTypes())
                {
                    if (!t.IsInterface && !t.IsAbstract && typeof(IDecompPlatformProvider).IsAssignableFrom(t) &&
                        t.GetConstructor(Type.EmptyTypes) is not null &&
                        Activator.CreateInstance(t) is IDecompPlatformProvider instance &&
                        string.Equals(instance.PlatformId, manifest.PlatformId, StringComparison.OrdinalIgnoreCase))
                    {
                        return instance;
                    }
                }
                diagnostics.Add($"no-provider-type:{Path.GetFileName(probePath)}");
            }
            catch (Exception ex)
            {
                diagnostics.Add($"load-error({Path.GetFileName(probePath)}): {ex.Message}");
            }
        }
        return null;
    }

    internal static IReadOnlyList<string> BuildProviderProbeList(DecompPackageManifest manifest, InstalledEngineCandidate engine)
    {
        var paths = new List<string>();
        void AddContained(string relOrName)
        {
            string candidate = Path.GetFullPath(Path.Combine(engine.EngineDirectory, relOrName));
            string contained = AppEnvironment.AssertPathContained(candidate, engine.EngineDirectory);
            if (!paths.Contains(contained, StringComparer.OrdinalIgnoreCase)) paths.Add(contained);
        }

        if (!string.IsNullOrWhiteSpace(manifest.DecompProviderAssembly)) AddContained(manifest.DecompProviderAssembly.Trim());
        string[] ids = EngineAliasCatalog.GetEquivalentIds(engine.EngineId).Concat(EngineAliasCatalog.GetEquivalentIds(manifest.EngineId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string id in ids) { AddContained($"{id}.Decomp.dll"); AddContained(Path.Combine("app", $"{id}.Decomp.dll")); }
        foreach (string id in ids) { AddContained($"{id}.dll"); AddContained(Path.Combine("app", $"{id}.dll")); }
        return paths;
    }

    internal static bool IsZipArchive(string path) { using FileStream fs = File.OpenRead(path); return fs.ReadByte() == 'P' && fs.ReadByte() == 'K'; }

    private static string ReadManifestJsonFromZip(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => string.Equals(e.Name, "module.decomp.json", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".decomp.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"ZIP .decomp archive '{zipPath}' contains no module.decomp.json entry.");
        string tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reengine_decomp_check"));
        _ = AppEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(tempRoot, entry.FullName)), tempRoot);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private sealed class ProviderLoadContext : AssemblyLoadContext
    {
        private static readonly HashSet<string> HostSharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
            { "reEngine.Core", "reEngine.Host", "Foundation.Core", "Foundation.Retro" };
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginDirectory;

        public ProviderLoadContext(string pluginPath) : base(isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _pluginDirectory = Path.GetDirectoryName(Path.GetFullPath(pluginPath)) ?? string.Empty;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName.Name) || HostSharedAssemblies.Contains(assemblyName.Name)) return null;
            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null) return LoadFromAssemblyPath(path);
            string siblingDll = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
            return File.Exists(siblingDll) ? LoadFromAssemblyPath(siblingDll) : null;
        }
    }
}
