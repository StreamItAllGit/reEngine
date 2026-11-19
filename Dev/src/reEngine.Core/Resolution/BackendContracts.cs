// @module: reEngine.Core.Resolution.BackendContracts
// @purpose: Canonical render backend identifiers (including DirectX 12 normalization and reserved Metal) and ExitCode=6 BackendUnavailableException.
// @depends-on: System, System.Collections.Generic
// @exports: HostRenderBackendIds, BackendUnavailableException

namespace reEngine.Core.Resolution;

/// <summary>
/// Canonical Standard 18 <c>backendId</c> tokens owned by <c>reEngine</c> without modifying read-only <c>Foundation.Retro</c>.
/// </summary>
public static class HostRenderBackendIds
{
    /// <summary>OpenGL 3.3+ hardware render backend identifier.</summary>
    public const string OpenGl = "opengl";

    /// <summary>Vulkan 1.3+ hardware render backend identifier.</summary>
    public const string Vulkan = "vulkan";

    /// <summary>DirectX 12 stable Windows hardware render backend identifier.</summary>
    public const string DirectX = "directx";

    /// <summary>Deterministic multi-threaded CPU band rasterizer identifier.</summary>
    public const string ParallelCpu = "parallelCpu";

    /// <summary>Reference single-threaded software CPU backend identifier.</summary>
    public const string Software = "software";

    /// <summary>Reserved Apple Metal backend identifier (<c>v1.1.0+</c> macOS/iOS).</summary>
    public const string Metal = "metal";

    /// <summary>All five desktop Windows render backends exposed in the UI and host pipeline.</summary>
    public static IReadOnlyList<string> AllWindowsBackends { get; } =
    [
        OpenGl, Vulkan, DirectX, ParallelCpu, Software
    ];

    /// <summary>Normalizes CLI/manifest backend strings (<c>dx12</c> / <c>d3d12</c> -&gt; <c>directx</c>) against canonical tokens.</summary>
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        switch (raw.Trim().ToLowerInvariant())
        {
            case "opengl":
            case "gl":
            case "opengl33":
                normalized = OpenGl;
                return true;
            case "vulkan":
            case "vk":
                normalized = Vulkan;
                return true;
            case "directx":
            case "dx12":
            case "d3d12":
            case "directx12":
                normalized = DirectX;
                return true;
            case "parallelcpu":
            case "parallel_cpu":
            case "cpuparallel":
                normalized = ParallelCpu;
                return true;
            case "software":
            case "cpu":
                normalized = Software;
                return true;
            case "metal":
                normalized = Metal;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Raised when <c>--require-backend</c> mandates a render backend that is unavailable or unsupported (maps to <c>ExitCode = 6</c>).
/// </summary>
public sealed class BackendUnavailableException : InvalidOperationException
{
    /// <summary>Requested backend identifier that could not be provisioned.</summary>
    public string RequestedBackend { get; }

    /// <summary>Diagnostic reason why the backend was unavailable or rejected.</summary>
    public string Reason { get; }

    /// <summary>Initializes a new <see cref="BackendUnavailableException"/> for <paramref name="requestedBackend"/>.</summary>
    public BackendUnavailableException(string requestedBackend, string reason)
        : base($"Required render backend '{requestedBackend}' is unavailable: {reason}")
    {
        RequestedBackend = requestedBackend;
        Reason = reason;
    }
}
