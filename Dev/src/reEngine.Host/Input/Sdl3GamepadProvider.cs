// @module: reEngine.Host.Input.Sdl3GamepadProvider
// @purpose: Cross-platform 4-port SDL3 Xbox/DualSense/DualShock/SwitchPro/HID gamepad IHostInputProvider with deadzone, trigger threshold, hot-plugging, and rumble.
// @depends-on: System, System.Collections.Generic, reEngine.Host.Contracts
// @exports: Sdl3GamepadProvider

using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Polls up to 4 hot-pluggable SDL3 gamepads (Xbox, DualSense, DualShock, Switch Pro, and generic HID controllers)
/// and maps Standard 18 binding codes (<c>2001..2032</c>) plus per-port dual-motor rumble.
/// </summary>
public sealed unsafe class Sdl3GamepadProvider : IHostInputProvider
{
    private sealed class PadSlot
    {
        public IntPtr Handle;
        public uint InstanceId;
        public string DeviceName = string.Empty;
        public string DeviceFamily = "Disconnected";
    }

    /// <summary>Maximum number of concurrent SDL3 gamepad ports polled (<c>0..3</c>).</summary>
    public const int MaxPorts = Sdl3GamepadNative.MaxGamepadPorts;

    /// <summary>Normalized radial axis deadzone threshold applied to left and right analog sticks.</summary>
    public const float StickDeadzone = 0.15f;

    /// <summary>Normalized analog trigger threshold required to assert digital trigger click bits.</summary>
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
    private readonly PadSlot[] _ports = [new(), new(), new(), new()];
    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "sdl3-gamepad";

    /// <summary>Gets the connected Port 0 controller display name, or empty when Port 0 is disconnected.</summary>
    public string DeviceName { get { lock (_sync) return _ports[0].DeviceName; } }

    /// <summary>Gets the connected Port 0 controller hardware family, or <c>"Disconnected"</c>.</summary>
    public string DeviceFamily { get { lock (_sync) return _ports[0].DeviceFamily; } }

    /// <inheritdoc />
    public bool IsConnected { get { lock (_sync) return !_disposed && _ports[0].Handle != IntPtr.Zero; } }

    /// <summary>Gets the count of currently connected SDL3 gamepad ports (<c>0..4</c>).</summary>
    public int ConnectedPortCount
    {
        get { lock (_sync) { int n = 0; if (!_disposed) for (int i = 0; i < MaxPorts; i++) if (_ports[i].Handle != IntPtr.Zero) n++; return n; } }
    }

    /// <summary>Returns whether the specified zero-based controller port (<c>0..3</c>) is currently connected.</summary>
    public bool IsPortConnected(int portIndex) { if ((uint)portIndex >= MaxPorts) return false; lock (_sync) return !_disposed && _ports[portIndex].Handle != IntPtr.Zero; }

    /// <summary>Gets the display name of the controller assigned to <paramref name="portIndex"/>.</summary>
    public string GetPortDeviceName(int portIndex) { if ((uint)portIndex >= MaxPorts) return string.Empty; lock (_sync) return _ports[portIndex].DeviceName; }

    /// <summary>Gets the classified hardware family of the controller assigned to <paramref name="portIndex"/>.</summary>
    public string GetPortDeviceFamily(int portIndex) { if ((uint)portIndex >= MaxPorts) return "Disconnected"; lock (_sync) return _ports[portIndex].DeviceFamily; }

    /// <inheritdoc />
    public HostExternalInputSnapshot Poll()
    {
        HostGamepadPortSnapshot p0 = PollAllPorts()[0];
        return p0.IsConnected
            ? new HostExternalInputSnapshot(p0.Buttons, p0.LeftX, p0.LeftY, p0.RightX, p0.RightY, p0.TriggerLeft, p0.TriggerRight, p0.RawButtonsMask, true, 0)
            : default;
    }

    /// <summary>Polls all 4 hot-pluggable SDL3 gamepad ports (<c>0..3</c>) and returns their per-port snapshots.</summary>
    public IReadOnlyList<HostGamepadPortSnapshot> PollAllPorts()
    {
        lock (_sync)
        {
            if (_disposed || !Sdl3GamepadNative.TryIsGamepadSubsystemInitialized())
            {
                CloseAllPortsUnsafe(sdlAlive: false);
                return CreateDisconnectedPorts();
            }
            try
            {
                Sdl3GamepadNative.SDL_UpdateGamepads();
                RefreshConnectionsUnsafe();
                var snapshots = new HostGamepadPortSnapshot[MaxPorts];
                for (int p = 0; p < MaxPorts; p++)
                {
                    IntPtr handle = _ports[p].Handle;
                    if (handle == IntPtr.Zero) { snapshots[p] = DisconnectedSnapshot(p); continue; }
                    Sdl3GamepadNative.ReadGamepadInputs(handle, out uint bits, out short lx, out short ly, out short rx, out short ry, out short lt, out short rt);
                    snapshots[p] = BuildPortSnapshot(p, bits, lx, ly, rx, ry, lt, rt, connected: true);
                }
                return snapshots;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                CloseAllPortsUnsafe(sdlAlive: false);
                return CreateDisconnectedPorts();
            }
        }
    }

    /// <inheritdoc />
    public bool TryRumble(HostRumbleRequest request) => TryRumble(0, request);

    /// <summary>Triggers dual-motor haptic rumble on the controller assigned to <paramref name="portIndex"/>.</summary>
    public bool TryRumble(int portIndex, HostRumbleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((uint)portIndex >= MaxPorts) return false;
        lock (_sync)
        {
            IntPtr handle = _ports[portIndex].Handle;
            if (_disposed || handle == IntPtr.Zero || !Sdl3GamepadNative.TryIsGamepadSubsystemInitialized()) return false;
            try { return Sdl3GamepadNative.SDL_RumbleGamepad(handle, request.LowFrequency, request.HighFrequency, request.DurationMs) != 0; }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return false; }
        }
    }

    /// <summary>Builds a normalized Port 0 <see cref="HostExternalInputSnapshot"/> from raw SDL3 button bits and axes.</summary>
    public static HostExternalInputSnapshot BuildSnapshot(
        uint rawSdlButtonBits, short rawLeftX, short rawLeftY, short rawRightX, short rawRightY,
        short rawLeftTrigger, short rawRightTrigger, bool connected = true)
    {
        if (!connected) return default;
        HostGamepadPortSnapshot p = BuildPortSnapshot(0, rawSdlButtonBits, rawLeftX, rawLeftY, rawRightX, rawRightY, rawLeftTrigger, rawRightTrigger, connected);
        return new HostExternalInputSnapshot(p.Buttons, p.LeftX, p.LeftY, p.RightX, p.RightY, p.TriggerLeft, p.TriggerRight, p.RawButtonsMask, true, 0);
    }

    /// <summary>Builds a normalized per-port <see cref="HostGamepadPortSnapshot"/> for <paramref name="portIndex"/> (<c>0..3</c>).</summary>
    public static HostGamepadPortSnapshot BuildSnapshot(
        int portIndex, uint rawSdlButtonBits, short rawLeftX, short rawLeftY, short rawRightX, short rawRightY,
        short rawLeftTrigger, short rawRightTrigger, bool connected = true) =>
        BuildPortSnapshot(portIndex, rawSdlButtonBits, rawLeftX, rawLeftY, rawRightX, rawRightY, rawLeftTrigger, rawRightTrigger, connected);

    /// <summary>Builds a normalized <see cref="HostGamepadPortSnapshot"/> from raw SDL3 button bits and axis readings.</summary>
    public static HostGamepadPortSnapshot BuildPortSnapshot(
        int portIndex, uint rawSdlButtonBits, short rawLeftX, short rawLeftY, short rawRightX, short rawRightY,
        short rawLeftTrigger, short rawRightTrigger, bool connected = true)
    {
        int clampedPort = Math.Clamp(portIndex, 0, MaxPorts - 1);
        if (!connected) return DisconnectedSnapshot(clampedPort);

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
        return new HostGamepadPortSnapshot(clampedPort, true, buttons, lx, ly, rx, ry, lt, rt, mask);
    }

    /// <summary>Normalizes a signed 16-bit SDL axis value into <c>[-1, 1]</c> with radial deadzone rescaling.</summary>
    public static float ApplyStickDeadzone(short rawAxis, float deadzone = StickDeadzone)
    {
        float norm = Math.Clamp(rawAxis < 0 ? rawAxis / 32768f : rawAxis / 32767f, -1f, 1f);
        float abs = MathF.Abs(norm);
        return abs < deadzone ? 0f : Math.Clamp(MathF.Sign(norm) * ((abs - deadzone) / MathF.Max(0.0001f, 1f - deadzone)), -1f, 1f);
    }

    /// <summary>Normalizes a signed 16-bit SDL trigger axis reading into <c>[0, 1]</c>.</summary>
    public static float NormalizeTrigger(short rawTrigger) => rawTrigger <= 0 ? 0f : Math.Clamp(rawTrigger / 32767f, 0f, 1f);

    /// <summary>Maps an SDL3 gamepad button index to a Standard 18 gamepad binding code (<c>2001..2032</c>).</summary>
    public static int MapSdlButtonToBindingCode(int sdlButtonIndex) => sdlButtonIndex switch
    {
        >= 0 and <= 14 => HostBindingCodes.GamepadMin + sdlButtonIndex,
        Sdl3GamepadNative.ButtonMisc1 => 2018, Sdl3GamepadNative.ButtonRightPaddle1 => 2019,
        Sdl3GamepadNative.ButtonTouchpad => 2020, Sdl3GamepadNative.ButtonLeftPaddle1 => 2021,
        Sdl3GamepadNative.ButtonRightPaddle2 => 2022, Sdl3GamepadNative.ButtonLeftPaddle2 => 2023,
        _ => 0
    };

    /// <summary>Converts a Standard 18 gamepad binding code (<c>2001..2032</c>) into its bitmask bit.</summary>
    public static uint ToGamepadMaskBit(int bindingCode) =>
        bindingCode is >= HostBindingCodes.GamepadMin and <= HostBindingCodes.GamepadMax ? 1u << (bindingCode - HostBindingCodes.GamepadMin) : 0u;

    /// <summary>Tests whether <paramref name="bindingCode"/> is asserted in <paramref name="gamepadButtonsMask"/>.</summary>
    public static bool IsGamepadCodePressed(uint gamepadButtonsMask, int bindingCode) =>
        (gamepadButtonsMask & ToGamepadMaskBit(bindingCode)) != 0;

    /// <summary>Classifies an SDL gamepad into a human-readable controller family label.</summary>
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

    private void RefreshConnectionsUnsafe()
    {
        uint* ids = Sdl3GamepadNative.SDL_GetGamepads(out int count);
        try
        {
            for (int p = 0; p < MaxPorts; p++) if (_ports[p].Handle != IntPtr.Zero && !ContainsInstanceId(ids, count, _ports[p].InstanceId)) ClosePortUnsafe(p, sdlAlive: true);
            if (ids == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                if (IsInstanceAssigned(ids[i])) continue;
                int freePort = FindFirstOpenPort();
                if (freePort < 0) break;
                IntPtr opened = Sdl3GamepadNative.SDL_OpenGamepad(ids[i]);
                if (opened == IntPtr.Zero) continue;
                string? name = Sdl3GamepadNative.ReadGamepadName(opened);
                _ports[freePort].Handle = opened;
                _ports[freePort].InstanceId = ids[i];
                _ports[freePort].DeviceName = string.IsNullOrWhiteSpace(name) ? $"Gamepad #{ids[i]}" : name;
                _ports[freePort].DeviceFamily = ClassifyControllerFamily(Sdl3GamepadNative.SDL_GetGamepadType(opened), name, Sdl3GamepadNative.SDL_GetGamepadVendor(opened));
            }
        }
        finally { if (ids != null) Sdl3GamepadNative.SDL_free(ids); }
    }

    private bool IsInstanceAssigned(uint id) { for (int p = 0; p < MaxPorts; p++) if (_ports[p].Handle != IntPtr.Zero && _ports[p].InstanceId == id) return true; return false; }
    private int FindFirstOpenPort() { for (int p = 0; p < MaxPorts; p++) if (_ports[p].Handle == IntPtr.Zero) return p; return -1; }
    private static bool ContainsInstanceId(uint* ids, int count, uint targetId) { if (ids == null || count <= 0 || targetId == 0) return false; for (int i = 0; i < count; i++) if (ids[i] == targetId) return true; return false; }
    private static HostGamepadPortSnapshot DisconnectedSnapshot(int p) => new(p, false, HostGamepadButtons.None, 0f, 0f, 0f, 0f, 0f, 0f, 0u);
    private static HostGamepadPortSnapshot[] CreateDisconnectedPorts() => [DisconnectedSnapshot(0), DisconnectedSnapshot(1), DisconnectedSnapshot(2), DisconnectedSnapshot(3)];
    private void CloseAllPortsUnsafe(bool sdlAlive) { for (int p = 0; p < MaxPorts; p++) ClosePortUnsafe(p, sdlAlive); }

    private void ClosePortUnsafe(int portIndex, bool sdlAlive)
    {
        if (_ports[portIndex].Handle != IntPtr.Zero && sdlAlive)
        {
            try { Sdl3GamepadNative.SDL_CloseGamepad(_ports[portIndex].Handle); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
        }
        _ports[portIndex].Handle = IntPtr.Zero;
        _ports[portIndex].InstanceId = 0;
        _ports[portIndex].DeviceName = string.Empty;
        _ports[portIndex].DeviceFamily = "Disconnected";
    }

    /// <inheritdoc />
    public void Dispose() { lock (_sync) { if (_disposed) return; _disposed = true; CloseAllPortsUnsafe(Sdl3GamepadNative.TryIsGamepadSubsystemInitialized()); } }
}
