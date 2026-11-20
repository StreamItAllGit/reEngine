// @module: reEngine.Host.Core.HostSessionRunner
// @purpose: Orchestrates SDL3 host frame loop, backend probing/fallback, native GPU/CPU presenters, audio pinning, hdpacks, overlays, and automation.
// @depends-on: System, System.Collections.Generic, System.IO, System.Security.Cryptography, System.Threading, System.Threading.Tasks, Foundation.Core, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Input, reEngine.Host.Rendering, reEngine.Host.Ui
// @exports: HostSessionRunner

using System.Security.Cryptography;
using Foundation.Core;
using reEngine.Core.Resolution;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;
using reEngine.Host.Input;
using reEngine.Host.Rendering;
using reEngine.Host.Ui;
#if REENGINE_ENABLE_AUTOMATION
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
    private readonly HostFramePacingController _pacingController = new();
    private readonly HostScreenshotService _screenshotService = new();
    private readonly HostVideoFilterPipeline _filterPipeline = new();
    private readonly HostDrawerControllerNav _controllerNav = new();
    private readonly HostAotCompileOverlayView _aotOverlay = new();
    private readonly HostRelativeMouseController _relativeMouse;
    private readonly object _commandSync = new();
    private readonly Queue<(int X, int Y, string? TargetId, bool IsRightClick)> _pendingClicks = new();
    private HostTouchOverlayView? _touchOverlay;
    private HostDisplayMode _activeDisplayMode = HostDisplayMode.Windowed;
    private HostRenderBackendKind _activeBackendKind;
    private Sdl3HostWindow? _activeWindow;
    private string? _controlsConfigPath;
    private string? _lastControlsJson;
    private bool _prevWindowFocused = true;
    private uint _prevCaptureGpMask;
    private bool _swallowNextEscapeHotkey;
    private volatile bool _stopRequested;

    /// <summary>Gets the active native or CPU graphics context for the running session.</summary>
    public IHostGraphicsContext? ActiveGraphicsContext { get; private set; }

    /// <summary>Gets the active HD texture override resolver when configured.</summary>
    public IHdTexturePackResolver? ActiveHdPackResolver { get; private set; }

    /// <summary>Initializes a new <see cref="HostSessionRunner"/> with the specified composite surface dimensions and descriptor.</summary>
    /// <param name="surfaceWidth">Composite surface width in pixels.</param>
    /// <param name="surfaceHeight">Composite surface height in pixels.</param>
    /// <param name="descriptor">Optional drawer descriptor configuring initial schema, scale, and execution tier label.</param>
    public HostSessionRunner(int surfaceWidth = 1280, int surfaceHeight = 720, HostDrawerDescriptor? descriptor = null)
    {
        HostDrawerDescriptor desc = descriptor ?? HostDrawerDescriptor.CreateDefault();
        _surface = new HostRgbaSurfaceBackend(surfaceWidth, surfaceHeight, desc.ResolutionScale, desc.BackendLabel);
        _relativeMouse = new HostRelativeMouseController(enabled => _activeWindow?.SetRelativeMouseMode(enabled));
        _drawer.ResolutionScale = desc.ResolutionScale;
        _drawer.SelectedSchema = HostInputSchemas.Parse(desc.ActiveSchema);
        _drawer.ExecutionTierLabel = desc.ExecutionTierLabel;
        _inputMapper.ActiveSchema = _drawer.SelectedSchema;
    }

    /// <summary>Runs the host presentation, audio, input, and overlay session loop until completion or cancellation.</summary>
    /// <param name="options">Session configuration options.</param>
    /// <param name="callbacks">Engine frame tick and audio callback receiver.</param>
    /// <param name="heartbeatSink">Optional periodic progress text callback.</param>
    /// <param name="ct">Cancellation token terminating the session loop.</param>
    /// <returns>Final session execution summary and captured output paths.</returns>
    public async Task<HostSessionResult> RunAsync(
        HostSessionOptions options,
        IHostSessionCallbacks callbacks,
        Action<string>? heartbeatSink = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(callbacks);
        HostSessionOptions effectiveOptions = options;

        using IHostAutomationBridge? automation = CreateAutomationBridge(options);
        if (options.AutomationPort.HasValue && automation is null)
            return new HostSessionResult(2, 0, false, false, _drawer.ActiveTab, null, null, "Automation disabled in Release builds (ExitCode=2).");

        if (options.InputProviders is not null)
            foreach (IHostInputProvider p in options.InputProviders) _inputMapper.RegisterProvider(p);

        using var defaultGamepad = options.InputProviders?.Any(p => p is Sdl3GamepadProvider) == true ? null : new Sdl3GamepadProvider();
        if (defaultGamepad is not null) _inputMapper.RegisterProvider(defaultGamepad);

        using HostTouchOverlayView? touchOverlay = options.EnableTouchOverlay ? new HostTouchOverlayView(_surface.SurfaceWidth, _surface.SurfaceHeight) : null;
        if (touchOverlay is not null) _inputMapper.RegisterProvider(touchOverlay);
        _touchOverlay = touchOverlay;

        HostBackendCapability probed = HostBackendProbe.Probe(effectiveOptions.PreferredBackend);
        HostRenderBackendKind activeKind = effectiveOptions.PreferredBackend;
        string activeBackendId = probed.BackendId;
        string? fallbackBanner = null;
        if (!probed.IsAvailable)
        {
            if (effectiveOptions.RequireBackend) return BuildUnavailableResult(probed.BackendId, probed.Diagnostic);
            activeKind = HostRenderBackendKind.ParallelCpu;
            activeBackendId = HostRenderBackendIds.ParallelCpu;
            fallbackBanner = $"Fallback to {activeBackendId}: {probed.BackendId} unavailable ({probed.Diagnostic})";
        }

        using var runtimeBridge = new HostSessionRuntimeBridge();
        effectiveOptions = runtimeBridge.PrepareExecution(effectiveOptions, ref fallbackBanner);
        activeKind = effectiveOptions.PreferredBackend;
        activeBackendId = HostBackendProbe.GetCanonicalBackendId(activeKind);

        using var window = new Sdl3HostWindow();
        bool useWindow = effectiveOptions.RequireVisibleWindow && !effectiveOptions.HeadlessFrameCount.HasValue;
        HostDisplayMode initialMode = Sdl3HostWindow.ResolveDisplayMode(effectiveOptions);
        _activeDisplayMode = initialMode;
        string? failReason = null;
        bool windowOpened = useWindow && window.TryOpenWithFallback(effectiveOptions, initialMode, ref activeKind, ref activeBackendId, ref fallbackBanner, out failReason);
        if (useWindow && !windowOpened && effectiveOptions.RequireBackend)
            return BuildUnavailableResult(activeBackendId, failReason ?? window.DiagnosticStatus);

        Sdl3HostWindow? activeWindow = windowOpened ? window : null;
        _activeWindow = activeWindow;
        ConfigureDrawerAndSurface(effectiveOptions, activeKind, fallbackBanner, window.GraphicsContext, activeWindow?.IsFullscreen ?? (initialMode != HostDisplayMode.Windowed));
        using var audioPump = new Sdl3AudioPump(effectiveOptions.AudioFormat, effectiveOptions.DumpAudioPath, openNativeStream: windowOpened, audioDriverOverride: effectiveOptions.AudioDriverOverride, initialSettings: effectiveOptions.AudioSettings);
        var soundTest = new HostSoundTestService(effectiveOptions.SoundTestProvider, effectiveOptions.AudioFormat.SampleRate);
        runtimeBridge.ConfigureDrawer(_drawer, effectiveOptions, audioPump, soundTest);
        string activeAudioDriver = audioPump.ActiveDriver ?? audioPump.SelectedDriver;
        using var pacer = new HighResFramePacer(effectiveOptions.TargetFps);
        _ = _pacingController.SyncPacer(pacer, effectiveOptions.TargetFps, effectiveOptions.BackgroundFpsCap, effectiveOptions.FastForwardMultiplier, _drawer.IsFastForwardEnabled, activeWindow?.IsWindowFocused ?? true, _drawer.IsVsyncEnabled);
        var heartbeat = new HeartbeatLogger("reEngine HostSession", heartbeatSink);
        if (automation is not null)
        {
            string token = !string.IsNullOrWhiteSpace(effectiveOptions.AutomationToken) ? effectiveOptions.AutomationToken!
                : (Environment.GetEnvironmentVariable("FOUNDATION_AUTOMATION_TOKEN") ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
            automation.Start(this, effectiveOptions.AutomationPort ?? 0, token, effectiveOptions.AllowedSnapshotRoot);
        }

        int contentW = Math.Max(64, effectiveOptions.ContentWidth), contentH = Math.Max(64, effectiveOptions.ContentHeight);
        byte[] contentRgba = new byte[checked(contentW * contentH * 4)];
        short[] audioBuffer = new short[Math.Max(512, (effectiveOptions.AudioFormat.SampleRate / 60) * effectiveOptions.AudioFormat.Channels * 2)];
        long frameNumber = 0;
        string? lastScreenshotPath = null;

        try
        {
            while (!ct.IsCancellationRequested && !_stopRequested)
            {
                if (effectiveOptions.HeadlessFrameCount is int limit && frameNumber >= limit) break;
                if (!HostFramePacingController.PumpLateLatchBeforeTick(
                    () => activeWindow is null || PollMappedWindowEvents(activeWindow),
                    () => PrepareLateLatchInput(activeWindow, automation, contentW, contentH, effectiveOptions),
                    () => _inputMapper.BuildFrame(),
                    out HostInputFrame frameInput)) break;

                CaptureGamepadInputIfPressed(frameInput.GamepadButtonsMask);
                ApplyNonSnapshotHotkeys(frameInput, effectiveOptions, activeWindow);
                _ = _drawer.TryHandleMouseWheel(frameInput.WheelDelta);
                IHostGraphicsContext? gfx = ActiveGraphicsContext;
                bool quit = HostSessionParityHelper.SyncLiveFrameState(effectiveOptions, _drawer, _mediaGate, _hud, _aotOverlay, _inputMapper, frameInput, _controllerNav, _relativeMouse, _surface, activeWindow, ref gfx, ref _activeBackendKind, ref _prevWindowFocused, _controlsConfigPath, ref _lastControlsJson);
                ActiveGraphicsContext = gfx;
                if (quit) { _stopRequested = true; break; }
                activeBackendId = HostBackendProbe.GetCanonicalBackendId(_activeBackendKind);
                runtimeBridge.SyncWindowVsyncIfChanged(_drawer, activeWindow);
                runtimeBridge.SyncSafeModeResetIfCleared(_drawer);
                _inputMapper.SetMenuOpen(_drawer.IsOpen || _mediaGate.IsVisible);
                _ = _pacingController.SyncPacer(pacer, HostSessionParityHelper.ResolveEffectiveBaseFps(_drawer), effectiveOptions.BackgroundFpsCap, _drawer.FastForwardMultiplier, _drawer.IsFastForwardEnabled || frameInput.IsFastForwardHeld, activeWindow?.IsWindowFocused ?? true, _drawer.IsVsyncEnabled);

                _surface.BeginFrame(contentW, contentH);
                await callbacks.OnFrameTickAsync(frameNumber, frameInput, contentRgba, contentW, contentH, ct);
                ReadOnlySpan<byte> filteredRgba = PresentGameAndOverlays(
                    effectiveOptions, contentW, contentH, contentRgba, frameInput, pacer, audioPump, runtimeBridge, activeWindow, frameNumber,
                    out int filteredW, out int filteredH);
                if (frameNumber == 0) runtimeBridge.ClearSentinelOnFirstFrame();
                HandlePostPresentActions(frameInput, effectiveOptions, contentRgba, contentW, contentH, filteredRgba, filteredW, filteredH, ref lastScreenshotPath);

                int readSamples = runtimeBridge.PumpFrameAudio(
                    audioPump, soundTest, _drawer, effectiveOptions, activeWindow?.IsWindowFocused ?? true,
                    _drawer.IsFastForwardEnabled || frameInput.IsFastForwardHeld, callbacks, audioBuffer);
                if (readSamples > 0) _ = audioPump.EnqueueInterleaved(audioBuffer.AsSpan(0, Math.Min(readSamples, audioBuffer.Length)));
                audioPump.PumpToDevice();
                automation?.SyncWidgets(GetOverlayWidgets());
                frameNumber++;
                heartbeat.ReportProgress(frameNumber, effectiveOptions.HeadlessFrameCount ?? 0, window.DiagnosticStatus);
                _ = _pacingController.WaitForNextFrame(pacer);
            }

            string? savedPng = string.IsNullOrWhiteSpace(effectiveOptions.DumpFramePath) ? lastScreenshotPath : Path.GetFullPath(effectiveOptions.DumpFramePath);
            if (!string.IsNullOrWhiteSpace(effectiveOptions.DumpFramePath)) _surface.SavePng(Path.GetFullPath(effectiveOptions.DumpFramePath));
            return new HostSessionResult(0, frameNumber, _drawer.IsOpen, _hud.IsVisible, _drawer.ActiveTab, savedPng, audioPump.CaptureFilePath, $"Completed {frameNumber} frame(s).", activeBackendId, activeAudioDriver, fallbackBanner);
        }
        finally
        {
            _ = _relativeMouse.ReleaseOnOverlayOpen();
            _activeWindow = null;
            if (defaultGamepad is not null) _inputMapper.UnregisterProvider(defaultGamepad);
            if (touchOverlay is not null) _inputMapper.UnregisterProvider(touchOverlay);
            _touchOverlay = null;
            ActiveGraphicsContext = null;
            heartbeat.Complete(frameNumber, "Host session closed.");
            _surface.Dispose();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HostWidgetBounds> GetOverlayWidgets()
    {
        var list = new List<HostWidgetBounds> { new("HostViewport", "Panel", "SDL3 Host Viewport", new System.Drawing.Rectangle(0, 0, _surface.SurfaceWidth, _surface.SurfaceHeight), true) };
        list.AddRange(_drawer.GetWidgets());
        list.AddRange(_mediaGate.GetWidgets());
        list.AddRange(_aotOverlay.GetWidgets());
        if (_touchOverlay is not null) list.AddRange(_touchOverlay.GetWidgets());
        if (_hud.TryBuildWidgetBounds() is { } hudWidget) list.Add(hudWidget);
        return list;
    }

    /// <inheritdoc />
    public void EnqueuePointerClick(int x, int y, string? targetId) => EnqueuePointerClick(x, y, targetId, false);

    /// <summary>Queues a primary or secondary pointer click on the host overlay surface.</summary>
    /// <param name="x">Surface-space horizontal coordinate.</param>
    /// <param name="y">Surface-space vertical coordinate.</param>
    /// <param name="targetId">Optional explicit control identifier.</param>
    /// <param name="isRightClick"><c>true</c> for secondary button click; otherwise primary click.</param>
    public void EnqueuePointerClick(int x, int y, string? targetId, bool isRightClick) { lock (_commandSync) _pendingClicks.Enqueue((x, y, targetId, isRightClick)); }

    /// <inheritdoc />
    public void EnqueueKeyPress(string key, IReadOnlyList<string>? modifiers = null) => _inputMapper.InjectSyntheticKey(key, modifiers);

    /// <inheritdoc />
    public bool TryCaptureFramebufferPng(out int width, out int height, out byte[] rgbaBytes) => _surface.TryCopyRgbaFrame(out width, out height, out rgbaBytes);

    private IHostAutomationBridge? CreateAutomationBridge(HostSessionOptions options)
    {
        IHostAutomationBridge? bridge = options.AutomationBridgeFactory?.Invoke();
#if REENGINE_ENABLE_AUTOMATION
        if (bridge is null && options.AutomationPort.HasValue)
            bridge = new SdlAutomationProxyControl(_surface.SurfaceWidth, _surface.SurfaceHeight);
#endif
        return bridge;
    }

    private HostSessionResult BuildUnavailableResult(string requestedBackendId, string reason) =>
        new(6, 0, false, false, _drawer.ActiveTab, null, null, $"Required render backend '{requestedBackendId}' unavailable: {reason}", null, null, null);

    private void ConfigureDrawerAndSurface(
        HostSessionOptions options, HostRenderBackendKind activeKind, string? fallbackBanner, IHostGraphicsContext? graphicsContext, bool isFullscreen)
    {
        _drawer.SelectedBackend = activeKind;
        _drawer.IsFullscreen = isFullscreen;
        _drawer.IsHdTexturePackEnabled = options.IsHdTexturePackEnabled;
        _drawer.FallbackBanner = fallbackBanner;
        _drawer.AspectRatioMode = options.AspectRatioMode;
        _drawer.OverscanCropPx = options.OverscanCropPx;
        _drawer.IsFastForwardEnabled = options.IsFastForwardEnabled;
        _drawer.EventCallbacks = options.SessionEventCallbacks;
        _drawer.ControlTierStore = _inputMapper.ControlTiers;
        _inputMapper.IsFastForwardToggled = options.IsFastForwardEnabled;
        _surface.UseParallelBandBlit = activeKind == HostRenderBackendKind.ParallelCpu;
        _surface.AspectRatioMode = options.AspectRatioMode;
        _surface.OverscanCropPx = options.OverscanCropPx;
        _surface.ContentPixelAspectRatio = options.ContentPixelAspectRatio;
        ActiveGraphicsContext = graphicsContext;
        _mediaGate.IsVisible = options.ShowMediaGateInitially;
        _mediaGate.ExpectedSha256 = string.IsNullOrWhiteSpace(options.ExpectedMediaSha256) ? "BYO-SHA256" : options.ExpectedMediaSha256!;
        ActiveHdPackResolver = !string.IsNullOrWhiteSpace(options.HdPacksDirectory) ? new HdPackResolver(options.HdPacksDirectory, options.IsHdTexturePackEnabled) : null;
        _activeBackendKind = activeKind;
        _prevWindowFocused = true;
        _controlsConfigPath = options.ControlsConfigPath;
        _lastControlsJson = HostSessionParityHelper.ConfigureInitialState(options, _inputMapper, _drawer, _mediaGate, _hud, _aotOverlay, _relativeMouse, activeKind);
    }

    private bool PollMappedWindowEvents(Sdl3HostWindow window) =>
        window.PollEvents(
            _inputMapper,
            (x, y) => { if (TryMapWindowPointToSurface(window, x, y, out int sx, out int sy)) EnqueuePointerClick(sx, sy, null, false); },
            (x, y) => { if (TryMapWindowPointToSurface(window, x, y, out int sx, out int sy)) EnqueuePointerClick(sx, sy, null, true); },
            path => HostSessionParityHelper.HandleFileDropped(path, _mediaGate, _drawer));

    private void PrepareLateLatchInput(Sdl3HostWindow? window, IHostAutomationBridge? automation, int contentW, int contentH, HostSessionOptions options)
    {
        automation?.DrainPendingCommands();
        HandleQueuedClicksOrCapture(window, options);
        _inputMapper.SetMenuOpen(_drawer.IsOpen || _mediaGate.IsVisible);
        UpdatePointerContentPosition(window, contentW, contentH, options);
    }

    private void HandleQueuedClicksOrCapture(Sdl3HostWindow? window, HostSessionOptions options)
    {
        if (_drawer.IsOpen && _drawer.ActiveTab == HostDrawerTabId.Keybinds && _drawer.KeybindsTab.IsCapturing
            && _inputMapper.TryConsumeFirstLatchedCode(out int capturedCode))
        {
            if (_drawer.TryCaptureInputCode(capturedCode, _inputMapper) && capturedCode == HostScancodes.Escape) _swallowNextEscapeHotkey = true;
            if (HostMultiBindRegistry.IsMouseOrWheelCode(capturedCode)) lock (_commandSync) _pendingClicks.Clear();
            return;
        }
        while (true)
        {
            (int X, int Y, string? TargetId, bool IsRightClick) click;
            lock (_commandSync) { if (_pendingClicks.Count == 0) break; click = _pendingClicks.Dequeue(); }
            bool consumed = _mediaGate.TryHandleClick(click.X, click.Y, click.TargetId)
                || _drawer.TryHandleClick(click.X, click.Y, click.TargetId, _inputMapper, click.IsRightClick);
            HostSessionParityHelper.HandlePointerClickOutcome(consumed, click.IsRightClick, _drawer, _mediaGate, window, _relativeMouse);
        }
        if (window is not null && window.IsFullscreen != _drawer.IsFullscreen)
        {
            _activeDisplayMode = _drawer.IsFullscreen ? (options.DisplayMode == HostDisplayMode.ExclusiveFullscreen ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen) : HostDisplayMode.Windowed;
            _ = window.TrySetDisplayMode(_activeDisplayMode, options.MonitorIndex);
        }
        _inputMapper.IsFastForwardToggled = _drawer.IsFastForwardEnabled;
    }

    private bool TryMapWindowPointToSurface(Sdl3HostWindow window, int winX, int winY, out int surfaceX, out int surfaceY)
    {
        HostViewportRect clientDest = HostViewportGeometry.ComputeDestinationRect(
            _surface.SurfaceWidth, _surface.SurfaceHeight, window.ClientWidth, window.ClientHeight, HostAspectRatioMode.NativeFit);
        if (!HostViewportGeometry.TryMapWindowToContentNormalized(winX, winY, clientDest, out float nx, out float ny))
        {
            surfaceX = surfaceY = -1;
            return false;
        }
        surfaceX = Math.Clamp((int)MathF.Round(nx * _surface.SurfaceWidth), 0, Math.Max(0, _surface.SurfaceWidth - 1));
        surfaceY = Math.Clamp((int)MathF.Round(ny * _surface.SurfaceHeight), 0, Math.Max(0, _surface.SurfaceHeight - 1));
        return true;
    }

    private void UpdatePointerContentPosition(Sdl3HostWindow? window, int contentW, int contentH, HostSessionOptions options)
    {
        if (window is null || !window.HasMousePosition ||
            !TryMapWindowPointToSurface(window, (int)MathF.Round(window.LastMouseX), (int)MathF.Round(window.LastMouseY), out int sx, out int sy))
        {
            _inputMapper.SetPointerContentPosition(-1f, -1f, false);
            return;
        }
        (_, HostViewportRect contentDest) = ComputeContentRects(contentW, contentH, options);
        bool inside = HostViewportGeometry.TryMapWindowToContentNormalized(sx, sy, contentDest, out float cnx, out float cny);
        _inputMapper.SetPointerContentPosition(cnx, cny, inside);
    }

    private (HostViewportRect SrcCrop, HostViewportRect ContentDest) ComputeContentRects(int contentW, int contentH, HostSessionOptions options)
    {
        HostViewportRect srcCrop = HostViewportGeometry.ComputeSourceCropRect(contentW, contentH, _drawer.OverscanCropPx);
        HostViewportRect contentDest = HostViewportGeometry.ComputeDestinationRect(
            srcCrop.Width, srcCrop.Height, _surface.SurfaceWidth, _surface.SurfaceHeight, _drawer.AspectRatioMode, options.ContentPixelAspectRatio);
        return (srcCrop, contentDest);
    }

    private void CaptureGamepadInputIfPressed(uint gamepadMask)
    {
        uint newlyPressed = gamepadMask & ~_prevCaptureGpMask;
        _prevCaptureGpMask = gamepadMask;
        if (!_drawer.IsOpen || _drawer.ActiveTab != HostDrawerTabId.Keybinds || !_drawer.KeybindsTab.IsCapturing || newlyPressed == 0) return;
        int bindingCode = HostBindingCodes.GamepadMin + System.Numerics.BitOperations.TrailingZeroCount(newlyPressed);
        if (bindingCode <= HostBindingCodes.GamepadMax) _ = _drawer.TryCaptureInputCode(bindingCode, _inputMapper);
    }

    private void ApplyNonSnapshotHotkeys(HostInputFrame input, HostSessionOptions options, Sdl3HostWindow? window)
    {
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.ToggleDrawer) != 0)
        {
            _drawer.IsOpen = !_drawer.IsOpen;
            if (_drawer.IsOpen) _ = _relativeMouse.ReleaseOnOverlayOpen();
        }
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.ToggleHud) != 0) _drawer.IsFpsOverlayVisible = !_drawer.IsFpsOverlayVisible;
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.ToggleFullscreen) != 0)
        {
            _drawer.IsFullscreen = !_drawer.IsFullscreen;
            _activeDisplayMode = _drawer.IsFullscreen
                ? (options.DisplayMode == HostDisplayMode.ExclusiveFullscreen ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen)
                : HostDisplayMode.Windowed;
            _ = window?.TrySetDisplayMode(_activeDisplayMode, options.MonitorIndex);
            options.SessionEventCallbacks?.OnFullscreenChanged(_drawer.IsFullscreen);
            options.SessionEventCallbacks?.OnDisplayModeChanged(_activeDisplayMode);
        }
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.FastForward) != 0)
        {
            _drawer.IsFastForwardEnabled = !_drawer.IsFastForwardEnabled;
            _inputMapper.IsFastForwardToggled = _drawer.IsFastForwardEnabled;
            options.SessionEventCallbacks?.OnFastForwardChanged(_drawer.IsFastForwardEnabled);
        }
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.QuickLoad) != 0) options.SessionEventCallbacks?.OnQuickLoadRequested();
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.CutsceneSkip) != 0) options.SessionEventCallbacks?.OnCutsceneSkipRequested();
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.Escape) == 0) return;
        HostSessionParityHelper.ApplyNonQuittingEscape(_drawer, _inputMapper, _relativeMouse, ref _swallowNextEscapeHotkey);
    }

    internal static void DispatchQuickSave(IHostGraphicsContext? graphicsContext, IHostSessionEventCallbacks? callbacks)
    {
        graphicsContext?.FlushAndSyncForSnapshot();
        callbacks?.OnQuickSaveRequested();
    }

    private void HandlePostPresentActions(
        HostInputFrame input, HostSessionOptions options,
        byte[] cleanRgba, int cleanW, int cleanH,
        ReadOnlySpan<byte> filteredRgba, int filteredW, int filteredH,
        ref string? lastScreenshotPath)
    {
        if ((input.HotkeyFlags & (uint)HostHotkeyFlags.QuickSave) != 0) DispatchQuickSave(ActiveGraphicsContext, options.SessionEventCallbacks);
        if ((input.HotkeyFlags & (uint)(HostHotkeyFlags.TakeScreenshot | HostHotkeyFlags.DumpFrame)) == 0) return;

        HostScreenshotResult shot = _screenshotService.Capture(
            _drawer.ScreenshotCaptureMode, cleanRgba, cleanW, cleanH, filteredRgba, filteredW, filteredH,
            options.ScreenshotsDirectory, options.ExecutionResilience?.DataDirectory, options.ModuleId,
            options.DumpFramePath, options.AllowedSnapshotRoot);
        if (shot.Succeeded) lastScreenshotPath = shot.FilePath;
        options.SessionEventCallbacks?.OnScreenshotCaptured(shot);
    }

    private ReadOnlySpan<byte> PresentGameAndOverlays(
        HostSessionOptions options, int contentW, int contentH, byte[] contentRgba,
        HostInputFrame frameInput, HighResFramePacer pacer, Sdl3AudioPump audioPump, HostSessionRuntimeBridge runtimeBridge, Sdl3HostWindow? window, long frameNumber,
        out int filteredW, out int filteredH)
    {
        _surface.ScaleFactor = _drawer.ResolutionScale;
        _surface.AspectRatioMode = _drawer.AspectRatioMode;
        _surface.OverscanCropPx = _drawer.OverscanCropPx;
        _surface.ContentPixelAspectRatio = options.ContentPixelAspectRatio;
        (HostViewportRect srcCrop, HostViewportRect contentDest) = ComputeContentRects(contentW, contentH, options);

        (int targetW, int targetH) = HostVideoFilterPipeline.ResolveTargetSize(
            _drawer.VideoFilterMode, _drawer.IsTrueColor24BitEnabled, _drawer.IsDitheringEnabled,
            _drawer.ResolutionScale, contentW, contentH, contentDest);
        ReadOnlySpan<byte> filteredRgba = _filterPipeline.Process(
            contentRgba, contentW, contentH, targetW, targetH, _drawer.VideoFilterMode,
            _drawer.IsTrueColor24BitEnabled, _drawer.IsDitheringEnabled, (int)frameNumber,
            out filteredW, out filteredH);
        HostViewportRect filteredCrop = HostVideoFilterPipeline.ScaleSourceCrop(srcCrop, contentW, contentH, filteredW, filteredH);
        _surface.PresentRgba(filteredW, filteredH, filteredRgba, contentDest, filteredCrop, publishSnapshot: false);

        int connectedPads = 0;
        foreach (HostGamepadPortSnapshot port in frameInput.PadPorts) if (port.IsConnected) connectedPads++;

        _touchOverlay?.Render(_surface.Canvas);
        if (_hud.IsVisible)
            _hud.Render(_surface.Canvas, HostSessionParityHelper.EnrichHudTelemetry(runtimeBridge.BuildHudTelemetry(pacer, audioPump, _drawer, _activeDisplayMode, connectedPads), _drawer));
        if (_drawer.IsOpen) _drawer.Render(_surface.Canvas, options.DrawerDescriptor, _inputMapper.Bindings);
        if (_mediaGate.IsVisible) _mediaGate.Render(_surface.Canvas, options.DrawerDescriptor);
        if (_aotOverlay.State.IsCompiling) _aotOverlay.Render(_surface.Canvas, options.DrawerDescriptor);
        _surface.PublishSnapshot();
        window?.PresentRgba(_surface.SurfaceWidth, _surface.SurfaceHeight, _surface.Canvas.Buffer, HostAspectRatioMode.NativeFit);
        return filteredRgba;
    }
}
