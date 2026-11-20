// @module: reEngine.App.Ui.LibraryDisplayAudioPanel
// @purpose: Dark Slate v0.6.0 Execution Resilience, Display/Filter, and Audio quick-control sub-panel inside LibraryShelfPanel.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.Core.Updates, reEngine.Host.Contracts
// @exports: LibraryDisplayAudioPanel

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App.Ui;

internal sealed class LibraryDisplayAudioPanel : Panel
{
    private readonly Label _lblDisplay;
    private readonly Label _lblAspect;
    private readonly Label _lblFilter;
    private readonly Label _lblShot;
    private readonly Label _lblDriver;
    private readonly Label _lblAudioFilter;
    private bool _suppressNotifications;
    private HostAudioFilterPreset _lastAppliedPreset = HostAudioFilterPreset.Off;
    private bool _audioPresetModified;

    public CheckBox PreloadMediaToRamCheckBox { get; }
    public CheckBox FastBootCheckBox { get; }
    public Label SafeModeBadgeLabel { get; }
    public Button ResetSafeModeButton { get; }
    public ComboBox DisplayModeCombo { get; }
    public ComboBox AspectRatioCombo { get; }
    public ComboBox VideoFilterCombo { get; }
    public CheckBox VsyncCheckBox { get; }
    public CheckBox TrueColorCheckBox { get; }
    public CheckBox DitheringCheckBox { get; }
    public ComboBox ScreenshotModeCombo { get; }
    public ComboBox AudioDriverCombo { get; }
    public CheckBox AudioMutedCheckBox { get; }
    public CheckBox MuteOnFocusLossCheckBox { get; }
    public ComboBox AudioFilterPresetCombo { get; }

    public event Action? SettingsChanged;
    public event Action? DisplayModeChanged;
    public event Action? ResetSafeModeRequested;

    public LibraryDisplayAudioPanel()
    {
        Name = "Panel_DisplayAudioControls";
        BackColor = ReEngineTheme.Surface2;
        AutoScroll = true;

        PreloadMediaToRamCheckBox = CreateCheck("Chk_PreloadMediaToRam", "Preload Media to RAM");
        FastBootCheckBox = CreateCheck("Chk_FastBoot", "Fast Boot");
        SafeModeBadgeLabel = new Label
        {
            Name = "Lbl_SafeModeBadge",
            Text = "SAFE MODE: OFF",
            ForeColor = ReEngineTheme.AccentSuccess,
            BackColor = ReEngineTheme.Surface3,
            Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false
        };
        ResetSafeModeButton = ReEngineTheme.CreateActionButton("Btn_ResetSafeMode", "Reset Safe Mode", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        ResetSafeModeButton.Click += (_, _) => ResetSafeModeRequested?.Invoke();

        _lblDisplay = CreateLabel("Display:");
        DisplayModeCombo = CreateCombo("Cmb_DisplayMode", ["windowed", "borderlessFullscreen", "exclusiveFullscreen"]);
        _lblAspect = CreateLabel("Aspect:");
        AspectRatioCombo = CreateCombo("Cmb_AspectRatio", ["nativePar", "fourByThree", "squareInteger", "nativeFit", "horPlus16By9", "horPlus21By9"]);
        _lblFilter = CreateLabel("Filter:");
        VideoFilterCombo = CreateCombo("Cmb_VideoFilter", ["nearest", "bilinear", "sharpBilinear", "scanlines", "crtLite", "lcdGrid", "xbrz", "ntscComposite"]);

        VsyncCheckBox = CreateCheck("Chk_Vsync", "VSync");
        TrueColorCheckBox = CreateCheck("Chk_TrueColor", "24-bit Color");
        DitheringCheckBox = CreateCheck("Chk_Dithering", "Dithering");
        _lblShot = CreateLabel("Capture:");
        ScreenshotModeCombo = CreateCombo("Cmb_ScreenshotMode", ["cleanNative", "filteredComposite"]);

        _lblDriver = CreateLabel("Audio:");
        AudioDriverCombo = CreateCombo("Cmb_AudioDriver", ["auto", "wasapi", "pipewire", "alsa", "coreaudio", "directsound"]);
        AudioMutedCheckBox = CreateCheck("Chk_AudioMuted", "Mute Audio");
        MuteOnFocusLossCheckBox = CreateCheck("Chk_MuteOnFocusLoss", "Mute on Focus Loss");
        _lblAudioFilter = CreateLabel("Audio Filter:");
        AudioFilterPresetCombo = CreateCombo("Cmb_AudioFilterPreset", ["off", "crtWarm", "handheldSpeaker"]);

        void RaiseChanged() { if (!_suppressNotifications) SettingsChanged?.Invoke(); }
        PreloadMediaToRamCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        FastBootCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        DisplayModeCombo.SelectedIndexChanged += (_, _) => { if (!_suppressNotifications) { DisplayModeChanged?.Invoke(); RaiseChanged(); } };
        AspectRatioCombo.SelectedIndexChanged += (_, _) => RaiseChanged();
        VideoFilterCombo.SelectedIndexChanged += (_, _) => RaiseChanged();
        VsyncCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        TrueColorCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        DitheringCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        ScreenshotModeCombo.SelectedIndexChanged += (_, _) => RaiseChanged();
        AudioDriverCombo.SelectedIndexChanged += (_, _) => RaiseChanged();
        AudioMutedCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        MuteOnFocusLossCheckBox.CheckedChanged += (_, _) => RaiseChanged();
        AudioFilterPresetCombo.SelectedIndexChanged += (_, _) => { if (!_suppressNotifications) { _audioPresetModified = true; RaiseChanged(); } };

        Controls.AddRange(new Control[]
        {
            PreloadMediaToRamCheckBox, FastBootCheckBox, SafeModeBadgeLabel, ResetSafeModeButton,
            _lblDisplay, DisplayModeCombo, _lblAspect, AspectRatioCombo, _lblFilter, VideoFilterCombo,
            VsyncCheckBox, TrueColorCheckBox, DitheringCheckBox, _lblShot, ScreenshotModeCombo,
            _lblDriver, AudioDriverCombo, AudioMutedCheckBox, MuteOnFocusLossCheckBox, _lblAudioFilter, AudioFilterPresetCombo
        });
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public HostDisplayMode SelectedDisplayMode => HostDisplayModeCodec.Parse(DisplayModeCombo.SelectedItem as string);
    public HostAspectRatioMode SelectedAspectRatioMode => HostAspectRatioModeCodec.Parse(AspectRatioCombo.SelectedItem as string);
    public HostVideoFilterMode SelectedVideoFilterMode => HostVideoFilterModeCodec.Parse(VideoFilterCombo.SelectedItem as string);
    public HostScreenshotCaptureMode SelectedScreenshotMode => HostScreenshotCaptureModeCodec.Parse(ScreenshotModeCombo.SelectedItem as string);
    public string? SelectedAudioDriver => string.Equals(AudioDriverCombo.SelectedItem as string, "auto", StringComparison.OrdinalIgnoreCase) ? null : AudioDriverCombo.SelectedItem as string;
    public HostAudioFilterPreset SelectedAudioFilterPreset => HostAudioFilterPresetCodec.Parse(AudioFilterPresetCombo.SelectedItem as string);

    public void SetDisplayModeSilently(HostDisplayMode mode)
    {
        _suppressNotifications = true;
        try { SelectItem(DisplayModeCombo, HostDisplayModeCodec.ToCanonicalId(mode)); }
        finally { _suppressNotifications = false; }
    }

    public void ApplyBundle(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        _suppressNotifications = true;
        try
        {
            ReEngineSettings c = bundle.Core;
            ReEngineVideoSettings v = bundle.Video;
            ReEngineAudioSettings a = bundle.Audio;

            PreloadMediaToRamCheckBox.Checked = c.ShouldPreloadMediaToRam;
            FastBootCheckBox.Checked = c.IsFastBootEnabled;
            UpdateSafeModeVisual(c.IsSafeModeActive);

            HostDisplayMode dm = v.IsFullscreen
                ? (string.Equals(v.DisplayMode, "exclusiveFullscreen", StringComparison.OrdinalIgnoreCase) ? HostDisplayMode.ExclusiveFullscreen : HostDisplayMode.BorderlessFullscreen)
                : HostDisplayMode.Windowed;
            SelectItem(DisplayModeCombo, HostDisplayModeCodec.ToCanonicalId(dm));
            SelectItem(AspectRatioCombo, HostAspectRatioModeCodec.ToCanonicalId(HostAspectRatioModeCodec.Parse(v.AspectRatioMode)));
            SelectItem(VideoFilterCombo, HostVideoFilterModeCodec.ToCanonicalId(HostVideoFilterModeCodec.Parse(v.VideoFilterMode)));
            VsyncCheckBox.Checked = v.IsVsyncEnabled;
            TrueColorCheckBox.Checked = v.IsTrueColor24BitEnabled;
            DitheringCheckBox.Checked = v.IsDitheringEnabled;
            SelectItem(ScreenshotModeCombo, HostScreenshotCaptureModeCodec.ToCanonicalId(HostScreenshotCaptureModeCodec.Parse(v.ScreenshotCaptureMode)));

            SelectItem(AudioDriverCombo, string.IsNullOrWhiteSpace(a.AudioDriver) ? "auto" : a.AudioDriver.Trim().ToLowerInvariant());
            AudioMutedCheckBox.Checked = a.IsAudioMuted;
            MuteOnFocusLossCheckBox.Checked = a.ShouldMuteOnFocusLoss;
            _lastAppliedPreset = HostAudioFilterPresetCodec.DetectPreset(a.AudioLowPassHz, a.AudioHighPassHz);
            _audioPresetModified = false;
            SelectItem(AudioFilterPresetCombo, PresetToId(_lastAppliedPreset));
        }
        finally { _suppressNotifications = false; }
    }

    public void WriteToBundle(ReEngineSettingsBundle bundle, bool fullscreenChecked)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ReEngineSettings c = bundle.Core;
        ReEngineVideoSettings v = bundle.Video;
        ReEngineAudioSettings a = bundle.Audio;

        c.ShouldPreloadMediaToRam = PreloadMediaToRamCheckBox.Checked;
        c.IsFastBootEnabled = FastBootCheckBox.Checked;

        HostDisplayMode dm = SelectedDisplayMode;
        v.IsFullscreen = fullscreenChecked && dm != HostDisplayMode.Windowed ? true : dm != HostDisplayMode.Windowed;
        v.DisplayMode = HostDisplayModeCodec.ToCanonicalId(v.IsFullscreen ? (dm == HostDisplayMode.Windowed ? HostDisplayMode.BorderlessFullscreen : dm) : HostDisplayMode.Windowed);
        v.AspectRatioMode = HostAspectRatioModeCodec.ToCanonicalId(SelectedAspectRatioMode);
        v.VideoFilterMode = HostVideoFilterModeCodec.ToCanonicalId(SelectedVideoFilterMode);
        v.IsVsyncEnabled = VsyncCheckBox.Checked;
        v.IsTrueColor24BitEnabled = TrueColorCheckBox.Checked;
        v.IsDitheringEnabled = DitheringCheckBox.Checked;
        v.ScreenshotCaptureMode = HostScreenshotCaptureModeCodec.ToCanonicalId(SelectedScreenshotMode);

        a.AudioDriver = SelectedAudioDriver;
        a.IsAudioMuted = AudioMutedCheckBox.Checked;
        a.ShouldMuteOnFocusLoss = MuteOnFocusLossCheckBox.Checked;
        if (_audioPresetModified || SelectedAudioFilterPreset != _lastAppliedPreset)
        {
            (int lp, int hp) = HostAudioFilterPresetCodec.ToCutoffsHz(SelectedAudioFilterPreset);
            a.AudioLowPassHz = lp;
            a.AudioHighPassHz = hp;
            _lastAppliedPreset = SelectedAudioFilterPreset;
            _audioPresetModified = false;
        }
    }

    private void UpdateSafeModeVisual(bool isSafeMode)
    {
        SafeModeBadgeLabel.Text = isSafeMode ? "SAFE MODE: ACTIVE" : "SAFE MODE: OFF";
        SafeModeBadgeLabel.ForeColor = isSafeMode ? ReEngineTheme.AccentWarning : ReEngineTheme.AccentSuccess;
        ResetSafeModeButton.Enabled = isSafeMode;
    }

    private void LayoutChildren()
    {
        int maxRight = Math.Max(300, ClientSize.Width - 10);
        int y = 8;
        y = PlaceGroups(10, y, maxRight,
            [(PreloadMediaToRamCheckBox, 168, 0)],
            [(FastBootCheckBox, 94, 0)],
            [(SafeModeBadgeLabel, 128, 22)],
            [(ResetSafeModeButton, 126, 24)]);
        y = PlaceGroups(10, y, maxRight,
            [(_lblDisplay, 56, 0), (DisplayModeCombo, 134, 24)],
            [(_lblAspect, 56, 0), (AspectRatioCombo, 112, 24)],
            [(_lblFilter, 46, 0), (VideoFilterCombo, 114, 24)]);
        y = PlaceGroups(10, y, maxRight,
            [(VsyncCheckBox, 68, 0)],
            [(TrueColorCheckBox, 108, 0)],
            [(DitheringCheckBox, 90, 0)],
            [(_lblShot, 64, 0), (ScreenshotModeCombo, 128, 24)]);
        _ = PlaceGroups(10, y, maxRight,
            [(_lblDriver, 50, 0), (AudioDriverCombo, 94, 24)],
            [(AudioMutedCheckBox, 106, 0)],
            [(MuteOnFocusLossCheckBox, 158, 0)],
            [(_lblAudioFilter, 88, 0), (AudioFilterPresetCombo, 122, 24)]);
    }

    private static int PlaceGroups(int left, int startY, int maxRight, params (Control Ctrl, int Width, int Height)[][] groups)
    {
        int x = left, y = startY;
        const int rowStep = 30;
        foreach ((Control Ctrl, int Width, int Height)[] group in groups)
        {
            int groupW = group.Sum(g => g.Width + 4);
            if (x > left && x + groupW > maxRight) { x = left; y += rowStep; }
            foreach ((Control ctrl, int w, int h) in group)
            {
                int ch = h > 0 ? h : 22;
                int yOff = h > 0 ? 0 : 3;
                ctrl.Bounds = new Rectangle(x, y + yOff, w, ch);
                x += w + 4;
            }
            x += 8;
        }
        return y + rowStep;
    }

    private static void SelectItem(ComboBox combo, string canonical)
    {
        int idx = combo.FindStringExact(canonical);
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string PresetToId(HostAudioFilterPreset preset) => preset switch
    {
        HostAudioFilterPreset.CrtWarm => "crtWarm",
        HostAudioFilterPreset.HandheldSpeaker => "handheldSpeaker",
        _ => "off"
    };

    private static Label CreateLabel(string text) =>
        new() { Text = text, ForeColor = ReEngineTheme.TextMuted, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

    private static CheckBox CreateCheck(string name, string text) =>
        new() { Name = name, Text = text, ForeColor = ReEngineTheme.TextPrimary, AutoSize = false, UseMnemonic = false };

    private static ComboBox CreateCombo(string name, string[] items)
    {
        var cmb = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ReEngineTheme.Surface1, ForeColor = ReEngineTheme.TextPrimary, FlatStyle = FlatStyle.Flat };
        cmb.Items.AddRange(items);
        if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        return cmb;
    }
}
