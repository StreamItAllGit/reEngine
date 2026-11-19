// @module: reEngine.App.Ui.BringUpShelfPanel
// @purpose: Decompilation toolchain readiness and local AOT cache status shelf.
// @depends-on: System, System.Drawing, System.Windows.Forms, reEngine.App
// @exports: BringUpShelfPanel

using System.Drawing;
using System.Windows.Forms;
using reEngine.Core.Manifests;

namespace reEngine.App.Ui;

internal sealed class BringUpShelfPanel : Panel
{
    private readonly Label _lblStage;
    private Action? _onOpenDecomp;
    private Action? _onLocateMedia;
    private Action? _onVerifyDecomp;
    private Action? _onRunStage;

    public Label HeaderLabel { get; }
    public Label ToolchainStatusLabel { get; }
    public Label AotCacheLabel { get; }
    public Label D4GuardLabel { get; }
    public Button OpenDecompButton { get; }
    public Label LoadedRecipeLabel { get; }
    public ComboBox StageCombo { get; }
    public Label MediaStatusLabel { get; }
    public Button LocateMediaButton { get; }
    public Button VerifyDecompButton { get; }
    public Button RunStageButton { get; }
    public TextBox LogBox { get; }

    public BringUpShelfPanel()
    {
        BackColor = ReEngineTheme.Surface1;
        Name = "Shelf_BringUp";

        HeaderLabel = new Label
        {
            Name = "BringUp_Header",
            Text = "Decompilation Bring-Up Runner & AOT Cache Readiness",
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = ReEngineTheme.TextPrimary,
            AutoSize = true,
            UseMnemonic = false
        };

        ToolchainStatusLabel = new Label { Name = "BringUp_ToolchainLabel", Text = "Decompilation Toolchain: Router mode (Lightweight profile)", ForeColor = ReEngineTheme.TextPrimary, AutoEllipsis = true, UseMnemonic = false };
        AotCacheLabel = new Label { Name = "BringUp_AotCacheLabel", Text = "AOT Cache: 0 precompiled module(s) cached.", ForeColor = ReEngineTheme.AccentCyan, AutoEllipsis = true, UseMnemonic = false };
        D4GuardLabel = new Label { Name = "BringUp_GuidanceLabel", Text = "Load a .decomp recipe to verify corpus/media and run D0..D5 stages interactively, or install the Full offline toolchain.", ForeColor = ReEngineTheme.TextMuted, UseMnemonic = false };

        OpenDecompButton = ReEngineTheme.CreateActionButton("Btn_OpenDecomp", "Open .decomp Recipe...", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        OpenDecompButton.Click += (_, _) => _onOpenDecomp?.Invoke();
        LoadedRecipeLabel = new Label { Name = "BringUp_RecipeLabel", Text = "Recipe: None loaded", ForeColor = ReEngineTheme.TextPrimary, AutoEllipsis = true, UseMnemonic = false };

        _lblStage = new Label { Text = "Stage:", ForeColor = ReEngineTheme.TextMuted, AutoSize = true, UseMnemonic = false };
        StageCombo = new ComboBox { Name = "Cmb_DecompStage", DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ReEngineTheme.Surface2, ForeColor = ReEngineTheme.TextPrimary, FlatStyle = FlatStyle.Flat };
        StageCombo.Items.AddRange(new object[] { "D0", "D1", "D2", "D3", "D4", "D5" });
        StageCombo.SelectedItem = "D3";

        MediaStatusLabel = new Label { Name = "BringUp_MediaLabel", Text = "Media: Not checked", ForeColor = ReEngineTheme.TextMuted, AutoEllipsis = true, UseMnemonic = false };
        LocateMediaButton = ReEngineTheme.CreateActionButton("Btn_LocateMedia", "Locate Media...", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        LocateMediaButton.Click += (_, _) => _onLocateMedia?.Invoke();
        LocateMediaButton.Enabled = false;

        VerifyDecompButton = ReEngineTheme.CreateActionButton("Btn_VerifyDecomp", "Verify Package...", ReEngineTheme.Surface3, ReEngineTheme.TextPrimary);
        VerifyDecompButton.Click += (_, _) => _onVerifyDecomp?.Invoke();
        VerifyDecompButton.Enabled = false;

        RunStageButton = ReEngineTheme.CreateActionButton("Btn_RunStage", "Run Stage", ReEngineTheme.AccentCyan, ReEngineTheme.Surface0);
        RunStageButton.Click += (_, _) => _onRunStage?.Invoke();
        RunStageButton.Enabled = false;

        LogBox = new TextBox
        {
            Name = "BringUp_LogBox",
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = ReEngineTheme.Surface0,
            ForeColor = ReEngineTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.75f)
        };

        Controls.AddRange(new Control[]
        {
            HeaderLabel, ToolchainStatusLabel, AotCacheLabel, D4GuardLabel,
            OpenDecompButton, LoadedRecipeLabel, _lblStage, StageCombo, MediaStatusLabel,
            LocateMediaButton, VerifyDecompButton, RunStageButton, LogBox
        });
        Resize += (_, _) => LayoutChildren();
        LayoutChildren();
    }

    public string SelectedStage => StageCombo.SelectedItem as string ?? "D3";

    public void BindActions(Action onOpenDecomp, Action onLocateMedia, Action onVerifyDecomp, Action onRunStage)
    {
        _onOpenDecomp = onOpenDecomp;
        _onLocateMedia = onLocateMedia;
        _onVerifyDecomp = onVerifyDecomp;
        _onRunStage = onRunStage;
    }

    public void SetLoadedDecomp(string packagePath, DecompPackageManifest manifest, string? mediaPath, string mediaSummary, string? requestedStage = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        LoadedRecipeLabel.Text = $"Recipe: {manifest.Title} ({manifest.ModuleId} -> {manifest.EngineId}) [{Path.GetFileName(packagePath)}]";
        string stageToSelect = !string.IsNullOrWhiteSpace(requestedStage) ? requestedStage.Trim().ToUpperInvariant() : (manifest.TargetDecompTier ?? "D3").Trim().ToUpperInvariant();
        int idx = StageCombo.FindStringExact(stageToSelect);
        StageCombo.SelectedIndex = idx >= 0 ? idx : 3;
        UpdateMediaStatus(mediaPath, mediaSummary);
        LocateMediaButton.Enabled = true;
        VerifyDecompButton.Enabled = true;
        RunStageButton.Enabled = true;
    }

    public void UpdateMediaStatus(string? mediaPath, string summary)
    {
        MediaStatusLabel.Text = string.IsNullOrWhiteSpace(mediaPath) ? $"Media: {summary}" : $"Media: {mediaPath}";
        MediaStatusLabel.ForeColor = string.IsNullOrWhiteSpace(mediaPath) ? ReEngineTheme.AccentWarning : ReEngineTheme.AccentSuccess;
    }

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogBox.AppendText(line + Environment.NewLine);
    }

    public void UpdateSummary(DecompDispatchStatusSummary decomp, AotCacheStatusSummary aot)
    {
        ArgumentNullException.ThrowIfNull(decomp);
        ArgumentNullException.ThrowIfNull(aot);

        ToolchainStatusLabel.Text = decomp.ToolchainAvailable
            ? $"Decompilation Toolchain: Offline CLI Toolchain Ready (Full profile, {decomp.InstalledEngineCount} engine(s)) — {decomp.ToolchainDirectory}"
            : $"Decompilation Toolchain: Router mode ({decomp.InstallProfile} profile, {decomp.InstalledEngineCount} engine(s))";
        ToolchainStatusLabel.ForeColor = decomp.ToolchainAvailable ? ReEngineTheme.AccentSuccess : ReEngineTheme.TextPrimary;

        double mb = aot.TotalBytes / (1024.0 * 1024.0);
        AotCacheLabel.Text = $"Precompiled AOT Cache: {aot.AssemblyCount} module(s) cached ({mb:0.00} MB) in {aot.CacheDirectory}";
    }

    private void LayoutChildren()
    {
        int w = Math.Max(340, Width);
        int h = Math.Max(300, Height);
        HeaderLabel.Location = new Point(16, 14);
        ToolchainStatusLabel.Bounds = new Rectangle(16, 44, w - 32, 20);
        AotCacheLabel.Bounds = new Rectangle(16, 68, w - 32, 20);
        D4GuardLabel.Bounds = new Rectangle(16, 92, w - 32, 34);
        OpenDecompButton.Bounds = new Rectangle(16, 132, 188, 28);
        LoadedRecipeLabel.Bounds = new Rectangle(214, 136, Math.Max(100, w - 230), 22);
        _lblStage.Location = new Point(16, 171);
        StageCombo.Bounds = new Rectangle(68, 167, 68, 26);
        MediaStatusLabel.Bounds = new Rectangle(148, 171, Math.Max(120, w - 164), 22);
        LocateMediaButton.Bounds = new Rectangle(16, 202, 132, 28);
        VerifyDecompButton.Bounds = new Rectangle(156, 202, 142, 28);
        RunStageButton.Bounds = new Rectangle(306, 202, 120, 28);
        LogBox.Bounds = new Rectangle(16, 238, w - 32, Math.Max(52, h - 250));
    }
}
