// @module: reEngine.App.RecompLaunchOptions
// @purpose: CLI option extraction and validated child passthrough flags for RecompLaunchCoordinator.
// @depends-on: System, System.Collections.Generic, System.Globalization, Foundation.Core
// @exports: RecompLaunchOptions

using System.Globalization;
using Foundation.Core;

namespace reEngine.App;

internal sealed record RecompLaunchOptions
{
    public static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir", "--stage",
        "--data-dir", "--config", "--log-dir", "--feed", "--port", "--ui",
        "--frames", "--fixture", "--dump-screenshot", "--dump-audio"
    };

    public bool ShouldWaitForExit { get; init; }
    public bool TrayOnly { get; init; }
    public bool ShouldEnableRuntimeLogs { get; init; }
    public string? LogDirectoryOverride { get; init; }
    public string? ConfigPathOverride { get; init; }
    public int? Frames { get; init; }
    public string? FixturePath { get; init; }
    public string? DumpScreenshotPath { get; init; }
    public string? DumpAudioPath { get; init; }

    public static RecompLaunchOptions FromArgs(string[] args, AppEnvironment appEnv)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(appEnv);

        string? framesRaw = GetOptionValue(args, "--frames", requiredIfPresent: true);
        int? frames = null;
        if (framesRaw is not null)
        {
            if (!int.TryParse(framesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFrames) || parsedFrames <= 0)
            {
                throw new ArgumentException($"Invalid --frames value '{framesRaw}'; expected a positive integer.");
            }
            frames = parsedFrames;
        }

        return new RecompLaunchOptions
        {
            ShouldWaitForExit = CliArgumentHelper.HasFlag(args, "--wait"),
            TrayOnly = appEnv.TrayOnly || appEnv.PresentationMode == UiPresentationMode.TrayOrBackground || CliArgumentHelper.HasFlag(args, "--tray-only"),
            ShouldEnableRuntimeLogs = CliArgumentHelper.HasFlag(args, "--enable-logs"),
            LogDirectoryOverride = GetOptionValue(args, "--log-dir", requiredIfPresent: true),
            ConfigPathOverride = GetOptionValue(args, "--config", requiredIfPresent: true),
            Frames = frames,
            FixturePath = GetOptionValue(args, "--fixture", requiredIfPresent: true),
            DumpScreenshotPath = GetOptionValue(args, "--dump-screenshot", requiredIfPresent: true),
            DumpAudioPath = GetOptionValue(args, "--dump-audio", requiredIfPresent: true)
        };
    }

    public static string? GetOptionValue(string[] args, string optionName, bool requiredIfPresent = false)
    {
        if (args is null || args.Length == 0)
        {
            return null;
        }

        string eqPrefix = optionName + "=";
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? string.Empty;
            if (arg.StartsWith(eqPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string inlineVal = arg[eqPrefix.Length..].Trim();
                if (inlineVal.Length == 0 && requiredIfPresent)
                {
                    throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                }
                return inlineVal.Length > 0 ? inlineVal : null;
            }

            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    if (requiredIfPresent)
                    {
                        throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                    }
                    return null;
                }

                string next = (args[i + 1] ?? string.Empty).Trim();
                if (next.Length == 0 || next.StartsWith("--", StringComparison.Ordinal))
                {
                    if (requiredIfPresent)
                    {
                        throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                    }
                    return null;
                }
                return next;
            }
        }

        return null;
    }

    public static string? GetPositionalArg(string[] args, int index)
    {
        if (args is null || args.Length == 0)
        {
            return null;
        }

        int current = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i] ?? string.Empty;
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (!a.Contains('=', StringComparison.Ordinal) &&
                    ValueOptions.Contains(a) &&
                    i + 1 < args.Length)
                {
                    i++;
                }
                continue;
            }

            if (a.StartsWith("-", StringComparison.Ordinal) ||
                string.Equals(a, "/?", StringComparison.Ordinal) ||
                string.Equals(a, "help", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (current == index)
            {
                return a;
            }
            current++;
        }

        return null;
    }
}
