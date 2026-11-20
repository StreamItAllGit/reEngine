# reEngine — Interfaces & Contracts

**Synced Version:** `0.6.0` (`2026-11-20`)  
**Scope:** Package Manifests, `reEngine.Host` Resilience/Display/VRR/Filter/Audio DSP Contracts, Standard 19 Split JSON Schemas & `>= 0.6.0` CLI Reference  
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

## 2. `reEngine.Host` Runtime Contracts (`SharedLibs\reEngine\v0.6.0\reEngine.Host.dll`)

Child platform engines reuse `reEngine.Host.dll` built on `Foundation v0.5.2` (`PixelOverlayCanvas`, `HighResFramePacer`, `AudioRingBuffer`, `RgbaPngWriter`, `WavCaptureWriter`, `GranularSettingsManager`), `ppy.SDL3-CS`, and clean-room OS bindings.

### 2.1 Display Modes, VRR Pacing, 8 Video Filters, `F12` Screenshots & Multi-Backend Readback
- **`HostViewportGeometry` & `HostHudAnchorMode`:** Supports 6 aspect presets (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`, `horPlus21By9`), automatic `4:3` FMV centering (`isFmvActive`) inside ultrawide frames, `ComputeHudAnchorRect` (`safeFourByThree`, `screenEdges`, `custom`), bounded source overscan trimming (`overscanCropPx`), and inverse pointer normalization across all 5 presenters (`OpenGl`, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`).
- **`HostDisplayMode` & `HostFramePacingController`:** Supports `windowed`, `borderlessFullscreen`, and `exclusiveFullscreen` + `MonitorIndex` selection (`Sdl3HostWindow.GetMonitors()`), dynamic VRR target FPS (`targetFps`, `backgroundFpsCap`, clamped `2.0..4.0` fast-forward) resetting `HighResFramePacer` on cadence transitions, and `SDL_SetWindowVSync` / swapchain sync updates.
- **`HostVideoFilterPipeline` (`HostRetroFilterPasses` + `HostEdgeScalerPasses`):** Applies zero-copy fast path (`nearest && 24-bit && !dither`), 24-bit vs 15-bit RGB555 + 4x4 Bayer dithering (`isTrueColor24BitEnabled`, `isDitheringEnabled`), and all 8 live video filters (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite` [with `"crt-aperture"` alias], `lcdGrid`, `xbrz`, `ntscComposite`).
- **`HostScreenshotService` & `IHostGraphicsContext` Readback:** Post-present `F12` capture supporting `cleanNative` vs `filteredComposite` (`RgbaPngWriter` into `<dataDir>\screenshots\<moduleId>\`), owner-thread `TryReadSurfaceRect` across OpenGL/Vulkan/D3D12/ParallelCpu/Software, and `FlushAndSyncForSnapshot()`.
- **`IHostAutomationBridge` & `IHostSessionEventCallbacks`:** Decouples automation from `SharedLibs\reEngine\v0.6.0\reEngine.Host.dll` (`ExitCode = 2` on Release `--automation-port`) and dispatches live Display/Filter/Audio/SoundTest/SafeMode events.

### 2.2 Dedicated Audio DSP, Hot-Switched Output Devices, Sound Test Jukebox & 6-Tab Drawer
- **`HostBiquadFilter` & `HostAudioDspPipeline`:** Stereo 2nd-order Butterworth LP/HP filters (`off`, `crtWarm` [`4800`/`90 Hz`], `handheldSpeaker` [`5500`/`260 Hz`]), master/BGM/SFX/Voice category mix, `0..200%` mid-side stereo width, 5 ms focus-loss mute ramp (`shouldMuteOnFocusLoss`), `linear` vs `sincCubic` (4-tap Catmull-Rom) resampling, fast-forward audio clamping (`attenuatedLowPass`, `muted`, `normal`), and zero-restart hot-switchable SDL3 playback devices (`Sdl3AudioDeviceNative`).
- **`HostSoundTestService` (`IHostSoundTestProvider`), 6th `Audio` Tab & `SystemTabView`:** Houses built-in / engine Sound Test playback inside `AudioTabView` (`HostDrawerTabId.Audio = 5`, drawer pill #2 `Tab_Audio`), pairs with extracted `SystemTabView`, and renders active filter/display/Safe Mode/Audio Mute badges on `F3HudOverlayView`.

### 2.3 Clean-Room Execution Resilience (`reEngine.Core.Execution`)
- **`BootCrashSentinel`, `MediaRamPreloader` & `ExecutionTierPolicy`:** Tracks `<dataDir>\booting.sentinel` PID liveness (triggering in-memory Safe Mode without mutating saved video/audio JSON, cleared on frame `1` or `--safe-mode-reset`), gates RAM preloading at `>= 512 MiB` headroom & `<= 75%` available physical RAM with `[WARN]` streaming fallback, enforces Clean-Room AOT defaults (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`), and restricts `interpreterOracle` strictly to `AllowCliOnly` invocations.

---

## 3. Decompilation Pipeline, Verification & Settings/Update Contracts (`reEngine.Core`)

### 3.1 `DecompPipelineCoordinator`, `DecompD4StrictnessValidator` & `PackageVerificationService`
- **`DecompPipelineCoordinator.DispatchAsync(options, enginesRootDir, ct)`:** Resolves engine/aliases, falls back through `SuiteMediaTreeWalker` to inject `discPath`/`exePath` into `PlatformArguments`, executes `IDecompPlatformProvider`, and runs clean-room output checks.
- **`DecompD4StrictnessValidator`:** Enforces canonical 5-tier `CanonicalCorpusTiers` (`D1_Raw`, `D2_Structured`, `D3_Mapped`, `D4_CleanSource`, `D5_ByteExact`) and maps `LegacyCorpusTierAliases["D4_FunctionalRewrite"] -> "D4_CleanSource"` without double counting.
- **`PackageVerificationService.Verify(...)`:** Validates `.recomp` / `.decomp` archives/manifests and emits Standard 18 camelCase `PackageVerificationReport` (`isPassed`, `generatedAtUtc`, `durationMs`, `exitCode`, `packageKind`, `packagePath`, `moduleId`, `engineId`, `resolvedEngine`, `hasMediaReady`, `primaryMediaPath`, `warnings`, `violations`); `hasMediaReady: false` is non-fatal (`ExitCode = 0`) unless `--require-media` is specified (`ExitCode = 1` / `3`).

### 3.2 Standard 19 Split-Domain Settings (`SettingsDomainStore` & `reEngine.Installer`)
- **`settings.json` (`v2`, `domain: "core"`):** `schemaVersion` (`2`), `appId`, `domain`, `updatedAtUtc`, `executionTierMode`, `isJitEnabled`, `shouldPreloadMediaToRam`, `isFastBootEnabled`, `isSafeModeActive`, `shouldFastLoadData`, `fastForwardMultiplier`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `lastUpdateFeedUrl`, `enginesDirectoryOverride`, `pinnedEngineVersions`, `stagedUpdatePath`, `windowWidth`, `windowHeight`.
- **`settings.video.json` (`domain: "video"`):** `backendId`, `resolutionScale`, `displayMode`, `monitorIndex`, `isFullscreen`, `isVsyncEnabled`, `targetFps`, `backgroundFpsCap`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isHdTexturePackEnabled`, `hudAnchorMode`, `videoFilterMode`, `isTrueColor24BitEnabled`, `isDitheringEnabled`, `screenshotCaptureMode`.
- **`settings.audio.json` (`domain: "audio"`):** `audioDriver`, `audioOutputDevice`, `masterVolume`, `isAudioMuted`, `bgmVolume`, `sfxVolume`, `voiceVolume`, `shouldMuteOnFocusLoss`, `audioLowPassHz`, `audioHighPassHz`, `audioStereoWidthPercent`, `audioResamplingMode`, `fastForwardAudioMode`.
- **`settings.controls.json` (`HostControlTierStore`):** Standard 33 3-tier bindings (`globalController`, `perGameController`, `perGameModern`) with conflict/hidden-slot validation and 4-port `Sdl3GamepadProvider` support.
- **Self-Healing & Upgrade Preservation:** `SettingsDomainStore` snapshots `v1 -> v2` conversions into `data\config_history\`, moves corrupt JSON into `data\quarantine\`, and recovers from `.bak` or snapshots; `LegacyMigrationHelper` preserves all 4 domain files and `Math.Max(existingSchemaVersion, 1)` across upgrades.

---

## 4. CLI Contract, Child Isolation & Exit Code Matrix

### 4.1 CLI Flags & Launcher Argument Rules
- **Subcommands & Package Flags:** `decomp --module <file.decomp> [--stage D0..D5] [--media-path <path>] [--engine-dir <dir>] [--toolchain-dir <dir>] [--output-dir <dir>] [--headless]`, `--open <path>` (or positional `*.recomp`/`*.decomp`), `--wait`, `--fullscreen`, `--save-dir <dir>`, `--shutdown-timeout-ms <100..30000>`, `--register-assoc`.
- **Verification, Host & v0.6.0 Flags (Gated `>= 0.6.0` by `RecompLaunchFlagRouter`):** `--verify-module <pkgOrEngineId> [--json] [--require-media]`, `--host-demo`, `--backend <opengl|vulkan|directx|dx12|parallelCpu|software>`, `--aspect-mode <nativePar|fourByThree|squareInteger|nativeFit|horPlus16By9|horPlus21By9>`, `--scale <1|2|4|8>`, `--require-backend`, `--display-mode <windowed|borderlessFullscreen|exclusiveFullscreen>`, `--monitor <n>`, `--target-fps <n>`, `--bg-fps-cap <n>`, `--vsync | --no-vsync`, `--video-filter <nearest|bilinear|sharpBilinear|scanlines|crtLite|crt-aperture|lcdGrid|xbrz|ntscComposite>`, `--true-color | --no-true-color`, `--dithering | --no-dithering`, `--hud-anchor <safeFourByThree|screenEdges|custom>`, `--screenshot-mode <cleanNative|filteredComposite>`, `--execution-tier <staticAotOnly|aotWithJitFallback|aotWithThreadedIr|jitBringup|interpreterOracle>`, `--jit-bringup`, `--preload-media`, `--preload-media-to-ram | --no-preload-media-to-ram`, `--fast-boot | --no-fast-boot`, `--safe-mode-reset`, `--ff-multiplier <2.0..4.0>`, `--audio-driver <wasapi|pipewire|alsa|coreaudio|...>`, `--audio-device <name>`, `--master-volume <0..1>`, `--bgm-volume <0..1>`, `--sfx-volume <0..1>`, `--voice-volume <0..1>`, `--audio-muted | --no-audio-muted`, `--mute-on-focus-loss | --no-mute-on-focus-loss`, `--audio-lowpass-hz <hz>`, `--audio-highpass-hz <hz>`, `--stereo-width <0..200>`, `--audio-resampling <linear|sincCubic>`, `--ff-audio-mode <attenuatedLowPass|muted|normal>`, `--verify`, `--doctor`, `--tray-only`, `--window`, `--automation-port <n>` *(fail-closed Exit `2` in Release on both Player and Setup)*, `--dry-run`, `--enable-logs`, `--frames <n>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--version` (`-v`), `--help` (`-h`, `/?`, `help`).
- **`LauncherArgsHelper`:** Synchronously awaits `--verify-module`, `--headless`, `--json`, `--open`, `--wait`, `decomp`, `--host-demo`, and CLI flags; enforces 1:1 `ValueOptions` parity with `RecompLaunchOptions`; normalizes caller-CWD relative paths (`--verify-module` normalized only when path-like, leaving bare engine slugs verbatim).

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
