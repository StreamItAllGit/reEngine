// @module: reEngine.Host.Input.HostInputMapper
// @purpose: Translates keyboard scancodes, mouse deltas, gamepad state, and synthetic commands into immutable HostInputFrame snapshots.
// @depends-on: System, System.Collections.Generic, reEngine.Host.Contracts
// @exports: HostHotkeyFlags, HostInputMapper

using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

/// <summary>
/// Edge-triggered host hotkey bitmask reported on <see cref="HostInputFrame.HotkeyFlags"/>.
/// </summary>
[Flags]
public enum HostHotkeyFlags : uint
{
    None = 0,
    ToggleDrawer = 1u << 0,
    ToggleHud = 1u << 1,
    DumpFrame = 1u << 2,
    Escape = 1u << 3
}

/// <summary>
/// Maps raw keyboard scancodes, synthetic test keys, mouse deltas, and gamepad state into non-ref <see cref="HostInputFrame"/> values.
/// </summary>
public sealed class HostInputMapper
{
    private readonly HashSet<int> _heldScancodes = new();
    private readonly List<IHostInputProvider> _providers = new();
    private long _frameSequence;
    private uint _pendingHotkeys;
    private float _pendingMouseDx;
    private float _pendingMouseDy;
    private float _pendingWheelDelta;
    private int _mouseButtons;

    public HostInputSchemaKind ActiveSchema { get; set; } = HostInputSchemaKind.SchemaB;

    public void SetScancodeState(int scancode, bool isPressed)
    {
        if (isPressed)
        {
            if (_heldScancodes.Add(scancode))
            {
                switch (scancode)
                {
                    case HostScancodes.F1: _pendingHotkeys |= (uint)HostHotkeyFlags.ToggleDrawer; break;
                    case HostScancodes.F3: _pendingHotkeys |= (uint)HostHotkeyFlags.ToggleHud; break;
                    case HostScancodes.F12: _pendingHotkeys |= (uint)HostHotkeyFlags.DumpFrame; break;
                    case HostScancodes.Escape: _pendingHotkeys |= (uint)HostHotkeyFlags.Escape; break;
                }
            }
        }
        else
        {
            _heldScancodes.Remove(scancode);
        }
    }

    public void InjectSyntheticKey(string? keyName)
    {
        if (TryMapKeyNameToScancode(keyName, out int scancode))
        {
            SetScancodeState(scancode, true);
            if (scancode is HostScancodes.F1 or HostScancodes.F3 or HostScancodes.F12 or HostScancodes.Escape)
            {
                _heldScancodes.Remove(scancode);
            }
        }
    }

    public void AccumulateMouse(float dx, float dy, int buttons)
    {
        _pendingMouseDx += dx;
        _pendingMouseDy += dy;
        _mouseButtons = buttons;
    }

    public void AccumulateMouseWheel(float deltaY) => _pendingWheelDelta += deltaY;

    public void SetMouseButton(int buttonIndex, bool isPressed)
    {
        if (buttonIndex is < 1 or > 5) return;
        int mask = 1 << (buttonIndex - 1);
        if (isPressed) _mouseButtons |= mask;
        else _mouseButtons &= ~mask;
    }

    public void RegisterProvider(IHostInputProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_providers.Contains(provider)) _providers.Add(provider);
    }

    public bool RequestRumble(HostRumbleRequest request)
    {
        bool sent = false;
        foreach (IHostInputProvider provider in _providers)
        {
            if (provider.TryRumble(request)) sent = true;
        }
        return sent;
    }

    public HostInputFrame BuildFrame(
        HostGamepadButtons externalButtons = HostGamepadButtons.None,
        float extLeftX = 0f,
        float extLeftY = 0f,
        float extRightX = 0f,
        float extRightY = 0f,
        float extTrigLeft = 0f,
        float extTrigRight = 0f)
    {
        HostGamepadButtons digital = externalButtons;
        float lx = extLeftX;
        float ly = extLeftY;
        float rx = extRightX;
        float ry = extRightY;
        float tl = extTrigLeft;
        float tr = extTrigRight;

        ApplyDirectionalKeys(ref digital, ref lx, ref ly);
        ApplySchemaButtons(ref digital, ref rx, ref ry, ref tl, ref tr);

        uint hotkeys = _pendingHotkeys;
        _pendingHotkeys = 0;
        float mdx = _pendingMouseDx;
        float mdy = _pendingMouseDy;
        float wheel = _pendingWheelDelta;
        _pendingMouseDx = 0f;
        _pendingMouseDy = 0f;
        _pendingWheelDelta = 0f;

        uint gpMask = 0u;
        bool gpConnected = false;
        int touchCount = 0;
        foreach (IHostInputProvider provider in _providers)
        {
            HostExternalInputSnapshot ext = provider.Poll();
            digital |= ext.Buttons;
            gpMask |= ext.GamepadButtonsMask;
            if (ext.ControllerConnected) gpConnected = true;
            touchCount += ext.TouchActiveCount;
            if (MathF.Abs(ext.LeftX) > MathF.Abs(lx)) lx = ext.LeftX;
            if (MathF.Abs(ext.LeftY) > MathF.Abs(ly)) ly = ext.LeftY;
            if (MathF.Abs(ext.RightX) > MathF.Abs(rx)) rx = ext.RightX;
            if (MathF.Abs(ext.RightY) > MathF.Abs(ry)) ry = ext.RightY;
            if (ext.TriggerLeft > tl) tl = ext.TriggerLeft;
            if (ext.TriggerRight > tr) tr = ext.TriggerRight;
        }

        return new HostInputFrame(
            frameSequence: ++_frameSequence,
            digitalButtons: digital,
            leftStickX: Math.Clamp(lx, -1f, 1f),
            leftStickY: Math.Clamp(ly, -1f, 1f),
            rightStickX: Math.Clamp(rx, -1f, 1f),
            rightStickY: Math.Clamp(ry, -1f, 1f),
            triggerLeft: Math.Clamp(tl, 0f, 1f),
            triggerRight: Math.Clamp(tr, 0f, 1f),
            mouseDeltaX: mdx,
            mouseDeltaY: mdy,
            mouseButtons: _mouseButtons,
            hotkeyFlags: hotkeys,
            activeSchema: HostInputSchemas.ToShortCode(ActiveSchema),
            wheelDelta: wheel,
            gamepadButtonsMask: gpMask,
            controllerConnected: gpConnected,
            touchActiveCount: touchCount);
    }

    public static bool TryMapKeyNameToScancode(string? keyName, out int scancode)
    {
        scancode = 0;
        if (string.IsNullOrWhiteSpace(keyName)) return false;
        scancode = keyName.Trim().ToUpperInvariant() switch
        {
            "W" => HostScancodes.W,
            "A" => HostScancodes.A,
            "S" => HostScancodes.S,
            "D" => HostScancodes.D,
            "Z" => HostScancodes.Z,
            "X" => HostScancodes.X,
            "Q" => HostScancodes.Q,
            "E" => HostScancodes.E,
            "K" => HostScancodes.K,
            "L" => HostScancodes.L,
            "UP" => HostScancodes.Up,
            "DOWN" => HostScancodes.Down,
            "LEFT" => HostScancodes.Left,
            "RIGHT" => HostScancodes.Right,
            "ENTER" or "RETURN" => HostScancodes.Return,
            "TAB" => HostScancodes.Tab,
            "SPACE" => HostScancodes.Space,
            "ESC" or "ESCAPE" => HostScancodes.Escape,
            "F1" => HostScancodes.F1,
            "F3" => HostScancodes.F3,
            "F12" => HostScancodes.F12,
            _ => 0
        };
        return scancode != 0;
    }

    private void ApplyDirectionalKeys(ref HostGamepadButtons digital, ref float lx, ref float ly)
    {
        if (_heldScancodes.Contains(HostScancodes.Up) || _heldScancodes.Contains(HostScancodes.W))
        {
            digital |= HostGamepadButtons.DPadUp;
            ly = -1f;
        }
        if (_heldScancodes.Contains(HostScancodes.Down) || _heldScancodes.Contains(HostScancodes.S))
        {
            digital |= HostGamepadButtons.DPadDown;
            ly = 1f;
        }
        if (_heldScancodes.Contains(HostScancodes.Left) || _heldScancodes.Contains(HostScancodes.A))
        {
            digital |= HostGamepadButtons.DPadLeft;
            lx = -1f;
        }
        if (_heldScancodes.Contains(HostScancodes.Right) || _heldScancodes.Contains(HostScancodes.D))
        {
            digital |= HostGamepadButtons.DPadRight;
            lx = 1f;
        }
        if (_heldScancodes.Contains(HostScancodes.Return)) digital |= HostGamepadButtons.Start;
        if (_heldScancodes.Contains(HostScancodes.Tab)) digital |= HostGamepadButtons.Select;
    }

    private void ApplySchemaButtons(
        ref HostGamepadButtons digital,
        ref float rx,
        ref float ry,
        ref float tl,
        ref float tr)
    {
        switch (ActiveSchema)
        {
            case HostInputSchemaKind.SchemaA:
                if (_heldScancodes.Contains(HostScancodes.Z)) digital |= HostGamepadButtons.South;
                if (_heldScancodes.Contains(HostScancodes.X)) digital |= HostGamepadButtons.East;
                break;

            case HostInputSchemaKind.SchemaC:
                if (_heldScancodes.Contains(HostScancodes.K)) digital |= HostGamepadButtons.South;
                if (_heldScancodes.Contains(HostScancodes.L)) digital |= HostGamepadButtons.West;
                if (_heldScancodes.Contains(HostScancodes.Space)) tl = 1f;
                if (_heldScancodes.Contains(HostScancodes.Q)) digital |= HostGamepadButtons.LeftShoulder;
                if (_heldScancodes.Contains(HostScancodes.E)) digital |= HostGamepadButtons.RightShoulder;
                if (_heldScancodes.Contains(HostScancodes.Left)) rx = -1f;
                if (_heldScancodes.Contains(HostScancodes.Right)) rx = 1f;
                if (_heldScancodes.Contains(HostScancodes.Up)) ry = -1f;
                if (_heldScancodes.Contains(HostScancodes.Down)) ry = 1f;
                break;

            default:
                if (_heldScancodes.Contains(HostScancodes.Z)) digital |= HostGamepadButtons.South;
                if (_heldScancodes.Contains(HostScancodes.X)) digital |= HostGamepadButtons.East;
                if (_heldScancodes.Contains(HostScancodes.A)) digital |= HostGamepadButtons.West;
                if (_heldScancodes.Contains(HostScancodes.Q)) digital |= HostGamepadButtons.LeftShoulder;
                if (_heldScancodes.Contains(HostScancodes.E)) digital |= HostGamepadButtons.RightShoulder;
                if (_heldScancodes.Contains(HostScancodes.Num1)) tl = 1f;
                if (_heldScancodes.Contains(HostScancodes.Num3)) tr = 1f;
                break;
        }
    }
}

internal static class HostScancodes
{
    public const int A = 4, D = 7, E = 8, K = 14, L = 15, Q = 20, S = 22, W = 26, X = 27, Z = 29;
    public const int Num1 = 30, Num3 = 32;
    public const int Return = 40, Escape = 41, Tab = 43, Space = 44;
    public const int F1 = 58, F3 = 60, F12 = 69;
    public const int Right = 79, Left = 80, Down = 81, Up = 82;
}
