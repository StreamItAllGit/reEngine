// @module: reEngine.Launcher.LauncherArgsHelper
// @purpose: Pure BCL CLI sync-wait detection and caller-CWD relative path normalization for the standalone reEngine root launcher.
// @depends-on: System, System.Collections.Generic, System.IO
// @exports: LauncherArgsHelper

using System;
using System.Collections.Generic;
using System.IO;

namespace reEngine.Launcher;

internal static class LauncherArgsHelper
{
    internal static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir", "--stage",
        "--data-dir", "--config", "--log-dir", "--feed", "--port", "--ui",
        "--frames", "--fixture", "--dump-screenshot", "--dump-audio", "--input-script"
    };

    private static readonly HashSet<string> PathOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir",
        "--data-dir", "--config", "--log-dir", "--feed",
        "--fixture", "--dump-screenshot", "--dump-audio", "--input-script"
    };

    private static readonly HashSet<string> SyncFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "decomp", "--open", "--wait", "--check-updates", "--register-assoc", "--visible-test", "--test-mode",
        "--headless", "--dry-run", "--help", "-h", "/?", "help", "--version", "-v"
    };

    public static bool ShouldWaitForChild(string[]? args)
    {
        if (args is null || args.Length == 0) return false;

        for (int i = 0; i < args.Length; i++)
        {
            string trimmed = (args[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0) continue;

            (string key, bool hasInlineValue) = SplitOption(trimmed);
            if (SyncFlags.Contains(key)) return true;

            if (ValueOptions.Contains(key))
            {
                if (!hasInlineValue && i + 1 < args.Length) i++;
                continue;
            }

            if (!trimmed.StartsWith("-", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    public static string[] NormalizePathArguments(string[]? args, string callerWorkingDirectory)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();

        string baseDir = string.IsNullOrWhiteSpace(callerWorkingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(callerWorkingDirectory);

        var normalized = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) { normalized[i] = raw; continue; }

            (string key, bool hasInlineValue) = SplitOption(trimmed);
            if (hasInlineValue)
            {
                string inlineVal = trimmed[(key.Length + 1)..];
                normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, inlineVal)
                    ? $"{key}={Path.GetFullPath(inlineVal.Trim(), baseDir)}"
                    : raw;
                continue;
            }

            normalized[i] = raw;
            if (ValueOptions.Contains(key))
            {
                if (i + 1 < args.Length)
                {
                    i++;
                    string val = args[i] ?? string.Empty;
                    normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, val)
                        ? Path.GetFullPath(val.Trim(), baseDir)
                        : val;
                }
                continue;
            }

            if (!trimmed.StartsWith("-", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "/?", StringComparison.Ordinal) &&
                !string.Equals(trimmed, "help", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmed, "decomp", StringComparison.OrdinalIgnoreCase))
            {
                normalized[i] = Path.GetFullPath(trimmed, baseDir);
            }
        }

        return normalized;
    }

    private static (string Key, bool HasInlineValue) SplitOption(string trimmed)
    {
        if (trimmed.StartsWith("--", StringComparison.Ordinal))
        {
            int eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 2) return (trimmed[..eqIdx], true);
        }
        return (trimmed, false);
    }

    private static bool ShouldNormalizeValue(string optionKey, string rawValue)
    {
        string trimmedVal = (rawValue ?? string.Empty).Trim();
        if (trimmedVal.Length == 0) return false;

        if (string.Equals(optionKey, "--feed", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(trimmedVal, UriKind.Absolute, out Uri? uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return true;
    }
}
