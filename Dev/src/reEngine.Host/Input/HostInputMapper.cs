// @module: reEngine.Host.Input.HostInputMapper
// @purpose: Translates keyboard scancodes, mouse/wheel, 3-tier multi-bind actions, 4-port gamepads, and hotkeys into HostInputFrame.
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
    /// <summary>No hotkeys asserted on this frame.</summary>
    None = 0,

    /// <summary>Toggles the <c>F1</c> settings drawer.</summary>
    ToggleDrawer = 1u << 0,

    /// <summary>Toggles the <c>F3</c> telemetry HUD overlay.</summary>
    ToggleHud = 1u << 1,

    /// <summary>Triggers a diagnostic frame dump (<c>F12</c>).</summary>
    DumpFrame = 1u << 2,

    /// <summary>Context-aware <c>Escape</c> key press.</summary>
    Escape = 1u << 3,

    /// <summary>Requests a VBlank-safe quick save (<c>F5</c>).</summary>
    QuickSave = 1u << 4,

    /// <summary>Requests a VBlank-safe quick load (<c>F9</c>).</summary>
    QuickLoad = 1u << 5,

    /// <summary>Toggles fullscreen presentation (<c>F11</c> or <c>Alt+Enter</c>).</summary>
    ToggleFullscreen = 1u << 6,

    /// <summary>Signals fast-forward toggle/hold edge (<c>F6</c> or <c>Grave</c>).</summary>
    FastForward = 1u << 7,

    /// <summary>Requests skipping the active cutscene (<c>F7</c>).</summary>
    CutsceneSkip = 1u << 8,

    /// <summary>Triggers clean/filtered <c>F12</c> PNG screenshot capture.</summary>
    TakeScreenshot = 1u << 9
}

/// <summary>
/// Maps raw keyboard scancodes, synthetic keys, mouse/wheel, 3-tier multi-binds, and 4-port gamepad/touch providers
/// into immutable non-ref <see cref="HostInputFrame"/> snapshots with repeat suppression and Menu isolation.
/// </summary>
public sealed class HostInputMapper
{
    private readonly HashSet<int> _heldScancodes = new();
    private readonly HashSet<int> _altSuppressedScancodes = new();
    private readonly HashSet<int> _latchedCodes = new();
    private readonly List<IHostInputProvider> _providers = new();
    private readonly HostControlTierStore _controlTiers = new(HostInputSchemaKind.SchemaB);
    private HostInputSchemaKind _activeSchema = HostInputSchemaKind.SchemaB;
    private long _frameSequence;
    private uint _pendingHotkeys;
    private float _pendingMouseDx, _pendingMouseDy, _pendingWheelDelta;
    private float _pointerContentX = -1f, _pointerContentY = -1f;
    private bool _isPointerInsideContent;
    private int _mouseButtons;

    /// <summary>Gets or sets the active input schema preset for the active control tier.</summary>
    public HostInputSchemaKind ActiveSchema
    {
        get => _activeSchema;
        set { if (_activeSchema != value) { _activeSchema = value; _controlTiers.ActiveSchema = value; _controlTiers.ActiveRegistry.ResetToSchemaDefaults(value); } }
    }

    /// <summary>Gets or sets whether a host or game menu is currently open, isolating Menu D-Pad/Left Stick navigation.</summary>
    public bool IsMenuOpen { get; set; }

    /// <summary>Gets or sets whether latched fast-forward mode is toggled on.</summary>
    public bool IsFastForwardToggled { get; set; }

    /// <summary>Gets the Standard 33 three-tier control binding store.</summary>
    public HostControlTierStore ControlTiers => _controlTiers;

    /// <summary>Gets or sets the active Standard 33 control tier (<c>globalController</c>, <c>perGameController</c>, or <c>perGameModern</c>).</summary>
    public HostControlTierMode ActiveControlTier { get => _controlTiers.ActiveTier; set => _controlTiers.ActiveTier = value; }

    /// <summary>Gets the active tier's multi-bind registry.</summary>
    public HostMultiBindRegistry Bindings => _controlTiers.ActiveRegistry;

    /// <summary>Gets the active semantic action identifiers for the current frame.</summary>
    public IReadOnlySet<string> ActiveActions => Bindings.ActiveActions;

    /// <summary>Gets the newly pressed semantic action identifiers for the current frame.</summary>
    public IReadOnlySet<string> JustPressedActions => Bindings.JustPressedActions;

    /// <summary>Sets <see cref="IsMenuOpen"/> explicitly for Standard 33 Menu/Free-Roam context separation.</summary>
    public void SetMenuOpen(bool isMenuOpen) => IsMenuOpen = isMenuOpen;

    /// <summary>Updates normalized content-space pointer coordinates (<c>[0, 1]</c>) mapped from window coordinates.</summary>
    public void SetPointerContentPosition(float normalizedX, float normalizedY, bool isInsideContent)
    {
        _isPointerInsideContent = isInsideContent;
        _pointerContentX = isInsideContent ? (float.IsFinite(normalizedX) ? Math.Clamp(normalizedX, 0f, 1f) : 0f) : (float.IsFinite(normalizedX) ? normalizedX : -1f);
        _pointerContentY = isInsideContent ? (float.IsFinite(normalizedY) ? Math.Clamp(normalizedY, 0f, 1f) : 0f) : (float.IsFinite(normalizedY) ? normalizedY : -1f);
    }

    /// <summary>Returns whether <paramref name="actionId"/> was active on the most recently built frame.</summary>
    public bool IsActionActive(string actionId) => Bindings.IsActionActive(actionId);

    /// <summary>Returns whether <paramref name="actionId"/> transitioned to active on the most recently built frame.</summary>
    public bool IsActionJustPressed(string actionId) => Bindings.IsActionJustPressed(actionId);

    /// <summary>Updates raw keyboard scancode state with repeat filtering enabled.</summary>
    public void SetScancodeState(int scancode, bool isPressed) => SetScancodeState(scancode, isPressed, isRepeat: false);

    /// <summary>Updates raw keyboard scancode state with repeat suppression and edge-triggered hotkey detection.</summary>
    public void SetScancodeState(int scancode, bool isPressed, bool isRepeat)
    {
        if (isRepeat) return;
        if (!isPressed) { _heldScancodes.Remove(scancode); _altSuppressedScancodes.Remove(scancode); return; }
        if (!_heldScancodes.Add(scancode)) return;

        bool altHeld = _heldScancodes.Contains(HostScancodes.LAlt) || _heldScancodes.Contains(HostScancodes.RAlt);
        if (altHeld && scancode == HostScancodes.Return)
        {
            _pendingHotkeys |= (uint)HostHotkeyFlags.ToggleFullscreen;
            _altSuppressedScancodes.Add(scancode);
            return;
        }

        if (HostMultiBindRegistry.IsKeyboardCode(scancode)) _latchedCodes.Add(scancode);
        _pendingHotkeys |= scancode switch
        {
            HostScancodes.F1 => (uint)HostHotkeyFlags.ToggleDrawer,
            HostScancodes.F3 => (uint)HostHotkeyFlags.ToggleHud,
            HostScancodes.F5 => (uint)HostHotkeyFlags.QuickSave,
            HostScancodes.F6 or HostScancodes.Grave => (uint)HostHotkeyFlags.FastForward,
            HostScancodes.F7 => (uint)HostHotkeyFlags.CutsceneSkip,
            HostScancodes.F9 => (uint)HostHotkeyFlags.QuickLoad,
            HostScancodes.F11 => (uint)HostHotkeyFlags.ToggleFullscreen,
            HostScancodes.F12 => (uint)(HostHotkeyFlags.DumpFrame | HostHotkeyFlags.TakeScreenshot),
            HostScancodes.Escape => (uint)HostHotkeyFlags.Escape,
            _ => 0u
        };
    }

    /// <summary>Injects a synthetic single-key tap by key name (e.g., <c>"F1"</c>, <c>"Enter"</c>).</summary>
    public void InjectSyntheticKey(string? keyName) => InjectSyntheticKey(keyName, modifiers: null);

    /// <summary>Injects a synthetic key tap with optional modifiers (e.g., <c>["Alt"]</c> + <c>"Enter"</c>).</summary>
    public void InjectSyntheticKey(string? keyName, IReadOnlyList<string>? modifiers)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return;
        string trimmed = keyName.Trim();
        List<int>? tempMods = null;
        if (modifiers is null && trimmed.Contains('+', StringComparison.Ordinal))
        {
            string[] parts = trimmed.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2) { trimmed = parts[^1]; tempMods = ApplyTemporaryModifiers(parts[..^1]); }
        }
        else if (modifiers is { Count: > 0 }) tempMods = ApplyTemporaryModifiers(modifiers);

        bool hasAlt = _heldScancodes.Contains(HostScancodes.LAlt) || _heldScancodes.Contains(HostScancodes.RAlt);
        if (TryMapKeyNameToScancode(trimmed, out int scancode))
        {
            SetScancodeState(scancode, true, isRepeat: false);
            if (IsInstantHotkeyScancode(scancode) || (hasAlt && scancode == HostScancodes.Return))
            {
                _heldScancodes.Remove(scancode);
                _altSuppressedScancodes.Remove(scancode);
            }
        }
        if (tempMods is not null) foreach (int m in tempMods) _heldScancodes.Remove(m);
    }

    /// <summary>Accumulates floating-point relative mouse motion and updates mouse button latch state.</summary>
    public void AccumulateMouse(float dx, float dy, int buttons)
    {
        _pendingMouseDx += dx; _pendingMouseDy += dy;
        int newlyPressed = buttons & ~_mouseButtons;
        for (int b = 1; b <= 5; b++) if ((newlyPressed & (1 << (b - 1))) != 0 && HostMultiBindRegistry.MouseButtonIndexToBindingCode(b) is int code and > 0) _latchedCodes.Add(code);
        _mouseButtons = buttons;
    }

    /// <summary>Accumulates vertical scroll wheel deltas for the current frame.</summary>
    public void AccumulateMouseWheel(float deltaY)
    {
        _pendingWheelDelta += deltaY;
        if (deltaY > 0f) _latchedCodes.Add(HostBindingCodes.WheelUp);
        else if (deltaY < 0f) _latchedCodes.Add(HostBindingCodes.WheelDown);
    }

    /// <summary>Sets a single mouse button state (<c>1..5</c>).</summary>
    public void SetMouseButton(int buttonIndex, bool isPressed)
    {
        if (buttonIndex is < 1 or > 5) return;
        int mask = 1 << (buttonIndex - 1);
        if (!isPressed) { _mouseButtons &= ~mask; return; }
        bool wasDown = (_mouseButtons & mask) != 0;
        _mouseButtons |= mask;
        if (!wasDown && HostMultiBindRegistry.MouseButtonIndexToBindingCode(buttonIndex) is int code and > 0) _latchedCodes.Add(code);
    }

    /// <summary>Registers an external gamepad or touch input provider.</summary>
    public void RegisterProvider(IHostInputProvider provider) { ArgumentNullException.ThrowIfNull(provider); if (!_providers.Contains(provider)) _providers.Add(provider); }

    /// <summary>Unregisters an external input provider.</summary>
    public bool UnregisterProvider(IHostInputProvider provider) => _providers.Remove(provider);

    internal bool TryConsumeFirstLatchedCode(out int code)
    {
        foreach (int candidate in _latchedCodes) { _latchedCodes.Remove(candidate); code = candidate; return true; }
        code = 0; return false;
    }

    /// <summary>Requests haptic rumble across registered input providers (Port 0 default).</summary>
    public bool RequestRumble(HostRumbleRequest request) { bool sent = false; foreach (IHostInputProvider p in _providers) if (p.TryRumble(request)) sent = true; return sent; }

    /// <summary>Requests haptic rumble for a specific zero-based controller port (<c>0..3</c>).</summary>
    public bool RequestRumble(int portIndex, HostRumbleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool sent = false;
        foreach (IHostInputProvider p in _providers)
        {
            if (p is Sdl3GamepadProvider sdl && sdl.TryRumble(portIndex, request)) sent = true;
            else if (portIndex == 0 && p.TryRumble(request)) sent = true;
        }
        return sent;
    }

    /// <summary>Evaluates all inputs, 4-port SDL3 gamepads, Menu isolation, and hotkeys into an immutable <see cref="HostInputFrame"/>.</summary>
    public HostInputFrame BuildFrame(
        HostGamepadButtons externalButtons = HostGamepadButtons.None,
        float extLeftX = 0f, float extLeftY = 0f, float extRightX = 0f, float extRightY = 0f, float extTrigLeft = 0f, float extTrigRight = 0f)
    {
        HostGamepadButtons digital = externalButtons, nonGamepadButtons = HostGamepadButtons.None;
        float lx = extLeftX, ly = extLeftY, rx = extRightX, ry = extRightY, tl = extTrigLeft, tr = extTrigRight;
        uint gpMask = 0u; bool gpConnected = false; int touchCount = 0;
        HostGamepadPortSnapshot[] padPorts = HostInputEvaluationHelper.PollProviders(_providers, ref nonGamepadButtons, ref lx, ref ly, ref rx, ref ry, ref tl, ref tr, ref gpMask, ref gpConnected, ref touchCount);

        float wheel = _pendingWheelDelta;
        IReadOnlySet<int> heldForEval = _altSuppressedScancodes.Count == 0 ? _heldScancodes : BuildUnsuppressedHeld();
        Bindings.EvaluateFrame(heldForEval, _latchedCodes, _mouseButtons, wheel, gpMask, isolateMenuDpad: IsMenuOpen);
        HostInputEvaluationHelper.ApplyEvaluatedActions(Bindings, ref digital, ref lx, ref ly, ref rx, ref ry, ref tl, ref tr, gpMask);
        digital |= nonGamepadButtons;
        if (IsMenuOpen) HostInputEvaluationHelper.ApplyMenuNavigationIsolation(ref digital, gpMask, padPorts, lx, ly, heldForEval, _latchedCodes);

        uint hotkeys = _pendingHotkeys;
        bool fastForwardHeld = IsFastForwardToggled || _heldScancodes.Contains(HostScancodes.Grave) || _heldScancodes.Contains(HostScancodes.F6) || Bindings.IsActionActive("fastForward");
        bool cutsceneSkipPressed = (hotkeys & (uint)HostHotkeyFlags.CutsceneSkip) != 0 || Bindings.IsActionJustPressed("cutsceneSkip");
        if (Bindings.IsActionJustPressed("cutsceneSkip")) hotkeys |= (uint)HostHotkeyFlags.CutsceneSkip;

        float mdx = _pendingMouseDx, mdy = _pendingMouseDy;
        _pendingHotkeys = 0; _pendingMouseDx = 0f; _pendingMouseDy = 0f; _pendingWheelDelta = 0f;
        _latchedCodes.Clear();

        return new HostInputFrame(
            ++_frameSequence, digital, Math.Clamp(lx, -1f, 1f), Math.Clamp(ly, -1f, 1f), Math.Clamp(rx, -1f, 1f), Math.Clamp(ry, -1f, 1f),
            Math.Clamp(tl, 0f, 1f), Math.Clamp(tr, 0f, 1f), mdx, mdy, _mouseButtons, hotkeys, HostInputSchemas.ToShortCode(_activeSchema),
            wheel, gpMask, gpConnected, touchCount, padPorts, _pointerContentX, _pointerContentY, _isPointerInsideContent, fastForwardHeld, cutsceneSkipPressed);
    }

    /// <summary>Maps a textual key name (e.g., <c>"F5"</c>, <c>"Grave"</c>, <c>"Enter"</c>) to an SDL3 scancode.</summary>
    public static bool TryMapKeyNameToScancode(string? keyName, out int scancode) => HostInputSchemas.TryMapKeyNameToScancode(keyName, out scancode);

    private List<int>? ApplyTemporaryModifiers(IEnumerable<string> modifiers)
    {
        List<int>? added = null;
        foreach (string mod in modifiers) if (TryMapKeyNameToScancode(mod, out int c) && _heldScancodes.Add(c)) { added ??= new(2); added.Add(c); }
        return added;
    }

    private HashSet<int> BuildUnsuppressedHeld() { var copy = new HashSet<int>(_heldScancodes); foreach (int s in _altSuppressedScancodes) copy.Remove(s); return copy; }

    private static bool IsInstantHotkeyScancode(int scancode) => scancode is
        HostScancodes.F1 or HostScancodes.F3 or HostScancodes.F5 or HostScancodes.F6 or
        HostScancodes.F7 or HostScancodes.F9 or HostScancodes.F11 or HostScancodes.F12 or
        HostScancodes.Escape or HostScancodes.Grave;
}
