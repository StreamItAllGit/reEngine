# Changelog

All notable changes to **`reEngine`** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.1] - 2026-11-15

### Added
- Added Setup Wizard directory normalization so selecting a generic parent folder automatically sorts `reEngine` into `<parent>\reEngine\` without double-nesting and keeps companion `engines\` inside `<reEngineDir>\engines\` unless an umbrella `reEngine\engines\` folder already exists.
- Added 3 regression tests verifying wizard folder sorting, companion `engines\` root selection, and live suite picker synchronization (`53/53` passing).

### Changed
- Extracted `ReEnginePackagingConstants` and `ReEngineBootstrapResult` into their own `reEngine.Core` source file and bumped `reEngine` and its SDK release metadata to `v0.2.1`.
- Synchronized the Setup Wizard suite directory field and post-install `engines\` creation for custom non-umbrella folders while preserving explicit CLI `--install-dir` and `--suite-dir` overrides.

## [0.2.0] - 2026-11-15

### Added
- Added platform-agnostic `.decomp` package and `D0`–`D5` pipeline contracts in `reEngine.Core`: `DecompPackageManifest`, `DecompToolchainRequirements`, `IDecompPlatformProvider`, `DecompStage` (`D0`–`D5`), `DecompPipelineCoordinator`, and deterministic 3-part `DecompUnbuiltEngineError` diagnostics (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) for unbuilt engines (`re64`, `reCube`, `reGenesis`).
- Added `OfflineToolchainLocator` (`ReEngineInstallProfile.Lightweight` / `Full`, `MaxToolchainBudgetMb = 160`, `THIRDPARTY_LICENSES.txt`) and `Dev\scripts\stage_offline_toolchain.ps1` + `Dev\scripts\THIRDPARTY_LICENSES.txt` to stage and validate stripped Apache-2.0 headless Ghidra (`GPL/`, `docs/`, and Swing modules removed) + `jlink` OpenJDK with zero runtime internet access.
- Added shared `<dataDir>\cache\` AOT cache contracts and manager (`IAotCacheManager`, `AotCacheManager`, `AotCacheKey`, `AotCacheEntry`, `AotCompileResult`, `CleanRoomRecompCheckResult`) backed by `Foundation.Retro.AotCacheLayoutHelper`, cross-process index locking (`aot-index.json`), SHA-256 tamper invalidation, stale eviction, and strict clean-room `.recomp` rejection of `*.Generated.dll` and `*.g.cs` artifacts.
- Added `.decomp` file association support (`RegisterDecompAssociation`, `ReadDecompAssociation`, `UnregisterDecompAssociation` under `HKCU\Software\Classes\.decomp` / `reEngine.DecompModule` and `<dataDir>\virtual_registry.json`), CLI subcommand routing (`reEngine.exe decomp --module <file.decomp> [--stage D0..D5] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>]`, returning Exit `3` on unbuilt engines), and `ReEngineAppStatusHelper` + Dark Slate UI `Profile:` (`Lightweight`/`Full`) and `.decomp`/AOT status indicators.
- Added `LegacyMigrationHelper` (`--profile Lightweight|Full`, `--suite-dir`, `--reengine-dir`, opt-in `--migrate-legacy`, and human-gated `--confirm-legacy-cleanup` for stranded `v0.1.1` artifacts) preserving user `data\rom\` files safely.
- Added `Dev\settings.example.json` personal config overlay template and Standard 18 keys (`installProfile`, `reEngineInstallDir`, `suiteInstallDir`, `umbrellaSubpath`, `decompSchemaVersion`, `targetDecompTier`, `goalDecompTier`), plus synthetic fixture `Dev\tests\reEngine.Tests\Fixtures\sample-re64.decomp`.
- Added 16 new xUnit tests (`DecompAndToolchainTests.cs`, `AotCacheManagerTests.cs`, `InstallerAndAppStatusTests.cs`, plus `.decomp` association and CLI tests) bringing `reEngine.Tests` to `50/50` passing with `0` warnings and `0` skipped.

### Changed
- Synced `Foundation v0.4.0` SDK drop (`Dev\lib\`) and bumped `reEngine` to `v0.2.0` across `Dev\version.txt`, `Dev\Directory.Build.props`, `Dev\app.manifest.json`, `active.state`, `ReEnginePackagingConstants.Version`, Manual Shelf docs, and `reEngine.Tests` (`net8.0-windows`).
- Switched default non-admin installation root in `ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir` to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine` (suite root `%LOCALAPPDATA%\Programs\StreamItAll\reEngine` via `ResolveDefaultSuiteInstallDir`) while enforcing `AllowAutoStart = false`.
- Updated `Dev\scripts\build_release.ps1` release highlights for `.decomp` routing, `<dataDir>\cache\` AOT management, and `--profile Lightweight|Full` user-scope installs.

### Fixed
- Fixed `LauncherArgsHelper.NormalizePathArguments` so the `decomp` subcommand token is preserved verbatim instead of being normalized into a caller-CWD filesystem path when forwarding `reEngine.exe decomp --module ...` to `app\reEngine.exe`.

## [0.1.3] - 2026-11-14

### Added
- Added explicit `## License & Legal` sections to `README.md` and the User Guide documenting the `MPL-2.0` router license (`LICENSE`), `7z.exe`/`7z.dll` (`LICENSE-7ZIP.txt`) LGPL-2.1+ with unRAR restriction attribution, trademark non-affiliation, and the strict zero-ROM / zero-BIOS BYO-media guarantee.
- Added a fail-fast post-sanitization leak guard to the release packager validating all packaged Markdown files (`Releases\v<Version>\*.md` and `Releases\Latest\*.md`) against workstation paths, local URI leaks, internal workflow identifiers, and UTF-8 mojibake.

### Changed
- Bumped `reEngine` to `v0.1.3` across assemblies, manifests, `active.state`, `ReEnginePackagingConstants.Version`, Manual Shelf guides, and redistributable SDK metadata.
- Normalized `app.manifest.json` `downstreamProjects` to clean workspace-relative slugs (`Personal/reStation`, `Personal/KingsFieldRecomp`, `Personal/HMBackToNature-Recomp`) and scrubbed workstation user-profile paths from `AGENTS.md` (`<= 25` lines, `< 1,600 B`).
- Expanded all four Manual Shelf documents (`ARCHITECTURE.md`, `INTERFACES.md`, `USER_GUIDE.md`, and `OPERATIONS.md`) to comprehensive `>= 85`-line references.
- Hardened the release packager (`Dev\scripts\build_release.ps1`) to ship `LICENSE` inside portable `Builds\v0.1.3\app\LICENSE`, `Archive\reEngine-v0.1.3-src.7z`, and `SharedLibs\reEngine\v0.1.3\` (`+ Latest\`, tracked in `sdk-manifest.json`), prune stale `Builds\v0.1.0` and `Builds\v0.1.1` staging directories, read UTF-8 Markdown explicitly, and run `Sanitize-MarkdownForEndUser` across `Installers\RELEASE_NOTES.md` plus all four `Releases\{v0.1.3,Latest}\*.md` files (`README.md`, `FEATURES.md`, `RELEASE_NOTES.md`, `CHANGELOG.md`).

### Fixed
- Eliminated `Â§` / UTF-8 mojibake and internal link/handle/path leaks in `Releases\Latest\` Markdown deliverables caused by Windows-1252 default file reads and unsanitized release notes / changelog staging.

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
- Initialized Tier A 6-folder repository scaffold (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`), outer Git repository, Root Documentation Quartet, `AGENTS.md`, Manual Shelf docs, `Foundation v0.3.1` SDK sync (`Dev\lib\`), and original [`Dev\assets\reEngine.{svg,png,ico}`](Dev/assets/reEngine.svg) brand artwork.
- Implemented `reEngine.slnx` (`Dev\Directory.Build.props`, `Dev\version.txt`) with `TreatWarningsAsErrors` and Release Zero-PII `<PathMap>` + `<DebugType>none</DebugType>`.
- Implemented `reEngine.Core` (`net8.0`, `Foundation.Core`-only): `RecompPackageManifest` + `RecompPackageReader` (JSON/ZIP + Zip-Slip guard + `data\rom\` preload discovery), `EngineVersionResolver` + `EngineTrackMatcher` (side-by-side track resolution), `IFileAssociationStore` + `VirtualRegistryAssociationStore` + `WindowsRegistryAssociationStore`, `UpdateCoordinator` (`GranularSettingsManager` + `SafeNetworkTransfer`), and `ReEngineBootstrapHelper` (`AllowAutoStart = false`).
- Implemented `reEngine.App` (`net8.0-windows`): CLI dispatcher (`--open`, `--register-assoc`, `--check-updates`, `--engines-dir`, `--data-dir`, `--config`, `--log-dir`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`), `RecompLaunchCoordinator`, and Dark Slate `ReEngineMainForm` (`#0E0E10` / `#18181B` / `#38BDF8`).
- Authored `reEngine.Launcher` (`RootLauncherHost` + `AssemblyResolve`), `reEngine.Installer` (`StandardInstallerHost` + compressed `AssemblyResolve` + `AllowAutoStart = false`), `.bat` helper scripts, and `Dev\scripts\build_release.ps1`; passed `@dean` Gate 2 and packaged `Builds\v0.1.0\`, signed zero-PII `Installers\reEngine-Setup-v0.1.0-win-x64.exe` + `<PROJECTS_ROOT>\Installers\` (`0` audit findings), 6-file `Releases\Latest\` + `.7z`, `Archive\reEngine-v0.1.0-src.7z`, and immutable `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.0\` (`+ Latest\`).
- Added `reEngine.Tests` (`14/14` passing xUnit tests) and passed `@inspecter` live `Sandbox\` + `--visible-test` verification (`virtual_registry.json`, `reStation` `0.1.x` resolution, `data\rom\` preload auto-detect, SHA-256 patch staging, and screenshot capture).
