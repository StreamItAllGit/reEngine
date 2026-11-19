// @module: reEngine.Core.Decomp.PackageVerificationService
// @purpose: Standalone .recomp/.decomp verification producing Standard 18 JSON reports across schema, clean-room, engine track, and BYO media checks.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.IO.Compression, System.Linq, System.Text.Json, System.Text.Json.Serialization, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Aot, reEngine.Core.Manifests, reEngine.Core.Resolution, reEngine.Core.Updates
// @exports: PackageVerificationReport, PackageVerificationService

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;
using reEngine.Core.Aot;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.Core.Decomp;

/// <summary>Standard 18 verification report emitted by <see cref="PackageVerificationService"/>.</summary>
public sealed record PackageVerificationReport(
    [property: JsonPropertyName("isPassed")] bool IsPassed,
    [property: JsonPropertyName("generatedAtUtc")] string GeneratedAtUtc,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("exitCode")] int ExitCode,
    [property: JsonPropertyName("packageKind")] string PackageKind,
    [property: JsonPropertyName("packagePath")] string PackagePath,
    [property: JsonPropertyName("moduleId")] string? ModuleId,
    [property: JsonPropertyName("engineId")] string? EngineId,
    [property: JsonPropertyName("resolvedEngine")] InstalledEngineCandidate? ResolvedEngine,
    [property: JsonPropertyName("hasMediaReady")] bool HasMediaReady,
    [property: JsonPropertyName("primaryMediaPath")] string? PrimaryMediaPath,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("violations")] IReadOnlyList<string> Violations)
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Serializes this verification report to Standard 18 camelCase JSON.</summary>
    public string ToJson(bool indented = true) => JsonSerializer.Serialize(this, indented ? IndentedOptions : CompactOptions);
}

/// <summary>Verifies <c>.recomp</c> and <c>.decomp</c> packages/directories for schema, clean-room, engine track, and BYO media readiness.</summary>
public sealed class PackageVerificationService
{
    private readonly EngineVersionResolver _resolver = new();

    /// <summary>Runs synchronous verification for a <c>.recomp</c>/<c>.recomp.json</c> or <c>.decomp</c>/<c>.decomp.json</c>/directory package.</summary>
    public PackageVerificationReport Verify(
        string packagePath, string enginesRootDir, string? dataDirectory = null,
        string? suiteRootDir = null, bool requireMedia = false, ReEngineSettings? settings = null, string? containmentRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(enginesRootDir);

        Stopwatch sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var violations = new List<string>();
        string fullPkg = Path.GetFullPath(packagePath);
        string fullEngines = Path.GetFullPath(enginesRootDir);
        string kind = InferKindFromPath(fullPkg);

        PackageVerificationReport MakeReport(int code, string? modId = null, string? engId = null,
            InstalledEngineCandidate? eng = null, bool mediaReady = false, string? mediaPath = null) =>
            new(code == 0, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Math.Max(0L, sw.ElapsedMilliseconds), code, kind, fullPkg, modId, engId, eng, mediaReady,
                mediaPath, warnings.ToList(), violations.ToList());

        try
        {
            if (!string.IsNullOrWhiteSpace(containmentRoot))
            {
                string boundary = Path.GetFullPath(containmentRoot);
                fullPkg = AppEnvironment.AssertPathContained(fullPkg, boundary);
                fullEngines = AppEnvironment.AssertPathContained(fullEngines, boundary);
                if (!string.IsNullOrWhiteSpace(dataDirectory)) AppEnvironment.AssertPathContained(Path.GetFullPath(dataDirectory), boundary);
                if (!string.IsNullOrWhiteSpace(suiteRootDir)) AppEnvironment.AssertPathContained(Path.GetFullPath(suiteRootDir), boundary);
            }

            if (!File.Exists(fullPkg) && !Directory.Exists(fullPkg)) { violations.Add($"Package path not found: '{fullPkg}'."); return MakeReport(2); }
            if (!TryResolveTarget(fullPkg, ref kind, out string manifestPath, out string moduleRootDir, out string? targetError))
            {
                violations.Add(targetError ?? $"Unrecognized package target '{fullPkg}'.");
                return MakeReport(2);
            }

            LoadedPackage pkg = LoadAndCleanRoomCheck(fullPkg, manifestPath, moduleRootDir, kind, dataDirectory, violations);
            if (violations.Count > 0) return MakeReport(2, pkg.ModuleId, pkg.EngineId);

            InstalledEngineCandidate? resolved = ResolveCompatibleEngine(fullEngines, kind, pkg, settings, violations);
            string? effectiveSuiteRoot = suiteRootDir ?? SuiteMediaTreeWalker.DeriveSuiteRootDirectory(fullEngines, dataDirectory, containmentRoot);
            SuiteMediaDiscoveryResult media = SuiteMediaTreeWalker.Discover(
                moduleRootDir, effectiveSuiteRoot, pkg.ModuleId, pkg.ExpectedSha, pkg.PreloadRomDir, containmentRoot);

            bool hasMedia = media.Found && !string.IsNullOrWhiteSpace(media.PrimaryMediaPath);
            if (!hasMedia)
            {
                string msg = media.IsAmbiguous ? media.DiagnosticMessage : $"No BYO media found for module '{pkg.ModuleId ?? Path.GetFileName(fullPkg)}'.";
                if (requireMedia) violations.Add(msg);
                else warnings.Add(msg);
            }

            return MakeReport(violations.Count > 0 ? (kind == "decomp" ? 3 : 1) : 0, pkg.ModuleId, pkg.EngineId, resolved, hasMedia, media.PrimaryMediaPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        {
            violations.Add(ex.Message);
            return MakeReport(2);
        }
    }

    /// <summary>Asynchronously verifies a <c>.recomp</c> or <c>.decomp</c> package.</summary>
    public Task<PackageVerificationReport> VerifyAsync(
        string packagePath, string enginesRootDir, string? dataDirectory = null, string? suiteRootDir = null,
        bool requireMedia = false, ReEngineSettings? settings = null, string? containmentRoot = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Verify(packagePath, enginesRootDir, dataDirectory, suiteRootDir, requireMedia, settings, containmentRoot));
    }

    private sealed record LoadedPackage(
        string? ModuleId, string? EngineId, string PreloadRomDir, string? ExpectedSha,
        RecompPackageManifest? Recomp, DecompPackageManifest? Decomp);

    private static LoadedPackage LoadAndCleanRoomCheck(
        string fullPkg, string manifestPath, string moduleRootDir, string kind, string? dataDirectory, List<string> violations)
    {
        bool isFileZip = File.Exists(manifestPath) && DecompPipelineCoordinator.IsZipArchive(manifestPath);
        if (isFileZip) InspectZipSafetyAndD4Report(manifestPath, violations);

        if (kind == "recomp")
        {
            RecompPackageLoadResult loaded = RecompPackageReader.Load(manifestPath);
            string? expectedSha = loaded.Manifest.ExtensionData?.TryGetValue("expectedSha256", out JsonElement shaEl) == true && shaEl.ValueKind == JsonValueKind.String ? shaEl.GetString() : null;
            if (!SemVer.TryParse(loaded.Manifest.EngineVersion, out _))
                violations.Add($"Manifest '{manifestPath}' has invalid SemVer engineVersion '{loaded.Manifest.EngineVersion}'.");
            CleanRoomRecompCheckResult clean = new AotCacheManager(dataDirectory ?? moduleRootDir).ValidateCleanRoomRecompPackage(fullPkg);
            if (!clean.IsClean) violations.AddRange(clean.UnexpectedGeneratedAssemblies.Select(a => $"Forbidden generated artifact: {a}"));
            CheckD4ReportFile(Path.Combine(moduleRootDir, "d4_build_report.json"), violations);
            return new LoadedPackage(loaded.Manifest.ModuleId, loaded.Manifest.EngineId, loaded.Manifest.PreloadRomDir, expectedSha, loaded.Manifest, null);
        }

        DecompPackageManifest manifest = DecompPipelineCoordinator.ParseManifest(manifestPath);
        string preloadDir = string.IsNullOrWhiteSpace(manifest.PreloadRomDir) ? "data/rom" : manifest.PreloadRomDir;
        if (!SemVer.TryParse(manifest.EngineVersion, out _))
            violations.Add($"Manifest '{manifestPath}' has invalid SemVer engineVersion '{manifest.EngineVersion}'.");
        if (Path.IsPathRooted(preloadDir))
            violations.Add($"Fail-Closed Containment Violation: preloadRomDir '{preloadDir}' must be relative.");

        if (File.Exists(fullPkg))
        {
            if (isFileZip && DecompD4StrictnessValidator.ValidateDecompArchiveCleanRoom(fullPkg) is { IsValid: false } zipRes)
                violations.AddRange(zipRes.Violations);
            else if (!isFileZip && DecompD4StrictnessValidator.ContainsCFunctionBody(File.ReadAllText(fullPkg)))
                violations.Add($"Clean-room D4 violation: C function body detected inside '{Path.GetFileName(fullPkg)}'.");
            CheckD4ReportFile(Path.Combine(moduleRootDir, "d4_build_report.json"), violations);
        }
        else if (Directory.Exists(fullPkg))
        {
            if (DecompD4StrictnessValidator.ValidateWorkspace(fullPkg) is { IsValid: false } wsRes) violations.AddRange(wsRes.Violations);
            CheckLooseDirectorySources(fullPkg, violations);
        }
        return new LoadedPackage(manifest.ModuleId, manifest.EngineId, preloadDir, manifest.ExpectedSha256, null, manifest);
    }

    private InstalledEngineCandidate? ResolveCompatibleEngine(string enginesRootDir, string kind, LoadedPackage pkg, ReEngineSettings? settings, List<string> violations)
    {
        if (kind == "recomp" && pkg.Recomp is not null)
        {
            EngineResolutionResult res = _resolver.ResolveForManifest(enginesRootDir, pkg.Recomp, settings);
            if (res.Found && res.SelectedEngine is not null) return res.SelectedEngine;
            violations.Add(res.DiagnosticMessage);
            return null;
        }
        if (pkg.Decomp is not { } decomp) return null;

        IReadOnlyList<string> tracks = decomp.SupportedEngineTracks is { Count: > 0 }
            ? decomp.SupportedEngineTracks
            : (string.IsNullOrWhiteSpace(decomp.CompatibleEngineTrack) ? Array.Empty<string>() : [decomp.CompatibleEngineTrack]);
        if (tracks.Count > 0)
        {
            var proxy = new RecompPackageManifest
            {
                ModuleId = decomp.ModuleId, EngineId = decomp.EngineId, EngineVersion = decomp.EngineVersion,
                CompatibleEngineTrack = tracks[0], SupportedEngineTracks = [.. tracks]
            };
            EngineResolutionResult res = _resolver.ResolveForManifest(enginesRootDir, proxy, settings);
            if (res.Found && res.SelectedEngine is not null) return res.SelectedEngine;
        }
        else
        {
            SemVer.TryParse(decomp.EngineVersion, out SemVer minVer);
            InstalledEngineCandidate? cand = _resolver.DiscoverInstalledEngines(enginesRootDir, decomp.EngineId)
                .Where(c => c.Version.CompareTo(minVer) >= 0)
                .OrderByDescending(c => string.Equals(c.EngineId, decomp.EngineId, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Version).FirstOrDefault();
            if (cand is not null) return cand;
        }
        violations.Add(DecompUnbuiltEngineError.Create(decomp.EngineId, decomp.CompatibleEngineTrack, decomp.EngineVersion, pkg.ModuleId).FormatMessage());
        return null;
    }

    private static bool TryResolveTarget(string fullPkg, ref string kind, out string manifestPath, out string moduleRootDir, out string? error)
    {
        manifestPath = fullPkg; moduleRootDir = fullPkg; error = null;
        if (Directory.Exists(fullPkg))
        {
            if (FindTopManifest(fullPkg, "module.decomp.json", ".decomp.json") is string dj) { kind = "decomp"; manifestPath = dj; return true; }
            if (FindTopManifest(fullPkg, "module.recomp.json", ".recomp.json") is string rj) { kind = "recomp"; manifestPath = rj; return true; }
            error = $"Directory '{fullPkg}' contains neither a .decomp.json nor a .recomp.json manifest.";
            return false;
        }
        moduleRootDir = RecompPackageReader.ResolveModuleRootDirectory(fullPkg);
        if (kind is "recomp" or "decomp") return true;
        if (DecompPipelineCoordinator.IsZipArchive(fullPkg))
        {
            using ZipArchive zip = ZipFile.OpenRead(fullPkg);
            if (zip.Entries.Any(e => e.Name.Equals("module.decomp.json", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".decomp.json", StringComparison.OrdinalIgnoreCase))) { kind = "decomp"; return true; }
            if (zip.Entries.Any(e => e.Name.Equals("module.recomp.json", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".recomp.json", StringComparison.OrdinalIgnoreCase))) { kind = "recomp"; return true; }
        }
        error = $"Unable to identify package kind (.recomp or .decomp) for '{fullPkg}'.";
        return false;
    }

    private static string? FindTopManifest(string dir, string preferredName, string suffix) =>
        Path.Combine(dir, preferredName) is string p && File.Exists(p) ? p : Directory.EnumerateFiles(dir, $"*{suffix}", SearchOption.TopDirectoryOnly).FirstOrDefault();

    private static string InferKindFromPath(string path) =>
        path.EndsWith(".decomp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".decomp.json", StringComparison.OrdinalIgnoreCase) ? "decomp"
        : (path.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".recomp.json", StringComparison.OrdinalIgnoreCase) ? "recomp" : "unknown");

    private static void InspectZipSafetyAndD4Report(string zipPath, List<string> violations)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        string virtualRoot = Path.Combine(Path.GetTempPath(), "reengine_verify_zip");
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string norm = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(norm) || norm.Contains(':', StringComparison.Ordinal))
                throw new InvalidDataException($"Zip-Slip Guard: archive entry '{entry.FullName}' contains a rooted path.");
            _ = AppEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(virtualRoot, norm)), virtualRoot);
            if (entry.Name.Equals("d4_build_report.json", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(entry.Open());
                CheckD4Json(reader.ReadToEnd(), violations);
            }
        }
    }

    private static void CheckD4ReportFile(string path, List<string> violations)
    {
        if (File.Exists(path)) CheckD4Json(File.ReadAllText(path), violations);
    }

    private static void CheckD4Json(string json, List<string> violations)
    {
        if (DecompD4StrictnessValidator.ValidateD4BuildReportJson(json) is { IsValid: false } res) violations.AddRange(res.Violations);
    }

    private static void CheckLooseDirectorySources(string rootDir, List<string> violations)
    {
        foreach (string file in Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(rootDir, file);
            if (rel.StartsWith("Corpus" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string ext = Path.GetExtension(file);
            if ((ext.Equals(".c", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cpp", StringComparison.OrdinalIgnoreCase)) &&
                DecompD4StrictnessValidator.ContainsCFunctionBody(File.ReadAllText(file)))
                violations.Add($"Clean-room D4 violation: C function body detected inside '{rel}'.");
        }
    }
}
