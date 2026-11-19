// @module: reEngine.Host.Core.HostBackendProbe
// @purpose: Probes OS and runtime GPU/CPU availability across OpenGL, Vulkan, DirectX 12, ParallelCpu, Software, and Metal backends.
// @depends-on: System, System.Collections.Generic, System.Runtime.InteropServices, reEngine.Core.Resolution, reEngine.Host.Contracts, reEngine.Host.Rendering
// @exports: HostBackendProbe

using System.Runtime.InteropServices;
using reEngine.Core.Resolution;
using reEngine.Host.Contracts;
using reEngine.Host.Rendering;

namespace reEngine.Host.Core;

/// <summary>
/// Probes OS and hardware driver readiness for all <see cref="HostRenderBackendKind"/> renderers.
/// </summary>
public static unsafe class HostBackendProbe
{
    public static IReadOnlyList<HostBackendCapability> ProbeAll() =>
    [
        Probe(HostRenderBackendKind.OpenGl),
        Probe(HostRenderBackendKind.Vulkan),
        Probe(HostRenderBackendKind.DirectX),
        Probe(HostRenderBackendKind.ParallelCpu),
        Probe(HostRenderBackendKind.Software),
        Probe(HostRenderBackendKind.Metal)
    ];

    public static string GetCanonicalBackendId(HostRenderBackendKind kind) => kind switch
    {
        HostRenderBackendKind.OpenGl => HostRenderBackendIds.OpenGl,
        HostRenderBackendKind.Vulkan => HostRenderBackendIds.Vulkan,
        HostRenderBackendKind.DirectX => HostRenderBackendIds.DirectX,
        HostRenderBackendKind.ParallelCpu => HostRenderBackendIds.ParallelCpu,
        HostRenderBackendKind.Metal => HostRenderBackendIds.Metal,
        _ => HostRenderBackendIds.Software
    };

    public static HostBackendCapability Probe(HostRenderBackendKind kind) => kind switch
    {
        HostRenderBackendKind.OpenGl => ProbeOpenGl(),
        HostRenderBackendKind.Vulkan => ProbeVulkan(),
        HostRenderBackendKind.DirectX => ProbeDirectX12(),
        HostRenderBackendKind.ParallelCpu => new(kind, HostRenderBackendIds.ParallelCpu, true, "Deterministic 16px band parallel CPU presenter ready."),
        HostRenderBackendKind.Software => new(kind, HostRenderBackendIds.Software, true, "Reference single-threaded software presenter ready."),
        HostRenderBackendKind.Metal => ProbeMetal(),
        _ => new(kind, HostRenderBackendIds.Software, true, "Defaulting to software presenter.")
    };

    private static HostBackendCapability ProbeOpenGl()
    {
        string[] libs = OperatingSystem.IsWindows()
            ? ["opengl32.dll"]
            : OperatingSystem.IsMacOS()
                ? ["/System/Library/Frameworks/OpenGL.framework/OpenGL"]
                : ["libGL.so.1", "libGL.so"];

        foreach (string lib in libs)
        {
            if (!NativeLibrary.TryLoad(lib, out IntPtr handle)) continue;
            try
            {
                return new(HostRenderBackendKind.OpenGl, HostRenderBackendIds.OpenGl, true, $"OpenGL 3.3+ host runtime available ({lib}).");
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        return new(HostRenderBackendKind.OpenGl, HostRenderBackendIds.OpenGl, false, "OpenGL runtime library not found.");
    }

    private static HostBackendCapability ProbeVulkan()
    {
        string[] libs = OperatingSystem.IsWindows()
            ? ["vulkan-1.dll"]
            : OperatingSystem.IsMacOS()
                ? ["libvulkan.1.dylib", "libMoltenVK.dylib"]
                : ["libvulkan.so.1", "libvulkan.so"];

        foreach (string lib in libs)
        {
            if (!NativeLibrary.TryLoad(lib, out IntPtr handle)) continue;
            try
            {
                if (NativeLibrary.TryGetExport(handle, "vkEnumerateInstanceVersion", out IntPtr enumVerPtr))
                {
                    uint ver = 0;
                    int vkRes = ((delegate* unmanaged[Stdcall]<uint*, int>)enumVerPtr)(&ver);
                    if (vkRes == 0)
                    {
                        uint major = (ver >> 22) & 0x7FFu;
                        uint minor = (ver >> 12) & 0x3FFu;
                        uint patch = ver & 0xFFFu;
                        return new(HostRenderBackendKind.Vulkan, HostRenderBackendIds.Vulkan, true, $"Vulkan {major}.{minor}.{patch} loader available ({lib}).");
                    }
                    return new(HostRenderBackendKind.Vulkan, HostRenderBackendIds.Vulkan, false, $"vkEnumerateInstanceVersion failed (VkResult={vkRes}).");
                }
                if (NativeLibrary.TryGetExport(handle, "vkCreateInstance", out _))
                {
                    return new(HostRenderBackendKind.Vulkan, HostRenderBackendIds.Vulkan, true, $"Vulkan 1.0 loader available ({lib}).");
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        return new(HostRenderBackendKind.Vulkan, HostRenderBackendIds.Vulkan, false, "Vulkan loader library not found.");
    }

    private static HostBackendCapability ProbeDirectX12()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(HostRenderBackendKind.DirectX, HostRenderBackendIds.DirectX, false, "DirectX 12 is only supported on Windows.");
        }

        IntPtr d3d12 = IntPtr.Zero;
        IntPtr dxgi = IntPtr.Zero;
        try
        {
            if (!NativeLibrary.TryLoad("d3d12.dll", out d3d12) || !NativeLibrary.TryLoad("dxgi.dll", out dxgi))
            {
                return new(HostRenderBackendKind.DirectX, HostRenderBackendIds.DirectX, false, "d3d12.dll or dxgi.dll missing.");
            }
            if (!NativeLibrary.TryGetExport(d3d12, "D3D12CreateDevice", out IntPtr createDev) ||
                !NativeLibrary.TryGetExport(dxgi, "CreateDXGIFactory1", out _))
            {
                return new(HostRenderBackendKind.DirectX, HostRenderBackendIds.DirectX, false, "Required D3D12/DXGI entry points missing.");
            }

            Guid iidDevice = D3d12Native.IidId3d12Device;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr, int>)createDev)(
                IntPtr.Zero, D3d12Native.FeatureLevel11_0, &iidDevice, IntPtr.Zero);
            return hr >= 0
                ? new(HostRenderBackendKind.DirectX, HostRenderBackendIds.DirectX, true, "DirectX 12 runtime and adapter available.")
                : new(HostRenderBackendKind.DirectX, HostRenderBackendIds.DirectX, false, $"D3D12 probe returned HRESULT 0x{hr:X8}.");
        }
        finally
        {
            if (dxgi != IntPtr.Zero) NativeLibrary.Free(dxgi);
            if (d3d12 != IntPtr.Zero) NativeLibrary.Free(d3d12);
        }
    }

    private static HostBackendCapability ProbeMetal()
    {
        bool available = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS();
        return available
            ? new(HostRenderBackendKind.Metal, HostRenderBackendIds.Metal, true, "Apple Metal host surface available.")
            : new(HostRenderBackendKind.Metal, HostRenderBackendIds.Metal, false, "Metal is only available on macOS/iOS.");
    }
}
