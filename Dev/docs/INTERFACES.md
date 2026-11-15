# reEngine — Interfaces & Contracts

**Synced Version:** `0.2.0` (`2026-11-15`)  
**Owner:** `reEngine` Core (`@devv` / `@dean`)  
**Schema Discipline:** Canonical `camelCase` JSON keys per [`18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md`](<PROJECTS_ROOT>/Personal/_DevBible/18_DATA_DICTIONARY_AND_SCHEMA_LOOKUP.md)

---

## 1. `.recomp` & `.decomp` Package Manifests

### 1.1 `.recomp` Package Manifest (`module.recomp.json` / `*.recomp`)
Parsed by `RecompPackageReader.Load(string packagePath)` (UTF-8 JSON or ZIP archive with fail-closed Zip-Slip validation).

| Field (`camelCase`) | Type | Required | Default / Validation | Purpose |
| :--- | :--- | :---: | :--- | :--- |
| `schemaVersion` | `int` | Yes | `>= 1` (`1`) | Manifest schema revision. |
| `moduleId` | `string` | Yes | `^[a-z0-9][a-z0-9-]*$` | Lowercase kebab slug isolating `<dataDir>\modules\<moduleId>\`. |
| `title` | `string` | Yes | Non-empty | Human-facing game/module display title. |
| `version` | `string` | Yes | SemVer (`"0.2.0"`) | Packaged module semantic version. |
| `engineId` | `string` | Yes | Non-empty | Target platform engine folder (`"reStation"`, `"reNes"`, `"re64"`, `"reCube"`). |
| `engineVersion` | `string` | Yes | Valid `SemVer` | Minimum compatible engine version required (`>=`). |
| `compatibleEngineTrack` | `string` | Yes | Track selector | Compatibility track (`X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, `X.Y.Z`). |
| `preloadRomDir` | `string` | No | `"data/rom"` | Relative preload directory inside `ModuleRootDirectory`. |
| `entryExecutable` | `string?` | No | `null` | Optional module-specific executable path relative to root. |

- **Parent-Root Cartridge Resolution (`ResolveModuleRootDirectory`):** Resolves `<InstallRoot>\app\<Module>.recomp` back to `<InstallRoot>\` so `DiscoverPreloadMedia` scans `<InstallRoot>\data\rom\` strictly.
- **Preload Media Priority (`DiscoverPreloadMedia`):** Ranks `.cue` (`0`) > `.chd` (`1`) > `.iso` (`2`) > `.gcm`/`.rvz`/`.n64`/`.z64`/`.v64`/`.sfc`/`.smc`/`.nes`/`.gb`/`.gbc`/`.gba` (`3`) > `.7z`/`.zip`/`.img` (`4`) > `.bin` (`5`).

### 1.2 `.decomp` Package Manifest (`module.decomp.json` / `*.decomp` — `DecompPackageManifest`)
Parsed by `DecompPipelineCoordinator.ParseManifest(string packagePath)` (UTF-8 JSON or ZIP archive with Zip-Slip containment) and implements `Foundation.Retro.IDecompPackageManifest`:

| Field (`camelCase`) | Type | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `moduleId` | `string` | Filename fallback | Canonical lowercase-kebab-case module identifier. |
| `title` | `string` | `""` | Human-facing module display title. |
| `platformId` | `string` | `""` | Target console identifier (`"psx"`, `"nes"`, `"n64"`). |
| `engineId` | `string` | Required | Target platform engine (`"reStation"`, `"reNes"`, `"re64"`). |
| `engineVersion` | `string` | `"0.1.0"` | Minimum compatible engine SemVer (`>=`). |
| `compatibleEngineTrack` | `string` | `""` | Compatible engine track selector (`"0.1.x"`, `"0.2.x"`). |
| `decompSchemaVersion` | `string` | `"1.0.0"` | Standard 18 SemVer of the `.decomp` container schema. |
| `targetDecompTier` | `string` | `"D3"` | Target decompilation tier (`none`, `D0`–`D5`). |
| `goalDecompTier` | `string` | `"D3"` | Goal decompilation tier (`none`, `D0`–`D5`). |
| `packageVersion` | `string?` | `null` | Optional SemVer of this `.decomp` package. |
| `upstreamResearchPath` | `string?` | `null` | Optional read-only Tier R research repository path/id. |
| `ghidraProjectName` | `string?` | `null` | Optional headless Ghidra project name. |
| `toolchainRequirements` | `DecompToolchainRequirements?` | `null` | `minimumGhidraVersion` (`"11.2"`), `requiresHeadlessJdk` (`true`), `requiredExtensions` (`string[]`). |
| `preloadRomDir` | `string` | `"data/rom"` | Relative preload media directory inside module root. |
| `recipeEntryPoint` | `string?` | `null` | Optional clean-room recipe/script path relative to package root. |

---

## 2. Decompilation Pipeline, Offline Toolchain & AOT Cache Contracts (`reEngine.Core`)

### 2.1 `IDecompPlatformProvider`, `DecompPipelineCoordinator` & `DecompUnbuiltEngineError`
- **`IDecompPlatformProvider`:** Discovered in-process or via `<engineDir>\{,app\}<engineId>.dll`:
  - Properties: `PlatformId`, `DisplayName`, `MinimumEngineVersion`.
  - Methods: `Task<DecompStageResult> RunDecompAsync(DecompRunOptions options, CancellationToken ct)` and `DecompCleanRoomValidationResult ValidateCleanRoomOutput(string outputDirectory)`.
- **`DecompStage` & `DecompRunOptions`:** `D0`–`D5` enum (`D3` default), carrying `PackagePath`, `OutputDirectory`, `DataDirectory`, `TargetStage`, `EngineDirectoryOverride`, `ToolchainDirectory`, `DryRun`, `Headless`, `PlatformArguments`.
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:**
  - Resolves engine (`--engine-dir` override or `EngineVersionResolver`). When missing/unbuilt (`re64`, `reGenesis`, `reCube`), populates `DecompDispatchResult.UnbuiltEngineError` (`DecompUnbuiltEngineError.Create`) with deterministic 3-part formatting:
    - `[1/3 What Failed]` module + engine context
    - `[2/3 Missing]` engine ID, `compatibleEngineTrack`, and minimum version
    - `[3/3 Remediation]` install under peer `engines\` (or local `engines\`) or pass `--engine-dir <path>`
  - Delegates `RunDecompAsync`, then enforces both `provider.ValidateCleanRoomOutput` and `ValidateCleanRoomOutputDirectory` (rejecting `.bin`, `.iso`, `.cue`, `.chd`, `.n64`, `.z64`, `.nes`, `.sav`, `.srm`, `.state`, `*.g.cs`, `*.Generated.dll`).

### 2.2 `OfflineToolchainLocator` (`ReEngineInstallProfile`)
- **`ReEngineInstallProfile`:** `Lightweight` (`0`) vs `Full` (`1`), `MaxToolchainBudgetMb = 160`, `ThirdPartyLicensesFileName = "THIRDPARTY_LICENSES.txt"`.
- **`OfflineToolchainLocator.ResolveToolchainDirectory(baseOrInstallDir, explicitToolchainDir)`:** Checks explicit override first, then sibling peer `..\toolchain\` (primary under `suiteInstallDir`), then falls back to `.\toolchain\` for portable/Sandbox layouts.
- **`OfflineToolchainLocator.Locate(baseOrInstallDir, profile, explicitToolchainDir, requirements)`:** Zero-network probe checking `ghidra\support\analyzeHeadless(.bat)`, `jdk\bin\java(.exe)` (when `RequiresHeadlessJdk`), `THIRDPARTY_LICENSES.txt`, and absence of `ghidra\GPL\`. Under `Lightweight` without `toolchain\`, returns `IsAvailable = false` guiding `--profile Full` or `--toolchain-dir <path>`.

### 2.3 `IAotCacheManager` & `AotCacheManager`
- **`AotCacheKey`:** `(ModuleId, RomSha256, EngineId, EngineVersion, ModuleVersion)` -> `ToCacheVersionSegment()` (`<romSha256_16>-<EngineId>-<EngineVersion>-<ModuleVersion>`).
- **`AotCacheEntry` Serialized JSON Keys (`Standard 18`):** `key`, `cacheDirectory`, `generatedAssemblyPath`, `createdAtUtc`, `lastAccessedAtUtc`, `fileSizeBytes` (`[JsonPropertyName("fileSizeBytes")]`), `sha256Hex` (`[JsonPropertyName("sha256Hex")]`), `isValid`.
- **Cache Operations (`<dataDir>\cache\aot-index.json` + `.bak` + `.aot-index.lock`):**
  - `ResolveCacheDirectory(key)` -> `<dataDir>\cache\<moduleId>\<segment>\` via `AotCacheLayoutHelper`.
  - `StoreCompiledAssemblyAsync(key, sourceAssemblyPath)` -> atomic temp write, `.bak` backup rotation on `aot-index.json`, size + SHA-256 indexing -> `AotCompileResult`.
  - `TryGetEntryAsync(key)` -> validates file existence, byte size, and SHA-256 (`null` on mismatch/corruption).
  - `EvictStaleEntriesAsync(moduleId, activeKey)` -> removes contained stale directories for `moduleId` while preserving `activeKey` and other modules.
  - `ValidateCleanRoomRecompPackage(recompPackagePath)` -> inspects ZIP `.recomp` archives or directories and returns `CleanRoomRecompCheckResult` (`IsClean == false` if any `*.Generated.dll` or `*.g.cs` exists).

---

## 3. Settings, Associations, Update Sweep & Bootstrap SDK

### 3.1 Settings Overlay & Standard 18 Keys (`data\settings.json` & `Dev\settings.example.json`)
- **`ReEngineSettings`:** Preserves unknown keys via `[JsonExtensionData]`: `schemaVersion`, `appId` (`"reengine"`), `updatedAtUtc`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `preferredResolutionScale`, `windowWidth`, `windowHeight`.
- **Standard 18 `v0.2.0` Config Keys (`Dev\settings.example.json` & Installer Persistence):**
  - `installProfile` (`"lightweight"` | `"full"`)
  - `reEngineInstallDir` (`"%LOCALAPPDATA%\\Programs\\StreamItAll\\reEngine\\reEngine"`)
  - `suiteInstallDir` (`"%LOCALAPPDATA%\\Programs\\StreamItAll\\reEngine"`)
  - `umbrellaSubpath` (`"StreamItAll\\reEngine\\reEngine"`)
  - `decompSchemaVersion` (`"1.0.0"`), `goalDecompTier` (`"D3"`), `targetDecompTier` (`"D3"`)
- **`ReEngineAppStatusHelper` (`reEngine.App`):** Reads/writes `installProfile` (`GetInstallProfile`/`SetInstallProfile`), summarizes `<dataDir>\cache\*.Generated.dll` (`InspectAotCache`), and resolves peer `..\toolchain` (via `OfflineToolchainLocator`) + `enginesDir` parent/local fallbacks to report `DECOMP: FULL` vs `DECOMP: ROUTER` (`InspectDecompStatus`).

### 3.2 File Associations (`FileAssociationRecord` — `IFileAssociationStore`)
Persisted under `HKCU\Software\Classes` (`WindowsRegistryAssociationStore`, throws when `testMode: true`) or `<dataDir>\virtual_registry.json` (`VirtualRegistryAssociationStore`):
- **`.recomp` (`reEngine.RecompModule`):** `"\"<exe>\" --open \"%1\""`
- **`.decomp` (`reEngine.DecompModule`):** `"\"<exe>\" decomp --module \"%1\""`
- UI button **Register Associations** registers both `.recomp` and `.decomp` via `Program.ResolveLauncherExecutablePath()`.

### 3.3 Suite App Manifest (`Dev\app.manifest.json`), Bootstrap SDK & Installer CLI
- **`Dev\app.manifest.json`:** `schemaVersion = 2`, `appId = "reengine"`, `version = "0.2.0"`, `activeMilestone = "0.2.0"`, `goalMajorVersion = "1.0.0"`, `sdkVersion = "0.4.0"`, `downstreamProjects = ["Personal/reStation", "Personal/KingsFieldRecomp", "Personal/HMBackToNature-Recomp"]`.
- **Bootstrap SDK (`ReEngineBootstrapHelper` -> `<SHARED_LIBS>\reEngine\v0.2.0\`):**
  - `ReEnginePackagingConstants`: `Version = "0.2.0"`, `StudioName = "StreamItAll"`, `SuiteName = "reEngine"`, `UmbrellaSubpath = "StreamItAll\\reEngine\\reEngine"`, `DefaultInstallProfile = "lightweight"`, `AllowAutoStart = false`.
  - `ResolveDefaultReEngineInstallDir(explicitDir, sandboxDataDir, testMode)` enforces `sandboxDataDir` containment in `--test-mode` or resolves `explicitDir` -> `REENGINE_HOME` -> `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine`.
  - `ResolveDefaultSuiteInstallDir(...)` resolves `%LOCALAPPDATA%\Programs\StreamItAll\reEngine`.
  - `ResolveToolchainDir(reEngineInstallDir)` / `ResolveEnginesRootDir(reEngineInstallDir)` resolve peer `..\toolchain` and `..\engines` under `suiteInstallDir` (non-drive-root parent) as primary, falling back to `.\toolchain` and `.\engines` for isolated Sandbox/portable installs.
- **Installer (`reEngine.Installer` & `LegacyMigrationHelper`):**
  - `Program`: `AllowAutoStart = false`, `BootstrapSharedSuiteDaemon = false`, `AllowLegacyMigration = true`, strips `--autostart` (`SanitizeArgs`), supports `--profile Lightweight|Full`, `--suite-dir` (`<dir>\reEngine` when `--install-dir` and `--reengine-dir` omitted), `--reengine-dir` (mapped to `--install-dir` when omitted), `--migrate-legacy`, `--confirm-legacy-cleanup`.
  - Interactive GUI mode (`InstallerWizardForm`) wires `CreateThemeOptions()` and reads `SelectedProfileId`, `SecondaryInstallDir`, `MigrateLegacyChecked`, and `InstallDirTextBox.Text` on exit to execute opt-in migration and `PersistInstallProfileSettings`.
  - `LegacyMigrationHelper`: copies `data\settings.json` and preserves `data\rom\*` from `<LEGACY_PROGRAMS_DIR>\reEngine` (or contained `legacy-programs\reEngine` in `--test-mode`), and gates cleanup of stranded `engines\reStation\v0.1.1\app\` + `logs\crashes\crash_*.json` behind explicit human confirmation.

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>]`, `--open <path>` (or positional `*.recomp`/`*.recomp.json`), `--wait`, `--register-assoc`.
- **Update & Directory Flags:** `--check-updates`, `--strict`, `--feed <urlOrPath>`, `--engines-dir <dir>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`.
- **Mode & Test Passthrough Flags:** `--test-mode`, `--dry-run`, `--headless`, `--visible-test`, `--enable-logs`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--input-script <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **Launcher Rules (`LauncherArgsHelper`):** Normalizes caller-CWD relative paths for `PathOptions` and positional file args (while preserving `decomp`, `help`, `/?`, and `http://`/`https://` `--feed` URIs), skips value tokens for `--port` / `--ui` / `--frames` / `--stage`, and sync-waits on CLI/`decomp`/test flags.

### 4.2 Per-Module Child Isolation (`RecompLaunchCoordinator.BuildChildLaunchPlan`)
1. Isolates `--data-dir` to `<dataDir>\modules\<moduleId>\`, `--config` to `<moduleDataDir>\settings.json` (accepting explicit `--config` only inside `<moduleDataDir>`), and `--log-dir` to `<moduleDataDir>\logs` (or containment-checked `--log-dir`).
2. Forwards `--module <fullPkgPath>`, optional `--preload-media <PrimaryMediaPath>`, plus active wait/mode/test flags.

### 4.3 Propagated Exit Codes (`reEngine.Launcher` <-> `reEngine.App`)

| Exit Code | Trigger / Meaning |
| :---: | :--- |
| `0` | Success, `--version`/`--help`/`--dry-run` pass, or non-strict offline `--check-updates` (`[WARN]`). |
| `1` | General runtime failure, missing compatible `re*` engine (`.recomp`), or child launch failure. |
| `2` | Invalid CLI `ArgumentException`, Zip-Slip/`InvalidDataException`/`JsonException` manifest error, or fail-closed containment violation. |
| `3` | `.decomp` unbuilt/missing platform engine (`DecompUnbuiltEngineError` 3-part diagnostic). |
| `4` | `--check-updates --strict` failure when update feed/network is unavailable. |
| `130` | `Console.CancelKeyPress` / `OperationCanceledException` interrupt (terminates child process tree cleanly). |
| `5, 6, ...` | Propagated verbatim from child `re*` engines during `--open` / `--wait`. |
