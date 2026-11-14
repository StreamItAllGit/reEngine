# reEngine — Interfaces & Contracts

**Synced Version:** `0.1.3` (`2026-11-14`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)  
**Schema Discipline:** Canonical `camelCase` JSON keys per [`18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md`](<PROJECTS_ROOT>/Personal/_DevBible/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md)

---

## 1. `.recomp` Package Manifest (`module.recomp.json` / `*.recomp`)

Parsed by `RecompPackageReader.Load(string packagePath)`. Supports plain UTF-8 JSON (`*.recomp.json`) and ZIP archives (`*.recomp`) containing a root `module.recomp.json` (or single root `*.recomp.json`). ZIP ingestion enforces fail-closed Zip-Slip validation rejecting rooted entries, `:` drive tokens, and `..` path traversal.

| Field (`camelCase`) | Type | Required | Default / Validation | Purpose |
| :--- | :--- | :---: | :--- | :--- |
| `schemaVersion` | `int` | Yes | `>= 1` (`1`) | Manifest schema revision. |
| `moduleId` | `string` | Yes | `^[a-z0-9][a-z0-9-]*$` | Lowercase kebab slug isolating `<dataDir>\modules\<moduleId>\`. |
| `title` | `string` | Yes | Non-empty | Human-facing game/module display title. |
| `version` | `string` | Yes | SemVer (`"0.1.5"`) | Packaged module semantic version. |
| `engineId` | `string` | Yes | Non-empty | Target platform engine folder (`"reStation"`, `"re64"`, `"reCube"`). |
| `engineVersion` | `string` | Yes | Valid `SemVer` | Minimum compatible engine version required (`>=`). |
| `compatibleEngineTrack` | `string` | Yes | Track selector | Compatibility track (`X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, `X.Y.Z`). |
| `preloadRomDir` | `string` | No | `"data/rom"` | Relative preload directory inside `ModuleRootDirectory`. |
| `entryExecutable` | `string?` | No | `null` | Optional module-specific executable path relative to root. |

- **Parent-Root Cartridge Resolution (`ResolveModuleRootDirectory`):** When a `.recomp` cartridge or `.recomp.json` manifest lives directly inside `<InstallRoot>\app\`, `ModuleRootDirectory` resolves to `<InstallRoot>\` (except when `<InstallRoot>` would be a drive volume root), aligning `data\rom\` discovery with clean-root installations.
- **Preload Media Priority (`DiscoverPreloadMedia`):** Enumerates `preloadRomDir` recursively and ranks `PrimaryMediaPath` deterministically:
  1. Priority `0`: `.cue`
  2. Priority `1`: `.chd`
  3. Priority `2`: `.iso`
  4. Priority `3`: `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`
  5. Priority `4`: `.7z`, `.zip`, `.img`
  6. Priority `5`: `.bin` (tie-broken lexicographically via `StringComparer.OrdinalIgnoreCase`)

---

## 2. Settings, Virtual Registry & App Manifest Contracts

### 2.1 User Settings (`data\settings.json` — `ReEngineSettings`)
Persisted atomically with `.bak` recovery via `GranularSettingsManager`, preserving unknown keys via `[JsonExtensionData]`:
- `schemaVersion` (`int`, default `1`): settings schema version.
- `appId` (`string`, default `"reengine"`): canonical lowercase application identifier.
- `updatedAtUtc` (`string`): ISO-8601 UTC timestamp updated on save.
- `shouldCheckUpdates` (`bool`, default `true`): toggles non-blocking launch update checks.
- `shouldAutoStagePatches` (`bool`, default `true`): toggles SHA-256 compatible patch staging.
- `lastUpdateFeedUrl` (`string?`): remote HTTP/HTTPS feed URL or sandbox-contained local feed path.
- `enginesDirectoryOverride` (`string?`): optional custom path to the `engines\` root.
- `preferredResolutionScale` (`int`, default `2`): pass-through default scale (`1..8`).
- `windowWidth` / `windowHeight` (`int?`): remembered WinForms manager dimensions.

### 2.2 File Association Record (`FileAssociationRecord`)
Written under `HKCU\Software\Classes\.recomp` (`reEngine.RecompModule`) live, or `<dataDir>\virtual_registry.json` when `--test-mode` / containment is active:
- `schemaVersion`: `1`
- `extension`: `".recomp"`
- `progId`: `"reEngine.RecompModule"`
- `commandTemplate`: `"\"<exe>\" --open \"%1\""`
- `iconPath`: resolved executable/icon path (`"<exe>,0"`)
- `updatedAtUtc`: ISO-8601 UTC timestamp

### 2.3 Suite App Manifest (`Dev\app.manifest.json`)
- `schemaVersion`: `2`, `manifestType`: `"standalone-app"`, `appId`: `"reengine"`, `version`: `"0.1.3"`, `activeMilestone`: `"0.1.3"`, `goalMajorVersion`: `"1.0.0"`, `sdkVersion`: `"0.3.1"`.
- `downstreamProjects`: `["Personal/reStation", "Personal/KingsFieldRecomp", "Personal/HMBackToNature-Recomp"]` (workspace-relative slugs, zero literal drive paths).

---

## 3. Update Feed, Multi-Engine Sweep & Bootstrap SDK (`reEngine.Core`)

- **Feed Schema (`UpdateFeedManifest`):**
  - `schemaVersion` (`int`), `updatedAtUtc` (`string`), `engines` (`UpdateFeedEntry[]`).
  - Each `UpdateFeedEntry` carries `engineId`, `version`, `downloadUrl`, `sha256Hex`, `fileSizeBytes`, and optional `notes`.
- **Multi-Engine Sweep (`UpdateCoordinator.CheckAllInstalledEnginesAsync`):**
  - Deduplicates installed engines by `(EngineId, "{Major}.{Minor}.x")` and picks the highest version per track (falling back to `reStation 0.1.0` when empty).
  - Stages compatible patches to `<dataDir>\staged_updates\<engineId>\v<Ver>\` via `SafeNetworkTransfer` with SHA-256 verification without mutating breaking tracks.
  - Degrades transient `HttpRequestException`, `SocketException`, `IOException`, `TimeoutException`, `JsonException`, and non-user `OperationCanceledException` faults to `MultiEngineUpdateSweepResult.NetworkUnavailable = true` (`0` crash dumps), while keeping sandbox escapes fail-closed (`InvalidOperationException`).
- **Bootstrap SDK (`ReEngineBootstrapHelper` & `SharedLibs\reEngine\v0.1.3\`):**
  - `sdk-manifest.json` tracks `reEngine.Core.dll`, `reEngine.Core.xml`, `reEngine.Core.deps.json`, `LICENSE`, and `reEngine-Setup-v0.1.3-win-x64.exe` with byte sizes and SHA-256 digests.
  - `ResolveDefaultReEngineInstallDir(explicitDir, sandboxDataDir, testMode)` enforces `sandboxDataDir` containment in `--test-mode` or falls back to `explicitDir` -> `REENGINE_HOME` -> dynamic `<D>:\Programs\reEngine`.
  - `EnsureReEngineInstalledAsync` probes `app\version.txt` -> `app\install-manifest.json` -> `.install_manifest.json` -> `version.txt`, invokes the bundled setup EXE silently via `JobObjectGuard` (`--silent --no-launch --no-shortcuts --install-dir ...`, plus `--test-mode --data-dir ...` when sandboxed), and optionally runs `--register-assoc --headless`.

### 3.1 Engine Resolution & Telemetry Types (`reEngine.Core`)
- **`InstalledEngineCandidate`:** `EngineId`, `Version` (`SemVer`), `VersionFolderName` (`v<Ver>`), `EngineDirectory`, `ExecutablePath` (discovered via `<engineId>.exe`, `app\<engineId>.exe`, or contained `engine.manifest.json` `entryExecutable`).
- **`EngineResolutionResult`:** `Found` (`bool`), `SelectedEngine` (`InstalledEngineCandidate?`), `DiscoveredCandidates`, `DiagnosticMessage`.
- **`ReEngineTelemetryFormatter`:**
  - `FormatLogLine(level, message, utcNow)` emits `<yyyy-MM-ddTHH:mm:ss.fffZ> [v0.1.3] [PID:<id>/T:<tid>] [<LEVEL>] <message>`.
  - `StripHeartbeatPrefix(line)` strips duplicate `[HEARTBEAT] ` prefixes produced by `HeartbeatLogger`.
- **`CrashBundleService` Integration:** `Program.Main` registers global handlers and writes `crash_reengine_*.json` with `"recentLogs"` populated from the bounded 100-entry `SnapshotRecentLogs()` queue.

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Package & Association Flags:** `--open <path>` (or positional `*.recomp` / `*.recomp.json`), `--wait`, `--register-assoc`.
- **Update Flags:** `--check-updates`, `--strict`, `--feed <urlOrPath>`.
- **Directory & Mode Flags:** `--engines-dir <dir>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **Forwarded Engine / Test Flags:** `--enable-logs`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--input-script <path>`.
- **Launcher Rules (`LauncherArgsHelper`):** Normalizes caller-CWD relative paths for `PathOptions` and positional targets (preserving `http://`/`https://` `--feed` URIs), skips value tokens for `--port` / `--ui` / `--frames`, and waits synchronously whenever `ShouldWaitForChild` detects any CLI/test flag.

### 4.2 Per-Module Child Isolation (`RecompLaunchCoordinator.BuildChildLaunchPlan`)
1. Isolates `--data-dir` to `<dataDir>\modules\<moduleId>\`, `--config` to `<moduleDataDir>\settings.json` (accepting explicit `--config` only if inside `<moduleDataDir>`), and `--log-dir` to `<moduleDataDir>\logs` (or containment-checked `--log-dir`).
2. Forwards `--module <fullPkgPath>`, optional `--preload-media <PrimaryMediaPath>`, plus active wait/mode/test flags.

### 4.3 Propagated Exit Codes (`reEngine.Launcher` $\leftrightarrow$ `reEngine.App`)

| Exit Code | Trigger / Meaning |
| :---: | :--- |
| `0` | Success, `--version`/`--help`/`--dry-run` pass, or non-strict offline `--check-updates` (`[WARN]`). |
| `1` | General runtime failure, missing compatible `re*` engine, or child launch failure. |
| `2` | Invalid CLI `ArgumentException`, Zip-Slip/`InvalidDataException`/`JsonException` manifest error, or fail-closed containment violation. |
| `4` | `--check-updates --strict` failure when update feed/network is unavailable. |
| `130` | `Console.CancelKeyPress` / `OperationCanceledException` interrupt (terminates child process tree cleanly). |
| `3, 5, 6, ...` | Propagated verbatim from child `re*` engines during `--open` / `--wait`. |
