// @module: reEngine.App.ReEngineMainForm
// @purpose: Dark Slate WinForms Suite Manager interaction logic, settings persistence, association triggers, and --visible-test showcase capture.
// @depends-on: System, System.Drawing, System.Drawing.Imaging, System.IO, System.Reflection, System.Runtime.InteropServices, System.Windows.Forms, Foundation.Core, reEngine.Core
// @exports: ReEngineMainForm

using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Foundation.Core;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;

namespace reEngine.App;

internal sealed partial class ReEngineMainForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly SandboxEnvironment _sandbox;
    private readonly string _enginesDir;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly IFileAssociationStore _assocStore;
    private readonly Action<string, string> _log;
    private readonly bool _visibleTestShowcase;

    private ReEngineSettings _settings;
    private System.Windows.Forms.Timer? _showcaseTimer;

    public int ExitCode { get; private set; }

    public ReEngineMainForm(
        SandboxEnvironment sandbox,
        string enginesDir,
        UpdateCoordinator updateCoordinator,
        IFileAssociationStore assocStore,
        Action<string, string> log,
        bool visibleTestShowcase = false)
    {
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _enginesDir = enginesDir ?? throw new ArgumentNullException(nameof(enginesDir));
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _assocStore = assocStore ?? throw new ArgumentNullException(nameof(assocStore));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _visibleTestShowcase = visibleTestShowcase;
        _settings = _updateCoordinator.LoadSettings().Value;

        Text = $"reEngine — Suite Router & Engine Manager (v{ReEnginePackagingConstants.Version})";
        ClientSize = new Size(Math.Max(780, _settings.WindowWidth ?? 860), Math.Max(520, _settings.WindowHeight ?? 560));
        MinimumSize = new Size(740, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Surface0;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        TryApplyEmbeddedIcon();
        InitializeControls();
        LayoutControls();
        RefreshAllState();

        if (_visibleTestShowcase)
        {
            ConfigureVisibleTestShowcase();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindows())
        {
            int enabled = 1;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutControls();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showcaseTimer?.Stop();
            _showcaseTimer?.Dispose();
            _showcaseTimer = null;
            _tooltip?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void TryApplyEmbeddedIcon()
    {
        try
        {
            using Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("reEngine.App.reEngine.ico");
            if (iconStream is not null)
            {
                Icon = new Icon(iconStream);
            }
        }
        catch { /* ignore icon load failure */ }
    }

    private void RefreshAllState()
    {
        _lstEngines.Items.Clear();
        var resolver = new EngineVersionResolver();
        IReadOnlyList<InstalledEngineCandidate> engines = resolver.DiscoverInstalledEngines(_enginesDir);
        foreach (InstalledEngineCandidate eng in engines)
        {
            var item = new ListViewItem(eng.EngineId);
            item.SubItems.Add($"v{eng.Version}");
            item.SubItems.Add($"{eng.Version.Major}.{eng.Version.Minor}.x");
            item.SubItems.Add(eng.EngineDirectory);
            _lstEngines.Items.Add(item);
        }

        FileAssociationRecord? assoc = _assocStore.ReadRecompAssociation();
        if (_sandbox.TestMode || _sandbox.IsContainedSandbox)
        {
            _lblAssocPill.Text = assoc is not null ? "VIRTUALIZED" : "UNREGISTERED";
            _lblAssocPill.ForeColor = assoc is not null ? AccentCyan : AccentWarning;
        }
        else
        {
            _lblAssocPill.Text = assoc is not null ? "REGISTERED" : "UNREGISTERED";
            _lblAssocPill.ForeColor = assoc is not null ? AccentSuccess : AccentWarning;
        }

        _chkCheckUpdates.Checked = _settings.ShouldCheckUpdates;
        _chkAutoStage.Checked = _settings.ShouldAutoStagePatches;
        _lblStatusText.Text = engines.Count > 0
            ? $"Discovered {engines.Count} side-by-side engine installation(s) under '{_enginesDir}'."
            : $"No installed platform engines found under '{_enginesDir}'.";
    }

    private void OnRegisterAssociationClicked()
    {
        string exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "reEngine.exe");
        FileAssociationRecord record = _assocStore.RegisterRecompAssociation(exePath);
        _log("INFO", $"UI registered .recomp association: {record.CommandTemplate}");
        RefreshAllState();
        _lblStatusText.Text = $"Registered .recomp association -> {record.ProgId}";
    }

    private async void OnCheckUpdatesClicked()
    {
        _btnCheckUpdates.Enabled = false;
        _lblUpdatePill.Text = "CHECKING";
        _lblUpdatePill.ForeColor = AccentCyan;
        try
        {
            _settings.ShouldCheckUpdates = _chkCheckUpdates.Checked;
            _settings.ShouldAutoStagePatches = _chkAutoStage.Checked;
            var resolver = new EngineVersionResolver();
            InstalledEngineCandidate? primary = resolver.DiscoverInstalledEngines(_enginesDir).FirstOrDefault();
            string engId = primary?.EngineId ?? "reStation";
            SemVer ver = primary?.Version ?? new SemVer(0, 1, 0);
            UpdateCheckOutcome outcome = await _updateCoordinator.CheckAndStageAsync(
                _settings,
                engId,
                ver,
                $"{ver.Major}.{ver.Minor}.x",
                _settings.LastUpdateFeedUrl);

            _lblUpdatePill.Text = outcome.BreakingUpdateAvailable
                ? "BREAKING AVAILABLE"
                : (outcome.CompatiblePatchStaged ? "PATCH STAGED" : "UP TO DATE");
            _lblUpdatePill.ForeColor = outcome.BreakingUpdateAvailable
                ? AccentWarning
                : (outcome.CompatiblePatchStaged ? AccentSuccess : AccentCyan);
            _lblStatusText.Text = outcome.StatusMessage;
            _log("INFO", $"UI update check: {outcome.StatusMessage}");
        }
        catch (Exception ex)
        {
            _lblUpdatePill.Text = "CHECK FAILED";
            _lblUpdatePill.ForeColor = AccentDanger;
            _lblStatusText.Text = $"Update check failed: {ex.Message}";
            _log("ERROR", _lblStatusText.Text);
        }
        finally
        {
            _btnCheckUpdates.Enabled = true;
        }
    }

    private async void OnOpenPackageClicked()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open .recomp Package",
            Filter = "reEngine Packages (*.recomp;*.recomp.json)|*.recomp;*.recomp.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var coordinator = new RecompLaunchCoordinator(_updateCoordinator, _log);
        int code = await coordinator.LaunchAsync(dlg.FileName, _enginesDir, _settings, _sandbox, null, CancellationToken.None);
        _lblStatusText.Text = code == 0 ? $"Launched '{Path.GetFileName(dlg.FileName)}' cleanly." : $"Launch failed with exit code {code}.";
    }

    private void OnSaveSettingsClicked()
    {
        _settings.ShouldCheckUpdates = _chkCheckUpdates.Checked;
        _settings.ShouldAutoStagePatches = _chkAutoStage.Checked;
        _settings.WindowWidth = ClientSize.Width;
        _settings.WindowHeight = ClientSize.Height;
        _updateCoordinator.SaveSettings(_settings);
        _log("INFO", "Saved reEngine settings.json.");
        _lblStatusText.Text = "Settings saved atomically.";
    }

    private void ConfigureVisibleTestShowcase()
    {
        _showcaseTimer = new System.Windows.Forms.Timer { Interval = 2200 };
        _showcaseTimer.Tick += (_, _) =>
        {
            _showcaseTimer?.Stop();
            CaptureShowcaseScreenshot();
            ExitCode = 0;
            Close();
        };
        Shown += (_, _) => _showcaseTimer.Start();
    }

    private void CaptureShowcaseScreenshot()
    {
        try
        {
            string shotsDir = Path.Combine(_sandbox.LogDirectory, "screenshots");
            Directory.CreateDirectory(shotsDir);
            string shotPath = Path.Combine(shotsDir, "reengine_visible_test.png");
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(shotPath, ImageFormat.Png);
            _log("INFO", $"Captured --visible-test screenshot to '{shotPath}'.");
        }
        catch (Exception ex)
        {
            _log("WARN", $"Failed to capture --visible-test screenshot: {ex.Message}");
        }
    }
}
