// @module: reEngine.Host.Audio.HostSoundTestService
// @purpose: Clean-room Sound Test jukebox controller wrapping optional child IHostSoundTestProvider or built-in synthetic PCM audition tracks.
// @depends-on: System, System.Collections.Generic, reEngine.Host.Contracts
// @exports: HostSoundTestService

using reEngine.Host.Contracts;

namespace reEngine.Host.Audio;

/// <summary>
/// Clean-room Sound Test jukebox supporting child <see cref="IHostSoundTestProvider"/> adapters and built-in synthetic stereo tracks.
/// </summary>
public sealed class HostSoundTestService : IHostSoundTestProvider
{
    private static readonly IReadOnlyList<HostSoundTestTrack> BuiltInTracks =
    [
        new("track-01", "Track 01: Warm Neon Chime", "Clean-Room Synth", 32),
        new("track-02", "Track 02: Crisp FM Bassline", "Clean-Room FM", 32),
        new("track-03", "Track 03: Retro Duty Arpeggio", "Clean-Room Chiptune", 32)
    ];

    private static readonly double[] ChimeHz = [261.63, 329.63, 392.00, 523.25, 440.00, 392.00, 329.63, 293.66];
    private static readonly double[] BassHz = [110.00, 110.00, 130.81, 146.83, 164.81, 146.83, 130.81, 98.00];
    private static readonly double[] ArpHz = [329.63, 392.00, 493.88, 659.25, 493.88, 392.00, 523.25, 587.33];

    private readonly IHostSoundTestProvider? _innerProvider;
    private readonly int _sampleRate;
    private long _sampleFrameCursor;

    /// <summary>Gets whether Sound Test audition playback is currently active.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Gets the zero-based index of the selected track.</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>Gets whether an external platform engine provider is attached.</summary>
    public bool IsUsingExternalProvider => _innerProvider is not null;

    /// <summary>Gets the currently selected Sound Test track descriptor.</summary>
    public HostSoundTestTrack CurrentTrack
    {
        get
        {
            IReadOnlyList<HostSoundTestTrack> tracks = GetTracks();
            if (tracks.Count == 0) return BuiltInTracks[0];
            int idx = Math.Clamp(SelectedIndex, 0, tracks.Count - 1);
            return tracks[idx];
        }
    }

    /// <summary>Initializes a new <see cref="HostSoundTestService"/>.</summary>
    public HostSoundTestService(IHostSoundTestProvider? innerProvider = null, int sampleRate = 44100)
    {
        _innerProvider = innerProvider;
        _sampleRate = Math.Max(8000, sampleRate);
    }

    /// <inheritdoc />
    public IReadOnlyList<HostSoundTestTrack> GetTracks()
    {
        if (_innerProvider is not null)
        {
            IReadOnlyList<HostSoundTestTrack> external = _innerProvider.GetTracks();
            if (external.Count > 0) return external;
        }
        return BuiltInTracks;
    }

    /// <inheritdoc />
    public void PlayTrack(string trackId)
    {
        IReadOnlyList<HostSoundTestTrack> tracks = GetTracks();
        int idx = 0;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (string.Equals(tracks[i].TrackId, trackId, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        SelectedIndex = idx;
        IsPlaying = true;
        _sampleFrameCursor = 0;
        _innerProvider?.PlayTrack(tracks[idx].TrackId);
    }

    /// <inheritdoc />
    public void Stop()
    {
        IsPlaying = false;
        _innerProvider?.Stop();
    }

    /// <summary>Toggles Sound Test playback on the currently selected track.</summary>
    public void TogglePlayStop()
    {
        if (IsPlaying) Stop();
        else PlayTrack(CurrentTrack.TrackId);
    }

    /// <summary>Advances to the next track (wrapping cleanly) and switches audition playback if currently playing.</summary>
    public HostSoundTestTrack NextTrack()
    {
        IReadOnlyList<HostSoundTestTrack> tracks = GetTracks();
        SelectedIndex = (SelectedIndex + 1) % Math.Max(1, tracks.Count);
        if (IsPlaying) PlayTrack(tracks[SelectedIndex].TrackId);
        return CurrentTrack;
    }

    /// <summary>Moves to the previous track (wrapping cleanly) and switches audition playback if currently playing.</summary>
    public HostSoundTestTrack PrevTrack()
    {
        IReadOnlyList<HostSoundTestTrack> tracks = GetTracks();
        int count = Math.Max(1, tracks.Count);
        SelectedIndex = (SelectedIndex - 1 + count) % count;
        if (IsPlaying) PlayTrack(tracks[SelectedIndex].TrackId);
        return CurrentTrack;
    }

    /// <summary>Synthesizes interleaved 16-bit stereo PCM for the active clean-room audition track into <paramref name="dst"/>.</summary>
    public int RenderInterleavedStereo(Span<short> dst)
    {
        int pairs = dst.Length / 2;
        if (!IsPlaying || pairs <= 0)
        {
            dst.Clear();
            return 0;
        }

        int trackStyle = Math.Abs(SelectedIndex) % 3;
        int noteFrames = Math.Max(1, _sampleRate / 4);
        for (int i = 0; i < pairs; i++)
        {
            long n = _sampleFrameCursor++;
            double t = (double)n / _sampleRate;
            int step = (int)((n / noteFrames) % 8);
            double notePhase = (double)(n % noteFrames) / noteFrames;
            double env = Math.Min(1.0, notePhase * 18.0) * Math.Exp(-2.6 * notePhase);

            double left;
            double right;
            if (trackStyle == 0)
            {
                double freq = ChimeHz[step];
                double body = Math.Sin(2.0 * Math.PI * freq * t) + (0.35 * Math.Sin(2.0 * Math.PI * freq * 2.0 * t));
                double pad = 0.25 * Math.Sin(2.0 * Math.PI * (freq * 0.5) * t);
                double pan = 0.22 * Math.Sin(2.0 * Math.PI * 0.5 * t);
                double mono = ((body * env) + pad) * 0.30;
                left = mono * (1.0 - pan);
                right = mono * (1.0 + pan);
            }
            else if (trackStyle == 1)
            {
                double freq = BassHz[step];
                double mod = 1.6 * Math.Exp(-4.0 * notePhase) * Math.Sin(2.0 * Math.PI * freq * 2.0 * t);
                double carrier = Math.Sin((2.0 * Math.PI * freq * t) + mod);
                double sub = 0.35 * Math.Sin(2.0 * Math.PI * freq * 0.5 * t);
                left = ((carrier * env) + (sub * 0.45)) * 0.32;
                right = ((carrier * env) + (sub * 0.45)) * 0.30;
            }
            else
            {
                double freq = ArpHz[step];
                double cyc = (freq * t) - Math.Floor(freq * t);
                double pulse = cyc < 0.25 ? 0.85 : -0.85;
                double tri = (4.0 * Math.Abs(((freq * 0.5 * t) - Math.Floor((freq * 0.5 * t) + 0.5)))) - 1.0;
                double sig = ((pulse * 0.55) + (tri * 0.45)) * (0.35 + (0.65 * env)) * 0.28;
                left = sig * (step % 2 == 0 ? 1.0 : 0.82);
                right = sig * (step % 2 == 0 ? 0.82 : 1.0);
            }

            dst[i * 2] = HostBiquadFilter.ClampToInt16(left * 32767.0);
            dst[(i * 2) + 1] = HostBiquadFilter.ClampToInt16(right * 32767.0);
        }

        return pairs * 2;
    }
}
