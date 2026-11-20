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
        "--open", "--module", "--verify-module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir", "--stage",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--feed", "--port", "--ui",
        "--backend", "--scale", "--aspect-mode", "--suite-tab", "--profile", "--automation-port", "--automation-token",
        "--frames", "--fixture", "--dump-screenshot", "--dump-audio",
        "--shutdown-timeout-ms", "--audio-driver", "--media-path",
        "--execution-tier", "--display-mode", "--monitor", "--target-fps", "--bg-fps-cap",
        "--video-filter", "--hud-anchor", "--screenshot-mode", "--audio-device",
        "--master-volume", "--bgm-volume", "--sfx-volume", "--voice-volume",
        "--audio-lowpass-hz", "--audio-highpass-hz", "--stereo-width", "--audio-resampling",
        "--ff-audio-mode", "--ff-multiplier"
    };

    private static readonly HashSet<string> PathOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--verify-module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--feed",
        "--fixture", "--dump-screenshot", "--dump-audio", "--media-path"
    };

    private static readonly HashSet<string> SyncFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "decomp", "--open", "--wait", "--verify-module", "--headless", "--json", "--check-updates", "--register-assoc",
        "--verify", "--doctor", "--automation-port", "--jit-bringup", "--require-backend",
        "--preload-media", "--host-demo", "--dry-run", "--tray-only", "--help", "-h", "/?", "help", "--version", "-v"
    };

    /// <summary>
    /// Returns <see langword="true"/> when the launcher should attach to the console and wait synchronously for the child process.
    /// </summary>
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

    /// <summary>
    /// Resolves relative file and directory CLI arguments against <paramref name="callerWorkingDirectory"/> while preserving non-path option values.
    /// </summary>
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
                normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, inlineVal, baseDir)
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
                    normalized[i] = PathOptions.Contains(key) && ShouldNormalizeValue(key, val, baseDir)
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

    private static bool ShouldNormalizeValue(string optionKey, string rawValue, string baseDir)
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
        if (string.Equals(optionKey, "--verify-module", StringComparison.OrdinalIgnoreCase))
        {
            return LooksLikePackageOrFilePath(trimmedVal, baseDir);
        }
        return true;
    }

    private static bool LooksLikePackageOrFilePath(string val, string baseDir)
    {
        if (Path.IsPathRooted(val) || val.Contains('/') || val.Contains('\\') || val.StartsWith('.'))
            return true;
        if (val.EndsWith(".recomp", StringComparison.OrdinalIgnoreCase) ||
            val.EndsWith(".decomp", StringComparison.OrdinalIgnoreCase) ||
            val.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return true;
        string cand = Path.Combine(baseDir, val);
        return File.Exists(cand) || Directory.Exists(cand);
    }
}
