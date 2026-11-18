// @module: reEngine.App.Ui.EnginesShelfPanel
// @purpose: Side-by-side platform engines shelf panel with track/alias listing, pin/rollback actions, and update checks.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Windows.Forms, reEngine.Core.Resolution, reEngine.Core.Updates
// @exports: EnginesShelfPanel

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App.Ui;

internal sealed class EnginesShelfPanel : Panel
{
    private bool _suppressNotifications;

    public ListView EnginesListView { get; }
    public CheckBox CheckUpdatesCheckBox { get; }
    public CheckBox AutoStageCheckBox { get; }
    public ComboBox InstallProfileCombo { get; }
    public Button RegisterAssocButton { get; }
    public Button CheckUpdatesButton { get; }
    public Button RollbackTrackButton { get; }

    public EnginesShelfPanel(
        Action onRegisterAssoc,
        Action onCheckUpdates,
        Action<InstalledEngineCandidate?> onRollback,
        Action<InstalledEngineCandidate?> onSelectionChanged,
        Action<bool, bool, string>? onOptionsChanged = null)
    {
        BackColor = ReEngineTheme.Surface1;
        Name = "Shelf_Engines";

        EnginesListView = new ListView
        {
            Name = "Lst_Engines",
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = ReEngineTheme.Surface0,
            ForeColor = ReEngineTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        EnginesListView.Columns.Add("Engine ID", 130);
        EnginesListView.Columns.Add("Version", 100);
        EnginesListView.Columns.Add("Track", 90);
        EnginesListView.Columns.Add("Directory", 320);
        EnginesListView.SelectedIndexChanged += (_, _) => onSelectionChanged(SelectedCandidate);

        CheckUpdatesCheckBox = new CheckBox { Name = "Chk_CheckUpdates", Text = "Check updates on launch", ForeColor = ReEngineTheme.TextPrimary, AutoSize = false, AutoEllipsis = true };
        AutoStageCheckBox = new CheckBox { Name = "Chk_AutoStage", Text = "Auto-stage .part patches", ForeColor = ReEngineTheme.TextPrimary, AutoSize = false, AutoEllipsis = true };

        InstallProfileCombo = new ComboBox
        {
            Name = "Cmb_InstallProfile",
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        InstallProfileCombo.Items.AddRange(new object[] { "Lightweight", "Full" });

        RegisterAssocButton = ReEngineTheme.CreateActionButton("Btn_RegisterAssoc", "Register Associations", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        RegisterAssocButton.Click += (_, _) => onRegisterAssoc();

        CheckUpdatesButton = ReEngineTheme.CreateActionButton("Btn_CheckUpdates", "Check For Updates", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        CheckUpdatesButton.Click += (_, _) => onCheckUpdates();

        RollbackTrackButton = ReEngineTheme.CreateActionButton("Btn_RollbackTrack", "Pin Selected Track", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        RollbackTrackButton.Click += (_, _) => onRollback(SelectedCandidate);

        void NotifyOptions()
        {
            if (_suppressNotifications || onOptionsChanged is null) return;
            string profile = InstallProfileCombo.SelectedItem as string ?? "Lightweight";
            onOptionsChanged(CheckUpdatesCheckBox.Checked, AutoStageCheckBox.Checked, profile);
        }
        CheckUpdatesCheckBox.CheckedChanged += (_, _) => NotifyOptions();
        AutoStageCheckBox.CheckedChanged += (_, _) => NotifyOptions();
        InstallProfileCombo.SelectedIndexChanged += (_, _) => NotifyOptions();

        Controls.AddRange(new Control[] { EnginesListView, CheckUpdatesCheckBox, AutoStageCheckBox, InstallProfileCombo, RegisterAssocButton, CheckUpdatesButton, RollbackTrackButton });
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public InstalledEngineCandidate? SelectedCandidate =>
        EnginesListView.SelectedItems.Count > 0 ? EnginesListView.SelectedItems[0].Tag as InstalledEngineCandidate : null;

    public void Populate(IReadOnlyList<InstalledEngineCandidate> engines, ReEngineSettings settings)
    {
        bool checkUpdates = settings.ShouldCheckUpdates;
        bool autoStage = settings.ShouldAutoStagePatches;
        string profileLabel = string.Equals(ReEngineAppStatusHelper.GetInstallProfile(settings), "full", StringComparison.OrdinalIgnoreCase)
            ? "Full"
            : "Lightweight";

        _suppressNotifications = true;
        try
        {
            EnginesListView.Items.Clear();
            foreach (InstalledEngineCandidate eng in engines)
            {
                var item = new ListViewItem(eng.EngineId) { Tag = eng };
                item.SubItems.Add($"v{eng.Version}");
                item.SubItems.Add($"{eng.Version.Major}.{eng.Version.Minor}.x");
                item.SubItems.Add(eng.EngineDirectory);
                EnginesListView.Items.Add(item);
            }

            CheckUpdatesCheckBox.Checked = checkUpdates;
            AutoStageCheckBox.Checked = autoStage;
            InstallProfileCombo.SelectedItem = profileLabel;
        }
        finally
        {
            _suppressNotifications = false;
        }
    }

    private void LayoutChildren()
    {
        int w = Math.Max(340, Width);
        int h = Math.Max(220, Height);
        int listH = Math.Max(110, h - 104);
        EnginesListView.Bounds = new Rectangle(16, 12, w - 32, listH);
        if (EnginesListView.Columns.Count == 4)
        {
            EnginesListView.Columns[3].Width = Math.Max(140, EnginesListView.Width - 340);
        }

        int row1Y = EnginesListView.Bottom + 10;
        InstallProfileCombo.Bounds = new Rectangle(w - 138, row1Y - 2, 122, 26);
        int chkAvail = Math.Max(180, InstallProfileCombo.Left - 24);
        int chkW1 = Math.Min(182, chkAvail / 2);
        int chkW2 = Math.Max(90, chkAvail - chkW1 - 8);
        CheckUpdatesCheckBox.Bounds = new Rectangle(16, row1Y, chkW1, 24);
        AutoStageCheckBox.Bounds = new Rectangle(16 + chkW1 + 8, row1Y, chkW2, 24);

        int row2Y = row1Y + 32;
        int totalBtnAvail = Math.Max(300, w - 32 - 16);
        int btnW1 = Math.Min(176, Math.Max(120, (int)Math.Round(totalBtnAvail * 0.36)));
        int btnW2 = Math.Min(156, (totalBtnAvail - btnW1) / 2);
        int btnW3 = Math.Min(156, totalBtnAvail - btnW1 - btnW2);
        RegisterAssocButton.Bounds = new Rectangle(16, row2Y, btnW1, 32);
        CheckUpdatesButton.Bounds = new Rectangle(16 + btnW1 + 8, row2Y, btnW2, 32);
        RollbackTrackButton.Bounds = new Rectangle(16 + btnW1 + 8 + btnW2 + 8, row2Y, btnW3, 32);
    }
}
