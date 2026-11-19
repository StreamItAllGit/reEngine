// @module: reEngine.Host.Rendering.HdPackResolver
// @purpose: Fail-closed path-contained <dataDir>\hdpacks\ texture override index keyed by (clutSha256Hex, rectSha256Hex).
// @depends-on: System, System.Collections.Generic, System.IO, System.Text.Json, Foundation.Core, reEngine.Host.Contracts
// @exports: HdPackResolver

using System.Text.Json;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Rendering;

/// <summary>
/// Path-contained index over <c>&lt;dataDir&gt;\hdpacks\</c> texture overrides keyed by CLUT and rect SHA-256 hashes.
/// </summary>
public sealed class HdPackResolver : IHdTexturePackResolver
{
    private readonly Dictionary<HdTextureOverrideKey, HdTextureOverrideEntry> _entries = new();

    public bool IsEnabled { get; }
    public string? PackRootDirectory { get; }
    public int IndexedCount => _entries.Count;

    public HdPackResolver(string? packRootDirectory, bool isEnabled = true, string? allowedContainmentRoot = null)
    {
        IsEnabled = isEnabled;
        if (string.IsNullOrWhiteSpace(packRootDirectory))
        {
            PackRootDirectory = null;
            return;
        }

        string fullRoot = Path.GetFullPath(packRootDirectory.Trim());
        if (!string.IsNullOrWhiteSpace(allowedContainmentRoot))
        {
            fullRoot = AppEnvironment.AssertPathContained(fullRoot, Path.GetFullPath(allowedContainmentRoot.Trim()));
        }

        PackRootDirectory = fullRoot;
        if (!isEnabled || !Directory.Exists(fullRoot)) return;
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"HD pack root '{fullRoot}' cannot be a reparse point.");
        }

        IndexDirectory(fullRoot);
    }

    public static HdPackResolver FromDataDirectory(string dataDirectory, bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string fullData = Path.GetFullPath(dataDirectory.Trim());
        return new HdPackResolver(Path.Combine(fullData, "hdpacks"), isEnabled, fullData);
    }

    public bool TryResolve(string clutSha256Hex, string rectSha256Hex, out HdTextureOverrideEntry? entry)
    {
        entry = null;
        if (!IsEnabled || string.IsNullOrWhiteSpace(clutSha256Hex) || string.IsNullOrWhiteSpace(rectSha256Hex))
        {
            return false;
        }

        var key = new HdTextureOverrideKey(NormalizeHex(clutSha256Hex), NormalizeHex(rectSha256Hex));
        if (_entries.TryGetValue(key, out HdTextureOverrideEntry? found) && File.Exists(found.FullFilePath))
        {
            entry = found;
            return true;
        }
        return false;
    }

    private void IndexDirectory(string rootDir)
    {
        foreach (string manifestPath in Directory.EnumerateFiles(rootDir, "hdpack.manifest.json", SearchOption.AllDirectories))
        {
            string containedManifest = AssertSafeFile(manifestPath, rootDir);
            IndexManifestFile(containedManifest, rootDir);
        }

        foreach (string pngPath in Directory.EnumerateFiles(rootDir, "*.png", SearchOption.AllDirectories))
        {
            string containedPng = AssertSafeFile(pngPath, rootDir);
            string stem = Path.GetFileNameWithoutExtension(containedPng);
            int sep = stem.IndexOf('_');
            if (sep <= 0 || sep >= stem.Length - 1) continue;

            string clut = stem[..sep];
            string rect = stem[(sep + 1)..];
            if (!IsValidHexToken(clut) || !IsValidHexToken(rect)) continue;

            AddEntry(clut, rect, containedPng, rootDir);
        }
    }

    private void IndexManifestFile(string manifestPath, string rootDir)
    {
        using FileStream fs = File.OpenRead(manifestPath);
        using JsonDocument doc = JsonDocument.Parse(fs);
        string manifestDir = Path.GetDirectoryName(manifestPath) ?? rootDir;

        JsonElement array = doc.RootElement.ValueKind switch
        {
            JsonValueKind.Array => doc.RootElement,
            JsonValueKind.Object when doc.RootElement.TryGetProperty("entries", out JsonElement e) && e.ValueKind == JsonValueKind.Array => e,
            JsonValueKind.Object when doc.RootElement.TryGetProperty("overrides", out JsonElement o) && o.ValueKind == JsonValueKind.Array => o,
            _ => default
        };
        if (array.ValueKind != JsonValueKind.Array) return;

        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? clut = TryReadString(item, "clutSha256Hex");
            string? rect = TryReadString(item, "rectSha256Hex");
            string? relPath = TryReadString(item, "relativeFilePath") ?? TryReadString(item, "file") ?? TryReadString(item, "path");
            if (string.IsNullOrWhiteSpace(clut) || string.IsNullOrWhiteSpace(rect) || string.IsNullOrWhiteSpace(relPath)) continue;
            if (!IsValidHexToken(clut) || !IsValidHexToken(rect)) continue;

            AssertSafeRelativePath(relPath, rootDir);
            string fullCandidate = Path.GetFullPath(Path.Combine(manifestDir, relPath));
            string contained = AssertSafeFile(fullCandidate, rootDir);
            if (File.Exists(contained)) AddEntry(clut, rect, contained, rootDir);
        }
    }

    private void AddEntry(string clutHex, string rectHex, string fullFilePath, string rootDir)
    {
        string normClut = NormalizeHex(clutHex);
        string normRect = NormalizeHex(rectHex);
        string relPath = Path.GetRelativePath(rootDir, fullFilePath).Replace('\\', '/');
        long size = new FileInfo(fullFilePath).Length;
        var key = new HdTextureOverrideKey(normClut, normRect);
        _entries[key] = new HdTextureOverrideEntry(normClut, normRect, relPath, fullFilePath, size);
    }

    private static string AssertSafeFile(string path, string rootDir)
    {
        string contained = AppEnvironment.AssertPathContained(Path.GetFullPath(path), rootDir);
        if (File.Exists(contained) && (File.GetAttributes(contained) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"HD pack file '{contained}' cannot be a reparse point.");
        }
        return contained;
    }

    private static void AssertSafeRelativePath(string relPath, string rootDir)
    {
        if (Path.IsPathRooted(relPath) || relPath.StartsWith('\\') || relPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"HD pack entry '{relPath}' must be relative to '{rootDir}'.");
        }
        foreach (string seg in relPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == "..")
            {
                throw new InvalidOperationException($"HD pack entry '{relPath}' contains forbidden '..' traversal.");
            }
        }
    }

    private static string? TryReadString(JsonElement obj, string propName) =>
        obj.TryGetProperty(propName, out JsonElement val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;

    private static string NormalizeHex(string raw) => raw.Trim().ToLowerInvariant();

    private static bool IsValidHexToken(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length is < 8 or > 64) return false;
        foreach (char c in trimmed)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }
}
