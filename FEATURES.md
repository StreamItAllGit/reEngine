# Features — reEngine

Overview of what `reEngine` supports today (`v0.3.0`) and what is planned next. For upcoming milestones, see [ROADMAP.md](ROADMAP.md).

## Feature Summary

| Feature | Status | Since | Primary Code | What It Does |
| :--- | :---: | :---: | :--- | :--- |
| **Brand Identity & Assets** | `Live` | `v0.1.0` | [`Dev\assets\reEngine.*`](Dev/assets/reEngine.svg) | Original routing-diamond vector SVG, PNG, and multi-size Windows icon. |
| **`.recomp` Package Manifest Reader** | `Live` | `v0.1.0` | [`RecompPackageManifest.cs`](Dev/src/reEngine.Core/Manifests/RecompPackageManifest.cs) | Parses JSON/ZIP `.recomp` packages with path traversal protection, multi-track compatibility, and `data\rom\` discovery. |
| **Side-by-Side Track & Alias Resolver** | `Live` | `v0.1.0` | [`EngineVersionResolver.cs`](Dev/src/reEngine.Core/Resolution/EngineVersionResolver.cs) | Selects the highest compatible engine under `engines\<id>\v<Ver>\`, honoring `.ready`, version pins, and `EngineAliasCatalog`. |
| **File Associations (`.recomp` & `.decomp`)** | `Live` | `v0.1.0` | [`FileAssociationStores.cs`](Dev/src/reEngine.Core/Associations/FileAssociationStores.cs) | Registers per-user `.recomp` and `.decomp` handlers in `HKCU\Software\Classes` (or virtual registry during isolated tests). |
| **Multi-Engine Update Sweep & Resumable Staging** | `Live` | `v0.3.0` | [`UpdatePackageApplier.cs`](Dev/src/reEngine.Core/Updates/UpdatePackageApplier.cs) | Polls feeds (`2s` timeout), resumes SSRF-checked `.part` downloads via HTTP `Range`, verifies SHA-256, and promotes `.ready` engines atomically. |
| **Dark Slate 3-Shelf Suite Manager UI** | `Live` | `v0.3.0` | [`ReEngineMainForm.cs`](Dev/src/reEngine.App/ReEngineMainForm.cs) | Dark Slate WinForms manager with `Library`, `Engines`, and `Bring-Up` shelves, collapsible `Inspector Drawer`, and status pills. |
| **Shared `reEngine.Host.dll` SDL3 Runtime** | `Live` | `v0.3.0` | [`HostSessionRunner.cs`](Dev/src/reEngine.Host/Core/HostSessionRunner.cs) | Reusable SDL3 window/audio/input host, `F1` 5-tab settings drawer, `F3` performance HUD, and first-run user media prompt. |
| **Detached & Sync-Wait Package Launcher** | `Live` | `v0.3.0` | [`RecompLaunchCoordinator.cs`](Dev/src/reEngine.App/RecompLaunchCoordinator.cs) | Launches `.recomp` packages cleanly in interactive detached mode (keeping child windows alive) or synchronous `--wait` mode with per-module isolation. |
| **Clean-Room `.decomp` Coordinator** | `Live` | `v0.2.0` | [`DecompPipelineCoordinator.cs`](Dev/src/reEngine.Core/Decomp/DecompPipelineCoordinator.cs) | Loads platform decompilation providers (`app\<engineId>.Decomp.dll`), validates clean-room output, and reports missing engines clearly (Exit `3`). |
| **Offline CLI Decompilation Toolchain** | `Live` | `v0.2.0` | [`OfflineToolchainLocator.cs`](Dev/src/reEngine.Core/Decomp/OfflineToolchainLocator.cs) | Stages and validates stripped Apache-2.0 CLI Ghidra + `jlink` OpenJDK under `toolchain\` (`--profile Full`) with zero runtime downloads. |
| **Local Precompiled AOT Cache Manager** | `Live` | `v0.2.0` | [`AotCacheManager.cs`](Dev/src/reEngine.Core/Aot/AotCacheManager.cs) | Indexes `<dataDir>\cache\` AOT assemblies with file locks, SHA-256 validation, and strict rejection of pre-built binaries inside `.recomp` files. |
| **Peer Suite Installer & Settings Migration** | `Live` | `v0.3.0` | [`LegacyMigrationHelper.cs`](Dev/src/reEngine.Installer/LegacyMigrationHelper.cs) | Installs to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`, consolidates `data\config\settings.json`, supports `--profile Lightweight\|Full`, and preserves `data\rom\`. |
| **Emulator Vault & Research Bridge** | `Planned` | `v0.4.0` | `RecompLaunchCoordinator.cs` | Adds `--save-dir` and `--fullscreen` passthrough, bounded teardown for frontend launchers, and one-click module validation (`--verify-module`). |
| **Cross-Platform SDL3 Router & Web/Mobile** | `Planned` | `v1.1.0–v2.0.0` | `reEngine.Core`, SDL3 Shell | Replaces Windows-only UI and Registry calls with SDL3, XDG/macOS handlers, WebAssembly OPFS loading, and zero-JIT console registries. |
| **Local First-Party `.recomp` Achievements** | `Planned` | `v2.0.0` | `reEngine.Core`, Host Overlay | Displays clean-room achievement unlock toasts and tracks mastery locally with zero external network calls. |

---

## Capability Details

### Package Manifests & Side-by-Side Engine Resolution
- **What it does:** Reads JSON or ZIP `.recomp` packages with strict Zip-Slip path checks, scans parent `<GameRoot>\data\rom\` directories automatically (`DiscoverPreloadMedia`), matches bidirectional engine aliases (`reSlate` / `reStation`, `reCartOne` / `reNes`, `reCart64` / `re64`, `reBlast16` / `reGenesis`), honors `supportedEngineTracks` and `pinnedEngineVersions`, and verifies `.ready` markers before launch.
- **Entry Points:** [`RecompPackageManifest.cs`](Dev/src/reEngine.Core/Manifests/RecompPackageManifest.cs), [`EngineVersionResolver.cs`](Dev/src/reEngine.Core/Resolution/EngineVersionResolver.cs), [`EngineAliasCatalog.cs`](Dev/src/reEngine.Core/Resolution/EngineAliasCatalog.cs)
- **Extension Point / What to Touch:** Register additional platform aliases in `EngineAliasCatalog` and track descriptors in `EngineManifestModel`.

### Interactive UI & Detached / Synchronous Launch Routing
- **What it does:** Lets users select currently supported render backends (`opengl` or `software`), resolution scale (`1x`, `2x`, `4x`), and optional live JIT fallback from the `Library` shelf, then open any `.recomp` package without terminating detached child game windows (such as `reStation.exe` launching `reStation.Player.exe`). Supports CLI `--open --wait` with full exit-code propagation, `--backend` / `--scale` / `--jit-bringup` forwarding (`reStation v0.2.7+` and `0.3.0+`), and per-game `<dataDir>\modules\<moduleId>\` isolation.
- **Entry Points:** [`ReEngineMainForm.cs`](Dev/src/reEngine.App/ReEngineMainForm.cs), [`LibraryShelfPanel.cs`](Dev/src/reEngine.App/Ui/LibraryShelfPanel.cs), [`RecompLaunchCoordinator.cs`](Dev/src/reEngine.App/RecompLaunchCoordinator.cs), [`RecompLaunchPlanBuilder.cs`](Dev/src/reEngine.App/RecompLaunchPlanBuilder.cs), [`LauncherArgsHelper.cs`](Dev/src/reEngine.Launcher/LauncherArgsHelper.cs)
- **Tests:** [`LauncherAndLaunchPlanTests.cs`](Dev/tests/reEngine.Tests/LauncherAndLaunchPlanTests.cs), [`SettingsMigrationAndLaunchTests.cs`](Dev/tests/reEngine.Tests/SettingsMigrationAndLaunchTests.cs), [`run_stage2_sandbox_e2e.ps1`](Dev/scripts/run_stage2_sandbox_e2e.ps1)

### Shared `reEngine.Host.dll` SDL3 Runtime & Overlays
- **What it does:** Provides platform engines with a console-agnostic SDL3 window, nearest/aspect RGBA presentation (`HostRgbaSurfaceBackend`), synchronized stereo audio ring-buffer playback (`Sdl3AudioPump`), multi-schema input (`HostInputMapper`), `F1` 5-tab settings drawer (`F1SettingsDrawerView`), `F3` FPS/backend HUD (`F3HudOverlayView`), and first-run disc/ROM verification prompt (`FirstRunMediaGateView`). Release builds exclude all test-automation code and fail closed with exit code `2` if `--automation-port` is passed.
- **Entry Points:** [`HostSessionRunner.cs`](Dev/src/reEngine.Host/Core/HostSessionRunner.cs), [`HostContracts.cs`](Dev/src/reEngine.Host/Contracts/HostContracts.cs), [`F1SettingsDrawerView.cs`](Dev/src/reEngine.Host/Ui/F1SettingsDrawerView.cs), [`HostInputMapper.cs`](Dev/src/reEngine.Host/Input/HostInputMapper.cs)
- **Tests:** [`HostSurfaceAndInputTests.cs`](Dev/tests/reEngine.Tests/HostSurfaceAndInputTests.cs), [`HostUiDrawerAndGateTests.cs`](Dev/tests/reEngine.Tests/HostUiDrawerAndGateTests.cs), [`HostReleaseAutomationStripTests.cs`](Dev/tests/reEngine.Tests/HostReleaseAutomationStripTests.cs)

### Clean-Room `.decomp` Orchestration, Offline Toolchain & AOT Cache
- **What it does:** Parses `.decomp` packages, loads platform decompiler assemblies (`<engineId>.Decomp.dll`) in a contract-unified `AssemblyLoadContext`, validates zero-warning / zero-decompiler-tell clean-room compliance (`DecompD4StrictnessValidator`), locates optional offline CLI Ghidra + OpenJDK toolchains (`--profile Full`), and tracks precompiled AOT game assemblies locally in `<dataDir>\cache\aot-index.json` with SHA-256 verification.
- **Entry Points:** [`DecompPipelineCoordinator.cs`](Dev/src/reEngine.Core/Decomp/DecompPipelineCoordinator.cs), [`DecompD4StrictnessValidator.cs`](Dev/src/reEngine.Core/Decomp/DecompD4StrictnessValidator.cs), [`OfflineToolchainLocator.cs`](Dev/src/reEngine.Core/Decomp/OfflineToolchainLocator.cs), [`AotCacheManager.cs`](Dev/src/reEngine.Core/Aot/AotCacheManager.cs)
- **Tests:** [`DecompAndToolchainTests.cs`](Dev/tests/reEngine.Tests/DecompAndToolchainTests.cs), [`DecompProviderAndD4ValidatorTests.cs`](Dev/tests/reEngine.Tests/DecompProviderAndD4ValidatorTests.cs), [`AotCacheManagerTests.cs`](Dev/tests/reEngine.Tests/AotCacheManagerTests.cs)

### Resumable Updates, Installer & Unified Configuration
- **What it does:** Keeps background checks off (`AllowAutoStart = false`), defaults non-admin installs into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`, sorts parent folder selections into `<parent>\reEngine\`, merges legacy split `settings.json` configs cleanly into `data\config\settings.json`, sweeps all installed engine tracks (`UpdateCoordinator`), and safely downloads/promotes updates with SSRF and Zip-Slip checks (`UpdatePackageApplier`).
- **Entry Points:** [`UpdatePackageApplier.cs`](Dev/src/reEngine.Core/Updates/UpdatePackageApplier.cs), [`UpdateCoordinator.cs`](Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs), [`SettingsMigrationHelper.cs`](Dev/src/reEngine.App/SettingsMigrationHelper.cs), [`ReEngineWizardPathNormalizer.cs`](Dev/src/reEngine.Installer/ReEngineWizardPathNormalizer.cs)
- **Tests:** [`UpdateStagingAndTracksTests.cs`](Dev/tests/reEngine.Tests/UpdateStagingAndTracksTests.cs), [`InstallerAndAppStatusTests.cs`](Dev/tests/reEngine.Tests/InstallerAndAppStatusTests.cs), [`InstallerWizardAutoSortTests.cs`](Dev/tests/reEngine.Tests/InstallerWizardAutoSortTests.cs)
