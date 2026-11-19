// @module: reEngine.App.ReEngineMainForm.Layout
// @purpose: Standard 09 Dark Slate Sub-Page Pill Navigation, status header, modular shelves, and slide-over right drawer layout.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.App.Ui
// @exports: ReEngineMainForm (partial: InitializeControls, LayoutControls, ActivateShelf)

using System.Drawing;
using System.Windows.Forms;
using reEngine.App.Ui;

namespace reEngine.App;

internal enum MainShelfKind
{
    Library = 0,
    Engines = 1,
    BringUp = 2
}

internal sealed partial class ReEngineMainForm
{
    private Panel _headerPanel = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblDaemonPill = null!;
    private Label _lblDecompPill = null!;
    private Label _lblAssocPill = null!;
    private Label _lblUpdatePill = null!;
    private Panel _navStrip = null!;
    private Button _pillLibrary = null!;
    private Button _pillEngines = null!;
    private Button _pillBringUp = null!;
    private Button _btnToggleDrawer = null!;
    private LibraryShelfPanel _libraryShelf = null!;
    private EnginesShelfPanel _enginesShelf = null!;
    private BringUpShelfPanel _bringUpShelf = null!;
    private DetailsRightDrawer _rightDrawer = null!;
    private Button _btnSaveSettings = null!;
    private Button _btnClose = null!;
    private Label _lblStatusText = null!;
    private ToolTip _tooltip = null!;
    private MainShelfKind _activeShelf = MainShelfKind.Library;

    private void InitializeControls()
    {
        _tooltip = new ToolTip { BackColor = ReEngineTheme.Surface2, ForeColor = ReEngineTheme.TextPrimary, AutoPopDelay = 7000 };

        _headerPanel = new Panel { Name = "Header_Panel", BackColor = ReEngineTheme.Surface2 };
        _lblTitle = new Label
        {
            Text = "reEngine — Suite Router & Engine Manager",
            Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        _lblSubtitle = new Label
        {
            Text = "Package Library, Engine Tracks, and Clean-Room Bring-Up",
            Font = new Font("Segoe UI", 9f),
            ForeColor = ReEngineTheme.TextMuted,
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false
        };

        _lblDaemonPill = CreateStatusPill("Pill_ZeroDaemon", "ZERO-DAEMON", ReEngineTheme.AccentCyan);
        _lblDecompPill = CreateStatusPill("Pill_Decomp", "DECOMP: ROUTER", ReEngineTheme.AccentCyan);
        _lblAssocPill = CreateStatusPill("Pill_Assoc", "UNREGISTERED", ReEngineTheme.AccentWarning);
        _lblUpdatePill = CreateStatusPill("Pill_Update", "UP TO DATE", ReEngineTheme.AccentCyan);
        _headerPanel.Controls.AddRange(new Control[] { _lblTitle, _lblSubtitle, _lblDaemonPill, _lblDecompPill, _lblAssocPill, _lblUpdatePill });

        _navStrip = new Panel { Name = "Nav_PillStrip", BackColor = ReEngineTheme.Surface1 };
        _pillLibrary = ReEngineTheme.CreatePillButton("Pill_Library", "Library", true);
        _pillEngines = ReEngineTheme.CreatePillButton("Pill_Engines", "Engines", false);
        _pillBringUp = ReEngineTheme.CreatePillButton("Pill_BringUp", "Bring-Up", false);
        _btnToggleDrawer = ReEngineTheme.CreateActionButton("Btn_ToggleDrawer", "Inspector Drawer", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);

        _pillLibrary.Click += (_, _) => ActivateShelf(MainShelfKind.Library);
        _pillEngines.Click += (_, _) => ActivateShelf(MainShelfKind.Engines);
        _pillBringUp.Click += (_, _) => ActivateShelf(MainShelfKind.BringUp);
        _btnToggleDrawer.Click += (_, _) => ToggleRightDrawer();
        _navStrip.Controls.AddRange(new Control[] { _pillLibrary, _pillEngines, _pillBringUp, _btnToggleDrawer });

        _libraryShelf = new LibraryShelfPanel(OnOpenPackageClicked, OnLibrarySelectionChanged, () => OnVerifyPackageClicked(null));
        _libraryShelf.FullscreenToggled += OnFullscreenToggled;
        _enginesShelf = new EnginesShelfPanel(OnRegisterAssociationClicked, OnCheckUpdatesClicked, OnPinTrackClicked, OnEngineSelected, OnEngineOptionsChanged);
        _bringUpShelf = new BringUpShelfPanel();
        _bringUpShelf.BindActions(OnOpenDecompClicked, OnLocateMediaClicked, () => OnVerifyPackageClicked(_loadedDecompPath), OnRunDecompStageClicked);
        _rightDrawer = new DetailsRightDrawer(() => SetDrawerVisible(false), OnClearPinsClicked);

        _btnSaveSettings = ReEngineTheme.CreateActionButton("Btn_SaveSettings", "Save Settings", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        _btnSaveSettings.Click += (_, _) => OnSaveSettingsClicked();
        _btnClose = ReEngineTheme.CreateActionButton("Btn_Close", "Close", ReEngineTheme.Surface1, ReEngineTheme.TextPrimary);
        _btnClose.Click += (_, _) => { ExitCode = 0; BeginInvoke(Close); };
        _lblStatusText = new Label { Name = "Lbl_StatusText", ForeColor = ReEngineTheme.TextMuted, Font = new Font("Segoe UI", 8.75f), AutoEllipsis = true, UseMnemonic = false };

        _tooltip.SetToolTip(_btnToggleDrawer, "Inspector Drawer\nShows execution mode, decompilation toolchain, AOT cache, media folder, and track pin controls.");

        Controls.AddRange(new Control[]
        {
            _headerPanel, _navStrip, _libraryShelf, _enginesShelf, _bringUpShelf, _rightDrawer, _btnSaveSettings, _btnClose, _lblStatusText
        });
        ActivateShelf(MainShelfKind.Library);
    }

    private void ActivateShelf(MainShelfKind shelf)
    {
        _activeShelf = shelf;
        _libraryShelf.Visible = shelf == MainShelfKind.Library;
        _enginesShelf.Visible = shelf == MainShelfKind.Engines;
        _bringUpShelf.Visible = shelf == MainShelfKind.BringUp;
        ReEngineTheme.StylePill(_pillLibrary, shelf == MainShelfKind.Library);
        ReEngineTheme.StylePill(_pillEngines, shelf == MainShelfKind.Engines);
        ReEngineTheme.StylePill(_pillBringUp, shelf == MainShelfKind.BringUp);
        LayoutControls();
    }

    private void LayoutControls()
    {
        if (_headerPanel is null) return;
        int w = Math.Max(740, ClientSize.Width);
        int h = Math.Max(480, ClientSize.Height);
        const int pad = 16;

        _headerPanel.Bounds = new Rectangle(0, 0, w, 68);

        int pillRight = w - pad;
        foreach (Label pill in new[] { _lblUpdatePill, _lblAssocPill, _lblDecompPill, _lblDaemonPill })
        {
            int measured = TextRenderer.MeasureText(pill.Text ?? string.Empty, pill.Font).Width;
            pill.Size = new Size(Math.Max(92, Math.Max(pill.PreferredWidth, measured) + 16), 24);
            pillRight -= pill.Width;
            pill.Location = new Point(pillRight, 14);
            pillRight -= 6;
        }

        _lblTitle.Bounds = new Rectangle(pad, 10, Math.Max(160, pillRight - pad - 2), 24);
        _lblSubtitle.Bounds = new Rectangle(pad, 38, Math.Max(160, w - (pad * 2)), 18);

        _navStrip.Bounds = new Rectangle(0, 68, w, 42);
        _pillLibrary.Bounds = new Rectangle(pad, 6, 108, 28);
        _pillEngines.Bounds = new Rectangle(pad + 116, 6, 108, 28);
        _pillBringUp.Bounds = new Rectangle(pad + 232, 6, 108, 28);
        _btnToggleDrawer.Bounds = new Rectangle(w - pad - 148, 6, 148, 28);

        int contentTop = 114;
        int footerHeight = 46;
        int contentHeight = Math.Max(220, h - contentTop - footerHeight);
        int drawerWidth = _rightDrawer.Visible ? Math.Min(290, w / 3) : 0;
        int shelfWidth = Math.Max(360, w - (pad * 2) - (drawerWidth > 0 ? drawerWidth + 12 : 0));

        var shelfBounds = new Rectangle(pad, contentTop, shelfWidth, contentHeight);
        _libraryShelf.Bounds = shelfBounds;
        _enginesShelf.Bounds = shelfBounds;
        _bringUpShelf.Bounds = shelfBounds;
        if (_rightDrawer.Visible)
        {
            _rightDrawer.Bounds = new Rectangle(shelfBounds.Right + 12, contentTop, drawerWidth, contentHeight);
            _rightDrawer.BringToFront();
        }

        int footerY = h - 38;
        _btnClose.Bounds = new Rectangle(w - pad - 92, footerY, 92, 28);
        _btnSaveSettings.Bounds = new Rectangle(_btnClose.Left - 124, footerY, 116, 28);
        _lblStatusText.Bounds = new Rectangle(pad, footerY + 5, Math.Max(180, _btnSaveSettings.Left - pad - 10), 20);
    }

    private static Label CreateStatusPill(string name, string text, Color accent) => new()
    {
        Name = name,
        Text = text,
        ForeColor = accent,
        BackColor = ReEngineTheme.Surface3,
        Font = new Font("Segoe UI Semibold", 8.25f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        UseMnemonic = false
    };
}
