// @module: reEngine.App.Ui.DetailsRightDrawer
// @purpose: Collapsible Slide-Over Right Drawer inspecting execution mode, video/audio presets, Safe Mode status, toolchain, AOT cache, and pins.
// @depends-on: System, System.Drawing, System.IO, System.Windows.Forms, reEngine.App, reEngine.Core.Resolution, reEngine.Core.Updates, reEngine.Host.Contracts
// @exports: DetailsRightDrawer

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App.Ui;

internal sealed class DetailsRightDrawer : Panel
{
    public Label TitleLabel { get; }
    public Button CloseButton { get; }
    public Label TargetSummaryLabel { get; }
    public Label TierBadgeLabel { get; }
    public Label PresentationSummaryLabel { get; }
    public Label PreservedTiersLabel { get; }
    public Label RomStatusLabel { get; }
    public Label PinnedTrackLabel { get; }
    public Button ClearPinButton { get; }

    public DetailsRightDrawer(Action onCloseRequested, Action onClearPinRequested)
    {
        Name = "Drawer_Panel";
        BackColor = ReEngineTheme.Surface2;
        Visible = false;

        TitleLabel = new Label
        {
            Name = "Drawer_Title",
            Text = "Inspector Drawer",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        CloseButton = ReEngineTheme.CreateActionButton("Drawer_CloseButton", "Close", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        CloseButton.Click += (_, _) => onCloseRequested();

        TargetSummaryLabel = new Label
        {
            Name = "Drawer_TargetSummary",
            Text = "Target: Suite Default",
            ForeColor = ReEngineTheme.TextPrimary,
            UseMnemonic = false
        };

        TierBadgeLabel = new Label
        {
            Name = "Drawer_TierBadge",
            Text = "Execution Mode: Precompiled AOT | Safe Mode: OFF",
            ForeColor = ReEngineTheme.AccentCyan,
            UseMnemonic = false
        };

        PresentationSummaryLabel = new Label
        {
            Name = "Drawer_PresentationSummary",
            Text = "Display: Windowed | Filter: Nearest | Audio: Off / Raw",
            ForeColor = ReEngineTheme.TextPrimary,
            UseMnemonic = false
        };

        PreservedTiersLabel = new Label
        {
            Name = "Drawer_PreservedTiers",
            Text = "Decompilation Pipeline: Router Mode (Lightweight)\r\nAOT Cache: 0 precompiled module(s) cached.",
            ForeColor = ReEngineTheme.TextMuted,
            UseMnemonic = false
        };

        RomStatusLabel = new Label
        {
            Name = "Drawer_RomStatus",
            Text = "No retail discs, ROMs, or BIOS files are bundled.",
            ForeColor = ReEngineTheme.TextMuted,
            UseMnemonic = false
        };

        PinnedTrackLabel = new Label
        {
            Name = "Drawer_PinnedTrack",
            Text = "Pinned Tracks: None (auto latest matching track)",
            ForeColor = ReEngineTheme.TextMuted,
            UseMnemonic = false
        };

        ClearPinButton = ReEngineTheme.CreateActionButton("Drawer_ClearPinButton", "Clear Pinned Track", ReEngineTheme.Surface3, ReEngineTheme.AccentWarning);
        ClearPinButton.Click += (_, _) => onClearPinRequested();

        Controls.AddRange(new Control[]
        {
            TitleLabel, CloseButton, TargetSummaryLabel, TierBadgeLabel, PresentationSummaryLabel,
            PreservedTiersLabel, RomStatusLabel, PinnedTrackLabel, ClearPinButton
        });
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public void Populate(
        InstalledEngineCandidate? selected,
        bool jitBringup,
        string selectedBackend,
        int selectedScale,
        DecompDispatchStatusSummary decomp,
        AotCacheStatusSummary aot,
        string dataDir,
        string pinnedSummary,
        ReEngineSettingsBundle? settingsBundle = null)
    {
        TitleLabel.Text = "Inspector";
        TargetSummaryLabel.Text = selected is null
            ? "Target: Suite Default (select an engine in Engines tab for details)"
            : $"Engine: {selected.EngineId} v{selected.Version}\r\nLocation: {selected.EngineDirectory}";

        bool safeMode = settingsBundle?.Core.IsSafeModeActive == true;
        string safeLabel = safeMode ? "ACTIVE" : "OFF";
        TierBadgeLabel.Text = $"Execution: {(jitBringup ? "Live JIT Fallback" : "Precompiled AOT")} | Safe Mode: {safeLabel}\r\nBackend: {selectedBackend} ({selectedScale}x)";
        TierBadgeLabel.ForeColor = safeMode ? ReEngineTheme.AccentWarning : ReEngineTheme.AccentCyan;

        if (settingsBundle is not null)
        {
            HostDisplayMode dm = settingsBundle.Video.IsFullscreen
                ? HostDisplayModeCodec.Parse(settingsBundle.Video.DisplayMode)
                : HostDisplayMode.Windowed;
            HostVideoFilterMode vf = HostVideoFilterModeCodec.Parse(settingsBundle.Video.VideoFilterMode);
            HostAudioFilterPreset af = HostAudioFilterPresetCodec.DetectPreset(
                settingsBundle.Audio.AudioLowPassHz, settingsBundle.Audio.AudioHighPassHz);
            string muteTag = settingsBundle.Audio.IsAudioMuted ? " [Muted]" : string.Empty;
            PresentationSummaryLabel.Text =
                $"Display: {HostDisplayModeCodec.ToDisplayLabel(dm)} | Filter: {HostVideoFilterModeCodec.ToDisplayLabel(vf)}\r\nAudio Filter: {HostAudioFilterPresetCodec.ToDisplayLabel(af)}{muteTag}";
        }
        else
        {
            PresentationSummaryLabel.Text = "Display: Windowed | Filter: Nearest\r\nAudio Filter: Off / Raw";
        }

        PreservedTiersLabel.Text = $"Decompilation Pipeline: {(decomp.ToolchainAvailable ? "Offline CLI Toolchain Ready (Full)" : "Router Mode (Lightweight)")}\r\nAOT Cache: {aot.AssemblyCount} precompiled module(s) cached.";
        RomStatusLabel.Text = $"Personal Media Folder: {Path.Combine(dataDir, "rom")}\r\nNo retail discs, ROMs, or BIOS files are bundled.";
        PinnedTrackLabel.Text = $"Pinned Tracks: {pinnedSummary}";
    }

    private void LayoutChildren()
    {
        int w = Math.Max(220, Width);
        int h = Math.Max(260, Height);
        int innerW = w - 24;
        TitleLabel.Location = new Point(12, 12);
        CloseButton.Bounds = new Rectangle(w - 76, 10, 64, 26);
        TargetSummaryLabel.Bounds = new Rectangle(12, 42, innerW, 44);
        TierBadgeLabel.Bounds = new Rectangle(12, 90, innerW, 38);
        PresentationSummaryLabel.Bounds = new Rectangle(12, 132, innerW, 40);
        PreservedTiersLabel.Bounds = new Rectangle(12, 176, innerW, 40);
        RomStatusLabel.Bounds = new Rectangle(12, 220, innerW, 46);
        PinnedTrackLabel.Bounds = new Rectangle(12, 270, innerW, Math.Max(32, h - 320));
        ClearPinButton.Bounds = new Rectangle(12, h - 42, innerW, 32);
    }
}
