# reEngine — Feature Map & Code Registry

> Maps live (`v0.2.1-hf.1`) and planned (`v0.3.0` $\rightarrow$ `v2.0.0`) `reEngine` capabilities to slices in `Dev\src\` and tests in `Dev\tests\`. Status tokens strictly follow Standard 16 (`Live`, `In-Dev`, `Planned`, `Deprecated`).

---

## 1. At-a-Glance Feature Summary

| Code | Feature | Status | Target Version | Primary Code Location | 1-Line Summary |
| :--- | :--- | :---: | :---: | :--- | :--- |
| `CORE-01` | **Brand Logo & Identity** | `Live` | `v0.1.0` | [`Dev\assets\reEngine.*`](Dev/assets/reEngine.svg) | Original routing-diamond vector SVG, `512px` PNG, and 4-size Windows `.ico`. |
| `CORE-02` | **`.recomp` Manifest Reader & Schema** | `Live` | `v0.1.0 / v0.1.2` | [`RecompPackageManifest.cs`](Dev/src/reEngine.Core/Manifests/RecompPackageManifest.cs) | Parses Standard 18 JSON/ZIP `.recomp` manifests with Zip-Slip guard and `ResolveModuleRootDirectory` parent `data\rom\` discovery for `app\*.recomp` cartridges. |
| `CORE-03` | **Side-by-Side Track Resolver** | `Live` | `v0.1.0` | [`EngineVersionResolver.cs`](Dev/src/reEngine.Core/Resolution/EngineVersionResolver.cs) | Selects highest installed `engines\re<Platform>\v<Ver>\` matching `compatibleEngineTrack` and `>= engineVersion`. |
| `CORE-04` | **`.recomp` & `.decomp` File Association Store** | `Live` | `v0.1.0 / v0.2.0` | [`FileAssociationStores.cs`](Dev/src/reEngine.Core/Associations/FileAssociationStores.cs) | Registers `HKCU\Software\Classes\.recomp` and `.decomp` live or inside `virtual_registry.json` under `APP_ISOLATED_TEST=1`. |
| `CORE-05` | **Multi-Engine Update Sweep & Offline Resilience** | `Live` | `v0.1.0 / v0.1.1` | [`UpdateCoordinator.cs`](Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs) | Sweeps every installed `(engineId, track)` side-by-side (`CheckAllInstalledEnginesAsync`), degrades offline/socket drops to `[WARN]` (`0` crash dumps, `--strict` Exit `4`), and verifies SHA-256 patches. |
| `CORE-06` | **Dark Suite Manager UI** | `Live` | `v0.1.0 / v0.2.0` | [`ReEngineMainForm.cs`](Dev/src/reEngine.App/ReEngineMainForm.cs) | Dark Slate WinForms manager with `APP_ISOLATED_TEST=1` containment gating, `DECOMP: FULL\|ROUTER` & `OFFLINE / SKIPPED` pills, `Profile:` selector, and AOT cache summary. |
| `CORE-07` | **Self-Bootstrapping SDK & Signed Setup EXE** | `Live` | `v0.1.0 / v0.2.1-hf.1` | [`ReEngineBootstrapHelper.cs`](Dev/src/reEngine.Core/Bootstrap/ReEngineBootstrapHelper.cs) & [`ReEngineWizardPathNormalizer.cs`](Dev/src/reEngine.Installer/ReEngineWizardPathNormalizer.cs) | Reusable `reEngine.Core` bootstrap SDK (`SharedLibs\reEngine\v0.2.1-hf.1\`), `reEngine.Launcher`, signed `reEngine.Installer` with live `<parent>\reEngine` auto-sort (`AllowAutoStart = false`, `Foundation v0.4.1-hf.1`), max-2 unpacked `Releases\`, and `Archive\`. |
| `CORE-08` | **Sync-Wait Launcher, Isolation & Telemetry** | `Live` | `v0.1.1 / v0.2.0` | [`LauncherArgsHelper.cs`](Dev/src/reEngine.Launcher/LauncherArgsHelper.cs) & [`RecompLaunchCoordinator.cs`](Dev/src/reEngine.App/RecompLaunchCoordinator.cs) | Normalizes caller-CWD paths (preserving `decomp`), waits synchronously (`--open --wait`, exit codes `0..6, 130`), isolates child `<dataDir>\modules\<moduleId>\`, forwards test flags, emits single `[HEARTBEAT]`, and captures crash `recentLogs`. |
| `CORE-09` | **`.decomp` Package Schema & `D0`–`D5` Pipeline Router** | `Live` | `v0.2.0` | [`DecompPackageManifest.cs`](Dev/src/reEngine.Core/Manifests/DecompPackageManifest.cs) & [`DecompPipelineCoordinator.cs`](Dev/src/reEngine.Core/Decomp/DecompPipelineCoordinator.cs) | Validates `.decomp` manifests, dispatches `decomp --module --stage D0..D5` to platform `re*` engines, and emits 3-part `Exit 3` diagnostics when an engine is unbuilt. |
| `DECOMP-02` | **Offline CLI Ghidra (`<= 160 MB`) & OpenJDK Stager** | `Live` | `v0.2.0` | [`OfflineToolchainLocator.cs`](Dev/src/reEngine.Core/Decomp/OfflineToolchainLocator.cs) & [`stage_offline_toolchain.ps1`](Dev/scripts/stage_offline_toolchain.ps1) | Stages and validates stripped Apache-2.0 CLI Ghidra (`GPL/` + `docs/` + Swing removed) + `jlink` OpenJDK + `THIRDPARTY_LICENSES.txt` for `--profile Full` offline decompilation. |
| `AOT-01` | **Shared `<dataDir>\cache\` AOT Manager & Clean-Room Guard** | `Live` | `v0.2.0` | [`AotCacheManager.cs`](Dev/src/reEngine.Core/Aot/AotCacheManager.cs) | Cross-process locked `<dataDir>\cache\aot-index.json`, SHA-256 tamper invalidation, stale eviction, and strict `.recomp` rejection of `*.Generated.dll` / `*.g.cs` artifacts. |
| `SUITE-01` | **Peer `StreamItAll\reEngine\` Layout, Profiles & `--migrate-legacy`** | `Live` | `v0.2.0` | [`LegacyMigrationHelper.cs`](Dev/src/reEngine.Installer/LegacyMigrationHelper.cs) & [`settings.example.json`](Dev/settings.example.json) | Defaults user installs to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`, exposes `--profile Lightweight|Full`, `--suite-dir`/`--reengine-dir`, and opt-in legacy migration preserving `data\rom\`. |
| `ROUTE-08` | **Resumable `.part` Staging, Atomic Apply & `"supportedEngineTracks"`** | `Planned` | `v0.3.0` | `reEngine.Core\Updates\`, `EngineVersionResolver.cs` | Decouples `2s` manifest fetch from resumable `.part` staging, atomically unpacks `staged_updates\` $\rightarrow$ `engines\`, and enables runtime `"supportedEngineTracks"` resolution. |
| `ROUTE-09` | **`--jit-bringup`, `backendId` Routing & Upstream `Foundation.Launcher` Sync** | `Planned` | `v0.3.0` | `RecompLaunchCoordinator.cs`, `Foundation.Launcher` | Forwards `--jit-bringup`, `--backend <opengl\|vulkan\|parallelCpu\|software>`, and `--require-backend` end-to-end while rolling `ShouldWaitForChild` sync upstream into `Foundation.Launcher`. |
| `ROUTE-10` | **Pill/Drawer `.recomp` Library Shelf & Track Rollback Manager** | `Planned` | `v0.3.0` | `reEngine.App` / `reEngine.Core\Resolution\` | Sub-Page Pill Navigation + Slide-Over Right Drawer for `.recomp` library browsing, `executionTierMode` badges, `HD` pack toggles, and side-by-side track pinning/rollback. |
| `ROUTE-11` | **`uStation -> reEngine --open --wait` & `R&D` Graduation Bridge** | `Planned` | `v0.4.0` | `RecompLaunchCoordinator.cs`, `reEngine.Core` | Adds `--save-dir` / `--fullscreen` passthrough, `<= 3s` `JobObjectGuard`/process-group teardown for `uStation` sessions, and one-click `R&D` `.recomp` validation. |
| `ROUTE-12` | **Cross-Platform `SDL3` Router UI & XDG/macOS/Deck Associations** | `Planned` | `v1.1.0` | `reEngine.Shell.Sdl3`, `reEngine.Core\Associations\` | Migrates `reEngine` off WinForms-only UI onto `IReEngineUiShell` (`SDL3`), adding Freedesktop `.desktop`/`mimeapps.list`, macOS `Info.plist` UTI, POSIX process-group guards, and SteamOS `16:10` gamepad navigation. |
| `ROUTE-13` | **`browser-wasm` Progressive Web Router & OPFS Loader** | `Planned` | `v1.3.0` | `reEngine.Shell.Wasm` | Runs `.recomp` + `data/rom/` browser routing over OPFS/IndexedDB with automatic COOP/COEP `SharedArrayBuffer` capability detection. |
| `ROUTE-14` | **Zero-RWX Pre-Linked Static Module Registry & 10-Platform Cert** | `Planned` | `v1.4.0–v2.0.0` | `reEngine.Core`, `Foundation.Retro` | Resolves pre-linked `IRecompStaticModuleRegistry` cartridges on `W^X`-locked hosts (`ios-arm64`, `switch`, `xbox`, `playstation`) and unifies 10-`hostRid` suite certification. |
| `ROUTE-15` | **First-Party `.recomp` Achievement Overlay & Vault Bridge** | `Planned` | `v2.0.0` | `reEngine.Core`, `IReEngineUiShell` | Displays clean-room `manifestType: "recomp-achievements"` unlocks/toasts and syncs local progress with `uStation` (zero RetroAchievements.org / `rcheevos` calls). |

---

## 2. Live `v0.1.1`–`v0.2.0` & Planned Feature Cards (`v0.3.0` $\rightarrow$ `v2.0.0`)

### `CORE-08` — Sync-Wait Launcher, Per-Module Isolation & Telemetry (`Live` — `v0.1.1 / v0.2.0`)
- **Problem Addressed:** Closed all `5` verified `v0.1.0` lockstep findings and preserved `decomp` subcommand routing in `v0.2.0`.
- **Behavior:** Root launcher waits synchronously for `--open`, `decomp`, `--wait`, `--check-updates`, `--register-assoc`, and `--tray-only` while normalizing caller-CWD relative paths; `reEngine.App` isolates child runtime state inside `<dataDir>\modules\<moduleId>\`, forwards `--preload-media` + runtime/diagnostic flags, logs `[v0.2.1-hf.1] [PID:<id>/T:<tid>]` with single `[HEARTBEAT]`, and captures `SnapshotRecentLogs()` on fatal errors.
- **Extension Point / What to Touch:** [`LauncherArgsHelper.cs`](Dev/src/reEngine.Launcher/LauncherArgsHelper.cs), [`RecompLaunchCoordinator.cs`](Dev/src/reEngine.App/RecompLaunchCoordinator.cs), [`ReEngineTelemetryFormatter.cs`](Dev/src/reEngine.Core/Bootstrap/ReEngineTelemetryFormatter.cs), and [`UpdateCoordinator.cs`](Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs).

### `CORE-09` / `DECOMP-02` — `.decomp` Orchestrator & Offline Toolchain (`Live` — `v0.2.0`)
- **Problem Addressed:** Provides a clean-room, platform-agnostic `.decomp` entry point (`D0`–`D5`) alongside `--profile Full` offline Apache-2.0 CLI Ghidra + `jlink` OpenJDK staging without runtime network access or copyrighted asset leakage.
- **Behavior:** `DecompPackageManifest` parses `.decomp` JSON/ZIP packages, `FileAssociationStores` binds `HKCU\Software\Classes\.decomp` (`reEngine.DecompModule`), `DecompPipelineCoordinator` delegates `reEngine.exe decomp --module <file.decomp>` to the matching `re*` engine (or returns `Exit 3` with a 3-part remediation error when unbuilt), and `OfflineToolchainLocator` validates `<InstallRoot>\toolchain\` + `THIRDPARTY_LICENSES.txt` (`<= 160 MB`).
- **Extension Point / What to Touch:** [`DecompPackageManifest.cs`](Dev/src/reEngine.Core/Manifests/DecompPackageManifest.cs), [`DecompPipelineCoordinator.cs`](Dev/src/reEngine.Core/Decomp/DecompPipelineCoordinator.cs), [`OfflineToolchainLocator.cs`](Dev/src/reEngine.Core/Decomp/OfflineToolchainLocator.cs), and [`stage_offline_toolchain.ps1`](Dev/scripts/stage_offline_toolchain.ps1).

### `AOT-01` / `SUITE-01` — Shared `<dataDir>\cache\` AOT Cache & Peer Suite Layout (`Live` — `v0.2.0`)
- **Problem Addressed:** Centralizes local AOT `.Generated.dll` caching away from distributable `.recomp` packages and transitions default non-admin installs onto `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`.
- **Behavior:** `AotCacheManager` maintains cross-process locked `<dataDir>\cache\aot-index.json` entries (SHA-256 validated, backup-protected, evictable by quota), rejects non-clean-room `.recomp` packages containing `*.Generated.dll` / `*.g.cs`, adopts `Foundation v0.4.1-hf.1` `--profile Lightweight|Full` + `--suite-dir` + `--reengine-dir`, ships `Dev\settings.example.json`, and offers opt-in `--migrate-legacy` preservation for `data\rom\`.
- **Extension Point / What to Touch:** [`AotCacheManager.cs`](Dev/src/reEngine.Core/Aot/AotCacheManager.cs), [`ReEngineBootstrapHelper.cs`](Dev/src/reEngine.Core/Bootstrap/ReEngineBootstrapHelper.cs), and [`LegacyMigrationHelper.cs`](Dev/src/reEngine.Installer/LegacyMigrationHelper.cs).

### `ROUTE-08` — Resumable `.part` Staging, Atomic Apply & `"supportedEngineTracks"` (`Planned` — `v0.3.0`)
- **Problem Addressed:** Decouples short manifest discovery timeouts from large binary patch downloads and enables runtime multi-track compatibility matching.
- **Behavior:** Separates `2s` manifest discovery from resumable `.part` downloads (`SafeNetworkTransfer`), verifies SHA-256 and atomically promotes staged updates into `engines\<engineId>\v<Ver>\`, and evaluates `"supportedEngineTracks"` compatibility in `EngineVersionResolver`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\Updates\UpdateCoordinator.cs` and `Dev\src\reEngine.Core\Resolution\EngineVersionResolver.cs`.
- **Verification:** Isolated `APP_ISOLATED_TEST=1` tests assert resumable `.part` recovery, SHA-256 atomic promotion into `engines\`, and side-by-side track compatibility without mutating older versions.

### `ROUTE-09` — `--jit-bringup`, `backendId` Routing & Upstream `Foundation.Launcher` (`Planned` — `v0.3.0`)
- **Behavior:** Forwards `--jit-bringup`, `--require-backend`, and all `4` canonical `backendId` targets (`"opengl"`, `"vulkan"`, `"parallelCpu"`, `"software"`) to child `re*` engines, and rolls the proven `ShouldWaitForChild` + CWD normalization helper upstream into `Foundation.Launcher`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\RecompLaunchCoordinator.cs`, `RecompLaunchOptions.cs`, and `Foundation.Launcher.RootLauncherHost`.
- **Verification:** Confirms `supportedEngineTracks` compatibility bridging (`0.1.x`..`0.3.x`), `<= 15s` single-prefix heartbeats, and end-to-end backend/JIT argument passthrough through root `reEngine.exe`.

### `ROUTE-10` — Pill/Drawer `.recomp` Library Shelf & Track Rollback (`Planned` — `v0.3.0`)
- **Behavior:** Replaces flat list management with Standard 09 Sub-Page Pill Navigation (`Library`, `Engines`, `Bring-Up`), Slide-Over Right Drawer inspection, `executionTierMode` (`100% static R3` / `isR4PromotionReady`) indicators, and safe side-by-side track rollback.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\ReEngineMainForm*.cs` and `Dev\src\reEngine.Core\Resolution\`.
- **Verification:** Automated UI layout & zero-overlap control verification under `APP_ISOLATED_TEST=1`.

### `ROUTE-11` — `uStation -> reEngine --open --wait` & `R&D` Bridge (`Planned` — `v0.4.0`)
- **Behavior:** Acts as the sole `.recomp` launch delegation endpoint for `uStation` (exposing `--save-dir` and `--fullscreen` passthrough plus `<= 3s` watchdog child-tree cleanup) and links `R&D` `graduate-recomp` outputs directly into `reEngine`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\RecompLaunchCoordinator.cs` and `Dev\src\reEngine.Core\Manifests\`.
- **Verification:** Bounded parent-harness `APP_ISOLATED_TEST=1` test confirming full session blocking, intact `.mcr`/`.rst1` save flush, and `<= 3s` watchdog child cleanup.

### `ROUTE-12` / `ROUTE-13` / `ROUTE-14` — 10-Platform North Star Stack (`Planned` — `v1.1.0–v2.0.0`)
- **Cross-OS Shell (`v1.1.0`):** Moves `RecompLaunchCoordinator` into `reEngine.Core`, introduces `reEngine.Shell.Sdl3` (`IReEngineUiShell`), and abstracts HKCU/XDG/macOS `.recomp`/`.decomp` associations behind `IFileAssociationStore` (virtualized under `APP_ISOLATED_TEST=1`).
- **Mobile, Web & Consoles (`v1.2.0–v2.0.0`):** Adds Android SAF `data/rom/` routing, `browser-wasm` OPFS/COOP-COEP progressive loader, and Zero-RWX `IRecompStaticModuleRegistry` pre-linked cartridge dispatch across `ios-arm64`, `switch`, `xbox`, and `playstation`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\Associations\`, `reEngine.Shell.Sdl3`, `reEngine.Shell.Wasm`, and `Foundation.Retro.IRecompStaticModuleRegistry`.
- **Non-Goals:** Strictly `0 MB` background daemon (`AllowAutoStart = false`), zero generic ROM emulator cores inside `reEngine`, zero copyrighted retail binaries or BIOS ROMs, and strictly `R1–R3` `.recomp`/`.decomp` only (graduated `R4`/`R5` full native ports ship as standalone EXEs/installers outside `reEngine`).

### `ROUTE-15` — First-Party `.recomp` Achievement Overlay & Vault Bridge (`Planned` — `v2.0.0`)
- **Behavior:** Reads clean-room `manifestType: "recomp-achievements"` unlocks emitted by `re*` engines, renders non-blocking UI toasts via `IReEngineUiShell`, and syncs mastery stats locally with `uStation` (strictly `0` RetroAchievements.org / `rcheevos` network calls).
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\` achievement manifest contracts and `IReEngineUiShell` overlay renderer.
