// @module: reEngine.Host.Input.HostInputMapper
// @purpose: Translates keyboard scancodes, mouse/wheel, 3-slot multi-bind actions, and IHostInputProvider snapshots into HostInputFrame.
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
/// Maps raw keyboard scancodes, synthetic keys, mouse/wheel, 3-slot multi-binds, and gamepad/touch providers
/// into immutable non-ref <see cref="HostInputFrame"/> snapshots with repeat suppression and single-edge tracking.
/// </summary>
public sealed class HostInputMapper
{
    private static readonly (string ActionId, HostGamepadButtons Button)[] ButtonActions =
    [
        ("south", HostGamepadButtons.South), ("east", HostGamepadButtons.East),
        ("west", HostGamepadButtons.West), ("north", HostGamepadButtons.North),
        ("leftShoulder", HostGamepadButtons.LeftShoulder), ("rightShoulder", HostGamepadButtons.RightShoulder),
        ("start", HostGamepadButtons.Start), ("select", HostGamepadButtons.Select),
        ("leftStickClick", HostGamepadButtons.LeftStickClick), ("rightStickClick", HostGamepadButtons.RightStickClick)
    ];

    private static readonly Dictionary<string, int> KeyNameToScancode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["W"] = HostScancodes.W, ["A"] = HostScancodes.A, ["S"] = HostScancodes.S, ["D"] = HostScancodes.D,
        ["Z"] = HostScancodes.Z, ["X"] = HostScancodes.X, ["Q"] = HostScancodes.Q, ["E"] = HostScancodes.E,
        ["K"] = HostScancodes.K, ["L"] = HostScancodes.L, ["C"] = HostScancodes.C, ["F"] = HostScancodes.F,
        ["G"] = HostScancodes.G, ["H"] = HostScancodes.H, ["I"] = HostScancodes.I, ["J"] = HostScancodes.J,
        ["M"] = HostScancodes.M, ["N"] = HostScancodes.N, ["P"] = HostScancodes.P, ["R"] = HostScancodes.R,
        ["B"] = HostScancodes.B, ["1"] = HostScancodes.Num1, ["3"] = HostScancodes.Num3,
        ["UP"] = HostScancodes.Up, ["DOWN"] = HostScancodes.Down, ["LEFT"] = HostScancodes.Left, ["RIGHT"] = HostScancodes.Right,
        ["ENTER"] = HostScancodes.Return, ["RETURN"] = HostScancodes.Return, ["TAB"] = HostScancodes.Tab,
        ["SPACE"] = HostScancodes.Space, ["ESC"] = HostScancodes.Escape, ["ESCAPE"] = HostScancodes.Escape,
        ["DEL"] = HostScancodes.Delete, ["DELETE"] = HostScancodes.Delete, ["BACKSPACE"] = HostScancodes.Backspace,
        ["PGUP"] = HostScancodes.PageUp, ["PAGEUP"] = HostScancodes.PageUp,
        ["PGDN"] = HostScancodes.PageDown, ["PAGEDOWN"] = HostScancodes.PageDown,
        ["F1"] = HostScancodes.F1, ["F3"] = HostScancodes.F3, ["F5"] = HostScancodes.F5, ["F9"] = HostScancodes.F9, ["F12"] = HostScancodes.F12
    };

    private readonly HashSet<int> _heldScancodes = new();
    private readonly HashSet<int> _latchedCodes = new();
    private readonly List<IHostInputProvider> _providers = new();
    private readonly HostMultiBindRegistry _registry = new(HostInputSchemaKind.SchemaB);
    private HostInputSchemaKind _activeSchema = HostInputSchemaKind.SchemaB;
    private long _frameSequence;
    private uint _pendingHotkeys;
    private float _pendingMouseDx, _pendingMouseDy, _pendingWheelDelta;
    private int _mouseButtons;

    public HostInputSchemaKind ActiveSchema
    {
        get => _activeSchema;
        set { if (_activeSchema != value) { _activeSchema = value; _registry.ResetToSchemaDefaults(value); } }
    }

    public HostMultiBindRegistry Bindings => _registry;
    public IReadOnlySet<string> ActiveActions => _registry.ActiveActions;
    public IReadOnlySet<string> JustPressedActions => _registry.JustPressedActions;
    public bool IsActionActive(string actionId) => _registry.IsActionActive(actionId);
    public bool IsActionJustPressed(string actionId) => _registry.IsActionJustPressed(actionId);

    public void SetScancodeState(int scancode, bool isPressed) => SetScancodeState(scancode, isPressed, isRepeat: false);

    public void SetScancodeState(int scancode, bool isPressed, bool isRepeat)
    {
        if (isRepeat) return;
        if (!isPressed) { _heldScancodes.Remove(scancode); return; }
        if (!_heldScancodes.Add(scancode)) return;

        if (HostMultiBindRegistry.IsKeyboardCode(scancode)) _latchedCodes.Add(scancode);
        _pendingHotkeys |= scancode switch
        {
            HostScancodes.F1 => (uint)HostHotkeyFlags.ToggleDrawer,
            HostScancodes.F3 => (uint)HostHotkeyFlags.ToggleHud,
            HostScancodes.F12 => (uint)HostHotkeyFlags.DumpFrame,
            HostScancodes.Escape => (uint)HostHotkeyFlags.Escape,
            _ => 0u
        };
    }

    public void InjectSyntheticKey(string? keyName)
    {
        if (!TryMapKeyNameToScancode(keyName, out int scancode)) return;
        SetScancodeState(scancode, true, isRepeat: false);
        if (scancode is HostScancodes.F1 or HostScancodes.F3 or HostScancodes.F12 or HostScancodes.Escape)
        {
            _heldScancodes.Remove(scancode);
        }
    }

    public void AccumulateMouse(float dx, float dy, int buttons)
    {
        _pendingMouseDx += dx;
        _pendingMouseDy += dy;
        int newlyPressed = buttons & ~_mouseButtons;
        for (int b = 1; b <= 5; b++)
        {
            if ((newlyPressed & (1 << (b - 1))) != 0 && HostMultiBindRegistry.MouseButtonIndexToBindingCode(b) is int code and > 0)
            {
                _latchedCodes.Add(code);
            }
        }
        _mouseButtons = buttons;
    }

    public void AccumulateMouseWheel(float deltaY)
    {
        _pendingWheelDelta += deltaY;
        if (deltaY > 0f) _latchedCodes.Add(HostBindingCodes.WheelUp);
        else if (deltaY < 0f) _latchedCodes.Add(HostBindingCodes.WheelDown);
    }

    public void SetMouseButton(int buttonIndex, bool isPressed)
    {
        if (buttonIndex is < 1 or > 5) return;
        int mask = 1 << (buttonIndex - 1);
        if (!isPressed) { _mouseButtons &= ~mask; return; }
        bool wasDown = (_mouseButtons & mask) != 0;
        _mouseButtons |= mask;
        if (!wasDown && HostMultiBindRegistry.MouseButtonIndexToBindingCode(buttonIndex) is int code and > 0) _latchedCodes.Add(code);
    }

    public void RegisterProvider(IHostInputProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_providers.Contains(provider)) _providers.Add(provider);
    }

    public bool RequestRumble(HostRumbleRequest request)
    {
        bool sent = false;
        foreach (IHostInputProvider provider in _providers) if (provider.TryRumble(request)) sent = true;
        return sent;
    }

    public HostInputFrame BuildFrame(
        HostGamepadButtons externalButtons = HostGamepadButtons.None,
        float extLeftX = 0f, float extLeftY = 0f, float extRightX = 0f, float extRightY = 0f,
        float extTrigLeft = 0f, float extTrigRight = 0f)
    {
        HostGamepadButtons digital = externalButtons, nonGamepadButtons = HostGamepadButtons.None;
        float lx = extLeftX, ly = extLeftY, rx = extRightX, ry = extRightY, tl = extTrigLeft, tr = extTrigRight;
        uint gpMask = 0u;
        bool gpConnected = false;
        int touchCount = 0;

        foreach (IHostInputProvider provider in _providers)
        {
            HostExternalInputSnapshot ext = provider.Poll();
            gpMask |= ext.GamepadButtonsMask;
            if (ext.GamepadButtonsMask == 0) nonGamepadButtons |= ext.Buttons;
            if (ext.ControllerConnected) gpConnected = true;
            touchCount += ext.TouchActiveCount;
            if (MathF.Abs(ext.LeftX) > MathF.Abs(lx)) lx = ext.LeftX;
            if (MathF.Abs(ext.LeftY) > MathF.Abs(ly)) ly = ext.LeftY;
            if (MathF.Abs(ext.RightX) > MathF.Abs(rx)) rx = ext.RightX;
            if (MathF.Abs(ext.RightY) > MathF.Abs(ry)) ry = ext.RightY;
            if (ext.TriggerLeft > tl) tl = ext.TriggerLeft;
            if (ext.TriggerRight > tr) tr = ext.TriggerRight;
        }

        float wheel = _pendingWheelDelta;
        _registry.EvaluateFrame(_heldScancodes, _latchedCodes, _mouseButtons, wheel, gpMask);
        ApplyEvaluatedActions(ref digital, ref lx, ref ly, ref rx, ref ry, ref tl, ref tr, gpMask);
        digital |= nonGamepadButtons;

        uint hotkeys = _pendingHotkeys;
        float mdx = _pendingMouseDx, mdy = _pendingMouseDy;
        _pendingHotkeys = 0; _pendingMouseDx = 0f; _pendingMouseDy = 0f; _pendingWheelDelta = 0f;
        _latchedCodes.Clear();

        return new HostInputFrame(
            ++_frameSequence, digital, Math.Clamp(lx, -1f, 1f), Math.Clamp(ly, -1f, 1f),
            Math.Clamp(rx, -1f, 1f), Math.Clamp(ry, -1f, 1f), Math.Clamp(tl, 0f, 1f), Math.Clamp(tr, 0f, 1f),
            mdx, mdy, _mouseButtons, hotkeys, HostInputSchemas.ToShortCode(_activeSchema),
            wheel, gpMask, gpConnected, touchCount);
    }

    public static bool TryMapKeyNameToScancode(string? keyName, out int scancode)
    {
        scancode = 0;
        return !string.IsNullOrWhiteSpace(keyName) && KeyNameToScancode.TryGetValue(keyName.Trim(), out scancode);
    }

    private void ApplyEvaluatedActions(ref HostGamepadButtons digital, ref float lx, ref float ly, ref float rx, ref float ry, ref float tl, ref float tr, uint gpMask)
    {
        if (_registry.IsActionActive("moveUp")) { digital |= HostGamepadButtons.DPadUp; if (_registry.IsActionFromNonGamepad("moveUp")) ly = -1f; }
        if (_registry.IsActionActive("moveDown")) { digital |= HostGamepadButtons.DPadDown; if (_registry.IsActionFromNonGamepad("moveDown")) ly = 1f; }
        if (_registry.IsActionActive("moveLeft")) { digital |= HostGamepadButtons.DPadLeft; if (_registry.IsActionFromNonGamepad("moveLeft")) lx = -1f; }
        if (_registry.IsActionActive("moveRight")) { digital |= HostGamepadButtons.DPadRight; if (_registry.IsActionFromNonGamepad("moveRight")) lx = 1f; }

        foreach ((string actionId, HostGamepadButtons button) in ButtonActions)
        {
            if (_registry.IsActionActive(actionId)) digital |= button;
        }
        if (_registry.IsActionActive("camUp")) ry = -1f;
        if (_registry.IsActionActive("camDown")) ry = 1f;
        if (_registry.IsActionActive("camLeft")) rx = -1f;
        if (_registry.IsActionActive("camRight")) rx = 1f;

        if (_registry.IsActionActive("leftTrigger"))
        {
            digital |= HostGamepadButtons.LeftTriggerClick;
            if (tl < Sdl3GamepadProvider.TriggerThreshold || !Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadLT)) tl = 1f;
        }
        if (_registry.IsActionActive("rightTrigger"))
        {
            digital |= HostGamepadButtons.RightTriggerClick;
            if (tr < Sdl3GamepadProvider.TriggerThreshold || !Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadRT)) tr = 1f;
        }
    }
}
