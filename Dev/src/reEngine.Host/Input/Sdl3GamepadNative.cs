// @module: reEngine.Host.Input.Sdl3GamepadNative
// @purpose: Minimal SDL3 gamepad P/Invokes and native SDL3 library resolver scoped inside reEngine.Host.Input.
// @depends-on: System, System.IO, System.Reflection, System.Runtime.InteropServices
// @exports: Sdl3GamepadNative

using System.Reflection;
using System.Runtime.InteropServices;

namespace reEngine.Host.Input;

/// <summary>
/// Minimal SDL3 C-ABI gamepad bindings and native resolver without coupling to <c>Core.Sdl3Native</c>.
/// </summary>
internal static unsafe class Sdl3GamepadNative
{
    private const string Lib = "SDL3";
    private static readonly object ResolverSync = new();
    private static bool _resolverInstalled;

    public const uint InitGamepad = 0x2000U;

    public const int ButtonSouth = 0;
    public const int ButtonEast = 1;
    public const int ButtonWest = 2;
    public const int ButtonNorth = 3;
    public const int ButtonBack = 4;
    public const int ButtonGuide = 5;
    public const int ButtonStart = 6;
    public const int ButtonLeftStick = 7;
    public const int ButtonRightStick = 8;
    public const int ButtonLeftShoulder = 9;
    public const int ButtonRightShoulder = 10;
    public const int ButtonDPadUp = 11;
    public const int ButtonDPadDown = 12;
    public const int ButtonDPadLeft = 13;
    public const int ButtonDPadRight = 14;
    public const int ButtonMisc1 = 15;
    public const int ButtonRightPaddle1 = 16;
    public const int ButtonLeftPaddle1 = 17;
    public const int ButtonRightPaddle2 = 18;
    public const int ButtonLeftPaddle2 = 19;
    public const int ButtonTouchpad = 20;

    public const int AxisLeftX = 0;
    public const int AxisLeftY = 1;
    public const int AxisRightX = 2;
    public const int AxisRightY = 3;
    public const int AxisLeftTrigger = 4;
    public const int AxisRightTrigger = 5;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern uint SDL_WasInit(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void SDL_UpdateGamepads();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern uint* SDL_GetGamepads(out int count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr SDL_OpenGamepad(uint instanceId);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void SDL_CloseGamepad(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern byte SDL_GetGamepadButton(IntPtr gamepad, int button);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern short SDL_GetGamepadAxis(IntPtr gamepad, int axis);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern byte SDL_RumbleGamepad(IntPtr gamepad, ushort lowFrequencyRumble, ushort highFrequencyRumble, uint durationMs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern byte* SDL_GetGamepadName(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int SDL_GetGamepadType(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ushort SDL_GetGamepadVendor(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ushort SDL_GetGamepadProduct(IntPtr gamepad);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void SDL_free(void* mem);

    public static void InstallResolver()
    {
        lock (ResolverSync)
        {
            if (_resolverInstalled) return;
            _ = TryPreloadSdl3();
            try
            {
                // Keep Core.Sdl3Native's single-per-assembly resolver guard synchronized when called first.
                Core.Sdl3Native.InstallResolver();
            }
            catch (InvalidOperationException)
            {
                // Resolver already installed for this assembly.
            }

            try
            {
                NativeLibrary.SetDllImportResolver(typeof(Sdl3GamepadNative).Assembly, ResolveSdl3);
            }
            catch (InvalidOperationException)
            {
                // Assembly resolver already installed.
            }
            _resolverInstalled = true;
        }
    }

    public static bool TryIsGamepadSubsystemInitialized()
    {
        InstallResolver();
        try
        {
            return (SDL_WasInit(InitGamepad) & InitGamepad) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    public static string? ReadGamepadName(IntPtr gamepad)
    {
        if (gamepad == IntPtr.Zero) return null;
        byte* ptr = SDL_GetGamepadName(gamepad);
        return ptr == null ? null : Marshal.PtrToStringUTF8((IntPtr)ptr);
    }

    private static bool TryPreloadSdl3() =>
        ResolveSdl3(Lib, typeof(Sdl3GamepadNative).Assembly, null) != IntPtr.Zero;

    private static IntPtr ResolveSdl3(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Lib, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        string baseDir = AppContext.BaseDirectory;
        foreach (string candidate in GetCandidateLibraryPaths(baseDir))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle)) return handle;
        }
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr fallback) ? fallback : IntPtr.Zero;
    }

    private static IEnumerable<string> GetCandidateLibraryPaths(string baseDir)
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(baseDir, "SDL3.dll");
            yield return Path.Combine(baseDir, "runtimes", "win-x64", "native", "SDL3.dll");
            yield return Path.Combine(baseDir, "runtimes", "win-arm64", "native", "SDL3.dll");
            yield return Path.Combine(baseDir, "lib", "SDL3.dll");
            yield break;
        }
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(baseDir, "libSDL3.dylib");
            yield return Path.Combine(baseDir, "runtimes", "osx-arm64", "native", "libSDL3.dylib");
            yield return Path.Combine(baseDir, "runtimes", "osx-x64", "native", "libSDL3.dylib");
            yield break;
        }
        yield return Path.Combine(baseDir, "libSDL3.so");
        yield return Path.Combine(baseDir, "runtimes", "linux-x64", "native", "libSDL3.so");
        yield return Path.Combine(baseDir, "runtimes", "linux-arm64", "native", "libSDL3.so");
    }
}
