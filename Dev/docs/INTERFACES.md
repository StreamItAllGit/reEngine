# reEngine — Interfaces & Contracts

**Synced Version:** `0.1.2` (`2026-11-14`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)

---

## 1. `.recomp` Package Manifest (`module.recomp.json` / `*.recomp`)
Parsed by `RecompPackageReader.Load(string packagePath)` (supports direct JSON and Zip-Slip-guarded `.recomp` ZIP archives containing `module.recomp.json`, calling `ResolveModuleRootDirectory` so `<InstallRoot>\app\*.recomp` resolves `ModuleRootDirectory` to `<InstallRoot>\`) using canonical Standard 18 `camelCase` keys:
- `schemaVersion`: `int` (must be `>= 1`, default `1`)
- `moduleId`: `string` (validated against `^[a-z0-9][a-z0-9-]*$` for isolated `<dataDir>\modules\<moduleId>\` containment, e.g., `"kings-field-slus-00158"`)
- `title`: `string`
- `version`: `string` (Full SemVer `"0.1.3"`)
- `engineId`: `string` (required, e.g., `"reStation"`, `"re64"`, `"reCube"`)
- `engineVersion`: `string` (required minimum SemVer, e.g., `"0.1.0"`)
- `compatibleEngineTrack`: `string` (required track selector, e.g., `"0.1.x"`, `"1.x"`)
- `preloadRomDir`: `string` (defaults to `"data/rom"`, strictly contained inside module root)
- `entryExecutable`: `string?` (optional module entrypoint relative path)

Preload media discovery (`RecompPackageReader.DiscoverPreloadMedia`) prioritizes `.cue` > `.chd` > `.iso` > cartridge/disc images > `.7z`/`.zip`/`.img` > `.bin`.

## 2. Settings (`data\settings.json`) & Virtual Registry (`data\virtual_registry.json`)
`ReEngineSettings` (persisted via `GranularSettingsManager` with `[JsonExtensionData]` preservation):
- `schemaVersion`: `int` (`1`), `appId`: `string` (`"reengine"`), `updatedAtUtc`: ISO-8601 UTC
- `shouldCheckUpdates`: `bool` (default `true`)
- `shouldAutoStagePatches`: `bool` (default `true`)
- `lastUpdateFeedUrl`: `string?` (optional remote HTTP/HTTPS URL or contained local feed path)
- `enginesDirectoryOverride`: `string?`
- `preferredResolutionScale`: `int` (default `2`), `windowWidth`: `int?`, `windowHeight`: `int?`

`FileAssociationRecord` (`VirtualRegistryAssociationStore` / `WindowsRegistryAssociationStore`):
- `schemaVersion`, `extension` (`".recomp"`), `progId` (`"reEngine.RecompModule"`), `commandTemplate` (`"\"<exe>\" --open \"%1\""`), `iconPath`, `updatedAtUtc`.

## 3. Update Feed (`UpdateFeedManifest`), Multi-Engine Sweep & SDK (`ReEngineBootstrapHelper`)
`UpdateFeedManifest`: `schemaVersion`, `updatedAtUtc`, `engines[]` (`engineId`, `version`, `downloadUrl`, `sha256Hex`, `fileSizeBytes`, `notes`).
- `UpdateCheckOutcome(..., bool NetworkUnavailable = false)`: single `(EngineId, Track)` evaluation result (`Checked` preserved in-memory only for `v0.1.0` compatibility).
- `MultiEngineUpdateSweepResult(bool SweepExecuted, bool NetworkUnavailable, IReadOnlyList<UpdateCheckOutcome> Outcomes, string SummaryMessage)`: returned by `UpdateCoordinator.CheckAllInstalledEnginesAsync(...)`, which groups installed engines by `(EngineId, Track = "{Major}.{Minor}.x")` (falling back to `reStation 0.1.0`), fetches `UpdateFeedManifest` once, stages compatible patches under `<dataDir>\staged_updates\<engineId>\v<Ver>\`, alerts on breaking tracks without overwriting, and degrades transient HTTP/socket/IO/timeout/feed-JSON faults cleanly (`NetworkUnavailable = true`, `0` crash dumps) while keeping `SandboxEnvironment.AssertPathContained` escapes fail-closed.

`ReEngineBootstrapHelper`:
- `ResolveDefaultReEngineInstallDir(explicitDir, sandboxDataDir, testMode)` (fail-closed in `--test-mode`; uses `REENGINE_HOME` or dynamically constructed `<D>:\Programs\reEngine` otherwise).
- `ResolveEnginesRootDir(reEngineInstallDir)` & `BuildEngineVersionInstallDir(reEngineInstallDir, engineId, version)`.
- `EnsureReEngineInstalledAsync(...)`: checks `app\version.txt` -> `app\install-manifest.json` -> `.install_manifest.json` -> `version.txt`, runs bundled silent installer via `JobObjectGuard` if missing/older, and optionally registers `.recomp` association.

## 4. CLI Flags, Child Isolation & Exit Code Contract
- **Primary CLI Flags:** `--open <path>` (or positional `*.recomp` / `*.recomp.json`), `--wait`, `--register-assoc`, `--check-updates`, `--strict`, `--feed <urlOrPath>`, `--engines-dir <dir>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--enable-logs`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--input-script <path>`, `--version`, `--help`.
- **Per-Module Child Isolation (`RecompLaunchCoordinator.BuildChildLaunchPlan`):**
  - Isolates child `--data-dir` to `<dataDir>\modules\<moduleId>\`, `--config` to `<moduleDataDir>\settings.json` (honoring an explicit `--config` only when already inside `<moduleDataDir>`), and `--log-dir` to `<moduleDataDir>\logs` (or explicit containment-checked CLI `--log-dir`).
  - Forwards `--module <fullPkgPath>`, optional `--preload-media <primaryMediaPath>`, `--wait`, `--test-mode`, `--headless`, `--visible-test`, `--enable-logs`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, and `--input-script <path>` (all paths containment-checked against `sandboxBoundary` when sandboxed).
- **Propagated Exit Codes (`reEngine.Launcher` $\leftrightarrow$ `reEngine.App`):**
  - `0`: Success (or non-strict offline `--check-updates` `[WARN]`).
  - `1`: General runtime failure, missing resolved engine, or child failure.
  - `2`: Fail-closed sandbox/containment violation, Zip-Slip/`InvalidDataException`/`JsonException` manifest error, or invalid CLI `ArgumentException`.
  - `4`: Offline/network failure when `--check-updates --strict` is set.
  - `130`: `Console.CancelKeyPress` / `OperationCanceledException` interruption.
  - Any other child exit codes (`3`, `5`, `6`, etc.) propagate verbatim from child engine to root `reEngine.exe`.
