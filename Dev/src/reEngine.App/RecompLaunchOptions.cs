// @module: reEngine.App.RecompLaunchOptions
// @purpose: CLI option record, registered value flags matching LauncherArgsHelper, and delegated parser accessors for RecompLaunchCoordinator.
// @depends-on: System, System.Collections.Generic, Foundation.Core, reEngine.Core.Resolution, reEngine.Host.Contracts
// @exports: RecompLaunchOptions

using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;

namespace reEngine.App;

internal sealed record RecompLaunchOptions
{
    public static readonly HashSet<string> ValueOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "--open", "--module", "--verify-module", "--engines-dir", "--engine-dir", "--toolchain-dir", "--output-dir", "--stage",
        "--data-dir", "--config", "--log-dir", "--save-dir", "--feed", "--port", "--ui",
        "--backend", "--scale", "--aspect-mode", "--suite-tab", "--profile", "--automation-port", "--automation-token",
        "--frames", "--fixture", "--dump-screenshot", "--dump-audio",
        "--shutdown-timeout-ms", "--audio-driver", "--media-path", "--rom", "--disc",
        "--execution-tier", "--display-mode", "--monitor", "--target-fps", "--bg-fps-cap",
        "--video-filter", "--hud-anchor", "--screenshot-mode", "--audio-device",
        "--master-volume", "--bgm-volume", "--sfx-volume", "--voice-volume",
        "--audio-lowpass-hz", "--audio-highpass-hz", "--stereo-width", "--audio-resampling",
        "--ff-audio-mode", "--ff-multiplier", "--overscan", "--fps-mode", "--ui-text-scale"
    };

    public static readonly IReadOnlyDictionary<string, string> SupportedBackends = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["opengl"] = HostRenderBackendIds.OpenGl,
        ["gl"] = HostRenderBackendIds.OpenGl,
        ["vulkan"] = HostRenderBackendIds.Vulkan,
        ["vk"] = HostRenderBackendIds.Vulkan,
        ["directx"] = HostRenderBackendIds.DirectX,
        ["dx12"] = HostRenderBackendIds.DirectX,
        ["d3d12"] = HostRenderBackendIds.DirectX,
        ["directx12"] = HostRenderBackendIds.DirectX,
        ["parallelCpu"] = HostRenderBackendIds.ParallelCpu,
        ["software"] = HostRenderBackendIds.Software,
        ["metal"] = HostRenderBackendIds.Metal
    };

    public bool ShouldWaitForExit { get; init; }
    public bool Fullscreen { get; init; }
    public bool Headless { get; init; }
    public bool Json { get; init; }
    public bool RequireMedia { get; init; }
    public bool TrayOnly { get; init; }
    public bool ShouldEnableRuntimeLogs { get; init; }
    public bool Verify { get; init; }
    public string? VerifyModule { get; init; }
    public bool Doctor { get; init; }
    public bool RequireBackend { get; init; }
    public bool JitBringup { get; init; }
    public bool PreloadMediaFlag { get; init; }
    public bool HostDemo { get; init; }
    public bool HdTexturePacks { get; init; }
    public string? BackendId { get; init; }
    public int? ResolutionScale { get; init; }
    /// <summary>Optional strict-parsed aspect ratio mode requested via <c>--aspect-mode</c>.</summary>
    public HostAspectRatioMode? AspectRatioMode { get; init; }
    public string? SaveDirOverride { get; init; }
    public string? AudioDriverOverride { get; init; }
    public string? MediaPathOverride { get; init; }
    public int? ShutdownTimeoutMs { get; init; }
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

    // v0.6.0 Execution Resilience, Display/Filters, and Audio DSP CLI overrides
    public bool? PreloadMediaToRam { get; init; }
    public bool? FastBoot { get; init; }
    public bool SafeModeReset { get; init; }
    public string? ExecutionTierMode { get; init; }
    public HostDisplayMode? DisplayMode { get; init; }
    public int? MonitorIndex { get; init; }
    public int? TargetFps { get; init; }
    public int? BackgroundFpsCap { get; init; }
    public bool? Vsync { get; init; }
    public HostVideoFilterMode? VideoFilterMode { get; init; }
    public bool? TrueColor { get; init; }
    public bool? Dithering { get; init; }
    public HostHudAnchorMode? HudAnchorMode { get; init; }
    public HostScreenshotCaptureMode? ScreenshotCaptureMode { get; init; }
    public double? FastForwardMultiplier { get; init; }
    public string? AudioOutputDevice { get; init; }
    public double? MasterVolume { get; init; }
    public bool? AudioMuted { get; init; }
    public bool? MuteOnFocusLoss { get; init; }
    public double? BgmVolume { get; init; }
    public double? SfxVolume { get; init; }
    public double? VoiceVolume { get; init; }
    public int? AudioLowPassHz { get; init; }
    public int? AudioHighPassHz { get; init; }
    public double? AudioStereoWidthPercent { get; init; }
    public HostAudioResamplingMode? AudioResamplingMode { get; init; }
    public HostFastForwardAudioMode? FastForwardAudioMode { get; init; }

    // v0.6.1 Unified R08 §4.4 CLI Parity Flags
    public bool Window { get; init; }
    public bool DemoCart { get; init; }
    public bool OpenSettings { get; init; }
    public bool ShowFps { get; init; }
    public bool ForceZeroRwx { get; init; }
    public bool? FastForward { get; init; }
    public HostOverscanCrop? OverscanCrop { get; init; }
    public HostFrameRateMode? FrameRateMode { get; init; }
    public double? UiTextScale { get; init; }

    public static RecompLaunchOptions FromArgs(string[] args, AppEnvironment appEnv) =>
        RecompLaunchOptionParser.Parse(args, appEnv, ValueOptions);

    public static string? GetOptionValue(string[] args, string optionName, bool requiredIfPresent = false) =>
        RecompLaunchOptionParser.GetOptionValue(args, optionName, requiredIfPresent);

    public static string? GetPositionalArg(string[] args, int index) =>
        RecompLaunchOptionParser.GetPositionalArg(args, index, ValueOptions);
}
