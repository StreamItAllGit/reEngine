// @module: reEngine.Core.Execution.MediaRamPreloader
// @purpose: Containment-enforced media RAM preloader guarding physical memory headroom (>= 512 MB & <= 75% available) with graceful streaming fallback.
// @depends-on: System, System.Buffers, System.IO, System.Runtime.InteropServices, System.Threading, Foundation.Core
// @exports: PreloadedMediaBuffer, MediaRamPreloadResult, MediaRamPreloader

using System.Buffers;
using System.Runtime.InteropServices;
using Foundation.Core;

namespace reEngine.Core.Execution;

/// <summary>
/// Disposable pinned/pooled byte buffer backing RAM-preloaded BYO ROM or disc track payloads.
/// </summary>
public sealed class PreloadedMediaBuffer : IDisposable
{
    private byte[]? _rented;
    private GCHandle _pinHandle;
    private int _disposed;

    internal PreloadedMediaBuffer(byte[]? rented, GCHandle pinHandle, int length)
    {
        _rented = rented;
        _pinHandle = pinHandle;
        Length = length;
    }

    /// <summary>Gets the exact payload byte length stored inside the buffer.</summary>
    public int Length { get; }

    /// <summary>Gets whether this buffer has already been released back to the pool.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Gets the pinned native memory pointer while allocated, or <see cref="IntPtr.Zero"/> when empty/disposed.</summary>
    public IntPtr PinnedAddress =>
        IsDisposed || !_pinHandle.IsAllocated ? IntPtr.Zero : _pinHandle.AddrOfPinnedObject();

    /// <summary>Returns a read-only byte span over the preloaded payload.</summary>
    public ReadOnlySpan<byte> AsSpan() =>
        IsDisposed || _rented is null ? ReadOnlySpan<byte>.Empty : _rented.AsSpan(0, Length);

    /// <summary>Returns read-only memory over the preloaded payload.</summary>
    public ReadOnlyMemory<byte> AsMemory() =>
        IsDisposed || _rented is null ? ReadOnlyMemory<byte>.Empty : _rented.AsMemory(0, Length);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_pinHandle.IsAllocated)
        {
            _pinHandle.Free();
        }

        byte[]? rented = Interlocked.Exchange(ref _rented, null);
        if (rented is not null)
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }
}

/// <summary>
/// Outcome of attempting to preload contained media bytes into RAM, falling back gracefully on insufficient headroom or missing files.
/// </summary>
public sealed record MediaRamPreloadResult : IDisposable
{
    /// <summary>Gets whether media bytes were successfully preloaded into RAM.</summary>
    public bool Preloaded { get; init; }

    /// <summary>Gets whether the host should stream media from disk instead of RAM.</summary>
    public bool FallbackToStreaming { get; init; }

    /// <summary>Gets the containment-verified media path.</summary>
    public string? ContainedPath { get; init; }

    /// <summary>Gets the inspected file length in bytes.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Gets the measured physical available RAM in bytes at check time.</summary>
    public long AvailablePhysicalBytes { get; init; }

    /// <summary>Gets the disposable pinned RAM buffer when <see cref="Preloaded"/> is <c>true</c>.</summary>
    public PreloadedMediaBuffer? Buffer { get; init; }

    /// <summary>Gets the diagnostic severity level (<c>INFO</c> or <c>WARN</c>).</summary>
    public string DiagnosticLevel { get; init; } = "INFO";

    /// <summary>Gets the human-readable diagnostic message describing preload success or streaming fallback.</summary>
    public string DiagnosticMessage { get; init; } = string.Empty;

    /// <inheritdoc />
    public void Dispose() => Buffer?.Dispose();
}

/// <summary>
/// Containment-safe preloader that verifies host physical RAM headroom before buffering media into pinned RAM.
/// </summary>
public static class MediaRamPreloader
{
    /// <summary>Minimum physical memory headroom (512 MiB) that must remain free after preloading.</summary>
    public const long RequiredFreeHeadroomBytes = 512L * 1024 * 1024;

    /// <summary>Maximum fraction (75%) of currently available physical RAM allowed for a single preload.</summary>
    public const double MaxPhysicalRamFraction = 0.75;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>
    /// Queries currently available physical memory using both <see cref="GC.GetGCMemoryInfo()"/> and Win32 <c>GlobalMemoryStatusEx</c> on Windows.
    /// </summary>
    /// <returns>Conservative available physical RAM estimate in bytes.</returns>
    public static long QueryAvailablePhysicalMemoryBytes()
    {
        long gcAvail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (!OperatingSystem.IsWindows())
        {
            return Math.Max(0L, gcAvail);
        }

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.AvailPhys == 0)
        {
            return Math.Max(0L, gcAvail);
        }

        long winAvail = (long)Math.Min(status.AvailPhys, (ulong)long.MaxValue);
        return gcAvail > 0 ? Math.Min(gcAvail, winAvail) : winAvail;
    }

    /// <summary>
    /// Evaluates whether preloading <paramref name="fileSizeBytes"/> leaves >= 512 MiB headroom and consumes &lt;= 75% of <paramref name="availablePhysicalBytes"/>.
    /// </summary>
    /// <param name="fileSizeBytes">Payload size in bytes.</param>
    /// <param name="availablePhysicalBytes">Measured available physical RAM in bytes.</param>
    /// <returns><c>true</c> if preloading is safe; otherwise <c>false</c>.</returns>
    public static bool IsSafeToPreload(long fileSizeBytes, long availablePhysicalBytes)
    {
        if (fileSizeBytes < 0 || fileSizeBytes > Array.MaxLength)
        {
            return false;
        }

        if (availablePhysicalBytes < RequiredFreeHeadroomBytes)
        {
            return false;
        }

        if (fileSizeBytes > availablePhysicalBytes - RequiredFreeHeadroomBytes)
        {
            return false;
        }

        long maxByFraction = (long)(availablePhysicalBytes * MaxPhysicalRamFraction);
        return fileSizeBytes <= maxByFraction;
    }

    /// <summary>
    /// Enforces containment of <paramref name="mediaPath"/> inside <paramref name="allowedRoot"/> and preloads bytes into RAM when safe.
    /// </summary>
    /// <param name="mediaPath">Target ROM or disc media path (absolute or relative to <paramref name="allowedRoot"/>).</param>
    /// <param name="allowedRoot">Fail-closed containment root directory.</param>
    /// <param name="availablePhysicalBytesOverride">Optional deterministic physical RAM override for testing.</param>
    /// <returns>Preload outcome containing a pinned <see cref="PreloadedMediaBuffer"/> or a <c>[WARN]</c> streaming fallback.</returns>
    public static MediaRamPreloadResult TryPreload(
        string mediaPath,
        string allowedRoot,
        long? availablePhysicalBytesOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);

        string fullRoot = Path.GetFullPath(allowedRoot.Trim());
        string candidatePath = Path.IsPathRooted(mediaPath.Trim())
            ? Path.GetFullPath(mediaPath.Trim())
            : Path.GetFullPath(Path.Combine(fullRoot, mediaPath.Trim()));
        string containedPath = AppEnvironment.AssertPathContained(candidatePath, fullRoot);

        long availableBytes = availablePhysicalBytesOverride ?? QueryAvailablePhysicalMemoryBytes();
        if (!File.Exists(containedPath))
        {
            return BuildWarnFallback(
                containedPath,
                0,
                availableBytes,
                $"[WARN] Preload media file not found at '{containedPath}'; falling back to disk streaming.");
        }

        if ((File.GetAttributes(containedPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Fail-Closed Containment Violation: media file '{containedPath}' cannot be a reparse point.");
        }

        long fileSizeBytes = new FileInfo(containedPath).Length;
        if (!IsSafeToPreload(fileSizeBytes, availableBytes))
        {
            return BuildWarnFallback(
                containedPath,
                fileSizeBytes,
                availableBytes,
                $"[WARN] Insufficient physical RAM headroom to preload '{Path.GetFileName(containedPath)}' ({fileSizeBytes} bytes vs {availableBytes} available); falling back to disk streaming.");
        }

        return ReadIntoPinnedBuffer(containedPath, (int)fileSizeBytes, availableBytes);
    }

    private static MediaRamPreloadResult ReadIntoPinnedBuffer(string containedPath, int length, long availableBytes)
    {
        if (length == 0)
        {
            var emptyBuffer = new PreloadedMediaBuffer(null, default, 0);
            return new MediaRamPreloadResult
            {
                Preloaded = true,
                FallbackToStreaming = false,
                ContainedPath = containedPath,
                FileSizeBytes = 0,
                AvailablePhysicalBytes = availableBytes,
                Buffer = emptyBuffer,
                DiagnosticLevel = "INFO",
                DiagnosticMessage = $"Preloaded 0 byte(s) of '{Path.GetFileName(containedPath)}' into RAM."
            };
        }

        byte[]? rented = null;
        GCHandle pinHandle = default;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(length);
            using FileStream fs = new(containedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = fs.Read(rented, totalRead, length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            pinHandle = GCHandle.Alloc(rented, GCHandleType.Pinned);
            var buffer = new PreloadedMediaBuffer(rented, pinHandle, totalRead);
            rented = null;
            pinHandle = default;

            return new MediaRamPreloadResult
            {
                Preloaded = true,
                FallbackToStreaming = false,
                ContainedPath = containedPath,
                FileSizeBytes = totalRead,
                AvailablePhysicalBytes = availableBytes,
                Buffer = buffer,
                DiagnosticLevel = "INFO",
                DiagnosticMessage = $"Preloaded {totalRead} byte(s) of '{Path.GetFileName(containedPath)}' into pinned RAM."
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            if (pinHandle.IsAllocated)
            {
                pinHandle.Free();
            }

            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
            }

            return BuildWarnFallback(
                containedPath,
                length,
                availableBytes,
                $"[WARN] RAM preload aborted for '{Path.GetFileName(containedPath)}' ({ex.Message}); falling back to disk streaming.");
        }
    }

    private static MediaRamPreloadResult BuildWarnFallback(
        string containedPath,
        long fileSizeBytes,
        long availableBytes,
        string diagnostic) =>
        new()
        {
            Preloaded = false,
            FallbackToStreaming = true,
            ContainedPath = containedPath,
            FileSizeBytes = fileSizeBytes,
            AvailablePhysicalBytes = availableBytes,
            Buffer = null,
            DiagnosticLevel = "WARN",
            DiagnosticMessage = diagnostic
        };
}
