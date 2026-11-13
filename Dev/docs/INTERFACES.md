# reEngine — Interfaces & Contracts

**Synced Version:** `0.1.0` (`2026-11-13`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)

---

## 1. `.recomp` Package Manifest (`module.recomp.json` / `*.recomp`)
Parsed by `RecompPackageReader.Load(string packagePath)` (supports direct JSON and Zip-Slip-guarded `.recomp` ZIP archives containing `module.recomp.json`) using canonical Standard 18 `camelCase` keys:
- `schemaVersion`: `int` (must be `>= 1`, default `1`)
- `moduleId`: `string` (e.g., `"kings-field-slus-00158"`)
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

## 3. Update Feed (`UpdateFeedManifest`) & SDK Contract (`ReEngineBootstrapHelper`)
`UpdateFeedManifest`: `schemaVersion`, `updatedAtUtc`, `engines[]` (`engineId`, `version`, `downloadUrl`, `sha256Hex`, `fileSizeBytes`, `notes`).
- Compatible track patches (`EngineTrackMatcher.IsTrackCompatible` + `SemVer > installed`) are staged via `SafeNetworkTransfer.DownloadFileAsync` to `<dataDir>\staged_updates\<engineId>\v<Ver>\` when `shouldAutoStagePatches == true`.
- Newer breaking `MAJOR`/`MINOR` versions trigger alert status without overwriting active tracks.

`ReEngineBootstrapHelper`:
- `ResolveDefaultReEngineInstallDir(explicitDir, sandboxDataDir, testMode)` (fail-closed in `--test-mode`; uses `REENGINE_HOME` or dynamically constructed `<D>:\Programs\reEngine` otherwise).
- `ResolveEnginesRootDir(reEngineInstallDir)` & `BuildEngineVersionInstallDir(reEngineInstallDir, engineId, version)`.
- `EnsureReEngineInstalledAsync(...)`: checks `app\version.txt` -> `app\install-manifest.json` -> `.install_manifest.json` -> `version.txt`, runs bundled silent installer via `JobObjectGuard` if missing/older, and optionally registers `.recomp` association.

## 4. CLI Flags & Fail-Closed Sandbox Contract
- `--open <path>` (or positional `*.recomp` / `*.recomp.json`), `--register-assoc`, `--check-updates`, `--feed <urlOrPath>`, `--engines-dir <dir>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--version`, `--help`.
- `--test-mode` validates `--data-dir`, `--config`, `--log-dir`, and `--engines-dir` via `SandboxEnvironment.AssertPathContained`, binds `IFileAssociationStore` to `VirtualRegistryAssociationStore` (`<dataDir>\virtual_registry.json`), and forbids `WindowsRegistryAssociationStore` construction.
