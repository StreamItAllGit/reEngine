// @module: reEngine.Host.Core.HostViewportGeometry
// @purpose: Deterministic source overscan cropping, aspect-correct destination viewport layout, and window-to-content pointer mapping.
// @depends-on: System, reEngine.Host.Contracts
// @exports: HostViewportGeometry

using reEngine.Host.Contracts;

namespace reEngine.Host.Core;

/// <summary>
/// Computes overscan-cropped source rectangles, aspect-preserving destination rectangles, and normalized pointer coordinates.
/// </summary>
public static class HostViewportGeometry
{
    internal const byte Surface0R = 14;
    internal const byte Surface0G = 14;
    internal const byte Surface0B = 16;
    internal const byte Surface0A = 255;

    /// <summary>
    /// Computes the clamped source crop rectangle inside <paramref name="contentWidth"/> x <paramref name="contentHeight"/>
    /// for the supplied <paramref name="crop"/>, guaranteeing dimensions of at least <c>1x1</c>.
    /// </summary>
    /// <param name="contentWidth">Full uncropped content width in pixels.</param>
    /// <param name="contentHeight">Full uncropped content height in pixels.</param>
    /// <param name="crop">Requested top, bottom, left, and right overscan trim counts in pixels.</param>
    /// <returns>Clamped source rectangle guaranteed to fit within the content buffer with width and height &gt;= 1.</returns>
    public static HostViewportRect ComputeSourceCropRect(int contentWidth, int contentHeight, HostOverscanCrop crop)
    {
        int w = Math.Max(1, contentWidth);
        int h = Math.Max(1, contentHeight);
        HostOverscanCrop clamped = crop.ClampForContent(w, h);
        int croppedW = Math.Max(1, w - clamped.Left - clamped.Right);
        int croppedH = Math.Max(1, h - clamped.Top - clamped.Bottom);
        return new HostViewportRect(clamped.Left, clamped.Top, croppedW, croppedH);
    }

    /// <summary>
    /// Computes a centered letterboxed or pillarboxed destination rectangle inside <paramref name="dstWidth"/> x <paramref name="dstHeight"/>
    /// honoring <paramref name="mode"/> and optional <paramref name="contentPixelAspectRatio"/>.
    /// </summary>
    /// <param name="srcWidth">Source content width in pixels.</param>
    /// <param name="srcHeight">Source content height in pixels.</param>
    /// <param name="dstWidth">Destination surface or window width in pixels.</param>
    /// <param name="dstHeight">Destination surface or window height in pixels.</param>
    /// <param name="mode">Target aspect ratio and scaling policy.</param>
    /// <param name="contentPixelAspectRatio">Optional explicit pixel aspect ratio override used in <see cref="HostAspectRatioMode.NativePar"/>.</param>
    /// <returns>Centered destination viewport rectangle inside <paramref name="dstWidth"/> x <paramref name="dstHeight"/>.</returns>
    public static HostViewportRect ComputeDestinationRect(
        int srcWidth,
        int srcHeight,
        int dstWidth,
        int dstHeight,
        HostAspectRatioMode mode,
        double? contentPixelAspectRatio = null)
    {
        if (dstWidth <= 0 || dstHeight <= 0)
        {
            return new HostViewportRect(0, 0, 0, 0);
        }

        int sw = Math.Max(1, srcWidth);
        int sh = Math.Max(1, srcHeight);
        int fitW;
        int fitH;

        if (mode == HostAspectRatioMode.SquareInteger)
        {
            int intScale = Math.Min(dstWidth / sw, dstHeight / sh);
            if (intScale >= 1)
            {
                fitW = sw * intScale;
                fitH = sh * intScale;
            }
            else
            {
                (fitW, fitH) = FitAspect(dstWidth, dstHeight, sw / (double)sh);
            }
        }
        else
        {
            double targetAspect = ResolveTargetDisplayAspect(sw, sh, mode, contentPixelAspectRatio);
            (fitW, fitH) = FitAspect(dstWidth, dstHeight, targetAspect);
        }

        int x = Math.Max(0, (dstWidth - fitW) / 2);
        int y = Math.Max(0, (dstHeight - fitH) / 2);
        return new HostViewportRect(x, y, fitW, fitH);
    }

    /// <summary>
    /// Maps a window-space pixel coordinate (<paramref name="winX"/>, <paramref name="winY"/>) into normalized <c>[0..1]</c>
    /// content coordinates when the point lies inside <paramref name="destRect"/>.
    /// </summary>
    /// <param name="winX">Horizontal coordinate in destination pixel space.</param>
    /// <param name="winY">Vertical coordinate in destination pixel space.</param>
    /// <param name="destRect">Active destination viewport rectangle.</param>
    /// <param name="normalizedX">Mapped horizontal coordinate in <c>[0..1]</c>, or <c>-1</c> when outside.</param>
    /// <param name="normalizedY">Mapped vertical coordinate in <c>[0..1]</c>, or <c>-1</c> when outside.</param>
    /// <returns><c>true</c> if (<paramref name="winX"/>, <paramref name="winY"/>) lies inside <paramref name="destRect"/>; otherwise <c>false</c>.</returns>
    public static bool TryMapWindowToContentNormalized(
        int winX,
        int winY,
        HostViewportRect destRect,
        out float normalizedX,
        out float normalizedY)
    {
        if (destRect.Width <= 0 || destRect.Height <= 0 ||
            winX < destRect.X || winX >= destRect.X + destRect.Width ||
            winY < destRect.Y || winY >= destRect.Y + destRect.Height)
        {
            normalizedX = -1f;
            normalizedY = -1f;
            return false;
        }

        normalizedX = Math.Clamp((winX - destRect.X) / (float)destRect.Width, 0f, 1f);
        normalizedY = Math.Clamp((winY - destRect.Y) / (float)destRect.Height, 0f, 1f);
        return true;
    }

    internal static HostViewportRect ClampSourceRect(int srcWidth, int srcHeight, HostViewportRect srcCropRect)
    {
        int x = Math.Clamp(srcCropRect.X, 0, Math.Max(0, srcWidth - 1));
        int y = Math.Clamp(srcCropRect.Y, 0, Math.Max(0, srcHeight - 1));
        int maxW = Math.Max(1, srcWidth - x);
        int maxH = Math.Max(1, srcHeight - y);
        int w = srcCropRect.Width <= 0 ? maxW : Math.Clamp(srcCropRect.Width, 1, maxW);
        int h = srcCropRect.Height <= 0 ? maxH : Math.Clamp(srcCropRect.Height, 1, maxH);
        return new HostViewportRect(x, y, w, h);
    }

    internal static HostViewportRect ClampDestinationRect(int dstWidth, int dstHeight, HostViewportRect destRect)
    {
        if (dstWidth <= 0 || dstHeight <= 0 || destRect.Width <= 0 || destRect.Height <= 0)
        {
            return new HostViewportRect(0, 0, 0, 0);
        }

        int x0 = Math.Clamp(destRect.X, 0, dstWidth);
        int y0 = Math.Clamp(destRect.Y, 0, dstHeight);
        int x1 = Math.Clamp(destRect.X + destRect.Width, 0, dstWidth);
        int y1 = Math.Clamp(destRect.Y + destRect.Height, 0, dstHeight);
        return new HostViewportRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    internal static bool TryGetWindowPixelSize(IntPtr sdlWindow, int fallbackW, int fallbackH, out int winW, out int winH)
    {
        if (sdlWindow == IntPtr.Zero)
        {
            winW = fallbackW;
            winH = fallbackH;
            return winW > 0 && winH > 0;
        }

        if (Sdl3Native.SDL_GetWindowSizeInPixels(sdlWindow, out int pw, out int ph) != 0)
        {
            winW = pw;
            winH = ph;
            return winW > 0 && winH > 0;
        }

        if (Sdl3Native.SDL_GetWindowSize(sdlWindow, out int lw, out int lh) != 0)
        {
            winW = lw;
            winH = lh;
            return winW > 0 && winH > 0;
        }

        winW = fallbackW;
        winH = fallbackH;
        return winW > 0 && winH > 0;
    }

    private static double ResolveTargetDisplayAspect(int sw, int sh, HostAspectRatioMode mode, double? contentPixelAspectRatio)
    {
        switch (mode)
        {
            case HostAspectRatioMode.FourByThree:
                return 4.0 / 3.0;
            case HostAspectRatioMode.NativeFit:
                return sw / (double)sh;
            case HostAspectRatioMode.HorPlus16By9:
                return 16.0 / 9.0;
            default:
            {
                double defaultPar = (sw == 256 && sh == 240) ? (8.0 / 7.0) : 1.0;
                double par = (contentPixelAspectRatio is double cpar && double.IsFinite(cpar) && cpar > 0.0)
                    ? cpar
                    : defaultPar;
                return (sw / (double)sh) * par;
            }
        }
    }

    private static (int FitW, int FitH) FitAspect(int dstWidth, int dstHeight, double targetAspect)
    {
        if (!double.IsFinite(targetAspect) || targetAspect <= 0.0)
        {
            return (dstWidth, dstHeight);
        }

        double dstAspect = dstWidth / (double)dstHeight;
        if (Math.Abs(dstAspect - targetAspect) < 1e-6)
        {
            return (dstWidth, dstHeight);
        }

        if (dstAspect > targetAspect)
        {
            int fitW = Math.Clamp((int)Math.Round(dstHeight * targetAspect), 1, dstWidth);
            return (fitW, dstHeight);
        }

        int fitH = Math.Clamp((int)Math.Round(dstWidth / targetAspect), 1, dstHeight);
        return (dstWidth, fitH);
    }
}
