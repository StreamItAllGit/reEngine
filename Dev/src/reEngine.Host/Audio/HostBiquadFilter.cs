// @module: reEngine.Host.Audio.HostBiquadFilter
// @purpose: Stereo 2nd-order RBJ Butterworth Low-Pass / High-Pass biquad filter with denormal protection.
// @depends-on: System
// @exports: HostBiquadKind, HostBiquadFilter

namespace reEngine.Host.Audio;

/// <summary>Filter topology implemented by <see cref="HostBiquadFilter"/>.</summary>
public enum HostBiquadKind
{
    LowPass = 0,
    HighPass = 1
}

/// <summary>
/// Stereo 2nd-order Butterworth biquad filter (<c>Q = 1 / Sqrt(2)</c>) using Transposed Direct Form II with denormal guards.
/// </summary>
public sealed class HostBiquadFilter
{
    private const double ButterworthQ = 0.7071067811865475; // 1.0 / Sqrt(2.0)
    private const double DenormalThreshold = 1e-20;

    private double _b0 = 1.0;
    private double _b1;
    private double _b2;
    private double _a1;
    private double _a2;

    private double _lz1;
    private double _lz2;
    private double _rz1;
    private double _rz2;

    /// <summary>Gets the active filter topology.</summary>
    public HostBiquadKind Kind { get; private set; }

    /// <summary>Gets the configured cutoff frequency in Hz (<c>0</c> = bypassed).</summary>
    public double CutoffHz { get; private set; }

    /// <summary>Gets the configured audio sample rate in Hz.</summary>
    public int SampleRate { get; private set; }

    /// <summary>Gets whether the filter is currently enabled (<see cref="CutoffHz"/> &gt; 0).</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Initializes a new stereo Butterworth biquad filter.</summary>
    public HostBiquadFilter(HostBiquadKind kind = HostBiquadKind.LowPass, double cutoffHz = 0.0, int sampleRate = 44100)
    {
        Configure(kind, cutoffHz, sampleRate);
    }

    /// <summary>Recomputes RBJ 2nd-order Butterworth coefficients for the given cutoff and sample rate.</summary>
    public void Configure(HostBiquadKind kind, double cutoffHz, int sampleRate)
    {
        int clampedRate = Math.Max(8000, sampleRate);
        double clampedCutoff = cutoffHz <= 0.0 || !double.IsFinite(cutoffHz)
            ? 0.0
            : Math.Clamp(cutoffHz, 10.0, clampedRate * 0.45);

        if (IsEnabled == (clampedCutoff > 0.0) &&
            Kind == kind &&
            Math.Abs(CutoffHz - clampedCutoff) < 0.001 &&
            SampleRate == clampedRate)
        {
            return;
        }

        bool wasEnabled = IsEnabled;
        bool rateChanged = SampleRate != clampedRate;
        Kind = kind;
        CutoffHz = clampedCutoff;
        SampleRate = clampedRate;
        IsEnabled = clampedCutoff > 0.0;

        if (!IsEnabled)
        {
            _b0 = 1.0;
            _b1 = 0.0;
            _b2 = 0.0;
            _a1 = 0.0;
            _a2 = 0.0;
            Reset();
            return;
        }

        double w0 = 2.0 * Math.PI * clampedCutoff / clampedRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2.0 * ButterworthQ);

        double b0;
        double b1;
        double b2;
        if (kind == HostBiquadKind.HighPass)
        {
            b0 = (1.0 + cosW0) * 0.5;
            b1 = -(1.0 + cosW0);
            b2 = (1.0 + cosW0) * 0.5;
        }
        else
        {
            b0 = (1.0 - cosW0) * 0.5;
            b1 = 1.0 - cosW0;
            b2 = (1.0 - cosW0) * 0.5;
        }

        double invA0 = 1.0 / (1.0 + alpha);
        _b0 = b0 * invA0;
        _b1 = b1 * invA0;
        _b2 = b2 * invA0;
        _a1 = (-2.0 * cosW0) * invA0;
        _a2 = (1.0 - alpha) * invA0;

        if (!wasEnabled || rateChanged)
        {
            Reset();
        }
    }

    /// <summary>Clears filter delay state across both channels.</summary>
    public void Reset()
    {
        _lz1 = 0.0;
        _lz2 = 0.0;
        _rz1 = 0.0;
        _rz2 = 0.0;
    }

    /// <summary>Processes a single stereo sample pair in place.</summary>
    public void ProcessStereo(ref double left, ref double right)
    {
        if (!IsEnabled) return;

        double lx = left;
        double ly = (_b0 * lx) + _lz1;
        _lz1 = (_b1 * lx) - (_a1 * ly) + _lz2;
        _lz2 = (_b2 * lx) - (_a2 * ly);

        double rx = right;
        double ry = (_b0 * rx) + _rz1;
        _rz1 = (_b1 * rx) - (_a1 * ry) + _rz2;
        _rz2 = (_b2 * rx) - (_a2 * ry);

        if (Math.Abs(_lz1) < DenormalThreshold) _lz1 = 0.0;
        if (Math.Abs(_lz2) < DenormalThreshold) _lz2 = 0.0;
        if (Math.Abs(_rz1) < DenormalThreshold) _rz1 = 0.0;
        if (Math.Abs(_rz2) < DenormalThreshold) _rz2 = 0.0;

        if (!double.IsFinite(ly) || !double.IsFinite(ry))
        {
            Reset();
            left = 0.0;
            right = 0.0;
            return;
        }

        left = ly;
        right = ry;
    }

    /// <summary>Processes an interleaved 16-bit stereo PCM buffer in place.</summary>
    public void ProcessInterleaved(Span<short> interleavedStereo)
    {
        if (!IsEnabled || interleavedStereo.Length < 2) return;
        int pairs = interleavedStereo.Length & ~1;
        for (int i = 0; i < pairs; i += 2)
        {
            double l = interleavedStereo[i];
            double r = interleavedStereo[i + 1];
            ProcessStereo(ref l, ref r);
            interleavedStereo[i] = ClampToInt16(l);
            interleavedStereo[i + 1] = ClampToInt16(r);
        }
    }

    internal static short ClampToInt16(double sample)
    {
        if (!double.IsFinite(sample)) return 0;
        double rounded = Math.Round(sample);
        if (rounded >= short.MaxValue) return short.MaxValue;
        if (rounded <= short.MinValue) return short.MinValue;
        return (short)rounded;
    }
}
