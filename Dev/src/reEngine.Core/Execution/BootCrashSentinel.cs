// @module: reEngine.Core.Execution.BootCrashSentinel
// @purpose: Manages atomic <dataDir>\booting.sentinel PID liveness checks and in-memory Safe Mode recovery with cache rebuild guidance.
// @depends-on: System, System.Diagnostics, System.IO, System.Text, System.Text.Json, System.Text.Json.Serialization
// @exports: BootCrashSentinelPayload, BootCrashCheckResult, BootCrashSentinel

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace reEngine.Core.Execution;

/// <summary>
/// Persisted JSON payload recorded inside <c>&lt;dataDir&gt;\booting.sentinel</c> while host boot is in progress.
/// </summary>
public sealed record BootCrashSentinelPayload
{
    /// <summary>OS process ID that initiated the boot attempt.</summary>
    [JsonPropertyName("pid")]
    public int Pid { get; init; }

    /// <summary>Canonical module identifier being booted (or <c>host-demo</c>).</summary>
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; init; } = "host-demo";

    /// <summary>ISO-8601 UTC timestamp when boot began.</summary>
    [JsonPropertyName("startedAtUtc")]
    public string StartedAtUtc { get; init; } = string.Empty;
}

/// <summary>
/// Startup Safe Mode evaluation result produced by inspecting <c>&lt;dataDir&gt;\booting.sentinel</c>.
/// </summary>
public sealed record BootCrashCheckResult
{
    /// <summary>Gets whether Safe Mode must be activated in memory for this session.</summary>
    public bool IsSafeModeTriggered { get; init; }

    /// <summary>Gets whether an uncleared <c>booting.sentinel</c> existed prior to boot.</summary>
    public bool HadPreviousSentinel { get; init; }

    /// <summary>Gets whether the PID recorded in the previous sentinel is still alive.</summary>
    public bool IsPreviousProcessAlive { get; init; }

    /// <summary>Gets the parsed previous sentinel payload when readable.</summary>
    public BootCrashSentinelPayload? PreviousPayload { get; init; }

    /// <summary>Gets the full path to <c>&lt;dataDir&gt;\booting.sentinel</c>.</summary>
    public string SentinelPath { get; init; } = string.Empty;

    /// <summary>Gets the full path to the AOT cache directory (<c>&lt;dataDir&gt;\cache\</c>).</summary>
    public string CacheDirectory { get; init; } = string.Empty;

    /// <summary>Gets actionable <c>&lt;dataDir&gt;\cache\</c> rebuild guidance when Safe Mode triggers.</summary>
    public string? CacheRebuildGuidance { get; init; }

    /// <summary>Gets a human-readable diagnostic describing the startup check outcome.</summary>
    public string DiagnosticMessage { get; init; } = string.Empty;
}

/// <summary>
/// Tracks in-flight boots via atomic <c>&lt;dataDir&gt;\booting.sentinel</c> files to trigger in-memory Safe Mode after dirty crashes.
/// </summary>
public sealed class BootCrashSentinel
{
    /// <summary>Canonical sentinel filename stored directly under <c>&lt;dataDir&gt;</c>.</summary>
    public const string SentinelFileName = "booting.sentinel";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Initializes a new <see cref="BootCrashSentinel"/> bound to <paramref name="dataDirectory"/>.</summary>
    /// <param name="dataDirectory">Application or module data directory root.</param>
    public BootCrashSentinel(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory.Trim());
        SentinelPath = Path.Combine(DataDirectory, SentinelFileName);
        CacheDirectory = Path.Combine(DataDirectory, "cache");
    }

    /// <summary>Gets the resolved data directory root.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the full path to <c>booting.sentinel</c>.</summary>
    public string SentinelPath { get; }

    /// <summary>Gets the full path to the local <c>cache\</c> directory.</summary>
    public string CacheDirectory { get; }

    /// <summary>Gets whether the sentinel has been cleared after rendering the first interactive frame.</summary>
    public bool HasClearedOnFirstFrame { get; private set; }

    /// <summary>
    /// Inspects any existing <c>booting.sentinel</c> on disk via PID liveness without overwriting it.
    /// </summary>
    /// <param name="wasSafeModeReset">When <c>true</c>, suppresses triggering Safe Mode for an uncleared dead-PID sentinel.</param>
    /// <returns>Evaluation outcome indicating whether Safe Mode must be activated in memory.</returns>
    public BootCrashCheckResult InspectExisting(bool wasSafeModeReset = false)
    {
        if (!File.Exists(SentinelPath))
        {
            return new BootCrashCheckResult
            {
                IsSafeModeTriggered = false,
                HadPreviousSentinel = false,
                SentinelPath = SentinelPath,
                CacheDirectory = CacheDirectory,
                DiagnosticMessage = "Clean boot: no previous booting.sentinel detected."
            };
        }

        BootCrashSentinelPayload? previous = ReadCurrentSentinel();
        if (previous is null || previous.Pid <= 0)
        {
            return BuildUnclearedResult(
                previous,
                isAlive: false,
                wasSafeModeReset,
                "Corrupt or unreadable booting.sentinel detected from prior boot.");
        }

        bool alive = IsProcessAlive(previous.Pid);
        return alive
            ? new BootCrashCheckResult
            {
                IsSafeModeTriggered = false,
                HadPreviousSentinel = true,
                IsPreviousProcessAlive = true,
                PreviousPayload = previous,
                SentinelPath = SentinelPath,
                CacheDirectory = CacheDirectory,
                DiagnosticMessage = $"Active process PID {previous.Pid} holds booting.sentinel for '{previous.ModuleId}'."
            }
            : BuildUnclearedResult(
                previous,
                isAlive: false,
                wasSafeModeReset,
                $"Dead PID {previous.Pid} left an uncleared booting.sentinel for '{previous.ModuleId}'.");
    }

    /// <summary>
    /// Inspects any existing sentinel for prior dirty termination, then atomically writes a fresh sentinel for this process.
    /// </summary>
    /// <param name="moduleId">Module identifier being booted.</param>
    /// <param name="wasSafeModeReset">When <c>true</c>, honors an explicit user Safe Mode reset.</param>
    /// <returns>The pre-boot crash-check evaluation outcome.</returns>
    public BootCrashCheckResult BeginBoot(string? moduleId = null, bool wasSafeModeReset = false)
    {
        BootCrashCheckResult result = InspectExisting(wasSafeModeReset);
        HasClearedOnFirstFrame = false;

        var payload = new BootCrashSentinelPayload
        {
            Pid = Environment.ProcessId,
            ModuleId = string.IsNullOrWhiteSpace(moduleId) ? "host-demo" : moduleId.Trim(),
            StartedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        WriteSentinelAtomically(payload);
        return result;
    }

    /// <summary>
    /// Clears <c>booting.sentinel</c> cleanly once the first interactive frame has rendered.
    /// </summary>
    /// <returns><c>true</c> if the sentinel is cleared (or already cleared); otherwise <c>false</c>.</returns>
    public bool ClearOnFirstFrame()
    {
        if (HasClearedOnFirstFrame)
        {
            return true;
        }

        return Clear();
    }

    /// <summary>
    /// Deletes <c>booting.sentinel</c> immediately if present.
    /// </summary>
    /// <returns><c>true</c> if the sentinel was removed or absent; otherwise <c>false</c>.</returns>
    public bool Clear()
    {
        try
        {
            if (File.Exists(SentinelPath))
            {
                File.Delete(SentinelPath);
            }

            HasClearedOnFirstFrame = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads and deserializes the current <c>booting.sentinel</c> payload if present and valid JSON.
    /// </summary>
    /// <returns>Parsed payload or <see langword="null"/> when missing or corrupt.</returns>
    public BootCrashSentinelPayload? ReadCurrentSentinel()
    {
        if (!File.Exists(SentinelPath))
        {
            return null;
        }

        try
        {
            string raw = File.ReadAllText(SentinelPath, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(raw)
                ? null
                : JsonSerializer.Deserialize<BootCrashSentinelPayload>(raw, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Formats Safe Mode and <c>&lt;dataDir&gt;\cache\</c> rebuild guidance for host UI banners and logs.
    /// </summary>
    /// <param name="cacheDirectory">Resolved <c>&lt;dataDir&gt;\cache\</c> path.</param>
    /// <returns>Human-readable Safe Mode guidance message.</returns>
    public static string BuildCacheRebuildGuidance(string cacheDirectory) =>
        $"Safe Mode active: previous launch exited before first frame. .re* packs, HD texture overrides, and unsafe GPU overrides are disabled in memory without altering saved config; rebuild AOT cache under '{cacheDirectory}' if crashes persist.";

    private BootCrashCheckResult BuildUnclearedResult(
        BootCrashSentinelPayload? previous,
        bool isAlive,
        bool wasSafeModeReset,
        string reason)
    {
        if (wasSafeModeReset)
        {
            return new BootCrashCheckResult
            {
                IsSafeModeTriggered = false,
                HadPreviousSentinel = true,
                IsPreviousProcessAlive = isAlive,
                PreviousPayload = previous,
                SentinelPath = SentinelPath,
                CacheDirectory = CacheDirectory,
                DiagnosticMessage = $"{reason} Safe Mode was explicitly reset; continuing normal launch."
            };
        }

        string guidance = BuildCacheRebuildGuidance(CacheDirectory);
        return new BootCrashCheckResult
        {
            IsSafeModeTriggered = true,
            HadPreviousSentinel = true,
            IsPreviousProcessAlive = isAlive,
            PreviousPayload = previous,
            SentinelPath = SentinelPath,
            CacheDirectory = CacheDirectory,
            CacheRebuildGuidance = guidance,
            DiagnosticMessage = $"{reason} {guidance}"
        };
    }

    private void WriteSentinelAtomically(BootCrashSentinelPayload payload)
    {
        Directory.CreateDirectory(DataDirectory);
        string tempPath = $"{SentinelPath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        try
        {
            File.WriteAllText(tempPath, json, Utf8NoBom);
            File.Move(tempPath, SentinelPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using Process proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}
