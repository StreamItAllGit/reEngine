// @module: reEngine.Host.Rendering.Gl33Native
// @purpose: Minimal clean-room OpenGL 3.3 Core dynamic entry-point loader via SDL_GL_GetProcAddress with OS library fallback.
// @depends-on: System, System.Runtime.InteropServices, reEngine.Host.Core
// @exports: Gl33Dispatch, Gl33Native

using System.Runtime.InteropServices;
using reEngine.Host.Core;

namespace reEngine.Host.Rendering;

internal unsafe struct Gl33Dispatch
{
    public delegate* unmanaged[Stdcall]<int, int, int, int, void> Viewport;
    public delegate* unmanaged[Stdcall]<float, float, float, float, void> ClearColor;
    public delegate* unmanaged[Stdcall]<uint, void> Clear;
    public delegate* unmanaged[Stdcall]<int, uint*, void> GenTextures;
    public delegate* unmanaged[Stdcall]<int, uint*, void> DeleteTextures;
    public delegate* unmanaged[Stdcall]<uint, uint, void> BindTexture;
    public delegate* unmanaged[Stdcall]<uint, uint, int, void> TexParameteri;
    public delegate* unmanaged[Stdcall]<uint, int, void> PixelStorei;
    public delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D;
    public delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void> TexSubImage2D;
    public delegate* unmanaged[Stdcall]<int, uint*, void> GenFramebuffers;
    public delegate* unmanaged[Stdcall]<int, uint*, void> DeleteFramebuffers;
    public delegate* unmanaged[Stdcall]<uint, uint, void> BindFramebuffer;
    public delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void> FramebufferTexture2D;
    public delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;
    public delegate* unmanaged[Stdcall]<uint, int*, void> GetIntegerv;
    public delegate* unmanaged[Stdcall]<uint, uint> CheckFramebufferStatus;
    public delegate* unmanaged[Stdcall]<void> Finish;
    public delegate* unmanaged[Stdcall]<int, int, int, int, uint, uint, void*, void> ReadPixels;

    public static bool TryLoad(out Gl33Dispatch dispatch, out string missingSymbol)
    {
        dispatch = default;
        if (!Load("glViewport", out nint p0)) { missingSymbol = "glViewport"; return false; }
        if (!Load("glClearColor", out nint p1)) { missingSymbol = "glClearColor"; return false; }
        if (!Load("glClear", out nint p2)) { missingSymbol = "glClear"; return false; }
        if (!Load("glGenTextures", out nint p3)) { missingSymbol = "glGenTextures"; return false; }
        if (!Load("glDeleteTextures", out nint p4)) { missingSymbol = "glDeleteTextures"; return false; }
        if (!Load("glBindTexture", out nint p5)) { missingSymbol = "glBindTexture"; return false; }
        if (!Load("glTexParameteri", out nint p6)) { missingSymbol = "glTexParameteri"; return false; }
        if (!Load("glPixelStorei", out nint p7)) { missingSymbol = "glPixelStorei"; return false; }
        if (!Load("glTexImage2D", out nint p8)) { missingSymbol = "glTexImage2D"; return false; }
        if (!Load("glTexSubImage2D", out nint p9)) { missingSymbol = "glTexSubImage2D"; return false; }
        if (!Load("glGenFramebuffers", out nint p10)) { missingSymbol = "glGenFramebuffers"; return false; }
        if (!Load("glDeleteFramebuffers", out nint p11)) { missingSymbol = "glDeleteFramebuffers"; return false; }
        if (!Load("glBindFramebuffer", out nint p12)) { missingSymbol = "glBindFramebuffer"; return false; }
        if (!Load("glFramebufferTexture2D", out nint p13)) { missingSymbol = "glFramebufferTexture2D"; return false; }
        if (!Load("glBlitFramebuffer", out nint p14)) { missingSymbol = "glBlitFramebuffer"; return false; }
        if (!Load("glGetIntegerv", out nint p15)) { missingSymbol = "glGetIntegerv"; return false; }
        if (!Load("glCheckFramebufferStatus", out nint p16)) { missingSymbol = "glCheckFramebufferStatus"; return false; }
        if (!Load("glFinish", out nint p17)) { missingSymbol = "glFinish"; return false; }
        if (!Load("glReadPixels", out nint p18)) { missingSymbol = "glReadPixels"; return false; }

        dispatch.Viewport = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)p0;
        dispatch.ClearColor = (delegate* unmanaged[Stdcall]<float, float, float, float, void>)p1;
        dispatch.Clear = (delegate* unmanaged[Stdcall]<uint, void>)p2;
        dispatch.GenTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)p3;
        dispatch.DeleteTextures = (delegate* unmanaged[Stdcall]<int, uint*, void>)p4;
        dispatch.BindTexture = (delegate* unmanaged[Stdcall]<uint, uint, void>)p5;
        dispatch.TexParameteri = (delegate* unmanaged[Stdcall]<uint, uint, int, void>)p6;
        dispatch.PixelStorei = (delegate* unmanaged[Stdcall]<uint, int, void>)p7;
        dispatch.TexImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)p8;
        dispatch.TexSubImage2D = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, int, uint, uint, void*, void>)p9;
        dispatch.GenFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)p10;
        dispatch.DeleteFramebuffers = (delegate* unmanaged[Stdcall]<int, uint*, void>)p11;
        dispatch.BindFramebuffer = (delegate* unmanaged[Stdcall]<uint, uint, void>)p12;
        dispatch.FramebufferTexture2D = (delegate* unmanaged[Stdcall]<uint, uint, uint, uint, int, void>)p13;
        dispatch.BlitFramebuffer = (delegate* unmanaged[Stdcall]<int, int, int, int, int, int, int, int, uint, uint, void>)p14;
        dispatch.GetIntegerv = (delegate* unmanaged[Stdcall]<uint, int*, void>)p15;
        dispatch.CheckFramebufferStatus = (delegate* unmanaged[Stdcall]<uint, uint>)p16;
        dispatch.Finish = (delegate* unmanaged[Stdcall]<void>)p17;
        dispatch.ReadPixels = (delegate* unmanaged[Stdcall]<int, int, int, int, uint, uint, void*, void>)p18;
        missingSymbol = string.Empty;
        return true;
    }

    private static bool Load(string name, out nint ptr)
    {
        ptr = Gl33Native.GetProcAddress(name);
        return ptr != IntPtr.Zero;
    }
}

internal static class Gl33Native
{
    public const uint ColorBufferBit = 0x00004000u;
    public const uint Texture2D = 0x0DE1u;
    public const uint UnsignedByte = 0x1401u;
    public const uint Rgba = 0x1908u;
    public const int Rgba8 = 0x8058;
    public const int Nearest = 0x2600;
    public const int Linear = 0x2601;
    public const uint TextureMagFilter = 0x2800u;
    public const uint TextureMinFilter = 0x2801u;
    public const uint PackRowLength = 0x0D02u;
    public const uint PackAlignment = 0x0D05u;
    public const uint UnpackRowLength = 0x0CF2u;
    public const uint UnpackAlignment = 0x0CF5u;
    public const uint ReadFramebuffer = 0x8CA8u;
    public const uint DrawFramebuffer = 0x8CA9u;
    public const uint ColorAttachment0 = 0x8CE0u;
    public const uint FramebufferComplete = 0x8CD5u;
    public const uint MajorVersion = 0x821Bu;
    public const uint MinorVersion = 0x821Cu;

    private static readonly object Sync = new();
    private static IntPtr _glModule;

    public static IntPtr GetProcAddress(string procName)
    {
        if (string.IsNullOrWhiteSpace(procName)) return IntPtr.Zero;
        IntPtr ptr = Sdl3Native.GetGlProcAddress(procName);
        if (ptr != IntPtr.Zero) return ptr;

        IntPtr lib = EnsureGlModule();
        if (lib != IntPtr.Zero && NativeLibrary.TryGetExport(lib, procName, out IntPtr exported))
        {
            return exported;
        }
        return IntPtr.Zero;
    }

    private static IntPtr EnsureGlModule()
    {
        lock (Sync)
        {
            if (_glModule != IntPtr.Zero) return _glModule;
            string[] candidates = OperatingSystem.IsWindows()
                ? ["opengl32.dll"]
                : OperatingSystem.IsMacOS()
                    ? ["/System/Library/Frameworks/OpenGL.framework/OpenGL"]
                    : ["libGL.so.1", "libGL.so"];
            foreach (string name in candidates)
            {
                if (NativeLibrary.TryLoad(name, out IntPtr handle))
                {
                    _glModule = handle;
                    break;
                }
            }
            return _glModule;
        }
    }
}
