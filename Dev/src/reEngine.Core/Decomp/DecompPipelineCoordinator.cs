// @module: reEngine.Core.Decomp.DecompPipelineCoordinator
// @purpose: Universal D0-D5 decompilation orchestrator resolving re* engines, delegating to IDecompPlatformProvider, and enforcing clean-room validation.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Reflection, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Manifests, reEngine.Core.Resolution
// @exports: DecompDispatchResult, DecompPipelineCoordinator

using System.IO.Compression;
using System.Reflection;
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
/// Universal <c>D0</c>–<c>D5</c> orchestrator that parses <c>.decomp</c> manifests, resolves installed <c>re*</c>
/// platform engines, delegates stage execution to <see cref="IDecompPlatformProvider"/>, and validates clean-room outputs.
/// </summary>
public sealed class DecompPipelineCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly HashSet<string> ForbiddenCleanRoomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".iso", ".cue", ".chd", ".img", ".n64", ".z64", ".v64",
        ".nes", ".sfc", ".smc", ".gb", ".gbc", ".gba", ".gcm", ".rvz",
        ".sav", ".srm", ".state", ".rom"
    };

    private readonly EngineVersionResolver _resolver;
    private readonly IReadOnlyList<IDecompPlatformProvider> _registeredProviders;

    /// <summary>
    /// Initializes a new coordinator with optional in-process <see cref="IDecompPlatformProvider"/> instances.
    /// </summary>
    public DecompPipelineCoordinator(IEnumerable<IDecompPlatformProvider>? providers = null)
    {
        _resolver = new EngineVersionResolver();
        _registeredProviders = providers?.ToList() ?? [];
    }

    /// <summary>
    /// Parses a JSON or ZIP-packaged <c>.decomp</c> manifest file with fail-closed Zip-Slip validation.
    /// </summary>
    public static DecompPackageManifest ParseManifest(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        string fullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Decompilation package not found: '{fullPath}'.", fullPath);
        }

        string json = IsZipArchive(fullPath) ? ReadManifestJsonFromZip(fullPath) : File.ReadAllText(fullPath);
        DecompPackageManifest manifest = JsonSerializer.Deserialize<DecompPackageManifest>(json, JsonOptions)
            ?? throw new InvalidDataException($"Unable to deserialize .decomp manifest '{fullPath}'.");

        if (string.IsNullOrWhiteSpace(manifest.EngineId))
        {
            throw new InvalidDataException($"Manifest '{fullPath}' is missing required 'engineId'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.ModuleId))
        {
            manifest.ModuleId = Path.GetFileNameWithoutExtension(fullPath);
        }
        return manifest;
    }

    /// <summary>
    /// Resolves the target <c>re*</c> engine, delegates <c>D0</c>–<c>D5</c> execution, and validates clean-room outputs.
    /// </summary>
    public async Task<DecompDispatchResult> DispatchAsync(
        DecompRunOptions options,
        string enginesRootDir,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);

        DecompPackageManifest manifest = ParseManifest(options.PackagePath);
        InstalledEngineCandidate? engine = ResolveEngine(manifest, enginesRootDir, options.EngineDirectoryOverride);
        if (engine is null)
        {
            DecompUnbuiltEngineError err = DecompUnbuiltEngineError.Create(
                manifest.EngineId,
                manifest.CompatibleEngineTrack,
                manifest.EngineVersion,
                manifest.ModuleId);
            return new DecompDispatchResult(false, manifest, null, err, null, null, err.FormatMessage());
        }

        IDecompPlatformProvider? provider = ResolveProvider(manifest, engine);
        if (provider is null)
        {
            if (options.DryRun)
            {
                var dryStage = new DecompStageResult(
                    options.TargetStage,
                    true,
                    $"[DRY-RUN] Resolved '{engine.EngineId}' v{engine.Version} at '{engine.EngineDirectory}' for '{manifest.ModuleId}'.",
                    options.OutputDirectory,
                    Array.Empty<string>(),
                    TimeSpan.Zero);
                return new DecompDispatchResult(true, manifest, engine, null, dryStage, null, dryStage.Message);
            }

            return new DecompDispatchResult(
                false,
                manifest,
                engine,
                null,
                null,
                null,
                $"Resolved engine '{engine.EngineId}' v{engine.Version} at '{engine.EngineDirectory}', " +
                $"but no IDecompPlatformProvider for platform '{manifest.PlatformId}' was found.");
        }

        DecompStageResult stageResult = await provider.RunDecompAsync(options, ct).ConfigureAwait(false);
        if (!stageResult.Success)
        {
            return new DecompDispatchResult(false, manifest, engine, null, stageResult, null, stageResult.Message);
        }

        string? outDir = stageResult.OutputDirectory ?? options.OutputDirectory;
        DecompCleanRoomValidationResult? validation = null;
        if (!string.IsNullOrWhiteSpace(outDir) && Directory.Exists(outDir))
        {
            DecompCleanRoomValidationResult providerCheck = provider.ValidateCleanRoomOutput(outDir);
            DecompCleanRoomValidationResult coreCheck = ValidateCleanRoomOutputDirectory(outDir);
            var combinedViolations = providerCheck.Violations.Concat(coreCheck.Violations).Distinct().ToList();
            bool isClean = providerCheck.IsValid && coreCheck.IsValid && combinedViolations.Count == 0;
            string summary = isClean
                ? providerCheck.Summary
                : $"Clean-room validation failed ({combinedViolations.Count} violation(s)): {string.Join("; ", combinedViolations)}";
            validation = new DecompCleanRoomValidationResult(isClean, combinedViolations, summary);

            if (!isClean)
            {
                return new DecompDispatchResult(false, manifest, engine, null, stageResult, validation, summary);
            }
        }

        return new DecompDispatchResult(true, manifest, engine, null, stageResult, validation, stageResult.Message);
    }

    /// <summary>
    /// Validates that <paramref name="outputDirectory"/> contains zero retail ROMs, save states, or pre-lifted artifacts.
    /// </summary>
    public static DecompCleanRoomValidationResult ValidateCleanRoomOutputDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        string fullOut = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(fullOut))
        {
            return new DecompCleanRoomValidationResult(true, Array.Empty<string>(), "Output directory does not exist yet.");
        }

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(fullOut, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file);
            if (ForbiddenCleanRoomExtensions.Contains(ext) ||
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

    private InstalledEngineCandidate? ResolveEngine(
        DecompPackageManifest manifest,
        string enginesRootDir,
        string? engineDirOverride)
    {
        if (!string.IsNullOrWhiteSpace(engineDirOverride))
        {
            string fullOverride = Path.GetFullPath(engineDirOverride.Trim());
            if (!Directory.Exists(fullOverride))
            {
                return null;
            }
            SemVer.TryParse(manifest.EngineVersion, out SemVer overrideVer);
            string exe = Path.Combine(fullOverride, $"{manifest.EngineId}.exe");
            if (!File.Exists(exe))
            {
                exe = Path.Combine(fullOverride, "app", $"{manifest.EngineId}.exe");
            }
            return new InstalledEngineCandidate(manifest.EngineId, overrideVer, Path.GetFileName(fullOverride), fullOverride, exe);
        }

        IReadOnlyList<InstalledEngineCandidate> candidates = _resolver.DiscoverInstalledEngines(enginesRootDir, manifest.EngineId);
        if (candidates.Count == 0)
        {
            return null;
        }

        SemVer.TryParse(manifest.EngineVersion, out SemVer minVer);
        return candidates
            .Where(c => c.Version.CompareTo(minVer) >= 0 &&
                        (string.IsNullOrWhiteSpace(manifest.CompatibleEngineTrack) ||
                         EngineTrackMatcher.IsTrackCompatible(manifest.CompatibleEngineTrack, c.Version)))
            .OrderByDescending(c => c.Version)
            .FirstOrDefault();
    }

    private IDecompPlatformProvider? ResolveProvider(DecompPackageManifest manifest, InstalledEngineCandidate engine)
    {
        IDecompPlatformProvider? match = _registeredProviders.FirstOrDefault(p =>
            string.Equals(p.PlatformId, manifest.PlatformId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        foreach (string dllPath in new[]
        {
            Path.Combine(engine.EngineDirectory, $"{engine.EngineId}.dll"),
            Path.Combine(engine.EngineDirectory, "app", $"{engine.EngineId}.dll")
        })
        {
            if (!File.Exists(dllPath))
            {
                continue;
            }

            try
            {
                Assembly asm = Assembly.LoadFrom(dllPath);
                foreach (Type t in asm.GetTypes())
                {
                    if (!t.IsInterface && !t.IsAbstract && typeof(IDecompPlatformProvider).IsAssignableFrom(t) &&
                        Activator.CreateInstance(t) is IDecompPlatformProvider instance &&
                        string.Equals(instance.PlatformId, manifest.PlatformId, StringComparison.OrdinalIgnoreCase))
                    {
                        return instance;
                    }
                }
            }
            catch
            {
                // Ignore non-managed or incompatible plugin assemblies and fall through cleanly.
            }
        }

        return null;
    }

    private static bool IsZipArchive(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return fs.ReadByte() == 'P' && fs.ReadByte() == 'K';
    }

    private static string ReadManifestJsonFromZip(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "module.decomp.json", StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".decomp.json", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new InvalidDataException($"ZIP .decomp archive '{zipPath}' contains no module.decomp.json entry.");
        }

        string tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reengine_decomp_check"));
        _ = SandboxEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(tempRoot, entry.FullName)), tempRoot);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
