# reEngine — Interfaces & Contracts

**Synced Version:** `0.4.0` (`2026-11-19`)  
**Scope:** Package Manifests, `reEngine.Host` GPU/Input/HD-Pack Contracts, Update/Settings JSON Schemas & CLI/Verification Reference  
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
| `version` | `string` | Yes | SemVer (`"0.4.0"`) | Packaged module semantic version. |
| `engineId` | `string` | Yes | Non-empty | Target engine folder or alias (`"reStation"`/`"reSlate"`, `"reNes"`/`"reCartOne"`, `"re64"`/`"reCart64"`, `"reGenesis"`/`"reBlast16"`, `"reCube"`). |
| `engineVersion` | `string` | Yes | Valid `SemVer` | Minimum compatible engine version required (`>=`). |
| `compatibleEngineTrack` | `string` | Yes | Track selector | Primary compatibility track (`X.Y.x`, `X.Y.*`, `X.Y`, `X.x`, `X.*`, `X.Y.Z`). |
| `supportedEngineTracks` | `string[]?` | No | `null` | Ordered list of compatible engine tracks checked before `compatibleEngineTrack`. |
| `backendId` | `string?` | No | `null` | Preferred default render backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`; `dx12` normalized on CLI). |
| `resolutionScale` | `int?` | No | `null` | Preferred integer resolution scale factor (`1..8`). |
| `executionTierMode` | `string?` | No | `null` | Execution mode (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`, `interpreterOracle`). |
| `preloadRomDir` | `string` | No | `"data/rom"` | Relative preload directory inside `ModuleRootDirectory`. |
| `entryExecutable` | `string?` | No | `null` | Optional module-specific executable path relative to root. |

- **Parent-Root & Suite-Tree Discovery (`ResolveModuleRootDirectory` & `SuiteMediaTreeWalker`):** Resolves `<GameRoot>\app\<Module>.recomp` back to `<GameRoot>\`, then searches `<SuiteRoot>\games\*\data\rom\`, `gamedata\`, and `<SuiteRoot>\data\rom\` (ranking local -> SHA-256 -> slug -> unique suite candidate, max depth `5`, skipping reparse points).
- **Preload Media Priority:** `.cue` (`0`) > `.chd` (`1`) > `.iso` (`2`) > `.gcm`/`.rvz`/`.n64`/`.z64`/`.v64`/`.sfc`/`.smc`/`.nes`/`.gb`/`.gbc`/`.gba` (`3`) > `.7z`/`.zip`/`.img` (`4`) > `.bin` (`5`).

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
Optional descriptor inside `engines\<engineId>\v<Ver>\engine.manifest.json` read by `RecompLaunchPlanBuilder.Supports030Routing`:
- Typed fields: `schemaVersion`, `engineId`, `displayName`, `version`, `effectiveVersion`, `engineVersion`, `compatibleEngineTrack`, `supportedEngineTracks`, `platformId`, `entryExecutable`, `decompProviderAssembly`, `executionTierMode`, `backendId`, `resolutionScale`; extra keys (`isFullscreen`, `audioDriver`, `isHdTexturePackEnabled`) pass cleanly via `[JsonExtensionData]`.

### 1.4 Clean-Room Package Naming & Bidirectional `engineId` Aliases (`EngineAliasCatalog`)
- **Clean-Room Package Naming:** Distributable clean-room packages use neutral filenames (`MyGame.recomp`, `SampleModule.decomp`) and lowercase-kebab `moduleId` values (`my-game`).
- **Bidirectional `engineId` Aliasing (`EngineAliasCatalog`):** Exact matches take precedence (`reSlate` <-> `reStation`, `reCartOne` <-> `reNes`, `reCart64` <-> `re64`, `reBlast16` <-> `reGenesis`).

---

## 2. `reEngine.Host` Runtime Contracts (`SharedLibs\reEngine\v0.4.0\reEngine.Host.dll`)

Child platform engines reuse `reEngine.Host.dll` built on `Foundation v0.5.0` (`PixelOverlayCanvas`, `HighResFramePacer`, `AudioRingBuffer`, `RgbaPngWriter`, `WavCaptureWriter`), `ppy.SDL3-CS`, and clean-room OS bindings.

### 2.1 Graphics, HD Packs & Audio Contracts (`reEngine.Host.Contracts`, `reEngine.Host.Rendering`, `reEngine.Host.Core`)
- **`HostRenderBackendKind`:** `OpenGl = 0`, `Vulkan = 1`, `ParallelCpu = 2`, `Software = 3`, `DirectX = 4`, `Metal = 5` (canonical tokens in `HostRenderBackendIds`: `"opengl"`, `"vulkan"`, `"directx"`, `"parallelCpu"`, `"software"`, `"metal"`).
- **`IHostGraphicsContext` & `HostGraphicsContextHandle`:** Exposes native OS graphics/swapchain handles (`BackendKind`, `WindowHandle`, `NativeDeviceHandle`, `NativeQueueOrContextHandle`, `NativeInstanceHandle`, `NativeSurfaceHandle`, `GlGetProcAddress`) without creating conflicting `SDL_CreateRenderer` contexts on raw GL/VK/DX12 windows.
- **`IHdTexturePackResolver` & `HdPackResolver`:** Indexes path-contained `<dataDir>\hdpacks\` PNG texture overrides keyed by `(clutSha256Hex, rectSha256Hex)`.
- **`NativeAudioDriverSelector`:** Pins `SDL_AUDIO_DRIVER` (`wasapi` on Windows, `pipewire`/`alsa` Linux, `coreaudio` Apple, `aaudio` Android, `emscripten` WASM) before `SDL_InitSubSystem(InitAudio)`.
- **`HostSessionRunner.RunAsync(...)`:** Returns `HostSessionResult(ExitCode, FramesRendered, IsDrawerOpen, IsHudVisible, ActiveTab, CapturedFramePath, CapturedAudioPath, Summary, ActiveBackendId, ActiveAudioDriver, FallbackBanner)`; returns `ExitCode = 6` when `--require-backend` rejects an unavailable backend, and `ExitCode = 2` in `Release` builds when `AutomationPort.HasValue`.

### 2.2 Multi-Bind Input, Gamepads, Touch & Overlays (`reEngine.Host.Contracts`, `reEngine.Host.Input`, `reEngine.Host.Ui`)
- **`HostBindingCodes` & `HostMultiBindRegistry`:** 3-slot simultaneous keyboard (`4..511`) + mouse/wheel (`1001..1007`) + gamepad (`2001..2032`) action registry with JSON persistence (`modernActionBindings`, `modernControllerBindings`), repeat filtering, and single-edge sub-tick isolation.
- **`Sdl3GamepadProvider` & `IHostInputProvider`:** Polls Xbox and open-standard HID SDL3 gamepads (`0.15f` deadzone, `0.40f` trigger threshold, hot-plug reconnect, dual-motor `HostRumbleRequest`) plus `HostTouchOverlayView` (portrait/landscape zones & swipe/pinch gestures) into non-`ref` `HostInputFrame`.
- **`F1SettingsDrawerView`:** Renders 5 `HostDrawerTabId` tabs (`Video`, `Controls`, `Keybinds`, `System`, `DiscMedia`): `Video` exposes all 5 Windows backends + `1x/2x/4x` + `Fullscreen` + `HD Packs`; `ControlsTabView` renders Schema A/B/C and dynamic module `ControlOptions`; `KeybindsTabView` renders the interactive 5-column `Action | 1st | 2nd | 3rd | Gamepad` capture matrix.

---

## 3. Decompilation Pipeline, Verification & Settings/Update Contracts (`reEngine.Core`)

### 3.1 `DecompPipelineCoordinator`, `DecompD4StrictnessValidator` & `PackageVerificationService`
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:** Resolves engine/aliases, falls back through `SuiteMediaTreeWalker` to inject `discPath`/`exePath` into `PlatformArguments`, executes `IDecompPlatformProvider`, and runs clean-room output checks.
- **`DecompD4StrictnessValidator`:** Enforces canonical 5-tier `CanonicalCorpusTiers` (`D1_Raw`, `D2_Structured`, `D3_Mapped`, `D4_CleanSource`, `D5_ByteExact`) and maps `LegacyCorpusTierAliases["D4_FunctionalRewrite"] -> "D4_CleanSource"` without double counting.
- **`PackageVerificationService.Verify(...)`:** Validates `.recomp` / `.decomp` archives/manifests and emits Standard 18 camelCase `PackageVerificationReport` (`isPassed`, `generatedAtUtc`, `durationMs`, `exitCode`, `packageKind`, `packagePath`, `moduleId`, `engineId`, `resolvedEngine`, `hasMediaReady`, `primaryMediaPath`, `warnings`, `violations`); `hasMediaReady: false` is non-fatal (`ExitCode = 0`) unless `--require-media` is specified (`ExitCode = 1` / `3`).

### 3.2 `ReEngineSettings` (`data\config\settings.json`) & `reEngine.Installer`
- **Canonical Keys (`UpdateModels.cs`):** `schemaVersion`, `appId`, `updatedAtUtc`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `resolutionScale`, `backendId`, `isFullscreen`, `audioDriver`, `isHdTexturePackEnabled`, `shutdownTimeoutMs` (`3000` default), `pinnedEngineVersions`, `stagedUpdatePath`, `windowWidth`, `windowHeight` (`[JsonExtensionData]` preserves unknown keys; `LegacyMigrationHelper` writes `.bak` before updating).

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--media-path <path>] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>] [--headless]`, `--open <path>` (or positional `*.recomp`/`*.decomp`), `--wait`, `--fullscreen`, `--save-dir <dir>`, `--shutdown-timeout-ms <100..30000>`, `--register-assoc`.
- **Verification, Host & Audio Flags (`v0.4.0`):** `--verify-module <pkgOrEngineId> [--json] [--require-media]`, `--host-demo`, `--backend <opengl|vulkan|directx|dx12|parallelCpu|software>`, `--scale <1|2|4|8>`, `--require-backend`, `--audio-driver <wasapi|pipewire|alsa|coreaudio|...>`, `--hd-packs`, `--jit-bringup`, `--preload-media`, `--verify`, `--doctor`, `--tray-only`, `--window`, `--automation-port <n>` *(fail-closed Exit `2` in Release)*, `--dry-run`, `--enable-logs`, `--frames <n>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
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
