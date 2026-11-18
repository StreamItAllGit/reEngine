// @module: reEngine.Host.Ui.FirstRunMediaGateView
// @purpose: Clean-room retail media verification prompt rendered directly onto Foundation.Core.PixelOverlayCanvas.
// @depends-on: System, System.Collections.Generic, System.Drawing, Foundation.Core, reEngine.Host.Contracts
// @exports: FirstRunMediaGateView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Clean-room retail media gate prompting optical/ROM verification without bundling retail assets.
/// </summary>
public sealed class FirstRunMediaGateView
{
    private Rectangle _browseRect;
    private Rectangle _demoBypassRect;

    public bool IsVisible { get; set; }
    public string StatusMessage { get; set; } = "Drop retail media into data\\rom\\ or browse to verify locally.";
    public string ExpectedSha256 { get; set; } = "UNSPECIFIED";
    public Func<string?>? OnBrowseRequested { get; set; }
    public string? VerifiedMediaPath { get; private set; }

    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsVisible) return;

        int cardW = Math.Min(460, Math.Max(280, canvas.Width - 40));
        int cardH = Math.Min(176, Math.Max(140, canvas.Height - 40));
        int x = Math.Max(8, (canvas.Width - cardW) / 2);
        int y = Math.Max(8, (canvas.Height - cardH) / 2);
        var card = new Rectangle(x, y, cardW, cardH);

        HostOverlayLayoutHelpers.DrawPanel(canvas, card, HostOverlayPalette.Surface2Glass, HostOverlayPalette.AccentWarning);
        canvas.DrawText(card.X + 14, card.Y + 14, $"MEDIA GATE - {descriptor.ModuleTitle}", HostOverlayPalette.AccentWarning, 1);
        canvas.DrawText(card.X + 14, card.Y + 32, $"Source: {descriptor.MediaLabel}", HostOverlayPalette.TextPrimary, 1);

        string shaPreview = ExpectedSha256.Length > 28 ? ExpectedSha256[..28] + "..." : ExpectedSha256;
        canvas.DrawText(card.X + 14, card.Y + 48, $"Expected SHA-256: {shaPreview}", HostOverlayPalette.TextMuted, 1);
        canvas.DrawText(card.X + 14, card.Y + 66, StatusMessage, HostOverlayPalette.TextPrimary, 1);

        int btnY = card.Bottom - 40;
        _browseRect = HostOverlayLayoutHelpers.DrawPillButton(
            canvas,
            new Rectangle(card.X + 14, btnY, 150, 26),
            "Browse Media...",
            true,
            HostOverlayPalette.AccentCyan);
        _demoBypassRect = HostOverlayLayoutHelpers.DrawPillButton(
            canvas,
            new Rectangle(card.X + 176, btnY, 146, 26),
            "Continue Session",
            false,
            HostOverlayPalette.AccentCyan);
    }

    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!IsVisible) return Array.Empty<HostWidgetBounds>();
        return
        [
            new("MediaGate_BrowseButton", "Button", "Browse Media", _browseRect, true),
            new("MediaGate_DemoBypassButton", "Button", "Continue Session", _demoBypassRect, true)
        ];
    }

    public bool TryHandleClick(int x, int y, string? targetId)
    {
        if (!IsVisible) return false;
        if (string.Equals(targetId, "MediaGate_BrowseButton", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(targetId) && _browseRect.Contains(x, y)))
        {
            string? picked = OnBrowseRequested?.Invoke();
            if (!string.IsNullOrWhiteSpace(picked))
            {
                VerifiedMediaPath = picked;
                StatusMessage = $"Verified: {Path.GetFileName(picked)}";
                IsVisible = false;
            }
            return true;
        }

        if (string.Equals(targetId, "MediaGate_DemoBypassButton", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(targetId) && _demoBypassRect.Contains(x, y)))
        {
            IsVisible = false;
            return true;
        }

        return false;
    }
}
