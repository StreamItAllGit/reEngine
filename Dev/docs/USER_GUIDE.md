# reEngine — User Guide

**Synced Version:** `0.6.2` (`2026-11-23`)  
**Scope:** Installation, Suite Layout, Execution Resilience & Crash Recovery, Split Settings, Scaled Crisp Overlays & 100% Controller Navigation, Rebindable Host Actions, Relative Mouse & Non-Quitting `Esc`, Display/VRR/`21:9`/8 Filters/`F12` Capture, Audio DSP & Sound Test, `.decomp` Bring-Up & Exit Codes

> **Beta (Pre-1.0):** `reEngine` is currently a pre-1.0 beta release working toward `v1.0.0` desktop stability and `v2.0.0` cross-platform compatibility. Official releases publish on GitHub Releases (`StreamItAllGit/reEngine`) and our Discord devlog channel.

---

## 1. Overview & Suite Installation

`reEngine` is a lightweight, zero-daemon suite router, unified 5-backend SDL3 host runtime (`reEngine.Host.dll`), `.decomp` Bring-Up runner, and engine version manager for static recompilation modules. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries.

1. **Standalone Setup:** Run `reEngine-Setup-v0.6.2-win-x64.exe` interactively to choose your profile and folders (choosing a parent folder such as `Misc` automatically sorts `reEngine` into `Misc\reEngine\` with companion `Misc\reEngine\engines\` created automatically), or pass CLI flags (`--silent --no-launch --profile Lightweight|Full`) for automated installs. Interactive and silent installs both use the unified `"suiteId": "reEngine"` suite layout.
2. **Suite Folder Layout (`StreamItAll\reEngine\`):** Installs by default into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` with side-by-side folders:
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` — router executables, `settings.example.json`, `data\config\` split settings (`settings.json`, `settings.video.json`, `settings.audio.json`, `settings.controls.json`), and license files (`--reengine-dir`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\` — optional offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` installed under `--profile Full`.
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\` — side-by-side platform engines (`reStation\v0.3.1\`, `reCart\v0.1.0\`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\` — slim game packages and preserved `data\rom\` user media folders.
3. **Install Profiles & Directory Flags:**
   - **Lightweight (`--profile Lightweight`, default):** Installs the compact router and engine manager without bundled decompilation tools.
   - **Full (`--profile Full`):** Includes the offline Apache-2.0 CLI Ghidra + OpenJDK toolchain (`toolchain\`) with zero internet calls required during decompilation.
   - **Path Overrides:** Override the suite root with `--suite-dir <path>`, override the router folder with `--reengine-dir <path>` or `REENGINE_HOME`, or opt into one-time migration from older installs via the unchecked **Migrate legacy installation** checkbox (`--migrate-legacy`).
4. **Upgrade Preservation (`v0.6.1+`):** Upgrading an existing installation preserves all four split domain config files (`settings.json`, `settings.video.json`, `settings.audio.json`, `settings.controls.json`) and keeps existing `schemaVersion >= 1` values (`v2`/`v3+`) intact without touching existing side-by-side engines or personal media in `data\rom\`.

---

## 2. Launching `.recomp`, `.decomp`, Verification & CLI Options

1. **Register Double-Click Associations:** Click **Register Associations** in the manager **Engines** tab (or run `reEngine.exe --register-assoc`) to bind both `.recomp` (`reEngine.RecompModule`) and `.decomp` (`reEngine.DecompModule`) files under per-user Windows Registry (`HKCU\Software\Classes`).
   - Double-clicking a `.recomp` file validates the manifest, honors per-module or engine pins, skips known incompatible engine releases, resolves compatible engines or 3-way aliases (`reCart` / `reCartOne` / `reNes`, `reSlate` / `reStation`), discovers personal media, and launches the compatible static recompilation engine.
   - Double-clicking a `.decomp` file opens the dark-slate manager directly on the **Bring-Up** shelf pre-loaded with the recipe, `D0..D5` stage selector, and suite-tree media status.
2. **Launch, Verify, Decompile, or Run Diagnostics from Terminal:**
   ```powershell
   reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend directx --display-mode borderlessFullscreen --aspect-mode fourByThree --video-filter crtLite --scale 4 --save-dir "D:\Games\MyGame\saves" --shutdown-timeout-ms 3000 --wait
   reEngine.exe --verify-module "D:\Games\MyGame\app\MyGame.recomp" --json
   reEngine.exe --verify-module "D:\Games\Samples\SampleModule.decomp" --require-media
   reEngine.exe decomp --module "D:\Games\Samples\SampleModule.decomp" --stage D4 --headless
   reEngine.exe --host-demo --backend opengl --require-backend --aspect-mode horPlus21By9 --video-filter crtLite --ui-text-scale 1.25 --show-fps --audio-driver wasapi --window
   reEngine.exe --demo-cart
   reEngine.exe --verify
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
3. **Interactive Host Controls, Scaled `F1` 6-Tab Drawer & 100% Controller Navigation (`reEngine.Host.dll`):**
   - **Scaled Crisp Overlays & 100% Controller Navigation:** All overlays scale crisply with an easy-to-read default size and live **UI Text Scale** (`0.75x–2.0x`), mouse-wheel / button scrolling, and full controller navigation (`LB`/`RB` switch tabs, `D-Pad`/Left Stick navigate rows and scroll, `A` activates, `X`/`Y` alternate/reset, `B` cancels keybind capture or closes the drawer) across both the `F1` drawer and the full-screen Media Gate.
   - `F1` (or customizable **Menu Controller Chord**, default `L3 + R3`): Toggle the interactive 6-tab settings drawer (**Video**, **Audio**, **Controls**, **Keybinds**, **System**, **Media**):
     - **Video:** Switch live across all 5 Windows backends (`OpenGL`, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`) inside the active session, pick scale (`1x`, `2x`, `4x`, `8x`), choose **Framerate** (`Original` / `Enhanced`, right-click resets to `Enhanced`) and **Target FPS** (`30`, `60`, `120`, `144`, `240`, `Max`), cycle **Aspect** (`Native PAR`, `4:3`, `Square Int`, `Native Fit`, `16:9 Hor+`, `21:9 Hor+` — auto-centering `4:3` FMVs; right-click resets), trim **Overscan** (`0px`, `8px`, `16px`; right-click resets to `0px`), toggle **VSync**, **Fullscreen**, **FPS Overlay (F3)**, **HD Packs**, **Fast-Forward**, and **F12: Clean / F12: Filtered**, adjust **UI Text Scale** (`-`/`+` `0.75x..2.0x`, right-click resets to `1.0x`), cycle all 8 live presets (**Filter**), and toggle **24-Bit** / **Dither** (4x4 Bayer).
     - **Audio:** Switch playback output devices live without restarting (`Output: ...`), step **Master Volume** (`-`/`+` `0..100%`), toggle **Mute** (`Mute: ON/OFF`) and 5ms **Focus Mute**, step **BGM / SFX / Vox** gains, cycle biquad presets (**Off / Raw**, **CRT Warm**, **Handheld**), adjust **Width** (`0..200%`), toggle **Resample** (`Linear` vs `Sinc Cubic`), cycle **FF Audio** clamping (`Atten LP`, `Muted`, `Normal`), and audition tracks in the **Sound Test** jukebox (`< Prev`, `Play`/`Stop`, `Next >`).
     - **Controls:** Switch expressive input tiers (**Modern PC (Per-Action)**, **Game 1:1**, **Global 1:1**), toggle **Modern WASD + Mouse** (left-click the unpaused game viewport to capture relative mouse look; opening overlays, losing window focus, pressing `Esc`, or switching to Classic Tank automatically releases capture), adjust **Mouse Sensitivity** (`-`/`+` `0.25x..3.0x`), customize **Chord** (`L3 + R3`, `Select + Start`, `Guide`, `None`), click **Cycle Control Profile** (`2D RPG / Simulation`, `3D Action / Adventure`, `Platformer / Dual-Stick`), and toggle module `ControlOptions`.
     - **Keybinds:** Rebind both **Game Actions** and **`--- HOST ACTIONS ---`** (`Pause / Menu`, `Toggle HUD`, `Quick Save`, `Quick Load`, `Fast Forward`, `Skip Cutscene`, `Fullscreen`, `Screenshot`) inside the 5-column matrix (`Action | 1st | 2nd | 3rd | Gamepad`), with automatic cross-conflict prevention between game bindings and host hotkeys, 4-port Xbox/HID gamepads, **Left-Click** capture (`Esc` cancels, `Del` / `Backspace` or **Right-Click** clears), and **Reset Defaults**.
     - **System:** Trigger **Quick Save (F5)**, **Quick Load (F9)**, **Fast-Forward (F6)** + **FF Speed (`2x` / `3x` / `4x`)**, **Skip Cutscene (F7)**, toggle **Runtime Logging**, toggle **Clean-Capture Mode** (hides `F3` telemetry cleanly for recording/streaming), cycle **Execution Tier** / **Allow Tier 3 JIT**, toggle **Preload Media to RAM**, **Fast Boot (Skip Splash)**, reset **Safe Mode**, or click **`Quit Game`** to exit cleanly.
     - **Media & Full-Screen Glass Media Gate:** Drag-and-drop media or `.recomp` files (`.cue`, `.bin`, `.iso`, `.chd`, `.nes`, `.fds`, `.7z`, `.zip`, `.recomp`) onto the window, click **Change Media...** / **Select Media...** or **Load .recomp...** / **Select .recomp Module...** to pick files via native file dialogs (with warning banners preserved whenever an invalid file is chosen), watch live compilation progress (`"Preparing Native Module..."`) without stalling window responsiveness, or click **Launch Demo / Test Cart** (which rests silently until you press buttons for a soft chime).
   - `F3` (`toggleHud`): Toggle the scaled, jitter-free fixed-width 2-line HUD displaying `[ORIG]` / `[ENH]` / `[MAX]` cadence badge, `[FF <N>x]` speed badge, FPS, frame time, connected gamepad ports (`0..4`), backend, scale, aspect mode, execution tier, audio fill/`MUTED`, display mode, active filter, and `SAFE MODE` — or auto-hide via **Clean-Capture Mode**.
   - **Automatic `F1` Video & Audio Persistence + Reliable Menu Taps:** Changes you make inside the `F1` **Video** and **Audio** tabs automatically save back to `settings.json`, `settings.video.json`, and `settings.audio.json` and restore cleanly across restarts (explicit launch flags override saved defaults, and in-memory Safe Mode keeps saved files untouched). Quick keyboard menu taps stay active long enough for games to register reliably across frame boundaries.
   - **Quick Hotkeys, Unbound-`Tab` Fast-Forward & Non-Quitting `Esc`:** Default hotkeys (`F1` Pause/Menu, `F3` HUD, `F5` QuickSave flushing/syncing the GPU surface first, `F6`/`Grave` Fast-Forward, `F7` Cutscene Skip, `F9` QuickLoad, `F11`/`Alt+Enter` Fullscreen, `F12` PNG screenshot into `<DataDir>\screenshots\<moduleId>\`) are all rebindable in **Keybinds**. Holding **`Tab`** also fast-forwards temporarily whenever `Tab` is not assigned to a game or host action. Pressing **`Esc`** never accidentally quits your game—it strictly follows 4 safe steps: (1) cancel active keybind capture, (2) close the open `F1` drawer and release mouse capture, (3) pass `Esc` to the game if bound in `Modern PC (Per-Action)`, or (4) release relative mouse capture; use **`System -> Quit Game`**, the window close button (`X`), or `Alt+F4` to close the app.
4. **Execution Resilience, Engine Precedence, Crash Fallback & Protocol Gating:**
   - **Safe Mode Crash Recovery (`booting.sentinel`):** Each launch writes `<DataDir>\booting.sentinel` with the active process PID and clears it automatically once frame `1` renders. If a previous session terminated before frame `1`, the next launch automatically enters in-memory Safe Mode (disabling `.re*` packs, HD texture overrides, and falling back Vulkan/DirectX 12 to OpenGL without modifying saved disk configs). Reset anytime via `--safe-mode-reset`, **Reset Safe Mode**, or the `F1` **System** tab.
   - **Engine Precedence & Prompt-First Crash Fallback:** Engine selection checks per-module pins (`pinnedEngineVersions[moduleId]`) -> engine/alias pins -> latest compatible `.ready` engine excluding `incompatibleEngineVersions` rules -> bundled/target fallback. If an engine crashes unexpectedly (`exitCode` outside `0`, `6`, `130`), `reEngine` locates your closest pre-crash checkpoint, quick-resume, quicksave, or save backup and prompts before relaunching on a fallback engine.
   - **Physical RAM Media Preload & Clean-Room Tiers:** `--preload-media-to-ram` checks for `>= 512 MiB` free RAM and `<= 75%` usage of available physical memory before pinning media into RAM (`[WARN]` disk-streaming fallback otherwise). Static AOT (`staticAotOnly`) is default; `--force-zero-rwx` selects zero-RWX `aotWithThreadedIr`; `interpreterOracle` stays CLI-only.
   - **Per-Module Isolation & Protocol Gating:** Each `.recomp` module isolates settings/logs under `<DataDir>\modules\<moduleId>\`. Engines declaring `hostProtocolVersion >= 0.6.1` receive all `v0.6.1` parity flags (`--ui-text-scale`, `--fps-mode`, `--overscan`, `--open-settings`, `--show-fps`, `--force-zero-rwx`, `--fast-forward`, `--demo-cart`), while `0.3.0–0.6.0` engines remain safely isolated.
5. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Load `.recomp` / `.recomp.json`, discover media (including `.fds`), resolve compatible/pinned engine (`incompatibleEngineVersions` + 3-way aliases), and launch cleanly. |
| `--rom <path>` / `--disc <path>` | Override ROM or disc media path (aliases for `--media-path`, normalized relative to caller CWD). |
| `decomp --module <path>` | Open a `.decomp` package interactively on the **Bring-Up** shelf, or run headless with `--headless` (`--stage D0..D5`, `--media-path`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--verify-module <target>` | Validate a `.recomp`/`.decomp` package (`--json`, `--require-media`) or inspect installed tracks when passed a bare engine ID (`reStation`, `reSlate`, `reCart`, `reNes`). |
| `--host-demo` / `--demo-cart` | Launch the standalone `reEngine.Host` SDL3 window with the Glass Media Gate, 5 backends, 6 aspects, scaled `F1` drawer, `F3` HUD, hotkeys, Sound Test, or quiet-idle `--demo-cart` test cartridge. |
| `--backend <id>` / `--scale <n>` | Select render backend (`opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, `software`) and scale (`1`, `2`, `4`, `8`). Optional `--require-backend [<id>]` fails with Exit `6` if unavailable. |
| `--display-mode <mode>` / `--monitor <n>` / `--window` | Set windowing mode (`windowed`, `borderlessFullscreen`, `exclusiveFullscreen`), target display index (`>= 0`), or `--window` forcing windowed mode over saved fullscreen. |
| `--fps-mode <original\|enhanced>` / `--target-fps <n>` / `--bg-fps-cap <n>` / `--vsync` (`--no-vsync`) | Set framerate cadence (`Original` `[ORIG]` vs `Enhanced` `[ENH]`/`[MAX]`), VRR target FPS (`0` = Max), unfocused FPS cap, and swapchain VSync. |
| `--aspect-mode <mode>` / `--overscan <px\|t,b,l,r>` / `--hud-anchor <mode>` | Select aspect preset (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`, `horPlus21By9`), edge overscan crop (`8px`, `16px`, or `t,b,l,r`), and wide HUD anchor (`safeFourByThree`, `screenEdges`, `custom`). |
| `--video-filter <mode>` / `--screenshot-mode <mode>` | Choose live filter (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite`/`crt-aperture`, `lcdGrid`, `xbrz`, `ntscComposite`) and `F12` source (`cleanNative`, `filteredComposite`). |
| `--true-color` (`--no-true-color`) / `--dithering` (`--no-dithering`) | Toggle 24-bit RGBA vs 15-bit RGB555 quantization and 4x4 Bayer ordered dithering. |
| `--ui-text-scale <0.75..2.0>` / `--open-settings` / `--show-fps` | Set overlay text scale (`0.75x–2.0x`), open the `F1` drawer immediately at startup, or enable `F3` HUD telemetry on start. |
| `--fullscreen` / `--hd-packs` / `--jit-bringup` / `--force-zero-rwx` | Start fullscreen, enable `<DataDir>\hdpacks\` texture overrides, enable live JIT bring-up fallback, or force zero-RWX threaded IR (`aotWithThreadedIr`). |
| `--execution-tier <mode>` / `--safe-mode-reset` | Override tier (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`, CLI-only `interpreterOracle`) or clear Safe Mode / `booting.sentinel` before boot. |
| `--preload-media-to-ram` (`--no-preload-media-to-ram`) / `--fast-boot` (`--no-fast-boot`) / `--fast-forward` (`--no-fast-forward`) / `--ff-multiplier <2.0..4.0>` | Toggle headroom-checked RAM media preloading, optional splash skip, initial fast-forward state, and fast-forward speed multiplier. |
| `--audio-driver <id>` / `--audio-device <name>` | Pin `SDL_AUDIO_DRIVER` prior to audio init (`wasapi` on Windows) or select an explicit playback endpoint. |
| `--master-volume` / `--bgm-volume` / `--sfx-volume` / `--voice-volume` | Set normalized `0.0..1.0` master and category gains. |
| `--audio-muted` (`--no-audio-muted`) / `--mute-on-focus-loss` (`--no-mute-on-focus-loss`) | Toggle volume-preserving mute or 5ms anti-pop focus-loss muting. |
| `--audio-lowpass-hz` / `--audio-highpass-hz` / `--stereo-width` / `--audio-resampling` / `--ff-audio-mode` | Configure 2nd-order Butterworth cutoffs (`Hz`), mid-side stereo width (`0..200%`), resampling (`linear`, `sincCubic`), and FF audio clamping (`attenuatedLowPass`, `muted`, `normal`). |
| `--wait` / `--save-dir <dir>` / `--shutdown-timeout-ms <ms>` | Wait synchronously, redirect per-game saves, flush child processes via bounded `WM_CLOSE` (`100..30000`, default `3000`), and propagate exact exit codes. |
| `--register-assoc` / `--check-updates` (`--strict`) | Register `.recomp`/`.decomp` Explorer handlers, or sweep installed engine update feeds (`2s` timeout, ranking `-hf.<n>` hotfixes above stable releases; `--strict` returns Exit `4` if offline). |
| `--feed <urlOrPath>` / `--engines-dir <dir>` | Override the update feed URL/path or `engines\` discovery root. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` | Redirect runtime data, canonical `data\config\settings.json` (plus split `video`/`audio`/`controls` files), and diagnostic logs. |
| `--verify` / `--doctor` / `--tray-only` / `--browser` | Run non-UI diagnostics, exit after diagnostics (`--tray-only`), or launch browser mode (`--browser`, `--port <n>`). |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` / `--dry-run` | Forward diagnostic logging, frame caps, or fixture overrides, or dry-run without spawning child processes. |
| `--dump-screenshot <path>` / `--dump-audio <path>` | Capture PNG frame or WAV PCM output cleanly. |
| `--version` / `--help` | Print the active router version (`0.6.2`) or CLI usage summary. |

6. **Exit Code Reference:**
   - `0`: Success (including `--verify-module` pass with `hasMediaReady: false` when `--require-media` is not set, or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure, no installed `.recomp` engine satisfies version/track/alias or incompatibility rules, or `--verify-module --require-media` failed on `.recomp`.
   - `2`: Invalid CLI flag, malformed `.recomp` / `.decomp` package, Zip-Slip archive error, containment violation, or `--automation-port` on a `Release` build of either `reEngine.exe` or `reEngine-Setup-v0.6.2-win-x64.exe`.
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
  - **Library:** Choose from all 5 Windows render backends (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`), resolution scale (`1x`, `2x`, `4x`, `8x`), toggle **Fullscreen**, toggle **Allow live JIT bring-up fallback when precompiled AOT cache is not built**, configure quick runtime options (**Preload Media to RAM**, **Fast Boot**, **SAFE MODE** badge + **Reset Safe Mode**, **Display** mode, **Aspect**, **Filter** across all 8 presets, **VSync**, **24-bit Color**, **Dithering**, **Capture** mode, **Audio** driver, **Mute Audio**, **Mute on Focus Loss**, and **Audio Filter** preset), click **Open .recomp Package...** to launch interactively, or click **Verify Package...** to validate any `.recomp` / `.decomp` package in-place.
  - **Engines:** Browse installed engines/tracks/aliases (`reCart`/`reCartOne`/`reNes`, `reSlate`/`reStation`), toggle launch update checks & auto-staged patches, select install **Profile** (`Lightweight` / `Full`), register `.recomp`/`.decomp` file associations, check for updates (including `-hf.<n>` hotfix releases), or **Pin Selected Track** to lock an engine version for rollback.
  - **Bring-Up:** Inspect offline `.decomp` toolchain readiness and `<DataDir>\cache\` AOT cache usage, click **Open .decomp Recipe...** (or double-click a `.decomp` file in Explorer), choose `D0`–`D5`, click **Locate Media...** to resolve or stage personal disc/ROM media, click **Verify Package...**, or click **Run Stage** with live log output.
  - **Inspector Drawer:** Collapsible side panel showing the active execution mode (`Precompiled AOT` or `Live JIT Fallback`), Safe Mode state, selected backend/scale, Display/Filter/Audio Filter summary, decompilation pipeline status, local `rom\` folder path, and pinned engine tracks (**Clear Pinned Track**).
- **Split-Domain Settings (`<DataDir>\config\`):**
  - `settings.json` (`schemaVersion: 2`, `domain: "core"`): stores `installProfile`, `isJitEnabled`, `executionTierMode`, `shouldPreloadMediaToRam`, `isFastBootEnabled`, `isSafeModeActive`, `shouldFastLoadData`, `shouldEnableRuntimeLogs`, `fastForwardMultiplier`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `pinnedEngineVersions`, `stagedUpdatePath`, install paths, and decomp tier preferences.
  - `settings.video.json` (`domain: "video"`): stores `backendId`, `resolutionScale`, `displayMode`, `monitorIndex`, `isFullscreen`, `isVsyncEnabled`, `frameRateMode`, `targetFps`, `backgroundFpsCap`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isHdTexturePackEnabled`, `hudAnchorMode`, `videoFilterMode`, `isTrueColor24BitEnabled`, `isDitheringEnabled`, `screenshotCaptureMode`, `uiTextScale`, `isFpsOverlayEnabled`, and `isCleanCaptureModeEnabled`.
  - `settings.audio.json` (`domain: "audio"`): stores `audioDriver`, `audioOutputDevice`, `masterVolume`, `isAudioMuted`, `bgmVolume`, `sfxVolume`, `voiceVolume`, `shouldMuteOnFocusLoss`, `audioLowPassHz`, `audioHighPassHz`, `audioStereoWidthPercent`, `audioResamplingMode`, and `fastForwardAudioMode`.
  - `settings.controls.json`: persists `controlMode`, `mouseSensitivity`, `menuControllerChord`, `hostActionBindings` (`8` rebindable host hotkeys), and all 3 controller and keyboard/mouse binding tiers (`Modern PC (Per-Action)`, `Game 1:1`, `Global 1:1`) with duplicate/hidden-slot validation and idle dirty-check protection preventing unnecessary disk writes.
  - **Self-Healing Recovery:** Automatic `v1 -> v2` upgrades snapshot legacy settings into `<DataDir>\config_history\`, corrupt JSON files are moved safely into `<DataDir>\quarantine\`, and loads recover automatically from `.bak` or history snapshots.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.6.2] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log` (with verbose runtime logs toggled via `shouldEnableRuntimeLogs` or `--enable-logs`), and unexpected errors write JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Suite Folder Structure
- `<SuiteRoot>\reEngine\reEngine.exe` — lightweight launcher entrypoint (zero background services; holds child process guard only under `--wait`).
- `<SuiteRoot>\reEngine\app\` — router and `reEngine.Host.dll` binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<SuiteRoot>\reEngine\settings.example.json` & `THIRDPARTY_LICENSES.txt` — core configuration template and third-party notices.
- `<SuiteRoot>\reEngine\scripts\` — optional Windows file association scripts.
- `<SuiteRoot>\toolchain\` — offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` (`Full` installs; portable `reEngine\toolchain\` also supported).
- `<SuiteRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`/`reSlate`, `reCart`/`reCartOne`/`reNes`, etc.; `0.3.0+` and `0.3.0-hf.<n>` builds promote via `.staging-<ver>` and `.ready`).
- `<DataDir>\config\`, `<DataDir>\config_history\`, `<DataDir>\quarantine\`, `<DataDir>\cache\`, `<DataDir>\hdpacks\`, `<DataDir>\screenshots\`, `<DataDir>\modules\<moduleId>\` — split atomic settings, snapshots, quarantined corrupt configs, AOT cache, HD texture overrides, `F12` PNG screenshots, and isolated per-game settings and logs.

### 5.2 Common Remedies
- **Safe Mode Activated After Crash:** `booting.sentinel` remained from a previous launch that crashed before rendering frame `1`. Safe Mode disables `.re*` packs, HD textures, and switches Vulkan/DirectX 12 to OpenGL in memory without changing your saved files. Click **Reset Safe Mode** in the **Library** tab, reset it inside the `F1` **System** tab, or pass `--safe-mode-reset`; rebuild `<DataDir>\cache\` if crashes repeat.
- **Pressing `Esc` Does Not Close the Host Window:** By design, `Esc` follows a safe 4-step precedence (cancel Keybind capture -> close `F1` drawer -> forward `Esc` if bound in `Modern PC (Per-Action)` -> release relative mouse capture). Use **`System -> Quit Game`** inside `F1`, the window close button (`X`), or `Alt+F4` to exit cleanly.
- **RAM Preload Warning (`Falling back to disk streaming`):** Ensure at least `512 MiB` of free physical RAM remains after loading the media and that the file uses `<= 75%` of available physical memory, or disable **Preload Media to RAM** (`--no-preload-media-to-ram`).
- **No Compatible `.recomp` Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId` (or alias), compatible track, minimum `engineVersion`, and not excluded by `incompatibleEngineVersions` is installed under `<SuiteRoot>\engines\` (with `.ready` present on `v0.3.0+` or `-hf.<n>` hotfixes), or pass `--engines-dir <path>`.
- **Backend Unavailable Under `--require-backend` (Exit `6`):** Switch to a supported Windows backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, or `software`), ensure `< 0.3.0` engines declare `hostProtocolVersion >= 0.3.0` if backend routing is expected, or omit `--require-backend` to allow automatic fallback (`software` / `parallelCpu`).
- **Unbuilt `.decomp` Platform Engine (Exit `3`):** Read the 3-part diagnostic (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) to see which engine track or decompiler plugin (`app\<engineId>.Decomp.dll`) is missing, or pass `--engine-dir <path>`.
- **Shows `DECOMP: ROUTER` After Installing Tools:** Ensure CLI Ghidra (`ghidra\support\analyzeHeadless.bat`, with `ghidra\GPL\` stripped) and OpenJDK (`jdk\bin\java.exe`) plus `THIRDPARTY_LICENSES.txt` sit inside `<SuiteRoot>\toolchain\` (or portable `.\toolchain\`).
- **Package Rejected (Exit `2`):** Ensure the `.recomp` / `.decomp` archive contains valid JSON, a valid lowercase `moduleId`, no `..` traversal paths, and (if passing `--automation-port`) is only run on Debug/test builds rather than Release binaries (both router and Setup EXE enforce Release `ExitCode = 2`).
- **Uninstalling `reEngine`:** Run `reEngine-Setup-v0.6.2-win-x64.exe --uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting your game saves or personal media inside game folders.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to verify the active router build (`0.6.2`).
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
