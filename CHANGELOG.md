# Changelog

All notable changes to **`reEngine`** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.2] - 2026-11-14

### Added
- Added `RecompPackageReader.ResolveModuleRootDirectory` (`reEngine.Core`) so clean-root single-file cartridges stored under `<InstallRoot>\app\<Module>.recomp` (or `app\module.recomp.json`) resolve `ModuleRootDirectory` to the parent `<InstallRoot>\` while preserving strict `manifest.PreloadRomDir` (`data\rom\`) discovery containment.
- Added xUnit regression test `ParseRecompArchiveInAppFolder_ResolvesParentModuleRootAndStrictDataRomPreload` in `ManifestAndResolverTests.cs` (`34/34` passing, `0` warnings).

### Changed
- Bumped `reEngine` to `v0.1.2` across `Dev\version.txt`, `Dev\Directory.Build.props`, `Dev\app.manifest.json`, `ReEnginePackagingConstants.Version`, Manual Shelf docs, and `SharedLibs\reEngine\v0.1.2\`.
- Updated `Dev\scripts\build_release.ps1` to prune older unpacked `Releases\v*` directories past max `2` (`Standard 03 §6.2`) while retaining `.7z` archives and `Installers\` history.

## [0.1.1] - 2026-11-14

### Added
- Implemented `LauncherArgsHelper` (`reEngine.Launcher`) for standalone synchronous-wait detection (`--open`, `--wait`, `--check-updates`, `--register-assoc`, `--visible-test`, `--test-mode`, `--headless`, `--dry-run`, `--help`, `--version`, and non-flag positional paths while preserving `--port`/`--ui`/`--frames` value pairs) and caller-CWD relative path normalization (preserving `http://`/`https://` `--feed` URIs).
- Added `MultiEngineUpdateSweepResult` (`SweepExecuted`, `NetworkUnavailable`, `Outcomes`, `SummaryMessage`) and `UpdateCoordinator.CheckAllInstalledEnginesAsync` to sweep every distinct `(EngineId, Track)` pair side-by-side (`reEngine.Core`).
- Added `RecompLaunchOptions` (`ShouldWaitForExit`, `ShouldRunHeadless`, `ShouldShowVisibleTest`, `ShouldEnableRuntimeLogs`, `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--input-script`) and `ChildLaunchPlan` per-module isolation (`<dataDir>\modules\<moduleId>\`) in `reEngine.App`.
- Added `ReEngineTelemetryFormatter` (`reEngine.Core`) enforcing Standard 15 `[v0.1.1] [PID:<id>/T:<tid>] [<LEVEL>]` lines and single `[HEARTBEAT]` prefixes, plus synthetic xUnit coverage in `LauncherAndLaunchPlanTests.cs` and `UpdateSweepAndTelemetryTests.cs` (`33/33` passing, `0` warnings).
- Logged Standard 15 read-only upstream proposal report `0.3.1.20261114-022857.-.rootlauncher-shouldwait-proposal.md` in `Foundation\Dev\docs\reports\`.

### Changed
- Propagated child CLI exit codes `(0, 1, 2, 3, 4, 5, 6, 130)` synchronously through root `reEngine.exe` (including `Console.CancelKeyPress` `130` cleanup) and mapped Zip-Slip/manifest `InvalidDataException`, `JsonException`, `ArgumentException`, and fail-closed containment violations to Exit `2`.
- Threaded `sandboxBoundary` into `ReEngineMainForm` (`OnCheckUpdatesClicked`, `OnOpenPackageClicked`) and invoked `LayoutControls()` on status pill text transitions (`OFFLINE / SKIPPED`, `BREAKING AVAILABLE`, `PATCH STAGED`) to prevent label clipping.
- Locked strategic scope & horizon docs (`README.md`, `FEATURES.md`, `ROADMAP.md`, `"supportedEngineTracks"` manifest handshake) for `v0.2.0` $\rightarrow$ `v1.0.0` (`win-x64` Gold, gated on `>= 2` `R1–R3` `.recomp` modules across `>= 2` `re*` engines and `uStation` `--open --wait` delegation) $\rightarrow$ `v2.0.0` (`SDL3`/XDG/macOS/mobile/WASM/console Zero-RWX 10-Platform North Star), promoted `CORE-08`, and narrowed `ROUTE-08`/`ROUTE-09` cards to remaining `v0.2.0`/`v0.3.0` work.
- Synchronized `AGENTS.md`, `Program.PrintUsage()`, Manual Shelf (`Dev\docs\ARCHITECTURE.md`, `Dev\docs\INTERFACES.md`, `Dev\docs\USER_GUIDE.md`, `Dev\docs\OPERATIONS.md`), signed `reEngine-Setup-v0.1.1-win-x64.exe`, `SharedLibs\reEngine\v0.1.1\`, and `@inspecter-reengine` live `Sandbox\` + `--visible-test` verification (`0.1.1.20261114-025555.-.sandbox-verify.md`).

### Fixed
- Eliminated unhandled `HttpRequestException`/`SocketException` crashes during offline/dropped-socket `--check-updates` (`[WARN]` return with `ExitCode = 0`, or `4` under `--strict`, writing `0` crash bundles).
- Populated `"recentLogs"` on `CrashBundleService.TryWriteCrashBundle` in `Program.Main` and removed duplicate `[HEARTBEAT] [HEARTBEAT]` log prefixes.
- Fixed `FirstOrDefault()` update check blindspot that skipped `0.1.x` tracks whenever `0.2.0+` existed side-by-side.

## [0.1.0] - 2026-11-13

### Added
- Initialized Tier A 6-folder repository scaffold (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`), outer Git repository, Root Documentation Quartet, `AGENTS.md`, Manual Shelf docs, `Foundation v0.3.1` SDK sync (`Dev\lib\`), and original [`Dev\assets\reEngine.{svg,png,ico}`](<REPO_ROOT>/Dev/assets/reEngine.svg) brand artwork.
- Implemented `reEngine.slnx` (`Dev\Directory.Build.props`, `Dev\version.txt`) with `TreatWarningsAsErrors` and Release Zero-PII `<PathMap>` + `<DebugType>none</DebugType>`.
- Implemented `reEngine.Core` (`net8.0`, `Foundation.Core`-only): `RecompPackageManifest` + `RecompPackageReader` (JSON/ZIP + Zip-Slip guard + `data\rom\` preload discovery), `EngineVersionResolver` + `EngineTrackMatcher` (side-by-side track resolution), `IFileAssociationStore` + `VirtualRegistryAssociationStore` + `WindowsRegistryAssociationStore`, `UpdateCoordinator` (`GranularSettingsManager` + `SafeNetworkTransfer`), and `ReEngineBootstrapHelper` (`AllowAutoStart = false`).
- Implemented `reEngine.App` (`net8.0-windows`): CLI dispatcher (`--open`, `--register-assoc`, `--check-updates`, `--engines-dir`, `--data-dir`, `--config`, `--log-dir`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`), `RecompLaunchCoordinator`, and Dark Slate `ReEngineMainForm` (`#0E0E10` / `#18181B` / `#38BDF8`).
- Authored `reEngine.Launcher` (`RootLauncherHost` + `AssemblyResolve`), `reEngine.Installer` (`StandardInstallerHost` + compressed `AssemblyResolve` + `AllowAutoStart = false`), `.bat` helper scripts, and `Dev\scripts\build_release.ps1`; passed `@dean` Gate 2 and packaged `Builds\v0.1.0\`, signed zero-PII `Installers\reEngine-Setup-v0.1.0-win-x64.exe` + `<PROJECTS_ROOT>\Installers\` (`0` audit findings), 6-file `Releases\Latest\` + `.7z`, `Archive\reEngine-v0.1.0-src.7z`, and immutable `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.0\` (`+ Latest\`).
- Added `reEngine.Tests` (`14/14` passing xUnit tests) and passed `@inspecter` live `Sandbox\` + `--visible-test` verification (`virtual_registry.json`, `reStation` `0.1.x` resolution, `data\rom\` preload auto-detect, SHA-256 patch staging, and screenshot capture).
