// @module: reEngine.Host.Audio.Sdl3AudioDeviceNative
// @purpose: Isolated SDL3 audio playback device enumeration P/Invokes with explicit SDL_free cleanup and device lookup.
// @depends-on: System, System.Collections.Generic, System.Runtime.InteropServices, reEngine.Host.Contracts, reEngine.Host.Core
// @exports: Sdl3AudioDeviceNative

using System.Runtime.InteropServices;
using reEngine.Host.Contracts;
using reEngine.Host.Core;

namespace reEngine.Host.Audio;

/// <summary>
/// Enumerates SDL3 audio playback endpoints and resolves device identifiers without modifying <see cref="Sdl3Native"/>.
/// </summary>
public static unsafe class Sdl3AudioDeviceNative
{
    private const string Lib = "SDL3";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern uint SDL_WasInit(uint flags);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern uint* SDL_GetAudioPlaybackDevices(out int count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern byte* SDL_GetAudioDeviceName(uint devid);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern void SDL_free(void* mem);

    /// <summary>
    /// Enumerates active OS audio playback devices via SDL3 and releases native array memory via <c>SDL_free</c>.
    /// </summary>
    public static IReadOnlyList<HostAudioDeviceInfo> EnumeratePlaybackDevices()
    {
        Sdl3Native.InstallResolver();
        bool initializedTemp = false;
        try
        {
            if ((SDL_WasInit(Sdl3Native.InitAudio) & Sdl3Native.InitAudio) == 0)
            {
                _ = NativeAudioDriverSelector.ApplyDriverHint();
                initializedTemp = Sdl3Native.TryInitSubSystem(Sdl3Native.InitAudio, out _);
                if (!initializedTemp) return Array.Empty<HostAudioDeviceInfo>();
            }

            uint* ids = SDL_GetAudioPlaybackDevices(out int count);
            try
            {
                if (ids == null || count <= 0) return Array.Empty<HostAudioDeviceInfo>();
                var devices = new List<HostAudioDeviceInfo>(count);
                for (int i = 0; i < count; i++)
                {
                    uint devId = ids[i];
                    if (devId == 0) continue;
                    byte* namePtr = SDL_GetAudioDeviceName(devId);
                    string? rawName = namePtr == null ? null : Marshal.PtrToStringUTF8((IntPtr)namePtr);
                    string name = string.IsNullOrWhiteSpace(rawName) ? $"Audio Device #{devId}" : rawName.Trim();
                    devices.Add(new HostAudioDeviceInfo(devId, name, IsDefault: devices.Count == 0));
                }
                return devices;
            }
            finally
            {
                if (ids != null) SDL_free(ids);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Array.Empty<HostAudioDeviceInfo>();
        }
        finally
        {
            if (initializedTemp)
            {
                try { Sdl3Native.SDL_QuitSubSystem(Sdl3Native.InitAudio); }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { }
            }
        }
    }

    internal static bool TryResolvePlaybackDevice(
        string? deviceName,
        IReadOnlyList<HostAudioDeviceInfo>? cachedDevices,
        out uint deviceId,
        out string? canonicalName)
    {
        if (string.IsNullOrWhiteSpace(deviceName) ||
            string.Equals(deviceName.Trim(), "default", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(deviceName.Trim(), "System Default", StringComparison.OrdinalIgnoreCase))
        {
            deviceId = Sdl3Native.DefaultAudioPlaybackDevice;
            canonicalName = null;
            return true;
        }

        IReadOnlyList<HostAudioDeviceInfo> devices = cachedDevices is { Count: > 0 }
            ? cachedDevices
            : EnumeratePlaybackDevices();

        string trimmed = deviceName.Trim();
        foreach (HostAudioDeviceInfo dev in devices)
        {
            if (string.Equals(dev.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                deviceId = dev.DeviceId;
                canonicalName = dev.Name;
                return true;
            }
        }

        foreach (HostAudioDeviceInfo dev in devices)
        {
            if (dev.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                deviceId = dev.DeviceId;
                canonicalName = dev.Name;
                return true;
            }
        }

        deviceId = 0;
        canonicalName = null;
        return false;
    }
}
