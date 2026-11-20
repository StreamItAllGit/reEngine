// @module: reEngine.Host.Input.HostInputEvaluationHelper
// @purpose: Internal provider polling, action-to-gamepad application, and Standard 33 Menu navigation isolation helpers for HostInputMapper.
// @depends-on: System, System.Collections.Generic, reEngine.Host.Contracts
// @exports: HostInputEvaluationHelper

using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

internal static class HostInputEvaluationHelper
{
    private const float MenuStickThreshold = 0.50f;

    private static readonly (string ActionId, HostGamepadButtons Button)[] ButtonActions =
    [
        ("south", HostGamepadButtons.South), ("east", HostGamepadButtons.East),
        ("west", HostGamepadButtons.West), ("north", HostGamepadButtons.North),
        ("leftShoulder", HostGamepadButtons.LeftShoulder), ("rightShoulder", HostGamepadButtons.RightShoulder),
        ("start", HostGamepadButtons.Start), ("select", HostGamepadButtons.Select),
        ("leftStickClick", HostGamepadButtons.LeftStickClick), ("rightStickClick", HostGamepadButtons.RightStickClick)
    ];

    public static HostGamepadPortSnapshot[] PollProviders(
        IReadOnlyList<IHostInputProvider> providers,
        ref HostGamepadButtons nonGamepadButtons, ref float lx, ref float ly, ref float rx, ref float ry,
        ref float tl, ref float tr, ref uint gpMask, ref bool gpConnected, ref int touchCount)
    {
        var padPorts = new HostGamepadPortSnapshot[Sdl3GamepadProvider.MaxPorts];
        for (int i = 0; i < padPorts.Length; i++)
        {
            padPorts[i] = new HostGamepadPortSnapshot(i, false, HostGamepadButtons.None, 0f, 0f, 0f, 0f, 0f, 0f, 0u);
        }

        bool port0Filled = false;
        foreach (IHostInputProvider provider in providers)
        {
            HostExternalInputSnapshot ext;
            if (provider is Sdl3GamepadProvider sdl)
            {
                IReadOnlyList<HostGamepadPortSnapshot> polled = sdl.PollAllPorts();
                for (int p = 0; p < Math.Min(padPorts.Length, polled.Count); p++)
                {
                    if (polled[p].IsConnected) padPorts[p] = polled[p];
                }
                if (polled.Count == 0 || !polled[0].IsConnected) continue;
                HostGamepadPortSnapshot p0 = polled[0];
                ext = new HostExternalInputSnapshot(p0.Buttons, p0.LeftX, p0.LeftY, p0.RightX, p0.RightY, p0.TriggerLeft, p0.TriggerRight, p0.RawButtonsMask, true, 0);
                port0Filled = true;
            }
            else
            {
                ext = provider.Poll();
                if (ext.ControllerConnected && !port0Filled)
                {
                    padPorts[0] = new HostGamepadPortSnapshot(0, true, ext.Buttons, ext.LeftX, ext.LeftY, ext.RightX, ext.RightY, ext.TriggerLeft, ext.TriggerRight, ext.GamepadButtonsMask);
                    port0Filled = true;
                }
            }

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
        return padPorts;
    }

    public static void ApplyEvaluatedActions(
        HostMultiBindRegistry bindings, ref HostGamepadButtons digital,
        ref float lx, ref float ly, ref float rx, ref float ry, ref float tl, ref float tr, uint gpMask)
    {
        if (bindings.IsActionActive("moveUp")) { digital |= HostGamepadButtons.DPadUp; if (bindings.IsActionFromNonGamepad("moveUp")) ly = -1f; }
        if (bindings.IsActionActive("moveDown")) { digital |= HostGamepadButtons.DPadDown; if (bindings.IsActionFromNonGamepad("moveDown")) ly = 1f; }
        if (bindings.IsActionActive("moveLeft")) { digital |= HostGamepadButtons.DPadLeft; if (bindings.IsActionFromNonGamepad("moveLeft")) lx = -1f; }
        if (bindings.IsActionActive("moveRight")) { digital |= HostGamepadButtons.DPadRight; if (bindings.IsActionFromNonGamepad("moveRight")) lx = 1f; }

        foreach ((string actionId, HostGamepadButtons button) in ButtonActions)
        {
            if (bindings.IsActionActive(actionId)) digital |= button;
        }
        if (bindings.IsActionActive("camUp")) ry = -1f;
        if (bindings.IsActionActive("camDown")) ry = 1f;
        if (bindings.IsActionActive("camLeft")) rx = -1f;
        if (bindings.IsActionActive("camRight")) rx = 1f;

        if (bindings.IsActionActive("leftTrigger"))
        {
            digital |= HostGamepadButtons.LeftTriggerClick;
            if (tl < Sdl3GamepadProvider.TriggerThreshold || !Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadLT)) tl = 1f;
        }
        if (bindings.IsActionActive("rightTrigger"))
        {
            digital |= HostGamepadButtons.RightTriggerClick;
            if (tr < Sdl3GamepadProvider.TriggerThreshold || !Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadRT)) tr = 1f;
        }
    }

    public static void ApplyMenuNavigationIsolation(
        ref HostGamepadButtons digital, uint gpMask, HostGamepadPortSnapshot[] padPorts, float lx, float ly,
        IReadOnlySet<int> heldScancodes, IReadOnlySet<int> latchedCodes)
    {
        bool IsKeyActive(int code) => heldScancodes.Contains(code) || latchedCodes.Contains(code);
        if (Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadDPadUp) || IsKeyActive(HostScancodes.Up) || IsKeyActive(HostScancodes.W)) digital |= HostGamepadButtons.DPadUp;
        if (Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadDPadDown) || IsKeyActive(HostScancodes.Down) || IsKeyActive(HostScancodes.S)) digital |= HostGamepadButtons.DPadDown;
        if (Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadDPadLeft) || IsKeyActive(HostScancodes.Left) || IsKeyActive(HostScancodes.A)) digital |= HostGamepadButtons.DPadLeft;
        if (Sdl3GamepadProvider.IsGamepadCodePressed(gpMask, HostBindingCodes.GamepadDPadRight) || IsKeyActive(HostScancodes.Right) || IsKeyActive(HostScancodes.D)) digital |= HostGamepadButtons.DPadRight;

        float sx = padPorts[0].IsConnected && MathF.Abs(padPorts[0].LeftX) > MathF.Abs(lx) ? padPorts[0].LeftX : lx;
        float sy = padPorts[0].IsConnected && MathF.Abs(padPorts[0].LeftY) > MathF.Abs(ly) ? padPorts[0].LeftY : ly;
        if (sx <= -MenuStickThreshold) digital |= HostGamepadButtons.DPadLeft;
        if (sx >= MenuStickThreshold) digital |= HostGamepadButtons.DPadRight;
        if (sy <= -MenuStickThreshold) digital |= HostGamepadButtons.DPadUp;
        if (sy >= MenuStickThreshold) digital |= HostGamepadButtons.DPadDown;
    }
}
