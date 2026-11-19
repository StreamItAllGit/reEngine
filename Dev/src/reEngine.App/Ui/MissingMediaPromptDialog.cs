// @module: reEngine.App.Ui.MissingMediaPromptDialog
// @purpose: Dark Slate BYO media locator & disambiguation modal defaulting to use-in-place with opt-in containment-checked data\rom\ copy.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.IO, System.Windows.Forms, reEngine.Core.Decomp
// @exports: MissingMediaPromptDialog

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Decomp;

namespace reEngine.App.Ui;

internal sealed class MissingMediaPromptDialog : Form
{
    private readonly ListBox _candidateList;
    private readonly TextBox _pathBox;
    private readonly CheckBox _chkCopyIntoRom;
    private readonly Label _hintLabel;
    private readonly Button _btnBrowse;
    private readonly Button _btnUse;
    private readonly Button _btnCancel;

    public string SelectedMediaPath => _pathBox.Text.Trim();
    public bool ShouldCopyIntoModuleRom => _chkCopyIntoRom.Checked;

    public MissingMediaPromptDialog(
        string moduleId,
        string targetRomDirectory,
        SuiteMediaDiscoveryResult? discovery)
    {
        Name = "MissingMediaPromptDialog";
        Text = $"Locate Personal Game Media — {moduleId}";
        ClientSize = new Size(580, 390);
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = ReEngineTheme.Surface0;
        ForeColor = ReEngineTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        var lblHeader = new Label
        {
            Text = discovery is { IsAmbiguous: true }
                ? $"Multiple media candidates were found for '{moduleId}'. Choose your BYO disc/ROM image or browse:"
                : $"No local media was found in '{targetRomDirectory}'. Select a discovered file or browse:",
            ForeColor = ReEngineTheme.TextPrimary,
            Bounds = new Rectangle(16, 14, 548, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            UseMnemonic = false
        };

        _candidateList = new ListBox
        {
            Name = "Lst_MediaCandidates",
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false,
            Bounds = new Rectangle(16, 56, 548, 128),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        if (discovery is { Candidates.Count: > 0 })
        {
            foreach (SuiteMediaCandidate c in discovery.Candidates)
            {
                string badge = c.MatchesExpectedSha256 ? "[SHA256 MATCH] " : (c.MatchesModuleSlug ? "[SLUG MATCH] " : string.Empty);
                _candidateList.Items.Add(new CandidateItem(c.FilePath, $"{badge}{c.FilePath}"));
            }
        }
        else
        {
            _candidateList.Items.Add("(No compatible media discovered automatically across suite directories)");
            _candidateList.Enabled = false;
        }

        _pathBox = new TextBox
        {
            Name = "Txt_SelectedMediaPath",
            BackColor = ReEngineTheme.Surface2,
            ForeColor = ReEngineTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Text = discovery?.PrimaryMediaPath ?? discovery?.Candidates.FirstOrDefault()?.FilePath ?? string.Empty,
            Bounds = new Rectangle(16, 196, 424, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _btnBrowse = ReEngineTheme.CreateActionButton("Btn_BrowseMedia", "Browse File...", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        _btnBrowse.Bounds = new Rectangle(450, 194, 114, 28);
        _btnBrowse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnBrowse.Click += (_, _) => OnBrowseClicked();

        _chkCopyIntoRom = new CheckBox
        {
            Name = "Chk_CopyIntoRom",
            Text = $"Copy selected media (+ .cue tracks) into module data\\rom\\ (unchecked = use-in-place)",
            ForeColor = ReEngineTheme.TextPrimary,
            Checked = false,
            Bounds = new Rectangle(16, 234, 548, 24),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            UseMnemonic = false
        };

        _hintLabel = new Label
        {
            Text = "Static recompilation tools never bundle retail discs, ROMs, or BIOS files. Use-in-place avoids duplicating large images unless copying is checked.",
            ForeColor = ReEngineTheme.TextMuted,
            Bounds = new Rectangle(16, 266, 548, 42),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            UseMnemonic = false
        };

        _btnUse = ReEngineTheme.CreateActionButton("Btn_ConfirmMedia", "Use Selected Media", ReEngineTheme.AccentCyan, ReEngineTheme.Surface0);
        _btnUse.Bounds = new Rectangle(356, 332, 128, 32);
        _btnUse.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnUse.Click += (_, _) => OnConfirmClicked();

        _btnCancel = ReEngineTheme.CreateActionButton("Btn_CancelMedia", "Cancel", ReEngineTheme.Surface1, ReEngineTheme.TextPrimary);
        _btnCancel.Bounds = new Rectangle(492, 332, 72, 32);
        _btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _candidateList.SelectedIndexChanged += (_, _) =>
        {
            if (_candidateList.SelectedItem is CandidateItem item) _pathBox.Text = item.FilePath;
        };

        AcceptButton = _btnUse;
        CancelButton = _btnCancel;
        Controls.AddRange(new Control[] { lblHeader, _candidateList, _pathBox, _btnBrowse, _chkCopyIntoRom, _hintLabel, _btnUse, _btnCancel });
    }

    private void OnBrowseClicked()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Personal Disc Image, ROM, or PS-X Executable",
            Filter = "Supported Game Media (*.cue;*.chd;*.iso;*.bin;*.n64;*.z64;*.v64;*.sfc;*.nes;*.gb;*.gba;*.gcm;*.rvz)|*.cue;*.chd;*.iso;*.bin;*.n64;*.z64;*.v64;*.sfc;*.nes;*.gb;*.gba;*.gcm;*.rvz|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dlg.FileName;
    }

    private void OnConfirmClicked()
    {
        if (string.IsNullOrWhiteSpace(SelectedMediaPath) || !File.Exists(SelectedMediaPath))
        {
            MessageBox.Show(this, "Choose or browse to an existing disc image, ROM, or PS-X executable file.", "Media File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record CandidateItem(string FilePath, string Display)
    {
        public override string ToString() => Display;
    }
}
