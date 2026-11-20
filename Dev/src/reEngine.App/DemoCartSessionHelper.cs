// @module: reEngine.App.DemoCartSessionHelper
// @purpose: Clean-room built-in demo/test cart callbacks (silent idle audio + 60ms soft chime on input) and HostSessionOptions builder.
// @depends-on: System, System.IO, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core, reEngine.Host.Contracts
// @exports: DemoCartSessionHelper, DemoHostCallbacks

using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App;

internal static class DemoCartSessionHelper
{
    public static HostSessionOptions BuildSessionOptions(
        RecompLaunchOptions opts, AppEnvironment appEnv, string? containmentRoot,
        ReEngineSettingsBundle bundle, IReEngineSettingsStore store,
        HostDrawerDescriptor descriptor, HostRenderBackendKind preferredKind, string verLabel)
    {
        ReEngineSettings core = bundle.Core; ReEngineVideoSettings video = bundle.Video; ReEngineAudioSettings audio = bundle.Audio;
        (HostDisplayMode displayMode, bool isFullscreen) = RecompLaunchFlagRouter.ResolveDisplayMode(opts, video, core);
        HostOverscanCrop overscan = opts.OverscanCrop ?? (video.OverscanCropPx is { } c
            ? new HostOverscanCrop(Math.Max(0, c.Top), Math.Max(0, c.Bottom), Math.Max(0, c.Left), Math.Max(0, c.Right)) : HostOverscanCrop.None);
        string executionTier = RecompLaunchFlagRouter.ResolveEffectiveExecutionTier(opts, manifest: null, core);
        bool isJitEnabled = !opts.ForceZeroRwx && (opts.JitBringup || core.IsJitEnabled ||
            (!string.Equals(executionTier, "staticAotOnly", StringComparison.OrdinalIgnoreCase) && !string.Equals(executionTier, "interpreterOracle", StringComparison.OrdinalIgnoreCase)));
        double ffMult = Math.Clamp(opts.FastForwardMultiplier ?? core.FastForwardMultiplier, 2.0, 4.0);
        string? mediaPath = !string.IsNullOrWhiteSpace(opts.MediaPathOverride)
            ? (containmentRoot is not null ? AppEnvironment.AssertPathContained(Path.GetFullPath(opts.MediaPathOverride.Trim()), containmentRoot) : Path.GetFullPath(opts.MediaPathOverride.Trim())) : null;
        string modId = opts.DemoCart ? "demo-cart" : "host-demo";

        var mixer = new HostAudioMixerSettings
        {
            AudioOutputDevice = !string.IsNullOrWhiteSpace(opts.AudioOutputDevice) ? opts.AudioOutputDevice : audio.AudioOutputDevice,
            MasterVolume = opts.MasterVolume ?? audio.MasterVolume, IsAudioMuted = opts.AudioMuted ?? audio.IsAudioMuted,
            BgmVolume = opts.BgmVolume ?? audio.BgmVolume, SfxVolume = opts.SfxVolume ?? audio.SfxVolume, VoiceVolume = opts.VoiceVolume ?? audio.VoiceVolume,
            ShouldMuteOnFocusLoss = opts.MuteOnFocusLoss ?? audio.ShouldMuteOnFocusLoss, AudioLowPassHz = opts.AudioLowPassHz ?? audio.AudioLowPassHz,
            AudioHighPassHz = opts.AudioHighPassHz ?? audio.AudioHighPassHz, AudioStereoWidthPercent = opts.AudioStereoWidthPercent ?? audio.AudioStereoWidthPercent,
            ResamplingMode = opts.AudioResamplingMode ?? HostAudioResamplingModeCodec.Parse(audio.AudioResamplingMode),
            FastForwardAudioMode = opts.FastForwardAudioMode ?? HostFastForwardAudioModeCodec.Parse(audio.FastForwardAudioMode)
        };
        var resilience = new HostExecutionResilienceOptions
        {
            DataDirectory = appEnv.DataDirectory, ModuleId = modId, IsJitEnabled = isJitEnabled, ExecutionTierMode = executionTier,
            ShouldPreloadMediaToRam = opts.PreloadMediaToRam ?? core.ShouldPreloadMediaToRam, PreloadMediaPath = mediaPath,
            IsFastBootEnabled = opts.FastBoot ?? core.IsFastBootEnabled, IsSafeModeActive = !opts.SafeModeReset && core.IsSafeModeActive,
            ShouldFastLoadData = core.ShouldFastLoadData, FastForwardMultiplier = ffMult
        };
        return new HostSessionOptions
        {
            WindowTitle = opts.DemoCart ? $"{verLabel} — Clean-Room Demo / Test Cart" : $"{verLabel} — Live Clean-Room Session",
            WindowWidth = 1280, WindowHeight = 720, ContentWidth = 320, ContentHeight = 240,
            TargetFps = opts.TargetFps ?? video.TargetFps, HeadlessFrameCount = opts.Frames, DumpFramePath = opts.DumpScreenshotPath,
            DumpAudioPath = opts.DumpAudioPath, AutomationPort = opts.AutomationPort, AutomationToken = opts.AutomationToken,
            AllowedSnapshotRoot = containmentRoot ?? appEnv.DataDirectory, RequireVisibleWindow = true,
            ShowMediaGateInitially = opts.PreloadMediaFlag && !opts.DemoCart, IsDemoCartMode = opts.DemoCart,
            OpenSettingsInitially = opts.OpenSettings, ShowFpsInitially = opts.ShowFps || video.IsFpsOverlayEnabled,
            UiTextScale = Math.Clamp(opts.UiTextScale ?? video.UiTextScale, 0.75, 2.0),
            FrameRateMode = opts.FrameRateMode ?? HostFrameRateModeCodec.Parse(video.FrameRateMode),
            IsCleanCaptureModeEnabled = video.IsCleanCaptureModeEnabled, ShouldEnableRuntimeLogs = opts.ShouldEnableRuntimeLogs || core.ShouldEnableRuntimeLogs,
            ControlsConfigPath = bundle.ControlsConfigPath, PreferredBackend = preferredKind, RequireBackend = opts.RequireBackend,
            IsFullscreen = isFullscreen, DisplayMode = displayMode, MonitorIndex = opts.MonitorIndex ?? video.MonitorIndex,
            BackgroundFpsCap = opts.BackgroundFpsCap ?? video.BackgroundFpsCap, IsVsyncEnabled = opts.Vsync ?? video.IsVsyncEnabled,
            FastForwardMultiplier = ffMult, HudAnchorMode = opts.HudAnchorMode ?? HostHudAnchorModeCodec.Parse(video.HudAnchorMode),
            VideoFilterMode = opts.VideoFilterMode ?? HostVideoFilterModeCodec.Parse(video.VideoFilterMode),
            IsTrueColor24BitEnabled = opts.TrueColor ?? video.IsTrueColor24BitEnabled, IsDitheringEnabled = opts.Dithering ?? video.IsDitheringEnabled,
            ScreenshotCaptureMode = opts.ScreenshotCaptureMode ?? HostScreenshotCaptureModeCodec.Parse(video.ScreenshotCaptureMode),
            ScreenshotsDirectory = Path.Combine(appEnv.DataDirectory, "screenshots"), ModuleId = modId,
            AudioDriverOverride = !string.IsNullOrWhiteSpace(opts.AudioDriverOverride) ? opts.AudioDriverOverride : audio.AudioDriver,
            AudioSettings = mixer, ExecutionResilience = resilience, HdPacksDirectory = Path.Combine(appEnv.DataDirectory, "hdpacks"),
            IsHdTexturePackEnabled = opts.HdTexturePacks || video.IsHdTexturePackEnabled,
            AspectRatioMode = opts.AspectRatioMode ?? HostAspectRatioModeCodec.Parse(video.AspectRatioMode),
            OverscanCropPx = overscan, IsFastForwardEnabled = opts.FastForward ?? video.IsFastForwardEnabled,
            DrawerDescriptor = descriptor, SafeModeCallback = active => { core.IsSafeModeActive = active; store.SaveCore(core); }
        };
    }

    public static HostRenderBackendKind ParseHostBackendKind(string? backendId) => backendId?.ToLowerInvariant() switch
    {
        HostRenderBackendIds.OpenGl => HostRenderBackendKind.OpenGl, HostRenderBackendIds.Vulkan => HostRenderBackendKind.Vulkan,
        HostRenderBackendIds.DirectX => HostRenderBackendKind.DirectX, "parallelcpu" => HostRenderBackendKind.ParallelCpu,
        HostRenderBackendIds.Metal => HostRenderBackendKind.Metal, _ => HostRenderBackendKind.Software
    };
}

internal sealed class DemoHostCallbacks : IHostSessionCallbacks
{
    private const int SampleRate = 44100, ChimeTotalFrames = (SampleRate * 60) / 1000;
    private HostGamepadButtons _prevButtons;
    private int _prevMouseButtons, _chimeRemainingFrames;
    private double _chimePhase, _chimeFreqHz = 523.25;

    public ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)
    {
        HostGamepadButtons newly = input.DigitalButtons & ~_prevButtons;
        int newlyMouse = input.MouseButtons & ~_prevMouseButtons;
        _prevButtons = input.DigitalButtons; _prevMouseButtons = input.MouseButtons;
        if (newly != HostGamepadButtons.None || newlyMouse != 0)
        {
            _chimeRemainingFrames = ChimeTotalFrames; _chimePhase = 0.0;
            _chimeFreqHz = (newly & (HostGamepadButtons.South | HostGamepadButtons.Start)) != 0 ? 659.25 : 523.25;
        }
        int shift = (int)(frameNumber % 256), pulse = _chimeRemainingFrames > 0 ? 36 : 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int idx = ((y * width) + x) * 4; bool chk = ((x / 16) + (y / 16)) % 2 == 0;
            rgbaFramebuffer[idx] = chk ? (byte)Math.Min(255, ((24 + shift) & 0x7F) + pulse) : (byte)18;
            rgbaFramebuffer[idx + 1] = chk ? (byte)Math.Min(255, 110 + pulse) : (byte)42;
            rgbaFramebuffer[idx + 2] = chk ? (byte)Math.Min(255, 168 + pulse) : (byte)84;
            rgbaFramebuffer[idx + 3] = 255;
        }
        return ValueTask.CompletedTask;
    }

    public int ReadAudioSamples(Span<short> interleavedStereoPcm)
    {
        int target = Math.Min(interleavedStereoPcm.Length, 1024);
        interleavedStereoPcm[..target].Clear();
        if (_chimeRemainingFrames <= 0 || target < 2) return target;
        int pairs = Math.Min(target / 2, _chimeRemainingFrames);
        double step = (2.0 * Math.PI * _chimeFreqHz) / SampleRate;
        for (int i = 0; i < pairs; i++)
        {
            double env = Math.Sin(Math.PI * Math.Clamp((double)(ChimeTotalFrames - _chimeRemainingFrames) / ChimeTotalFrames, 0.0, 1.0));
            short s = (short)Math.Clamp(Math.Sin(_chimePhase) * env * 0.14 * short.MaxValue, short.MinValue, short.MaxValue);
            interleavedStereoPcm[i * 2] = s; interleavedStereoPcm[(i * 2) + 1] = s;
            _chimePhase += step; _chimeRemainingFrames--;
        }
        return target;
    }
}
