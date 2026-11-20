// @module: reEngine.Host.Input.HostMultiBindHelpers
// @purpose: Internal code/JSON/evaluation helpers keeping HostMultiBindRegistry concise and modular.
// @depends-on: System, System.Collections.Generic, System.Text.Json, reEngine.Host.Contracts
// @exports: HostMultiBindHelpers

using System.Text.Json;
using reEngine.Host.Contracts;

namespace reEngine.Host.Input;

internal static class HostMultiBindHelpers
{
    public sealed class BindingPayloadDto
    {
        public Dictionary<string, JsonElement>? ModernActionBindings { get; set; }
        public Dictionary<string, JsonElement>? ModernControllerBindings { get; set; }
        public Dictionary<string, JsonElement>? KeyBindings { get; set; }
    }

    public static void ReadCodesInto(JsonElement value, int[] dest, bool gamepadOnly)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int scalar))
        {
            dest[0] = gamepadOnly ? HostMultiBindRegistry.NormalizeGamepadCode(scalar) : HostMultiBindRegistry.NormalizeCode(scalar);
            return;
        }
        if (value.ValueKind != JsonValueKind.Array) return;
        int idx = 0;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (idx >= dest.Length) break;
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int code))
            {
                dest[idx] = gamepadOnly ? HostMultiBindRegistry.NormalizeGamepadCode(code) : HostMultiBindRegistry.NormalizeCode(code);
            }
            idx++;
        }
    }

    public static int[] TrimTrailingZeros(int[] source)
    {
        int last = -1;
        for (int i = source.Length - 1; i >= 0; i--) { if (source[i] != 0) { last = i; break; } }
        return last < 0 ? Array.Empty<int>() : source[..(last + 1)];
    }

    public static bool IsAnyCodeHeld(int[] slots, int[] gamepadCodes, IReadOnlySet<int> heldScancodes, int mouseButtons, float wheelDelta, uint gamepadMask)
    {
        for (int i = 0; i < 3; i++)
        {
            if (IsCodeHeld(slots[i], heldScancodes, mouseButtons, wheelDelta, gamepadMask) ||
                IsCodeHeld(gamepadCodes[i], heldScancodes, mouseButtons, wheelDelta, gamepadMask)) return true;
        }
        return false;
    }

    public static bool IsAnyCodeLatched(int[] slots, int[] gamepadCodes, IReadOnlySet<int> latchedCodes)
    {
        for (int i = 0; i < 3; i++)
        {
            if ((slots[i] > 0 && latchedCodes.Contains(slots[i])) ||
                (gamepadCodes[i] > 0 && latchedCodes.Contains(gamepadCodes[i]))) return true;
        }
        return false;
    }

    private static bool IsCodeHeld(int code, IReadOnlySet<int> heldScancodes, int mouseButtons, float wheelDelta, uint gamepadMask) => code switch
    {
        <= 0 => false,
        >= HostBindingCodes.MinScancode and <= HostBindingCodes.MaxScancode => heldScancodes.Contains(code),
        HostBindingCodes.MouseLeft => (mouseButtons & (1 << 0)) != 0,
        HostBindingCodes.MouseMiddle => (mouseButtons & (1 << 1)) != 0,
        HostBindingCodes.MouseRight => (mouseButtons & (1 << 2)) != 0,
        HostBindingCodes.Mouse4 => (mouseButtons & (1 << 3)) != 0,
        HostBindingCodes.Mouse5 => (mouseButtons & (1 << 4)) != 0,
        HostBindingCodes.WheelUp => wheelDelta > 0f,
        HostBindingCodes.WheelDown => wheelDelta < 0f,
        _ => Sdl3GamepadProvider.IsGamepadCodePressed(gamepadMask, code)
    };
}
