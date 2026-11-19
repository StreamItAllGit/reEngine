// @module: reEngine.Host.Core.HostSessionRunner
// @purpose: Orchestrates SDL3 host frame loop, backend probing/fallback, native GPU/CPU presenters, audio pinning, hdpacks, overlays, and automation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Input, reEngine.Host.Rendering, reEngine.Host.Ui
// @exports: HostSessionRunner

using System.Security.Cryptography;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Rendering;
using reEngine.Host.Ui;
#if REENGINE_ENABLE_AUTOMATION
using Foundation.Automation;
using reEngine.Host.Automation;
#endif

namespace reEngine.Host.Core;

/// <summary>
/// Clean-room host runtime orchestrating video, audio, input, overlays, and opt-in loopback test automation.
/// </summary>
public sealed class HostSessionRunner : IHostAutomationTarget
{
    private readonly HostRgbaSurfaceBackend _surface;
    private readonly HostInputMapper _inputMapper = new();
    private readonly F1SettingsDrawerView _drawer = new();
    private readonly F3HudOverlayView _hud = new();
    private readonly FirstRunMediaGateView _mediaGate = new();
    private readonly object _commandSync = new();
    private readonly Queue<(int X, int Y, string? TargetId)> _pendingClicks = new();
    private volatile bool _stopRequested;

    public IHostGraphicsContext? ActiveGraphicsContext { get; private set; }
    public IHdTexturePackResolver? ActiveHdPackResolver { get; private set; }

    public HostSessionRunner(int surfaceWidth = 1280, int surfaceHeight = 720, HostDrawerDescriptor? descriptor = null)
    {
        HostDrawerDescriptor desc = descriptor ?? HostDrawerDescriptor.CreateDefault();
        _surface = new HostRgbaSurfaceBackend(surfaceWidth, surfaceHeight, desc.ResolutionScale, desc.BackendLabel);
        _drawer.ResolutionScale = desc.ResolutionScale;
        _drawer.SelectedSchema = HostInputSchemas.Parse(desc.ActiveSchema);
        _drawer.ExecutionTierLabel = desc.ExecutionTierLabel;
        _inputMapper.ActiveSchema = _drawer.SelectedSchema;
    }

    public async Task<HostSessionResult> RunAsync(
        HostSessionOptions options,
        IHostSessionCallbacks callbacks,
        Action<string>? heartbeatSink = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callbacks);

#if !REENGINE_ENABLE_AUTOMATION
        if (options.AutomationPort.HasValue)
            return new HostSessionResult(2, 0, false, false, _drawer.ActiveTab, null, null, "Automation disabled in Release builds (ExitCode=2).");
#endif

        if (options.InputProviders is not null)
            foreach (IHostInputProvider p in options.InputProviders) _inputMapper.RegisterProvider(p);

        HostBackendCapability probed = HostBackendProbe.Probe(options.PreferredBackend);
        HostRenderBackendKind activeKind = options.PreferredBackend;
        string activeBackendId = probed.BackendId;
        string? fallbackBanner = null;
        if (!probed.IsAvailable)
        {
            if (options.RequireBackend) return BuildUnavailableResult(probed.BackendId, probed.Diagnostic);
            activeKind = HostRenderBackendKind.ParallelCpu;
            activeBackendId = HostRenderBackendIds.ParallelCpu;
            fallbackBanner = $"Fallback to {activeBackendId}: {probed.BackendId} unavailable ({probed.Diagnostic})";
        }

        using var window = new Sdl3HostWindow();
        bool useWindow = options.RequireVisibleWindow && !options.HeadlessFrameCount.HasValue;
        bool windowOpened = false;
        if (useWindow)
        {
            windowOpened = TryOpenWithFallback(window, options, ref activeKind, ref activeBackendId, ref fallbackBanner, out string? failReason);
            if (!windowOpened && options.RequireBackend) return BuildUnavailableResult(activeBackendId, failReason ?? window.DiagnosticStatus);
        }

        ConfigureDrawerAndSurface(options, activeKind, fallbackBanner, window.GraphicsContext);
        using var audioPump = new Sdl3AudioPump(options.AudioFormat, options.DumpAudioPath, openNativeStream: windowOpened, audioDriverOverride: options.AudioDriverOverride);
        string activeAudioDriver = audioPump.ActiveDriver ?? audioPump.SelectedDriver;
        using var pacer = new HighResFramePacer(options.TargetFps);
        var heartbeat = new HeartbeatLogger("reEngine HostSession", heartbeatSink);
#if REENGINE_ENABLE_AUTOMATION
        using SdlAutomationProxyControl? automation = TryStartAutomation(options);
#endif
        int contentW = Math.Max(64, options.ContentWidth), contentH = Math.Max(64, options.ContentHeight);
        byte[] contentRgba = new byte[checked(contentW * contentH * 4)];
        short[] audioBuffer = new short[Math.Max(512, (options.AudioFormat.SampleRate / 60) * options.AudioFormat.Channels * 2)];
        long frameNumber = 0;

        try
        {
            while (!ct.IsCancellationRequested && !_stopRequested)
            {
                if (options.HeadlessFrameCount is int limit && frameNumber >= limit) break;
                if (windowOpened && !window.PollEvents(_inputMapper, (x, y) => EnqueuePointerClick(x, y, null))) break;
#if REENGINE_ENABLE_AUTOMATION
                automation?.DrainPendingCommands();
#endif
                ProcessQueuedClicks();
                HostInputFrame frameInput = _inputMapper.BuildFrame();
                ApplyHotkeys(frameInput);

                _surface.BeginFrame(contentW, contentH);
                await callbacks.OnFrameTickAsync(frameNumber, frameInput, contentRgba, contentW, contentH, ct);
                _surface.ScaleFactor = _drawer.ResolutionScale;
                _surface.PresentRgba(contentW, contentH, contentRgba);
                RenderOverlays(options, pacer, audioPump);
                _surface.PublishSnapshot();
                if (windowOpened) window.PresentRgba(_surface.SurfaceWidth, _surface.SurfaceHeight, _surface.Canvas.Buffer);

                int readSamples = callbacks.ReadAudioSamples(audioBuffer);
                if (readSamples > 0) _ = audioPump.EnqueueInterleaved(audioBuffer.AsSpan(0, Math.Min(readSamples, audioBuffer.Length)));
                audioPump.PumpToDevice();
#if REENGINE_ENABLE_AUTOMATION
                automation?.SyncWidgets(GetOverlayWidgets());
#endif
                frameNumber++;
                heartbeat.ReportProgress(frameNumber, options.HeadlessFrameCount ?? 0, window.DiagnosticStatus);
                pacer.WaitForNextFrame();
            }

            string? savedPng = null;
            if (!string.IsNullOrWhiteSpace(options.DumpFramePath))
            {
                savedPng = Path.GetFullPath(options.DumpFramePath);
                _surface.SavePng(savedPng);
            }
            return new HostSessionResult(0, frameNumber, _drawer.IsOpen, _hud.IsVisible, _drawer.ActiveTab, savedPng, audioPump.CaptureFilePath, $"Completed {frameNumber} frame(s).", activeBackendId, activeAudioDriver, fallbackBanner);
        }
        finally
        {
            ActiveGraphicsContext = null;
            heartbeat.Complete(frameNumber, "Host session closed.");
            _surface.Dispose();
        }
    }

    public IReadOnlyList<HostWidgetBounds> GetOverlayWidgets()
    {
        var list = new List<HostWidgetBounds> { new("HostViewport", "Panel", "SDL3 Host Viewport", new System.Drawing.Rectangle(0, 0, _surface.SurfaceWidth, _surface.SurfaceHeight), true) };
        list.AddRange(_drawer.GetWidgets());
        list.AddRange(_mediaGate.GetWidgets());
        if (_hud.TryBuildWidgetBounds() is { } hudWidget) list.Add(hudWidget);
        return list;
    }

    public void EnqueuePointerClick(int x, int y, string? targetId) { lock (_commandSync) _pendingClicks.Enqueue((x, y, targetId)); }
    public void EnqueueKeyPress(string key, IReadOnlyList<string>? modifiers = null) => _inputMapper.InjectSyntheticKey(key);
    public bool TryCaptureFramebufferPng(out int width, out int height, out byte[] rgbaBytes) => _surface.TryCopyRgbaFrame(out width, out height, out rgbaBytes);

    private HostSessionResult BuildUnavailableResult(string requestedBackendId, string reason) =>
        new(6, 0, false, false, _drawer.ActiveTab, null, null, $"Required render backend '{requestedBackendId}' unavailable: {reason}", null, null, null);

    private static bool TryOpenWithFallback(
        Sdl3HostWindow window, HostSessionOptions options, ref HostRenderBackendKind activeKind, ref string activeBackendId, ref string? fallbackBanner, out string? failureDiag)
    {
        if (window.TryOpen(options.WindowTitle, options.WindowWidth, options.WindowHeight, visible: true, activeKind, options.IsFullscreen, out string diag))
        {
            failureDiag = null;
            return true;
        }
        failureDiag = diag;
        if (options.RequireBackend) return false;

        foreach (HostRenderBackendKind fbKind in new[] { HostRenderBackendKind.ParallelCpu, HostRenderBackendKind.Software })
        {
            if (activeKind == fbKind) continue;
            if (!window.TryOpen(options.WindowTitle, options.WindowWidth, options.WindowHeight, visible: true, fbKind, options.IsFullscreen, out _)) continue;
            activeKind = fbKind;
            activeBackendId = HostBackendProbe.GetCanonicalBackendId(fbKind);
            fallbackBanner ??= $"Fallback to {activeBackendId}: init failed ({diag})";
            return true;
        }
        return false;
    }

    private void ConfigureDrawerAndSurface(HostSessionOptions options, HostRenderBackendKind activeKind, string? fallbackBanner, IHostGraphicsContext? graphicsContext)
    {
        _drawer.SelectedBackend = activeKind;
        _drawer.IsFullscreen = options.IsFullscreen;
        _drawer.IsHdTexturePackEnabled = options.IsHdTexturePackEnabled;
        _drawer.FallbackBanner = fallbackBanner;
        _surface.UseParallelBandBlit = activeKind == HostRenderBackendKind.ParallelCpu;
        ActiveGraphicsContext = graphicsContext;
        _mediaGate.IsVisible = options.ShowMediaGateInitially;
        _mediaGate.ExpectedSha256 = string.IsNullOrWhiteSpace(options.ExpectedMediaSha256) ? "BYO-SHA256" : options.ExpectedMediaSha256!;
        ActiveHdPackResolver = !string.IsNullOrWhiteSpace(options.HdPacksDirectory) ? new HdPackResolver(options.HdPacksDirectory, options.IsHdTexturePackEnabled) : null;
    }

    private void ProcessQueuedClicks()
    {
        while (true)
        {
            (int X, int Y, string? TargetId) click;
            lock (_commandSync)
            {
                if (_pendingClicks.Count == 0) break;
                click = _pendingClicks.Dequeue();
            }
            if (_mediaGate.TryHandleClick(click.X, click.Y, click.TargetId)) continue;
            _ = _drawer.TryHandleClick(click.X, click.Y, click.TargetId, _inputMapper);
        }
    }

    private void ApplyHotkeys(HostInputFrame input)
    {
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.ToggleDrawer) != 0) _drawer.IsOpen = !_drawer.IsOpen;
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.ToggleHud) != 0) _hud.IsVisible = !_hud.IsVisible;
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.Escape) != 0)
        {
            if (_drawer.IsOpen) _drawer.IsOpen = false;
            else _stopRequested = true;
        }
    }

    private void RenderOverlays(HostSessionOptions options, HighResFramePacer pacer, Sdl3AudioPump audioPump)
    {
        if (_drawer.IsOpen) _drawer.Render(_surface.Canvas, options.DrawerDescriptor);
        if (_mediaGate.IsVisible) _mediaGate.Render(_surface.Canvas, options.DrawerDescriptor);
        if (_hud.IsVisible)
            _hud.Render(_surface.Canvas, new F3HudTelemetry(pacer.CurrentFps, pacer.AverageFrameMs, _drawer.SelectedBackend.ToString(), _drawer.ResolutionScale, _drawer.ExecutionTierLabel, audioPump.FillRatio));
    }

#if REENGINE_ENABLE_AUTOMATION
    private SdlAutomationProxyControl? TryStartAutomation(HostSessionOptions options)
    {
        if (!options.AutomationPort.HasValue) return null;
        string token = !string.IsNullOrWhiteSpace(options.AutomationToken)
            ? options.AutomationToken!
            : (Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        var proxy = new SdlAutomationProxyControl(this, new AutomationServerOptions(options.AutomationPort.Value, token, "reengine-host", options.AllowedSnapshotRoot), _surface.SurfaceWidth, _surface.SurfaceHeight);
        proxy.Start();
        return proxy;
    }
#endif
}
