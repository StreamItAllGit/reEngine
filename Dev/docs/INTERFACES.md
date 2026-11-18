# reEngine — Interfaces & Contracts

**Synced Version:** `0.3.0` (`2026-11-18`)  
**Scope:** Package Manifests, `reEngine.Host` Runtime Contracts, Update/Settings JSON Schemas & CLI Reference  
**Schema Style:** Canonical `camelCase` JSON keys

---

## 1. `.recomp`, `.decomp`, and `engine.manifest.json` Schemas

### 1.1 `.recomp` Package Manifest (`module.recomp.json` / `*.recomp`)
Parsed by `RecompPackageReader.Load(string packagePath)` (UTF-8 JSON or ZIP archive with fail-closed Zip-Slip validation).

| Field (`camelCase`) | Type | Required | Default / Validation | Purpose |
| :--- | :--- | :---: | :--- | :--- |
| `schemaVersion` | `int` | Yes | `>= 1` (`1`) | Manifest schema revision. |
| `moduleId` | `string` | Yes | `^[a-z0-9][a-z0-9-]*$` | Lowercase kebab slug isolating `<dataDir>\modules\<moduleId>\`. |
| `title` | `string` | Yes | Non-empty | Human-facing game/module display title. |
| `version` | `string` | Yes | SemVer (`"0.3.0"`) | Packaged module semantic version. |
| `engineId` | `string` | Yes | Non-empty | Target engine folder or alias (`"reStation"`/`"reSlate"`, `"reNes"`/`"reCartOne"`, `"re64"`/`"reCart64"`, `"reGenesis"`/`"reBlast16"`, `"reCube"`). |
| `engineVersion` | `string` | Yes | Valid `SemVer` | Minimum compatible engine version required (`>=`). |
| `compatibleEngineTrack` | `string` | Yes | Track selector | Primary compatibility track (`X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, `X.Y.Z`). |
| `supportedEngineTracks` | `string[]?` | No | `null` | Ordered list of compatible engine tracks checked before `compatibleEngineTrack`. |
| `backendId` | `string?` | No | `null` | Preferred default render backend (`opengl`, `software`, or `vulkan`/`parallelCpu` for upcoming `0.3.x` engines). |
| `resolutionScale` | `int?` | No | `null` | Preferred integer resolution scale factor (`1..8`). |
| `executionTierMode` | `string?` | No | `null` | Execution mode (`aotPrimary`, `jitBringup`, `threadedIr`). |
| `preloadRomDir` | `string` | No | `"data/rom"` | Relative preload directory inside `ModuleRootDirectory`. |
| `entryExecutable` | `string?` | No | `null` | Optional module-specific executable path relative to root. |

- **Parent-Root Cartridge Resolution (`ResolveModuleRootDirectory`):** Resolves `<InstallRoot>\app\<Module>.recomp` back to `<InstallRoot>\` so `DiscoverPreloadMedia` scans `<InstallRoot>\data\rom\` strictly.
- **Preload Media Priority (`DiscoverPreloadMedia`):** Ranks `.cue` (`0`) > `.chd` (`1`) > `.iso` (`2`) > `.gcm`/`.rvz`/`.n64`/`.z64`/`.v64`/`.sfc`/`.smc`/`.nes`/`.gb`/`.gbc`/`.gba` (`3`) > `.7z`/`.zip`/`.img` (`4`) > `.bin` (`5`).

### 1.2 `.decomp` Package Manifest (`module.decomp.json` / `*.decomp` — `DecompPackageManifest`)
Parsed by `DecompPipelineCoordinator.ParseManifest(string packagePath)` and implements `Foundation.Retro.IDecompPackageManifest`:

| Field (`camelCase`) | Type | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `moduleId` | `string` | Filename fallback | Canonical lowercase-kebab-case module identifier. |
| `title` | `string` | `""` | Human-facing module display title. |
| `platformId` | `string` | `""` | Target console identifier (`"psx"`, `"nes"`, `"n64"`, `"genesis"`). |
| `engineId` | `string` | Required | Target platform engine (`"reStation"`/`"reSlate"`, `"reNes"`/`"reCartOne"`, `"re64"`/`"reCart64"`, `"reGenesis"`/`"reBlast16"`). |
| `engineVersion` | `string` | `"0.1.0"` | Minimum compatible engine SemVer (`>=`). |
| `compatibleEngineTrack` | `string` | `""` | Primary engine track selector (`"0.2.x"`, `"0.3.x"`). |
| `supportedEngineTracks` | `string[]?` | `null` | Ordered compatible track constraints checked before `compatibleEngineTrack`. |
| `decompProviderAssembly` | `string?` | `null` | Explicit contained relative DLL path overriding default `<engineId>.Decomp.dll` probing. |
| `decompSchemaVersion` | `string` | `"1.0.0"` | Semantic version of the `.decomp` container schema. |
| `targetDecompTier` | `string` | `"D3"` | Target decompilation tier (`none`, `D0`–`D5`). |
| `goalDecompTier` | `string` | `"D3"` | Goal decompilation tier (`none`, `D0`–`D5`). |
| `packageVersion` | `string?` | `null` | Optional SemVer of this `.decomp` package. |
| `upstreamResearchPath` | `string?` | `null` | Optional read-only upstream research repository path/id. |
| `ghidraProjectName` | `string?` | `null` | Optional CLI Ghidra project name. |
| `toolchainRequirements` | `DecompToolchainRequirements?` | `null` | `minimumGhidraVersion` (`"11.2"`), `requiresHeadlessJdk` (`true`), `requiredExtensions` (`string[]`). |
| `preloadRomDir` | `string` | `"data/rom"` | Relative preload media directory inside module root. |
| `recipeEntryPoint` | `string?` | `null` | Optional clean-room recipe/script path relative to package root. |

### 1.3 `engine.manifest.json` (`EngineManifestModel`)
Optional descriptor inside `engines\<engineId>\v<Ver>\engine.manifest.json` read by `RecompLaunchPlanBuilder.Supports030Routing`:
- Fields: `schemaVersion`, `engineId`, `displayName`, `version`, `engineVersion`, `compatibleEngineTrack`, `supportedEngineTracks`, `platformId`, `entryExecutable`, `decompProviderAssembly`, `executionTierMode`, `backendId`, `resolutionScale`, plus `[JsonExtensionData]`.

### 1.4 Clean-Room Package Naming & Bidirectional `engineId` Aliases (`EngineAliasCatalog`)
- **Clean-Room Package Naming:** Distributable clean-room packages use neutral filenames (`MyGame.recomp`, `SampleModule.decomp`) and lowercase-kebab `moduleId` values (`my-game`).
- **Bidirectional `engineId` Aliasing (`EngineAliasCatalog`):** Exact matches take precedence, with symmetric lookup across coded and legacy engine IDs:

| Alias Engine ID | Standard Engine ID | Target Platform |
| :--- | :--- | :--- |
| `reSlate` | `reStation` | PlayStation 1 (`psx`, R3000A) |
| `reCartOne` | `reNes` | NES (`nes`, 6502) |
| `reCart64` | `re64` | Nintendo 64 (`n64`, VR4300) |
| `reBlast16` | `reGenesis` | Sega Genesis (`genesis`, m68k) |

---

## 2. `reEngine.Host` Runtime Contracts (`SharedLibs\reEngine\v0.3.0\reEngine.Host.dll`)

Child platform engines reuse `reEngine.Host.dll` built on `Foundation v0.5.0` (`PixelOverlayCanvas`, `HighResFramePacer`, `AudioRingBuffer`, `RgbaPngWriter`, `WavCaptureWriter`) and `ppy.SDL3-CS`.

### 2.1 Core Host Interfaces & Frame Contracts (`reEngine.Host.Contracts`)
- **`HostInputSchemaKind` (`reEngine.Host.Input`):** `SchemaA` (`0` — classic D-pad + face buttons), `SchemaB` (`1` — dual-input keyboard/mouse + gamepad), `SchemaC` (`2` — analog/C-button layout).
- **`HostRenderBackendKind`:** `OpenGl`, `Vulkan`, `ParallelCpu`, `Software`.
- **`HostHotkeyFlags` (`[Flags]` `uint`):** `None`, `ToggleDrawer` (`F1`), `ToggleHud` (`F3`), `DumpFrame` (`F12`), `Escape`.
- **`HostInputFrame` (`public readonly struct`):** `FrameSequence` (`long`), `DigitalButtons` (`HostGamepadButtons`), `LeftStickX`/`LeftStickY`/`RightStickX`/`RightStickY` (`float`), `TriggerLeft`/`TriggerRight` (`float`), `MouseDeltaX`/`MouseDeltaY` (`float`), `MouseButtons` (`int`), `HotkeyFlags` (`uint`), `ActiveSchema` (`string`).
- **`IHostSessionCallbacks`:**
  - `ValueTask OnFrameTickAsync(long frameNumber, HostInputFrame input, Span<byte> rgbaFramebuffer, int width, int height, CancellationToken ct)`
  - `int ReadAudioSamples(Span<short> interleavedStereoPcm)`
- **`HostSessionRunner.RunAsync(HostSessionOptions, IHostSessionCallbacks, heartbeatSink, ct)`:**
  - Returns `HostSessionResult(ExitCode, FramesRendered, IsDrawerOpen, IsHudVisible, ActiveTab, CapturedFramePath, CapturedAudioPath, Summary)`.
  - Fails closed (`ExitCode = 2`) in `Release` builds whenever `HostSessionOptions.AutomationPort.HasValue`.

### 2.2 Host Overlays & Input Mapping (`reEngine.Host.Ui`, `reEngine.Host.Input`)
- **`F1SettingsDrawerView`:** Renders 5 `HostDrawerTabId` tabs (`Video`, `Controls`, `Keybinds`, `System`, `DiscMedia`) into `PixelOverlayCanvas`, exposing hit-testable `HostWidgetBounds` (`F1Drawer`, `Tab_Video`, `Tab_Controls`, `Tab_Keybinds`, `Tab_System`, `Tab_DiscMedia`, `Drawer_CloseButton`, `Drawer_ActionButton`) and integer scale `1x/2x/4x`.
- **`F3HudOverlayView`:** Compact single-row HUD (`F3Hud`) showing FPS, frame time (`ms`), backend, scale factor, active execution tier, and audio buffer fill ratio.
- **`FirstRunMediaGateView`:** First-run user media prompt (`data\rom\`) displaying expected SHA-256 and exposing `MediaGate_BrowseButton` + `MediaGate_DemoBypassButton`.
- **`HostInputMapper` & `HostInputSchemas`:** Supports dynamic keybind remaps, live Schema A/B/C hot-swapping, and synthetic key injection for debug automation.

---

## 3. Decompilation Pipeline, Strictness Validator, Offline Toolchain & AOT Cache (`reEngine.Core`)

### 3.1 `IDecompPlatformProvider`, `DecompPipelineCoordinator` & `D4BuildReport`
- **`IDecompPlatformProvider`:** Discovered in-process or via probe order (`decompProviderAssembly` -> `<id>.Decomp.dll` -> `app\<id>.Decomp.dll` -> `<id>.dll` -> `app\<id>.dll` across aliases).
  - `ProviderLoadContext` unifies shared contract assemblies (`reEngine.Core`, `reEngine.Host`, `Foundation.Core`, `Foundation.Retro`) with `AssemblyLoadContext.Default` and supports `.deps.json` and same-directory sibling DLL fallback.
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:** Resolves engine/aliases, returns 3-part `DecompUnbuiltEngineError` (`ExitCode = 3`) if missing, executes `RunDecompAsync`, and runs both `provider.ValidateCleanRoomOutput` and `ValidateCleanRoomOutputDirectory`.
- **`D4BuildReport` & `DecompD4StrictnessValidator`:**
  - Parses `d4_build_report.json` accepting `compilerFlags` as either `string[]` or space-delimited `string` (`FlexibleCompilerFlagsJsonConverter`), and reads `isZeroWarningsVerified` / `isZeroDecompilerTellsVerified` alongside legacy `zeroWarningsVerified` / `zeroDecompilerTellsVerified`.
  - Rejects `compilerErrorCount > 0`, `compilerWarningCount > 0`, `craftsmanshipTellCount > 0`, `-Wno-everything`, missing `Corpus\tiers\{D1_Raw,D2_Structured,D3_Mapped,D4_FunctionalRewrite,D5_ByteExact}` folders during corpus strictness checks, raw C/C++ rewrite files or `cBody` fields in `Dev\schemas\d4_rewrites\*.json` / `.decomp` archives, or ROM/generated artifacts.

### 3.2 `OfflineToolchainLocator` (`ReEngineInstallProfile`)
- **`ReEngineInstallProfile`:** `Lightweight` (`0`) vs `Full` (`1`), `MaxToolchainBudgetMb = 160`, `ThirdPartyLicensesFileName = "THIRDPARTY_LICENSES.txt"`.
- **`OfflineToolchainLocator.ResolveToolchainDirectory`:** Prefers explicit override, then peer `..\toolchain\`, then portable `.\toolchain\`, rejecting unstripped `ghidra\GPL\`.

### 3.3 `IAotCacheManager` & `AotCacheManager`
- **`AotCacheEntry` JSON Keys:** `key`, `cacheDirectory`, `generatedAssemblyPath`, `createdAtUtc`, `lastAccessedAtUtc`, `fileSizeBytes`, `sha256Hex`, `isValid`.
- **Integrity:** `<dataDir>\cache\aot-index.json` + `.aot-index.lock` + `.bak` rotation + `ValidateCleanRoomRecompPackage`.

---

## 4. Settings, Updates, Associations, Bootstrap SDK & Installer

### 4.1 Settings Overlay & Migration (`data\config\settings.json`)
- **`ReEngineSettings` (`UpdateModels.cs`):** `[JsonExtensionData]` preserves forward-compatible keys; writes canonical keys (`schemaVersion`, `appId`, `updatedAtUtc`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `resolutionScale`, `backendId`, `pinnedEngineVersions`, `stagedUpdatePath`, `windowWidth`, `windowHeight`), while reading legacy `preferredResolutionScale` read-only for migration (`JsonIgnore(WhenWritingNull)`).
- **`ProgramBootstrapHelper` & `SettingsMigrationHelper`:** Defaults non-isolated installs to `<InstallRoot>\data\config\settings.json` and `<InstallRoot>\logs\`; merges legacy `data\settings.json` and `app\data\settings.json` newest-wins, backs up to `data\config_history\legacy_*.json`, and archives `.migrated.bak`.

### 4.2 Resumable Patch Staging & Side-by-Side Track Resolution (`UpdatePackageApplier` & `UpdateCoordinator`)
- **`UpdatePackageApplier`:**
  - `AssertSafeRemoteUrl`: blocks non-HTTP(S), private RFC1918/ULA, link-local, metadata hostnames, and unapproved loopback targets.
  - `DownloadResumablePartAsync`: HTTP `Range` resume against `.part` and `.part.meta.json` (`UpdatePartMeta`: `engineId`, `version`, `downloadUrl`, `sha256Hex`, `expectedSizeBytes`, `fileSizeBytes`, `updatedAtUtc`), then verifies SHA-256.
  - `StageAndPromoteArchive`: Zip-Slip-checked extraction to `engines\<engineId>\.staging-<ver>\` -> atomic move to `v<Ver>\` -> writes `.ready` (`{ engineId, version, completedAtUtc }`).
- **`UpdateCoordinator`:** Uses a `2s` feed poll timeout (`ManifestPollTimeout = 2s`) across deduplicated `(EngineId, Track)` pairs, preserving breaking major/minor tracks.
- **`EngineVersionResolver`:** Requires `.ready` on `>= 0.3.0` engines, skips `.staging-*`, resolves bidirectional `EngineAliasCatalog` equivalents, and honors `pinnedEngineVersions`.

### 4.3 File Associations, App Manifest & Installer (`installer.manifest.json`)
- **Associations (`IFileAssociationStore`):** `.recomp` (`reEngine.RecompModule` -> `--open "%1"`) and `.decomp` (`reEngine.DecompModule` -> `decomp --module "%1"`), written to `HKCU\Software\Classes` or `<dataDir>\virtual_registry.json` (`APP_ISOLATED_TEST=1`).
- **`Dev\app.manifest.json`:** `schemaVersion = 2`, `appId = "reengine"`, `version = "0.3.0"`, `activeMilestone = "0.3.0"`, `goalMajorVersion = "1.0.0"`, `sdkVersion = "0.5.0"`.
- **`Dev\Installer\installer.manifest.json`:** Embedded resource inside `reEngine.Installer` carrying `studioName = "StreamItAll"`, `suiteName = "reEngine"`, `umbrellaSubpath = "StreamItAll\\reEngine\\reEngine"`, `allowAutoStart = false`, `layoutMode = "single-line"`, and `--suite-dir` secondary picker.
- **`ReEnginePackagingConstants` (`SharedLibs\reEngine\v0.3.0\`):** `Version = "0.3.0"`, `AllowAutoStart = false`, `BrandAccentHex = "#38BDF8"`.

---

## 5. CLI Contract, Child Isolation & Exit Code Matrix

### 5.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>]`, `--open <path>` (or positional `*.recomp`/`*.recomp.json`), `--wait`, `--register-assoc`.
- **Update & Directory Flags:** `--check-updates`, `--strict`, `--feed <urlOrPath>`, `--engines-dir <dir>`, `--data-dir <dir>`, `--config <path>`, `--log-dir <dir>`, `--save-dir <dir>`.
- **Host, Routing & Verification Flags (`v0.3.0`):** `--host-demo`, `--backend <opengl|vulkan|parallelCpu|software>`, `--scale <1|2|4|8>`, `--require-backend`, `--jit-bringup`, `--preload-media`, `--verify`, `--verify-module <id>`, `--doctor`, `--tray-only`, `--window`, `--browser`, `--suite-tab <tab>`, `--profile <Lightweight|Full>`, `--port <n>`, `--automation-port <n>` *(fail-closed Exit `2` in Release)*, `--automation-token <tok>`, `--dry-run`, `--enable-logs`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **`LauncherArgsHelper`:** Normalizes caller-CWD relative file arguments (`PathOptions`), skips value tokens (`--port`, `--automation-port`, `--automation-token`, `--backend`, `--scale`, `--suite-tab`, `--verify-module`, `--profile`, `--frames`, `--stage`, `--ui`), and sync-waits on CLI/`decomp`/`--host-demo` invocations.

### 5.2 Per-Module Child Isolation (`RecompLaunchPlanBuilder` & `RecompLaunchCoordinator`)
1. Isolates `--data-dir` to `<dataDir>\modules\<moduleId>\`, `--config` to `<moduleDataDir>\settings.json`, and `--log-dir` to `<moduleDataDir>\logs` (strictly containment-checked under `APP_ISOLATED_TEST=1`).
2. Forwards `--module <fullPkgPath>`, optional `--preload-media`, `--scale`, plus `opengl`/`software` and `--jit-bringup` to `>= 0.2.7` engines; reserves `vulkan`/`parallelCpu`, `--require-backend`, and `--automation-port` strictly for `>= 0.3.0` engines.
3. Attaches Windows `JobObjectGuard` only when `--wait` (`ShouldWaitForExit = true`) is active; interactive detached launches leave detached child windows running cleanly after the launcher stub exits `0`.

### 5.3 Propagated Exit Codes (`reEngine.Launcher` <-> `reEngine.App` <-> `reEngine.Host`)

| Exit Code | Trigger / Meaning |
| :---: | :--- |
| `0` | Success, `--version`/`--help`/`--verify`/`--dry-run` pass, interactive child handoff, or non-strict offline `--check-updates`. |
| `1` | General runtime failure, missing compatible `re*` engine (`.recomp`), or child launch failure. |
| `2` | Invalid CLI `ArgumentException`, Zip-Slip/`InvalidDataException`/`JsonException`, containment error, or `--automation-port` in a `Release` build. |
| `3` | `.decomp` unbuilt/missing platform engine (`DecompUnbuiltEngineError` 3-part diagnostic). |
| `4` | `--check-updates --strict` failure when update feed/network is unavailable. |
| `130` | `Console.CancelKeyPress` / `OperationCanceledException` interrupt (terminates synchronous child process tree cleanly). |
| `5, 6, ...` | Propagated verbatim from child `re*` engines during `--open --wait`. |
