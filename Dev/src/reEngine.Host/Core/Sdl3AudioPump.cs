// @module: reEngine.Host.Core.Sdl3AudioPump
// @purpose: Bounded audio ring pump backed by Foundation.Core.AudioRingBuffer, HostAudioDspPipeline, hot-switchable SDL3 playback devices, and WavCaptureWriter.
// @depends-on: System, System.Collections.Generic, System.IO, Foundation.Core, reEngine.Host.Audio, reEngine.Host.Contracts
// @exports: Sdl3AudioPump

using Foundation.Core;
using reEngine.Host.Audio;
using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

internal sealed unsafe class Sdl3AudioPump : IDisposable
{
    private const int DrainChunkSamples = 2048;

    private readonly AudioRingBuffer _ring;
    private readonly HostAudioFormat _format;
    private readonly HostAudioDspPipeline _dsp;
    private readonly short[] _drainScratch = new short[DrainChunkSamples];
    private short[] _dspScratch = new short[DrainChunkSamples * 2];
    private short[] _resampleScratch = new short[DrainChunkSamples * 2];
    private IReadOnlyList<HostAudioDeviceInfo> _availableDevices = Array.Empty<HostAudioDeviceInfo>();
    private WavCaptureWriter? _wavWriter;
    private IntPtr _audioStream;
    private bool _ownsAudioSubsystem;
    private bool _disposed;

    public double FillRatio => _ring.FillRatio;
    public long UnderrunCount => _ring.UnderrunCount;
    public string? CaptureFilePath { get; }
    public string SelectedDriver { get; }
    public string? ActiveDriver { get; }
    public string? ActiveOutputDevice { get; private set; }
    public bool IsAudioSubsystemInitialized => _ownsAudioSubsystem;
    public string DiagnosticStatus { get; private set; }
    public HostAudioMixerSettings MixerSettings => _dsp.Settings;
    public HostAudioDspPipeline DspPipeline => _dsp;
    public IReadOnlyList<HostAudioDeviceInfo> AvailableOutputDevices => _availableDevices;

    public Sdl3AudioPump(HostAudioFormat format, string? wavOutputPath, bool openNativeStream)
        : this(format, wavOutputPath, openNativeStream, null, null)
    {
    }

    public Sdl3AudioPump(HostAudioFormat format, string? wavOutputPath, bool openNativeStream, string? audioDriverOverride)
        : this(format, wavOutputPath, openNativeStream, audioDriverOverride, null)
    {
    }

    public Sdl3AudioPump(
        HostAudioFormat format,
        string? wavOutputPath,
        bool openNativeStream,
        string? audioDriverOverride,
        HostAudioMixerSettings? initialSettings)
    {
        _format = format ?? new HostAudioFormat();
        _ring = new AudioRingBuffer(Math.Max(4096, _format.RingCapacitySamples), _format.Channels);
        _dsp = new HostAudioDspPipeline(_format.SampleRate, _format.Channels, initialSettings);

        if (!string.IsNullOrWhiteSpace(wavOutputPath))
        {
            CaptureFilePath = Path.GetFullPath(wavOutputPath);
            _wavWriter = new WavCaptureWriter(CaptureFilePath, _format.SampleRate, _format.Channels);
        }

        if (!openNativeStream)
        {
            SelectedDriver = NativeAudioDriverSelector.SelectBestDriver(audioDriverOverride);
            ActiveDriver = null;
            ActiveOutputDevice = _dsp.Settings.AudioOutputDevice;
            DiagnosticStatus = $"headless ({SelectedDriver})";
            return;
        }

        if (!NativeAudioDriverSelector.TryInitializeAudioSubsystem(
                audioDriverOverride,
                out string selected,
                out string? active,
                out string diag))
        {
            SelectedDriver = selected;
            ActiveDriver = null;
            DiagnosticStatus = diag;
            return;
        }

        SelectedDriver = selected;
        ActiveDriver = active;
        _ownsAudioSubsystem = true;
        _availableDevices = Sdl3AudioDeviceNative.EnumeratePlaybackDevices();

        uint targetId = Sdl3Native.DefaultAudioPlaybackDevice;
        string? resolvedName = null;
        if (!string.IsNullOrWhiteSpace(_dsp.Settings.AudioOutputDevice) &&
            Sdl3AudioDeviceNative.TryResolvePlaybackDevice(_dsp.Settings.AudioOutputDevice, _availableDevices, out uint matchedId, out string? matchedName))
        {
            targetId = matchedId;
            resolvedName = matchedName;
        }

        _audioStream = OpenNativeDeviceStream(targetId);
        if (_audioStream != IntPtr.Zero)
        {
            _ = Sdl3Native.SDL_ResumeAudioStreamDevice(_audioStream);
            ActiveOutputDevice = resolvedName;
            DiagnosticStatus = resolvedName is null ? diag : $"{diag} ({resolvedName})";
        }
        else
        {
            DiagnosticStatus = $"{diag} (stream unavailable: {Sdl3Native.GetErrorString()})";
        }
    }

    public IReadOnlyList<HostAudioDeviceInfo> RefreshOutputDevices()
    {
        _availableDevices = Sdl3AudioDeviceNative.EnumeratePlaybackDevices();
        return _availableDevices;
    }

    public void ApplySettings(HostAudioMixerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? prevDevice = _dsp.Settings.AudioOutputDevice;
        _dsp.ApplySettings(settings);
        if (_ownsAudioSubsystem && !string.Equals(prevDevice, _dsp.Settings.AudioOutputDevice, StringComparison.OrdinalIgnoreCase))
        {
            _ = TrySwitchOutputDevice(_dsp.Settings.AudioOutputDevice, out _);
        }
    }

    public void SetFocusAndFastForward(bool isWindowFocused, bool isFastForwardEnabled, double ffMultiplier = 2.0) =>
        _dsp.SetFocusAndFastForward(isWindowFocused, isFastForwardEnabled, ffMultiplier);

    public bool TrySwitchOutputDevice(string? deviceName, out string diagnostic)
    {
        if (_disposed)
        {
            diagnostic = "Audio pump disposed.";
            return false;
        }

        _availableDevices = Sdl3AudioDeviceNative.EnumeratePlaybackDevices();
        if (!Sdl3AudioDeviceNative.TryResolvePlaybackDevice(deviceName, _availableDevices, out uint targetId, out string? canonicalName))
        {
            diagnostic = $"Audio output device '{deviceName}' not found.";
            return false;
        }

        if (!_ownsAudioSubsystem)
        {
            ActiveOutputDevice = canonicalName;
            _dsp.ApplySettings(_dsp.Settings with { AudioOutputDevice = canonicalName });
            DiagnosticStatus = $"headless ({SelectedDriver}: {canonicalName ?? "System Default"})";
            diagnostic = DiagnosticStatus;
            return true;
        }

        IntPtr nextStream = OpenNativeDeviceStream(targetId);
        if (nextStream == IntPtr.Zero && _audioStream != IntPtr.Zero)
        {
            Sdl3Native.SDL_DestroyAudioStream(_audioStream);
            _audioStream = IntPtr.Zero;
            nextStream = OpenNativeDeviceStream(targetId);
        }

        if (nextStream == IntPtr.Zero)
        {
            string err = Sdl3Native.GetErrorString();
            if (_audioStream == IntPtr.Zero)
            {
                _audioStream = OpenNativeDeviceStream(Sdl3Native.DefaultAudioPlaybackDevice);
                if (_audioStream != IntPtr.Zero) _ = Sdl3Native.SDL_ResumeAudioStreamDevice(_audioStream);
            }
            diagnostic = $"Failed to open audio device '{canonicalName ?? "System Default"}': {err}";
            return false;
        }

        if (_audioStream != IntPtr.Zero)
        {
            Sdl3Native.SDL_DestroyAudioStream(_audioStream);
        }

        _audioStream = nextStream;
        _ = Sdl3Native.SDL_ResumeAudioStreamDevice(_audioStream);
        ActiveOutputDevice = canonicalName;
        _dsp.ApplySettings(_dsp.Settings with { AudioOutputDevice = canonicalName });
        DiagnosticStatus = $"audio={ActiveDriver ?? SelectedDriver} ({canonicalName ?? "System Default"})";
        diagnostic = DiagnosticStatus;
        return true;
    }

    public int EnqueueInterleaved(ReadOnlySpan<short> samples, int sourceSampleRate)
    {
        if (_disposed || samples.IsEmpty) return 0;
        if (sourceSampleRate <= 0 || sourceSampleRate == _format.SampleRate || _format.Channels != 2)
        {
            return EnqueueInterleaved(samples);
        }

        int needed = Math.Max(samples.Length, (int)Math.Ceiling(samples.Length * (double)_format.SampleRate / sourceSampleRate) + 4);
        if (_resampleScratch.Length < needed) _resampleScratch = new short[needed];
        int resampledCount = _dsp.ResampleInterleavedStereo(samples, sourceSampleRate, _format.SampleRate, _resampleScratch);
        return resampledCount > 0 ? EnqueueInterleaved(_resampleScratch.AsSpan(0, resampledCount)) : 0;
    }

    public int EnqueueInterleaved(ReadOnlySpan<short> samples)
    {
        if (_disposed || samples.IsEmpty) return 0;
        if (_dspScratch.Length < samples.Length) _dspScratch = new short[samples.Length];

        Span<short> processed = _dspScratch.AsSpan(0, samples.Length);
        _dsp.ProcessInterleaved(samples, processed);
        _wavWriter?.WriteInterleavedSamples(processed);

        if (_dsp.IsFastForwardEnabled)
        {
            int ffClampCap = Math.Min(_ring.CapacitySamples / 2, Math.Max(4096, (_format.SampleRate / 5) * _format.Channels));
            int headroom = Math.Max(0, ffClampCap - _ring.BufferedSamples);
            if (headroom <= 0) return 0;
            return _ring.Write(processed[..Math.Min(processed.Length, headroom)]);
        }

        return _ring.Write(processed);
    }

    public void PumpToDevice()
    {
        if (_disposed || _audioStream == IntPtr.Zero) return;

        int maxQueuedBytes = (_format.SampleRate / 10) * _format.Channels * sizeof(short);
        int currentlyQueued = Sdl3Native.SDL_GetAudioStreamQueued(_audioStream);
        if (currentlyQueued >= maxQueuedBytes) return;

        int buffered = _ring.BufferedSamples;
        if (buffered <= 0) return;

        int toRead = Math.Min(buffered, _drainScratch.Length);
        _ = _ring.Read(_drainScratch.AsSpan(0, toRead));
        fixed (short* ptr = _drainScratch)
        {
            _ = Sdl3Native.SDL_PutAudioStreamData(_audioStream, ptr, toRead * sizeof(short));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_audioStream != IntPtr.Zero)
        {
            Sdl3Native.SDL_DestroyAudioStream(_audioStream);
            _audioStream = IntPtr.Zero;
        }
        if (_ownsAudioSubsystem)
        {
            Sdl3Native.SDL_QuitSubSystem(Sdl3Native.InitAudio);
            _ownsAudioSubsystem = false;
        }
        _wavWriter?.Dispose();
        _wavWriter = null;
    }

    private IntPtr OpenNativeDeviceStream(uint deviceId)
    {
        var spec = new SdlAudioSpec
        {
            Format = Sdl3Native.AudioS16Le,
            Channels = _format.Channels,
            Freq = _format.SampleRate
        };
        return Sdl3Native.SDL_OpenAudioDeviceStream(deviceId, &spec, IntPtr.Zero, IntPtr.Zero);
    }
}
