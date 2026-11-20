// @module: reEngine.App.Ui.LibraryShelfPanel
// @purpose: Library sub-page shelf panel hosting backend/scale/JIT controls, LibraryDisplayAudioPanel, and .recomp package launcher actions.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.Core.Resolution, reEngine.Core.Updates, reEngine.Host.Contracts
// @exports: LibraryShelfPanel

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Contracts;

namespace reEngine.App.Ui;

internal sealed class LibraryShelfPanel : Panel
{
    private readonly Label _lblBackend;
    private readonly Label _lblScale;
    private bool _suppressNotifications;

    public Label HeaderLabel { get; }
    public ComboBox BackendCombo { get; }
    public ComboBox ScaleCombo { get; }
    public CheckBox FullscreenCheckBox { get; }
    public CheckBox JitBringupCheckBox { get; }
    public LibraryDisplayAudioPanel DisplayAudioPanel { get; }
    public Label IntakeHintLabel { get; }
    public Button OpenPackageButton { get; }
    public Button VerifyPackageButton { get; }

    public event Action? FullscreenToggled;
    public event Action? DisplayAudioChanged;
    public event Action? ResetSafeModeRequested;

    public LibraryShelfPanel(Action onOpenPackage, Action<string, int, bool> onSelectionChanged, Action? onVerifyPackage = null)
    {
        BackColor = ReEngineTheme.Surface1;
        Name = "Shelf_Library";

        HeaderLabel = new Label
        {
            Name = "Library_HeaderLabel",
            Text = "Module Library & Clean-Room Launch Routing",
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        _lblBackend = new Label { Text = "Backend:", ForeColor = ReEngineTheme.TextMuted, AutoSize = true, UseMnemonic = false };
        BackendCombo = new ComboBox
        {
            Name = "Cmb_Backend",
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        foreach (string id in HostRenderBackendIds.AllWindowsBackends) BackendCombo.Items.Add(id);

        _lblScale = new Label { Text = "Scale:", ForeColor = ReEngineTheme.TextMuted, AutoSize = true, UseMnemonic = false };
        ScaleCombo = new ComboBox
        {
            Name = "Cmb_ResolutionScale",
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        ScaleCombo.Items.AddRange(new object[] { "1x", "2x", "4x" });

        FullscreenCheckBox = new CheckBox
        {
            Name = "Chk_Fullscreen",
            Text = "Fullscreen",
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        JitBringupCheckBox = new CheckBox
        {
            Name = "Chk_JitBringup",
            Text = "Allow live JIT bring-up fallback when precompiled AOT cache is not built",
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        DisplayAudioPanel = new LibraryDisplayAudioPanel();
        DisplayAudioPanel.DisplayModeChanged += OnPanelDisplayModeChanged;
        DisplayAudioPanel.SettingsChanged += () => { if (!_suppressNotifications) DisplayAudioChanged?.Invoke(); };
        DisplayAudioPanel.ResetSafeModeRequested += () => { if (!_suppressNotifications) ResetSafeModeRequested?.Invoke(); };

        IntakeHintLabel = new Label
        {
            Name = "Library_IntakeHint",
            Text = "Place personal game discs or ROM dumps in data\\rom\\ or locate them anywhere on disk via Bring-Up (no ROMs or BIOS files are bundled).",
            ForeColor = ReEngineTheme.TextMuted,
            AutoEllipsis = true,
            UseMnemonic = false
        };

        OpenPackageButton = ReEngineTheme.CreateActionButton("Btn_OpenPackage", "Open .recomp Package...", ReEngineTheme.AccentCyan, ReEngineTheme.Surface0);
        OpenPackageButton.Click += (_, _) => onOpenPackage();

        VerifyPackageButton = ReEngineTheme.CreateActionButton("Btn_VerifyPackage", "Verify Package...", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        VerifyPackageButton.Click += (_, _) => onVerifyPackage?.Invoke();

        void Notify()
        {
            if (_suppressNotifications) return;
            onSelectionChanged(SelectedBackend, SelectedResolutionScale, JitBringupCheckBox.Checked);
        }
        BackendCombo.SelectedIndexChanged += (_, _) => Notify();
        ScaleCombo.SelectedIndexChanged += (_, _) => Notify();
        JitBringupCheckBox.CheckedChanged += (_, _) => Notify();
        FullscreenCheckBox.CheckedChanged += OnFullscreenCheckedChanged;

        Controls.AddRange(new Control[]
        {
            HeaderLabel, _lblBackend, BackendCombo, _lblScale, ScaleCombo, FullscreenCheckBox,
            JitBringupCheckBox, DisplayAudioPanel, IntakeHintLabel, OpenPackageButton, VerifyPackageButton
        });

        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public string SelectedBackend => BackendCombo.SelectedItem as string ?? HostRenderBackendIds.OpenGl;

    public int SelectedResolutionScale => ScaleCombo.SelectedItem?.ToString() switch
    {
        "1x" => 1,
        "4x" => 4,
        _ => 2
    };

    public void ApplySettings(ReEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ApplySettings(SettingsMigrationHelper.BuildBundleFromLegacyCore(settings));
    }

    public void ApplySettings(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ReEngineVideoSettings v = bundle.Video;
        string? backend = HostRenderBackendIds.TryNormalize(v.BackendId, out string normalized) ? normalized : v.BackendId;
        int scale = v.ResolutionScale;
        bool jitBringup = ReEngineAppStatusHelper.GetJitBringup(bundle.Core);

        _suppressNotifications = true;
        try
        {
            int idx = !string.IsNullOrWhiteSpace(backend) ? BackendCombo.FindStringExact(backend) : 0;
            BackendCombo.SelectedIndex = idx >= 0 ? idx : 0;
            ScaleCombo.SelectedItem = scale switch { 1 => "1x", 4 => "4x", _ => "2x" };
            FullscreenCheckBox.Checked = v.IsFullscreen;
            JitBringupCheckBox.Checked = jitBringup;
            DisplayAudioPanel.ApplyBundle(bundle);
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    public void WriteToBundle(ReEngineSettingsBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        bundle.Video.BackendId = SelectedBackend;
        bundle.Video.ResolutionScale = SelectedResolutionScale;
        ReEngineAppStatusHelper.SetJitBringup(bundle.Core, JitBringupCheckBox.Checked);
        DisplayAudioPanel.WriteToBundle(bundle, FullscreenCheckBox.Checked);
        SettingsMigrationHelper.SyncCoreLegacyMirror(bundle);
    }

    private void OnFullscreenCheckedChanged(object? sender, EventArgs e)
    {
        if (_suppressNotifications) return;
        bool isFs = FullscreenCheckBox.Checked;
        HostDisplayMode nextMode = isFs
            ? (DisplayAudioPanel.SelectedDisplayMode == HostDisplayMode.ExclusiveFullscreen
                ? HostDisplayMode.ExclusiveFullscreen
                : HostDisplayMode.BorderlessFullscreen)
            : HostDisplayMode.Windowed;
        DisplayAudioPanel.SetDisplayModeSilently(nextMode);
        FullscreenToggled?.Invoke();
    }

    private void OnPanelDisplayModeChanged()
    {
        if (_suppressNotifications) return;
        _suppressNotifications = true;
        try { FullscreenCheckBox.Checked = DisplayAudioPanel.SelectedDisplayMode != HostDisplayMode.Windowed; }
        finally { _suppressNotifications = false; }
    }

    private void LayoutChildren()
    {
        int w = Math.Max(320, Width);
        int h = Math.Max(340, Height);
        HeaderLabel.Location = new Point(16, 10);
        _lblBackend.Location = new Point(16, 40);
        BackendCombo.Bounds = new Rectangle(84, 36, 136, 26);
        _lblScale.Location = new Point(236, 40);
        ScaleCombo.Bounds = new Rectangle(284, 36, 80, 26);
        FullscreenCheckBox.Location = new Point(380, 39);
        JitBringupCheckBox.Location = new Point(16, 68);

        int displayPanelH = Math.Clamp(h - 196, 128, 210);
        DisplayAudioPanel.Bounds = new Rectangle(16, 94, w - 32, displayPanelH);
        int hintY = DisplayAudioPanel.Bottom + 8;
        IntakeHintLabel.Bounds = new Rectangle(16, hintY, w - 32, 32);
        int btnY = IntakeHintLabel.Bottom + 6;
        OpenPackageButton.Bounds = new Rectangle(16, btnY, 210, 34);
        VerifyPackageButton.Bounds = new Rectangle(238, btnY, 180, 34);
    }
}
