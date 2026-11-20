// @module: reEngine.Host.Audio.HostAudioDspPipeline
// @purpose: Host stereo DSP chain (master/category gains, 5ms focus ramp, mid-side width, linear/4-tap Catmull-Rom resampling, biquad LP/HP, FF audio clamp).
// @depends-on: System, reEngine.Host.Contracts
// @exports: HostAudioDspPipeline

using reEngine.Host.Contracts;

namespace reEngine.Host.Audio;

/// <summary>
/// Deterministic audio DSP pipeline applying mixer gains, smooth focus-loss ramps, mid-side width,
/// 2nd-order Butterworth LP/HP filters, fast-forward clamping, and stereo resampling.
/// </summary>
public sealed class HostAudioDspPipeline
{
    private const int FastForwardLowPassCutoffHz = 6000;

    private readonly HostBiquadFilter _highPassFilter;
    private readonly HostBiquadFilter _lowPassFilter;
    private double _focusGain = 1.0;

    /// <summary>Gets the configured output sample rate in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the configured channel count (<c>1</c> or <c>2</c>).</summary>
    public int Channels { get; }

    /// <summary>Gets the active normalized mixer settings.</summary>
    public HostAudioMixerSettings Settings { get; private set; } = new();

    /// <summary>Gets whether the host window currently has focus.</summary>
    public bool IsWindowFocused { get; private set; } = true;

    /// <summary>Gets whether fast-forward mode is active.</summary>
    public bool IsFastForwardEnabled { get; private set; }

    /// <summary>Gets the active fast-forward speed multiplier.</summary>
    public double FastForwardMultiplier { get; private set; } = 2.0;

    /// <summary>Gets the instantaneous focus-ramp gain (<c>0.0..1.0</c>) for diagnostics and testing.</summary>
    public double CurrentFocusGain => _focusGain;

    /// <summary>Initializes a new <see cref="HostAudioDspPipeline"/>.</summary>
    public HostAudioDspPipeline(int sampleRate = 44100, int channels = 2, HostAudioMixerSettings? initialSettings = null)
    {
        SampleRate = Math.Max(8000, sampleRate);
        Channels = Math.Clamp(channels, 1, 2);
        _highPassFilter = new HostBiquadFilter(HostBiquadKind.HighPass, 0.0, SampleRate);
        _lowPassFilter = new HostBiquadFilter(HostBiquadKind.LowPass, 0.0, SampleRate);
        ApplySettings(initialSettings ?? new HostAudioMixerSettings());
    }

    /// <summary>Applies normalized mixer and filter settings without clearing running biquad state unnecessarily.</summary>
    public void ApplySettings(HostAudioMixerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings with
        {
            MasterVolume = Math.Clamp(settings.MasterVolume, 0.0, 1.0),
            BgmVolume = Math.Clamp(settings.BgmVolume, 0.0, 1.0),
            SfxVolume = Math.Clamp(settings.SfxVolume, 0.0, 1.0),
            VoiceVolume = Math.Clamp(settings.VoiceVolume, 0.0, 1.0),
            AudioLowPassHz = Math.Max(0, settings.AudioLowPassHz),
            AudioHighPassHz = Math.Max(0, settings.AudioHighPassHz),
            AudioStereoWidthPercent = Math.Clamp(settings.AudioStereoWidthPercent, 0.0, 200.0)
        };
        RefreshFilterCutoffs();
    }

    /// <summary>Updates window focus and fast-forward state for the 5 ms anti-pop focus ramp and FF clamping.</summary>
    public void SetFocusAndFastForward(bool isWindowFocused, bool isFastForwardEnabled, double ffMultiplier = 2.0)
    {
        IsWindowFocused = isWindowFocused;
        IsFastForwardEnabled = isFastForwardEnabled;
        FastForwardMultiplier = Math.Clamp(ffMultiplier, 2.0, 4.0);
        RefreshFilterCutoffs();
    }

    /// <summary>Resets biquad delay lines and restores the focus-loss ramp to its steady target.</summary>
    public void ResetFilterState()
    {
        _highPassFilter.Reset();
        _lowPassFilter.Reset();
        _focusGain = GetTargetFocusGain();
    }

    /// <summary>Mixes BGM, SFX, and Voice interleaved buffers using <see cref="Settings"/> category gains.</summary>
    public void MixCategories(
        ReadOnlySpan<short> bgmInterleaved,
        ReadOnlySpan<short> sfxInterleaved,
        ReadOnlySpan<short> voiceInterleaved,
        Span<short> destinationInterleaved)
    {
        float bgmGain = (float)Settings.BgmVolume;
        float sfxGain = (float)Settings.SfxVolume;
        float voiceGain = (float)Settings.VoiceVolume;
        for (int i = 0; i < destinationInterleaved.Length; i++)
        {
            float sum = 0f;
            if (i < bgmInterleaved.Length) sum += bgmInterleaved[i] * bgmGain;
            if (i < sfxInterleaved.Length) sum += sfxInterleaved[i] * sfxGain;
            if (i < voiceInterleaved.Length) sum += voiceInterleaved[i] * voiceGain;
            destinationInterleaved[i] = HostBiquadFilter.ClampToInt16(sum);
        }
    }

    /// <summary>Processes interleaved 16-bit PCM samples in place through the full DSP chain.</summary>
    public void ProcessInterleaved(Span<short> interleavedPcm) => ProcessInterleaved(interleavedPcm, interleavedPcm);

    /// <summary>Processes interleaved 16-bit PCM samples from <paramref name="sourcePcm"/> into <paramref name="destinationPcm"/>.</summary>
    public void ProcessInterleaved(ReadOnlySpan<short> sourcePcm, Span<short> destinationPcm)
    {
        int count = Math.Min(sourcePcm.Length, destinationPcm.Length);
        if (count <= 0) return;

        double targetFocusGain = GetTargetFocusGain();
        double focusStep = 1.0 / Math.Max(1.0, SampleRate * 0.005);

        if (Settings.IsAudioMuted || (IsFastForwardEnabled && Settings.FastForwardAudioMode == HostFastForwardAudioMode.Muted))
        {
            int frames = Math.Max(1, count / Channels);
            StepFocusGainTowards(targetFocusGain, focusStep * frames);
            destinationPcm[..count].Clear();
            return;
        }

        double widthFactor = Settings.AudioStereoWidthPercent / 100.0;
        double ffGain = IsFastForwardEnabled && Settings.FastForwardAudioMode == HostFastForwardAudioMode.AttenuatedLowPass ? 0.5 : 1.0;
        double baseGain = Settings.MasterVolume * ffGain;

        if (Channels == 1)
        {
            for (int i = 0; i < count; i++)
            {
                StepFocusGainTowards(targetFocusGain, focusStep);
                double s = sourcePcm[i];
                double dummy = s;
                _highPassFilter.ProcessStereo(ref s, ref dummy);
                _lowPassFilter.ProcessStereo(ref s, ref dummy);
                destinationPcm[i] = HostBiquadFilter.ClampToInt16(s * baseGain * _focusGain);
            }
            return;
        }

        int pairs = count & ~1;
        for (int i = 0; i < pairs; i += 2)
        {
            StepFocusGainTowards(targetFocusGain, focusStep);
            double l = sourcePcm[i];
            double r = sourcePcm[i + 1];

            _highPassFilter.ProcessStereo(ref l, ref r);
            _lowPassFilter.ProcessStereo(ref l, ref r);

            if (Math.Abs(widthFactor - 1.0) > 1e-6)
            {
                double mid = (l + r) * 0.5;
                double side = (l - r) * 0.5 * widthFactor;
                l = mid + side;
                r = mid - side;
            }

            double gain = baseGain * _focusGain;
            destinationPcm[i] = HostBiquadFilter.ClampToInt16(l * gain);
            destinationPcm[i + 1] = HostBiquadFilter.ClampToInt16(r * gain);
        }

        if (pairs < count) destinationPcm[pairs] = 0;
    }

    /// <summary>Resamples interleaved stereo PCM using configured <see cref="HostAudioResamplingMode"/> (<c>Linear</c> vs 4-tap Catmull-Rom).</summary>
    public int ResampleInterleavedStereo(
        ReadOnlySpan<short> sourceInterleaved,
        int sourceSampleRate,
        int destinationSampleRate,
        Span<short> destinationInterleaved)
    {
        if (sourceSampleRate <= 0 || destinationSampleRate <= 0) return 0;
        if (sourceSampleRate == destinationSampleRate)
        {
            int copyCount = Math.Min(sourceInterleaved.Length & ~1, destinationInterleaved.Length & ~1);
            sourceInterleaved[..copyCount].CopyTo(destinationInterleaved);
            return copyCount;
        }

        double speedRatio = (double)sourceSampleRate / destinationSampleRate;
        return ResampleInterleavedStereo(sourceInterleaved, speedRatio, destinationInterleaved);
    }

    /// <summary>Resamples interleaved stereo PCM at an explicit step ratio using Linear or 4-tap Catmull-Rom interpolation.</summary>
    public int ResampleInterleavedStereo(
        ReadOnlySpan<short> sourceInterleaved,
        double stepRatio,
        Span<short> destinationInterleaved)
    {
        int srcFrames = sourceInterleaved.Length / 2;
        int maxDstFrames = destinationInterleaved.Length / 2;
        if (srcFrames <= 0 || maxDstFrames <= 0 || stepRatio <= 0.0 || !double.IsFinite(stepRatio)) return 0;

        int dstFrames = Math.Min(maxDstFrames, Math.Max(1, (int)Math.Floor(srcFrames / stepRatio)));
        bool cubic = Settings.ResamplingMode == HostAudioResamplingMode.SincCubic;

        for (int d = 0; d < dstFrames; d++)
        {
            double pos = d * stepRatio;
            int i1 = Math.Clamp((int)Math.Floor(pos), 0, srcFrames - 1);
            double t = Math.Clamp(pos - i1, 0.0, 1.0);

            double l;
            double r;
            if (!cubic)
            {
                int i2 = Math.Clamp(i1 + 1, 0, srcFrames - 1);
                l = sourceInterleaved[i1 * 2] + ((sourceInterleaved[i2 * 2] - sourceInterleaved[i1 * 2]) * t);
                r = sourceInterleaved[(i1 * 2) + 1] + ((sourceInterleaved[(i2 * 2) + 1] - sourceInterleaved[(i1 * 2) + 1]) * t);
            }
            else
            {
                int i0 = Math.Clamp(i1 - 1, 0, srcFrames - 1);
                int i2 = Math.Clamp(i1 + 1, 0, srcFrames - 1);
                int i3 = Math.Clamp(i1 + 2, 0, srcFrames - 1);
                l = CatmullRom(sourceInterleaved[i0 * 2], sourceInterleaved[i1 * 2], sourceInterleaved[i2 * 2], sourceInterleaved[i3 * 2], t);
                r = CatmullRom(sourceInterleaved[(i0 * 2) + 1], sourceInterleaved[(i1 * 2) + 1], sourceInterleaved[(i2 * 2) + 1], sourceInterleaved[(i3 * 2) + 1], t);
            }

            destinationInterleaved[d * 2] = HostBiquadFilter.ClampToInt16(l);
            destinationInterleaved[(d * 2) + 1] = HostBiquadFilter.ClampToInt16(r);
        }

        return dstFrames * 2;
    }

    private void RefreshFilterCutoffs()
    {
        _highPassFilter.Configure(HostBiquadKind.HighPass, Settings.AudioHighPassHz, SampleRate);
        int lpHz = Settings.AudioLowPassHz;
        if (IsFastForwardEnabled && Settings.FastForwardAudioMode == HostFastForwardAudioMode.AttenuatedLowPass)
        {
            lpHz = lpHz > 0 ? Math.Min(lpHz, FastForwardLowPassCutoffHz) : FastForwardLowPassCutoffHz;
        }
        _lowPassFilter.Configure(HostBiquadKind.LowPass, lpHz, SampleRate);
    }

    private double GetTargetFocusGain() => Settings.ShouldMuteOnFocusLoss && !IsWindowFocused ? 0.0 : 1.0;

    private void StepFocusGainTowards(double target, double maxDelta)
    {
        if (_focusGain < target) _focusGain = Math.Min(target, _focusGain + maxDelta);
        else if (_focusGain > target) _focusGain = Math.Max(target, _focusGain - maxDelta);
    }

    private static double CatmullRom(double y0, double y1, double y2, double y3, double t)
    {
        double c0 = y1;
        double c1 = 0.5 * (y2 - y0);
        double c2 = y0 - (2.5 * y1) + (2.0 * y2) - (0.5 * y3);
        double c3 = (0.5 * (y3 - y0)) + (1.5 * (y1 - y2));
        return (((((c3 * t) + c2) * t) + c1) * t) + c0;
    }
}
