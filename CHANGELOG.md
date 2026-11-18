# Changelog

All notable changes to **`reEngine`** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-11-18

### Added
- **Shared `reEngine.Host.dll` Runtime (`net8.0;net8.0-windows`, SDL3):** Added a reusable, console-agnostic host library providing SDL3 window creation, software/hardware framebuffer presentation (`HostRgbaSurfaceBackend`), audio ring-buffer streaming (`Sdl3AudioPump`), multi-schema input mapping (`HostInputFrame`, `HostInputMapper`), `F1` 5-tab settings drawer, `F3` telemetry HUD, and first-run user media prompt.
- **Resumable Engine Updates & Bidirectional Aliases:** Added `UpdatePackageApplier` with SSRF filtering, HTTP `Range` resumable `.part` downloads, SHA-256 verification, and atomic `.staging-<ver>` to `v<Ver>` promotion with `.ready` markers, alongside symmetric engine aliases (`reSlate` / `reStation`, `reCartOne` / `reNes`, `reCart64` / `re64`, `reBlast16` / `reGenesis`) and track pinning for rollback.
- **Clean-Room Decompilation Strictness & Unified Settings Migration:** Added `DecompD4StrictnessValidator` to verify zero compiler errors/warnings and zero decompiler tells without bundled retail source bodies, plus `SettingsMigrationHelper` and `ProgramBootstrapHelper` to consolidate legacy `settings.json` files into `<InstallRoot>\data\config\settings.json`.
- **Upgraded Manager Shelves & Inspector Drawer:** Added `Library`, `Engines`, and `Bring-Up` shelves, a collapsible `Inspector Drawer`, `--scale` CLI/UI passthrough (`1x`, `2x`, `4x`), persisted `shouldAllowJitBringup` / scale / backend preferences, `SECURITY.md`, embedded installer manifest, and VS Code workspace configuration.
- **Portable Release Archive & Expanded Automated / Live Sandbox Verification:** Added `reEngine-v0.3.0-win-x64-Portable.zip` alongside the signed Setup EXE in the 7-file `Releases\` bundle (containing all portable binaries and sanitized docs with zero installer EXEs), expanded the test suite across 14 suites (`Release` and `Debug` with automation enabled), and upgraded `run_stage2_sandbox_e2e.ps1` to verify the 12-point installer sandbox, live WinForms UI navigation, a real detached child game window launch, live SDL3 host rendering, and fail-closed `Release` automation rejection (`ExitCode = 2`).

### Changed
- Restricted the `Library` tab **Backend** dropdown to currently implemented backends (`opengl` and `software`) while retaining CLI `--backend` support for `vulkan` and `parallelCpu` on upcoming `0.3.x` engines.
- Replaced the unused visible SDL3 demo button on the **Bring-Up** shelf with a focused **Decompilation Toolchain & AOT Cache Readiness** panel, and rewrote UI labels and repository documentation to be plain-English and public-facing.
- Updated `RecompLaunchPlanBuilder` and `LauncherArgsHelper` to forward `--scale`, `opengl`/`software`, and `--jit-bringup` to `reStation v0.2.7+` engines while omitting `0.3.0+`-only flags unless `--require-backend` is set.
- Updated shared SDK dependencies (`Foundation v0.5.0`), stripped unused legacy library artifacts, updated release packaging (`Releases\Latest\`, `<Release>-Portable.zip`, hardlinks, source archive exclusions), and synced version metadata to `0.3.0`.

### Fixed
- **Installer Existing-Path, Suite Root & Profile Auto-Detection:** Fixed `reEngine.Installer` defaulting the Setup Wizard and silent CLI upgrades to `%LOCALAPPDATA%\Programs\...` and `"lightweight"` instead of reading an existing installation's `HKCU` `InstallLocation` and `data\config\settings.json` (`installProfile`, `suiteInstallDir`, `reEngineInstallDir`), and disabled WinForms `&` mnemonic swallowing on Setup Wizard labels and subtitle text.
- **Library Settings Clobber & Immediate Persistence:** Fixed `LibraryShelfPanel.ApplySettings` firing `SelectedIndexChanged` mid-initialization (which previously overwrote `resolutionScale: 4` with default `2` before `ScaleCombo` was populated), persisted `shouldAllowJitBringup`, and saved Library/Engine control changes automatically on change and close.
- **Detached `.recomp` Child Window Survival:** Fixed `RecompLaunchCoordinator` attaching a kill-on-close Windows `JobObjectGuard` during interactive launches without `--wait`, which previously terminated detached child windows (such as `reStation.exe` spawning `reStation.Player.exe`) as soon as the launcher stub exited cleanly with code `0`.
- **WinForms Label Mnemonic & Async UI Guards:** Disabled WinForms mnemonic parsing (`UseMnemonic = false`) on headers and hints so `&` characters render cleanly instead of disappearing, added button/disposal guards during async package launches, and kept header status pills aligned on window resize.
- **Decompilation Provider Loading & Split Config Fixes:** Resolved `app\<engineId>.Decomp.dll` and alias providers cleanly inside a unified `AssemblyLoadContext`, anchored runtime storage to the parent installation root instead of `\app\data`, and replaced silent `IOException` catches with diagnostic tracing.

### Security
- Enforced fail-closed `ExitCode = 2` when `--automation-port` is passed to `Release` builds (with zero automation code compiled into shipping binaries), SSRF blocking on update URLs, Zip-Slip archive traversal protection, and clean-room exclusions for `data\`, `logs\`, and local configuration files.

## [0.2.1-hf.1] - 2026-11-15

### Changed
- Aligned runtime environment and tray/background presentation helpers to `APP_ISOLATED_TEST=1`, retired legacy test-harness CLI wrappers in favor of `51/51` isolated unit tests (`0` warnings), and hardened release packaging leak checks.

## [0.2.1] - 2026-11-15

### Added
- Added Setup Wizard directory auto-sorting so selecting a parent folder places `reEngine` into `<parent>\reEngine\` without double-nesting and keeps companion `engines\` synced cleanly.
- Added 3 regression tests verifying wizard folder sorting and suite path synchronization (`53/53` passing).

### Changed
- Extracted packaging constants into `ReEnginePackagingConstants` and synchronized custom non-umbrella `engines\` creation in the Setup Wizard.

## [0.2.0] - 2026-11-15

### Added
- Added platform-agnostic `.decomp` package manifests and pipeline coordination (`IDecompPlatformProvider`) with clear 3-part remediation diagnostics (`ExitCode = 3`) when an engine is missing or unbuilt.
- Added offline CLI toolchain detection and staging (`stage_offline_toolchain.ps1`, stripped Apache-2.0 CLI Ghidra + `jlink` OpenJDK within `160 MB`, plus `THIRDPARTY_LICENSES.txt`) for `--profile Full`.
- Added shared `<dataDir>\cache\` AOT assembly indexing (`AotCacheManager`) with cross-process file locking, SHA-256 tamper checks, and clean-room rejection of pre-built generated binaries inside `.recomp` files.
- Added `.decomp` file associations, `decomp` CLI subcommand routing, `Dev\settings.example.json`, opt-in `--migrate-legacy` upgrade preservation for `data\rom\`, and 16 new tests (`50/50` passing).

### Changed
- Moved default user-scope installation path to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine` (suite root `%LOCALAPPDATA%\Programs\StreamItAll\reEngine`) while keeping auto-start disabled (`AllowAutoStart = false`).

### Fixed
- Preserved the `decomp` CLI subcommand token verbatim during launcher argument normalization instead of treating it as a relative file path.

## [0.1.3] - 2026-11-14

### Added
- Added explicit `## License & Legal` documentation covering `MPL-2.0`, `7-Zip` notices, trademark non-affiliation, and clean-room user-supplied media guarantees, plus automated release leak checks.

### Changed
- Expanded all four technical and user manuals inside `Dev\docs\`, bundled verbatim root `LICENSE` into `Builds\` and `SharedLibs\`, and cleaned workspace-specific paths out of manifests and packaged documentation.

### Fixed
- Fixed UTF-8 character encoding and relative link stripping when generating end-user markdown files inside `Releases\`.

## [0.1.2] - 2026-11-14

### Added
- Added parent-directory resolution for packaged `app\*.recomp` cartridges so `data\rom\` preload media is discovered reliably under `<InstallRoot>\data\rom\`.

### Changed
- Automatically pruned unpacked `Releases\v*` directories past the 2 most recent versions while retaining archives.

## [0.1.1] - 2026-11-14

### Added
- Added synchronous child waiting and caller working-directory path resolution to `reEngine.Launcher`, multi-engine `(EngineId, Track)` update sweeps with offline resilience, per-module runtime isolation (`<dataDir>\modules\<moduleId>\`), and contextual crash log snapshots (`33/33` tests passing).

### Changed
- Propagated exact exit codes `(0, 1, 2, 3, 4, 5, 6, 130)` synchronously through the root launcher and improved UI layout and test isolation guards.

### Fixed
- Eliminated crashes during offline update checks (logging `[WARN]` unless `--strict` is specified), removed duplicate `[HEARTBEAT]` log prefixes, and fixed update sweeps skipping `0.1.x` tracks when `0.2.0+` existed side-by-side.

## [0.1.0] - 2026-11-13

### Added
- Initial repository scaffold, build configuration, original branding assets, `reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer` (no auto-start), initial unit tests (`14/14` passing), and initial signed release packaging.
