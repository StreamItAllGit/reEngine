// @module: reEngine.Host.Ui.FirstRunMediaGateView
// @purpose: Full-screen Deep Slate Glass clean-room retail media gate with drag-drop zone, module picker, and demo cart launch.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.IO, Foundation.Core, reEngine.Host.Contracts
// @exports: FirstRunMediaGateView

using System.Drawing;
using Foundation.Core;
using reEngine.Host.Contracts;

namespace reEngine.Host.Ui;

/// <summary>
/// Full-screen Deep Slate Glass clean-room media gate prompting optical/ROM verification without bundling retail assets.
/// </summary>
public sealed class FirstRunMediaGateView
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".bin", ".iso", ".chd", ".nes", ".fds", ".7z", ".zip", ".recomp"
    };

    private Rectangle _dropZoneRect, _browseRect, _selectModuleRect, _launchDemoRect;
    private double _uiTextScale = 1.0;

    /// <summary>Gets or sets whether the full-screen Media Gate overlay is currently visible.</summary>
    public bool IsVisible { get; set; }

    /// <summary>Gets or sets the live status prompt displayed inside the drop zone.</summary>
    public string StatusMessage { get; set; } = "Drop retail media into data\\rom\\ or drag-and-drop onto this window.";

    /// <summary>Gets or sets an optional validation error banner displayed inside the drop zone.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the expected SHA-256 verification digest displayed in the header.</summary>
    public string ExpectedSha256 { get; set; } = "UNSPECIFIED";

    /// <summary>Gets or sets the active overlay UI text scale (<c>0.75..2.0</c>).</summary>
    public double UiTextScale { get => _uiTextScale; set => _uiTextScale = HostOverlayTextRenderer.ClampUiTextScale(value); }

    /// <summary>Gets or sets the currently focused Media Gate button identifier for controller navigation.</summary>
    public string? FocusedControlId { get; set; }

    /// <summary>Gets or sets the file-picker callback invoked when <c>Select Media...</c> is activated.</summary>
    public Func<string?>? OnBrowseRequested { get; set; }

    /// <summary>Gets or sets the module-picker callback invoked when <c>Select .recomp Module...</c> is activated.</summary>
    public Func<string?>? OnBrowseModuleRequested { get; set; }

    /// <summary>Gets or sets an optional drag-and-drop validation callback returning a non-empty error if rejected.</summary>
    public Func<string, string?>? OnFileDropped { get; set; }

    /// <summary>Gets or sets an optional callback invoked when <c>Launch Demo / Test Cart</c> is activated.</summary>
    public Action? OnDemoCartRequested { get; set; }

    /// <summary>Gets the verified retail media path selected or dropped by the user.</summary>
    public string? VerifiedMediaPath { get; private set; }

    /// <summary>Gets the selected <c>.recomp</c> module path chosen or dropped by the user.</summary>
    public string? SelectedModulePath { get; private set; }

    /// <summary>Gets whether the user explicitly chose <c>Launch Demo / Test Cart</c>.</summary>
    public bool IsDemoCartSelected { get; private set; }

    /// <summary>Renders the full-screen Deep Slate Glass Media Gate onto <paramref name="canvas"/>.</summary>
    public void Render(PixelOverlayCanvas canvas, HostDrawerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsVisible) return;

        double scale = UiTextScale;
        int lineH = HostOverlayTextRenderer.GetLineHeight(scale);
        int rowH = Math.Max(HostOverlayTextRenderer.GetRowHeight(scale), 28);
        canvas.FillRect(0, 0, canvas.Width, canvas.Height, HostOverlayPalette.DeepSlateBackdrop);

        int margin = Math.Clamp(Math.Min(canvas.Width, canvas.Height) / 14, 10, 36);
        var card = new Rectangle(margin, margin, Math.Max(260, canvas.Width - (margin * 2)), Math.Max(180, canvas.Height - (margin * 2)));
        HostOverlayLayoutHelpers.DrawPanel(canvas, card, HostOverlayPalette.Surface2Glass, HostOverlayPalette.AccentCyan, 2);

        int pad = 16;
        int innerW = Math.Max(120, card.Width - (pad * 2));
        int y = card.Y + pad;
        HostOverlayTextRenderer.DrawText(canvas, card.X + pad, y,
            HostOverlayTextRenderer.FitText($"CLEAN-ROOM MEDIA GATE - {descriptor.ModuleTitle}", innerW, scale),
            HostOverlayPalette.AccentWarning, scale);
        y += lineH + 4;
        HostOverlayTextRenderer.DrawText(canvas, card.X + pad, y,
            HostOverlayTextRenderer.FitText($"Target Source: {descriptor.MediaLabel}", innerW, scale),
            HostOverlayPalette.TextPrimary, scale);
        y += lineH + 2;
        HostOverlayTextRenderer.DrawText(canvas, card.X + pad, y,
            HostOverlayTextRenderer.FitText($"Expected SHA-256: {ExpectedSha256}", innerW, scale),
            HostOverlayPalette.TextMuted, scale);
        y += lineH + 10;

        int btnGap = 8;
        int btnAreaH = innerW >= 540 ? rowH + 8 : (rowH * 2) + btnGap + 8;
        int dropH = Math.Max(72, card.Bottom - pad - btnAreaH - y - 10);
        _dropZoneRect = new Rectangle(card.X + pad, y, innerW, dropH);
        bool hasError = !string.IsNullOrWhiteSpace(ErrorMessage);
        RgbaColor dropBorder = IsFocused("MediaGate_DropZone")
            ? HostOverlayPalette.FocusRing
            : (hasError ? HostOverlayPalette.AccentWarning : HostOverlayPalette.AccentCyan);
        HostOverlayLayoutHelpers.DrawPanel(canvas, _dropZoneRect, HostOverlayPalette.DropZoneFill, dropBorder, IsFocused("MediaGate_DropZone") ? 2 : 1);

        int dzY = _dropZoneRect.Y + 12;
        int dzW = Math.Max(80, _dropZoneRect.Width - 24);
        HostOverlayTextRenderer.DrawText(canvas, _dropZoneRect.X + 12, dzY,
            HostOverlayTextRenderer.FitText("DRAG & DROP MEDIA OR .RECOMP FILE HERE", dzW, scale),
            HostOverlayPalette.AccentCyan, scale);
        dzY += lineH + 4;
        HostOverlayTextRenderer.DrawText(canvas, _dropZoneRect.X + 12, dzY,
            HostOverlayTextRenderer.FitText("Supported: .cue, .bin, .iso, .chd, .nes, .fds, .7z, .zip, .recomp", dzW, scale),
            HostOverlayPalette.TextMuted, scale);
        dzY += lineH + 8;

        string activeBanner = hasError ? ErrorMessage! : StatusMessage;
        int bannerH = Math.Max(rowH, Math.Min(lineH + 14, Math.Max(22, _dropZoneRect.Bottom - dzY - 8)));
        if (_dropZoneRect.Bottom - dzY >= 20)
        {
            HostOverlayLayoutHelpers.DrawBanner(canvas, new(_dropZoneRect.X + 12, dzY, dzW, bannerH), activeBanner,
                hasError ? HostOverlayPalette.WarnFill : HostOverlayPalette.Surface3Glass,
                hasError ? HostOverlayPalette.AccentWarning : HostOverlayPalette.Border,
                hasError ? HostOverlayPalette.AccentWarning : HostOverlayPalette.TextPrimary, scale);
        }

        int btnY = card.Bottom - pad - rowH;
        if (innerW >= 540)
        {
            int btnW = (innerW - (btnGap * 2)) / 3;
            _browseRect = new(card.X + pad, btnY, btnW, rowH);
            _selectModuleRect = new(card.X + pad + btnW + btnGap, btnY, btnW, rowH);
            _launchDemoRect = new(card.X + pad + ((btnW + btnGap) * 2), btnY, innerW - ((btnW + btnGap) * 2), rowH);
        }
        else
        {
            int topRowY = btnY - rowH - btnGap;
            int halfW = (innerW - btnGap) / 2;
            _browseRect = new(card.X + pad, topRowY, halfW, rowH);
            _selectModuleRect = new(card.X + pad + halfW + btnGap, topRowY, innerW - halfW - btnGap, rowH);
            _launchDemoRect = new(card.X + pad, btnY, innerW, rowH);
        }

        _ = HostOverlayLayoutHelpers.DrawPillButton(canvas, _browseRect, "Select Media...", true, HostOverlayPalette.AccentCyan, scale, IsFocused("MediaGate_BrowseButton"));
        _ = HostOverlayLayoutHelpers.DrawPillButton(canvas, _selectModuleRect, "Select .recomp Module...", false, HostOverlayPalette.AccentCyan, scale, IsFocused("MediaGate_SelectModuleButton"));
        _ = HostOverlayLayoutHelpers.DrawPillButton(canvas, _launchDemoRect, "Launch Demo / Test Cart", false, HostOverlayPalette.AccentSuccess, scale, IsFocused("MediaGate_LaunchDemoButton"));
    }

    /// <summary>Returns automation and controller-navigation bounds for the visible Media Gate.</summary>
    public IReadOnlyList<HostWidgetBounds> GetWidgets()
    {
        if (!IsVisible) return Array.Empty<HostWidgetBounds>();
        return
        [
            new("MediaGate_DropZone", "Panel", "Drag and Drop Media Zone", _dropZoneRect, true),
            new("MediaGate_BrowseButton", "Button", "Select Media...", _browseRect, true),
            new("MediaGate_SelectModuleButton", "Button", "Select .recomp Module...", _selectModuleRect, true),
            new("MediaGate_LaunchDemoButton", "Button", "Launch Demo / Test Cart", _launchDemoRect, true)
        ];
    }

    /// <summary>Validates and ingests a file dropped onto the Media Gate window.</summary>
    public bool HandleDroppedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorMessage = "No dropped file path provided.";
            StatusMessage = ErrorMessage;
            return false;
        }

        string trimmed = path.Trim();
        string ext = Path.GetExtension(trimmed);
        if (!SupportedExtensions.Contains(ext))
        {
            ErrorMessage = $"Unsupported file '{ext}'. Drop .cue, .bin, .iso, .chd, .nes, .fds, .7z, .zip, or .recomp.";
            StatusMessage = ErrorMessage;
            return false;
        }

        string? rejection = OnFileDropped?.Invoke(trimmed);
        if (!string.IsNullOrWhiteSpace(rejection))
        {
            ErrorMessage = rejection.Trim();
            StatusMessage = ErrorMessage;
            return false;
        }

        ErrorMessage = null;
        IsDemoCartSelected = false;
        if (string.Equals(ext, ".recomp", StringComparison.OrdinalIgnoreCase))
        {
            SelectedModulePath = trimmed;
            StatusMessage = $"Loaded module: {Path.GetFileName(trimmed)}";
        }
        else
        {
            VerifiedMediaPath = trimmed;
            StatusMessage = $"Verified: {Path.GetFileName(trimmed)}";
        }
        IsVisible = false;
        return true;
    }

    /// <summary>Processes a pointer click or controller activation inside the Media Gate.</summary>
    public bool TryHandleClick(int x, int y, string? targetId)
    {
        if (!IsVisible) return false;
        if (Match(x, y, targetId, "MediaGate_BrowseButton", _browseRect))
        {
            AcceptPickedPath(OnBrowseRequested?.Invoke(), isModule: false);
            return true;
        }

        if (Match(x, y, targetId, "MediaGate_SelectModuleButton", _selectModuleRect))
        {
            AcceptPickedPath(OnBrowseModuleRequested?.Invoke(), isModule: true);
            return true;
        }

        if (Match(x, y, targetId, "MediaGate_LaunchDemoButton", _launchDemoRect))
        {
            ErrorMessage = null;
            IsDemoCartSelected = true;
            StatusMessage = "Demo / Test Cart active.";
            IsVisible = false;
            OnDemoCartRequested?.Invoke();
            return true;
        }

        if (Match(x, y, targetId, "MediaGate_DropZone", _dropZoneRect))
        {
            AcceptPickedPath(OnBrowseRequested?.Invoke(), isModule: false);
            return true;
        }

        return false;
    }

    private void AcceptPickedPath(string? picked, bool isModule)
    {
        if (string.IsNullOrWhiteSpace(picked)) return;
        if (OnFileDropped is not null)
        {
            _ = HandleDroppedFile(picked);
            return;
        }

        ErrorMessage = null;
        IsDemoCartSelected = false;
        if (isModule)
        {
            SelectedModulePath = picked;
            StatusMessage = $"Loaded module: {Path.GetFileName(picked)}";
        }
        else
        {
            VerifiedMediaPath = picked;
            StatusMessage = $"Verified: {Path.GetFileName(picked)}";
        }
        IsVisible = false;
    }

    private bool IsFocused(string id) =>
        !string.IsNullOrEmpty(FocusedControlId) && string.Equals(FocusedControlId, id, StringComparison.OrdinalIgnoreCase);

    private static bool Match(int x, int y, string? targetId, string expectedId, Rectangle rect) =>
        !string.IsNullOrEmpty(targetId) ? string.Equals(targetId, expectedId, StringComparison.OrdinalIgnoreCase) : !rect.IsEmpty && rect.Contains(x, y);
}
