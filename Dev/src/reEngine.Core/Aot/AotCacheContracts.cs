// @module: reEngine.Core.Aot.AotCacheContracts
// @purpose: Shared <dataDir>\cache\ AOT cache key, entry, compile result, and manager interface contracts.
// @depends-on: System, System.Collections.Generic, System.Threading, System.Threading.Tasks
// @exports: AotCacheKey, AotCacheEntry, AotCompileResult, CleanRoomRecompCheckResult, IAotCacheManager

namespace reEngine.Core.Aot;

/// <summary>
/// Deterministic cache key identifying an Ahead-of-Time (AOT) compiled assembly under <c>&lt;dataDir&gt;\cache\</c>.
/// </summary>
public sealed record AotCacheKey(
    string ModuleId,
    string RomSha256,
    string EngineId,
    string EngineVersion,
    string ModuleVersion)
{
    /// <summary>
    /// Returns the deterministic 16-char ROM hash prefix + engine/module version segment.
    /// </summary>
    public string ToCacheVersionSegment()
    {
        string hashPrefix = RomSha256.Length >= 16
            ? RomSha256[..16].ToLowerInvariant()
            : RomSha256.ToLowerInvariant();
        return $"{hashPrefix}-{EngineId}-{EngineVersion}-{ModuleVersion}";
    }
}

/// <summary>
/// Indexed metadata entry for a cached AOT-compiled <c>*.Generated.dll</c> binary.
/// </summary>
public sealed record AotCacheEntry(
    AotCacheKey Key,
    string CacheDirectory,
    string GeneratedAssemblyPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastAccessedAtUtc,
    [property: System.Text.Json.Serialization.JsonPropertyName("fileSizeBytes")] long AssemblySizeBytes,
    [property: System.Text.Json.Serialization.JsonPropertyName("sha256Hex")] string AssemblySha256,
    bool IsValid);

/// <summary>
/// Outcome of an AOT cache lookup or local compilation pass.
/// </summary>
public sealed record AotCompileResult(
    bool Success,
    AotCacheEntry? Entry,
    bool CacheHit,
    TimeSpan Elapsed,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Clean-room validation outcome ensuring <c>.recomp</c> packages do not bundle pre-compiled <c>*.Generated.dll</c> binaries.
/// </summary>
public sealed record CleanRoomRecompCheckResult(
    bool IsClean,
    IReadOnlyList<string> UnexpectedGeneratedAssemblies,
    string Summary);

/// <summary>
/// Contract for managing deterministic <c>&lt;dataDir&gt;\cache\</c> AOT assemblies and clean-room <c>.recomp</c> checks.
/// </summary>
public interface IAotCacheManager
{
    /// <summary>Resolves the deterministic cache directory for the specified <see cref="AotCacheKey"/>.</summary>
    string ResolveCacheDirectory(AotCacheKey key);

    /// <summary>Attempts to retrieve and validate an existing cached AOT entry.</summary>
    Task<AotCacheEntry?> TryGetEntryAsync(AotCacheKey key, CancellationToken ct = default);

    /// <summary>Stores a locally compiled AOT assembly into <c>&lt;dataDir&gt;\cache\</c> under a cross-process lock.</summary>
    Task<AotCompileResult> StoreCompiledAssemblyAsync(
        AotCacheKey key,
        string sourceAssemblyPath,
        CancellationToken ct = default);

    /// <summary>Evicts stale cache entries for <paramref name="moduleId"/>, optionally preserving <paramref name="activeKey"/>.</summary>
    Task<int> EvictStaleEntriesAsync(
        string moduleId,
        AotCacheKey? activeKey = null,
        CancellationToken ct = default);

    /// <summary>Rejects unexpected pre-compiled <c>*.Generated.dll</c> files inside a <c>.recomp</c> archive or directory.</summary>
    CleanRoomRecompCheckResult ValidateCleanRoomRecompPackage(string recompPackagePath);
}
