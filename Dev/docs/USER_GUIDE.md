# reEngine — User Guide

**Synced Version:** `0.4.0` (`2026-11-19`)  
**Scope:** Installation, Suite Layout, 5-Backend Rendering, Multi-Bind Input, `.decomp` Bring-Up Runner, Package Verification & Licensing

> **Beta (Pre-1.0):** `reEngine` is currently a pre-1.0 beta release working toward `v1.0.0` desktop stability and `v2.0.0` cross-platform compatibility.

---

## 1. Overview & Suite Installation

`reEngine` is a lightweight, zero-daemon suite router, unified 5-backend SDL3 host runtime (`reEngine.Host.dll`), `.decomp` Bring-Up runner, and engine version manager for static recompilation modules. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries.

1. **Standalone Setup:** Run `reEngine-Setup-v0.4.0-win-x64.exe` interactively to choose your profile and folders (choosing a parent folder such as `Misc` automatically sorts `reEngine` into `Misc\reEngine\` with companion `Misc\reEngine\engines\` created automatically), or pass CLI flags (`--silent --no-launch --profile Lightweight|Full`) for automated installs.
2. **Suite Folder Layout (`StreamItAll\reEngine\`):** Installs by default into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` with side-by-side folders:
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` — router executables, `settings.example.json`, `data\config\settings.json`, and license files (`--reengine-dir`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\` — optional offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` installed under `--profile Full`.
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\` — side-by-side platform engines (`reStation\v0.3.1\`, `reNes\v0.1.0\`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\` — slim game packages and preserved `data\rom\` user media folders.
3. **Install Profiles & Directory Flags:**
   - **Lightweight (`--profile Lightweight`, default):** Installs the compact router and engine manager without bundled decompilation tools.
   - **Full (`--profile Full`):** Includes the offline Apache-2.0 CLI Ghidra + OpenJDK toolchain (`toolchain\`) with zero internet calls required during decompilation.
   - **Path Overrides:** Override the suite root with `--suite-dir <path>`, override the router folder with `--reengine-dir <path>` or `REENGINE_HOME`, or opt into one-time migration from older installs via the unchecked **Migrate legacy installation** checkbox (`--migrate-legacy`).
4. **Automatic Game-Suite Bootstrap:** Installing any compatible platform engine or game module checks your installed `reEngine` version and installs or upgrades `reEngine` when needed without touching existing side-by-side engines or overwriting `v0.4.0` settings.

---

## 2. Launching `.recomp`, `.decomp`, Verification & CLI Options

1. **Register Double-Click Associations:** Click **Register Associations** in the manager **Engines** tab (or run `reEngine.exe --register-assoc`) to bind both `.recomp` (`reEngine.RecompModule`) and `.decomp` (`reEngine.DecompModule`) files under per-user Windows Registry (`HKCU\Software\Classes`).
   - Double-clicking a `.recomp` file validates the manifest, discovers personal media, and launches the compatible static recompilation engine.
   - Double-clicking a `.decomp` file opens the dark-slate manager directly on the **Bring-Up** shelf pre-loaded with the recipe, `D0..D5` stage selector, and suite-tree media status.
2. **Launch, Verify, Decompile, or Run Diagnostics from Terminal:**
   ```powershell
   reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend directx --scale 4 --fullscreen --save-dir "D:\Games\MyGame\saves" --shutdown-timeout-ms 3000 --wait
   reEngine.exe --verify-module "D:\Games\MyGame\app\MyGame.recomp" --json
   reEngine.exe --verify-module "D:\Games\Samples\SampleModule.decomp" --require-media
   reEngine.exe decomp --module "D:\Games\Samples\SampleModule.decomp" --stage D4 --headless
   reEngine.exe --host-demo --backend opengl --require-backend --audio-driver wasapi --window
   reEngine.exe --verify
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
3. **Interactive Host Controls & `F1` 5-Tab Settings Drawer (`reEngine.Host.dll`):**
   - `F1`: Toggle the 5-tab settings drawer (`Video`, `Ctrl`, `Keys`, `Sys`, `Media`):
     - **Video:** Switch live across all 5 Windows backends (`OpenGl`, `Vulkan`, `DirectX`, `ParallelCpu`, `Software`), scale (`1x`, `2x`, `4x`), **Fullscreen**, and **HD Packs** (loads PNG overrides from `<DataDir>\hdpacks\` keyed by CLUT and rect SHA-256).
     - **Controls (`Ctrl`):** Switch Schema `A` / `B` / `C` profiles and toggle dynamic per-game `ControlOptions` pills exposed by the active module.
     - **Keybinds (`Keys`):** Interactive 5-column capture matrix (`Action | 1st | 2nd | 3rd | Gamepad`) supporting 3 simultaneous keyboard/mouse/wheel bindings plus Xbox and open-standard HID SDL3 gamepads (with rumble). **Left-click** any cell (`...`) and press a key, mouse button/wheel, or gamepad input to bind (`Esc` cancels, `Del` or `Backspace` clears); **right-click** any cell to clear that slot immediately; or click **Reset Defaults** / scroll (`^`, `v`).
     - **System (`Sys`) & Media (`Media`):** View active execution tier (`AOT Primary` / `JIT Bring-Up`), runtime identity, and loaded disc/ROM location.
   - `F3`: Toggle the live FPS, frame time, backend, scale, execution tier, and audio fill HUD (`F3Hud`).
   - `F12`: Request a PNG frame capture when `--dump-screenshot` is active; `ESC`: Cancel active keybind capture, close open drawers, or exit cleanly.
4. **Automatic Per-Module Isolation & Bounded Frontend Shutdown:** Each `.recomp` module receives its own storage folder under `<DataDir>\modules\<moduleId>\` containing isolated `settings.json` and `logs\`. When launched synchronously from a frontend via `--open <pkg> --wait [--fullscreen] [--save-dir <dir>] [--audio-driver <id>]`, cancelling `--wait` sends a bounded graceful `WM_CLOSE` signal across the child process tree (default `--shutdown-timeout-ms 3000`) before force-terminating so memory cards and saves flush cleanly into `--save-dir`.
5. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` | Load `.recomp` / `.recomp.json`, discover media, resolve compatible engine/alias, and launch cleanly (detached unless `--wait` is also passed). |
| `decomp --module <path>` | Open a `.decomp` package interactively on the **Bring-Up** shelf, or run headless with `--headless` (`--stage D0..D5`, `--media-path`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--verify-module <target>` | Validate a `.recomp`/`.decomp` package (`--json`, `--require-media`) or inspect installed tracks when passed a bare engine ID (`reStation`, `reSlate`). |
| `--host-demo` | Launch the standalone visible `reEngine.Host` SDL3 test window with 5-backend selection, `F1` drawer, `F3` HUD, and `--preload-media` media gate preview. |
| `--backend <id>` / `--scale <n>` | Select render backend (`opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, `software`) and scale (`1`, `2`, `4`, `8`). |
| `--require-backend` / `--fullscreen` / `--hd-packs` | Fail with Exit `6` if the requested backend is unavailable, start in fullscreen, or enable `<DataDir>\hdpacks\` texture overrides. |
| `--audio-driver <id>` / `--jit-bringup` | Pin `SDL_AUDIO_DRIVER` before audio init (`wasapi` on Windows) or opt into live JIT fallback when no AOT cache is built. |
| `--wait` / `--save-dir <dir>` / `--shutdown-timeout-ms <ms>` | Wait synchronously, redirect per-game saves, flush child processes via bounded `WM_CLOSE` (`100..30000`, default `3000`), and propagate exact exit codes. |
| `--register-assoc` / `--check-updates` (`--strict`) | Register `.recomp`/`.decomp` Explorer handlers, or sweep installed engine update feeds (`2s` timeout; `--strict` returns Exit `4` if offline). |
| `--feed <urlOrPath>` / `--engines-dir <dir>` | Override the update feed URL/path or `engines\` discovery root. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` | Redirect runtime data, canonical `data\config\settings.json`, and diagnostic logs. |
| `--verify` / `--doctor` / `--tray-only` / `--window` / `--browser` | Run non-UI diagnostics, open the Dark Slate manager UI (`--window`, `--suite-tab <tab>`), or launch browser mode (`--browser`, `--port <n>`). |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` / `--dry-run` | Forward diagnostic logging, frame caps, or fixture overrides, or dry-run without spawning child processes. |
| `--dump-screenshot <path>` / `--dump-audio <path>` | Capture PNG frame or WAV PCM output cleanly. |
| `--version` / `--help` | Print the active router version (`0.4.0`) or CLI usage summary. |

6. **Exit Code Reference:**
   - `0`: Success (including `--verify-module` pass with `hasMediaReady: false` when `--require-media` is not set, or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure, no installed `.recomp` engine satisfies version/track/alias requirements, or `--verify-module --require-media` failed on `.recomp`.
   - `2`: Invalid CLI flag, malformed `.recomp` / `.decomp` package, Zip-Slip archive error, containment violation, or `--automation-port` on a `Release` build.
   - `3`: Unbuilt or missing platform engine for a `.decomp` module (prints `[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`), or `--verify-module --require-media` failed on `.decomp`.
   - `4`: Offline/network error under `--check-updates --strict`.
   - `6`: Requested render backend is unavailable under `--require-backend` (for example, `metal` on Windows or a pre-`0.3.0` engine lacking backend support).
   - `130`: Interrupted by `Ctrl+C`; synchronous child processes receive bounded `WM_CLOSE` teardown before exit.

---

## 3. Adding Your Personal Discs & ROMs (`data\rom\` & Suite Discovery)

`reEngine` never bundles copyrighted game discs, ROM images, or console BIOS files. You can keep legally dumped media in a game's `data\rom\` folder or anywhere inside your suite directory tree:

- **Installed Modules & Suite-Tree Discovery:** Place dumps in `<GameInstallRoot>\data\rom\` (for example `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\MyGame\data\rom\`). When a module's local `data\rom\` is empty, `reEngine` automatically searches `<SuiteRoot>` (`games\*\data\rom\`, `gamedata\`, and `<SuiteRoot>\data\rom\`), matching module SHA-256 hashes or slugs.
- **Missing Media Prompt (`Locate Media...`):** On the **Bring-Up** shelf, clicking **Locate Media...** opens `Locate Personal Game Media`: select a discovered candidate or click **Browse File...**. The **Copy selected media (+ .cue tracks) into module `data\rom\`** checkbox is **unchecked by default** (use-in-place) to avoid duplicating large disc images; check it only if you want `reEngine` to copy the selected file (and any companion `.bin` tracks listed inside a `.cue` sheet) into `data\rom\`.
- **Automatic Format Priority:** `reEngine` selects primary media in this order: `.cue` -> `.chd` -> `.iso` -> `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba` -> `.7z`, `.zip`, `.img` -> `.bin`.

---

## 4. Using the Manager UI, AOT Cache & Settings

Launch `reEngine.exe` without CLI flags to open the Dark Slate manager window:

- **Header Status Pills:** Displays `ZERO-DAEMON`, `DECOMP: FULL` (offline CLI Ghidra + OpenJDK detected under `toolchain\`) or `DECOMP: ROUTER` (router-only `Lightweight` profile), association status (`REGISTERED`, `VIRTUALIZED`, or `UNREGISTERED`), and update status (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `OFFLINE`, or `FAILED`).
- **Three Navigation Tabs (`Library`, `Engines`, `Bring-Up`) & `Inspector Drawer`:**
  - **Library:** Choose from all 5 Windows render backends (`opengl`, `vulkan`, `directx`, `parallelCpu`, `software`), resolution scale (`1x`, `2x`, `4x`), toggle **Fullscreen**, toggle **Allow live JIT bring-up fallback when precompiled AOT cache is not built**, click **Open .recomp Package...** to launch interactively, or click **Verify Package...** to validate any `.recomp` / `.decomp` package in-place.
  - **Engines:** Browse installed engines/tracks/aliases, toggle launch update checks & auto-staged patches, select install **Profile** (`Lightweight` / `Full`), register `.recomp`/`.decomp` file associations, check for updates, or **Pin Selected Track** to lock an engine version for rollback.
  - **Bring-Up:** Inspect offline `.decomp` toolchain readiness and `<DataDir>\cache\` AOT cache usage, click **Open .decomp Recipe...** (or double-click a `.decomp` file in Explorer), choose `D0`–`D5`, click **Locate Media...** to resolve or stage personal disc/ROM media, click **Verify Package...**, or click **Run Stage** with live log output.
  - **Inspector Drawer:** Collapsible side panel showing the active execution mode (`Precompiled AOT` or `Live JIT Fallback`), selected backend/scale, decompilation pipeline status, local `rom\` folder path, and pinned engine tracks (**Clear Pinned Track**).
- **Unified Settings (`<DataDir>\config\settings.json`):** Stores `installProfile`, `backendId`, `resolutionScale`, `isFullscreen`, `audioDriver`, `isHdTexturePackEnabled`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, and `pinnedEngineVersions` atomically with `.bak` recovery and automatic migration from older installs.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.4.0] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log`, and unexpected errors write JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Suite Folder Structure
- `<SuiteRoot>\reEngine\reEngine.exe` — lightweight launcher entrypoint (zero background services).
- `<SuiteRoot>\reEngine\app\` — router and `reEngine.Host.dll` binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<SuiteRoot>\reEngine\settings.example.json` & `THIRDPARTY_LICENSES.txt` — configuration template and third-party notices.
- `<SuiteRoot>\reEngine\scripts\` — optional Windows file association scripts.
- `<SuiteRoot>\toolchain\` — offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` (`Full` installs; portable `reEngine\toolchain\` also supported).
- `<SuiteRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`/`reSlate`, `reNes`/`reCartOne`, etc.; `0.3.0+` builds promote via `.staging-<ver>` and `.ready`).
- `<DataDir>\config\settings.json`, `<DataDir>\cache\`, `<DataDir>\hdpacks\`, `<DataDir>\modules\<moduleId>\` — user settings (`.bak` self-healing), AOT cache, HD texture overrides, and isolated per-game settings and logs.

### 5.2 Common Remedies
- **No Compatible `.recomp` Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId` (or alias), compatible track, and minimum `engineVersion` is installed under `<SuiteRoot>\engines\` (with `.ready` present on `v0.3.0+`), or pass `--engines-dir <path>`.
- **Backend Unavailable Under `--require-backend` (Exit `6`):** Switch to a supported Windows backend (`opengl`, `vulkan`, `directx`, `parallelCpu`, or `software`) or omit `--require-backend` to allow automatic fallback (`software`).
- **Unbuilt `.decomp` Platform Engine (Exit `3`):** Read the 3-part diagnostic (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) to see which engine track or decompiler plugin (`app\<engineId>.Decomp.dll`) is missing, or pass `--engine-dir <path>`.
- **Shows `DECOMP: ROUTER` After Installing Tools:** Ensure CLI Ghidra (`ghidra\support\analyzeHeadless.bat`, with `ghidra\GPL\` stripped) and OpenJDK (`jdk\bin\java.exe`) plus `THIRDPARTY_LICENSES.txt` sit inside `<SuiteRoot>\toolchain\` (or portable `.\toolchain\`).
- **Package Rejected (Exit `2`):** Ensure the `.recomp` / `.decomp` archive contains valid JSON, a valid lowercase `moduleId`, no `..` traversal paths, and (if passing `--automation-port`) is only run on Debug/test builds rather than Release binaries.
- **Uninstalling `reEngine`:** Run `reEngine-Setup-v0.4.0-win-x64.exe --uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting your game saves or personal media inside game folders.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to verify the active router build (`0.4.0`).
- Run `reEngine.exe --verify` or `reEngine.exe --verify-module "<PackagePath>" --json` to inspect environment status, engines, and package compliance.
- Attach `<LogDir>\reengine.log` and any matching `<LogDir>\crashes\crash_reengine_*.json` bundle when reporting issues.

---

## License & Legal

- **Router, Host & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0**. A verbatim copy of `LICENSE` is included inside `app\LICENSE`, source archives, and SDK distributions.
- **Host Windowing & Audio (`ppy.SDL3-CS` & `SDL3`):** Cross-platform SDL3 windowing, audio streaming, and input bindings use **ppy.SDL3-CS** (`MIT License`) and **Simple DirectMedia Layer 3 (SDL3)** (`zlib License`). Full notices ship in `THIRDPARTY_LICENSES.txt`.
- **Offline Toolchain Licenses (`THIRDPARTY_LICENSES.txt`):** Optional `Full` toolchain installs include CLI **Ghidra** (`Apache-2.0`, with `ghidra\GPL\` and `ghidra\docs\` removed) and a `jlink`-trimmed **OpenJDK** runtime (**GPL-2.0 with Classpath Exception**).
- **Bundled Archiver Attribution (`7-Zip`):** Build-time archive packaging uses `7z.exe` and `7z.dll`, distributed under the **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause and BSD 2-clause licenses for LZFSE, ZSTD, and XXH64 components).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or console hardware manufacturer. All trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must supply their own legally obtained optical discs or cartridge dumps inside `data\rom\` or suite media folders.
