# reEngine — Interfaces & Contracts

**Synced Version:** `0.5.0` (`2026-11-20`)  
**Scope:** Package Manifests, `reEngine.Host` Viewport/GPU/4-Port Input/Automation Bridge Contracts, Update/Settings JSON Schemas & CLI/Verification Reference  
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
| `version` | `string` | Yes | SemVer (`"0.5.0"`) | Packaged module semantic version. |
| `engineId` | `string` | Yes | Non-empty | Target engine folder or alias (`"reSlate"`/`"reStation"`, `"reCart"`/`"reCartOne"`/`"reNes"`, `"reCart64"`/`"re64"`, `"reBlast16"`/`"reGenesis"`, `"reCube"`). |
| `engineVersion` | `string` | Yes | Valid `SemVer` | Minimum compatible engine version required (`>=`). |
| `compatibleEngineTrack` | `string` | Yes | Track selector | Primary compatibility track (`X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, `X.Y.Z`). |
| `supportedEngineTracks` | `string[]?` | No | `null` | Ordered list of compatible engine tracks checked before `compatibleEngineTrack`. |
| `backendId` | `string?` | No | `null` | Preferred default render backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`; `dx12` normalized on CLI). |
| `resolutionScale` | `int?` | No | `null` | Preferred integer resolution scale factor (`1..8`). |
| `executionTierMode` | `string?` | No | `null` | Execution mode (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`, `interpreterOracle`). |
| `preloadRomDir` | `string` | No | `"data/rom"` | Relative preload directory inside `ModuleRootDirectory`. |
| `entryExecutable` | `string?` | No | `null` | Optional module-specific executable path relative to root. |

- **Parent-Root & Suite-Tree Discovery (`ResolveModuleRootDirectory` & `SuiteMediaTreeWalker`):** Resolves `<GameRoot>\app\<Module>.recomp` back to `<GameRoot>\`, then searches `<SuiteRoot>\games\*\data\rom\`, `gamedata\`, and `<SuiteRoot>\data\rom\` (ranking local -> SHA-256 -> slug -> unique suite candidate, max depth `5`, skipping reparse points).
- **Preload Media Priority:** `.cue` (`0`) > `.chd` (`1`) > `.iso` (`2`) > `.gcm`/`.rvz`/`.n64`/`.z64`/`.v64`/`.sfc`/`.smc`/`.nes`/`.fds`/`.gb`/`.gbc`/`.gba` (`3`) > `.7z`/`.zip`/`.img` (`4`) > `.bin` (`5`).

### 1.2 `.decomp` Package Manifest (`module.decomp.json` / `*.decomp` — `DecompPackageManifest`)
Parsed by `DecompPipelineCoordinator.ParseManifest(string packagePath)` and implements `Foundation.Retro.IDecompPackageManifest`:

| Field (`camelCase`) | Type | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `moduleId` | `string` | Filename fallback | Canonical lowercase-kebab-case module identifier. |
| `title` | `string` | `""` | Human-facing module display title. |
| `platformId` | `string` | `""` | Target console identifier (`"psx"`, `"nes"`, `"n64"`, `"genesis"`). |
| `engineId` | `string` | Required | Target platform engine (`"reSlate"`/`"reStation"`, `"reCart"`/`"reCartOne"`/`"reNes"`, `"reCart64"`/`"re64"`, `"reBlast16"`/`"reGenesis"`). |
| `engineVersion` | `string` | `"0.1.0"` | Minimum compatible engine SemVer (`>=`). |
| `compatibleEngineTrack` | `string` | `""` | Primary engine track selector (`"0.2.x"`, `"0.3.x"`). |
| `supportedEngineTracks` | `string[]?` | `null` | Ordered compatible track constraints checked before `compatibleEngineTrack`. |
| `decompProviderAssembly` | `string?` | `null` | Explicit contained relative DLL path overriding default `<engineId>.Decomp.dll` probing. |
| `preservedTiers` | `string[]?` | `null` | Preserved research tiers (`D1_Raw`, `D2_Structured`, `D3_Mapped`, canonical `D4_CleanSource`, `D5_ByteExact`). |
| `d4ParityTier` | `string?` | `null` | Standard 18 D4 parity level (`none`, `D4a`, `D4b`). |
| `currentPortTier` | `string?` | `null` | Separate zero-emulation port axis tier (`none`, `P0`–`P5`). |
| `expectedSha256` | `string?` | `null` | Optional expected 64-char hex SHA-256 of user media for suite-tree disambiguation. |
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
Descriptor inside `engines\<engineId>\v<Ver>\engine.manifest.json` read by `EngineVersionResolver.IsReadyInstallation` and `RecompLaunchPlanBuilder.Supports030Routing`:
- Typed fields: `schemaVersion`, `engineId`, `legacyEngineIds`, `displayName`, `version`, `effectiveVersion`, `engineVersion`, `hostProtocolVersion`, `minSuiteVersion`, `compatibleEngineTrack`, `supportedEngineTracks`, `platformId`, `entryExecutable`, `decompProviderAssembly`, `executionTierMode`, `backendId`, `resolutionScale`; extra keys pass cleanly via `[JsonExtensionData]`.

### 1.4 Clean-Room Package Naming & 3-Way `engineId` Aliases (`EngineAliasCatalog`)
- **Clean-Room Package Naming:** Distributable clean-room packages use neutral filenames (`MyGame.recomp`, `SampleModule.decomp`) and lowercase-kebab `moduleId` values (`my-game`).
- **Equivalence Groups (`EngineAliasCatalog`):** Exact matches take precedence (`reSlate` <-> `reStation`, 3-way `reCart` <-> `reCartOne` <-> `reNes`, `reCart64` <-> `re64`, `reBlast16` <-> `reGenesis`).

---

## 2. `reEngine.Host` Runtime Contracts (`SharedLibs\reEngine\v0.5.0\reEngine.Host.dll`)

Child platform engines reuse `reEngine.Host.dll` built on `Foundation v0.5.2` (`PixelOverlayCanvas`, `HighResFramePacer`, `AudioRingBuffer`, `RgbaPngWriter`, `WavCaptureWriter`), `ppy.SDL3-CS`, and clean-room OS bindings.

### 2.1 Viewport Geometry, Graphics, HD Packs, Audio & Automation Bridge (`reEngine.Host.Contracts`, `Rendering`, `Core`)
- **`HostAspectRatioMode`, `HostOverscanCrop`, `HostViewportRect` & `HostViewportGeometry`:** Exposes `nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, and `horPlus16By9`, source edge trimming (`overscanCropPx`), `Surface0` margin clearing across all 5 presenters (`OpenGl` `0x0`/`0x8CD5` guarded, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`), and `TryMapWindowToContentNormalized` inverse pointer coordinates on `HostInputFrame`.
- **`IHostGraphicsContext` Surface Sync & Readback:** Exposes optional `OwnerThreadId`, owner-thread-guarded `TryReadSurfaceRect(x, y, width, height, rgbaDestination)` (no Y-flip inversion), and `FlushAndSyncForSnapshot()` invoked before QuickSave (`F5`) and live presenter teardown (`Sdl3HostWindow.CloseWindowResources`).
- **`IHostAutomationBridge` & `IHostSessionEventCallbacks`:** Decouples automation from `SharedLibs\reEngine\v0.5.0\reEngine.Host.dll` via `HostSessionOptions.AutomationBridgeFactory` (returning `ExitCode = 2` when `AutomationPort.HasValue` without an active Debug/Test bridge) and pushes live `F1`/`F5`/`F9`/`F11`/`FastForward`/`CutsceneSkip`/`ExtraRow` events back to child engines.
- **`HostRenderBackendKind`:** `OpenGl = 0`, `Vulkan = 1`, `ParallelCpu = 2`, `Software = 3`, `DirectX = 4`, `Metal = 5`.
- **`SemVer` Precedence (`EngineVersionResolver`):** Ecosystem ordering ranks `-hf.<n>` hotfix releases (`hf.10 > hf.3 > hf.2 > hf.1`) strictly above stable empty suffixes and other prereleases (`-alpha`, `-beta`, `-rc`, `-nightly`), while keeping `.ready` enforcement on `>= 0.3.0`.

### 2.2 4-Port Gamepads, 3-Tier Input (`settings.controls.json`), Menu Isolation & Overlays (`Input`, `Ui`)
- **`HostControlTierStore` & `HostMultiBindRegistry`:** Persists Standard 33 all-3-tier bindings (`globalController`, `perGameController`, `perGameModern`) in `settings.controls.json` with duplicate/hidden-slot validation, 3-slot KBM + mouse/wheel (`1001..1007`) + gamepad (`2001..2032`) bindings, and Menu D-Pad + Left-Stick isolation.
- **`Sdl3GamepadProvider.PollAllPorts()`:** Polls up to `4` hot-pluggable SDL3 controllers into `HostInputFrame.PadPorts[4]`.
- **`F1SettingsDrawerView` & `F3HudOverlayView`:** Interactive aspect ratio cycle, overscan crop, fullscreen, fast-forward, cutscene skip, 3-tier Keybinds tabs, interactive `ExtraRows`, and `F3` HUD telemetry pills.

---

## 3. Decompilation Pipeline, Verification & Settings/Update Contracts (`reEngine.Core`)

### 3.1 `DecompPipelineCoordinator`, `DecompD4StrictnessValidator` & `PackageVerificationService`
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:** Resolves engine/aliases, falls back through `SuiteMediaTreeWalker` to inject `discPath`/`exePath` into `PlatformArguments`, executes `IDecompPlatformProvider`, and runs clean-room output checks.
- **`DecompD4StrictnessValidator`:** Enforces canonical 5-tier `CanonicalCorpusTiers` (`D1_Raw`, `D2_Structured`, `D3_Mapped`, `D4_CleanSource`, `D5_ByteExact`) and maps `LegacyCorpusTierAliases["D4_FunctionalRewrite"] -> "D4_CleanSource"` without double counting.
- **`PackageVerificationService.Verify(...)`:** Validates `.recomp` / `.decomp` archives/manifests and emits Standard 18 camelCase `PackageVerificationReport` (`isPassed`, `generatedAtUtc`, `durationMs`, `exitCode`, `packageKind`, `packagePath`, `moduleId`, `engineId`, `resolvedEngine`, `hasMediaReady`, `primaryMediaPath`, `warnings`, `violations`); `hasMediaReady: false` is non-fatal (`ExitCode = 0`) unless `--require-media` is specified (`ExitCode = 1` / `3`).

### 3.2 `ReEngineSettings` (`data\config\settings.json`) & `reEngine.Installer`
- **Canonical Keys (`UpdateModels.cs`):** `schemaVersion`, `appId`, `updatedAtUtc`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `resolutionScale`, `backendId`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isFullscreen`, `audioDriver`, `isHdTexturePackEnabled`, `shutdownTimeoutMs` (`3000` default), `pinnedEngineVersions`, `stagedUpdatePath`, `windowWidth`, `windowHeight` (`[JsonExtensionData]` preserves unknown keys; `LegacyMigrationHelper` writes `.bak` before updating).

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--media-path <path>] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>] [--headless]`, `--open <path>` (or positional `*.recomp`/`*.decomp`), `--wait`, `--fullscreen`, `--save-dir <dir>`, `--shutdown-timeout-ms <100..30000>`, `--register-assoc`.
- **Verification, Host & Audio Flags (`v0.5.0`):** `--verify-module <pkgOrEngineId> [--json] [--require-media]`, `--host-demo`, `--backend <opengl|vulkan|directx|dx12|parallelCpu|software>`, `--aspect-mode <nativePar|fourByThree|squareInteger|nativeFit|horPlus16By9>`, `--scale <1|2|4|8>`, `--require-backend`, `--audio-driver <wasapi|pipewire|alsa|coreaudio|...>`, `--hd-packs`, `--jit-bringup`, `--preload-media`, `--verify`, `--doctor`, `--tray-only`, `--window`, `--automation-port <n>` *(fail-closed Exit `2` in Release on both Player and Setup)*, `--dry-run`, `--enable-logs`, `--frames <n>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **`LauncherArgsHelper`:** Synchronously awaits `--verify-module`, `--headless`, `--json`, `--open`, `--wait`, `decomp`, `--host-demo`, and CLI flags; normalizes caller-CWD relative paths (`--verify-module` normalized only when path-like, leaving bare engine slugs verbatim).

### 4.2 Propagated Exit Codes (`reEngine.Launcher` <-> `reEngine.App` <-> `reEngine.Host`)

| Exit Code | Trigger / Meaning |
| :---: | :--- |
| `0` | Success, `--version`/`--help`/`--verify`/`--dry-run` pass, valid `--verify-module`, interactive child handoff, or non-strict offline `--check-updates`. |
| `1` | General runtime failure, missing compatible `re*` engine (`.recomp`), or `--verify-module --require-media` failure on `.recomp`. |
| `2` | Invalid CLI `ArgumentException`, Zip-Slip/`InvalidDataException`/`JsonException`, containment violation, or `--automation-port` in a `Release` build. |
| `3` | `.decomp` unbuilt/missing platform engine (`DecompUnbuiltEngineError`), or `--verify-module --require-media` failure on `.decomp`. |
| `4` | `--check-updates --strict` failure when update feed/network is unavailable. |
| `6` | `BackendUnavailableException` (`--require-backend` failed: unavailable/unsupported render backend such as `metal` on Windows or pre-0.3.0 engine). |
| `130` | `Console.CancelKeyPress` / `OperationCanceledException` (triggers bounded `<= 3s` graceful `WM_CLOSE` tree shutdown during `--wait`). |
