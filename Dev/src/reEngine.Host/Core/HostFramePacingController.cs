// @module: reEngine.Host.Core.HostFramePacingController
// @purpose: Late-latch VRR frame pacing controller adjusting HighResFramePacer for TargetFps, BackgroundFpsCap, and Fast-Forward without Run-Ahead rollback.
// @depends-on: System, Foundation.Core, reEngine.Host.Contracts
// @exports: HostFramePacingController

using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

/// <summary>
/// Manages dynamic VRR frame pacing cadence and late-latch input sampling atop <see cref="HighResFramePacer"/>
/// without latency-heavy frame-rollback Run-Ahead.
/// </summary>
public sealed class HostFramePacingController
{
    private bool _hasSyncedOnce;

    /// <summary>Gets the configured base target FPS (<c>0</c> = unlocked).</summary>
    public double BaseTargetFps { get; private set; } = 60.0;

    /// <summary>Gets the configured unfocused background FPS cap (<c>0</c> = inherit/uncapped).</summary>
    public int BackgroundFpsCap { get; private set; }

    /// <summary>Gets the clamped fast-forward multiplier (<c>2.0..4.0</c>).</summary>
    public double FastForwardMultiplier { get; private set; } = 2.0;

    /// <summary>Gets whether fast-forward pacing is currently active.</summary>
    public bool IsFastForwardActive { get; private set; }

    /// <summary>Gets whether the host window currently holds input focus.</summary>
    public bool IsWindowFocused { get; private set; } = true;

    /// <summary>Gets whether swapchain VSync is enabled.</summary>
    public bool IsVsyncEnabled { get; private set; }

    /// <summary>Gets the latest effective FPS applied to <see cref="HighResFramePacer"/>.</summary>
    public double EffectiveTargetFps { get; private set; } = 60.0;

    /// <summary>Gets the number of times <see cref="HighResFramePacer.Reset"/> was invoked due to cadence changes.</summary>
    public long CadenceResetCount { get; private set; }

    /// <summary>
    /// Computes the effective frame rate target from base FPS, unfocused background cap, and clamped fast-forward multiplier.
    /// </summary>
    public static double ComputeEffectiveTargetFps(
        double baseTargetFps,
        int backgroundFpsCap,
        double fastForwardMultiplier,
        bool isFastForwardActive,
        bool isWindowFocused)
    {
        double normalizedBase = double.IsFinite(baseTargetFps) && baseTargetFps > 0.0 ? baseTargetFps : 0.0;
        int clampedBgCap = Math.Max(0, backgroundFpsCap);

        if (!isWindowFocused && clampedBgCap > 0)
        {
            return normalizedBase > 0.0 ? Math.Min(normalizedBase, clampedBgCap) : clampedBgCap;
        }

        if (isFastForwardActive)
        {
            if (normalizedBase <= 0.0) return 0.0;
            double mult = double.IsFinite(fastForwardMultiplier) ? Math.Clamp(fastForwardMultiplier, 2.0, 4.0) : 2.0;
            return normalizedBase * mult;
        }

        return normalizedBase;
    }

    /// <summary>
    /// Synchronizes <paramref name="pacer"/> with the current display/focus/fast-forward state and calls
    /// <see cref="HighResFramePacer.Reset"/> immediately whenever the effective FPS changes.
    /// </summary>
    public double SyncPacer(
        HighResFramePacer pacer,
        double baseTargetFps,
        int backgroundFpsCap,
        double fastForwardMultiplier,
        bool isFastForwardActive,
        bool isWindowFocused,
        bool isVsyncEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(pacer);

        BaseTargetFps = double.IsFinite(baseTargetFps) && baseTargetFps > 0.0 ? baseTargetFps : 0.0;
        BackgroundFpsCap = Math.Max(0, backgroundFpsCap);
        FastForwardMultiplier = double.IsFinite(fastForwardMultiplier) ? Math.Clamp(fastForwardMultiplier, 2.0, 4.0) : 2.0;
        IsFastForwardActive = isFastForwardActive;
        IsWindowFocused = isWindowFocused;
        IsVsyncEnabled = isVsyncEnabled;

        double desiredFps = ComputeEffectiveTargetFps(
            BaseTargetFps,
            BackgroundFpsCap,
            FastForwardMultiplier,
            IsFastForwardActive,
            IsWindowFocused);

        bool cadenceChanged = !_hasSyncedOnce
            || Math.Abs(desiredFps - EffectiveTargetFps) > 1e-6
            || Math.Abs(desiredFps - pacer.TargetFps) > 1e-6;

        EffectiveTargetFps = desiredFps;
        if (cadenceChanged)
        {
            pacer.TargetFps = desiredFps;
            pacer.Reset();
            CadenceResetCount++;
            _hasSyncedOnce = true;
        }

        return desiredFps;
    }

    /// <summary>
    /// Pumps window events and input providers immediately before the frame tick for minimum-latency late-latch sampling.
    /// </summary>
    public static bool PumpLateLatchBeforeTick(
        Func<bool> pollWindowEvents,
        Action? prepareInputState,
        Func<HostInputFrame> sampleInput,
        out HostInputFrame frameInput)
    {
        ArgumentNullException.ThrowIfNull(pollWindowEvents);
        ArgumentNullException.ThrowIfNull(sampleInput);

        if (!pollWindowEvents())
        {
            frameInput = default;
            return false;
        }

        prepareInputState?.Invoke();
        frameInput = sampleInput();
        return true;
    }

    /// <summary>Waits until the next target frame deadline on <paramref name="pacer"/>.</summary>
    public double WaitForNextFrame(HighResFramePacer pacer)
    {
        ArgumentNullException.ThrowIfNull(pacer);
        return pacer.WaitForNextFrame();
    }
}
