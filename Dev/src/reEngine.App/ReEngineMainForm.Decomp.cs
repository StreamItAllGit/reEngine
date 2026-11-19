// @module: reEngine.App.ReEngineMainForm.Decomp
// @purpose: Interactive .decomp recipe loader, suite-tree media locator, missing-media prompt, package verifier, and Bring-Up runner handlers.
// @depends-on: System, System.Collections.Generic, System.IO, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Core, reEngine.App.Ui, reEngine.Core.Decomp, reEngine.Core.Manifests
// @exports: ReEngineMainForm (partial: ApplyInitialDecompLaunch, OnOpenDecompClicked, OnLocateMediaClicked, OnRunDecompStageClicked, OnVerifyPackageClicked)

using System.Windows.Forms;
using Foundation.Core;
using reEngine.App.Ui;
using reEngine.Core.Decomp;
using reEngine.Core.Manifests;

namespace reEngine.App;

internal sealed partial class ReEngineMainForm
{
    private string? _loadedDecompPath;
    private DecompPackageManifest? _loadedDecompManifest;
    private string? _resolvedDecompMediaPath;
    private SuiteMediaDiscoveryResult? _lastMediaDiscovery;

    private void ApplyInitialDecompLaunch(string? initialDecompPath, string? initialDecompStage)
    {
        if (string.IsNullOrWhiteSpace(initialDecompPath)) return;
        ActivateShelf(MainShelfKind.BringUp);
        LoadDecompRecipe(initialDecompPath, initialDecompStage, autoRunIfReady: false);
    }

    private async void OnOpenPackageClicked()
    {
        using var dlg = new OpenFileDialog { Title = "Open .recomp Package", Filter = "reEngine Packages (*.recomp;*.recomp.json)|*.recomp;*.recomp.json|All Files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _libraryShelf.OpenPackageButton.Enabled = false;
        _lblStatusText.Text = $"Launching '{Path.GetFileName(dlg.FileName)}'...";
        try
        {
            var opts = new RecompLaunchOptions
            {
                BackendId = _libraryShelf.SelectedBackend,
                ResolutionScale = _libraryShelf.SelectedResolutionScale,
                Fullscreen = _libraryShelf.FullscreenCheckBox.Checked,
                JitBringup = _libraryShelf.JitBringupCheckBox.Checked
            };
            int code = await new RecompLaunchCoordinator(_updateCoordinator, _log).LaunchAsync(dlg.FileName, _enginesDir, _settings, _appEnv, _containmentRoot, opts, CancellationToken.None);
            if (IsDisposed) return;
            _lblStatusText.Text = code == 0 ? $"Launched '{Path.GetFileName(dlg.FileName)}' cleanly." : $"Launch failed with exit code {code}.";
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatusText.Text = $"Launch blocked: {ex.Message}";
        }
        finally
        {
            if (!IsDisposed) _libraryShelf.OpenPackageButton.Enabled = true;
        }
    }

    private void OnOpenDecompClicked()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open .decomp Recipe",
            Filter = "reEngine Decomp Modules (*.decomp;*.decomp.json)|*.decomp;*.decomp.json|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadDecompRecipe(dlg.FileName, requestedStage: null, autoRunIfReady: false);
    }

    private void LoadDecompRecipe(string packagePath, string? requestedStage, bool autoRunIfReady)
    {
        try
        {
            string fullPath = Path.GetFullPath(packagePath);
            if (!string.IsNullOrWhiteSpace(_containmentRoot)) AppEnvironment.AssertPathContained(fullPath, _containmentRoot);

            DecompPackageManifest manifest = DecompPipelineCoordinator.ParseManifest(fullPath);
            string moduleRoot = Directory.Exists(fullPath) ? fullPath : RecompPackageReader.ResolveModuleRootDirectory(fullPath);
            string? suiteRoot = SuiteMediaTreeWalker.DeriveSuiteRootDirectory(_enginesDir, _appEnv.DataDirectory, _containmentRoot);
            SuiteMediaDiscoveryResult walk = SuiteMediaTreeWalker.Discover(
                moduleRoot, suiteRoot, manifest.ModuleId, manifest.ExpectedSha256, manifest.PreloadRomDir, _containmentRoot);

            _loadedDecompPath = fullPath;
            _loadedDecompManifest = manifest;
            _lastMediaDiscovery = walk;
            _resolvedDecompMediaPath = walk.Found ? walk.PrimaryMediaPath : null;

            string mediaSummary = walk.Found
                ? $"Matched {Path.GetFileName(walk.PrimaryMediaPath)}"
                : (walk.IsAmbiguous ? $"Ambiguous ({walk.Candidates.Count} candidates) — click Locate Media..." : "Missing — click Locate Media...");

            ActivateShelf(MainShelfKind.BringUp);
            _bringUpShelf.SetLoadedDecomp(fullPath, manifest, _resolvedDecompMediaPath, mediaSummary, requestedStage);
            _bringUpShelf.AppendLog($"[LOAD] {manifest.Title} ({manifest.ModuleId} -> {manifest.EngineId}, target={manifest.TargetDecompTier})");
            _bringUpShelf.AppendLog($"[MEDIA] {walk.DiagnosticMessage}");
            _lblStatusText.Text = walk.Found
                ? $"Loaded '{Path.GetFileName(fullPath)}' with media ready."
                : $"Loaded '{Path.GetFileName(fullPath)}': media needs selection via Locate Media...";
            if (autoRunIfReady && walk.Found) BeginInvoke(() => OnRunDecompStageClicked());
        }
        catch (Exception ex)
        {
            _bringUpShelf.AppendLog($"[ERROR] Failed to load '{packagePath}': {ex.Message}");
            _lblStatusText.Text = $"Decomp load failed: {ex.Message}";
        }
    }

    private async void OnLocateMediaClicked()
    {
        if (_loadedDecompManifest is null || string.IsNullOrWhiteSpace(_loadedDecompPath)) return;

        try
        {
            string moduleRoot = Path.GetFullPath(Directory.Exists(_loadedDecompPath)
                ? _loadedDecompPath
                : RecompPackageReader.ResolveModuleRootDirectory(_loadedDecompPath));
            string relRomDir = string.IsNullOrWhiteSpace(_loadedDecompManifest.PreloadRomDir)
                ? Path.Combine("data", "rom")
                : _loadedDecompManifest.PreloadRomDir!;
            string romDir = AppEnvironment.AssertPathContained(Path.GetFullPath(Path.Combine(moduleRoot, relRomDir)), moduleRoot);

            using var prompt = new MissingMediaPromptDialog(_loadedDecompManifest.ModuleId, romDir, _lastMediaDiscovery);
            if (prompt.ShowDialog(this) != DialogResult.OK) return;

            string chosen = Path.GetFullPath(prompt.SelectedMediaPath);
            if (!string.IsNullOrWhiteSpace(_containmentRoot)) AppEnvironment.AssertPathContained(chosen, _containmentRoot);

            if (prompt.ShouldCopyIntoModuleRom)
            {
                StagedMediaResult staged = await SuiteMediaTreeWalker.StageMediaIntoRomDirectoryAsync(chosen, romDir, _containmentRoot, CancellationToken.None);
                if (IsDisposed) return;
                _resolvedDecompMediaPath = staged.PrimaryTargetPath;
                _bringUpShelf.AppendLog($"[STAGE] Copied {staged.StagedFiles.Count} file(s) into '{romDir}' -> '{staged.PrimaryTargetPath}'.");
            }
            else
            {
                _resolvedDecompMediaPath = chosen;
                _bringUpShelf.AppendLog($"[MEDIA] Using personal media in-place: '{chosen}'.");
            }

            _bringUpShelf.UpdateMediaStatus(_resolvedDecompMediaPath, "Selected");
            _lblStatusText.Text = $"Selected BYO media: {Path.GetFileName(_resolvedDecompMediaPath)}";
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _bringUpShelf.AppendLog($"[ERROR] Media selection failed: {ex.Message}");
            _lblStatusText.Text = $"Media selection blocked: {ex.Message}";
        }
    }

    private async void OnRunDecompStageClicked()
    {
        if (_loadedDecompManifest is null || string.IsNullOrWhiteSpace(_loadedDecompPath)) return;
        if (!Enum.TryParse(_bringUpShelf.SelectedStage, ignoreCase: true, out DecompStage stage)) stage = DecompStage.D3;

        _bringUpShelf.RunStageButton.Enabled = false;
        _lblStatusText.Text = $"Running {_loadedDecompManifest.ModuleId} {stage}...";
        try
        {
            string outDir = Path.Combine(_appEnv.DataDirectory, "decomp-output", _loadedDecompManifest.ModuleId);
            if (!string.IsNullOrWhiteSpace(_containmentRoot)) AppEnvironment.AssertPathContained(outDir, _containmentRoot);

            var platformArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_resolvedDecompMediaPath)) platformArgs["discPath"] = _resolvedDecompMediaPath;
            if (!string.IsNullOrWhiteSpace(_containmentRoot)) platformArgs["containmentRoot"] = _containmentRoot;

            var opts = new DecompRunOptions(_loadedDecompPath, outDir, _appEnv.DataDirectory, stage, DryRun: _appEnv.DryRun, Headless: false, PlatformArguments: platformArgs);
            DecompDispatchResult res = await new DecompPipelineCoordinator().DispatchAsync(opts, _enginesDir, CancellationToken.None);
            if (IsDisposed) return;

            string msg = res.UnbuiltEngineError is not null ? res.UnbuiltEngineError.FormatMessage() : res.DiagnosticMessage;
            _bringUpShelf.AppendLog($"[{(res.Success ? "PASS" : "FAIL")}] {msg}");
            _lblStatusText.Text = msg;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _bringUpShelf.AppendLog($"[ERROR] {ex.Message}");
            _lblStatusText.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed) _bringUpShelf.RunStageButton.Enabled = true;
        }
    }

    private void OnVerifyPackageClicked(string? explicitPackagePath)
    {
        string? targetPath = explicitPackagePath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Verify .recomp or .decomp Package",
                Filter = "reEngine Packages (*.recomp;*.decomp;*.json)|*.recomp;*.recomp.json;*.decomp;*.decomp.json;*.json|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            targetPath = dlg.FileName;
        }

        string? suiteRoot = SuiteMediaTreeWalker.DeriveSuiteRootDirectory(_enginesDir, _appEnv.DataDirectory, _containmentRoot);
        PackageVerificationReport report = new PackageVerificationService().Verify(
            targetPath, _enginesDir, _appEnv.DataDirectory, suiteRoot, requireMedia: false, settings: _settings, containmentRoot: _containmentRoot);

        string summary = $"Verify {report.PackageKind} '{report.ModuleId ?? Path.GetFileName(targetPath)}': passed={report.IsPassed}, exitCode={report.ExitCode}, mediaReady={report.HasMediaReady}";
        _bringUpShelf.AppendLog($"[VERIFY] {summary}");
        foreach (string w in report.Warnings) _bringUpShelf.AppendLog($"[WARN] {w}");
        foreach (string v in report.Violations) _bringUpShelf.AppendLog($"[VIOLATION] {v}");
        _lblStatusText.Text = summary;
        _log(report.IsPassed ? "INFO" : "WARN", summary);
    }
}
