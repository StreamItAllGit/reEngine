// @module: reEngine.Host.Core.HostScreenshotService
// @purpose: Post-present lossless F12 PNG screenshot service supporting CleanNative vs FilteredComposite capture modes via RgbaPngWriter.
// @depends-on: System, System.Globalization, System.IO, System.Text, Foundation.Core, reEngine.Host.Contracts
// @exports: HostScreenshotService

using System.Globalization;
using System.Text;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

/// <summary>
/// Writes lossless RGBA PNG screenshots for <c>F12</c> captures into <c>&lt;dataDir&gt;\screenshots\&lt;moduleId&gt;\</c>
/// (or explicit dump paths) using <see cref="RgbaPngWriter"/>.
/// </summary>
public sealed class HostScreenshotService
{
    private long _sequenceCounter;

    /// <summary>Captures a clean native or filtered composite frame to disk and returns the capture outcome.</summary>
    public HostScreenshotResult Capture(
        HostScreenshotCaptureMode captureMode,
        ReadOnlySpan<byte> cleanNativeRgba,
        int cleanWidth,
        int cleanHeight,
        ReadOnlySpan<byte> filteredCompositeRgba,
        int filteredWidth,
        int filteredHeight,
        string? screenshotsDirectory,
        string? dataDirectory,
        string? moduleId,
        string? explicitDumpFramePath = null,
        string? allowedSnapshotRoot = null)
    {
        bool useFiltered = captureMode == HostScreenshotCaptureMode.FilteredComposite
            && filteredWidth > 0
            && filteredHeight > 0
            && filteredCompositeRgba.Length >= checked(filteredWidth * filteredHeight * 4);

        ReadOnlySpan<byte> source = useFiltered ? filteredCompositeRgba : cleanNativeRgba;
        int width = useFiltered ? filteredWidth : cleanWidth;
        int height = useFiltered ? filteredHeight : cleanHeight;
        HostScreenshotCaptureMode effectiveMode = useFiltered ? HostScreenshotCaptureMode.FilteredComposite : HostScreenshotCaptureMode.CleanNative;

        if (width <= 0 || height <= 0 || source.Length < checked(width * height * 4))
        {
            return new HostScreenshotResult(false, null, effectiveMode, width, height, "Source RGBA buffer is empty or smaller than expected dimensions.");
        }

        string targetPath = ResolveOutputPath(screenshotsDirectory, dataDirectory, moduleId, explicitDumpFramePath, DateTime.UtcNow);
        try
        {
            if (!string.IsNullOrWhiteSpace(allowedSnapshotRoot))
            {
                targetPath = AppEnvironment.AssertPathContained(targetPath, allowedSnapshotRoot);
            }

            ReadOnlySpan<byte> exactSlice = source[..checked(width * height * 4)];
            RgbaPngWriter.WritePng(targetPath, width, height, exactSlice);
            return new HostScreenshotResult(true, targetPath, effectiveMode, width, height, $"Captured {effectiveMode} ({width}x{height}) to {targetPath}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return new HostScreenshotResult(false, null, effectiveMode, width, height, $"Screenshot capture failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a collision-free <c>YYYYMMDD_HHMMSS_&lt;n&gt;.png</c> destination under
    /// <c>&lt;dataDir&gt;\screenshots\&lt;moduleId&gt;\</c> (defaulting <c>moduleId</c> to <c>host-demo</c>, or using <paramref name="explicitDumpFramePath"/>).
    /// </summary>
    public string ResolveOutputPath(
        string? screenshotsDirectory,
        string? dataDirectory,
        string? moduleId,
        string? explicitDumpFramePath,
        DateTime utcNow)
    {
        string safeModule = SanitizeModuleFolder(moduleId);
        string? folder = null;
        if (!string.IsNullOrWhiteSpace(screenshotsDirectory))
        {
            folder = Path.Combine(Path.GetFullPath(screenshotsDirectory.Trim()), safeModule);
        }
        else if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            folder = Path.Combine(Path.GetFullPath(dataDirectory.Trim()), "screenshots", safeModule);
        }
        else if (!string.IsNullOrWhiteSpace(explicitDumpFramePath))
        {
            return Path.GetFullPath(explicitDumpFramePath.Trim());
        }
        else
        {
            folder = Path.Combine(Path.GetTempPath(), "reEngine", "screenshots", safeModule);
        }

        string stamp = utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        while (true)
        {
            long seq = Interlocked.Increment(ref _sequenceCounter);
            string candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stamp}_{seq}.png"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    internal static string SanitizeModuleFolder(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId)) return "host-demo";
        var sb = new StringBuilder(moduleId.Length);
        foreach (char ch in moduleId.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        string sanitized = sb.ToString().Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "host-demo" : sanitized;
    }
}
