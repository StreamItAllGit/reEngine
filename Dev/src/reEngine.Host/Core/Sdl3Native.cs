// @module: reEngine.Host.Core.Sdl3Native
// @purpose: Minimal SDL3 C-ABI P/Invokes and native resolver for windowing, textures, audio streams, input events, GL33, and Vulkan surfaces.
// @depends-on: System, System.Reflection, System.Runtime.InteropServices, System.Text
// @exports: SdlAudioSpec, SdlFRect, Sdl3Native

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using reEngine.Host.Contracts;

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

[StructLayout(LayoutKind.Sequential)]
internal struct SdlRect
{
    public int X;
    public int Y;
    public int W;
    public int H;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SdlDisplayMode
{
    public uint DisplayId;
    public uint Format;
    public int W;
    public int H;
    public float PixelDensity;
    public float RefreshRate;
    public int RefreshRateNumerator;
    public int RefreshRateDenominator;
    public IntPtr Internal;
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

    public const ulong WindowFullscreen = 0x01UL;
    public const ulong WindowOpenGl = 0x02UL;
    public const ulong WindowHidden = 0x08UL;
    public const ulong WindowBorderless = 0x10UL;
    public const ulong WindowResizable = 0x20UL;
    public const ulong WindowVulkan = 0x10000000UL;

    public const int GlRedSize = 0;
    public const int GlGreenSize = 1;
    public const int GlBlueSize = 2;
    public const int GlAlphaSize = 3;
    public const int GlDoubleBuffer = 5;
    public const int GlContextMajorVersion = 17;
    public const int GlContextMinorVersion = 18;
    public const int GlContextFlags = 19;
    public const int GlContextProfileMask = 20;
    public const int GlContextProfileCore = 1;

    public const uint EventQuit = 0x100U;
    public const uint EventWindowFocusGained = 0x20EU;
    public const uint EventWindowFocusLost = 0x20FU;
    public const uint EventWindowCloseRequested = 0x210U;
    public const uint EventKeyDown = 0x300U;
    public const uint EventKeyUp = 0x301U;
    public const uint EventMouseMotion = 0x400U;
    public const uint EventMouseButtonDown = 0x401U;
    public const uint EventMouseButtonUp = 0x402U;
    public const uint EventMouseWheel = 0x403U;
    public const uint EventDropFile = 0x1000U;

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_Init(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_InitSubSystem(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_WasInit(uint flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Quit();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetHint(byte* name, byte* value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte* SDL_GetError();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte* SDL_GetCurrentAudioDriver();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetNumAudioDrivers();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte* SDL_GetAudioDriver(int index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_CreateWindow(byte* title, int w, int h, ulong flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_DestroyWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_ShowWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_RaiseWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetWindowFullscreen(IntPtr window, byte fullscreen);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetWindowFullscreenMode(IntPtr window, SdlDisplayMode* mode);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetWindowPosition(IntPtr window, int x, int y);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetWindowBordered(IntPtr window, byte bordered);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_SetWindowRelativeMouseMode(IntPtr window, byte enabled);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetWindowRelativeMouseMode(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint* SDL_GetDisplays(out int count);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint SDL_GetPrimaryDisplay();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetDisplayBounds(uint displayId, SdlRect* rect);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte* SDL_GetDisplayName(uint displayId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern SdlDisplayMode* SDL_GetCurrentDisplayMode(uint displayId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_free(IntPtr mem);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetWindowSize(IntPtr window, out int w, out int h);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GetWindowSizeInPixels(IntPtr window, out int w, out int h);
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
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GL_ResetAttributes();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_SetAttribute(int attr, int value);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GL_CreateContext(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_MakeCurrent(IntPtr window, IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_SetSwapInterval(int interval);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_SwapWindow(IntPtr window);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GL_DestroyContext(IntPtr context);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GL_GetProcAddress(byte* proc);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_Vulkan_LoadLibrary(byte* path);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Vulkan_UnloadLibrary();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_Vulkan_GetVkGetInstanceProcAddr();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte** SDL_Vulkan_GetInstanceExtensions(out uint count);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_Vulkan_CreateSurface(IntPtr window, IntPtr instance, IntPtr allocator, out ulong surface);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_Vulkan_DestroySurface(IntPtr instance, ulong surface, IntPtr allocator);
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

    public static bool TryInit(uint flags, out string error) => TryInitCore(() => SDL_Init(flags), out error);
    public static bool TryInitSubSystem(uint flags, out string error) => TryInitCore(() => SDL_InitSubSystem(flags), out error);

    private static bool TryInitCore(Func<byte> initCall, out string error)
    {
        try
        {
            if (initCall() != 0)
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

    public static bool SetHintUtf8(string name, string value)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name + "\0");
        byte[] valueBytes = Encoding.UTF8.GetBytes(value + "\0");
        fixed (byte* n = nameBytes)
        fixed (byte* v = valueBytes)
        {
            return SDL_SetHint(n, v) != 0;
        }
    }

    public static string GetErrorString()
    {
        byte* p = SDL_GetError();
        return p == null ? string.Empty : Marshal.PtrToStringUTF8((IntPtr)p) ?? string.Empty;
    }

    public static string? GetCurrentAudioDriverString()
    {
        byte* p = SDL_GetCurrentAudioDriver();
        return p == null ? null : Marshal.PtrToStringUTF8((IntPtr)p);
    }

    public static IntPtr GetGlProcAddress(string procName)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(procName + "\0");
        fixed (byte* p = bytes) return SDL_GL_GetProcAddress(p);
    }

    public static IntPtr CreateWindowUtf8(string title, int w, int h, ulong flags)
    {
        byte[] bytes = Encoding.UTF8.GetBytes((title ?? "reEngine") + "\0");
        fixed (byte* ptr = bytes) return SDL_CreateWindow(ptr, w, h, flags);
    }

    public static IntPtr GetWin32Hwnd(IntPtr window)
    {
        if (window == IntPtr.Zero || !OperatingSystem.IsWindows()) return IntPtr.Zero;
        uint props = SDL_GetWindowProperties(window);
        if (props == 0) return IntPtr.Zero;
        byte[] name = Encoding.UTF8.GetBytes("SDL.window.win32.hwnd\0");
        fixed (byte* p = name) return SDL_GetPointerProperty(props, p, IntPtr.Zero);
    }

    public static void BringToForeground(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        _ = SDL_ShowWindow(window);
        _ = SDL_RaiseWindow(window);
        IntPtr hwnd = GetWin32Hwnd(window);
        if (hwnd != IntPtr.Zero) _ = SetForegroundWindow(hwnd);
    }

    public static void PresentSdlTexture(
        IntPtr renderer,
        IntPtr texture,
        int texW,
        int texH,
        int renderW,
        int renderH,
        HostViewportRect destRect,
        HostViewportRect srcCropRect)
    {
        if (renderer == IntPtr.Zero || texture == IntPtr.Zero || renderW <= 0 || renderH <= 0) return;

        HostViewportRect src = HostViewportGeometry.ClampSourceRect(texW, texH, srcCropRect);
        HostViewportRect dst = HostViewportGeometry.ClampDestinationRect(renderW, renderH, destRect);

        _ = SDL_SetRenderDrawColor(renderer, HostViewportGeometry.Surface0R, HostViewportGeometry.Surface0G, HostViewportGeometry.Surface0B, HostViewportGeometry.Surface0A);
        _ = SDL_RenderClear(renderer);
        if (dst.Width > 0 && dst.Height > 0 && src.Width > 0 && src.Height > 0)
        {
            var srcF = new SdlFRect { X = src.X, Y = src.Y, W = src.Width, H = src.Height };
            var dstF = new SdlFRect { X = dst.X, Y = dst.Y, W = dst.Width, H = dst.Height };
            _ = SDL_RenderTexture(renderer, texture, &srcF, &dstF);
        }
        _ = SDL_RenderPresent(renderer);
    }

    public static uint EventType(byte* ev) => *(uint*)ev;
    public static int EventScancode(byte* ev) => *(int*)(ev + 24);
    public static bool EventKeyIsDown(byte* ev) => *(ev + 36) != 0;
    public static bool EventKeyIsRepeat(byte* ev) => *(ev + 37) != 0;
    public static byte EventMouseButton(byte* ev) => *(ev + 24);
    public static int EventMouseMotionButtons(byte* ev) => (int)(*(uint*)(ev + 24) & 0x1Fu);
    public static (float X, float Y) EventMousePosition(byte* ev) => (*(float*)(ev + 28), *(float*)(ev + 32));
    public static (float Dx, float Dy) EventMouseRelative(byte* ev) => (*(float*)(ev + 36), *(float*)(ev + 40));
    public static float EventMouseWheelDeltaY(byte* ev) => *(float*)(ev + 28);
    public static string? EventDropPath(byte* ev)
    {
        if (ev == null) return null;
        IntPtr dataPtr = *(IntPtr*)(ev + (IntPtr.Size == 8 ? 40 : 28));
        return dataPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(dataPtr);
    }

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
