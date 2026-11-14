# reEngine — System Architecture

**Synced Version:** `0.1.3` (`2026-11-14`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)  
**License:** Mozilla Public License Version 2.0 (`MPL-2.0`, root [`LICENSE`](<REPO_ROOT>/LICENSE))

---

## 1. Zero-Daemon Router Model & Lifecycle Boundaries

`reEngine` is a lightweight, zero-daemon top-level router and suite manager for `.recomp` static recompilation modules and side-by-side platform engines (`reStation`, `re64`, `reCube`, `rePS2`, `reAdvance`).

- **Zero Background Footprint:** Leaving the manager or a game exits all processes cleanly (`ReEnginePackagingConstants.AllowAutoStart = false`, `0 MB` idle background memory, zero Windows Startup entries).
- **Idempotent Self-Bootstrap:** Any `re*` engine or `<Game>Recomp` installer invokes `ReEngineBootstrapHelper.EnsureReEngineInstalledAsync` to install or upgrade `reEngine` without clobbering existing side-by-side engines under `engines\re<Platform>\v<Ver>\`.
- **Zero-PII Default Path Resolution:** `ReEngineBootstrapHelper.ResolveDefaultReEngineInstallDir` checks `--test-mode` containment first, then explicit directory arguments, then `REENGINE_HOME`, and finally constructs `<Drive>:\Programs\reEngine` dynamically with zero literal host paths in binaries.
- **Fail-Closed Sandbox Containment:** Under `--test-mode` or contained `Sandbox\` runs, `Program.EnforceSandboxContainment` and `SandboxEnvironment.AssertPathContained` lock `--data-dir`, `--config`, `--log-dir`, `--engines-dir`, package inputs, local update feeds, and test capture paths inside the sandbox boundary.
- **Sister-App & Read-Only Boundaries:**
  - `uStation` handles generic cycle-accurate emulation and delegates `.recomp` launches synchronously to `reEngine.exe --open <pkg> --wait`.
  - `reEngine` handles `R1–R3` `.recomp` packages only (`R4`/`R5` standalone native ports ship independently).
  - `Foundation\`, `_DevBible\`, and `<PROJECTS_ROOT>\Research\*` (`Tier R`) remain strictly read-only (`0` raw `Corpus\` copies).

---

## 2. Solution Topology & Build Invariants (`<= 400 LOC` Gate)

The solution [`Dev\reEngine.slnx`](<REPO_ROOT>/Dev/reEngine.slnx) inherits [`Dev\Directory.Build.props`](<REPO_ROOT>/Dev/Directory.Build.props):
- `LangVersion = latest`, `Nullable = enable`, `ImplicitUsings = enable`, `TreatWarningsAsErrors = true`, `Deterministic = true`.
- Release builds set `ContinuousIntegrationBuild = true`, `<DebugType>none</DebugType>`, `<DebugSymbols>false</DebugType>`, and `<PathMap>` (`proj\`, `usr\`) to prevent PDB or workstation-path leaks.
- Every `.cs` source file and `.ps1` automation script enforces `<= 400` code-only lines.

```text
reEngine.Launcher (net8.0-windows, zero reEngine.Core ref, embedded Foundation.Launcher.dll)
   └──► resolves app\reEngine.exe, normalizes caller-CWD paths, sync-waits on CLI flags
reEngine.App (net8.0-windows WinForms/CLI, Foundation.Core + reEngine.Core)
   └──► sandbox/containment guard, CLI dispatcher, RecompLaunchCoordinator, Dark Slate UI
reEngine.Core (net8.0 pure library, Foundation.Core only -> SharedLibs\reEngine\v0.1.3\)
   └──► Manifests\, Resolution\, Associations\, Updates\, Bootstrap\
reEngine.Installer (net8.0-windows single-EXE Setup, gzipped DLLs + payload.zip)
   └──► StandardInstallerHost (AllowAutoStart = false, Sky Cyan #38BDF8 accent)
reEngine.Tests (net8.0 xUnit)
   └──► 34 deterministic contract, Zip-Slip, isolation, sweep, launcher, and telemetry tests
```

---

## 3. Core Assemblies & Internal Components

### 3.1 `reEngine.Core` (`net8.0`, Pure `Foundation.Core`-Only Library)
- **`Manifests\RecompPackageManifest.cs`:**
  - `RecompPackageManifest` defines canonical `camelCase` `.recomp` package properties (`schemaVersion`, `moduleId`, `title`, `version`, `engineId`, `engineVersion`, `compatibleEngineTrack`, `preloadRomDir`, `entryExecutable`) plus `[JsonExtensionData]`.
  - `RecompPackageReader.Load` inspects ZIP magic bytes (`0x50 0x4B ...`) to dispatch plain JSON (`*.recomp.json`) or ZIP archive (`*.recomp`) parsing.
  - Archive loading enforces fail-closed Zip-Slip protection rejecting rooted paths, `:` drive tokens, and `..` traversal via `SandboxEnvironment.AssertPathContained`.
  - `RecompPackageReader.ResolveModuleRootDirectory` maps `<InstallRoot>\app\<Module>.recomp` (or `<InstallRoot>\app\module.recomp.json`) back to `<InstallRoot>\` so `DiscoverPreloadMedia` scans `<InstallRoot>\data\rom\` strictly without touching `data\gamedata\` or `app\`.
- **`Resolution\EngineVersionResolver.cs`:**
  - `SemVer` parses and compares `MAJOR.MINOR.PATCH[-prerelease]` (`-hf.<n>`, `-nightly.<n>`).
  - `EngineTrackMatcher.IsTrackCompatible` matches `X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, and exact `X.Y.Z` selectors.
  - `EngineVersionResolver` enumerates `engines\<engineId>\v<Ver>\` (probing `<engineId>.exe`, `app\<engineId>.exe`, or contained `engine.manifest.json` `entryExecutable`) and selects the highest matching version `>= manifest.EngineVersion` without crossing breaking tracks.
- **`Associations\FileAssociationStores.cs`:**
  - `IFileAssociationStore` defines `RegisterRecompAssociation`, `ReadRecompAssociation`, and `UnregisterRecompAssociation`.
  - `VirtualRegistryAssociationStore` persists atomically (temp write + `.bak` swap) to `<dataDir>\virtual_registry.json` during sandboxed runs.
  - `WindowsRegistryAssociationStore` writes strictly to `HKCU\Software\Classes` and throws `InvalidOperationException` if constructed with `testMode: true`.
- **`Updates\UpdateModels.cs` & `Updates\UpdateCoordinator.cs`:**
  - `UpdateCoordinator` persists `ReEngineSettings` via `GranularSettingsManager` and stages verified patches via `SafeNetworkTransfer` under `<dataDir>\staged_updates\<engineId>\v<Ver>\`.
  - `CheckAllInstalledEnginesAsync` sweeps every distinct `(EngineId, Major.Minor.x)` candidate (defaulting to `reStation 0.1.0` when empty), fetches `UpdateFeedManifest` once, stages compatible patches, reports breaking tracks without overwriting, and degrades transient HTTP/socket/IO/timeout/JSON errors to `NetworkUnavailable = true` (`0` crash dumps).
- **`Bootstrap\ReEngineBootstrapHelper.cs` & `Bootstrap\ReEngineTelemetryFormatter.cs`:**
  - `ReEnginePackagingConstants` freezes `AppId = "reengine"`, `DisplayName = "reEngine"`, `Version = "0.1.3"`, `BrandAccentHex = "#38BDF8"`, `AllowAutoStart = false`, `DefaultPreloadRomDir = "data/rom"`, and `VirtualRegistryFileName = "virtual_registry.json"`.
  - `ReEngineTelemetryFormatter` formats UTC `[v0.1.3] [PID:<id>/T:<tid>] [<LEVEL>]` lines and strips duplicate `[HEARTBEAT] ` prefixes from `HeartbeatLogger`.

### 3.2 `reEngine.App` (`net8.0-windows`)
- **`Program.cs`:**
  - Attaches parent console (`AttachConsole(-1)`) for non-redirected CLI output.
  - Registers `CrashBundleService` with `SnapshotRecentLogs()` (100-entry ring buffer) and `CappedSharedLogSink`.
  - Dispatches `--version`, `--help`, `--register-assoc`, `--check-updates`, `--open`, and `--headless`, catches `CancelKeyPress` (`130`), and maps `ArgumentException`, `InvalidDataException`, `JsonException`, and containment violations to Exit `2`.
- **`RecompLaunchOptions.cs` & `RecompLaunchCoordinator.cs`:**
  - Validates `moduleId` against `^[a-z0-9][a-z0-9-]*$`.
  - Isolates child `--data-dir`, `--config`, and `--log-dir` inside `<dataDir>\modules\<moduleId>\` and containment-checks `--fixture`, `--dump-screenshot`, `--dump-audio`, and `--input-script`.
  - Runs a bounded 3-second non-blocking update check before spawning the resolved engine inside `JobObjectGuard`.
- **`ReEngineMainForm.cs` & `ReEngineMainForm.Layout.cs`:**
  - Renders the Dark Slate (`#0E0E10` / `#18181B` / `#38BDF8`) WinForms suite manager with DWM immersive dark title bar and two-line tooltips.
  - Invokes `LayoutControls()` on resize and status pill updates (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `PATCH AVAILABLE`, `BREAKING AVAILABLE`, `OFFLINE / SKIPPED`, `CHECK FAILED`, `VIRTUALIZED`) to eliminate label clipping.
  - Captures `--visible-test` foreground screenshots to `Sandbox\logs\screenshots\reengine_visible_test.png` and exits cleanly with `0` prompts.

### 3.3 `reEngine.Launcher` & `reEngine.Installer`
- **`reEngine.Launcher` (`net8.0-windows`):** Single-file root `reEngine.exe` with zero reference to `reEngine.Core`. Loads embedded `Foundation.Launcher.dll`, resolves `app\reEngine.exe`, guards against self-spawn recursion, normalizes caller-CWD relative paths (`LauncherArgsHelper.NormalizePathArguments`), waits synchronously across CLI operations (`ShouldWaitForChild`), terminates child trees on `CancelKeyPress` (`130`), and propagates exact child exit codes.
- **`reEngine.Installer` (`net8.0-windows`):** Single-EXE 4-pillar setup embedding `Foundation.Core.dll.gz`, `Foundation.Installer.dll.gz`, `reEngine.Core.dll.gz`, and `payload.zip`. Strips `--autostart` flags (`SanitizeArgs`) and runs `StandardInstallerHost.Run` with `AllowAutoStart = false` and `#38BDF8` Sky Cyan styling.

### 3.4 Runtime Directory Layout & Isolation Tree
- `<InstallRoot>\reEngine.exe` — clean-root launcher forwarding to `app\reEngine.exe`
- `<InstallRoot>\AGENTS.md` — compact runtime context anchor generated during staging
- `<InstallRoot>\app\reEngine.exe` + `Foundation.Core.dll` + `reEngine.Core.dll` + `version.txt` + `LICENSE` (`MPL-2.0`)
- `<InstallRoot>\scripts\` — `.bat` registration and sandbox smoke wrappers (`interactive\launch-manager.bat`)
- `<InstallRoot>\engines\<engineId>\v<Ver>\` — immutable side-by-side platform engine tracks
- `<DataDir>\settings.json` + `<DataDir>\virtual_registry.json` (in `--test-mode`) + `<DataDir>\staged_updates\`
- `<DataDir>\modules\<moduleId>\settings.json` + `logs\` — per-module isolated runtime storage
- `<LogDir>\reengine.log` + `<LogDir>\crashes\crash_reengine_*.json` — Standard 15 telemetry and crash snapshots

### 3.5 Failure Modes & Defensive Invariants
- **Zip-Slip & Malformed Manifests:** `RecompPackageReader.ReadFromZipArchive` checks every entry before opening entries; rooted paths, drive letters (`:`), and `..` escapes throw and map to Exit `2`.
- **Launcher Self-Spawn Loop:** `reEngine.Launcher.Program` compares `Path.GetFullPath(appExe)` against `Environment.ProcessPath` and exits `1` fail-closed if they match.
- **Offline Update Drops:** `UpdateCoordinator.CheckAllInstalledEnginesAsync` isolates transient network/socket/JSON errors into `NetworkUnavailable = true` (`Exit 0` / `[WARN]`, or `Exit 4` with `--strict`) without emitting spurious crash bundles.
- **Child Orphan Prevention:** Both `ReEngineBootstrapHelper` and `RecompLaunchCoordinator` attach child processes to `Foundation.Core.JobObjectGuard` for clean tree termination.

---

## 4. Packaging & Cross-Platform Trajectory

- **Single-Pass Packaging (`Dev\scripts\build_release.ps1`):** Executes Release tests (`/warnaserror`), stages `Builds\v0.1.3\` (with `app\LICENSE`), prunes stale staging folders (`v0.1.0`, `v0.1.1`), builds/signs `reEngine-Setup-v0.1.3-win-x64.exe`, audits via `audit_installer.ps1`, writes sanitized UTF-8 `Releases\{v0.1.3,Latest}\` bundles with a fail-fast leak check, archives `Archive\reEngine-v0.1.3-src.7z`, and publishes `SharedLibs\reEngine\v0.1.3\` (`+ Latest\`).
- **Roadmap Progression:** `v0.2.0` (resumable `.part` atomic patch apply & `"supportedEngineTracks"`) -> `v0.3.0` (`--jit-bringup`, 4-backend routing, Pill/Drawer shelf UI) -> `v0.4.0` (`uStation` delegation hardening) -> `v1.0.0` (`win-x64` Gold) -> `v1.1.0–v2.0.0` (`SDL3`, mobile Zero-RWX, `browser-wasm`, console HAL, and 10-platform + first-party achievement overlay).
