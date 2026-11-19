// @module: reEngine.App.Ui.LibraryShelfPanel
// @purpose: Library sub-page shelf panel with backend/scale/JIT selectors and .recomp package launcher controls.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.Core.Updates
// @exports: LibraryShelfPanel

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App.Ui;

internal sealed class LibraryShelfPanel : Panel
{
    private bool _suppressNotifications;

    public Label HeaderLabel { get; }
    public ComboBox BackendCombo { get; }
    public ComboBox ScaleCombo { get; }
    public CheckBox FullscreenCheckBox { get; }
    public CheckBox JitBringupCheckBox { get; }
    public Label IntakeHintLabel { get; }
    public Button OpenPackageButton { get; }
    public Button VerifyPackageButton { get; }

    public event Action? FullscreenToggled;

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

        var lblBackend = new Label { Text = "Backend:", ForeColor = ReEngineTheme.TextMuted, AutoSize = true, UseMnemonic = false };
        BackendCombo = new ComboBox
        {
            Name = "Cmb_Backend",
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        foreach (string id in HostRenderBackendIds.AllWindowsBackends) BackendCombo.Items.Add(id);

        var lblScale = new Label { Text = "Scale:", ForeColor = ReEngineTheme.TextMuted, AutoSize = true, UseMnemonic = false };
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
        FullscreenCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_suppressNotifications) FullscreenToggled?.Invoke();
        };

        Controls.AddRange(new Control[]
        {
            HeaderLabel, lblBackend, BackendCombo, lblScale, ScaleCombo, FullscreenCheckBox,
            JitBringupCheckBox, IntakeHintLabel, OpenPackageButton, VerifyPackageButton
        });

        Resize += (_, _) => LayoutChildren(lblBackend, lblScale);
        LayoutChildren(lblBackend, lblScale);
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
        string? backend = HostRenderBackendIds.TryNormalize(settings.BackendId, out string normalized) ? normalized : settings.BackendId;
        int scale = settings.ResolutionScale ?? settings.PreferredResolutionScale ?? 2;
        bool jitBringup = ReEngineAppStatusHelper.GetJitBringup(settings);

        _suppressNotifications = true;
        try
        {
            int idx = !string.IsNullOrWhiteSpace(backend) ? BackendCombo.FindStringExact(backend) : 0;
            BackendCombo.SelectedIndex = idx >= 0 ? idx : 0;
            ScaleCombo.SelectedItem = scale switch { 1 => "1x", 4 => "4x", _ => "2x" };
            FullscreenCheckBox.Checked = settings.IsFullscreen;
            JitBringupCheckBox.Checked = jitBringup;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    private void LayoutChildren(Label lblBackend, Label lblScale)
    {
        int w = Math.Max(300, Width);
        HeaderLabel.Location = new Point(16, 16);
        lblBackend.Location = new Point(16, 52);
        BackendCombo.Bounds = new Rectangle(84, 48, 136, 26);
        lblScale.Location = new Point(236, 52);
        ScaleCombo.Bounds = new Rectangle(284, 48, 80, 26);
        FullscreenCheckBox.Location = new Point(380, 51);
        JitBringupCheckBox.Location = new Point(16, 88);
        IntakeHintLabel.Bounds = new Rectangle(16, 120, w - 32, 34);
        OpenPackageButton.Bounds = new Rectangle(16, 162, 210, 34);
        VerifyPackageButton.Bounds = new Rectangle(238, 162, 180, 34);
    }
}
