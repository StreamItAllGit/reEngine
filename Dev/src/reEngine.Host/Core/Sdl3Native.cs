// @module: reEngine.Host.Core.Sdl3Native
// @purpose: Minimal SDL3 C-ABI P/Invokes and native resolver for windowing, textures, audio streams, input events, and OpenGL context helpers.
// @depends-on: System, System.Reflection, System.Runtime.InteropServices, System.Text
// @exports: SdlAudioSpec, SdlFRect, Sdl3Native

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace reEngine.Host.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct SdlAudioSpec
{
    public uint Format;
    public int Channels;
    public int Freq;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlFRect
{
    public float X;
    public float Y;
    public float W;
    public float H;
}

internal static unsafe class Sdl3Native
{
    private const string Lib = "SDL3";
    private static readonly object ResolverSync = new();
    private static bool _resolverInstalled;

    public const uint InitAudio = 0x10U;
    public const uint InitVideo = 0x20U;
    public const uint InitGamepad = 0x2000U;
    public const uint PixelFormatAbgr8888 = 0x16762004U;
    public const uint AudioS16Le = 0x8010U;
    public const uint DefaultAudioPlaybackDevice = 0xFFFFFFFFU;
    public const int TextureAccessStreaming = 1;
    public const int ScaleModeNearest = 0;

    public const ulong WindowOpenGl = 0x02UL;
    public const ulong WindowHidden = 0x08UL;
    public const ulong WindowResizable = 0x20UL;

    public const uint EventQuit = 0x100U;
    public const uint EventWindowCloseRequested = 0x203U;
    public const uint EventKeyDown = 0x300U;
    public const uint EventKeyUp = 0x301U;
    public const uint EventMouseMotion = 0x400U;
    public const uint EventMouseButtonDown = 0x401U;
    public const uint EventMouseButtonUp = 0x402U;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_Init(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte* SDL_GetError();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateWindow(byte* title, int w, int h, ulong flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_ShowWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_RaiseWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetWindowSize(IntPtr window, out int w, out int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetPointerProperty(uint props, byte* name, IntPtr defaultValue);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateRenderer(IntPtr window, byte* name);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyRenderer(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateTexture(IntPtr renderer, uint format, int access, int w, int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyTexture(IntPtr texture);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_UpdateTexture(IntPtr texture, IntPtr rect, void* pixels, int pitch);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetTextureScaleMode(IntPtr texture, int scaleMode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetRenderDrawColor(IntPtr renderer, byte r, byte g, byte b, byte a);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_RenderClear(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_RenderTexture(IntPtr renderer, IntPtr texture, SdlFRect* src, SdlFRect* dst);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_RenderPresent(IntPtr renderer);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetRenderVSync(IntPtr renderer, int vsync);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_PollEvent(byte* ev);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_OpenAudioDeviceStream(uint devid, SdlAudioSpec* spec, IntPtr callback, IntPtr userdata);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_ResumeAudioStreamDevice(IntPtr stream);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_PutAudioStreamData(IntPtr stream, void* buf, int len);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetAudioStreamQueued(IntPtr stream);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyAudioStream(IntPtr stream);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_SetAttribute(int attr, int value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GL_CreateContext(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_MakeCurrent(IntPtr window, IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_SwapWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_DestroyContext(IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GL_GetProcAddress(byte* proc);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int SetForegroundWindow(IntPtr hWnd);

    public static void InstallResolver()
    {
        lock (ResolverSync)
        {
            if (_resolverInstalled) return;
            NativeLibrary.SetDllImportResolver(typeof(Sdl3Native).Assembly, ResolveSdl3);
            _resolverInstalled = true;
        }
    }

    public static bool TryInit(uint flags, out string error)
    {
        try
        {
            if (SDL_Init(flags) != 0)
            {
                error = string.Empty;
                return true;
            }
            error = GetErrorString();
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public static string GetErrorString()
    {
        byte* p = SDL_GetError();
        return p == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)p) ?? string.Empty;
    }

    public static IntPtr CreateWindowUtf8(string title, int w, int h, ulong flags)
    {
        byte[] bytes = Encoding.UTF8.GetBytes((title ?? "reEngine") + "\0");
        fixed (byte* ptr = bytes) return SDL_CreateWindow(ptr, w, h, flags);
    }

    public static void BringToForeground(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        _ = SDL_ShowWindow(window);
        _ = SDL_RaiseWindow(window);
        if (!OperatingSystem.IsWindows()) return;

        uint props = SDL_GetWindowProperties(window);
        if (props == 0) return;
        byte[] name = Encoding.UTF8.GetBytes("SDL.window.win32.hwnd\0");
        IntPtr hwnd;
        fixed (byte* p = name) hwnd = SDL_GetPointerProperty(props, p, IntPtr.Zero);
        if (hwnd != IntPtr.Zero) _ = SetForegroundWindow(hwnd);
    }

    public static uint EventType(byte* ev) => *(uint*)ev;
    public static int EventScancode(byte* ev) => *(int*)(ev + 24);
    public static byte EventMouseButton(byte* ev) => *(ev + 24);
    public static (float X, float Y) EventMousePosition(byte* ev) => (*(float*)(ev + 28), *(float*)(ev + 32));
    public static (float Dx, float Dy) EventMouseRelative(byte* ev) => (*(float*)(ev + 36), *(float*)(ev + 40));

    private static IntPtr ResolveSdl3(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Lib, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        string baseDir = AppContext.BaseDirectory;
        foreach (string candidate in new[]
        {
            Path.Combine(baseDir, "SDL3.dll"),
            Path.Combine(baseDir, "runtimes", "win-x64", "native", "SDL3.dll"),
            Path.Combine(baseDir, "lib", "SDL3.dll")
        })
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle)) return handle;
        }
        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out IntPtr fallback) ? fallback : IntPtr.Zero;
    }
}
