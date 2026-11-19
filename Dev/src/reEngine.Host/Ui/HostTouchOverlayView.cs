// @module: reEngine.Host.Ui.HostTouchOverlayView
// @purpose: Cross-platform immediate-mode touch overlay provider (D-Pad, A/B/X/Y, Start/Select) and swipe/pinch gesture decoder.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts
// @exports: HostTouchOverlayView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Cross-platform <see cref="IHostInputProvider"/> rendering responsive portrait/landscape touch controls onto
/// <see cref="PixelOverlayCanvas"/> with hit-testing and swipe/pinch gesture decoding.
/// </summary>
public sealed class HostTouchOverlayView : IHostInputProvider
{
    private sealed record TouchZone(string ControlId, string Label, HostGamepadButtons ButtonFlag, Rectangle Bounds);

    private sealed class TrackedFinger
    {
        public float StartX { get; init; }
        public float StartY { get; init; }
        public float PrevX { get; set; }
        public float PrevY { get; set; }
        public float CurrX { get; set; }
        public float CurrY { get; set; }
        public bool HitButton { get; init; }
        public bool SwipeEmitted { get; set; }
    }

    private const float SwipeThreshold = 0.08f;
    private const float PinchDeltaThreshold = 0.015f;

    private readonly List<TouchZone> _zones = new();
    private readonly Dictionary<long, TrackedFinger> _fingers = new();
    private readonly List<HostTouchGestureEvent> _lastGestures = new();
    private HostGamepadButtons _currentButtons;
    private float _leftX;
    private float _leftY;
    private int _activeTouchCount;
    private bool _disposed;

    public HostTouchOverlayView(int viewportWidth = 1280, int viewportHeight = 720) =>
        RecomputeZones(viewportWidth, viewportHeight);

    public string ProviderId => "host-touch-overlay";
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsConnected => !_disposed && IsEnabled;
    public int ViewportWidth { get; private set; }
    public int ViewportHeight { get; private set; }
    public bool IsPortraitLayout => ViewportHeight > ViewportWidth;
    public IReadOnlyList<HostTouchGestureEvent> LastDecodedGestures => _lastGestures;

    public event Action<HostTouchGestureEvent>? GestureDecoded;

    public void RecomputeZones(int viewportWidth, int viewportHeight)
    {
        int w = Math.Max(160, viewportWidth);
        int h = Math.Max(160, viewportHeight);
        if (w == ViewportWidth && h == ViewportHeight && _zones.Count > 0) return;

        ViewportWidth = w;
        ViewportHeight = h;
        _zones.Clear();

        int unit = Math.Clamp(Math.Min(w, h) / 15, 22, 54);
        int margin = Math.Max(12, unit / 2);
        int dpadCenterX = margin + unit + (unit / 2);
        int dpadCenterY = h - margin - (unit * 2);
        int faceCenterX = w - margin - (unit * 2);
        int faceCenterY = dpadCenterY;

        _zones.Add(new("Touch_DPadUp", "^", HostGamepadButtons.DPadUp, new(dpadCenterX - (unit / 2), dpadCenterY - (unit * 3 / 2), unit, unit)));
        _zones.Add(new("Touch_DPadDown", "v", HostGamepadButtons.DPadDown, new(dpadCenterX - (unit / 2), dpadCenterY + (unit / 2), unit, unit)));
        _zones.Add(new("Touch_DPadLeft", "<", HostGamepadButtons.DPadLeft, new(dpadCenterX - (unit * 3 / 2), dpadCenterY - (unit / 2), unit, unit)));
        _zones.Add(new("Touch_DPadRight", ">", HostGamepadButtons.DPadRight, new(dpadCenterX + (unit / 2), dpadCenterY - (unit / 2), unit, unit)));

        _zones.Add(new("Touch_North", "Y", HostGamepadButtons.North, new(faceCenterX - (unit / 2), faceCenterY - (unit * 3 / 2), unit, unit)));
        _zones.Add(new("Touch_South", "A", HostGamepadButtons.South, new(faceCenterX - (unit / 2), faceCenterY + (unit / 2), unit, unit)));
        _zones.Add(new("Touch_West", "X", HostGamepadButtons.West, new(faceCenterX - (unit * 3 / 2), faceCenterY - (unit / 2), unit, unit)));
        _zones.Add(new("Touch_East", "B", HostGamepadButtons.East, new(faceCenterX + (unit / 2), faceCenterY - (unit / 2), unit, unit)));

        int btnW = Math.Max(42, (int)(unit * 1.4f));
        int btnH = Math.Max(20, (int)(unit * 0.6f));
        int centerX = w / 2;
        int sysY = IsPortraitLayout ? dpadCenterY - (unit * 2) - 8 : h - margin - btnH;
        _zones.Add(new("Touch_Select", "SEL", HostGamepadButtons.Select, new(centerX - btnW - 6, sysY, btnW, btnH)));
        _zones.Add(new("Touch_Start", "STR", HostGamepadButtons.Start, new(centerX + 6, sysY, btnW, btnH)));
    }

    public IReadOnlyList<HostTouchGestureEvent> ProcessTouchPoints(
        IReadOnlyList<HostTouchPoint> points,
        int viewportWidth = 0,
        int viewportHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (viewportWidth > 0 && viewportHeight > 0) RecomputeZones(viewportWidth, viewportHeight);

        _lastGestures.Clear();
        _currentButtons = HostGamepadButtons.None;
        _leftX = 0f;
        _leftY = 0f;
        _activeTouchCount = 0;

        if (_disposed || !IsEnabled)
        {
            _fingers.Clear();
            return Array.Empty<HostTouchGestureEvent>();
        }

        if (points.Count == 0)
        {
            _fingers.Clear();
            return Array.Empty<HostTouchGestureEvent>();
        }

        var pressedIds = new List<long>(2);
        foreach (HostTouchPoint pt in points)
        {
            float nx = Math.Clamp(pt.NormalizedX, 0f, 1f);
            float ny = Math.Clamp(pt.NormalizedY, 0f, 1f);
            int px = (int)MathF.Round(nx * ViewportWidth);
            int py = (int)MathF.Round(ny * ViewportHeight);

            bool hitAny = false;
            if (pt.IsPressed)
            {
                _activeTouchCount++;
                foreach (TouchZone zone in _zones)
                {
                    if (!zone.Bounds.Contains(px, py)) continue;
                    _currentButtons |= zone.ButtonFlag;
                    hitAny = true;
                }
            }

            UpdateFingerGesture(pt, nx, ny, hitAny);
            if (pt.IsPressed) pressedIds.Add(pt.FingerId);
        }

        DecodePinch(pressedIds);
        UpdateStickFromDpad();
        return _lastGestures.ToArray();
    }

    public void Render(PixelOverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_disposed || !IsEnabled || !IsVisible) return;

        RecomputeZones(canvas.Width, canvas.Height);
        foreach (TouchZone zone in _zones)
        {
            bool pressed = _currentButtons.HasFlag(zone.ButtonFlag);
            HostOverlayLayoutHelpers.DrawPillButton(
                canvas,
                zone.Bounds,
                zone.Label,
                pressed,
                HostOverlayPalette.TouchPadActive);
        }
    }

    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (_disposed || !IsEnabled || !IsVisible) return Array.Empty<HostWidgetBounds>();
        var list = new List<HostWidgetBounds>(_zones.Count);
        foreach (TouchZone zone in _zones)
        {
            list.Add(new HostWidgetBounds(zone.ControlId, "Button", zone.Label, zone.Bounds, true));
        }
        return list;
    }

    public HostExternalInputSnapshot Poll() =>
        _disposed || !IsEnabled
            ? default
            : new HostExternalInputSnapshot(
                _currentButtons, _leftX, _leftY, 0f, 0f, 0f, 0f,
                GamepadButtonsMask: 0u, ControllerConnected: false, TouchActiveCount: _activeTouchCount);

    public bool TryRumble(HostRumbleRequest request) => false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fingers.Clear();
        _lastGestures.Clear();
        _currentButtons = HostGamepadButtons.None;
        _activeTouchCount = 0;
    }

    private void UpdateFingerGesture(HostTouchPoint pt, float nx, float ny, bool hitButton)
    {
        if (pt.IsPressed)
        {
            if (!_fingers.TryGetValue(pt.FingerId, out TrackedFinger? tracked))
            {
                _fingers[pt.FingerId] = new TrackedFinger
                {
                    StartX = nx, StartY = ny, PrevX = nx, PrevY = ny, CurrX = nx, CurrY = ny, HitButton = hitButton
                };
                return;
            }
            tracked.PrevX = tracked.CurrX;
            tracked.PrevY = tracked.CurrY;
            tracked.CurrX = nx;
            tracked.CurrY = ny;

            if (!tracked.HitButton && !tracked.SwipeEmitted && _activeTouchCount == 1)
            {
                float dx = nx - tracked.StartX;
                float dy = ny - tracked.StartY;
                if (Hypot(dx, dy) >= SwipeThreshold)
                {
                    EmitGesture(new HostTouchGestureEvent("swipe", dx, dy, 1.0f));
                    tracked.SwipeEmitted = true;
                }
            }
            return;
        }

        if (_fingers.Remove(pt.FingerId, out TrackedFinger? released) && !released.HitButton && !released.SwipeEmitted)
        {
            float dx = nx - released.StartX;
            float dy = ny - released.StartY;
            float dist = Hypot(dx, dy);
            if (dist >= SwipeThreshold) EmitGesture(new HostTouchGestureEvent("swipe", dx, dy, 1.0f));
            else if (dist < 0.04f) EmitGesture(new HostTouchGestureEvent("tap", 0f, 0f, 1.0f));
        }
    }

    private void DecodePinch(List<long> pressedIds)
    {
        if (pressedIds.Count < 2) return;
        if (!_fingers.TryGetValue(pressedIds[0], out TrackedFinger? f0) || !_fingers.TryGetValue(pressedIds[1], out TrackedFinger? f1)) return;
        if (f0.HitButton || f1.HitButton) return;

        float prevDist = Hypot(f0.PrevX - f1.PrevX, f0.PrevY - f1.PrevY);
        float currDist = Hypot(f0.CurrX - f1.CurrX, f0.CurrY - f1.CurrY);
        if (prevDist <= 0.01f || MathF.Abs(currDist - prevDist) < PinchDeltaThreshold) return;

        float prevCx = (f0.PrevX + f1.PrevX) * 0.5f;
        float prevCy = (f0.PrevY + f1.PrevY) * 0.5f;
        float currCx = (f0.CurrX + f1.CurrX) * 0.5f;
        float currCy = (f0.CurrY + f1.CurrY) * 0.5f;
        EmitGesture(new HostTouchGestureEvent("pinch", currCx - prevCx, currCy - prevCy, currDist / prevDist));
    }

    private static float Hypot(float dx, float dy) => MathF.Sqrt((dx * dx) + (dy * dy));

    private void UpdateStickFromDpad()
    {
        if (_currentButtons.HasFlag(HostGamepadButtons.DPadLeft)) _leftX = -1f;
        if (_currentButtons.HasFlag(HostGamepadButtons.DPadRight)) _leftX = 1f;
        if (_currentButtons.HasFlag(HostGamepadButtons.DPadUp)) _leftY = -1f;
        if (_currentButtons.HasFlag(HostGamepadButtons.DPadDown)) _leftY = 1f;
    }

    private void EmitGesture(HostTouchGestureEvent gesture)
    {
        _lastGestures.Add(gesture);
        GestureDecoded?.Invoke(gesture);
    }
}
