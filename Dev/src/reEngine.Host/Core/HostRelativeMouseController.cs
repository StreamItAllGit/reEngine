// @module: reEngine.Host.Core.HostRelativeMouseController
// @purpose: Pure stateful lifecycle controller for SDL relative mouse capture across viewport clicks, overlay toggles, focus loss, Esc, and control-mode changes.
// @depends-on: System, reEngine.Host.Contracts
// @exports: HostRelativeMouseController

using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

/// <summary>
/// Tracks and enforces deterministic OS relative mouse capture rules per <c>R08 §4.2</c>.
/// </summary>
public sealed class HostRelativeMouseController
{
    private readonly Action<bool>? _applyNativeMode;

    /// <summary>Initializes a new controller with an optional native capture callback.</summary>
    public HostRelativeMouseController(Action<bool>? applyNativeMode = null) => _applyNativeMode = applyNativeMode;

    /// <summary>Gets whether relative mouse capture is currently active.</summary>
    public bool IsCaptured { get; private set; }

    /// <summary>
    /// Attempts to capture relative mouse input upon a viewport left-click when
    /// <paramref name="mode"/> is <see cref="HostControlMode.ModernWasdMouse"/>, the overlay is closed, and the window is focused.
    /// </summary>
    public bool TryCaptureOnViewportClick(HostControlMode mode, bool isOverlayOpen, bool isWindowFocused)
    {
        if (mode != HostControlMode.ModernWasdMouse || isOverlayOpen || !isWindowFocused)
        {
            _ = SetCaptured(false);
            return false;
        }
        _ = SetCaptured(true);
        return true;
    }

    /// <summary>Releases relative mouse capture when an overlay drawer or media gate opens.</summary>
    public bool ReleaseOnOverlayOpen() => SetCaptured(false);

    /// <summary>Releases relative mouse capture when the host window loses OS focus.</summary>
    public bool ReleaseOnFocusLoss() => SetCaptured(false);

    /// <summary>Releases relative mouse capture on <c>Escape</c>, returning <c>true</c> if capture was active and released.</summary>
    public bool TryReleaseOnEscape() => SetCaptured(false);

    /// <summary>Updates capture state when <c>controlMode</c> changes, releasing immediately if switching away from <see cref="HostControlMode.ModernWasdMouse"/>.</summary>
    public bool OnControlModeChanged(HostControlMode mode) =>
        mode != HostControlMode.ModernWasdMouse && SetCaptured(false);

    private bool SetCaptured(bool next)
    {
        if (IsCaptured == next) return false;
        IsCaptured = next;
        _applyNativeMode?.Invoke(next);
        return true;
    }
}
