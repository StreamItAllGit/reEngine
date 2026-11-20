# reEngine — User Guide

**Synced Version:** `0.6.0` (`2026-11-20`)  
**Scope:** Installation, Suite Layout, Execution Resilience & Safe Mode, Split Settings, Display/VRR/`21:9`/8 Filters/`F12` Capture, Audio DSP & Sound Test, 4-Port 3-Tier Input, `.decomp` Bring-Up & Exit Codes

> **Beta (Pre-1.0):** `reEngine` is currently a pre-1.0 beta release working toward `v1.0.0` desktop stability and `v2.0.0` cross-platform compatibility. Official releases publish on GitHub Releases (`StreamItAllGit/reEngine`) and our Discord devlog channel.

---

## 1. Overview & Suite Installation

`reEngine` is a lightweight, zero-daemon suite router, unified 5-backend SDL3 host runtime (`reEngine.Host.dll`), `.decomp` Bring-Up runner, and engine version manager for static recompilation modules. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries.

1. **Standalone Setup:** Run `reEngine-Setup-v0.6.0-win-x64.exe` interactively to choose your profile and folders (choosing a parent folder such as `Misc` automatically sorts `reEngine` into `Misc\reEngine\` with companion `Misc\reEngine\engines\` created automatically), or pass CLI flags (`--silent --no-launch --profile Lightweight|Full`) for automated installs. Interactive and silent installs both use the unified `"suiteId": "reEngine"` suite layout.
2. **Suite Folder Layout (`StreamItAll\reEngine\`):** Installs by default into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` with side-by-side folders:
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` — router executables, `settings.example.json`, `data\config\` split settings (`settings.json`, `settings.video.json`, `settings.audio.json`, `settings.controls.json`), and license files (`--reengine-dir`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\` — optional offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` installed under `--profile Full`.
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\` — side-by-side platform engines (`reStation\v0.3.1\`, `reCart\v0.1.0\`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\` — slim game packages and preserved `data\rom\` user media folders.
3. **Install Profiles & Directory Flags:**
   - **Lightweight (`--profile Lightweight`, default):** Installs the compact router and engine manager without bundled decompilation tools.
   - **Full (`--profile Full`):** Includes the offline Apache-2.0 CLI Ghidra + OpenJDK toolchain (`toolchain\`) with zero internet calls required during decompilation.
   - **Path Overrides:** Override the suite root with `--suite-dir <path>`, override the router folder with `--reengine-dir <path>` or `REENGINE_HOME`, or opt into one-time migration from older installs via the unchecked **Migrate legacy installation** checkbox (`--migrate-legacy`).
4. **Upgrade Preservation (`v0.6.0+`):** Upgrading an existing installation preserves all four split domain config files (`settings.json`, `settings.video.json`, `settings.audio.json`, `settings.controls.json`) and keeps existing `schemaVersion >= 1` values (`v2`/`v3+`) intact without touching existing side-by-side engines or personal media in `data\rom\`.

---

## 2. Launching `.recomp`, `.decomp`, Verification & CLI Options

1. **Register Double-Click Associations:** Click **Register Associations** in the manager **Engines** tab (or run `reEngine.exe --register-assoc`) to bind both `.recomp` (`reEngine.RecompModule`) and `.decomp` (`reEngine.DecompModule`) files under per-user Windows Registry (`HKCU\Software\Classes`).
   - Double-clicking a `.recomp` file validates the manifest, resolves compatible engines or 3-way aliases (`reCart` / `reCartOne` / `reNes`, `reSlate` / `reStation`), discovers personal media, and launches the compatible static recompilation engine.
   - Double-clicking a `.decomp` file opens the dark-slate manager directly on the **Bring-Up** shelf pre-loaded with the recipe, `D0..D5` stage selector, and suite-tree media status.
2. **Launch, Verify, Decompile, or Run Diagnostics from Terminal:**
   ```powershell
   reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend directx --display-mode borderlessFullscreen --aspect-mode fourByThree --video-filter crtLite --scale 4 --save-dir "D:\Games\MyGame\saves" --shutdown-timeout-ms 3000 --wait
   reEngine.exe --verify-module "D:\Games\MyGame\app\MyGame.recomp" --json
   reEngine.exe --verify-module "D:\Games\Samples\SampleModule.decomp" --require-media
   reEngine.exe decomp --module "D:\Games\Samples\SampleModule.decomp" --stage D4 --headless
   reEngine.exe --host-demo --backend opengl --require-backend --aspect-mode horPlus21By9 --video-filter crtLite --audio-driver wasapi --window
   reEngine.exe --verify
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
3. **Interactive Host Controls, Hotkeys & `F1` 6-Tab Drawer (`reEngine.Host.dll`):**
   - `F1`: Toggle the interactive 6-tab settings drawer (`Video`, `Audio`, `Ctrl`, `Keys`, `Sys`, `Media`):
     - **Video:** Switch live across all 5 Windows backends (`OpenGl`, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`), scale (`1x`, `2x`, `4x`), **Aspect** ratio mode (`Native PAR`, `4:3`, `Square Int`, `Native Fit`, `16:9 Hor+`, `21:9 Hor+` — auto-centering `4:3` FMVs inside ultrawide frames with inverse normalized pointer coordinates), **Overscan Crop** (`0px` / `8px` edge trim toggle), **Fullscreen**, **Fast-Forward**, **HD Packs** (loads PNG overrides from `<DataDir>\hdpacks\`), **Filter** cycling all 8 live video presets (`Nearest`, `Bilinear`, `Sharp Bilinear`, `Scanlines`, `CRT Lite`, `LCD Grid`, `xBRZ`, `NTSC Composite`), **24-Bit** color vs 15-bit RGB555, **Dither** (4x4 Bayer ordered dithering), **VSync**, and **F12: Clean / F12: Filtered** screenshot mode.
     - **Audio (Slot #2):** Switch playback output devices live without restarting (`Output: ...`), adjust **Master** (`0..100%`) and **BGM / SFX / Vox** category volumes, toggle volume-preserving **Mute** (`Mute: ON/OFF`) and smooth 5ms **Mute on Focus Loss**, cycle biquad audio filter presets (**Off / Raw**, **CRT Warm** `4800 Hz LP / 90 Hz HP`, **Handheld** `5500 Hz LP / 260 Hz HP`), adjust **Width** (`0..200%`), toggle **Resample** (`Linear` vs `Sinc Cubic`), cycle **FF Audio** clamping (`Atten LP`, `Muted`, `Normal`), and audition tracks in the **Sound Test Jukebox** (`< Prev`, `Play`/`Stop`, `Next >`).
     - **Controls (`Ctrl`):** Switch Schema `A` / `B` / `C` profiles and toggle dynamic per-game `ControlOptions` pills exposed by the active module.
     - **Keybinds (`Keys`):** Switch across all 3 customizable control tiers (`Global 1:1`, `Game 1:1`, `Modern PC`, persisted atomically in `<DataDir>\config\settings.controls.json` with collision and hidden-slot guards) and bind via the interactive 5-column matrix (`Action | 1st | 2nd | 3rd | Gamepad`), supporting 3 simultaneous keyboard/mouse/wheel bindings plus up to 4 hot-pluggable Xbox and open-standard HID SDL3 gamepads (with per-port rumble). When any overlay or game menu is open, `D-Pad` and `Left Stick` remain dedicated to menu navigation regardless of Free-Roam bindings. **Left-click** any cell (`...`) and press a key, mouse button/wheel, or gamepad input to bind (`Esc` cancels, `Del` or `Backspace` clears); **right-click** any cell to clear that slot immediately; or click **Reset Defaults** / scroll (`^`, `v`).
     - **System (`Sys`):** View and cycle the active execution tier (`AOT Primary` vs `Allow Tier 3 JIT`), toggle **Preload Media to RAM**, toggle **Fast Boot (Skip Splash)**, trigger **Skip Cutscene**, or click **Safe Mode: ACTIVE (Reset)** to clear an active crash-recovery Safe Mode state.
     - **Media (`Media`):** Inspect loaded disc/ROM location (`data\rom\`) and interact with module extra rows.
   - `F3`: Toggle the live HUD showing FPS, frame time, backend, scale, aspect mode, fast-forward indicator, connected gamepad count (up to 4 ports), execution tier, audio ring fill or `MUTED`, and active status badges (`Display Mode`, `Filter`, `SAFE MODE`).
   - **Quick Hotkeys:** `F5` requests a QuickSave (flushing and syncing the host graphics context before saving), `F6` / `Grave` toggles Fast-Forward (`2.0x..4.0x`), `F7` triggers Cutscene Skip, `F9` requests QuickLoad, `F11` / `Alt+Enter` toggles live Fullscreen, `F12` saves a lossless PNG screenshot (`cleanNative` or `filteredComposite`) into `<DataDir>\screenshots\<moduleId>\YYYYMMDD_HHMMSS_<n>.png` (or `--dump-screenshot`), and `ESC` cancels active keybind capture, closes open drawers, or exits cleanly.
4. **Execution Resilience, Safe Mode, Protocol-Gated Routing & Bounded Shutdown:**
   - **Safe Mode Crash Recovery (`booting.sentinel`):** Each launch writes `<DataDir>\booting.sentinel` with the active process PID and clears it automatically once frame `1` renders. If a previous session terminated before its first frame, the next launch automatically enters in-memory Safe Mode (disabling `.re*` packs, HD texture overrides, and falling back Vulkan/DirectX 12 to OpenGL without altering your saved `settings.video.json` or `settings.audio.json` on disk) and advises rebuilding `<DataDir>\cache\` if issues persist. Reset anytime via `--safe-mode-reset`, **Reset Safe Mode** in the Manager UI, or the `F1` **Sys** tab.
   - **Physical RAM Media Preload & Clean-Room Tiers:** Enabling **Preload Media to RAM** (`--preload-media-to-ram`) verifies at least `512 MiB` of free physical RAM headroom remains and the media uses `<= 75%` of available physical RAM before pinning bytes into memory, logging a `[WARN]` and falling back cleanly to disk streaming if RAM is tight. Static AOT (`staticAotOnly`) is the default when JIT is disabled and fails closed on unlifted raw media; `aotWithJitFallback`, `aotWithThreadedIr`, and `jitBringup` allow Tier 3 JIT; differential `interpreterOracle` is strictly quarantined to explicit CLI runs (`--execution-tier interpreterOracle`) and can never persist as a saved default.
   - **Per-Module Isolation & Protocol Gating:** Each `.recomp` module isolates its own settings and logs under `<DataDir>\modules\<moduleId>\`. Engines declaring `hostProtocolVersion >= 0.6.0` (or `minSuiteVersion >= 0.6.0`) receive all `v0.6.0` display, filter, capture, execution, and audio DSP CLI flags; `0.3.0–0.5.x` engines receive compatible `0.3.0+` flags with clean `[WARN]` omission logs for `0.6.0+` overrides. Synchronous `--open <pkg> --wait` launches flush saves cleanly across child process trees via bounded `WM_CLOSE` (default `--shutdown-timeout-ms 3000`) when cancelled.
5. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` | Load `.recomp` / `.recomp.json`, discover media (including `.fds`), resolve compatible engine or 3-way alias, and launch cleanly (detached unless `--wait` is also passed). |
| `decomp --module <path>` | Open a `.decomp` package interactively on the **Bring-Up** shelf, or run headless with `--headless` (`--stage D0..D5`, `--media-path`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--verify-module <target>` | Validate a `.recomp`/`.decomp` package (`--json`, `--require-media`) or inspect installed tracks when passed a bare engine ID (`reStation`, `reSlate`, `reCart`, `reNes`). |
| `--host-demo` | Launch the visible `reEngine.Host` SDL3 test window with 5-backend + 6-aspect presentation, interactive `F1` 6-tab drawer, `F3` HUD, `F5`/`F9`/`F11`/`F12` hotkeys, Sound Test jukebox, and `--preload-media` gate preview. |
| `--backend <id>` / `--scale <n>` | Select render backend (`opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, `software`) and scale (`1`, `2`, `4`, `8`). `--require-backend` fails with Exit `6` if unavailable. |
| `--display-mode <mode>` / `--monitor <n>` | Set windowing mode (`windowed`, `borderlessFullscreen`, `exclusiveFullscreen`) and target monitor index (`>= 0`). |
| `--aspect-mode <mode>` / `--hud-anchor <mode>` | Select aspect preset (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`, `horPlus21By9`) and wide HUD anchor (`safeFourByThree`, `screenEdges`, `custom`). |
| `--target-fps <n>` / `--bg-fps-cap <n>` / `--vsync` (`--no-vsync`) | Set VRR target FPS (`0` = unlocked), unfocused FPS cap (`0` = inherit), and swapchain VSync. |
| `--video-filter <mode>` / `--screenshot-mode <mode>` | Choose live filter (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite`/`crt-aperture`, `lcdGrid`, `xbrz`, `ntscComposite`) and `F12` source (`cleanNative`, `filteredComposite`). |
| `--true-color` (`--no-true-color`) / `--dithering` (`--no-dithering`) | Toggle 24-bit RGBA vs 15-bit RGB555 color quantization and 4x4 Bayer ordered dithering. |
| `--fullscreen` / `--hd-packs` / `--jit-bringup` | Start fullscreen, enable `<DataDir>\hdpacks\` texture overrides, or enable live JIT bring-up fallback when no AOT cache exists. |
| `--execution-tier <mode>` / `--safe-mode-reset` | Override tier (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`, CLI-only `interpreterOracle`) or clear Safe Mode / `booting.sentinel` before boot. |
| `--preload-media-to-ram` (`--no-preload-media-to-ram`) / `--fast-boot` (`--no-fast-boot`) / `--ff-multiplier <2.0..4.0>` | Toggle headroom-checked RAM media preloading, optional splash skip, and fast-forward speed multiplier. |
| `--audio-driver <id>` / `--audio-device <name>` | Pin `SDL_AUDIO_DRIVER` prior to audio init (`wasapi` on Windows) or select an explicit playback endpoint. |
| `--master-volume` / `--bgm-volume` / `--sfx-volume` / `--voice-volume` | Set normalized `0.0..1.0` master and category gains. |
| `--audio-muted` (`--no-audio-muted`) / `--mute-on-focus-loss` (`--no-mute-on-focus-loss`) | Toggle volume-preserving mute or 5ms anti-pop focus-loss muting. |
| `--audio-lowpass-hz` / `--audio-highpass-hz` / `--stereo-width` / `--audio-resampling` / `--ff-audio-mode` | Configure 2nd-order Butterworth cutoffs (`Hz`), mid-side stereo width (`0..200%`), resampling (`linear`, `sincCubic`), and FF audio clamping (`attenuatedLowPass`, `muted`, `normal`). |
| `--wait` / `--save-dir <dir>` / `--shutdown-timeout-ms <ms>` | Wait synchronously, redirect per-game saves, flush child processes via bounded `WM_CLOSE` (`100..30000`, default `3000`), and propagate exact exit codes. |
| `--register-assoc` / `--check-updates` (`--strict`) | Register `.recomp`/`.decomp` Explorer handlers, or sweep installed engine update feeds (`2s` timeout, ranking `-hf.<n>` hotfixes above stable releases; `--strict` returns Exit `4` if offline). |
| `--feed <urlOrPath>` / `--engines-dir <dir>` | Override the update feed URL/path or `engines\` discovery root. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` | Redirect runtime data, canonical `data\config\settings.json` (plus split `video`/`audio`/`controls` files), and diagnostic logs. |
| `--verify` / `--doctor` / `--tray-only` / `--window` / `--browser` | Run non-UI diagnostics, open the Dark Slate manager UI (`--window`, `--suite-tab <tab>`), or launch browser mode (`--browser`, `--port <n>`). |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` / `--dry-run` | Forward diagnostic logging, frame caps, or fixture overrides, or dry-run without spawning child processes. |
| `--dump-screenshot <path>` / `--dump-audio <path>` | Capture PNG frame or WAV PCM output cleanly. |
| `--version` / `--help` | Print the active router version (`0.6.0`) or CLI usage summary. |

6. **Exit Code Reference:**
   - `0`: Success (including `--verify-module` pass with `hasMediaReady: false` when `--require-media` is not set, or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure, no installed `.recomp` engine satisfies version/track/alias requirements, or `--verify-module --require-media` failed on `.recomp`.
   - `2`: Invalid CLI flag, malformed `.recomp` / `.decomp` package, Zip-Slip archive error, containment violation, or `--automation-port` on a `Release` build of either `reEngine.exe` or `reEngine-Setup-v0.6.0-win-x64.exe`.
   - `3`: Unbuilt or missing platform engine for a `.decomp` module (prints `[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`), or `--verify-module --require-media` failed on `.decomp`.
   - `4`: Offline/network error under `--check-updates --strict`.
   - `6`: Requested render backend is unavailable under `--require-backend` (for example, `metal` on Windows or a pre-`0.3.0` engine without `hostProtocolVersion >= 0.3.0`).
   - `130`: Interrupted by `Ctrl+C`; synchronous child processes receive bounded `WM_CLOSE` teardown before exit.

---

## 3. Adding Your Personal Discs & ROMs (`data\rom\` & Suite Discovery)

`reEngine` never bundles copyrighted game discs, ROM images, or console BIOS files. You can keep legally dumped media in a game's `data\rom\` folder or anywhere inside your suite directory tree:

- **Installed Modules & Suite-Tree Discovery:** Place dumps in `<GameInstallRoot>\data\rom\` (for example `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\MyGame\data\rom\`). When a module's local `data\rom\` is empty, `reEngine` automatically searches `<SuiteRoot>` (`games\*\data\rom\`, `gamedata\`, and `<SuiteRoot>\data\rom\`), matching module SHA-256 hashes or slugs.
- **Missing Media Prompt (`Locate Media...`):** On the **Bring-Up** shelf, clicking **Locate Media...** opens `Locate Personal Game Media`: select a discovered candidate or click **Browse File...**. The **Copy selected media (+ .cue tracks) into module `data\rom\`** checkbox is **unchecked by default** (use-in-place) to avoid duplicating large disc images; check it only if you want `reEngine` to copy the selected file (and any companion `.bin` tracks listed inside a `.cue` sheet) into `data\rom\`.
- **Automatic Format Priority:** `reEngine` selects primary media in this order: `.cue` -> `.chd` -> `.iso` -> `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.fds`, `.gb`, `.gbc`, `.gba`, `.gen`, `.smd`, `.sms`, `.gg`, `.nds` -> `.7z`, `.zip`, `.img` -> `.bin`.

---

## 4. Using the Manager UI, AOT Cache & Split Settings

Launch `reEngine.exe` without CLI flags to open the Dark Slate manager window:

- **Header Status Pills:** Displays `ZERO-DAEMON`, `DECOMP: FULL` (offline CLI Ghidra + OpenJDK detected under `toolchain\`) or `DECOMP: ROUTER` (router-only `Lightweight` profile), association status (`REGISTERED`, `VIRTUALIZED`, or `UNREGISTERED`), and update status (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `OFFLINE`, or `FAILED`).
- **Three Navigation Tabs (`Library`, `Engines`, `Bring-Up`) & `Inspector Drawer`:**
  - **Library:** Choose from all 5 Windows render backends (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`), resolution scale (`1x`, `2x`, `4x`), toggle **Fullscreen**, toggle **Allow live JIT bring-up fallback when precompiled AOT cache is not built**, configure quick v0.6.0 runtime options (**Preload Media to RAM**, **Fast Boot**, **SAFE MODE** badge + **Reset Safe Mode**, **Display** mode, **Aspect**, **Filter** across all 8 presets, **VSync**, **24-bit Color**, **Dithering**, **Capture** mode, **Audio** driver, **Mute Audio**, **Mute on Focus Loss**, and **Audio Filter** preset), click **Open .recomp Package...** to launch interactively, or click **Verify Package...** to validate any `.recomp` / `.decomp` package in-place.
  - **Engines:** Browse installed engines/tracks/aliases (`reCart`/`reCartOne`/`reNes`, `reSlate`/`reStation`), toggle launch update checks & auto-staged patches, select install **Profile** (`Lightweight` / `Full`), register `.recomp`/`.decomp` file associations, check for updates (including `-hf.<n>` hotfix releases), or **Pin Selected Track** to lock an engine version for rollback.
  - **Bring-Up:** Inspect offline `.decomp` toolchain readiness and `<DataDir>\cache\` AOT cache usage, click **Open .decomp Recipe...** (or double-click a `.decomp` file in Explorer), choose `D0`–`D5`, click **Locate Media...** to resolve or stage personal disc/ROM media, click **Verify Package...**, or click **Run Stage** with live log output.
  - **Inspector Drawer:** Collapsible side panel showing the active execution mode (`Precompiled AOT` or `Live JIT Fallback`), Safe Mode state, selected backend/scale, Display/Filter/Audio Filter summary, decompilation pipeline status, local `rom\` folder path, and pinned engine tracks (**Clear Pinned Track**).
- **Split-Domain Settings (`<DataDir>\config\`):**
  - `settings.json` (`schemaVersion: 2`, `domain: "core"`): stores `installProfile`, `isJitEnabled`, `executionTierMode`, `shouldPreloadMediaToRam`, `isFastBootEnabled`, `isSafeModeActive`, `shouldFastLoadData`, `fastForwardMultiplier`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `pinnedEngineVersions`, `stagedUpdatePath`, install paths, and decomp tier preferences.
  - `settings.video.json` (`domain: "video"`): stores `backendId`, `resolutionScale`, `displayMode`, `monitorIndex`, `isFullscreen`, `isVsyncEnabled`, `targetFps`, `backgroundFpsCap`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isHdTexturePackEnabled`, `hudAnchorMode`, `videoFilterMode`, `isTrueColor24BitEnabled`, `isDitheringEnabled`, and `screenshotCaptureMode`.
  - `settings.audio.json` (`domain: "audio"`): stores `audioDriver`, `audioOutputDevice`, `masterVolume`, `isAudioMuted`, `bgmVolume`, `sfxVolume`, `voiceVolume`, `shouldMuteOnFocusLoss`, `audioLowPassHz`, `audioHighPassHz`, `audioStereoWidthPercent`, `audioResamplingMode`, and `fastForwardAudioMode`.
  - `settings.controls.json`: persists all 3 controller and keyboard/mouse binding tiers (`globalController`, `perGameController`, `perGameModern`) with duplicate and hidden-slot validation.
  - **Self-Healing Recovery:** Automatic `v1 -> v2` upgrades snapshot legacy settings into `<DataDir>\config_history\`, corrupt JSON files are moved safely into `<DataDir>\quarantine\`, and loads recover automatically from `.bak` or history snapshots.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.6.0] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log`, and unexpected errors write JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Suite Folder Structure
- `<SuiteRoot>\reEngine\reEngine.exe` — lightweight launcher entrypoint (zero background services).
- `<SuiteRoot>\reEngine\app\` — router and `reEngine.Host.dll` binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<SuiteRoot>\reEngine\settings.example.json` & `THIRDPARTY_LICENSES.txt` — core configuration template and third-party notices.
- `<SuiteRoot>\reEngine\scripts\` — optional Windows file association scripts.
- `<SuiteRoot>\toolchain\` — offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` (`Full` installs; portable `reEngine\toolchain\` also supported).
- `<SuiteRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`/`reSlate`, `reCart`/`reCartOne`/`reNes`, etc.; `0.3.0+` and `0.3.0-hf.<n>` builds promote via `.staging-<ver>` and `.ready`).
- `<DataDir>\config\`, `<DataDir>\config_history\`, `<DataDir>\quarantine\`, `<DataDir>\cache\`, `<DataDir>\hdpacks\`, `<DataDir>\screenshots\`, `<DataDir>\modules\<moduleId>\` — split atomic settings, snapshots, quarantined corrupt configs, AOT cache, HD texture overrides, `F12` PNG screenshots, and isolated per-game settings and logs.

### 5.2 Common Remedies
- **Safe Mode Activated After Crash:** `booting.sentinel` remained from a previous launch that crashed before rendering frame `1`. Safe Mode disables `.re*` packs, HD textures, and switches Vulkan/DirectX 12 to OpenGL in memory without changing your saved files. Click **Reset Safe Mode** in the **Library** tab, reset it inside the `F1` **Sys** tab, or pass `--safe-mode-reset`; rebuild `<DataDir>\cache\` if crashes repeat.
- **RAM Preload Warning (`Falling back to disk streaming`):** Ensure at least `512 MiB` of free physical RAM remains after loading the media and that the file uses `<= 75%` of available physical memory, or disable **Preload Media to RAM** (`--no-preload-media-to-ram`).
- **No Compatible `.recomp` Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId` (or alias), compatible track, and minimum `engineVersion` is installed under `<SuiteRoot>\engines\` (with `.ready` present on `v0.3.0+` or `-hf.<n>` hotfixes), or pass `--engines-dir <path>`.
- **Backend Unavailable Under `--require-backend` (Exit `6`):** Switch to a supported Windows backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, or `software`), ensure `< 0.3.0` engines declare `hostProtocolVersion >= 0.3.0` if backend routing is expected, or omit `--require-backend` to allow automatic fallback (`software` / `parallelCpu`).
- **Unbuilt `.decomp` Platform Engine (Exit `3`):** Read the 3-part diagnostic (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) to see which engine track or decompiler plugin (`app\<engineId>.Decomp.dll`) is missing, or pass `--engine-dir <path>`.
- **Shows `DECOMP: ROUTER` After Installing Tools:** Ensure CLI Ghidra (`ghidra\support\analyzeHeadless.bat`, with `ghidra\GPL\` stripped) and OpenJDK (`jdk\bin\java.exe`) plus `THIRDPARTY_LICENSES.txt` sit inside `<SuiteRoot>\toolchain\` (or portable `.\toolchain\`).
- **Package Rejected (Exit `2`):** Ensure the `.recomp` / `.decomp` archive contains valid JSON, a valid lowercase `moduleId`, no `..` traversal paths, and (if passing `--automation-port`) is only run on Debug/test builds rather than Release binaries (both router and Setup EXE enforce Release `ExitCode = 2`).
- **Uninstalling `reEngine`:** Run `reEngine-Setup-v0.6.0-win-x64.exe --uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting your game saves or personal media inside game folders.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to verify the active router build (`0.6.0`).
- Run `reEngine.exe --verify` or `reEngine.exe --verify-module "<PackagePath>" --json` to inspect environment status, engines, and package compliance.
- Attach `<LogDir>\reengine.log` and any matching `<LogDir>\crashes\crash_reengine_*.json` bundle when reporting issues.

---

## License & Legal

- **Router, Host & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0**. A verbatim copy of `LICENSE` is included inside `app\LICENSE`, source archives, and SDK distributions.
- **Host Windowing & Audio (`ppy.SDL3-CS` & `SDL3`):** Cross-platform SDL3 windowing, audio streaming, and input bindings use **ppy.SDL3-CS** (`MIT License`) and **Simple DirectMedia Layer 3 (SDL3)** (`zlib License`). Full notices ship in `THIRDPARTY_LICENSES.txt`.
- **Offline Toolchain Licenses (`THIRDPARTY_LICENSES.txt`):** Optional `Full` toolchain installs include CLI **Ghidra** (`Apache-2.0`, with `ghidra\GPL\` and `ghidra\docs\` removed) and a `jlink`-trimmed **OpenJDK** runtime (**GPL-2.0 with Classpath Exception**).
- **Bundled Archiver Attribution (`7-Zip`):** Build-time archive packaging uses `7z.exe` and `7z.dll`, distributed under the **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause and BSD 2-clause licenses for LZFSE, ZSTD, and XXH64 components).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or hardware manufacturer. All trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must supply their own legally obtained optical discs or cartridge dumps inside `data\rom\` or suite media folders.
