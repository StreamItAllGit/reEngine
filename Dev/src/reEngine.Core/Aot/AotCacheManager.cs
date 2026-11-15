// @module: reEngine.Core.Aot.AotCacheManager
// @purpose: Deterministic <dataDir>\cache\ AOT assembly index/cache manager and clean-room .recomp package guard.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.IO, System.IO.Compression, System.Security.Cryptography, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Retro
// @exports: AotCacheManager

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundation.Core;
using Foundation.Retro;

namespace reEngine.Core.Aot;

/// <summary>
/// Manages deterministic Ahead-of-Time (AOT) compiled <c>*.Generated.dll</c> entries under
/// <c>&lt;dataDir&gt;\cache\</c> with <c>aot-index.json</c> tracking, cross-process locking, and clean-room checks.
/// </summary>
public sealed class AotCacheManager : IAotCacheManager
{
    /// <summary>Filename of the deterministic AOT cache index stored under <c>&lt;dataDir&gt;\cache\</c>.</summary>
    public const string IndexFileName = "aot-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDir;
    private readonly string _cacheRoot;
    private readonly string _indexPath;
    private readonly string _lockPath;

    /// <summary>
    /// Initializes a new <see cref="AotCacheManager"/> rooted at <paramref name="dataDir"/>.
    /// </summary>
    public AotCacheManager(string dataDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        _dataDir = Path.GetFullPath(dataDir);
        _cacheRoot = Path.Combine(_dataDir, AotCacheLayoutHelper.CacheRootFolderName);
        _indexPath = Path.Combine(_cacheRoot, IndexFileName);
        _lockPath = Path.Combine(_cacheRoot, ".aot-index.lock");
    }

    /// <inheritdoc />
    public string ResolveCacheDirectory(AotCacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return AotCacheLayoutHelper.ResolveModuleCacheRoot(_dataDir, key.ModuleId, key.ToCacheVersionSegment());
    }

    /// <inheritdoc />
    public async Task<AotCacheEntry?> TryGetEntryAsync(AotCacheKey key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(_indexPath))
        {
            return null;
        }

        AotCacheIndexDocument index = await LoadIndexAsync(ct).ConfigureAwait(false);
        string compositeKey = BuildCompositeKey(key);
        if (!index.Entries.TryGetValue(compositeKey, out AotCacheEntry? entry) || !entry.IsValid)
        {
            return null;
        }

        if (!File.Exists(entry.GeneratedAssemblyPath))
        {
            return null;
        }

        FileInfo info = new(entry.GeneratedAssemblyPath);
        if (info.Length != entry.AssemblySizeBytes)
        {
            return null;
        }

        string actualSha = await ComputeSha256HexAsync(entry.GeneratedAssemblyPath, ct).ConfigureAwait(false);
        if (!string.Equals(actualSha, entry.AssemblySha256, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task<AotCompileResult> StoreCompiledAssemblyAsync(
        AotCacheKey key,
        string sourceAssemblyPath,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAssemblyPath);

        Stopwatch sw = Stopwatch.StartNew();
        string fullSource = Path.GetFullPath(sourceAssemblyPath);
        if (!File.Exists(fullSource))
        {
            return new AotCompileResult(
                false,
                null,
                false,
                sw.Elapsed,
                "AOT_SOURCE_MISSING",
                $"Compiled assembly not found at '{fullSource}'.");
        }

        string cacheDir = ResolveCacheDirectory(key);
        string destPath = AotCacheLayoutHelper.ResolveGeneratedAssemblyPath(_dataDir, key.ModuleId, key.ToCacheVersionSegment());
        Directory.CreateDirectory(_cacheRoot);
        Directory.CreateDirectory(cacheDir);

        await using FileStream indexLock = await AcquireIndexLockAsync(ct).ConfigureAwait(false);
        string tempPath = $"{destPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            await using (FileStream src = new(fullSource, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (FileStream dst = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await src.CopyToAsync(dst, ct).ConfigureAwait(false);
            }

            File.Move(tempPath, destPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        long sizeBytes = new FileInfo(destPath).Length;
        string sha256 = await ComputeSha256HexAsync(destPath, ct).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AotCacheEntry entry = new(key, cacheDir, destPath, now, now, sizeBytes, sha256, true);

        AotCacheIndexDocument index = await LoadIndexAsync(ct).ConfigureAwait(false);
        index.Entries[BuildCompositeKey(key)] = entry;
        index.UpdatedAtUtc = now;
        await SaveIndexAsync(index, ct).ConfigureAwait(false);

        return new AotCompileResult(true, entry, false, sw.Elapsed);
    }

    /// <inheritdoc />
    public async Task<int> EvictStaleEntriesAsync(
        string moduleId,
        AotCacheKey? activeKey = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        if (!File.Exists(_indexPath))
        {
            return 0;
        }

        Directory.CreateDirectory(_cacheRoot);
        await using FileStream indexLock = await AcquireIndexLockAsync(ct).ConfigureAwait(false);
        AotCacheIndexDocument index = await LoadIndexAsync(ct).ConfigureAwait(false);
        string? activeComposite = activeKey is null ? null : BuildCompositeKey(activeKey);

        List<string> staleKeys = new();
        foreach (KeyValuePair<string, AotCacheEntry> kvp in index.Entries)
        {
            if (!string.Equals(kvp.Value.Key.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (activeComposite is not null &&
                string.Equals(kvp.Key, activeComposite, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            staleKeys.Add(kvp.Key);
        }

        foreach (string staleKey in staleKeys)
        {
            AotCacheEntry stale = index.Entries[staleKey];
            if (!string.IsNullOrWhiteSpace(stale.CacheDirectory) && Directory.Exists(stale.CacheDirectory))
            {
                string containedDir = SandboxEnvironment.AssertPathContained(stale.CacheDirectory, _cacheRoot);
                Directory.Delete(containedDir, recursive: true);
            }

            index.Entries.Remove(staleKey);
        }

        if (staleKeys.Count > 0)
        {
            index.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await SaveIndexAsync(index, ct).ConfigureAwait(false);
        }

        return staleKeys.Count;
    }

    /// <inheritdoc />
    public CleanRoomRecompCheckResult ValidateCleanRoomRecompPackage(string recompPackagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recompPackagePath);
        string fullPath = Path.GetFullPath(recompPackagePath);
        List<string> unexpected = new();

        if (Directory.Exists(fullPath))
        {
            foreach (string file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(fullPath, file).Replace('\\', '/');
                if (IsForbiddenGeneratedEntry(rel))
                {
                    unexpected.Add(rel);
                }
            }
        }
        else if (File.Exists(fullPath))
        {
            if (IsZipArchive(fullPath))
            {
                using ZipArchive archive = ZipFile.OpenRead(fullPath);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string normalized = entry.FullName.Replace('\\', '/');
                    if (IsForbiddenGeneratedEntry(normalized))
                    {
                        unexpected.Add(normalized);
                    }
                }
            }
        }
        else
        {
            return new CleanRoomRecompCheckResult(
                false,
                unexpected,
                $"Package path not found: '{fullPath}'.");
        }

        return unexpected.Count == 0
            ? new CleanRoomRecompCheckResult(true, unexpected, "Clean-room .recomp package verified (0 generated artifacts).")
            : new CleanRoomRecompCheckResult(
                false,
                unexpected,
                $"Clean-room violation: found {unexpected.Count} forbidden generated artifact(s) in '{fullPath}'.");
    }

    private static bool IsForbiddenGeneratedEntry(string entryName) =>
        entryName.EndsWith(".Generated.dll", StringComparison.OrdinalIgnoreCase) ||
        entryName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsZipArchive(string filePath)
    {
        Span<byte> header = stackalloc byte[2];
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return fs.Read(header) == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    private static string BuildCompositeKey(AotCacheKey key) =>
        $"{key.ModuleId.Trim().ToLowerInvariant()}::{key.ToCacheVersionSegment()}";

    private static async Task<string> ComputeSha256HexAsync(string filePath, CancellationToken ct)
    {
        await using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<FileStream> AcquireIndexLockAsync(CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<AotCacheIndexDocument> LoadIndexAsync(CancellationToken ct)
    {
        if (!File.Exists(_indexPath))
        {
            return new AotCacheIndexDocument();
        }

        string json = await File.ReadAllTextAsync(_indexPath, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AotCacheIndexDocument>(json, JsonOptions) ?? new AotCacheIndexDocument();
    }

    private async Task SaveIndexAsync(AotCacheIndexDocument index, CancellationToken ct)
    {
        string tempPath = $"{_indexPath}.tmp.{Guid.NewGuid():N}";
        string backupPath = $"{_indexPath}.bak";
        string json = JsonSerializer.Serialize(index, JsonOptions);
        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            if (File.Exists(_indexPath))
            {
                File.Copy(_indexPath, backupPath, overwrite: true);
            }

            File.Move(tempPath, _indexPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch { /* best-effort temp cleanup */ }
        }
    }

    private sealed class AotCacheIndexDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("updatedAtUtc")]
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        [JsonPropertyName("entries")]
        public Dictionary<string, AotCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
