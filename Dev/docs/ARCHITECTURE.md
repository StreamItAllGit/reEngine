# reEngine — System Architecture

**Synced Version:** `0.1.0` (`2026-11-13`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)

---

## 1. Zero-Daemon Router Model
Unlike `Mother` (`:5100`), `reEngine` runs **zero** background daemons or Windows Startup entries (`ReEnginePackagingConstants.AllowAutoStart = false`, `0 MB` idle footprint). Any suite installer (`reStation`, `<Game>Recomp`, `reEngine`) deploys or idempotently upgrades the `reEngine` root (defaulting dynamically via `ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir` with `REENGINE_HOME` override and fail-closed `--test-mode` containment) while retaining older `engines\re<Platform>\v<Ver>\` side-by-side so legacy `.recomp` modules never break.

## 2. Module & Solution Split (`<= 400 LOC` Hard Gate)
Solution: [`Dev\reEngine.slnx`](<REPO_ROOT>/Dev/reEngine.slnx) (`Dev\Directory.Build.props`, `TreatWarningsAsErrors = true`, Release `<DebugType>none</DebugType>` + `<PathMap>`).

- **`reEngine.Core` (`net8.0`, Pure `Foundation.Core`-Only Library):**
  - `Manifests\RecompPackageManifest.cs`: `RecompPackageManifest`, `PreloadRomDiscoveryResult`, `RecompPackageLoadResult`, `RecompPackageReader` (reads JSON & Zip-Slip-guarded `.recomp` archives; discovers gitignored `data\rom\` optical/ROM media).
  - `Resolution\EngineVersionResolver.cs`: `SemVer`, `InstalledEngineCandidate`, `EngineResolutionResult`, `EngineTrackMatcher`, `EngineVersionResolver` (discovers `engines\<engineId>\v<Ver>\` and selects highest compatible track `>= engineVersion`, ignoring newer breaking tracks).
  - `Associations\FileAssociationStores.cs`: `FileAssociationRecord`, `IFileAssociationStore`, `VirtualRegistryAssociationStore` (`<dataDir>\virtual_registry.json`), `WindowsRegistryAssociationStore` (HKCU `Software\Classes`, throws if constructed in `--test-mode`).
  - `Updates\UpdateCoordinator.cs`: `ReEngineSettings`, `UpdateFeedEntry`, `UpdateFeedManifest`, `UpdateCheckOutcome`, `UpdateCoordinator` (`GranularSettingsManager` + 6-pillar `SafeNetworkTransfer`).
  - `Bootstrap\ReEngineBootstrapHelper.cs`: `ReEnginePackagingConstants`, `ReEngineBootstrapResult`, `ReEngineBootstrapHelper` (zero-PII path resolution + silent installer bootstrap via `JobObjectGuard`).
- **`reEngine.App` (`net8.0-windows`, WinForms/CLI):**
  - `Program.cs`: `AttachConsole(-1)`, `SandboxEnvironment`, `CrashBundleService`, `CappedSharedLogSink`, fail-closed sandbox containment guard, CLI dispatch.
  - `RecompLaunchCoordinator.cs`: `.recomp` package load, `data\rom\` preload detection, track-safe engine resolution, non-blocking update check, and `JobObjectGuard` launch (`--dry-run` safe).
  - `ReEngineMainForm.cs` & `ReEngineMainForm.Layout.cs`: Dark Slate (`#0E0E10` / `#18181B` / `#38BDF8`) suite manager with OwnerDraw dark ListView headers, 2-line tooltips, `OnResize` layout bounds, and `--visible-test` PNG showcase capture.
- **`reEngine.Launcher` (`net8.0-windows`):** Single-file root stub (`reEngine.exe`) embedding `Foundation.Launcher.dll` via `AssemblyResolve` + `[MethodImpl(MethodImplOptions.NoInlining)]` calling `RootLauncherHost.Run(Path.Combine("app", "reEngine.exe"), "reEngine", args)`.
- **`reEngine.Installer` (`net8.0-windows`):** Single-EXE setup (`StandardInstallerHost.Run`) embedding gzip-compressed `Foundation.Core.dll.gz`, `Foundation.Installer.dll.gz`, and `reEngine.Core.dll.gz` with `AllowAutoStart = false` and `#38BDF8` accent.
- **`reEngine.Tests` (`net8.0`, xUnit):** 14 contract and sandbox containment tests across `ManifestAndResolverTests.cs` and `AssociationUpdateAndContainmentTests.cs`.
