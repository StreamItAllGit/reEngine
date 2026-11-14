# reEngine — Feature Map & Code Registry

> Maintained by `@tessa`. Maps live (`v0.1.2`) and planned (`v0.2.0` $\rightarrow$ `v2.0.0`) `reEngine` capabilities to slices in `Dev\src\` and tests in `Dev\tests\`. Status tokens strictly follow [Standard 16 §3.2](<PROJECTS_ROOT>/Personal/_DevBible/16_PROJECT_DOSSIERS_AND_FEATURE_TRACKING.md) (`Live`, `In-Dev`, `Planned`, `Deprecated`).

---

## 1. At-a-Glance Feature Summary

| Code | Feature | Status | Target Version | Primary Code Location | 1-Line Summary |
| :--- | :--- | :---: | :---: | :--- | :--- |
| `CORE-01` | **Brand Logo & Identity** | `Live` | `v0.1.0` | [`Dev\assets\reEngine.*`](<REPO_ROOT>/Dev/assets/reEngine.svg) | Original routing-diamond vector SVG, `512px` PNG, and 4-size Windows `.ico`. |
| `CORE-02` | **`.recomp` Manifest Reader & Schema** | `Live` | `v0.1.0 / v0.1.2` | [`RecompPackageManifest.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Manifests/RecompPackageManifest.cs) | Parses Standard 18 JSON/ZIP `.recomp` manifests with Zip-Slip guard and `ResolveModuleRootDirectory` parent `data\rom\` discovery for `app\*.recomp` cartridges. |
| `CORE-03` | **Side-by-Side Track Resolver** | `Live` | `v0.1.0` | [`EngineVersionResolver.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Resolution/EngineVersionResolver.cs) | Selects highest installed `engines\re<Platform>\v<Ver>\` matching `compatibleEngineTrack` and `>= engineVersion`. |
| `CORE-04` | **`.recomp` File Association Store** | `Live` | `v0.1.0` | [`FileAssociationStores.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Associations/FileAssociationStores.cs) | Registers `HKCU\Software\Classes\.recomp` live or `virtual_registry.json` under `--test-mode`. |
| `CORE-05` | **Multi-Engine Update Sweep & Offline Resilience** | `Live` | `v0.1.0 / v0.1.1` | [`UpdateCoordinator.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs) | Sweeps every installed `(engineId, track)` side-by-side (`CheckAllInstalledEnginesAsync`), degrades offline/socket drops to `[WARN]` (`0` crash dumps, `--strict` Exit `4`), and verifies SHA-256 patches. |
| `CORE-06` | **Dark Suite Manager UI & `--visible-test`** | `Live` | `v0.1.0 / v0.1.1` | [`ReEngineMainForm.cs`](<REPO_ROOT>/Dev/src/reEngine.App/ReEngineMainForm.cs) | Dark Slate WinForms manager with `sandboxBoundary` gating, `OFFLINE / SKIPPED` pill, dynamic `LayoutControls()` pill sizing, and `--visible-test` PNG showcase. |
| `CORE-07` | **Self-Bootstrapping SDK & Signed Setup EXE** | `Live` | `v0.1.0 / v0.1.2` | [`ReEngineBootstrapHelper.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Bootstrap/ReEngineBootstrapHelper.cs) | Reusable `reEngine.Core` bootstrap SDK (`SharedLibs\reEngine\v0.1.2\`), `reEngine.Launcher`, signed `reEngine.Installer` (`AllowAutoStart = false`), max-2 unpacked `Releases\`, and `Archive\`. |
| `CORE-08` | **Sync-Wait Launcher, Isolation & Telemetry** | `Live` | `v0.1.1` | [`LauncherArgsHelper.cs`](<REPO_ROOT>/Dev/src/reEngine.Launcher/LauncherArgsHelper.cs) & [`RecompLaunchCoordinator.cs`](<REPO_ROOT>/Dev/src/reEngine.App/RecompLaunchCoordinator.cs) | Normalizes caller-CWD paths, waits synchronously (`--open --wait`, exit codes `0..6, 130`), isolates child `<dataDir>\modules\<moduleId>\`, forwards test flags, emits single `[HEARTBEAT]`, and populates crash `recentLogs`. |
| `ROUTE-08` | **Resumable `.part` Staging, Atomic Apply & `"supportedEngineTracks"`** | `Planned` | `v0.2.0` | `reEngine.Core\Updates\`, `EngineVersionResolver.cs` | Decouples `2s` manifest fetch from resumable `.part` staging, atomically unpacks `staged_updates\` $\rightarrow$ `engines\`, and enables runtime `"supportedEngineTracks"` resolution. |
| `ROUTE-09` | **`--jit-bringup`, `backendId` Routing & Upstream `Foundation.Launcher` Sync** | `Planned` | `v0.3.0` | `RecompLaunchCoordinator.cs`, `Foundation.Launcher` | Forwards `--jit-bringup`, `--backend <opengl\|vulkan\|parallelCpu\|software>`, and `--require-backend` end-to-end while rolling `ShouldWaitForChild` sync upstream into `Foundation.Launcher`. |
| `ROUTE-10` | **Pill/Drawer `.recomp` Library Shelf & Track Rollback Manager** | `Planned` | `v0.3.0` | `reEngine.App` / `reEngine.Core\Resolution\` | Sub-Page Pill Navigation + Slide-Over Right Drawer for `.recomp` library browsing, `executionTierMode` badges, `HD` pack toggles, and side-by-side track pinning/rollback. |
| `ROUTE-11` | **`uStation -> reEngine --open --wait` & `R&D` Graduation Bridge** | `Planned` | `v0.4.0` | `RecompLaunchCoordinator.cs`, `reEngine.Core` | Adds `--save-dir` / `--fullscreen` passthrough, `<= 3s` `JobObjectGuard`/process-group teardown for `uStation` sessions, and one-click `R&D` `.recomp` validation. |
| `ROUTE-12` | **Cross-Platform `SDL3` Router UI & XDG/macOS/Deck Associations** | `Planned` | `v1.1.0` | `reEngine.Shell.Sdl3`, `reEngine.Core\Associations\` | Migrates `reEngine` off WinForms-only UI onto `IReEngineUiShell` (`SDL3`), adding Freedesktop `.desktop`/`mimeapps.list`, macOS `Info.plist` UTI, POSIX process-group guards, and SteamOS `16:10` gamepad navigation. |
| `ROUTE-13` | **`browser-wasm` Progressive Web Router & OPFS Loader** | `Planned` | `v1.3.0` | `reEngine.Shell.Wasm` | Runs `.recomp` + `data/rom/` browser routing over OPFS/IndexedDB with automatic COOP/COEP `SharedArrayBuffer` capability detection. |
| `ROUTE-14` | **Zero-RWX Pre-Linked Static Module Registry & 10-Platform Cert** | `Planned` | `v1.4.0–v2.0.0` | `reEngine.Core`, `Foundation.Retro` | Resolves pre-linked `IRecompStaticModuleRegistry` cartridges on `W^X`-locked hosts (`ios-arm64`, `switch`, `xbox`, `playstation`) and unifies 10-`hostRid` suite certification. |
| `ROUTE-15` | **First-Party `.recomp` Achievement Overlay & Vault Bridge** | `Planned` | `v2.0.0` | `reEngine.Core`, `IReEngineUiShell` | Displays clean-room `manifestType: "recomp-achievements"` unlocks/toasts and syncs local progress with `uStation` (zero RetroAchievements.org / `rcheevos` calls). |

---

## 2. Live `v0.1.1` & Planned Feature Cards (`v0.2.0` $\rightarrow$ `v2.0.0`)

### `CORE-08` — Sync-Wait Launcher, Per-Module Isolation & Telemetry (`Live` — `v0.1.1`)
- **Problem Addressed:** Closed all `5` verified `v0.1.0` lockstep findings (async root launcher detach returning `0` early, relative CWD path breakage, unhandled `--check-updates` socket drop crashes with empty crash `recentLogs`, duplicate `[HEARTBEAT]` prefixes, `FirstOrDefault()` update blindspot, and shared child `settings.json` collisions).
- **Behavior:** Root launcher waits synchronously for `--open`, `--wait`, `--check-updates`, `--register-assoc`, and `--headless` while resolving caller-CWD relative paths; `reEngine.App` isolates child runtime state inside `<dataDir>\modules\<moduleId>\`, forwards `--preload-media` + runtime/test flags, logs `[v0.1.1] [PID:<id>/T:<tid>]` with single `[HEARTBEAT]`, and captures `SnapshotRecentLogs()` on fatal errors.
- **Extension Point / What to Touch:** [`LauncherArgsHelper.cs`](<REPO_ROOT>/Dev/src/reEngine.Launcher/LauncherArgsHelper.cs), [`RecompLaunchCoordinator.cs`](<REPO_ROOT>/Dev/src/reEngine.App/RecompLaunchCoordinator.cs), [`ReEngineTelemetryFormatter.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Bootstrap/ReEngineTelemetryFormatter.cs), and [`UpdateCoordinator.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs).

### `ROUTE-08` — Resumable `.part` Staging, Atomic Apply & `"supportedEngineTracks"` (`Planned` — `v0.2.0`)
- **Problem Addressed:** Decouples short manifest discovery timeouts from large binary patch downloads and enables runtime multi-track compatibility matching.
- **Behavior:** Separates `2s` manifest discovery from resumable `.part` downloads (`SafeNetworkTransfer`), verifies SHA-256 and atomically promotes staged updates into `engines\<engineId>\v<Ver>\`, and evaluates `"supportedEngineTracks"` compatibility in `EngineVersionResolver`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\Updates\UpdateCoordinator.cs` and `Dev\src\reEngine.Core\Resolution\EngineVersionResolver.cs`.
- **Verification:** `Sandbox\` tests assert resumable `.part` recovery, SHA-256 atomic promotion into `engines\`, and side-by-side track compatibility without mutating older versions.

### `ROUTE-09` — `--jit-bringup`, `backendId` Routing & Upstream `Foundation.Launcher` (`Planned` — `v0.3.0`)
- **Behavior:** Forwards `--jit-bringup`, `--require-backend`, and all `4` canonical `backendId` targets (`"opengl"`, `"vulkan"`, `"parallelCpu"`, `"software"`) to child `re*` engines, and rolls the proven `ShouldWaitForChild` + CWD normalization helper upstream into `Foundation.Launcher`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\RecompLaunchCoordinator.cs`, `RecompLaunchOptions.cs`, and `Foundation.Launcher.RootLauncherHost`.
- **Verification:** Confirms `supportedEngineTracks` compatibility bridging (`0.1.x`..`0.3.x`), `<= 15s` single-prefix heartbeats, and end-to-end backend/JIT argument passthrough through root `reEngine.exe`.

### `ROUTE-10` — Pill/Drawer `.recomp` Library Shelf & Track Rollback (`Planned` — `v0.3.0`)
- **Behavior:** Replaces flat list management with Standard 09 Sub-Page Pill Navigation (`Library`, `Engines`, `Bring-Up`), Slide-Over Right Drawer inspection, `executionTierMode` (`100% static R3` / `isR4PromotionReady`) indicators, and safe side-by-side track rollback.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\ReEngineMainForm*.cs` and `Dev\src\reEngine.Core\Resolution\`.
- **Verification:** Dual-viewport (`1920x1080` + `390x844` / `844x390`) DOM/PNG layout inspection in `Sandbox\logs\screenshots\`.

### `ROUTE-11` — `uStation -> reEngine --open --wait` & `R&D` Bridge (`Planned` — `v0.4.0`)
- **Behavior:** Acts as the sole `.recomp` launch delegation endpoint for `uStation` (exposing `--save-dir` and `--fullscreen` passthrough plus `<= 3s` watchdog child-tree cleanup) and links `R&D` `graduate-recomp` outputs directly into `reEngine`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.App\RecompLaunchCoordinator.cs` and `Dev\src\reEngine.Core\Manifests\`.
- **Verification:** Bounded parent-harness `Sandbox\` test confirming full session blocking, intact `.mcr`/`.rst1` save flush, and `<= 3s` watchdog child cleanup.

### `ROUTE-12` / `ROUTE-13` / `ROUTE-14` — 10-Platform North Star Stack (`Planned` — `v1.1.0–v2.0.0`)
- **Cross-OS Shell (`v1.1.0`):** Moves `RecompLaunchCoordinator` into `reEngine.Core`, introduces `reEngine.Shell.Sdl3` (`IReEngineUiShell`), and abstracts HKCU/XDG/macOS `.recomp` associations behind `IFileAssociationStore` (virtualized in `--test-mode`).
- **Mobile, Web & Consoles (`v1.2.0–v2.0.0`):** Adds Android SAF `data/rom/` routing, `browser-wasm` OPFS/COOP-COEP progressive loader, and Zero-RWX `IRecompStaticModuleRegistry` pre-linked cartridge dispatch across `ios-arm64`, `switch`, `xbox`, and `playstation`.
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\Associations\`, `reEngine.Shell.Sdl3`, `reEngine.Shell.Wasm`, and `Foundation.Retro.IRecompStaticModuleRegistry`.
- **Non-Goals:** Strictly `0 MB` background daemon (`AllowAutoStart = false`), zero generic ROM emulator cores inside `reEngine`, zero copyrighted retail binaries or BIOS ROMs, and strictly `R1–R3` `.recomp` only (graduated `R4`/`R5` full native ports ship as standalone EXEs/installers outside `reEngine`).

### `ROUTE-15` — First-Party `.recomp` Achievement Overlay & Vault Bridge (`Planned` — `v2.0.0`)
- **Behavior:** Reads clean-room `manifestType: "recomp-achievements"` unlocks emitted by `re*` engines, renders non-blocking UI toasts via `IReEngineUiShell`, and syncs mastery stats locally with `uStation` (strictly `0` RetroAchievements.org / `rcheevos` network calls).
- **Extension Point / What to Touch:** `Dev\src\reEngine.Core\` achievement manifest contracts and `IReEngineUiShell` overlay renderer.
