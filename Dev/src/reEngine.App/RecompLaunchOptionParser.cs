// @module: reEngine.App.RecompLaunchOptionParser
// @purpose: Strict CLI option extraction, --no-* boolean override parsing, and v0.6.0 range/enum validation helpers.
// @depends-on: System, System.Collections.Generic, System.Globalization, Foundation.Core, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: RecompLaunchOptionParser

using System.Globalization;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reEngine.App;

internal static class RecompLaunchOptionParser
{
    public delegate bool TryParseCodecDelegate<T>(string? raw, out T value);

    public static RecompLaunchOptions Parse(string[] args, AppEnvironment appEnv, IReadOnlySet<string> valueOptions)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(appEnv);
        ArgumentNullException.ThrowIfNull(valueOptions);

        string? Val(string name) => GetOptionValue(args, name, requiredIfPresent: true);
        string? rawBackend = Val("--backend");
        string? normalizedBackend = null;
        if (!string.IsNullOrWhiteSpace(rawBackend))
        {
            if (!HostRenderBackendIds.TryNormalize(rawBackend, out string canonical))
                throw new ArgumentException($"Unsupported --backend '{rawBackend}'; expected opengl, vulkan, directx, parallelCpu, or software.");
            normalizedBackend = canonical;
        }

        return new RecompLaunchOptions
        {
            ShouldWaitForExit = CliArgumentHelper.HasFlag(args, "--wait"),
            Fullscreen = CliArgumentHelper.HasFlag(args, "--fullscreen"),
            Headless = CliArgumentHelper.HasFlag(args, "--headless"),
            Json = CliArgumentHelper.HasFlag(args, "--json"),
            RequireMedia = CliArgumentHelper.HasFlag(args, "--require-media"),
            TrayOnly = appEnv.TrayOnly || appEnv.PresentationMode == UiPresentationMode.TrayOrBackground || CliArgumentHelper.HasFlag(args, "--tray-only"),
            ShouldEnableRuntimeLogs = CliArgumentHelper.HasFlag(args, "--enable-logs"),
            Verify = CliArgumentHelper.HasFlag(args, "--verify"),
            VerifyModule = Val("--verify-module"),
            Doctor = CliArgumentHelper.HasFlag(args, "--doctor"),
            RequireBackend = CliArgumentHelper.HasFlag(args, "--require-backend"),
            JitBringup = CliArgumentHelper.HasFlag(args, "--jit-bringup"),
            PreloadMediaFlag = CliArgumentHelper.HasFlag(args, "--preload-media"),
            HostDemo = CliArgumentHelper.HasFlag(args, "--host-demo"),
            HdTexturePacks = CliArgumentHelper.HasFlag(args, "--hd-packs"),
            BackendId = normalizedBackend,
            ResolutionScale = ParseOptionalResolutionScale(Val("--scale")),
            AspectRatioMode = ParseStrictCodec<HostAspectRatioMode>(Val("--aspect-mode"), "--aspect-mode", HostAspectRatioModeCodec.TryParseStrict, "nativePar, fourByThree, squareInteger, nativeFit, horPlus16By9, or horPlus21By9"),
            SaveDirOverride = Val("--save-dir"),
            AudioDriverOverride = Val("--audio-driver"),
            MediaPathOverride = Val("--media-path"),
            ShutdownTimeoutMs = ParseInt(Val("--shutdown-timeout-ms"), "--shutdown-timeout-ms", 100, 30000),
            SuiteTab = Val("--suite-tab"),
            Profile = Val("--profile"),
            AutomationPort = ParseInt(Val("--automation-port"), "--automation-port", 0, 65535),
            AutomationToken = Val("--automation-token"),
            LogDirectoryOverride = Val("--log-dir"),
            ConfigPathOverride = Val("--config"),
            Frames = ParseInt(Val("--frames"), "--frames", 1, int.MaxValue),
            FixturePath = Val("--fixture"),
            DumpScreenshotPath = Val("--dump-screenshot"),
            DumpAudioPath = Val("--dump-audio"),
            PreloadMediaToRam = ParseSwitch(args, "--preload-media-to-ram", "--no-preload-media-to-ram"),
            FastBoot = ParseSwitch(args, "--fast-boot", "--no-fast-boot"),
            SafeModeReset = CliArgumentHelper.HasFlag(args, "--safe-mode-reset"),
            ExecutionTierMode = ParseOptionalExecutionTier(Val("--execution-tier")),
            DisplayMode = ParseStrictCodec<HostDisplayMode>(Val("--display-mode"), "--display-mode", HostDisplayModeCodec.TryParseStrict, "windowed, borderlessFullscreen, or exclusiveFullscreen"),
            MonitorIndex = ParseInt(Val("--monitor"), "--monitor", 0, int.MaxValue),
            TargetFps = ParseInt(Val("--target-fps"), "--target-fps", 0, int.MaxValue),
            BackgroundFpsCap = ParseInt(Val("--bg-fps-cap"), "--bg-fps-cap", 0, int.MaxValue),
            Vsync = ParseSwitch(args, "--vsync", "--no-vsync"),
            VideoFilterMode = ParseStrictCodec<HostVideoFilterMode>(Val("--video-filter"), "--video-filter", HostVideoFilterModeCodec.TryParseStrict, "nearest, bilinear, sharpBilinear, scanlines, crtLite, lcdGrid, xbrz, or ntscComposite"),
            TrueColor = ParseSwitch(args, "--true-color", "--no-true-color"),
            Dithering = ParseSwitch(args, "--dithering", "--no-dithering"),
            HudAnchorMode = ParseStrictCodec<HostHudAnchorMode>(Val("--hud-anchor"), "--hud-anchor", HostHudAnchorModeCodec.TryParseStrict, "safeFourByThree, screenEdges, or custom"),
            ScreenshotCaptureMode = ParseStrictCodec<HostScreenshotCaptureMode>(Val("--screenshot-mode"), "--screenshot-mode", HostScreenshotCaptureModeCodec.TryParseStrict, "cleanNative or filteredComposite"),
            FastForwardMultiplier = ParseDouble(Val("--ff-multiplier"), "--ff-multiplier", 2.0, 4.0),
            AudioOutputDevice = Val("--audio-device"),
            MasterVolume = ParseDouble(Val("--master-volume"), "--master-volume", 0.0, 1.0),
            AudioMuted = ParseSwitch(args, "--audio-muted", "--no-audio-muted"),
            MuteOnFocusLoss = ParseSwitch(args, "--mute-on-focus-loss", "--no-mute-on-focus-loss"),
            BgmVolume = ParseDouble(Val("--bgm-volume"), "--bgm-volume", 0.0, 1.0),
            SfxVolume = ParseDouble(Val("--sfx-volume"), "--sfx-volume", 0.0, 1.0),
            VoiceVolume = ParseDouble(Val("--voice-volume"), "--voice-volume", 0.0, 1.0),
            AudioLowPassHz = ParseInt(Val("--audio-lowpass-hz"), "--audio-lowpass-hz", 0, int.MaxValue),
            AudioHighPassHz = ParseInt(Val("--audio-highpass-hz"), "--audio-highpass-hz", 0, int.MaxValue),
            AudioStereoWidthPercent = ParseDouble(Val("--stereo-width"), "--stereo-width", 0.0, 200.0),
            AudioResamplingMode = ParseStrictCodec<HostAudioResamplingMode>(Val("--audio-resampling"), "--audio-resampling", HostAudioResamplingModeCodec.TryParseStrict, "linear or sincCubic"),
            FastForwardAudioMode = ParseStrictCodec<HostFastForwardAudioMode>(Val("--ff-audio-mode"), "--ff-audio-mode", HostFastForwardAudioModeCodec.TryParseStrict, "attenuatedLowPass, muted, or normal")
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
                if (i + 1 >= args.Length) { if (requiredIfPresent) throw new ArgumentException($"Missing value for CLI option '{optionName}'."); return null; }
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

    public static string? GetPositionalArg(string[] args, int index, IReadOnlySet<string> valueOptions)
    {
        if (args is null || args.Length == 0) return null;
        int current = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i] ?? string.Empty;
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (!a.Contains('=', StringComparison.Ordinal) && valueOptions.Contains(a) && i + 1 < args.Length) i++;
                continue;
            }
            if (a.StartsWith("-", StringComparison.Ordinal) || string.Equals(a, "/?", StringComparison.Ordinal) || string.Equals(a, "help", StringComparison.OrdinalIgnoreCase)) continue;
            if (current == index) return a;
            current++;
        }
        return null;
    }

    private static bool? ParseSwitch(string[] args, string positiveFlag, string negativeFlag)
    {
        bool? state = null;
        foreach (string raw in args)
        {
            if (string.Equals(raw, positiveFlag, StringComparison.OrdinalIgnoreCase)) state = true;
            else if (string.Equals(raw, negativeFlag, StringComparison.OrdinalIgnoreCase)) state = false;
        }
        return state;
    }

    private static string? ParseOptionalExecutionTier(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "staticaotonly" or "staticaot" => "staticAotOnly",
        "aotwithjitfallback" => "aotWithJitFallback",
        "aotwiththreadedir" => "aotWithThreadedIr",
        "jitbringup" => "jitBringup",
        "interpreteroracle" => "interpreterOracle",
        _ => throw new ArgumentException($"Unsupported --execution-tier '{raw}'; expected staticAotOnly, aotWithJitFallback, aotWithThreadedIr, jitBringup, or interpreterOracle.")
    };

    private static T? ParseStrictCodec<T>(string? raw, string optionName, TryParseCodecDelegate<T> tryParse, string expected) where T : struct
    {
        if (raw is null) return null;
        if (!tryParse(raw, out T value)) throw new ArgumentException($"Unsupported {optionName} '{raw}'; expected {expected}.");
        return value;
    }

    private static int? ParseOptionalResolutionScale(string? raw)
    {
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) || val is not (1 or 2 or 4 or 8))
            throw new ArgumentException($"Invalid --scale value '{raw}'; expected 1, 2, 4, or 8.");
        return val;
    }

    private static int? ParseInt(string? raw, string optionName, int min, int max)
    {
        if (raw is null) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val) || val < min || val > max)
            throw new ArgumentException(max == int.MaxValue ? $"Invalid {optionName} value '{raw}'; expected integer >= {min}." : $"Invalid {optionName} value '{raw}'; expected integer in {min}..{max}.");
        return val;
    }

    private static double? ParseDouble(string? raw, string optionName, double min, double max)
    {
        if (raw is null) return null;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double val) || !double.IsFinite(val) || val < min || val > max)
            throw new ArgumentException($"Invalid {optionName} value '{raw}'; expected finite number in {min}..{max}.");
        return val;
    }
}
