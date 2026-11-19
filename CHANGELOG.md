# Changelog

All notable changes to **`reEngine`** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.4.0] - 2026-11-19

### Added
- **Unified `reEngine.Host` Native GPU Presenters, `hdpacks\` & Native Audio (`SDL_AUDIO_DRIVER`):** Implemented native OpenGL 3.3+, Vulkan 1.3+, and DirectX 12 (`directx` / CLI `dx12`) contexts and swapchain presenters via direct OS bindings (without attaching `SDL_CreateRenderer` to GPU windows), deterministic 16px horizontal-band `ParallelCpuPresenter` matching `Software` bit-for-bit at `1x`, path-contained `<dataDir>\hdpacks\` `(clutSha256Hex, rectSha256Hex)` overrides, native OS audio pinning (`wasapi` on Windows set prior to `SDL_InitSubSystem`), `--require-backend` (`ExitCode = 6`), and fullscreen presentation.
- **3-Slot Multi-Bind Input, Xbox/HID Gamepads, Dynamic `ControlOptions` & Touch Contracts:** Added `HostMultiBindRegistry` (3-slot `[1st, 2nd, 3rd]` simultaneous KBM + mouse/wheel `1001..1007`, repeat filtering, single-edge isolation), `Sdl3GamepadProvider` (Xbox & open-standard HID `2001..2032`, `0.15f` deadzone, `0.40f` trigger threshold, rumble), dynamic `ControlOptions` (`ControlsTabView`), interactive 5-column `KeybindsTabView`, and cross-platform `IHostInputProvider` / `HostTouchOverlayView`.
- **Interactive `.decomp` Bring-Up Runner, `SuiteMediaTreeWalker` & Missing-Media Prompt:** Opening `.decomp` files from Windows Explorer now launches the **Bring-Up** shelf pre-loaded with the recipe, `D0..D5` stage picker, automatic suite-tree media discovery (`SuiteMediaTreeWalker`), `MissingMediaPromptDialog` (defaulting to use-in-place with opt-in `.cue`/`.bin` staging into `data\rom\`), and live logs.
- **Standalone `--verify-module` & Frontend `--wait` / `--fullscreen` / `--save-dir` Bridge:** Added `PackageVerificationService` emitting Standard 18 camelCase JSON reports (`--json`, `--require-media`), normalized `--verify-module` selectively in `reEngine.Launcher` while preserving bare engine slugs, and added bounded `<= 3s` graceful `WM_CLOSE` tree teardown in `GracefulProcessTreeShutdown` so saves flush into `--save-dir` before process-tree termination.
- **Canonical `D4_CleanSource` & Expanded Verification:** Switched `DecompD4StrictnessValidator.CanonicalCorpusTiers` to `D4_CleanSource` (keeping `D4_FunctionalRewrite` as a non-double-counted transition alias) and added four modular xUnit suites (`97` total tests across Release & Debug+Automation builds).

### Changed
- Upgraded the `Library` shelf **Backend** dropdown to expose all five Windows backends (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`), added the **Fullscreen** checkbox and **Verify Package...** action, and aligned all UI/CLI/documentation terminology to static recompilation without emulator framing.
- Updated `reEngine.Installer` to preserve `v0.4.0` keys (`backendId: "directx"`, `isFullscreen`, `audioDriver`, `isHdTexturePackEnabled`) with `.bak` recovery and enforced the 3-MINOR retention policy on `Releases\` (pruning `v0.1.*.7z`, unpacked `v0.2.1`, and `v0.2.1-hf.1`).
- Trimmed `0.1.x` changelog history entries per Dev Bible Standard 04 (`<= 3` prior `MINOR` series retained).

### Fixed
- Fixed `LauncherArgsHelper` dropping synchronous execution on `--verify-module`, `--headless`, and `--json`, and fixed `SDL_AUDIO_DRIVER` hint ordering in `Sdl3HostWindow` / `NativeAudioDriverSelector`.
- Fixed `RecompLaunchPlanBuilder` mapping `--require-backend` failures to Exit `1` instead of Exit `6` (`BackendUnavailableException`), and fixed child suite-root derivation when `engines\<id>\v<Ver>` sits two levels below the suite root.

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
