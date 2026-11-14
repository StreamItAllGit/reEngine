# reEngine — System Architecture

**Synced Version:** `0.1.1` (`2026-11-14`)  
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
  - `Updates\UpdateModels.cs` & `Updates\UpdateCoordinator.cs`: `ReEngineSettings`, `UpdateFeedEntry`, `UpdateFeedManifest`, `UpdateCheckOutcome`, `MultiEngineUpdateSweepResult` (`SweepExecuted`, `NetworkUnavailable`, `Outcomes`, `SummaryMessage`), and `CheckAllInstalledEnginesAsync` (sweeps every distinct `(EngineId, Track)` pair and degrades transient HTTP/socket/IO/timeout/JSON feed failures gracefully without crash dumps while keeping containment checks fail-closed).
  - `Bootstrap\ReEngineBootstrapHelper.cs` & `Bootstrap\ReEngineTelemetryFormatter.cs`: `ReEnginePackagingConstants`, `ReEngineBootstrapResult`, `ReEngineBootstrapHelper` (zero-PII path resolution + silent installer bootstrap via `JobObjectGuard`), plus `ReEngineTelemetryFormatter` (Standard 15 `[v<Ver>] [PID:<id>/T:<tid>] [<LEVEL>]` log lines + single `[HEARTBEAT]` prefix normalization).
- **`reEngine.App` (`net8.0-windows`, WinForms/CLI):**
  - `Program.cs`: `AttachConsole(-1)`, `SandboxEnvironment`, `CrashBundleService` (populating `"recentLogs"` via `SnapshotRecentLogs()`), `CappedSharedLogSink`, fail-closed sandbox containment guard, CLI dispatch (`--strict`, `--wait`, Zip-Slip/`InvalidDataException`/`JsonException`/`ArgumentException` Exit `2`, `CancelKeyPress` `130`), and updated `PrintUsage()`.
  - `RecompLaunchOptions.cs` & `RecompLaunchCoordinator.cs`: validates `moduleId` (`^[a-z0-9][a-z0-9-]*$`), isolates child storage to `<dataDir>\modules\<moduleId>\` (`--data-dir`, `--config`, `--log-dir`), enforces containment on `--fixture`/`--dump-screenshot`/`--dump-audio`/`--input-script`, and forwards `--preload-media`, `--wait`, `--headless`, `--visible-test`, `--enable-logs`, `--frames`, and test artifact flags cleanly.
  - `ReEngineMainForm.cs` & `ReEngineMainForm.Layout.cs`: Dark Slate (`#0E0E10` / `#18181B` / `#38BDF8`) suite manager threading `sandboxBoundary`, using shared `CheckAllInstalledEnginesAsync` sweep (`OFFLINE / SKIPPED` warning pill), refreshing `LayoutControls()` on pill text mutations, and capturing `--visible-test` PNGs.
- **`reEngine.Launcher` (`net8.0-windows`, Standalone — zero `reEngine.Core` ref):** `Program.cs` + `LauncherArgsHelper.cs` (`<= 140 LOC`): resolves `app\reEngine.exe` via embedded `Foundation.Launcher.dll` (`RootLauncherHost.ResolveTargetExecutable`), normalizes caller-CWD relative paths (preserving HTTP/HTTPS `--feed` URIs), waits synchronously across CLI/router flags (`--open`, `--wait`, `--check-updates`, `--register-assoc`, `--visible-test`, `--test-mode`, `--headless`, `--dry-run`, `--help`, `--version`, positional paths while skipping `ValueOptions` values such as `--port 15100` / `--ui desktop`), handles `CancelKeyPress` (`130`), and propagates child exit codes verbatim.
- **`reEngine.Installer` (`net8.0-windows`):** Single-EXE setup (`StandardInstallerHost.Run`) embedding gzip-compressed `Foundation.Core.dll.gz`, `Foundation.Installer.dll.gz`, and `reEngine.Core.dll.gz` with `AllowAutoStart = false` and `#38BDF8` accent.
- **`reEngine.Tests` (`net8.0`, xUnit):** 33 passing contract, containment, multi-engine sweep, launcher sync-wait/normalization, launch-plan isolation, and telemetry/crash-bundle tests across `ManifestAndResolverTests.cs`, `AssociationUpdateAndContainmentTests.cs`, `LauncherAndLaunchPlanTests.cs`, and `UpdateSweepAndTelemetryTests.cs`.
