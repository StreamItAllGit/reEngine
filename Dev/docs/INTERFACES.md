# reEngine — Interfaces & Contracts

**Synced Version:** `0.6.1` (`2026-11-20`)  
**Scope:** Package Manifests, `reEngine.Host` R08 Parity / Display / Audio / Action / Nav Contracts, Engine Compatibility & Recovery, Standard 19 Split JSON Schemas & `>= 0.6.1` CLI Reference  
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
| `incompatibleEngineVersions` | `string[]?` | No | `null` | Exact SemVer, wildcard (`0.2.x`, `0.*`), or range (`>=0.5.0 <0.5.2`) exclusions evaluated by `EngineCompatibilityEvaluator`. |
| `defaultFramerateMode` | `string?` | No | `null` | Default framerate cadence requested by the module (`"enhanced"` or `"original"`). |
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

## 2. `reEngine.Host` Runtime Contracts (`SharedLibs\reEngine\v0.6.1\reEngine.Host.dll`)

Child platform engines reuse `reEngine.Host.dll` built on `Foundation v0.5.3` (`PixelOverlayCanvas`, `HighResFramePacer`, `AudioRingBuffer`, `RgbaPngWriter`, `WavCaptureWriter`, `GranularSettingsManager`), `ppy.SDL3-CS`, and clean-room OS bindings.

### 2.1 Display Modes, VRR Pacing, 8 Video Filters, `F12` Screenshots & Multi-Backend Readback
- **`HostViewportGeometry` & `HostHudAnchorMode`:** Supports 6 aspect presets (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`, `horPlus21By9`), automatic `4:3` FMV centering (`isFmvActive`) inside ultrawide frames, `ComputeHudAnchorRect` (`safeFourByThree`, `screenEdges`, `custom`), bounded source overscan trimming (`overscanCropPx`), and inverse pointer normalization across all 5 presenters (`OpenGl`, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`).
- **`HostDisplayMode`, `HostFrameRateMode` & `HostFramePacingController`:** Supports `windowed`, `borderlessFullscreen`, and `exclusiveFullscreen` + `MonitorIndex` selection (`Sdl3HostWindow.GetMonitors()`), `Framerate` (`Original` `[ORIG]` / `Enhanced` `[ENH]` / `[MAX]`), `Target FPS` (`30`, `60`, `120`, `144`, `240`, `0` Max), `backgroundFpsCap`, clamped `2.0..4.0` fast-forward resetting `HighResFramePacer` on cadence transitions, and `Sdl3HostWindow.TrySwitchBackend` live switching.
- **`HostVideoFilterPipeline` (`HostRetroFilterPasses` + `HostEdgeScalerPasses`):** Applies zero-copy fast path (`nearest && 24-bit && !dither`), 24-bit vs 15-bit RGB555 + 4x4 Bayer dithering (`isTrueColor24BitEnabled`, `isDitheringEnabled`), and all 8 live video filters (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite` [with `"crt-aperture"` alias], `lcdGrid`, `xbrz`, `ntscComposite`).
- **`HostScreenshotService` & `IHostGraphicsContext` Readback:** Post-present `F12` capture supporting `cleanNative` vs `filteredComposite` (`RgbaPngWriter` into `<dataDir>\screenshots\<moduleId>\`), owner-thread `TryReadSurfaceRect` across OpenGL/Vulkan/D3D12/ParallelCpu/Software, and `FlushAndSyncForSnapshot()`.
- **`IHostAutomationBridge`, `HostSessionOptions` & `IHostSessionEventCallbacks`:** Decouples automation from `SharedLibs\reEngine\v0.6.1\reEngine.Host.dll` (`ExitCode = 2` on Release `--automation-port`); adds `HostSessionOptions` parity fields (`UiTextScale`, `FrameRateMode`, `ControlMode`, `MouseSensitivity`, `MenuControllerChord`, `IsCleanCaptureModeEnabled`, `ShouldEnableRuntimeLogs`, `OpenSettingsInitially`, `ShowFpsInitially`, `IsDemoCartMode`, `ControlsConfigPath`, `OnBrowseMediaRequested`, `OnBrowseModuleRequested`, `OnFileDropped`, `OnDemoCartRequested`) and default interface callbacks (`OnFrameRateModeChanged`, `OnUiTextScaleChanged`, `OnControlModeChanged`, `OnMouseSensitivityChanged`, `OnMenuControllerChordChanged`, `OnCleanCaptureModeChanged`, `OnRuntimeLoggingChanged`, `OnQuitGameRequested`, `OnChangeMediaRequested`, `OnLoadModuleRequested`, `OnFileDropped`).

### 2.2 Audio DSP, Scaled Overlays, Host Action Bindings, Relative Mouse, Media Gate & Controller Nav
- **`HostBiquadFilter`, `HostAudioDspPipeline` & `HostSoundTestService`:** Stereo 2nd-order Butterworth LP/HP filters (`off`, `crtWarm` [`4800`/`90 Hz`], `handheldSpeaker` [`5500`/`260 Hz`]), master/BGM/SFX/Voice category mix, `0..200%` mid-side stereo width, 5 ms focus-loss mute ramp (`shouldMuteOnFocusLoss`), `linear` vs `sincCubic` resampling, FF audio clamping (`attenuatedLowPass`, `muted`, `normal`), hot-switchable SDL3 playback devices (`Sdl3AudioDeviceNative`), and `IHostSoundTestProvider` inside `AudioTabView`.
- **`HostOverlayTextRenderer`, `F1SettingsDrawerView` & `F3HudOverlayView`:** 2x2 coverage-sampled 5x7 ASCII bitmap text (`BaseScale = 1.45x` * `UiTextScale` `0.75..2.0`); full clear drawer tab labels (`Video`, `Audio`, `Controls`, `Keybinds`, `System`, `Media`) with dynamic width (`430..780`) and per-tab scrolling (`ScrollActiveTab`), `8x` scale, right-click defaults resets, `SystemTabView` (`Quick Save`, `Quick Load`, `Fast-Forward` + `FF Speed: 2x/3x/4x`, `Skip Cutscene`, `Runtime Logging`, `Clean-Capture Mode`, `Quit Game`), and `F3HudOverlayView` (`[ORIG]`/`[ENH]`/`[MAX]` + `[FF <N>x]` badges and Clean-Capture widget hiding).
- **`HostActionContracts`, `HostActionBindingSet`, `HostRelativeMouseController` & `HostSessionParityHelper`:** Defines `HostControlMode` (`ModernWasdMouse` `"modernWasdMouse"`, `ClassicTank` `"classicTank"`), `HostMenuControllerChord` (`L3PlusR3` `"l3+r3"`, `SelectPlusStart` `"select+start"`, `Guide` `"guide"`, `None` `"none"`), and `IHostActionBindingSet` / `HostActionBindingSet` for the 8 `HostActionBindingIds` (`toggleMenu` `F1`, `toggleHud` `F3`, `quickSave` `F5`, `quickLoad` `F9`, `fastForward` `F6`/`Grave`, `cutsceneSkip` `F7`, `toggleFullscreen` `F11`, `takeScreenshot` `F12`) with cross-tier conflict checks against `HostMultiBindRegistry`. Supports unbound-`Tab` Fast-Forward hold, expressive `ControlsTabView` labels (zero `"Schema A/B/C"` strings), deterministic `HostRelativeMouseController` (`SDL_SetWindowRelativeMouseMode`), and 4-step non-quitting `Esc` (`ApplyNonQuittingEscape`: cancel Keybind capture -> close `F1` drawer -> forward `Esc` if bound in `perGameModern` -> release relative mouse capture; explicit exit via `System_QuitGame`).
- **`FirstRunMediaGateView`, `HostNativeFileDialog`, `HostAotCompileOverlayView` & `HostDrawerControllerNav`:** Full-screen Deep Slate Glass Media Gate (`SDL_EVENT_DROP_FILE`, pure Win32 `comdlg32.dll` `GetOpenFileNameW` `OFN_NOCHANGEDIR` pickers with zero WinForms coupling, quiet-idle `Launch Demo / Test Cart`), responsive `HostAotCompileOverlayView` (`"Preparing Native Module..."` via `RunWithResponsivePumpAsync`), and `HostDrawerControllerNav` (`LB`/`RB`, `D-Pad`/Left Stick repeat `18`/`6` frames, `A`/`B`/`X`-`Y`) across `F1` drawer and Media Gate.

### 2.3 Clean-Room Execution Resilience & Crash Recovery (`reEngine.Core.Execution`)
- **`BootCrashSentinel`, `MediaRamPreloader` & `ExecutionTierPolicy`:** Tracks `<dataDir>\booting.sentinel` PID liveness (triggering in-memory Safe Mode without mutating saved video/audio JSON, cleared on frame `1` or `--safe-mode-reset`), gates RAM preloading at `>= 512 MiB` headroom & `<= 75%` available physical RAM with `[WARN]` streaming fallback, enforces Clean-Room AOT defaults (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`), and restricts `interpreterOracle` strictly to CLI invocations.
- **`SessionRecoveryLocator` & Prompt-First Crash Fallback:** `SessionRecoveryLocator.FindClosestRecovery(moduleDataDir, saveDir?, containmentRoot?)` returns `SessionRecoveryCandidate(Found, FilePath, Kind, ModifiedAtUtc, DiagnosticMessage)` prioritizing `checkpoint` (`4`) > `quickResume` (`3`) > `quickSave`/`.state` (`2`) > `saveVaultBackup` (`1`) > `saveFile` (`0`) across `.sav`/`.mcr`/`.srm`/`.json`/`.state` (skipping reparse points & config/logs/cache folders) to feed `RecompLaunchCoordinator`'s interactive fallback prompt (`EngineCrashFallbackPrompt`).

---

## 3. Resolution, Decompilation, Verification & Settings Contracts (`reEngine.Core`)

### 3.1 `EngineCompatibilityEvaluator`, `EngineVersionResolver`, `DecompPipelineCoordinator` & `PackageVerificationService`
- **`EngineCompatibilityEvaluator` & `EngineVersionResolver`:** `EvaluateRule(candidate, rule)` / `IsVersionIncompatible(candidate, rules)` support exact SemVer, wildcards (`0.2.x`, `0.*`), and AND-ed comparator ranges (`>=`, `<=`, `!=`, `==`, `>`, `<`, `=`). `ResolveForManifest(...)` enforces precedence: `pinnedEngineVersions` (`moduleId` -> `engineId` -> `EngineAliasCatalog` aliases: exact `SemVer`, `"bundled"`, `"target"`, `"latest"`) -> latest `.ready`-gated `re*` `>= engineVersion` NOT matching `incompatibleEngineVersions` (`-hf.<n> > stable > prerelease`) -> `TryResolveBundledOrTargetFallback(...)`.
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:** Resolves engine/aliases, falls back through `SuiteMediaTreeWalker` to inject `discPath`/`exePath` into `PlatformArguments`, executes `IDecompPlatformProvider`, and runs clean-room output checks; `DecompD4StrictnessValidator` enforces `D4_CleanSource`.
- **`PackageVerificationService.Verify(...)`:** Validates `.recomp` / `.decomp` archives/manifests and emits Standard 18 camelCase `PackageVerificationReport` (`isPassed`, `generatedAtUtc`, `durationMs`, `exitCode`, `packageKind`, `packagePath`, `moduleId`, `engineId`, `resolvedEngine`, `hasMediaReady`, `primaryMediaPath`, `warnings`, `violations`); `hasMediaReady: false` is non-fatal (`ExitCode = 0`) unless `--require-media` is specified (`ExitCode = 1` / `3`).

### 3.2 Standard 19 Split-Domain Settings (`SettingsDomainStore`, `HostControlTierStore` & `reEngine.Installer`)
- **`settings.json` (`v2`, `domain: "core"`):** `schemaVersion` (`2`), `appId`, `domain`, `updatedAtUtc`, `executionTierMode`, `isJitEnabled`, `shouldPreloadMediaToRam`, `isFastBootEnabled`, `isSafeModeActive`, `shouldFastLoadData`, `shouldEnableRuntimeLogs`, `fastForwardMultiplier`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `pinnedEngineVersions`, `stagedUpdatePath`, `windowWidth`, `windowHeight`.
- **`settings.video.json` (`v2`, `domain: "video"`):** `backendId`, `resolutionScale`, `displayMode`, `monitorIndex`, `isFullscreen`, `isVsyncEnabled`, `frameRateMode`, `targetFps`, `backgroundFpsCap`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isHdTexturePackEnabled`, `hudAnchorMode`, `videoFilterMode`, `isTrueColor24BitEnabled`, `isDitheringEnabled`, `screenshotCaptureMode`, `uiTextScale` (`0.75..2.0`, default `1.0`), `isFpsOverlayEnabled`, `isCleanCaptureModeEnabled`.
- **`settings.audio.json` (`v2`, `domain: "audio"`):** `audioDriver`, `audioOutputDevice`, `masterVolume`, `isAudioMuted`, `bgmVolume`, `sfxVolume`, `voiceVolume`, `shouldMuteOnFocusLoss`, `audioLowPassHz`, `audioHighPassHz`, `audioStereoWidthPercent`, `audioResamplingMode`, `fastForwardAudioMode`.
- **`settings.controls.json` (`HostControlTierStore`):** `schemaVersion`, `appId`, `domain` (`"controls"`), `updatedAtUtc`, `inputProfileMode`, `activeSchema`, `controlMode`, `mouseSensitivity` (`0.25..3.0`), `menuControllerChord`, `hostActionBindings`, and Standard 33 3-tier registries (`globalController`, `perGameController`, `perGameModern`), with `[JsonExtensionData]` preservation, duplicate/hidden-slot validation, and `SerializeDirtyCheckJson()` idle write prevention.
- **Self-Healing & Upgrade Preservation:** `SettingsDomainStore` snapshots `v1 -> v2` conversions into `data\config_history\`, moves corrupt JSON into `data\quarantine\`, and recovers from `.bak` or snapshots; `LegacyMigrationHelper` preserves all 4 domain files and `Math.Max(existingSchemaVersion, 1)` across upgrades.

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--media-path <path>] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>] [--headless]`, `--open <path>` (or positional `*.recomp`/`*.decomp`), `--rom <path>`, `--disc <path>`, `--wait`, `--fullscreen`, `--window`, `--save-dir <dir>`, `--shutdown-timeout-ms <100..30000>`, `--register-assoc`.
- **Verification, Host & `>= 0.6.1` Parity Flags (`RecompLaunchFlagRouter.Supports061Routing`):** `--verify-module <pkgOrEngineId> [--json] [--require-media]`, `--host-demo`, `--demo-cart`, `--backend <opengl|vulkan|directx|dx12|parallelCpu|software>`, `--require-backend [<id>]` *(optional value)*, `--aspect-mode <nativePar|fourByThree|squareInteger|nativeFit|horPlus16By9|horPlus21By9>`, `--scale <1|2|4|8>`, `--display-mode <windowed|borderlessFullscreen|exclusiveFullscreen>`, `--monitor <n>`, `--overscan <px|t,b,l,r>`, `--fps-mode <original|enhanced>`, `--target-fps <n>`, `--bg-fps-cap <n>`, `--vsync | --no-vsync`, `--video-filter <nearest|bilinear|sharpBilinear|scanlines|crtLite|crt-aperture|lcdGrid|xbrz|ntscComposite>`, `--true-color | --no-true-color`, `--dithering | --no-dithering`, `--hud-anchor <safeFourByThree|screenEdges|custom>`, `--screenshot-mode <cleanNative|filteredComposite>`, `--ui-text-scale <0.75..2.0>`, `--open-settings`, `--show-fps`, `--execution-tier <staticAotOnly|aotWithJitFallback|aotWithThreadedIr|jitBringup|interpreterOracle>`, `--jit-bringup`, `--force-zero-rwx`, `--preload-media`, `--preload-media-to-ram | --no-preload-media-to-ram`, `--fast-boot | --no-fast-boot`, `--safe-mode-reset`, `--fast-forward | --no-fast-forward`, `--ff-multiplier <2.0..4.0>`, `--audio-driver <wasapi|pipewire|alsa|coreaudio|...>`, `--audio-device <name>`, `--master-volume <0..1>`, `--bgm-volume <0..1>`, `--sfx-volume <0..1>`, `--voice-volume <0..1>`, `--audio-muted | --no-audio-muted`, `--mute-on-focus-loss | --no-mute-on-focus-loss`, `--audio-lowpass-hz <hz>`, `--audio-highpass-hz <hz>`, `--stereo-width <0..200>`, `--audio-resampling <linear|sincCubic>`, `--ff-audio-mode <attenuatedLowPass|muted|normal>`, `--verify`, `--doctor`, `--tray-only`, `--automation-port <n>` *(fail-closed Exit `2` in Release on both Player and Setup)*, `--dry-run`, `--enable-logs`, `--frames <n>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **`LauncherArgsHelper` & `LauncherJobObjectGuard`:** Synchronously awaits `--verify-module`, `--headless`, `--json`, `--open`, `--wait`, `decomp`, `--host-demo`, `--demo-cart`, `--frames`, `--dump-screenshot`, `--dump-audio`, and CLI diagnostics (`SyncFlags`); enforces 1:1 `ValueOptions` parity with `RecompLaunchOptions` plus optional-value `--require-backend [<id>]`; normalizes caller-CWD relative paths (`--rom`, `--disc`, `--media-path`, selective `--verify-module`); and binds `LauncherJobObjectGuard` (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) strictly when `ShouldWaitForChild == true`.

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
