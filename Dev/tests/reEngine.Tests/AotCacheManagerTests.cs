// @module: reEngine.Tests.AotCacheManagerTests
// @purpose: xUnit contract tests for deterministic <dataDir>\cache\ AOT store, hit/invalidation, stale eviction, and clean-room .recomp validation.
// @depends-on: System, System.IO, System.IO.Compression, System.Security.Cryptography, System.Text, System.Threading.Tasks, Xunit, reEngine.Core
// @exports: AotCacheManagerTests

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using reEngine.Core.Aot;
using Xunit;

namespace reEngine.Tests;

public sealed class AotCacheManagerTests : IDisposable
{
    private readonly string _tempRoot;

    public AotCacheManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "reengine_aot_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { /* ignore transient temp cleanup lock */ }
    }

    [Fact]
    public async Task AotCacheManager_StoreHitAndTamperInvalidation_WorkDeterministically()
    {
        string dataDir = Path.Combine(_tempRoot, "data");
        string sourceDir = Path.Combine(_tempRoot, "build_out");
        Directory.CreateDirectory(sourceDir);
        string srcDll = Path.Combine(sourceDir, "kings-field.Generated.dll");
        byte[] payload = Encoding.UTF8.GetBytes("MZ-SYNTHETIC-AOT-BINARY-V1");
        await File.WriteAllBytesAsync(srcDll, payload);

        string romSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-rom"))).ToLowerInvariant();
        var key = new AotCacheKey("kings-field", romSha, "reStation", "0.2.0", "0.2.0");
        var manager = new AotCacheManager(dataDir);

        Assert.Null(await manager.TryGetEntryAsync(key));

        AotCompileResult stored = await manager.StoreCompiledAssemblyAsync(key, srcDll);
        Assert.True(stored.Success);
        Assert.False(stored.CacheHit);
        Assert.NotNull(stored.Entry);
        string indexPath = Path.Combine(dataDir, "cache", AotCacheManager.IndexFileName);
        Assert.True(File.Exists(stored.Entry!.GeneratedAssemblyPath));
        Assert.True(File.Exists(indexPath));
        Assert.Equal(payload.Length, stored.Entry.AssemblySizeBytes);

        using (JsonDocument indexDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath)))
        {
            JsonElement entries = indexDoc.RootElement.GetProperty("entries");
            JsonElement entryElem = entries.EnumerateObject().Single().Value;
            Assert.Equal(payload.Length, entryElem.GetProperty("fileSizeBytes").GetInt64());
            Assert.Equal(stored.Entry.AssemblySha256, entryElem.GetProperty("sha256Hex").GetString());
            Assert.False(entryElem.TryGetProperty("assemblySizeBytes", out _));
            Assert.False(entryElem.TryGetProperty("assemblySha256", out _));
        }

        string expectedCacheDir = manager.ResolveCacheDirectory(key);
        Assert.Contains(romSha[..16], expectedCacheDir, StringComparison.OrdinalIgnoreCase);

        AotCacheEntry? hit = await manager.TryGetEntryAsync(key);
        Assert.NotNull(hit);
        Assert.True(hit!.IsValid);
        Assert.Equal(stored.Entry.AssemblySha256, hit.AssemblySha256);

        // Tampering with cached DLL bytes must invalidate the entry cleanly.
        await File.WriteAllBytesAsync(hit.GeneratedAssemblyPath, Encoding.UTF8.GetBytes("TAMPERED-SAME-OR-DIFF-LEN"));
        Assert.Null(await manager.TryGetEntryAsync(key));
    }

    [Fact]
    public async Task AotCacheManager_EvictStaleEntriesAsync_RemovesOldEntriesAndPreservesActiveAndOtherModules()
    {
        string dataDir = Path.Combine(_tempRoot, "evict_data");
        string srcDir = Path.Combine(_tempRoot, "src_bins");
        Directory.CreateDirectory(srcDir);

        string dllV1 = Path.Combine(srcDir, "v1.Generated.dll");
        string dllV2 = Path.Combine(srcDir, "v2.Generated.dll");
        string dllOther = Path.Combine(srcDir, "other.Generated.dll");
        await File.WriteAllBytesAsync(dllV1, Encoding.UTF8.GetBytes("AOT-V1"));
        await File.WriteAllBytesAsync(dllV2, Encoding.UTF8.GetBytes("AOT-V2-NEW"));
        await File.WriteAllBytesAsync(dllOther, Encoding.UTF8.GetBytes("AOT-OTHER"));

        string romSha = new string('a', 64);
        var kfV1 = new AotCacheKey("kings-field", romSha, "reStation", "0.2.0", "0.1.9");
        var kfV2 = new AotCacheKey("kings-field", romSha, "reStation", "0.2.0", "0.2.0");
        var otherKey = new AotCacheKey("metal-gear", new string('b', 64), "reStation", "0.2.0", "0.1.0");

        var manager = new AotCacheManager(dataDir);
        AotCompileResult res1 = await manager.StoreCompiledAssemblyAsync(kfV1, dllV1);
        AotCompileResult res2 = await manager.StoreCompiledAssemblyAsync(kfV2, dllV2);
        AotCompileResult resOther = await manager.StoreCompiledAssemblyAsync(otherKey, dllOther);

        Assert.True(res1.Success && res2.Success && resOther.Success);

        int evicted = await manager.EvictStaleEntriesAsync("kings-field", activeKey: kfV2);
        Assert.Equal(1, evicted);
        Assert.False(Directory.Exists(res1.Entry!.CacheDirectory));
        Assert.Null(await manager.TryGetEntryAsync(kfV1));
        Assert.NotNull(await manager.TryGetEntryAsync(kfV2));
        Assert.NotNull(await manager.TryGetEntryAsync(otherKey));

        string indexPath = Path.Combine(dataDir, "cache", AotCacheManager.IndexFileName);
        string backupPath = $"{indexPath}.bak";
        Assert.True(File.Exists(backupPath));
        using (JsonDocument currentDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath)))
        using (JsonDocument backupDoc = JsonDocument.Parse(await File.ReadAllTextAsync(backupPath)))
        {
            Assert.Equal(2, currentDoc.RootElement.GetProperty("entries").EnumerateObject().Count());
            Assert.Equal(3, backupDoc.RootElement.GetProperty("entries").EnumerateObject().Count());
        }
    }

    [Fact]
    public void AotCacheManager_ValidateCleanRoomRecompPackage_RejectsGeneratedDllAndGCsInZipAndDirectory()
    {
        var manager = new AotCacheManager(Path.Combine(_tempRoot, "data"));

        string cleanZip = Path.Combine(_tempRoot, "clean.recomp");
        using (FileStream fs = File.Create(cleanZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteZipEntry(zip, "module.recomp.json", """{ "moduleId": "kf" }""");
            WriteZipEntry(zip, "patches/lift_recipe.json", """{ "entry": "0x80010000" }""");
        }

        CleanRoomRecompCheckResult cleanZipResult = manager.ValidateCleanRoomRecompPackage(cleanZip);
        Assert.True(cleanZipResult.IsClean);
        Assert.Empty(cleanZipResult.UnexpectedGeneratedAssemblies);

        string dirtyZip = Path.Combine(_tempRoot, "dirty.recomp");
        using (FileStream fs = File.Create(dirtyZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            WriteZipEntry(zip, "module.recomp.json", """{ "moduleId": "kf" }""");
            WriteZipEntry(zip, "bin/KingsField.Generated.dll", "binary");
            WriteZipEntry(zip, "lifted/Main.g.cs", "// lifted");
        }

        CleanRoomRecompCheckResult dirtyZipResult = manager.ValidateCleanRoomRecompPackage(dirtyZip);
        Assert.False(dirtyZipResult.IsClean);
        Assert.Equal(2, dirtyZipResult.UnexpectedGeneratedAssemblies.Count);
        Assert.Contains("bin/KingsField.Generated.dll", dirtyZipResult.UnexpectedGeneratedAssemblies);
        Assert.Contains("lifted/Main.g.cs", dirtyZipResult.UnexpectedGeneratedAssemblies);

        string dirtyDir = Path.Combine(_tempRoot, "dirty_dir_pkg");
        Directory.CreateDirectory(Path.Combine(dirtyDir, "cache"));
        File.WriteAllText(Path.Combine(dirtyDir, "module.recomp.json"), """{ "moduleId": "kf" }""");
        File.WriteAllText(Path.Combine(dirtyDir, "cache", "Stub.g.cs"), "// generated");
        File.WriteAllBytes(Path.Combine(dirtyDir, "Stub.Generated.dll"), [0x4D, 0x5A]);

        CleanRoomRecompCheckResult dirtyDirResult = manager.ValidateCleanRoomRecompPackage(dirtyDir);
        Assert.False(dirtyDirResult.IsClean);
        Assert.Equal(2, dirtyDirResult.UnexpectedGeneratedAssemblies.Count);
        Assert.Contains("cache/Stub.g.cs", dirtyDirResult.UnexpectedGeneratedAssemblies);
        Assert.Contains("Stub.Generated.dll", dirtyDirResult.UnexpectedGeneratedAssemblies);
    }

    private static void WriteZipEntry(ZipArchive zip, string entryName, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
