# reEngine — User Guide

**Synced Version:** `0.3.0` (`2026-11-18`)  
**Scope:** Installation, Suite Layout, Launching `.recomp` / `.decomp` Modules, Overlays, AOT Cache & Licensing

> **Beta (Pre-1.0):** `reEngine` is currently a pre-1.0 beta release working toward `v1.0.0` desktop stability and `v2.0.0` cross-platform compatibility.

---

## 1. Overview & Suite Installation

`reEngine` is a lightweight, zero-daemon suite router, shared SDL3 host (`reEngine.Host.dll`), `.decomp` pipeline coordinator, and engine version manager for static recompilation modules. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries.

1. **Standalone Setup:** Run `reEngine-Setup-v0.3.0-win-x64.exe` interactively to choose your profile and folders (choosing a parent folder such as `Misc` automatically sorts `reEngine` into `Misc\reEngine\` with companion `Misc\reEngine\engines\` created automatically), or pass CLI flags (`--silent --no-launch --profile Lightweight|Full`) for automated installs.
2. **Suite Folder Layout (`StreamItAll\reEngine\`):** Installs by default into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` with side-by-side folders:
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` — router executables, `settings.example.json`, `data\config\settings.json`, and license files (`--reengine-dir`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\` — optional offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` installed under `--profile Full`.
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\` — side-by-side platform engines (`reStation\v0.2.8\`, `reStation\v0.3.0\`, `reNes\v0.1.0\`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\` — slim game packages and preserved `data\rom\` user media folders.
3. **Install Profiles & Directory Flags:**
   - **Lightweight (`--profile Lightweight`, default):** Installs the compact router and engine manager without bundled decompilation tools.
   - **Full (`--profile Full`):** Includes the offline Apache-2.0 CLI Ghidra + OpenJDK toolchain (`toolchain\`) with zero internet calls required during decompilation.
   - **Path Overrides:** Override the suite root with `--suite-dir <path>`, override the router folder with `--reengine-dir <path>` or `REENGINE_HOME`, or opt into one-time migration from older installs via the unchecked **Migrate legacy installation** checkbox (`--migrate-legacy`).
4. **Automatic Game-Suite Bootstrap:** Installing any compatible platform engine or game module checks your installed `reEngine` version and installs or upgrades `reEngine` when needed without touching existing side-by-side engines.

---

## 2. Launching `.recomp`, `.decomp` & CLI Options

1. **Register Double-Click Associations:** Click **Register Associations** in the manager **Engines** tab (or run `reEngine.exe --register-assoc`) to bind both `.recomp` (`reEngine.RecompModule`) and `.decomp` (`reEngine.DecompModule`) files under per-user Windows Registry (`HKCU\Software\Classes`).
2. **Launch, Decompile, or Run Diagnostics from Terminal:**
   ```powershell
   reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend opengl --scale 4 --wait
   reEngine.exe decomp --module "D:\Games\Samples\SampleModule.decomp" --stage D4
   reEngine.exe --host-demo --window
   reEngine.exe --verify
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
3. **Interactive Host Controls (`reEngine.Host.dll` & compatible engines):**
   - `F1`: Toggle the 5-tab settings drawer (`Video`, `Controls`, `Keybinds`, `System`, `DiscMedia`).
   - `F3`: Toggle the live FPS, frame time, backend, scale, execution tier, and audio fill HUD (`F3Hud`).
   - `F12`: Request a PNG frame capture when `--dump-screenshot` is active; `ESC`: Close open drawers or exit cleanly.
4. **Automatic Per-Module Isolation:** Each `.recomp` module receives its own storage folder under `<DataDir>\modules\<moduleId>\` containing an isolated `settings.json` and `logs\` directory so settings and logs never collide across games.
5. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` | Load `.recomp` / `.recomp.json`, detect `data\rom\` media, resolve compatible engine/alias, and launch cleanly (detached unless `--wait` is also passed). |
| `decomp --module <path>` | Dispatch `.decomp` clean-room decompilation (`--stage D0..D5`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--host-demo` | Launch the standalone visible `reEngine.Host` SDL3 test window with `F1` drawer, `F3` HUD, and BYO media gate preview. |
| `--backend <id>` / `--scale <n>` | Select render backend (`opengl` or `software` today, plus `vulkan`/`parallelCpu` for upcoming `0.3.x` engines) and resolution scale (`1`, `2`, `4`, `8`). |
| `--require-backend` / `--jit-bringup` | `--require-backend` requires a `0.3.0+` engine; `--jit-bringup` opts into live JIT fallback when no precompiled AOT cache is built. |
| `--wait` | Wait synchronously for the launched engine process and propagate exact exit codes. |
| `--register-assoc` | Register per-user `.recomp` and `.decomp` file association handlers. |
| `--check-updates` (`--strict`) | Sweep all installed `(engineId, track)` engines (`2s` feed poll + resumable `.part` staging); `--strict` returns Exit `4` if offline. |
| `--feed <urlOrPath>` / `--engines-dir <dir>` | Override the update feed URL/path or `engines\` discovery root. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` / `--save-dir <dir>` | Redirect runtime data, canonical `data\config\settings.json`, logs, and module saves. |
| `--verify` / `--verify-module <id>` / `--doctor` / `--tray-only` | Run non-UI diagnostics covering engine discovery, AOT cache, offline toolchain, and file associations. |
| `--window` / `--browser` / `--suite-tab <tab>` / `--dry-run` | Open the Dark Slate manager UI (`--window`), open browser mode (`--browser`, `--port <n>`), or dry-run without spawning child processes. |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` | Forward diagnostic logging, frame caps, or fixture overrides to the child engine. |
| `--dump-screenshot <path>` / `--dump-audio <path>` | Capture PNG frame or WAV PCM output cleanly. |
| `--version` / `--help` | Print the active router version (`0.3.0`) or CLI usage summary. |

6. **Exit Code Reference:**
   - `0`: Success (or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure or no installed `.recomp` engine satisfies version/track/alias requirements.
   - `2`: Invalid CLI flag, malformed `.recomp` / `.decomp` package, Zip-Slip archive error, containment violation, or `--automation-port` on a `Release` build.
   - `3`: Unbuilt or missing platform engine for a `.decomp` module (prints what failed, what engine/track is missing, and how to fix it).
   - `4`: Offline/network error under `--check-updates --strict`.
   - `130`: Interrupted by `Ctrl+C` (`CancelKeyPress`); synchronous child processes are cleanly terminated.

---

## 3. Adding Your Personal Discs & ROMs (`data\rom\`)

`reEngine` never bundles copyrighted game discs, ROM images, or console BIOS files. Place your legally dumped personal media inside the game module's `data\rom\` folder to skip first-run media prompts:

- **Installed Modules:** Place dumps in `<GameInstallRoot>\data\rom\` (for example `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\MyGame\data\rom\`). Cartridges packaged under `<GameInstallRoot>\app\<Module>.recomp` automatically look inside `<GameInstallRoot>\data\rom\`.
- **Automatic Format Priority:** `reEngine` scans `data\rom\` recursively and selects the primary image automatically in this order:
  1. `.cue` (disc sheet)
  2. `.chd` (compressed disc archive)
  3. `.iso` (raw disc image)
  4. `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`
  5. `.7z`, `.zip`, `.img`
  6. `.bin`

---

## 4. Using the Manager UI, AOT Cache & Settings

Launch `reEngine.exe` without CLI flags to open the Dark Slate manager window:

- **Header Status Pills:** Displays `ZERO-DAEMON`, `DECOMP: FULL` (offline CLI Ghidra + OpenJDK detected under `toolchain\`) or `DECOMP: ROUTER` (router-only `Lightweight` profile), association status (`REGISTERED`, `VIRTUALIZED`, or `UNREGISTERED`), and update status (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `OFFLINE`, or `FAILED`).
- **Three Navigation Tabs (`Library`, `Engines`, `Bring-Up`) & `Inspector Drawer`:**
  - **Library:** Choose your render backend (`opengl` or `software`), resolution scale (`1x`, `2x`, `4x`), toggle **Allow live JIT fallback when precompiled AOT cache is not built**, and click **Open .recomp Package...** to launch a game interactively.
  - **Engines:** Browse installed engines/tracks/aliases, toggle launch update checks & auto-staged patches, select install **Profile** (`Lightweight` / `Full`), register `.recomp`/`.decomp` file associations, check for updates, or **Pin Selected Track** to lock an engine version for rollback.
  - **Bring-Up:** Inspect offline `.decomp` toolchain readiness and local precompiled AOT cache usage under `<DataDir>\cache\`.
  - **Inspector Drawer:** Collapsible side panel showing the active execution mode (`Precompiled AOT` or `Live JIT Fallback`), selected backend/scale, decompilation pipeline status, local `rom\` folder path, and pinned engine tracks (**Clear Pinned Track**).
- **Unified Settings (`<DataDir>\config\settings.json`) & Automatic Migration:** On startup, `reEngine` automatically merges older legacy `data\settings.json` / `app\data\settings.json` files newest-wins into `<DataDir>\config\settings.json`, preserves custom keys, snapshots originals into `data\config_history\`, and renames migrated files with `.migrated.bak`.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.3.0] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log`, and unexpected errors write JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Suite Folder Structure
- `<SuiteRoot>\reEngine\reEngine.exe` — lightweight launcher entrypoint (zero background services).
- `<SuiteRoot>\reEngine\app\` — router and `reEngine.Host.dll` binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<SuiteRoot>\reEngine\settings.example.json` & `THIRDPARTY_LICENSES.txt` — configuration template and third-party notices.
- `<SuiteRoot>\reEngine\scripts\` — optional Windows file association scripts.
- `<SuiteRoot>\toolchain\` — offline CLI Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` (`Full` installs; portable `reEngine\toolchain\` also supported).
- `<SuiteRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`/`reSlate`, `reNes`/`reCartOne`, etc.; `0.3.0+` builds promote via `.staging-<ver>` and `.ready`).
- `<DataDir>\config\settings.json`, `<DataDir>\cache\`, `<DataDir>\modules\<moduleId>\` — user settings (with `.bak` self-healing), AOT cache, and isolated per-game settings and logs.

### 5.2 Common Remedies
- **No Compatible `.recomp` Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId` (or alias), compatible track, and minimum `engineVersion` is installed under `<SuiteRoot>\engines\` (with `.ready` present on `v0.3.0+`), or pass `--engines-dir <path>`.
- **Unbuilt `.decomp` Platform Engine (Exit `3`):** Read the 3-part diagnostic (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) to see which engine track or decompiler plugin (`app\<engineId>.Decomp.dll`) is missing, or pass `--engine-dir <path>`.
- **Shows `DECOMP: ROUTER` After Installing Tools:** Ensure CLI Ghidra (`ghidra\support\analyzeHeadless.bat`, with `ghidra\GPL\` stripped) and OpenJDK (`jdk\bin\java.exe`) plus `THIRDPARTY_LICENSES.txt` sit inside `<SuiteRoot>\toolchain\` (or portable `.\toolchain\`).
- **Package Rejected (Exit `2`):** Ensure the `.recomp` / `.decomp` archive contains valid JSON, a valid lowercase `moduleId`, no `..` traversal paths, and (if passing `--automation-port`) is only run on Debug/test builds rather than Release binaries.
- **Offline Update Failure (Exit `4`):** Occurs only when `--check-updates --strict` is passed while offline; omit `--strict` during normal interactive sessions so `reEngine` degrades safely to `OFFLINE`.
- **Media Picker Still Appears:** Verify your personal disc/ROM dump sits inside `<GameInstallRoot>\data\rom\` with a supported extension (`.cue`, `.chd`, `.iso`, `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`, `.7z`, `.zip`, `.img`, `.bin`).
- **Uninstalling `reEngine`:** Run `reEngine-Setup-v0.3.0-win-x64.exe --uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting your game saves or personal media inside game folders.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to check the active router build (`0.3.0`).
- Run `reEngine.exe --verify` (or `--tray-only`) to check active `profile`, `toolchain`, `aotAssemblies`, discovered engines, and association status.
- Attach `<LogDir>\reengine.log` and any matching `<LogDir>\crashes\crash_reengine_*.json` bundle when reporting issues.

---

## License & Legal

- **Router, Host & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0**. A verbatim copy of `LICENSE` is included inside `app\LICENSE`, source archives, and SDK distributions.
- **Host Windowing & Audio (`ppy.SDL3-CS` & `SDL3`):** Cross-platform SDL3 windowing, audio streaming, and input bindings use **ppy.SDL3-CS** (`MIT License`) and **Simple DirectMedia Layer 3 (SDL3)** (`zlib License`). Full notices ship in `THIRDPARTY_LICENSES.txt`.
- **Offline Toolchain Licenses (`THIRDPARTY_LICENSES.txt`):** Optional `Full` toolchain installs include CLI **Ghidra** (`Apache-2.0`, with `ghidra\GPL\` and `ghidra\docs\` removed) and a `jlink`-trimmed **OpenJDK** runtime (**GPL-2.0 with Classpath Exception**).
- **Bundled Archiver Attribution (`7-Zip`):** Build-time archive packaging uses `7z.exe` and `7z.dll`, distributed under the **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause and BSD 2-clause licenses for LZFSE, ZSTD, and XXH64 components).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or console hardware manufacturer. All trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must supply their own legally obtained optical discs or cartridge dumps inside `data\rom\`.
