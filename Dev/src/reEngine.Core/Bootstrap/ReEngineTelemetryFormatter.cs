// @module: reEngine.Core.Bootstrap.ReEngineTelemetryFormatter
// @purpose: Standard 15 log line formatter and single-prefix [HEARTBEAT] normalizer for reEngine runtime logs.
// @depends-on: System, reEngine.Core.Bootstrap
// @exports: ReEngineTelemetryFormatter

namespace reEngine.Core.Bootstrap;

/// <summary>
/// Formats Standard 15 runtime log lines with version, process/thread identifiers, and single <c>[HEARTBEAT]</c> tags.
/// </summary>
public static class ReEngineTelemetryFormatter
{
    private const string HeartbeatPrefix = "[HEARTBEAT] ";

    /// <summary>
    /// Formats a UTC log entry matching Standard 15 §5.1 (<c>timestamp [vVer] [PID:id/T:tid] [LEVEL] message</c>).
    /// </summary>
    public static string FormatLogLine(string level, string message, DateTime? utcNow = null)
    {
        DateTime ts = utcNow ?? DateTime.UtcNow;
        string cleanLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
        string cleanMsg = string.Equals(cleanLevel, "HEARTBEAT", StringComparison.Ordinal)
            ? StripHeartbeatPrefix(message)
            : (message ?? string.Empty);

        return $"{ts:yyyy-MM-ddTHH:mm:ss.fffZ} [v{ReEnginePackagingConstants.Version}] [PID:{Environment.ProcessId}/T:{Environment.CurrentManagedThreadId:D2}] [{cleanLevel}] {cleanMsg}";
    }

    /// <summary>
    /// Removes a leading <c>[HEARTBEAT] </c> prefix produced by <c>Foundation.Core.HeartbeatLogger</c> to avoid duplicate tags.
    /// </summary>
    public static string StripHeartbeatPrefix(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        string trimmed = line.TrimStart();
        return trimmed.StartsWith(HeartbeatPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[HeartbeatPrefix.Length..]
            : line;
    }
}
