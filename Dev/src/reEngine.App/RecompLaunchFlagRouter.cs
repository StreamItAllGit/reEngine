// @module: reEngine.App.RecompLaunchFlagRouter
// @purpose: Protocol-gated child CLI routing (v0.2.x fallback, v0.3.0+ base flags, >= 0.6.0 display/DSP flags, and >= 0.6.1 R08 parity flags).
// @depends-on: System, System.Collections.Generic, System.Globalization, System.Linq, reEngine.Core, reEngine.Host.Contracts
// @exports: RecompLaunchFlagRouter

using System.Globalization;
using reEngine.Core.Manifests;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App;

internal static class RecompLaunchFlagRouter
{
    private static readonly SemVer Track027Threshold = new(0, 2, 7);
    private static readonly SemVer Track060Threshold = new(0, 6, 0);
    private static readonly SemVer Track061Threshold = new(0, 6, 1);

    public static bool Supports030Routing(InstalledEngineCandidate engine)
    {
        if (engine is null) return false;
        if (engine.Version.CompareTo(SemVer.Track030Threshold) >= 0) return true;
        EngineManifestModel? model = EngineVersionResolver.TryReadEngineManifest(engine.EngineDirectory);
        if (model is null) return false;
        if (SemVer.TryParse(model.HostProtocolVersion, out SemVer proto) && proto.CompareTo(SemVer.Track030Threshold) >= 0) return true;
        if (SemVer.TryParse(model.MinSuiteVersion, out SemVer minSuite) && minSuite.CompareTo(SemVer.Track030Threshold) >= 0) return true;
        if (model.SupportedEngineTracks?.Any(IsTrack030OrHigher) == true || IsTrack030OrHigher(model.CompatibleEngineTrack)) return true;
        return SemVer.TryParse(model.EffectiveVersion, out SemVer declared) && declared.CompareTo(SemVer.Track030Threshold) >= 0;
    }

    public static bool Supports060Routing(InstalledEngineCandidate engine) => SupportsProtocolThreshold(engine, Track060Threshold);
    public static bool Supports061Routing(InstalledEngineCandidate engine) => SupportsProtocolThreshold(engine, Track061Threshold);

    private static bool SupportsProtocolThreshold(InstalledEngineCandidate? engine, SemVer threshold)
    {
        if (engine is null) return false;
        EngineManifestModel? model = EngineVersionResolver.TryReadEngineManifest(engine.EngineDirectory);
        if (model is not null)
        {
            if (SemVer.TryParse(model.HostProtocolVersion, out SemVer proto)) return proto.CompareTo(threshold) >= 0;
            if (SemVer.TryParse(model.MinSuiteVersion, out SemVer minSuite) && minSuite.CompareTo(threshold) >= 0) return true;
        }
        return engine.Version.CompareTo(threshold) >= 0;
    }

    public static void AppendRoutedFlags(
        List<string> args, RecompLaunchOptions opts, RecompPackageManifest manifest,
        InstalledEngineCandidate? selectedEngine, ReEngineSettingsBundle? bundle, Action<string, string>? log)
    {
        ReEngineSettings? core = bundle?.Core;
        ReEngineVideoSettings? video = bundle?.Video;
        ReEngineAudioSettings? audio = bundle?.Audio;
        bool supports030 = selectedEngine is null || Supports030Routing(selectedEngine);
        bool supports060 = selectedEngine is null || Supports060Routing(selectedEngine);
        bool supports061 = selectedEngine is null || Supports061Routing(selectedEngine);
        bool supports027 = selectedEngine is null || selectedEngine.Version.CompareTo(Track027Threshold) >= 0;

        string? rawBackend = opts.BackendId ?? manifest.BackendId ?? video?.BackendId ?? core?.BackendId;
        string? effectiveBackend = HostRenderBackendIds.TryNormalize(rawBackend, out string canonical) ? canonical : rawBackend;
        bool wantsJitBringup = !opts.ForceZeroRwx && !string.Equals(opts.ExecutionTierMode, "staticAotOnly", StringComparison.OrdinalIgnoreCase) &&
                               (opts.JitBringup || string.Equals(manifest.ExecutionTierMode, "jitBringup", StringComparison.OrdinalIgnoreCase) ||
                                (core is not null && ReEngineAppStatusHelper.GetJitBringup(core) && opts.ExecutionTierMode is null));

        if (OperatingSystem.IsWindows() && string.Equals(effectiveBackend, HostRenderBackendIds.Metal, StringComparison.OrdinalIgnoreCase) && opts.RequireBackend)
            throw new BackendUnavailableException(HostRenderBackendIds.Metal, "Apple Metal backend is not supported on Windows.");

        if (!supports030)
        {
            AppendPre030FallbackFlags(args, opts, selectedEngine!, effectiveBackend, wantsJitBringup, supports027, log);
            return;
        }

        if (!string.IsNullOrWhiteSpace(effectiveBackend)) AddPair(args, "--backend", effectiveBackend);
        if (opts.RequireBackend) args.Add("--require-backend");

        (HostDisplayMode effectiveDisplayMode, bool effectiveFullscreen) = ResolveDisplayMode(opts, video, core);
        if (effectiveFullscreen) args.Add("--fullscreen");

        string? audioDriver = !string.IsNullOrWhiteSpace(opts.AudioDriverOverride) ? opts.AudioDriverOverride : (audio?.AudioDriver ?? core?.AudioDriver);
        if (!string.IsNullOrWhiteSpace(audioDriver)) AddPair(args, "--audio-driver", audioDriver.Trim().ToLowerInvariant());

        string? settingsAspect = video?.AspectRatioMode ?? core?.AspectRatioMode;
        HostAspectRatioMode? effectiveAspect = opts.AspectRatioMode ?? (!string.IsNullOrWhiteSpace(settingsAspect) ? HostAspectRatioModeCodec.Parse(settingsAspect) : (HostAspectRatioMode?)null);
        if (effectiveAspect is HostAspectRatioMode aspectMode) AddPair(args, "--aspect-mode", HostAspectRatioModeCodec.ToCanonicalId(aspectMode));

        if (wantsJitBringup) args.Add("--jit-bringup");
        if (opts.AutomationPort is int port)
        {
            AddIntFlag(args, "--automation-port", port);
            if (!string.IsNullOrWhiteSpace(opts.AutomationToken)) AddPair(args, "--automation-token", opts.AutomationToken);
        }

        if (!supports060)
        {
            if (HasExplicitV060CliOverrides(opts) || HasExplicitV061CliOverrides(opts))
                log?.Invoke("WARN", $"Omitting 0.6.0+ flags for pre-0.6.0 child '{selectedEngine!.EngineId}' v{selectedEngine.Version}.");
            return;
        }

        AppendV060Flags(args, opts, manifest, bundle, effectiveDisplayMode);
        if (!supports061)
        {
            if (HasExplicitV061CliOverrides(opts))
                log?.Invoke("WARN", $"Omitting 0.6.1+ flags for pre-0.6.1 child '{selectedEngine!.EngineId}' v{selectedEngine.Version}.");
            return;
        }
        AppendV061Flags(args, opts, manifest, video);
    }

    public static (HostDisplayMode DisplayMode, bool IsFullscreen) ResolveDisplayMode(
        RecompLaunchOptions opts, ReEngineVideoSettings? video, ReEngineSettings? core)
    {
        if (opts.DisplayMode is HostDisplayMode explicitMode) return (explicitMode, explicitMode != HostDisplayMode.Windowed);
        if (opts.Window) return (HostDisplayMode.Windowed, false);
        bool storedExclusive = string.Equals(video?.DisplayMode, "exclusiveFullscreen", StringComparison.OrdinalIgnoreCase);
        bool isFullscreen = opts.Fullscreen || video?.IsFullscreen == true || core?.IsFullscreen == true;
        HostDisplayMode mode = isFullscreen
            ? (storedExclusive ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen)
            : HostDisplayMode.Windowed;
        return (mode, isFullscreen);
    }

    public static string ResolveEffectiveExecutionTier(RecompLaunchOptions opts, RecompPackageManifest? manifest, ReEngineSettings? core)
    {
        if (opts.ForceZeroRwx) return "aotWithThreadedIr";
        if (!string.IsNullOrWhiteSpace(opts.ExecutionTierMode)) return opts.ExecutionTierMode!;
        if (opts.JitBringup) return "jitBringup";
        if (!string.IsNullOrWhiteSpace(manifest?.ExecutionTierMode)) return manifest!.ExecutionTierMode!;
        if (core is null) return "staticAotOnly";
        if (!core.IsJitEnabled && !ReEngineAppStatusHelper.GetJitBringup(core)) return "staticAotOnly";
        return !string.IsNullOrWhiteSpace(core.ExecutionTierMode) && !string.Equals(core.ExecutionTierMode, "interpreterOracle", StringComparison.OrdinalIgnoreCase)
            ? core.ExecutionTierMode : "jitBringup";
    }

    private static void AppendV060Flags(
        List<string> args, RecompLaunchOptions opts, RecompPackageManifest manifest,
        ReEngineSettingsBundle? bundle, HostDisplayMode displayMode)
    {
        ReEngineSettings? core = bundle?.Core; ReEngineVideoSettings? video = bundle?.Video; ReEngineAudioSettings? audio = bundle?.Audio;
        if (opts.ForceZeroRwx || opts.ExecutionTierMode is not null || opts.JitBringup || !string.IsNullOrWhiteSpace(manifest.ExecutionTierMode) || core is not null)
            AddPair(args, "--execution-tier", ResolveEffectiveExecutionTier(opts, manifest, core));
        AddSwitchFlag(args, opts.PreloadMediaToRam, core?.ShouldPreloadMediaToRam, "--preload-media-to-ram", "--no-preload-media-to-ram", false);
        AddSwitchFlag(args, opts.FastBoot, core?.IsFastBootEnabled, "--fast-boot", "--no-fast-boot", false);
        if (opts.SafeModeReset) args.Add("--safe-mode-reset");
        if (opts.HdTexturePacks || video?.IsHdTexturePackEnabled == true) args.Add("--hd-packs");
        if (opts.FastForwardMultiplier.HasValue || core is not null)
            AddDoubleFlag(args, "--ff-multiplier", Math.Clamp(opts.FastForwardMultiplier ?? core?.FastForwardMultiplier ?? 2.0, 2.0, 4.0));
        if (opts.DisplayMode.HasValue || opts.Window || opts.Fullscreen || video is not null || core is not null)
            AddPair(args, "--display-mode", HostDisplayModeCodec.ToCanonicalId(displayMode));
        if (opts.MonitorIndex.HasValue || video is not null) AddIntFlag(args, "--monitor", Math.Max(0, opts.MonitorIndex ?? video?.MonitorIndex ?? 0));
        if (opts.TargetFps.HasValue || video is not null) AddIntFlag(args, "--target-fps", Math.Max(0, opts.TargetFps ?? video?.TargetFps ?? 60));
        if (opts.BackgroundFpsCap.HasValue || (video is not null && video.BackgroundFpsCap > 0))
            AddIntFlag(args, "--bg-fps-cap", Math.Max(0, opts.BackgroundFpsCap ?? video?.BackgroundFpsCap ?? 0));
        AddSwitchFlag(args, opts.Vsync, video?.IsVsyncEnabled, "--vsync", "--no-vsync", video is not null);
        HostVideoFilterMode? vf = opts.VideoFilterMode ?? (video is not null ? HostVideoFilterModeCodec.Parse(video.VideoFilterMode) : (HostVideoFilterMode?)null);
        if (vf is HostVideoFilterMode filter) AddPair(args, "--video-filter", HostVideoFilterModeCodec.ToCanonicalId(filter));
        AddSwitchFlag(args, opts.TrueColor, video?.IsTrueColor24BitEnabled, "--true-color", "--no-true-color", video is not null);
        AddSwitchFlag(args, opts.Dithering, video?.IsDitheringEnabled, "--dithering", "--no-dithering", false);
        HostHudAnchorMode? ha = opts.HudAnchorMode ?? (video is not null ? HostHudAnchorModeCodec.Parse(video.HudAnchorMode) : (HostHudAnchorMode?)null);
        if (ha is HostHudAnchorMode anchor) AddPair(args, "--hud-anchor", HostHudAnchorModeCodec.ToCanonicalId(anchor));
        HostScreenshotCaptureMode? sm = opts.ScreenshotCaptureMode ?? (video is not null ? HostScreenshotCaptureModeCodec.Parse(video.ScreenshotCaptureMode) : (HostScreenshotCaptureMode?)null);
        if (sm is HostScreenshotCaptureMode shotMode) AddPair(args, "--screenshot-mode", HostScreenshotCaptureModeCodec.ToCanonicalId(shotMode));
        string? audioDevice = !string.IsNullOrWhiteSpace(opts.AudioOutputDevice) ? opts.AudioOutputDevice : audio?.AudioOutputDevice;
        if (!string.IsNullOrWhiteSpace(audioDevice)) AddPair(args, "--audio-device", audioDevice.Trim());
        if (opts.MasterVolume.HasValue || audio is not null) AddDoubleFlag(args, "--master-volume", opts.MasterVolume ?? audio?.MasterVolume ?? 0.85);
        AddSwitchFlag(args, opts.AudioMuted, audio?.IsAudioMuted, "--audio-muted", "--no-audio-muted", false);
        AddSwitchFlag(args, opts.MuteOnFocusLoss, audio?.ShouldMuteOnFocusLoss, "--mute-on-focus-loss", "--no-mute-on-focus-loss", false);
        if (opts.BgmVolume.HasValue || audio is not null) AddDoubleFlag(args, "--bgm-volume", opts.BgmVolume ?? audio?.BgmVolume ?? 1.0);
        if (opts.SfxVolume.HasValue || audio is not null) AddDoubleFlag(args, "--sfx-volume", opts.SfxVolume ?? audio?.SfxVolume ?? 1.0);
        if (opts.VoiceVolume.HasValue || audio is not null) AddDoubleFlag(args, "--voice-volume", opts.VoiceVolume ?? audio?.VoiceVolume ?? 1.0);
        if (opts.AudioLowPassHz.HasValue || (audio is not null && audio.AudioLowPassHz > 0))
            AddIntFlag(args, "--audio-lowpass-hz", Math.Max(0, opts.AudioLowPassHz ?? audio?.AudioLowPassHz ?? 0));
        if (opts.AudioHighPassHz.HasValue || (audio is not null && audio.AudioHighPassHz > 0))
            AddIntFlag(args, "--audio-highpass-hz", Math.Max(0, opts.AudioHighPassHz ?? audio?.AudioHighPassHz ?? 0));
        if (opts.AudioStereoWidthPercent.HasValue || audio is not null) AddDoubleFlag(args, "--stereo-width", opts.AudioStereoWidthPercent ?? audio?.AudioStereoWidthPercent ?? 100.0);
        HostAudioResamplingMode? rs = opts.AudioResamplingMode ?? (audio is not null ? HostAudioResamplingModeCodec.Parse(audio.AudioResamplingMode) : (HostAudioResamplingMode?)null);
        if (rs is HostAudioResamplingMode resampling) AddPair(args, "--audio-resampling", HostAudioResamplingModeCodec.ToCanonicalId(resampling));
        HostFastForwardAudioMode? ffa = opts.FastForwardAudioMode ?? (audio is not null ? HostFastForwardAudioModeCodec.Parse(audio.FastForwardAudioMode) : (HostFastForwardAudioMode?)null);
        if (ffa is HostFastForwardAudioMode ffAudio) AddPair(args, "--ff-audio-mode", HostFastForwardAudioModeCodec.ToCanonicalId(ffAudio));
    }

    private static void AppendV061Flags(List<string> args, RecompLaunchOptions opts, RecompPackageManifest manifest, ReEngineVideoSettings? video)
    {
        if (opts.UiTextScale.HasValue || video is not null)
            AddDoubleFlag(args, "--ui-text-scale", Math.Clamp(opts.UiTextScale ?? video?.UiTextScale ?? 1.0, 0.75, 2.0));
        HostFrameRateMode? fpsMode = opts.FrameRateMode
            ?? (!string.IsNullOrWhiteSpace(manifest.DefaultFramerateMode) ? HostFrameRateModeCodec.Parse(manifest.DefaultFramerateMode) : (HostFrameRateMode?)null)
            ?? (video is not null ? HostFrameRateModeCodec.Parse(video.FrameRateMode) : (HostFrameRateMode?)null);
        if (fpsMode is HostFrameRateMode mode) AddPair(args, "--fps-mode", HostFrameRateModeCodec.ToCanonicalId(mode));

        HostOverscanCrop? overscan = opts.OverscanCrop ?? (video?.OverscanCropPx is { } vc && (vc.Top > 0 || vc.Bottom > 0 || vc.Left > 0 || vc.Right > 0)
            ? new HostOverscanCrop(Math.Max(0, vc.Top), Math.Max(0, vc.Bottom), Math.Max(0, vc.Left), Math.Max(0, vc.Right)) : null);
        if (overscan is HostOverscanCrop oc)
            AddPair(args, "--overscan", oc.Top == oc.Bottom && oc.Top == oc.Left && oc.Top == oc.Right ? oc.Top.ToString(CultureInfo.InvariantCulture) : $"{oc.Top},{oc.Bottom},{oc.Left},{oc.Right}");
        if (opts.OpenSettings) args.Add("--open-settings");
        if (opts.ShowFps || video?.IsFpsOverlayEnabled == true) args.Add("--show-fps");
        if (opts.ForceZeroRwx) args.Add("--force-zero-rwx");
        if (opts.FastForward == true || (opts.FastForward is null && video?.IsFastForwardEnabled == true)) args.Add("--fast-forward");
        if (opts.DemoCart) args.Add("--demo-cart");
    }

    private static void AppendPre030FallbackFlags(
        List<string> args, RecompLaunchOptions opts, InstalledEngineCandidate engine,
        string? effectiveBackend, bool wantsJitBringup, bool supports027, Action<string, string>? log)
    {
        if (!string.IsNullOrWhiteSpace(effectiveBackend))
        {
            if (supports027 && IsLegacyCompatibleBackend(effectiveBackend) && !opts.RequireBackend) AddPair(args, "--backend", effectiveBackend.ToLowerInvariant());
            else if (opts.RequireBackend) throw new BackendUnavailableException(effectiveBackend!, $"Selected engine '{engine.EngineId}' v{engine.Version} does not support required '--backend {effectiveBackend}' routing (requires >= 0.3.0).");
            else log?.Invoke("WARN", $"Omitting 0.3.0+ backend '{effectiveBackend}' for pre-0.3.0 child '{engine.EngineId}' v{engine.Version}.");
        }
        if (wantsJitBringup)
        {
            if (supports027 && !opts.RequireBackend && (string.IsNullOrWhiteSpace(effectiveBackend) || IsLegacyCompatibleBackend(effectiveBackend))) args.Add("--jit-bringup");
            else log?.Invoke("WARN", $"Omitting 0.3.0+ flag --jit-bringup for pre-0.3.0 child '{engine.EngineId}' v{engine.Version}.");
        }
        if (opts.AspectRatioMode is not null) log?.Invoke("WARN", $"Omitting 0.3.0+ flag --aspect-mode for pre-0.3.0 child '{engine.EngineId}' v{engine.Version}.");
        if (HasExplicitV060CliOverrides(opts) || HasExplicitV061CliOverrides(opts)) log?.Invoke("WARN", $"Omitting 0.6.0+ flags for pre-0.6.0 child '{engine.EngineId}' v{engine.Version}.");
    }

    private static void AddSwitchFlag(List<string> args, bool? cliVal, bool? settingVal, string posFlag, string negFlag, bool emitFalseDefault)
    {
        if (cliVal is bool explicitVal) { args.Add(explicitVal ? posFlag : negFlag); return; }
        if (settingVal == true) args.Add(posFlag);
        else if (emitFalseDefault && settingVal == false) args.Add(negFlag);
    }

    private static void AddPair(List<string> args, string flag, string value) { args.Add(flag); args.Add(value); }
    private static void AddIntFlag(List<string> args, string flag, int val) => AddPair(args, flag, val.ToString(CultureInfo.InvariantCulture));
    private static void AddDoubleFlag(List<string> args, string flag, double val) => AddPair(args, flag, val.ToString("0.###", CultureInfo.InvariantCulture));
    private static bool IsLegacyCompatibleBackend(string? backendId) =>
        string.Equals(backendId, HostRenderBackendIds.OpenGl, StringComparison.OrdinalIgnoreCase) || string.Equals(backendId, HostRenderBackendIds.Software, StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitV060CliOverrides(RecompLaunchOptions opts) =>
        opts.PreloadMediaToRam.HasValue || opts.FastBoot.HasValue || opts.SafeModeReset || opts.ExecutionTierMode is not null ||
        opts.DisplayMode.HasValue || opts.MonitorIndex.HasValue || opts.TargetFps.HasValue || opts.BackgroundFpsCap.HasValue ||
        opts.Vsync.HasValue || opts.VideoFilterMode.HasValue || opts.TrueColor.HasValue || opts.Dithering.HasValue ||
        opts.HudAnchorMode.HasValue || opts.ScreenshotCaptureMode.HasValue || opts.FastForwardMultiplier.HasValue ||
        !string.IsNullOrWhiteSpace(opts.AudioOutputDevice) || opts.MasterVolume.HasValue || opts.AudioMuted.HasValue ||
        opts.MuteOnFocusLoss.HasValue || opts.BgmVolume.HasValue || opts.SfxVolume.HasValue || opts.VoiceVolume.HasValue ||
        opts.AudioLowPassHz.HasValue || opts.AudioHighPassHz.HasValue || opts.AudioStereoWidthPercent.HasValue ||
        opts.AudioResamplingMode.HasValue || opts.FastForwardAudioMode.HasValue;

    private static bool HasExplicitV061CliOverrides(RecompLaunchOptions opts) =>
        opts.UiTextScale.HasValue || opts.FrameRateMode.HasValue || opts.OverscanCrop.HasValue ||
        opts.OpenSettings || opts.ShowFps || opts.ForceZeroRwx || opts.FastForward.HasValue || opts.DemoCart;

    private static bool IsTrack030OrHigher(string? track)
    {
        if (string.IsNullOrWhiteSpace(track)) return false;
        string norm = track.Trim().TrimStart('v', 'V');
        int suffixIdx = norm.IndexOfAny(['-', '+']);
        if (suffixIdx >= 0) norm = norm[..suffixIdx];
        string[] parts = norm.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) &&
               (major >= 1 || (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) && minor >= 3));
    }
}
