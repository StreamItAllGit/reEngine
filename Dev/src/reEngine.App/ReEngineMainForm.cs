// @module: reEngine.App.ReEngineMainForm
// @purpose: Dark Slate WinForms Suite Manager interaction logic, backend/track pinning, and right-drawer state sync.
// @depends-on: System, System.ComponentModel, System.Drawing, System.IO, System.Linq, System.Reflection, System.Runtime.InteropServices, System.Windows.Forms, Foundation.Core, reEngine.Core, reEngine.App.Ui
// @exports: ReEngineMainForm

using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Foundation.Core;
using reEngine.App.Ui;
using reEngine.Core.Associations;
using reEngine.Core.Bootstrap;
using reEngine.Core.Execution;
using reEngine.Core.Resolution;
using reEngine.Core.Updates;
using reEngine.Host.Settings;

namespace reEngine.App;

internal sealed partial class ReEngineMainForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly AppEnvironment _appEnv;
    private readonly string _enginesDir;
    private readonly UpdateCoordinator _updateCoordinator;
    private readonly IReEngineSettingsStore _settingsStore;
    private readonly IFileAssociationStore _assocStore;
    private readonly Action<string, string> _log;
    private readonly string? _containmentRoot;
    private ReEngineSettingsBundle _settingsBundle;
    private ReEngineSettings _settings;
    private DecompDispatchStatusSummary _lastDecompStatus = new(false, string.Empty, 0, "lightweight");
    private AotCacheStatusSummary _lastAotStatus = new(string.Empty, 0, 0);

    public int ExitCode { get; private set; }

    public ReEngineMainForm(
        AppEnvironment appEnv,
        string enginesDir,
        UpdateCoordinator updateCoordinator,
        IFileAssociationStore assocStore,
        Action<string, string> log,
        string? containmentRoot = null,
        string? initialDecompPath = null,
        string? initialDecompStage = null,
        IReEngineSettingsStore? settingsStore = null)
    {
        _appEnv = appEnv ?? throw new ArgumentNullException(nameof(appEnv));
        _enginesDir = enginesDir ?? throw new ArgumentNullException(nameof(enginesDir));
        _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
        _assocStore = assocStore ?? throw new ArgumentNullException(nameof(assocStore));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _containmentRoot = containmentRoot;
        _settingsStore = settingsStore ?? new HostSettingsStore(_appEnv.DataDirectory, _appEnv.ConfigFilePath);
        _settingsBundle = _settingsStore.LoadOrMigrate();
        _settings = _settingsBundle.Core;

        Name = "ReEngineMainForm";
        Text = $"reEngine — Suite Router & Engine Manager (v{ReEnginePackagingConstants.Version})";
        ClientSize = new Size(Math.Max(860, _settings.WindowWidth ?? 900), Math.Max(540, _settings.WindowHeight ?? 580));
        MinimumSize = new Size(860, 520);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BackColor = ReEngineTheme.Surface0;
        ForeColor = ReEngineTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        TryApplyEmbeddedIcon();
        InitializeControls();
        RefreshAllState();
        ApplyInitialDecompLaunch(initialDecompPath, initialDecompStage);
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
        if (disposing) _tooltip?.Dispose();
        base.Dispose(disposing);
    }

    private void TryApplyEmbeddedIcon()
    {
        try
        {
            using Stream? iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("reEngine.App.reEngine.ico");
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            _log("WARN", $"Embedded icon load skipped: {ex.Message}");
        }
    }

    private void RefreshAllState()
    {
        var resolver = new EngineVersionResolver();
        IReadOnlyList<InstalledEngineCandidate> engines = resolver.DiscoverInstalledEngines(_enginesDir);
        _enginesShelf.Populate(engines, _settings);
        _libraryShelf.ApplySettings(_settingsBundle);

        FileAssociationRecord? assoc = _assocStore.ReadRecompAssociation();
        _lblAssocPill.Text = _assocStore is VirtualRegistryAssociationStore ? (assoc is not null ? "VIRTUALIZED" : "UNREGISTERED") : (assoc is not null ? "REGISTERED" : "UNREGISTERED");
        _lblAssocPill.ForeColor = assoc is not null ? ReEngineTheme.AccentSuccess : ReEngineTheme.AccentWarning;

        DecompDispatchStatusSummary decompStatus = ReEngineAppStatusHelper.InspectDecompStatus(_enginesDir, _settings, engines.Count);
        AotCacheStatusSummary aotStatus = ReEngineAppStatusHelper.InspectAotCache(_appEnv.DataDirectory);
        _lastDecompStatus = decompStatus;
        _lastAotStatus = aotStatus;
        _lblDecompPill.Text = decompStatus.FormatPillText();
        _lblDecompPill.ForeColor = decompStatus.ToolchainAvailable ? ReEngineTheme.AccentSuccess : ReEngineTheme.AccentCyan;
        _bringUpShelf.UpdateSummary(decompStatus, aotStatus);
        _lblStatusText.Text = engines.Count > 0
            ? $"Discovered {engines.Count} side-by-side engine installation(s) under '{_enginesDir}'."
            : $"No installed platform engines found under '{_enginesDir}'.";
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
        LayoutControls();
    }

    private void ToggleRightDrawer() => SetDrawerVisible(!_rightDrawer.Visible);

    private void SetDrawerVisible(bool visible)
    {
        _rightDrawer.Visible = visible;
        LayoutControls();
    }

    private void OnLibrarySelectionChanged(string backendId, int scale, bool jitBringup)
    {
        _libraryShelf.WriteToBundle(_settingsBundle);
        _settingsStore.SaveAll(_settingsBundle);
        _lblStatusText.Text = $"Launch defaults saved: backend={backendId}, scale={scale}x, liveJitFallback={(jitBringup ? "on" : "off")}.";
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
    }

    private void OnFullscreenToggled()
    {
        _libraryShelf.WriteToBundle(_settingsBundle);
        _settingsStore.SaveAll(_settingsBundle);
        _lblStatusText.Text = $"Default fullscreen presentation {(_settingsBundle.Video.IsFullscreen ? "enabled" : "disabled")} ({_settingsBundle.Video.DisplayMode}).";
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
    }

    private void OnDisplayAudioSettingsChanged()
    {
        _libraryShelf.WriteToBundle(_settingsBundle);
        _settingsStore.SaveAll(_settingsBundle);
        _lblStatusText.Text = $"Display/audio defaults saved: display={_settingsBundle.Video.DisplayMode}, filter={_settingsBundle.Video.VideoFilterMode}, muted={_settingsBundle.Audio.IsAudioMuted}.";
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
    }

    private void OnResetSafeModeRequested()
    {
        _ = new BootCrashSentinel(_appEnv.DataDirectory).Clear();
        _settingsBundle.Core.IsSafeModeActive = false;
        _settingsStore.SaveCore(_settingsBundle.Core);
        _libraryShelf.ApplySettings(_settingsBundle);
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
        _lblStatusText.Text = "Safe Mode cleared; next launch will restore configured HD packs and overrides.";
    }

    private void OnEngineOptionsChanged(bool shouldCheckUpdates, bool shouldAutoStage, string profile)
    {
        _settings.ShouldCheckUpdates = shouldCheckUpdates;
        _settings.ShouldAutoStagePatches = shouldAutoStage;
        ReEngineAppStatusHelper.SetInstallProfile(_settings, profile);
        _settingsStore.SaveCore(_settings);
        _lblStatusText.Text = $"Engine preferences saved (profile={ReEngineAppStatusHelper.GetInstallProfile(_settings)}).";
    }

    private void OnEngineSelected(InstalledEngineCandidate? candidate) => UpdateDrawerSummary(candidate);

    private void UpdateDrawerSummary(InstalledEngineCandidate? selected)
    {
        string pinned = _settings.PinnedEngineVersions is { Count: > 0 }
            ? string.Join(", ", _settings.PinnedEngineVersions.Select(kv => $"{kv.Key}={kv.Value}"))
            : "None (auto latest matching track)";
        _rightDrawer.Populate(
            selected,
            _libraryShelf.JitBringupCheckBox.Checked,
            _libraryShelf.SelectedBackend,
            _libraryShelf.SelectedResolutionScale,
            _lastDecompStatus,
            _lastAotStatus,
            _appEnv.DataDirectory,
            pinned,
            _settingsBundle);
    }

    private void OnPinTrackClicked(InstalledEngineCandidate? selected)
    {
        if (selected is null) { _lblStatusText.Text = "Select an engine row in the Engines list to pin its version."; return; }
        _settings.PinnedEngineVersions ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _settings.PinnedEngineVersions[selected.EngineId] = selected.Version.ToString();
        _settingsStore.SaveCore(_settings);
        UpdateDrawerSummary(selected);
        _lblStatusText.Text = $"Pinned '{selected.EngineId}' to v{selected.Version} for track rollback.";
    }

    private void OnClearPinsClicked()
    {
        _settings.PinnedEngineVersions?.Clear();
        _settingsStore.SaveCore(_settings);
        UpdateDrawerSummary(_enginesShelf.SelectedCandidate);
        _lblStatusText.Text = "Cleared pinned engine track overrides.";
    }

    private void OnRegisterAssociationClicked()
    {
        string exePath = Program.ResolveLauncherExecutablePath();
        FileAssociationRecord recompRecord = _assocStore.RegisterRecompAssociation(exePath);
        _ = _assocStore.RegisterDecompAssociation(exePath);
        _log("INFO", $"UI registered .recomp & .decomp associations: {recompRecord.CommandTemplate}");
        RefreshAllState();
    }

    private async void OnCheckUpdatesClicked()
    {
        _enginesShelf.CheckUpdatesButton.Enabled = false;
        _lblUpdatePill.Text = "CHECKING";
        try
        {
            _settings.ShouldCheckUpdates = _enginesShelf.CheckUpdatesCheckBox.Checked;
            _settings.ShouldAutoStagePatches = _enginesShelf.AutoStageCheckBox.Checked;
            IReadOnlyList<InstalledEngineCandidate> engines = new EngineVersionResolver().DiscoverInstalledEngines(_enginesDir);
            MultiEngineUpdateSweepResult sweep = await _updateCoordinator.CheckAllInstalledEnginesAsync(_settings, engines, _settings.LastUpdateFeedUrl, allowedLocalFeedRoot: _containmentRoot);
            if (IsDisposed) return;
            _lblUpdatePill.Text = sweep.NetworkUnavailable ? "OFFLINE" : (sweep.Outcomes.Any(o => o.CompatiblePatchStaged) ? "PATCH STAGED" : "UP TO DATE");
            _lblUpdatePill.ForeColor = sweep.NetworkUnavailable ? ReEngineTheme.AccentWarning : ReEngineTheme.AccentCyan;
            _lblStatusText.Text = sweep.SummaryMessage;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblUpdatePill.Text = "FAILED";
            _lblStatusText.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed)
            {
                _enginesShelf.CheckUpdatesButton.Enabled = true;
                LayoutControls();
            }
        }
    }

    private void OnSaveSettingsClicked()
    {
        _settings.ShouldCheckUpdates = _enginesShelf.CheckUpdatesCheckBox.Checked;
        _settings.ShouldAutoStagePatches = _enginesShelf.AutoStageCheckBox.Checked;
        ReEngineAppStatusHelper.SetInstallProfile(_settings, _enginesShelf.InstallProfileCombo.SelectedItem as string);
        _libraryShelf.WriteToBundle(_settingsBundle);
        _settings.WindowWidth = ClientSize.Width;
        _settings.WindowHeight = ClientSize.Height;
        _settingsStore.SaveAll(_settingsBundle);
        RefreshAllState();
        _lblStatusText.Text = "Settings saved atomically.";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        bool windowResized = _settings.WindowWidth != ClientSize.Width || _settings.WindowHeight != ClientSize.Height;
        bool libraryDiff = !string.Equals(_settingsBundle.Video.BackendId, _libraryShelf.SelectedBackend, StringComparison.OrdinalIgnoreCase) ||
                           _settingsBundle.Video.ResolutionScale != _libraryShelf.SelectedResolutionScale ||
                           _settingsBundle.Video.IsFullscreen != _libraryShelf.FullscreenCheckBox.Checked ||
                           ReEngineAppStatusHelper.GetJitBringup(_settings) != _libraryShelf.JitBringupCheckBox.Checked;
        if (windowResized || libraryDiff)
        {
            _libraryShelf.WriteToBundle(_settingsBundle);
            _settings.WindowWidth = ClientSize.Width;
            _settings.WindowHeight = ClientSize.Height;
            _settingsStore.SaveAll(_settingsBundle);
        }
        base.OnFormClosing(e);
    }
}
