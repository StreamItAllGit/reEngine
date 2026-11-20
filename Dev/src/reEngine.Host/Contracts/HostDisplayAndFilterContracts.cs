// @module: reEngine.Host.Contracts.HostDisplayAndFilterContracts
// @purpose: Display modes, multi-monitor info, HUD anchor policies, 8-mode live video filters, F12 screenshot modes, and presentation state contracts.
// @depends-on: System
// @exports: HostDisplayMode, HostDisplayModeCodec, HostHudAnchorMode, HostHudAnchorModeCodec, HostVideoFilterMode, HostVideoFilterModeCodec, HostScreenshotCaptureMode, HostScreenshotCaptureModeCodec, HostMonitorInfo, HostVideoPresentationState

namespace reEngine.Host.Contracts;

/// <summary>Host windowing mode (Standard 18: <c>displayMode</c>).</summary>
public enum HostDisplayMode
{
    Windowed = 0,
    BorderlessFullscreen = 1,
    ExclusiveFullscreen = 2
}

/// <summary>Normalizes and cycles <see cref="HostDisplayMode"/> values.</summary>
public static class HostDisplayModeCodec
{
    public static HostDisplayMode Parse(string? raw) =>
        TryParseStrict(raw, out HostDisplayMode mode) ? mode : HostDisplayMode.Windowed;

    public static bool TryParseStrict(string? raw, out HostDisplayMode mode)
    {
        mode = HostDisplayMode.Windowed;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "windowed" or "window":
                mode = HostDisplayMode.Windowed; return true;
            case "borderlessfullscreen" or "borderless" or "fullscreen":
                mode = HostDisplayMode.BorderlessFullscreen; return true;
            case "exclusivefullscreen" or "exclusive":
                mode = HostDisplayMode.ExclusiveFullscreen; return true;
            default:
                return false;
        }
    }

    public static string ToCanonicalId(HostDisplayMode mode) => mode switch
    {
        HostDisplayMode.BorderlessFullscreen => "borderlessFullscreen",
        HostDisplayMode.ExclusiveFullscreen => "exclusiveFullscreen",
        _ => "windowed"
    };

    public static string ToDisplayLabel(HostDisplayMode mode) => mode switch
    {
        HostDisplayMode.BorderlessFullscreen => "Borderless",
        HostDisplayMode.ExclusiveFullscreen => "Exclusive",
        _ => "Windowed"
    };

    public static HostDisplayMode CycleNext(HostDisplayMode current) => current switch
    {
        HostDisplayMode.Windowed => HostDisplayMode.BorderlessFullscreen,
        HostDisplayMode.BorderlessFullscreen => HostDisplayMode.ExclusiveFullscreen,
        _ => HostDisplayMode.Windowed
    };
}

/// <summary>Widescreen HUD alignment policy (Standard 18: <c>hudAnchorMode</c>).</summary>
public enum HostHudAnchorMode
{
    SafeFourByThree = 0,
    ScreenEdges = 1,
    Custom = 2
}

/// <summary>Normalizes and cycles <see cref="HostHudAnchorMode"/> values.</summary>
public static class HostHudAnchorModeCodec
{
    public static HostHudAnchorMode Parse(string? raw) =>
        TryParseStrict(raw, out HostHudAnchorMode mode) ? mode : HostHudAnchorMode.SafeFourByThree;

    public static bool TryParseStrict(string? raw, out HostHudAnchorMode mode)
    {
        mode = HostHudAnchorMode.SafeFourByThree;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "safefourbythree" or "4:3" or "safe":
                mode = HostHudAnchorMode.SafeFourByThree; return true;
            case "screenedges" or "edges" or "wide":
                mode = HostHudAnchorMode.ScreenEdges; return true;
            case "custom":
                mode = HostHudAnchorMode.Custom; return true;
            default:
                return false;
        }
    }

    public static string ToCanonicalId(HostHudAnchorMode mode) => mode switch
    {
        HostHudAnchorMode.ScreenEdges => "screenEdges",
        HostHudAnchorMode.Custom => "custom",
        _ => "safeFourByThree"
    };

    public static string ToDisplayLabel(HostHudAnchorMode mode) => mode switch
    {
        HostHudAnchorMode.ScreenEdges => "Screen Edges",
        HostHudAnchorMode.Custom => "Custom",
        _ => "Safe 4:3"
    };

    public static HostHudAnchorMode CycleNext(HostHudAnchorMode current) => current switch
    {
        HostHudAnchorMode.SafeFourByThree => HostHudAnchorMode.ScreenEdges,
        HostHudAnchorMode.ScreenEdges => HostHudAnchorMode.Custom,
        _ => HostHudAnchorMode.SafeFourByThree
    };
}

/// <summary>All 8 live presentation filter presets (Standard 18: <c>videoFilterMode</c>).</summary>
public enum HostVideoFilterMode
{
    Nearest = 0,
    Bilinear = 1,
    SharpBilinear = 2,
    Scanlines = 3,
    CrtLite = 4,
    LcdGrid = 5,
    Xbrz = 6,
    NtscComposite = 7
}

/// <summary>Normalizes, aliases (<c>crt-aperture -&gt; crtLite</c>), and cycles <see cref="HostVideoFilterMode"/>.</summary>
public static class HostVideoFilterModeCodec
{
    public static HostVideoFilterMode Parse(string? raw) =>
        TryParseStrict(raw, out HostVideoFilterMode mode) ? mode : HostVideoFilterMode.Nearest;

    public static bool TryParseStrict(string? raw, out HostVideoFilterMode mode)
    {
        mode = HostVideoFilterMode.Nearest;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "nearest" or "pixel": mode = HostVideoFilterMode.Nearest; return true;
            case "bilinear" or "linear": mode = HostVideoFilterMode.Bilinear; return true;
            case "sharpbilinear" or "sharp": mode = HostVideoFilterMode.SharpBilinear; return true;
            case "scanlines" or "scanline": mode = HostVideoFilterMode.Scanlines; return true;
            case "crtlite" or "crt-aperture" or "crtaperture" or "crt": mode = HostVideoFilterMode.CrtLite; return true;
            case "lcdgrid" or "lcd": mode = HostVideoFilterMode.LcdGrid; return true;
            case "xbrz" or "xbr": mode = HostVideoFilterMode.Xbrz; return true;
            case "ntsccomposite" or "ntsc" or "composite": mode = HostVideoFilterMode.NtscComposite; return true;
            default: return false;
        }
    }

    public static string ToCanonicalId(HostVideoFilterMode mode) => mode switch
    {
        HostVideoFilterMode.Bilinear => "bilinear",
        HostVideoFilterMode.SharpBilinear => "sharpBilinear",
        HostVideoFilterMode.Scanlines => "scanlines",
        HostVideoFilterMode.CrtLite => "crtLite",
        HostVideoFilterMode.LcdGrid => "lcdGrid",
        HostVideoFilterMode.Xbrz => "xbrz",
        HostVideoFilterMode.NtscComposite => "ntscComposite",
        _ => "nearest"
    };

    public static string ToDisplayLabel(HostVideoFilterMode mode) => mode switch
    {
        HostVideoFilterMode.Bilinear => "Bilinear",
        HostVideoFilterMode.SharpBilinear => "Sharp Bilinear",
        HostVideoFilterMode.Scanlines => "Scanlines",
        HostVideoFilterMode.CrtLite => "CRT Lite",
        HostVideoFilterMode.LcdGrid => "LCD Grid",
        HostVideoFilterMode.Xbrz => "xBRZ",
        HostVideoFilterMode.NtscComposite => "NTSC Composite",
        _ => "Nearest"
    };

    public static HostVideoFilterMode CycleNext(HostVideoFilterMode current) =>
        (HostVideoFilterMode)(((int)current + 1) % 8);
}

/// <summary>F12 screenshot capture source (Standard 18: <c>screenshotCaptureMode</c>).</summary>
public enum HostScreenshotCaptureMode
{
    CleanNative = 0,
    FilteredComposite = 1
}

/// <summary>Normalizes and toggles <see cref="HostScreenshotCaptureMode"/>.</summary>
public static class HostScreenshotCaptureModeCodec
{
    public static HostScreenshotCaptureMode Parse(string? raw) =>
        TryParseStrict(raw, out HostScreenshotCaptureMode mode) ? mode : HostScreenshotCaptureMode.CleanNative;

    public static bool TryParseStrict(string? raw, out HostScreenshotCaptureMode mode)
    {
        mode = HostScreenshotCaptureMode.CleanNative;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "cleannative" or "native" or "clean": mode = HostScreenshotCaptureMode.CleanNative; return true;
            case "filteredcomposite" or "filtered" or "composite": mode = HostScreenshotCaptureMode.FilteredComposite; return true;
            default: return false;
        }
    }

    public static string ToCanonicalId(HostScreenshotCaptureMode mode) =>
        mode == HostScreenshotCaptureMode.FilteredComposite ? "filteredComposite" : "cleanNative";

    public static string ToDisplayLabel(HostScreenshotCaptureMode mode) =>
        mode == HostScreenshotCaptureMode.FilteredComposite ? "Filtered Composite" : "Clean Native";
}

/// <summary>Enumerated OS monitor metadata returned by SDL3 display enumeration.</summary>
public sealed record HostMonitorInfo(
    int Index,
    uint DisplayId,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float RefreshRateHz,
    bool IsPrimary);

/// <summary>Frame-rate cadence policy (Standard 18: <c>frameRateMode</c>).</summary>
public enum HostFrameRateMode
{
    Enhanced = 0,
    Original = 1
}

/// <summary>Normalizes and formats <see cref="HostFrameRateMode"/>.</summary>
public static class HostFrameRateModeCodec
{
    public static HostFrameRateMode Parse(string? raw) =>
        TryParseStrict(raw, out HostFrameRateMode mode) ? mode : HostFrameRateMode.Enhanced;

    public static bool TryParseStrict(string? raw, out HostFrameRateMode mode)
    {
        mode = HostFrameRateMode.Enhanced;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "enhanced": mode = HostFrameRateMode.Enhanced; return true;
            case "original" or "orig": mode = HostFrameRateMode.Original; return true;
            default: return false;
        }
    }

    public static string ToCanonicalId(HostFrameRateMode mode) =>
        mode == HostFrameRateMode.Original ? "original" : "enhanced";

    public static string ToDisplayLabel(HostFrameRateMode mode) =>
        mode == HostFrameRateMode.Original ? "Original" : "Enhanced";
}

/// <summary>Progress snapshot rendered on the responsive AOT compile overlay.</summary>
public sealed record HostAotCompileState(
    bool IsCompiling,
    string StageLabel,
    double ProgressRatio,
    string? DetailMessage = null);

/// <summary>Snapshot of active video presentation, VRR pacing, filter, color, and screenshot state.</summary>
public sealed record HostVideoPresentationState
{
    public HostDisplayMode DisplayMode { get; init; } = HostDisplayMode.Windowed;
    public int MonitorIndex { get; init; }
    public bool IsVsyncEnabled { get; init; }
    public int TargetFps { get; init; } = 60;
    public int BackgroundFpsCap { get; init; }
    public double FastForwardMultiplier { get; init; } = 2.0;
    public HostHudAnchorMode HudAnchorMode { get; init; } = HostHudAnchorMode.SafeFourByThree;
    public HostVideoFilterMode VideoFilterMode { get; init; } = HostVideoFilterMode.Nearest;
    public bool IsTrueColor24BitEnabled { get; init; } = true;
    public bool IsDitheringEnabled { get; init; }
    public HostScreenshotCaptureMode ScreenshotCaptureMode { get; init; } = HostScreenshotCaptureMode.CleanNative;
    public bool IsFmvActive { get; init; }
    public HostFrameRateMode FrameRateMode { get; init; } = HostFrameRateMode.Enhanced;
    public double UiTextScale { get; init; } = 1.0;
    public bool IsFpsOverlayEnabled { get; init; }
    public bool IsCleanCaptureModeEnabled { get; init; }
}
