// @module: reEngine.Host.Contracts.HostContracts
// @purpose: Console-agnostic SDL3 host contracts, non-ref HostInputFrame, 5-tab F1 drawer descriptor, and automation hook interface.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Threading, System.Threading.Tasks, Foundation.Retro
// @exports: HostGamepadButtons, HostInputFrame, HostRenderBackendKind, HostAudioFormat, HostDrawerTabId, HostDrawerRowDescriptor, HostDrawerDescriptor, HostWidgetBounds, HostSessionOptions, HostSessionResult, IHostRenderBackend, IHostSessionCallbacks, IHostAutomationTarget

using System.Drawing;
using Foundation.Retro;

namespace reEngine.Host.Contracts;

/// <summary>
/// Platform-neutral controller button bitmask populated per frame.
/// </summary>
[Flags]
public enum HostGamepadButtons : uint
{
    None = 0,
    DPadUp = 1u << 0,
    DPadDown = 1u << 1,
    DPadLeft = 1u << 2,
    DPadRight = 1u << 3,
    South = 1u << 4,
    East = 1u << 5,
    West = 1u << 6,
    North = 1u << 7,
    LeftShoulder = 1u << 8,
    RightShoulder = 1u << 9,
    LeftTriggerClick = 1u << 10,
    RightTriggerClick = 1u << 11,
    Start = 1u << 12,
    Select = 1u << 13,
    LeftStickClick = 1u << 14,
    RightStickClick = 1u << 15
}

/// <summary>
/// Supported clean-room render backend kinds exposed by <c>reEngine.Host</c>.
/// </summary>
public enum HostRenderBackendKind
{
    OpenGl = 0,
    Vulkan = 1,
    ParallelCpu = 2,
    Software = 3,
    DirectX = 4,
    Metal = 5
}

/// <summary>
/// Canonical host overlay tabs exposed in the <c>F1</c> slide-over settings drawer.
/// </summary>
public enum HostDrawerTabId
{
    Video = 0,
    Controls = 1,
    Keybinds = 2,
    System = 3,
    DiscMedia = 4
}

/// <summary>
/// Non-ref immutable value-type frame input snapshot safe for generic delegates, queues, and async boundaries.
/// </summary>
public readonly struct HostInputFrame
{
    public HostInputFrame(
        long frameSequence,
        HostGamepadButtons digitalButtons,
        float leftStickX = 0f,
        float leftStickY = 0f,
        float rightStickX = 0f,
        float rightStickY = 0f,
        float triggerLeft = 0f,
        float triggerRight = 0f,
        float mouseDeltaX = 0f,
        float mouseDeltaY = 0f,
        int mouseButtons = 0,
        uint hotkeyFlags = 0,
        string activeSchema = "B",
        float wheelDelta = 0f,
        uint gamepadButtonsMask = 0u,
        bool controllerConnected = false,
        int touchActiveCount = 0,
        IReadOnlyList<HostGamepadPortSnapshot>? padPorts = null,
        float pointerContentX = -1f,
        float pointerContentY = -1f,
        bool isPointerInsideContent = false,
        bool isFastForwardHeld = false,
        bool isCutsceneSkipPressed = false)
    {
        FrameSequence = frameSequence;
        DigitalButtons = digitalButtons;
        LeftStickX = leftStickX;
        LeftStickY = leftStickY;
        RightStickX = rightStickX;
        RightStickY = rightStickY;
        TriggerLeft = triggerLeft;
        TriggerRight = triggerRight;
        MouseDeltaX = mouseDeltaX;
        MouseDeltaY = mouseDeltaY;
        MouseButtons = mouseButtons;
        HotkeyFlags = hotkeyFlags;
        ActiveSchema = string.IsNullOrWhiteSpace(activeSchema) ? "B" : activeSchema;
        WheelDelta = wheelDelta;
        GamepadButtonsMask = gamepadButtonsMask;
        ControllerConnected = controllerConnected;
        TouchActiveCount = touchActiveCount;
        PadPorts = padPorts ?? Array.Empty<HostGamepadPortSnapshot>();
        PointerContentX = pointerContentX;
        PointerContentY = pointerContentY;
        IsPointerInsideContent = isPointerInsideContent;
        IsFastForwardHeld = isFastForwardHeld;
        IsCutsceneSkipPressed = isCutsceneSkipPressed;
    }

    public long FrameSequence { get; }
    public HostGamepadButtons DigitalButtons { get; }
    public float LeftStickX { get; }
    public float LeftStickY { get; }
    public float RightStickX { get; }
    public float RightStickY { get; }
    public float TriggerLeft { get; }
    public float TriggerRight { get; }
    public float MouseDeltaX { get; }
    public float MouseDeltaY { get; }
    public int MouseButtons { get; }
    public uint HotkeyFlags { get; }
    public string ActiveSchema { get; }
    public float WheelDelta { get; }
    public uint GamepadButtonsMask { get; }
    public bool ControllerConnected { get; }
    public int TouchActiveCount { get; }
    public IReadOnlyList<HostGamepadPortSnapshot> PadPorts { get; }
    public float PointerContentX { get; }
    public float PointerContentY { get; }
    public bool IsPointerInsideContent { get; }
    public bool IsFastForwardHeld { get; }
    public bool IsCutsceneSkipPressed { get; }
}

/// <summary>
/// Describes the interleaved audio stream format configured by a host session.
/// </summary>
public sealed record HostAudioFormat(int SampleRate = 44100, short Channels = 2, int RingCapacitySamples = 32768);

/// <summary>
/// Single descriptor item rendered inside an <c>F1</c> drawer tab.
/// </summary>
public sealed record HostDrawerRowDescriptor(
    string RowId,
    HostDrawerTabId Tab,
    string Label,
    string Value,
    string Description = "",
    bool IsInteractive = false);

/// <summary>
/// Console-agnostic descriptor parameterizing the <c>F1</c> 5-tab settings drawer and media gate.
/// </summary>
public sealed record HostDrawerDescriptor(
    string ModuleTitle,
    string EngineDisplayName,
    string ExecutionTierLabel,
    string BackendLabel,
    int ResolutionScale,
    string ActiveSchema,
    string MediaLabel = "Disc/Media Image",
    IReadOnlyList<HostDrawerRowDescriptor>? ExtraRows = null,
    IReadOnlyList<HostControlOptionDescriptor>? ControlOptions = null)
{
    public static HostDrawerDescriptor CreateDefault(string moduleTitle = "reEngine Host", string engineDisplayName = "reEngine.Host") =>
        new(moduleTitle, engineDisplayName, "AOT Primary", RecompRenderBackendIds.OpenGl33, 2, "B");
}

/// <summary>
/// Reported overlay control geometry mirrored into automation inspection trees.
/// </summary>
public sealed record HostWidgetBounds(
    string ControlId,
    string ControlType,
    string Label,
    Rectangle Bounds,
    bool IsVisible = true);

/// <summary>
/// Runtime launch options accepted by <c>HostSessionRunner</c>.
/// </summary>
public sealed record HostSessionOptions
{
    public string WindowTitle { get; init; } = "reEngine Host Session";
    public int WindowWidth { get; init; } = 1280;
    public int WindowHeight { get; init; } = 720;
    public int ContentWidth { get; init; } = 320;
    public int ContentHeight { get; init; } = 240;
    public double TargetFps { get; init; } = 60.0;
    public int? HeadlessFrameCount { get; init; }
    public string? DumpFramePath { get; init; }
    public string? DumpAudioPath { get; init; }
    public int? AutomationPort { get; init; }
    public string? AutomationToken { get; init; }
    public string? AllowedSnapshotRoot { get; init; }
    public bool RequireVisibleWindow { get; init; } = true;
    public bool ShowMediaGateInitially { get; init; }
    public string? ExpectedMediaSha256 { get; init; }
    public HostRenderBackendKind PreferredBackend { get; init; } = HostRenderBackendKind.Software;
    public bool RequireBackend { get; init; }
    public bool IsFullscreen { get; init; }
    public string? AudioDriverOverride { get; init; }
    public string? HdPacksDirectory { get; init; }
    public bool IsHdTexturePackEnabled { get; init; }
    public HostAspectRatioMode AspectRatioMode { get; init; } = HostAspectRatioMode.NativePar;
    public HostOverscanCrop OverscanCropPx { get; init; }
    public bool IsFastForwardEnabled { get; init; }
    public double? ContentPixelAspectRatio { get; init; }
    public bool EnableTouchOverlay { get; init; }
    public IReadOnlyList<IHostInputProvider>? InputProviders { get; init; }
    public HostAudioFormat AudioFormat { get; init; } = new();
    public HostDrawerDescriptor DrawerDescriptor { get; init; } = HostDrawerDescriptor.CreateDefault();
    public Func<IHostAutomationBridge>? AutomationBridgeFactory { get; init; }
    public IHostSessionEventCallbacks? SessionEventCallbacks { get; init; }
}

/// <summary>
/// Exit result returned by <c>HostSessionRunner</c>.
/// </summary>
public sealed record HostSessionResult(
    int ExitCode,
    long FramesRendered,
    bool IsDrawerOpen,
    bool IsHudVisible,
    HostDrawerTabId ActiveTab,
    string? CapturedFramePath,
    string? CapturedAudioPath,
    string Summary,
    string? ActiveBackendId = null,
    string? ActiveAudioDriver = null,
    string? FallbackBanner = null);

/// <summary>
/// Video surface presenter abstraction implemented cleanly for RGBA or GL backends.
/// </summary>
public interface IHostRenderBackend : IRecompRenderBackend, IDisposable
{
    int SurfaceWidth { get; }
    int SurfaceHeight { get; }
    bool TryCopyRgbaFrame(out int width, out int height, out byte[] rgbaBytes);
    void SavePng(string outputPath);
}

/// <summary>
/// Engine callbacks invoked each frame by <c>HostSessionRunner</c>.
/// </summary>
public interface IHostSessionCallbacks
{
    ValueTask OnFrameTickAsync(
        long frameNumber,
        HostInputFrame input,
        Span<byte> rgbaFramebuffer,
        int width,
        int height,
        CancellationToken ct);

    int ReadAudioSamples(Span<short> interleavedStereoPcm);
}

/// <summary>
/// Thread-safe automation bridge target driven at the SDL frame boundary under Debug/Test builds.
/// </summary>
public interface IHostAutomationTarget
{
    IReadOnlyList<HostWidgetBounds> GetOverlayWidgets();
    void EnqueuePointerClick(int x, int y, string? targetId);
    void EnqueueKeyPress(string key, IReadOnlyList<string>? modifiers = null);
    bool TryCaptureFramebufferPng(out int width, out int height, out byte[] rgbaBytes);
}
