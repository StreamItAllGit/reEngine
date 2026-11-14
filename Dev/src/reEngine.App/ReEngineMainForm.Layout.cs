// @module: reEngine.App.ReEngineMainForm.Layout
// @purpose: Standard 09 Dark Slate control builders, 2-line tooltips, and dynamic Resize-driven layout bounds for ReEngineMainForm.
// @depends-on: System, System.Drawing, System.Windows.Forms
// @exports: ReEngineMainForm (partial: InitializeControls, LayoutControls)

using System.Drawing;
using System.Windows.Forms;

namespace reEngine.App;

internal sealed partial class ReEngineMainForm
{
    private static readonly Color Surface0 = Color.FromArgb(14, 14, 16);
    private static readonly Color Surface1 = Color.FromArgb(18, 18, 20);
    private static readonly Color Surface2 = Color.FromArgb(24, 24, 27);
    private static readonly Color Surface3 = Color.FromArgb(39, 39, 42);
    private static readonly Color BorderDefault = Color.FromArgb(63, 63, 70);
    private static readonly Color TextPrimary = Color.FromArgb(244, 244, 245);
    private static readonly Color TextMuted = Color.FromArgb(161, 161, 170);
    private static readonly Color AccentCyan = Color.FromArgb(56, 189, 248);
    private static readonly Color AccentSuccess = Color.FromArgb(16, 185, 129);
    private static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
    private static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);

    private Panel _headerPanel = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblDaemonPill = null!;
    private Label _lblAssocPill = null!;
    private Label _lblUpdatePill = null!;
    private Label _lblEnginesHeader = null!;
    private ListView _lstEngines = null!;
    private Panel _controlsCard = null!;
    private Label _lblControlsHeader = null!;
    private CheckBox _chkCheckUpdates = null!;
    private CheckBox _chkAutoStage = null!;
    private Button _btnOpenPackage = null!;
    private Button _btnRegisterAssoc = null!;
    private Button _btnCheckUpdates = null!;
    private Button _btnSaveSettings = null!;
    private Button _btnClose = null!;
    private Label _lblStatusText = null!;
    private ToolTip _tooltip = null!;

    private void InitializeControls()
    {
        _tooltip = new ToolTip
        {
            BackColor = Surface2,
            ForeColor = TextPrimary,
            AutoPopDelay = 8000,
            InitialDelay = 300,
            ReshowDelay = 150
        };

        _headerPanel = new Panel { BackColor = Surface2 };
        _lblTitle = new Label
        {
            Text = "reEngine — Suite Router & Engine Manager",
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };
        _lblSubtitle = new Label
        {
            Text = "Side-by-side platform engine tracks, .recomp associations, and non-blocking updates",
            Font = new Font("Segoe UI", 9f),
            ForeColor = TextMuted,
            AutoSize = true
        };

        _lblDaemonPill = CreateStatusPill("ZERO-DAEMON", AccentCyan);
        _lblAssocPill = CreateStatusPill("UNREGISTERED", AccentWarning);
        _lblUpdatePill = CreateStatusPill("UP TO DATE", AccentCyan);
        _headerPanel.Controls.AddRange(new Control[] { _lblTitle, _lblSubtitle, _lblDaemonPill, _lblAssocPill, _lblUpdatePill });

        _lblEnginesHeader = new Label
        {
            Text = "Installed Platform Engines",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true
        };

        _lstEngines = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            OwnerDraw = true,
            BackColor = Surface1,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _lstEngines.DrawColumnHeader += static (_, e) =>
        {
            using var bg = new SolidBrush(Surface2);
            using var pen = new Pen(BorderDefault);
            using var headerFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            e.Graphics.FillRectangle(bg, e.Bounds);
            e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(
                e.Graphics,
                e.Header?.Text ?? string.Empty,
                headerFont,
                Rectangle.Inflate(e.Bounds, -6, 0),
                TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        _lstEngines.DrawItem += static (_, e) => e.DrawDefault = true;
        _lstEngines.DrawSubItem += static (_, e) => e.DrawDefault = true;
        _lstEngines.Columns.Add("Engine ID", 140);
        _lstEngines.Columns.Add("Installed Version", 130);
        _lstEngines.Columns.Add("Engine Track", 120);
        _lstEngines.Columns.Add("Install Directory", 400);

        _controlsCard = new Panel { BackColor = Surface2 };
        _lblControlsHeader = new Label
        {
            Text = "Update & Association Controls",
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            ForeColor = TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };
        _chkCheckUpdates = new CheckBox
        {
            Text = "Check for compatible updates on launch",
            ForeColor = TextPrimary,
            AutoSize = true
        };
        _chkAutoStage = new CheckBox
        {
            Text = "Automatically stage compatible patches",
            ForeColor = TextPrimary,
            AutoSize = true
        };

        _btnOpenPackage = CreateActionButton("Open .recomp Package", AccentCyan, Surface0);
        _btnOpenPackage.Click += (_, _) => OnOpenPackageClicked();

        _btnRegisterAssoc = CreateActionButton("Register .recomp Association", Surface3, TextPrimary);
        _btnRegisterAssoc.Click += (_, _) => OnRegisterAssociationClicked();

        _btnCheckUpdates = CreateActionButton("Check For Updates", Surface3, TextPrimary);
        _btnCheckUpdates.Click += (_, _) => OnCheckUpdatesClicked();

        _btnSaveSettings = CreateActionButton("Save Settings", Surface3, TextPrimary);
        _btnSaveSettings.Click += (_, _) => OnSaveSettingsClicked();

        _btnClose = CreateActionButton("Close", Surface1, TextPrimary);
        _btnClose.Click += (_, _) => { ExitCode = 0; Close(); };

        _lblStatusText = new Label
        {
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 8.75f),
            AutoEllipsis = true
        };

        SetTwoLineTooltip(_btnOpenPackage, "Open .recomp Package [Ctrl+O]", "Resolves the highest compatible engine track and detects data\\rom preload media.");
        SetTwoLineTooltip(_btnRegisterAssoc, "Register .recomp Association [Ctrl+R]", "Writes the .recomp open handler to HKCU or virtual_registry.json in --test-mode.");
        SetTwoLineTooltip(_btnCheckUpdates, "Check For Updates [F5]", "Checks for compatible track patches while preserving older breaking engine tracks.");
        SetTwoLineTooltip(_btnSaveSettings, "Save Settings [Ctrl+S]", "Saves update and manager preferences atomically to settings.json.");
        SetTwoLineTooltip(_chkCheckUpdates, "Launch Update Checks", "Toggles non-blocking compatible update checks when launching .recomp modules.");
        SetTwoLineTooltip(_chkAutoStage, "Auto-Stage Compatible Patches", "Downloads and SHA-256 verifies compatible track patches without overwriting breaking tracks.");

        _controlsCard.Controls.AddRange(new Control[]
        {
            _lblControlsHeader, _chkCheckUpdates, _chkAutoStage,
            _btnOpenPackage, _btnRegisterAssoc, _btnCheckUpdates, _btnSaveSettings
        });

        Controls.AddRange(new Control[] { _headerPanel, _lblEnginesHeader, _lstEngines, _controlsCard, _btnClose, _lblStatusText });
    }

    private void LayoutControls()
    {
        if (_headerPanel is null) return;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        const int pad = 16;

        _headerPanel.Bounds = new Rectangle(0, 0, w, 72);
        _lblTitle.Location = new Point(pad, 12);
        _lblSubtitle.Location = new Point(pad, 40);

        int pillRight = w - pad;
        foreach (Label pill in new[] { _lblUpdatePill, _lblAssocPill, _lblDaemonPill })
        {
            int measured = TextRenderer.MeasureText(pill.Text ?? string.Empty, pill.Font).Width;
            int pillWidth = Math.Max(96, Math.Max(pill.PreferredWidth, measured) + 18);
            pill.Size = new Size(pillWidth, 24);
            pillRight -= pill.Width;
            pill.Location = new Point(pillRight, 14);
            pillRight -= 8;
        }

        _lblEnginesHeader.Location = new Point(pad, 88);
        int controlsHeight = 136;
        int footerHeight = 44;
        int listTop = 114;
        int listHeight = Math.Max(140, h - listTop - controlsHeight - footerHeight - pad * 2);
        _lstEngines.Bounds = new Rectangle(pad, listTop, w - pad * 2, listHeight);
        if (_lstEngines.Columns.Count == 4)
        {
            int used = 130 + 130 + 110 + 24;
            _lstEngines.Columns[3].Width = Math.Max(180, _lstEngines.Width - used);
        }

        int cardTop = _lstEngines.Bottom + 12;
        _controlsCard.Bounds = new Rectangle(pad, cardTop, w - pad * 2, controlsHeight);
        _lblControlsHeader.Location = new Point(16, 12);
        _chkCheckUpdates.Location = new Point(16, 40);
        _chkAutoStage.Location = new Point(310, 40);

        int btnY = 80;
        _btnOpenPackage.Bounds = new Rectangle(16, btnY, 176, 34);
        _btnRegisterAssoc.Bounds = new Rectangle(200, btnY, 204, 34);
        _btnCheckUpdates.Bounds = new Rectangle(412, btnY, 152, 34);
        _btnSaveSettings.Bounds = new Rectangle(_controlsCard.Width - 136, btnY, 120, 34);

        int footerY = h - 42;
        _btnClose.Bounds = new Rectangle(w - pad - 96, footerY, 96, 30);
        _lblStatusText.Bounds = new Rectangle(pad, footerY + 6, w - pad * 2 - 110, 22);
    }

    private static Label CreateStatusPill(string text, Color accent) => new()
    {
        Text = text,
        ForeColor = accent,
        BackColor = Surface3,
        Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        UseMnemonic = false
    };

    private static Button CreateActionButton(string text, Color back, Color fore)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btn.FlatAppearance.BorderColor = BorderDefault;
        return btn;
    }

    private void SetTwoLineTooltip(Control control, string title, string description) =>
        _tooltip.SetToolTip(control, $"{title}\n{description}");
}
