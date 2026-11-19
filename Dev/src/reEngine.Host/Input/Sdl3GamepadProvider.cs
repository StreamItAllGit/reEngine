// @module: reEngine.Host.Input.Sdl3GamepadProvider
// @purpose: Cross-platform SDL3 Xbox/DualSense/DualShock/SwitchPro/HID gamepad IHostInputProvider with deadzone, trigger threshold, reconnect, and rumble.
// @depends-on: System, reEngine.Host.Contracts
// @exports: Sdl3GamepadProvider

using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Polls SDL3 gamepads (Xbox, DualSense, DualShock, Switch Pro, and generic HID controllers)
/// and maps Standard 18 binding codes (<c>2001..2032</c>) plus dual-motor rumble.
/// </summary>
public sealed unsafe class Sdl3GamepadProvider : IHostInputProvider
{
    public const float StickDeadzone = 0.15f;
    public const float TriggerThreshold = 0.40f;

    private static readonly HostGamepadButtons[] StandardButtonFlags =
    [
        HostGamepadButtons.South, HostGamepadButtons.East, HostGamepadButtons.West, HostGamepadButtons.North,
        HostGamepadButtons.Select, HostGamepadButtons.None, HostGamepadButtons.Start,
        HostGamepadButtons.LeftStickClick, HostGamepadButtons.RightStickClick,
        HostGamepadButtons.LeftShoulder, HostGamepadButtons.RightShoulder,
        HostGamepadButtons.DPadUp, HostGamepadButtons.DPadDown, HostGamepadButtons.DPadLeft, HostGamepadButtons.DPadRight
    ];

    private readonly object _sync = new();
    private IntPtr _activeHandle;
    private uint _activeInstanceId;
    private bool _disposed;

    public string ProviderId => "sdl3-gamepad";
    public string DeviceName { get; private set; } = string.Empty;
    public string DeviceFamily { get; private set; } = "Disconnected";
    public bool IsConnected { get { lock (_sync) return !_disposed && _activeHandle != IntPtr.Zero; } }

    public HostExternalInputSnapshot Poll()
    {
        lock (_sync)
        {
            if (_disposed || !Sdl3GamepadNative.TryIsGamepadSubsystemInitialized())
            {
                CloseHandleUnsafe(sdlAlive: false);
                return default;
            }
            try
            {
                Sdl3GamepadNative.SDL_UpdateGamepads();
                RefreshConnectionUnsafe();
                if (_activeHandle == IntPtr.Zero) return default;

                uint rawBtnBits = 0u;
                for (int b = 0; b <= Sdl3GamepadNative.ButtonTouchpad; b++)
                {
                    if (Sdl3GamepadNative.SDL_GetGamepadButton(_activeHandle, b) != 0) rawBtnBits |= 1u << b;
                }
                return BuildSnapshot(
                    rawBtnBits,
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisLeftX),
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisLeftY),
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisRightX),
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisRightY),
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisLeftTrigger),
                    Sdl3GamepadNative.SDL_GetGamepadAxis(_activeHandle, Sdl3GamepadNative.AxisRightTrigger),
                    connected: true);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                CloseHandleUnsafe(sdlAlive: false);
                return default;
            }
        }
    }

    public bool TryRumble(HostRumbleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (_disposed || _activeHandle == IntPtr.Zero || !Sdl3GamepadNative.TryIsGamepadSubsystemInitialized()) return false;
            try
            {
                return Sdl3GamepadNative.SDL_RumbleGamepad(_activeHandle, request.LowFrequency, request.HighFrequency, request.DurationMs) != 0;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
        }
    }

    public static HostExternalInputSnapshot BuildSnapshot(
        uint rawSdlButtonBits, short rawLeftX, short rawLeftY, short rawRightX, short rawRightY,
        short rawLeftTrigger, short rawRightTrigger, bool connected = true)
    {
        if (!connected) return default;
        float lx = ApplyStickDeadzone(rawLeftX, StickDeadzone), ly = ApplyStickDeadzone(rawLeftY, StickDeadzone);
        float rx = ApplyStickDeadzone(rawRightX, StickDeadzone), ry = ApplyStickDeadzone(rawRightY, StickDeadzone);
        float lt = NormalizeTrigger(rawLeftTrigger), rt = NormalizeTrigger(rawRightTrigger);

        HostGamepadButtons buttons = HostGamepadButtons.None;
        uint mask = 0u;
        for (int b = 0; b <= Sdl3GamepadNative.ButtonTouchpad; b++)
        {
            if ((rawSdlButtonBits & (1u << b)) == 0) continue;
            if (MapSdlButtonToBindingCode(b) is int code and > 0) mask |= ToGamepadMaskBit(code);
            if ((uint)b < (uint)StandardButtonFlags.Length) buttons |= StandardButtonFlags[b];
        }
        if (lt >= TriggerThreshold) { buttons |= HostGamepadButtons.LeftTriggerClick; mask |= ToGamepadMaskBit(HostBindingCodes.GamepadLT); }
        if (rt >= TriggerThreshold) { buttons |= HostGamepadButtons.RightTriggerClick; mask |= ToGamepadMaskBit(HostBindingCodes.GamepadRT); }

        return new HostExternalInputSnapshot(buttons, lx, ly, rx, ry, lt, rt, mask, ControllerConnected: true, TouchActiveCount: 0);
    }

    public static float ApplyStickDeadzone(short rawAxis, float deadzone = StickDeadzone)
    {
        float norm = Math.Clamp(rawAxis < 0 ? rawAxis / 32768f : rawAxis / 32767f, -1f, 1f);
        float abs = MathF.Abs(norm);
        if (abs < deadzone) return 0f;
        return Math.Clamp(MathF.Sign(norm) * ((abs - deadzone) / MathF.Max(0.0001f, 1f - deadzone)), -1f, 1f);
    }

    public static float NormalizeTrigger(short rawTrigger) => rawTrigger <= 0 ? 0f : Math.Clamp(rawTrigger / 32767f, 0f, 1f);

    public static int MapSdlButtonToBindingCode(int sdlButtonIndex) => sdlButtonIndex switch
    {
        >= 0 and <= 14 => HostBindingCodes.GamepadMin + sdlButtonIndex,
        Sdl3GamepadNative.ButtonMisc1 => 2018, Sdl3GamepadNative.ButtonRightPaddle1 => 2019,
        Sdl3GamepadNative.ButtonTouchpad => 2020, Sdl3GamepadNative.ButtonLeftPaddle1 => 2021,
        Sdl3GamepadNative.ButtonRightPaddle2 => 2022, Sdl3GamepadNative.ButtonLeftPaddle2 => 2023,
        _ => 0
    };

    public static uint ToGamepadMaskBit(int bindingCode) =>
        bindingCode is >= HostBindingCodes.GamepadMin and <= HostBindingCodes.GamepadMax ? 1u << (bindingCode - HostBindingCodes.GamepadMin) : 0u;

    public static bool IsGamepadCodePressed(uint gamepadButtonsMask, int bindingCode) =>
        (gamepadButtonsMask & ToGamepadMaskBit(bindingCode)) != 0;

    public static string ClassifyControllerFamily(int sdlGamepadType, string? deviceName, ushort vendorId = 0)
    {
        string name = deviceName ?? string.Empty;
        return sdlGamepadType switch
        {
            2 => "Xbox 360", 3 => "Xbox One / Series", 4 => "DualShock 3", 5 => "DualShock 4", 6 => "DualSense",
            7 or 8 or 9 or 10 => "Switch Pro", 11 => "GameCube", 12 => "Steam",
            _ when vendorId == 0x054C || name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) => "DualSense",
            _ when name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) || name.Contains("PS4", StringComparison.OrdinalIgnoreCase) => "DualShock",
            _ when vendorId == 0x045E || name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) => "Xbox",
            _ when vendorId == 0x057E || name.Contains("Switch", StringComparison.OrdinalIgnoreCase) => "Switch Pro",
            _ => "Generic HID"
        };
    }

    private void RefreshConnectionUnsafe()
    {
        uint* ids = Sdl3GamepadNative.SDL_GetGamepads(out int count);
        try
        {
            if (_activeHandle != IntPtr.Zero && !ContainsInstanceId(ids, count, _activeInstanceId)) CloseHandleUnsafe(sdlAlive: true);
            if (_activeHandle != IntPtr.Zero || ids == null || count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                IntPtr opened = Sdl3GamepadNative.SDL_OpenGamepad(ids[i]);
                if (opened == IntPtr.Zero) continue;
                _activeHandle = opened;
                _activeInstanceId = ids[i];
                string? name = Sdl3GamepadNative.ReadGamepadName(opened);
                DeviceName = string.IsNullOrWhiteSpace(name) ? $"Gamepad #{ids[i]}" : name;
                DeviceFamily = ClassifyControllerFamily(Sdl3GamepadNative.SDL_GetGamepadType(opened), name, Sdl3GamepadNative.SDL_GetGamepadVendor(opened));
                break;
            }
        }
        finally { if (ids != null) Sdl3GamepadNative.SDL_free(ids); }
    }

    private static bool ContainsInstanceId(uint* ids, int count, uint targetId)
    {
        if (ids == null || count <= 0 || targetId == 0) return false;
        for (int i = 0; i < count; i++) if (ids[i] == targetId) return true;
        return false;
    }

    private void CloseHandleUnsafe(bool sdlAlive)
    {
        if (_activeHandle != IntPtr.Zero && sdlAlive)
        {
            try { Sdl3GamepadNative.SDL_CloseGamepad(_activeHandle); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        }
        _activeHandle = IntPtr.Zero;
        _activeInstanceId = 0;
        DeviceName = string.Empty;
        DeviceFamily = "Disconnected";
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            CloseHandleUnsafe(Sdl3GamepadNative.TryIsGamepadSubsystemInitialized());
        }
    }
}
