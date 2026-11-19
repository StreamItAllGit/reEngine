// @module: reEngine.Host.Core.NativeAudioDriverSelector
// @purpose: Selects native OS audio drivers (wasapi/pipewire/alsa/coreaudio/aaudio/emscripten) and applies SDL_AUDIO_DRIVER before InitAudio.
// @depends-on: System, System.IO, System.Runtime.InteropServices
// @exports: NativeAudioDriverSelector

using System.Runtime.InteropServices;

namespace reEngine.Host.Core;

/// <summary>
/// Selects and pins the optimal native OS audio backend prior to <c>SDL_InitSubSystem(SDL_INIT_AUDIO)</c>.
/// </summary>
public static class NativeAudioDriverSelector
{
    public const string SdlAudioDriverHint = "SDL_AUDIO_DRIVER";

    public static string SelectBestDriver(string? audioDriverOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(audioDriverOverride))
        {
            string trimmed = audioDriverOverride.Trim().ToLowerInvariant();
            if (!string.Equals(trimmed, "auto", StringComparison.Ordinal)) return trimmed;
        }

        if (OperatingSystem.IsWindows()) return "wasapi";
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS()) return "coreaudio";
        if (OperatingSystem.IsAndroid()) return "aaudio";
        if (OperatingSystem.IsBrowser() || RuntimeInformation.OSArchitecture == Architecture.Wasm) return "emscripten";
        if (OperatingSystem.IsLinux()) return HasPipeWireSocket() ? "pipewire" : "alsa";
        return "dummy";
    }

    public static string ApplyDriverHint(string? audioDriverOverride = null)
    {
        string driver = SelectBestDriver(audioDriverOverride);
        Sdl3Native.InstallResolver();
        try
        {
            _ = Sdl3Native.SetHintUtf8(SdlAudioDriverHint, driver);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            System.Diagnostics.Trace.WriteLine($"SDL_SetHint({SdlAudioDriverHint}={driver}) skipped: {ex.Message}");
        }
        return driver;
    }

    internal static bool TryInitializeAudioSubsystem(
        string? audioDriverOverride,
        out string selectedDriver,
        out string? activeDriver,
        out string diagnostic)
    {
        selectedDriver = ApplyDriverHint(audioDriverOverride);
        if (!Sdl3Native.TryInitSubSystem(Sdl3Native.InitAudio, out string initError))
        {
            activeDriver = null;
            diagnostic = $"Audio subsystem disabled ({selectedDriver}: {initError})";
            return false;
        }

        activeDriver = Sdl3Native.GetCurrentAudioDriverString();
        if (string.IsNullOrWhiteSpace(activeDriver)) activeDriver = selectedDriver;
        diagnostic = $"audio={activeDriver}";
        return true;
    }

    private static bool HasPipeWireSocket()
    {
        string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(runtimeDir) && File.Exists(Path.Combine(runtimeDir, "pipewire-0"));
    }
}
