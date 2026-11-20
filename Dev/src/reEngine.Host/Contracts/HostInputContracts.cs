// @module: reEngine.Host.Contracts.HostInputContracts
// @purpose: Standard 18 multi-bind key/mouse/gamepad codes, dynamic ControlOptions, rumble, touch overlay, and IHostInputProvider contracts.
// @depends-on: System, System.Collections.Generic
// @exports: HostBindingCodes, HostMultiBindSlot, HostControlOptionDescriptor, HostRumbleRequest, HostTouchPoint, HostTouchGestureEvent, HostExternalInputSnapshot, IHostInputProvider

namespace reEngine.Host.Contracts;

/// <summary>
/// Standard 18 binding code constants across SDL keyboard scancodes (4..511), mouse/wheel (1001..1007), and gamepad (2001..2032).
/// </summary>
public static class HostBindingCodes
{
    public const int MinScancode = 4;
    public const int MaxScancode = 511;

    public const int MouseLeft = 1001;
    public const int MouseRight = 1002;
    public const int MouseMiddle = 1003;
    public const int Mouse4 = 1004;
    public const int Mouse5 = 1005;
    public const int WheelUp = 1006;
    public const int WheelDown = 1007;

    public const int GamepadMin = 2001;
    public const int GamepadSouth = 2001;
    public const int GamepadEast = 2002;
    public const int GamepadWest = 2003;
    public const int GamepadNorth = 2004;
    public const int GamepadSelect = 2005;
    public const int GamepadGuide = 2006;
    public const int GamepadStart = 2007;
    public const int GamepadL3 = 2008;
    public const int GamepadR3 = 2009;
    public const int GamepadLB = 2010;
    public const int GamepadRB = 2011;
    public const int GamepadDPadUp = 2012;
    public const int GamepadDPadDown = 2013;
    public const int GamepadDPadLeft = 2014;
    public const int GamepadDPadRight = 2015;
    public const int GamepadLT = 2016;
    public const int GamepadRT = 2017;
    public const int GamepadMax = 2032;
}

/// <summary>
/// 3-slot keyboard/mouse plus gamepad binding row exposed in the 5-column F1 Keybinds drawer.
/// </summary>
public sealed record HostMultiBindSlot(
    string ActionId,
    string DisplayName,
    int Slot1,
    int Slot2,
    int Slot3,
    int GamepadCode);

/// <summary>
/// Dynamic module-declared control pill rendered inside <c>F1 -&gt; Controls</c> from <c>metadataJson</c>.
/// </summary>
public sealed record HostControlOptionDescriptor(
    string OptionId,
    string Label,
    IReadOnlyList<string> Choices,
    string SelectedChoice,
    string Description = "");

/// <summary>
/// Dual-motor gamepad haptic rumble command.
/// </summary>
public sealed record HostRumbleRequest(
    ushort LowFrequency,
    ushort HighFrequency,
    uint DurationMs);

/// <summary>
/// Normalized touch contact point (0..1 viewport coordinates) for mobile/handheld overlays.
/// </summary>
public sealed record HostTouchPoint(
    long FingerId,
    float NormalizedX,
    float NormalizedY,
    float Pressure,
    bool IsPressed);

/// <summary>
/// Decoded touch gesture notification (tap, swipe, pinch).
/// </summary>
public sealed record HostTouchGestureEvent(
    string GestureKind,
    float DeltaX,
    float DeltaY,
    float Scale);

/// <summary>
/// Polled snapshot produced by a hardware gamepad, touch overlay, or mobile/platform <see cref="IHostInputProvider"/>.
/// </summary>
public readonly record struct HostExternalInputSnapshot(
    HostGamepadButtons Buttons,
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    float TriggerLeft,
    float TriggerRight,
    uint GamepadButtonsMask,
    bool ControllerConnected,
    int TouchActiveCount);

/// <summary>
/// Platform-neutral non-KB/M input provider (SDL3 Xbox/HID gamepads, touch overlays, Android/iOS/macOS controllers).
/// </summary>
public interface IHostInputProvider : IDisposable
{
    string ProviderId { get; }
    bool IsConnected { get; }
    HostExternalInputSnapshot Poll();
    bool TryRumble(HostRumbleRequest request);
}
