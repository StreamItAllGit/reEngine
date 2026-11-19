// @module: reEngine.Host.Core.Sdl3AudioPump
// @purpose: Bounded audio ring pump backed by Foundation.Core.AudioRingBuffer, NativeAudioDriverSelector, SDL_AudioStream, and WavCaptureWriter.
// @depends-on: System, System.IO, Foundation.Core, reEngine.Host.Contracts
// @exports: Sdl3AudioPump

using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

internal sealed unsafe class Sdl3AudioPump : IDisposable
{
    private const int DrainChunkSamples = 2048;

    private readonly AudioRingBuffer _ring;
    private readonly HostAudioFormat _format;
    private readonly short[] _drainScratch = new short[DrainChunkSamples];
    private WavCaptureWriter? _wavWriter;
    private IntPtr _audioStream;
    private bool _ownsAudioSubsystem;
    private bool _disposed;

    public double FillRatio => _ring.FillRatio;
    public long UnderrunCount => _ring.UnderrunCount;
    public string? CaptureFilePath { get; }
    public string SelectedDriver { get; }
    public string? ActiveDriver { get; }
    public bool IsAudioSubsystemInitialized => _ownsAudioSubsystem;
    public string DiagnosticStatus { get; }

    public Sdl3AudioPump(HostAudioFormat format, string? wavOutputPath, bool openNativeStream)
        : this(format, wavOutputPath, openNativeStream, null)
    {
    }

    public Sdl3AudioPump(HostAudioFormat format, string? wavOutputPath, bool openNativeStream, string? audioDriverOverride)
    {
        _format = format ?? new HostAudioFormat();
        _ring = new AudioRingBuffer(Math.Max(4096, _format.RingCapacitySamples), _format.Channels);

        if (!string.IsNullOrWhiteSpace(wavOutputPath))
        {
            CaptureFilePath = Path.GetFullPath(wavOutputPath);
            _wavWriter = new WavCaptureWriter(CaptureFilePath, _format.SampleRate, _format.Channels);
        }

        if (!openNativeStream)
        {
            SelectedDriver = NativeAudioDriverSelector.SelectBestDriver(audioDriverOverride);
            ActiveDriver = null;
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

        var spec = new SdlAudioSpec
        {
            Format = Sdl3Native.AudioS16Le,
            Channels = _format.Channels,
            Freq = _format.SampleRate
        };
        _audioStream = Sdl3Native.SDL_OpenAudioDeviceStream(Sdl3Native.DefaultAudioPlaybackDevice, &spec, IntPtr.Zero, IntPtr.Zero);
        if (_audioStream != IntPtr.Zero)
        {
            _ = Sdl3Native.SDL_ResumeAudioStreamDevice(_audioStream);
            DiagnosticStatus = diag;
        }
        else
        {
            DiagnosticStatus = $"{diag} (stream unavailable: {Sdl3Native.GetErrorString()})";
        }
    }

    public int EnqueueInterleaved(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return 0;
        _wavWriter?.WriteInterleavedSamples(samples);
        return _ring.Write(samples);
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
}
