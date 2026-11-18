// @module: reEngine.App.RecompLaunchOptions
// @purpose: CLI option extraction and validated child passthrough flags (backends, JIT bring-up, automation) for RecompLaunchCoordinator.
// @depends-on: System, System.Collections.Generic, System.Globalization, Foundation.Core
// @exports: RecompLaunchOptions

using System.Globalization;
using Foundation.Core;

namespace reEngine.App;

internal sealed record RecompLaunchOptions
{
    public static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--verify-module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir", "--stage",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--feed", "--port", "--ui",
        "--backend", "--scale", "--suite-tab", "--profile", "--automation-port", "--automation-token",
        "--frames", "--fixture", "--dump-screenshot", "--dump-audio"
    };

    public static readonly IReadOnlyDictionary<string, string> SupportedBackends = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["opengl"] = "opengl",
        ["vulkan"] = "vulkan",
        ["parallelCpu"] = "parallelCpu",
        ["software"] = "software"
    };

    public bool ShouldWaitForExit { get; init; }
    public bool TrayOnly { get; init; }
    public bool ShouldEnableRuntimeLogs { get; init; }
    public bool Verify { get; init; }
    public string? VerifyModule { get; init; }
    public bool Doctor { get; init; }
    public bool RequireBackend { get; init; }
    public bool JitBringup { get; init; }
    public bool PreloadMediaFlag { get; init; }
    public bool HostDemo { get; init; }
    public string? BackendId { get; init; }
    public int? ResolutionScale { get; init; }
    public string? SaveDirOverride { get; init; }
    public string? SuiteTab { get; init; }
    public string? Profile { get; init; }
    public int? AutomationPort { get; init; }
    public string? AutomationToken { get; init; }
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

        int? frames = ParseOptionalPositiveInt(GetOptionValue(args, "--frames", requiredIfPresent: true), "--frames");
        int? automationPort = ParseOptionalNonNegativeInt(GetOptionValue(args, "--automation-port", requiredIfPresent: true), "--automation-port");
        string? rawBackend = GetOptionValue(args, "--backend", requiredIfPresent: true);
        string? normalizedBackend = null;
        if (!string.IsNullOrWhiteSpace(rawBackend))
        {
            if (!SupportedBackends.TryGetValue(rawBackend.Trim(), out normalizedBackend))
            {
                throw new ArgumentException($"Unsupported --backend '{rawBackend}'; expected opengl, vulkan, parallelCpu, or software.");
            }
        }
        int? resolutionScale = ParseOptionalResolutionScale(GetOptionValue(args, "--scale", requiredIfPresent: true));

        return new RecompLaunchOptions
        {
            ShouldWaitForExit = CliArgumentHelper.HasFlag(args, "--wait"),
            TrayOnly = appEnv.TrayOnly || appEnv.PresentationMode == UiPresentationMode.TrayOrBackground || CliArgumentHelper.HasFlag(args, "--tray-only"),
            ShouldEnableRuntimeLogs = CliArgumentHelper.HasFlag(args, "--enable-logs"),
            Verify = CliArgumentHelper.HasFlag(args, "--verify"),
            VerifyModule = GetOptionValue(args, "--verify-module", requiredIfPresent: true),
            Doctor = CliArgumentHelper.HasFlag(args, "--doctor"),
            RequireBackend = CliArgumentHelper.HasFlag(args, "--require-backend"),
            JitBringup = CliArgumentHelper.HasFlag(args, "--jit-bringup"),
            PreloadMediaFlag = CliArgumentHelper.HasFlag(args, "--preload-media"),
            HostDemo = CliArgumentHelper.HasFlag(args, "--host-demo"),
            BackendId = normalizedBackend,
            ResolutionScale = resolutionScale,
            SaveDirOverride = GetOptionValue(args, "--save-dir", requiredIfPresent: true),
            SuiteTab = GetOptionValue(args, "--suite-tab", requiredIfPresent: true),
            Profile = GetOptionValue(args, "--profile", requiredIfPresent: true),
            AutomationPort = automationPort,
            AutomationToken = GetOptionValue(args, "--automation-token", requiredIfPresent: true),
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
        if (args is null || args.Length == 0) return null;
        string eqPrefix = optionName + "=";
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? string.Empty;
            if (arg.StartsWith(eqPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string inlineVal = arg[eqPrefix.Length..].Trim();
                if (inlineVal.Length == 0 && requiredIfPresent) throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                return inlineVal.Length > 0 ? inlineVal : null;
            }
            if (string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    if (requiredIfPresent) throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                    return null;
                }
                string next = (args[i + 1] ?? string.Empty).Trim();
                if (next.Length == 0 || next.StartsWith("--", StringComparison.Ordinal))
                {
                    if (requiredIfPresent) throw new ArgumentException($"Missing value for CLI option '{optionName}'.");
                    return null;
                }
                return next;
            }
        }
        return null;
    }

    public static string? GetPositionalArg(string[] args, int index)
    {
        if (args is null || args.Length == 0) return null;
        int current = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i] ?? string.Empty;
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (!a.Contains('=', StringComparison.Ordinal) && ValueOptions.Contains(a) && i + 1 < args.Length) i++;
                continue;
            }
            if (a.StartsWith("-", StringComparison.Ordinal) || string.Equals(a, "/?", StringComparison.Ordinal) || string.Equals(a, "help", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (current == index) return a;
            current++;
        }
        return null;
    }

    private static int? ParseOptionalResolutionScale(string? raw)
    {
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) ||
            val is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentException($"Invalid --scale value '{raw}'; expected 1, 2, 4, or 8.");
        }
        return val;
    }

    private static int? ParseOptionalPositiveInt(string? raw, string optionName)
    {
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) || val <= 0)
            throw new ArgumentException($"Invalid {optionName} value '{raw}'; expected a positive integer.");
        return val;
    }

    private static int? ParseOptionalNonNegativeInt(string? raw, string optionName)
    {
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) || val < 0 || val > 65535)
            throw new ArgumentException($"Invalid {optionName} value '{raw}'; expected port 0..65535.");
        return val;
    }
}
