// @module: reEngine.Core.Manifests.RecompPackageManifest
// @purpose: Standard 18 .recomp package manifest model, ZIP/JSON reader with Zip-Slip guard, and data\rom preload media discovery.
// @depends-on: System, System.Collections.Generic, System.IO, System.IO.Compression, System.Linq, System.Text, System.Text.Json, Foundation.Core
// @exports: RecompPackageManifest, PreloadRomDiscoveryResult, RecompPackageLoadResult, RecompPackageReader

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;

namespace reEngine.Core.Manifests;

/// <summary>
/// Canonical Standard 18 manifest schema for <c>.recomp</c> modules (<c>module.recomp.json</c> / <c>*.recomp</c>).
/// </summary>
public sealed class RecompPackageManifest
{
    /// <summary>Manifest schema version (currently 1).</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical lowercase-kebab-case module identifier.</summary>
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>Human-facing module display title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Module semantic version string.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.1.0";

    /// <summary>Target platform engine identifier (e.g., <c>reStation</c>, <c>re64</c>, <c>reCube</c>).</summary>
    [JsonPropertyName("engineId")]
    public string EngineId { get; set; } = string.Empty;

    /// <summary>Minimum compatible engine SemVer required by this module.</summary>
    [JsonPropertyName("engineVersion")]
    public string EngineVersion { get; set; } = string.Empty;

    /// <summary>Compatible engine track selector (e.g., <c>0.1.x</c>, <c>1.x</c>).</summary>
    [JsonPropertyName("compatibleEngineTrack")]
    public string CompatibleEngineTrack { get; set; } = string.Empty;

    /// <summary>Ordered list of compatible engine tracks supported by this module.</summary>
    [JsonPropertyName("supportedEngineTracks")]
    public List<string>? SupportedEngineTracks { get; set; }

    /// <summary>Optional preferred render backend identifier (<c>opengl</c>, <c>vulkan</c>, <c>parallelCpu</c>, <c>software</c>).</summary>
    [JsonPropertyName("backendId")]
    public string? BackendId { get; set; }

    /// <summary>Optional integer resolution scale factor requested by this module (1..8).</summary>
    [JsonPropertyName("resolutionScale")]
    public int? ResolutionScale { get; set; }

    /// <summary>Optional execution tier mode (<c>aotPrimary</c>, <c>jitBringup</c>, <c>threadedIr</c>).</summary>
    [JsonPropertyName("executionTierMode")]
    public string? ExecutionTierMode { get; set; }

    /// <summary>Relative preload media directory inside the module root (defaults to <c>data/rom</c>).</summary>
    [JsonPropertyName("preloadRomDir")]
    public string PreloadRomDir { get; set; } = "data/rom";

    /// <summary>Optional module-specific entrypoint executable relative path.</summary>
    [JsonPropertyName("entryExecutable")]
    public string? EntryExecutable { get; set; }

    /// <summary>Forward-compatible unknown JSON properties preserved across round-trips.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Result of scanning a module's gitignored <c>data\rom\</c> preload directory.
/// </summary>
public sealed record PreloadRomDiscoveryResult(
    string PreloadDirectory,
    bool DirectoryExists,
    IReadOnlyList<string> DiscoveredMediaFiles,
    string? PrimaryMediaPath);

/// <summary>
/// Result of loading a <c>.recomp</c> package manifest and discovering its preloaded optical/ROM media.
/// </summary>
public sealed record RecompPackageLoadResult(
    RecompPackageManifest Manifest,
    string PackagePath,
    string ModuleRootDirectory,
    PreloadRomDiscoveryResult PreloadMedia);

/// <summary>
/// Reads JSON or ZIP-packaged <c>.recomp</c> manifests with fail-closed Zip-Slip and path containment guards.
/// </summary>
public static class RecompPackageReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Dictionary<string, int> ExtensionPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cue"] = 0,
        [".chd"] = 1,
        [".iso"] = 2,
        [".gcm"] = 3,
        [".rvz"] = 3,
        [".n64"] = 3,
        [".z64"] = 3,
        [".v64"] = 3,
        [".sfc"] = 3,
        [".smc"] = 3,
        [".nes"] = 3,
        [".gb"] = 3,
        [".gbc"] = 3,
        [".gba"] = 3,
        [".7z"] = 4,
        [".zip"] = 4,
        [".img"] = 4,
        [".bin"] = 5
    };

    /// <summary>
    /// Loads a <c>.recomp</c> JSON file or ZIP archive and discovers preloaded media in <c>data\rom\</c>.
    /// </summary>
    public static RecompPackageLoadResult Load(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        string fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"Recomp package not found: '{fullPackagePath}'.", fullPackagePath);
        }

        string moduleRootDir = ResolveModuleRootDirectory(fullPackagePath);

        RecompPackageManifest manifest = IsZipArchive(fullPackagePath)
            ? ReadFromZipArchive(fullPackagePath)
            : ReadFromJsonFile(fullPackagePath);

        ValidateManifest(manifest, fullPackagePath);
        PreloadRomDiscoveryResult preload = DiscoverPreloadMedia(moduleRootDir, manifest);
        return new RecompPackageLoadResult(manifest, fullPackagePath, moduleRootDir, preload);
    }

    /// <summary>
    /// Resolves the logical module root directory for a package path. When a <c>.recomp</c> cartridge or manifest
    /// sits inside an <c>app\</c> subfolder of an installed app root, returns its parent install root so
    /// <c>data\rom\</c> discovery and per-app containment align with Standard 12 clean-root installs.
    /// </summary>
    public static string ResolveModuleRootDirectory(string fullPackagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPackagePath);
        string full = Path.GetFullPath(fullPackagePath);
        string packageDir = Path.GetDirectoryName(full)
            ?? throw new InvalidOperationException($"Cannot determine module root directory for '{full}'.");
        string trimmedDir = packageDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(trimmedDir), "app", StringComparison.OrdinalIgnoreCase))
        {
            string? parentDir = Path.GetDirectoryName(trimmedDir);
            string? volumeRoot = string.IsNullOrWhiteSpace(parentDir) ? null : Path.GetPathRoot(parentDir);
            if (!string.IsNullOrWhiteSpace(parentDir) &&
                !string.Equals(
                    parentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    volumeRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return parentDir;
            }
        }
        return packageDir;
    }

    /// <summary>
    /// Discovers optical discs and ROM images under the module's configured <c>preloadRomDir</c>.
    /// </summary>
    public static PreloadRomDiscoveryResult DiscoverPreloadMedia(string moduleRootDir, RecompPackageManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleRootDir);
        ArgumentNullException.ThrowIfNull(manifest);

        string fullRoot = Path.GetFullPath(moduleRootDir);
        string relDir = string.IsNullOrWhiteSpace(manifest.PreloadRomDir)
            ? "data/rom"
            : manifest.PreloadRomDir.Trim();

        if (Path.IsPathRooted(relDir))
        {
            throw new InvalidOperationException($"Fail-Closed Containment Violation: preloadRomDir '{relDir}' must be relative to module root.");
        }

        string normalizedRel = relDir
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string candidateDir = Path.GetFullPath(Path.Combine(fullRoot, normalizedRel));
        string containedDir = AppEnvironment.AssertPathContained(candidateDir, fullRoot);

        if (!Directory.Exists(containedDir))
        {
            return new PreloadRomDiscoveryResult(containedDir, false, Array.Empty<string>(), null);
        }

        List<string> mediaFiles = Directory.EnumerateFiles(containedDir, "*.*", SearchOption.AllDirectories)
            .Where(path => ExtensionPriority.ContainsKey(Path.GetExtension(path)))
            .OrderBy(path => ExtensionPriority[Path.GetExtension(path)])
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PreloadRomDiscoveryResult(
            containedDir,
            true,
            mediaFiles,
            mediaFiles.Count > 0 ? mediaFiles[0] : null);
    }

    private static bool IsZipArchive(string filePath)
    {
        Span<byte> header = stackalloc byte[4];
        using FileStream fs = File.OpenRead(filePath);
        if (fs.Read(header) < 4)
        {
            return false;
        }
        return header[0] == 0x50 && header[1] == 0x4B && (header[2] == 0x03 || header[2] == 0x05) && (header[3] == 0x04 || header[3] == 0x06);
    }

    private static RecompPackageManifest ReadFromJsonFile(string jsonPath)
    {
        string rawJson = File.ReadAllText(jsonPath, Encoding.UTF8);
        return JsonSerializer.Deserialize<RecompPackageManifest>(rawJson, JsonOptions)
            ?? throw new InvalidDataException($"Manifest '{jsonPath}' deserialized to null.");
    }

    private static RecompPackageManifest ReadFromZipArchive(string archivePath)
    {
        using FileStream fs = File.OpenRead(archivePath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        string virtualRoot = Path.Combine(Path.GetTempPath(), "reengine_zip_guard");

        ZipArchiveEntry? manifestEntry = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string entryName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(entryName) || entryName.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Zip-Slip Guard: archive entry '{entry.FullName}' contains a rooted path.");
            }

            string candidatePath = Path.GetFullPath(Path.Combine(virtualRoot, entryName));
            AppEnvironment.AssertPathContained(candidatePath, virtualRoot);

            if (string.Equals(entry.FullName, "module.recomp.json", StringComparison.OrdinalIgnoreCase))
            {
                manifestEntry = entry;
            }
            else if (manifestEntry is null &&
                     !entry.FullName.Contains('/', StringComparison.Ordinal) &&
                     !entry.FullName.Contains('\\', StringComparison.Ordinal) &&
                     entry.FullName.EndsWith(".recomp.json", StringComparison.OrdinalIgnoreCase))
            {
                manifestEntry = entry;
            }
        }

        if (manifestEntry is null)
        {
            throw new InvalidDataException($"Archive '{archivePath}' does not contain a root 'module.recomp.json' manifest.");
        }

        using Stream entryStream = manifestEntry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        string rawJson = reader.ReadToEnd();
        return JsonSerializer.Deserialize<RecompPackageManifest>(rawJson, JsonOptions)
            ?? throw new InvalidDataException($"Embedded manifest '{manifestEntry.FullName}' deserialized to null.");
    }

    private static void ValidateManifest(RecompPackageManifest manifest, string sourcePath)
    {
        if (manifest.SchemaVersion < 1)
        {
            throw new InvalidDataException($"Manifest '{sourcePath}' has invalid schemaVersion ({manifest.SchemaVersion}).");
        }
        if (string.IsNullOrWhiteSpace(manifest.EngineId))
        {
            throw new InvalidDataException($"Manifest '{sourcePath}' is missing required 'engineId'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.EngineVersion))
        {
            throw new InvalidDataException($"Manifest '{sourcePath}' is missing required 'engineVersion'.");
        }
        bool hasTracks = manifest.SupportedEngineTracks is { Count: > 0 };
        if (string.IsNullOrWhiteSpace(manifest.CompatibleEngineTrack))
        {
            if (hasTracks)
            {
                manifest.CompatibleEngineTrack = manifest.SupportedEngineTracks![0];
            }
            else
            {
                throw new InvalidDataException($"Manifest '{sourcePath}' is missing required 'compatibleEngineTrack' or 'supportedEngineTracks'.");
            }
        }
        if (string.IsNullOrWhiteSpace(manifest.PreloadRomDir))
        {
            manifest.PreloadRomDir = "data/rom";
        }
    }
}
