// @module: reEngine.Host.Contracts.HostHdPackContracts
// @purpose: Standard 18 clean-room <dataDir>\hdpacks\ texture override contracts keyed by clutSha256Hex and rectSha256Hex.
// @depends-on: System
// @exports: HdTextureOverrideKey, HdTextureOverrideEntry, IHdTexturePackResolver

namespace reEngine.Host.Contracts;

/// <summary>
/// Composite CLUT + VRAM rectangle SHA-256 key identifying an override texture under <c>hdpacks\</c>.
/// </summary>
public readonly record struct HdTextureOverrideKey(
    string ClutSha256Hex,
    string RectSha256Hex);

/// <summary>
/// Validated path-contained <c>hdpacks\</c> texture override asset record.
/// </summary>
public sealed record HdTextureOverrideEntry(
    string ClutSha256Hex,
    string RectSha256Hex,
    string RelativeFilePath,
    string FullFilePath,
    long FileSizeBytes);

/// <summary>
/// Resolves optional user-supplied HD texture overrides strictly contained inside <c>&lt;dataDir&gt;\hdpacks\</c>.
/// </summary>
public interface IHdTexturePackResolver
{
    bool IsEnabled { get; }
    string? PackRootDirectory { get; }
    int IndexedCount { get; }
    bool TryResolve(string clutSha256Hex, string rectSha256Hex, out HdTextureOverrideEntry? entry);
}
