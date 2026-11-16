// @module: reEngine.App.ReEngineMainForm
// @purpose: Dark Slate WinForms Suite Manager interaction logic, settings persistence, and association triggers.
// @depends-on: System, System.ComponentModel, System.Drawing, System.IO, System.Linq, System.Reflection, System.Runtime.InteropServices, System.Windows.Forms, Foundation.Core, reEngine.Core
// @exports: ReEngineMainForm

using System.ComponentModel;
using System.Drawing;
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

    private readonly AppEnvironment _appEnv;
    private readonly string _enginesDir;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly IFileAssociationStore _assocStore;
    private readonly Action<string, string> _log;
    private readonly string? _containmentRoot;

    private ReEngineSettings _settings;

    public int ExitCode { get; private set; }

    public ReEngineMainForm(
        AppEnvironment appEnv,
        string enginesDir,
        UpdateCoordinator updateCoordinator,
        IFileAssociationStore assocStore,
        Action<string, string> log,
        string? containmentRoot = null)
    {
        _appEnv = appEnv ?? throw new ArgumentNullException(nameof(appEnv));
        _enginesDir = enginesDir ?? throw new ArgumentNullException(nameof(enginesDir));
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _assocStore = assocStore ?? throw new ArgumentNullException(nameof(assocStore));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _containmentRoot = containmentRoot;
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
        RefreshAllState();
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            _log("WARN", $"Embedded icon load skipped: {ex.Message}");
        }
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
        if (_assocStore is VirtualRegistryAssociationStore)
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
        string activeProfile = ReEngineAppStatusHelper.GetInstallProfile(_settings);
        _cmbInstallProfile.SelectedItem = string.Equals(activeProfile, "full", StringComparison.OrdinalIgnoreCase)
            ? "Full"
            : "Lightweight";

        DecompDispatchStatusSummary decompStatus = ReEngineAppStatusHelper.InspectDecompStatus(_enginesDir, _settings, engines.Count);
        AotCacheStatusSummary aotStatus = ReEngineAppStatusHelper.InspectAotCache(_appEnv.DataDirectory);
        _lblDecompPill.Text = decompStatus.FormatPillText();
        _lblDecompPill.ForeColor = decompStatus.ToolchainAvailable ? AccentSuccess : AccentCyan;
        _lblDecompAotStatus.Text = $"{decompStatus.FormatSummary()} | {aotStatus.FormatSummary()}";

        _lblStatusText.Text = engines.Count > 0
            ? $"Discovered {engines.Count} side-by-side engine installation(s) under '{_enginesDir}'."
            : $"No installed platform engines found under '{_enginesDir}'.";
        LayoutControls();
    }

    private void OnRegisterAssociationClicked()
    {
        string exePath = Program.ResolveLauncherExecutablePath();
        FileAssociationRecord recompRecord = _assocStore.RegisterRecompAssociation(exePath);
        FileAssociationRecord decompRecord = _assocStore.RegisterDecompAssociation(exePath);
        _log("INFO", $"UI registered .recomp & .decomp associations: {recompRecord.CommandTemplate} | {decompRecord.CommandTemplate}");
        RefreshAllState();
        _lblStatusText.Text = $"Registered .recomp & .decomp associations -> {recompRecord.ProgId} / {decompRecord.ProgId}";
    }

    private async void OnCheckUpdatesClicked()
    {
        _btnCheckUpdates.Enabled = false;
        _lblUpdatePill.Text = "CHECKING";
        _lblUpdatePill.ForeColor = AccentCyan;
        LayoutControls();
        try
        {
            _settings.ShouldCheckUpdates = _chkCheckUpdates.Checked;
            _settings.ShouldAutoStagePatches = _chkAutoStage.Checked;
            var resolver = new EngineVersionResolver();
            IReadOnlyList<InstalledEngineCandidate> engines = resolver.DiscoverInstalledEngines(_enginesDir);

            MultiEngineUpdateSweepResult sweep = await _updateCoordinator.CheckAllInstalledEnginesAsync(
                _settings,
                engines,
                _settings.LastUpdateFeedUrl,
                allowedLocalFeedRoot: _containmentRoot);

            ApplySweepStatus(sweep);
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
            LayoutControls();
        }
    }

    private void ApplySweepStatus(MultiEngineUpdateSweepResult sweep)
    {
        if (sweep.NetworkUnavailable)
        {
            _lblUpdatePill.Text = "OFFLINE / SKIPPED";
            _lblUpdatePill.ForeColor = AccentWarning;
            _lblStatusText.Text = sweep.SummaryMessage;
            _log("WARN", $"UI update check: {sweep.SummaryMessage}");
            return;
        }

        bool anyBreaking = sweep.Outcomes.Any(o => o.BreakingUpdateAvailable);
        bool anyStaged = sweep.Outcomes.Any(o => o.CompatiblePatchStaged);
        bool anyAvailable = sweep.Outcomes.Any(o => o.CompatiblePatchAvailable);

        if (anyBreaking)
        {
            _lblUpdatePill.Text = "BREAKING AVAILABLE";
            _lblUpdatePill.ForeColor = AccentWarning;
        }
        else if (anyStaged)
        {
            _lblUpdatePill.Text = "PATCH STAGED";
            _lblUpdatePill.ForeColor = AccentSuccess;
        }
        else if (anyAvailable)
        {
            _lblUpdatePill.Text = "PATCH AVAILABLE";
            _lblUpdatePill.ForeColor = AccentWarning;
        }
        else
        {
            _lblUpdatePill.Text = "UP TO DATE";
            _lblUpdatePill.ForeColor = AccentCyan;
        }

        _lblStatusText.Text = sweep.SummaryMessage;
        _log("INFO", $"UI update check: {sweep.SummaryMessage}");
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

        try
        {
            var coordinator = new RecompLaunchCoordinator(_updateCoordinator, _log);
            int code = await coordinator.LaunchAsync(
                dlg.FileName,
                _enginesDir,
                _settings,
                _appEnv,
                _containmentRoot,
                new RecompLaunchOptions(),
                CancellationToken.None);
            _lblStatusText.Text = code == 0 ? $"Launched '{Path.GetFileName(dlg.FileName)}' cleanly." : $"Launch failed with exit code {code}.";
        }
        catch (Exception ex)
        {
            _lblStatusText.Text = $"Launch blocked: {ex.Message}";
            _log("ERROR", $"UI package launch failed: {ex.Message}");
        }
    }

    private void OnSaveSettingsClicked()
    {
        _settings.ShouldCheckUpdates = _chkCheckUpdates.Checked;
        _settings.ShouldAutoStagePatches = _chkAutoStage.Checked;
        ReEngineAppStatusHelper.SetInstallProfile(_settings, _cmbInstallProfile.SelectedItem as string);
        _settings.WindowWidth = ClientSize.Width;
        _settings.WindowHeight = ClientSize.Height;
        _updateCoordinator.SaveSettings(_settings);
        RefreshAllState();
        _log("INFO", "Saved reEngine settings.json.");
        _lblStatusText.Text = "Settings saved atomically.";
    }
}
