# reEngine — User Guide

**Synced Version:** `0.2.1` (`2026-11-15`)  
**Scope:** End-User Installation, Suite Topology, Launching `.recomp` / `.decomp` Modules, AOT Cache & Licensing

---

## 1. Overview & Peer Suite Installation

`reEngine` is the lightweight, zero-daemon suite router, `.decomp` pipeline coordinator, and engine manager for `.recomp` and `.decomp` packages. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries (`AllowAutoStart = false`).

1. **Standalone Setup:** Run `reEngine-Setup-v0.2.1-win-x64.exe` (the `v0.2.1` single-EXE installer) interactively to choose your profile and directories (choosing a parent folder such as `Misc` automatically sorts `reEngine` into `Misc\reEngine\` with companion `Misc\reEngine\engines\` created automatically), or pass CLI flags (`--silent --no-launch --profile Lightweight|Full`) for unattended exact-path setup.
2. **Peer Suite Layout (`StreamItAll\reEngine\`):** Installs by default into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` with peer folders side-by-side:
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` — router executables, `settings.example.json`, and license files (`--reengine-dir`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\` — optional offline headless Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` installed under `--profile Full`.
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\engines\` — side-by-side platform engines (`reStation\v0.2.4\`, `reNes\v0.1.0\`).
   - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\` — slim game packages and preserved `data\rom\` preload media.
3. **Install Profiles & Directory Flags:**
   - **Lightweight (`--profile Lightweight`, default):** Installs the compact router and engine manager without bundled decompilation tools.
   - **Full (`--profile Full`):** Includes the offline Apache-2.0 headless Ghidra + OpenJDK toolchain (`toolchain\`) with zero internet calls required during decompilation.
   - **Path Overrides:** Override the suite root with `--suite-dir <path>`, override the router folder with `--reengine-dir <path>` or `REENGINE_HOME`, or opt into one-time migration from older installs via the unchecked **Migrate legacy installation** checkbox (`--migrate-legacy`).
4. **Automatic Game-Suite Bootstrap:** Installing any compatible `re*` platform engine or `<Game>Recomp` title checks the installed `reEngine` version and silently installs or upgrades `reEngine` to at least `v0.2.1` without touching existing side-by-side engine tracks.

---

## 2. Launching `.recomp` & `.decomp` Modules

1. **Register Double-Click Associations:** Click **Register Associations** in the manager window (or run `reEngine.exe --register-assoc`) to bind both `.recomp` (`reEngine.RecompModule`) and `.decomp` (`reEngine.DecompModule`) files under per-user Windows Registry (`HKCU\Software\Classes`).
2. **Launch or Decompile from Explorer or Terminal:**
   ```powershell
   reEngine.exe --open "D:\Games\KingsField\app\KingsFieldRecomp.recomp" --wait
   reEngine.exe decomp --module "D:\Games\Samples\sample-re64.decomp" --stage D3
   reEngine.exe ".\sample.recomp.json" --dry-run
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
3. **Automatic Per-Module Isolation:** Each module receives its own storage folder under `<DataDir>\modules\<moduleId>\` containing an isolated `settings.json` and `logs\` directory so settings and logs never collide across games.
4. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` | Load `.recomp` / `.recomp.json`, detect `data\rom\` media, resolve compatible engine, and launch. |
| `decomp --module <path>` | Dispatch `.decomp` clean-room decompilation (`--stage D0..D5`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--wait` | Wait synchronously for child execution and propagate exact exit codes. |
| `--register-assoc` | Register per-user `.recomp` and `.decomp` file association handlers. |
| `--check-updates` (`--strict`) | Sweep all installed `(engineId, track)` engines; `--strict` returns Exit `4` if offline. |
| `--feed <urlOrPath>` | Override the update feed URL or local feed JSON path. |
| `--engines-dir <dir>` | Override the `engines\` root directory used for discovery. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` | Redirect runtime data, `settings.json`, and log outputs. |
| `--headless` / `--dry-run` | Run automated headless diagnostics or log planned child actions without spawning. |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` | Forward diagnostic logging, frame caps, or fixture overrides to the child engine. |
| `--dump-screenshot <path>` / `--dump-audio <path>` / `--input-script <path>` | Forward capture and deterministic input playback options to the child engine. |
| `--version` / `--help` | Print the active router version (`0.2.1`) or CLI usage summary. |

5. **Exit Code Reference:**
   - `0`: Success (or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure or no installed `.recomp` engine satisfies version and track requirements.
   - `2`: Invalid CLI flag, malformed `.recomp` / `.decomp` package, Zip-Slip archive error, or containment violation.
   - `3`: Unbuilt or missing platform engine for a `.decomp` module (emits a 3-part remediation message: what failed, missing engine/track, and how to install or pass `--engine-dir`).
   - `4`: Offline/network error under `--check-updates --strict`.
   - `130`: Interrupted by `Ctrl+C` (`CancelKeyPress`); child processes are cleanly terminated.

---

## 3. Preloading Legal Optical Discs & ROMs (`data\rom\`)

`reEngine` never bundles copyrighted game discs, ROM images, or console BIOS files. Place your legally dumped personal media inside the game module's `data\rom\` folder to skip first-run media prompts:

- **Installed Modules:** Place dumps in `<GameInstallRoot>\data\rom\` (for example `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\KingsFieldRecomp\data\rom\`). Cartridges packaged under `<GameInstallRoot>\app\<Module>.recomp` automatically resolve `data\rom\` from `<GameInstallRoot>\`.
- **Automatic Format Priority:** `reEngine` scans `data\rom\` recursively and picks the primary image automatically in this order:
  1. `.cue` (disc sheet)
  2. `.chd` (compressed disc archive)
  3. `.iso` (raw disc image)
  4. `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`
  5. `.7z`, `.zip`, `.img`
  6. `.bin`

---

## 4. Using the Suite Manager UI, AOT Cache & Settings

Launch `reEngine.exe` without CLI flags to open the Dark Slate suite manager window:

- **Header Status Pills:** Displays `ZERO-DAEMON`, `DECOMP: FULL` (offline Ghidra + OpenJDK detected under peer `..\toolchain\` or local `toolchain\`) or `DECOMP: ROUTER` (router-only `Lightweight` profile), association status (`REGISTERED` or `UNREGISTERED`), and update status (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `PATCH AVAILABLE`, `BREAKING AVAILABLE`, `OFFLINE / SKIPPED`, or `CHECK FAILED`).
- **AOT Cache Status Line:** Summarizes `<DataDir>\cache\` compiled AOT assembly count and footprint (`aot-index.json` SHA-256 verified per module/ROM/engine key; automatically invalidates corrupted files and retains `.bak` index backups).
- **Installed Platform Engines Table:** Lists every discovered `engineId`, installed SemVer, compatibility track (`Major.Minor.x`), and installation path.
- **Profile & Settings Controls (`settings.json` / `settings.example.json`):**
  - **Profile:** Switch between `Lightweight` (core router) and `Full` (offline headless toolchain) via the `Profile:` dropdown.
  - **Check for compatible updates on launch** (`shouldCheckUpdates`)
  - **Automatically stage compatible patches** (`shouldAutoStagePatches`)
  - **Open .recomp Package**, **Register Associations**, **Check For Updates**, and **Save Settings**
  - A clean template `settings.example.json` ships in `<InstallRoot>\` showing all supported personal overlay keys (`installProfile`, `reEngineInstallDir`, `suiteInstallDir`, `umbrellaSubpath`, `decompSchemaVersion`, `goalDecompTier`, `targetDecompTier`).
- **Offline & Multi-Engine Sweeps:** Every distinct `(engineId, track)` pair is checked independently. Compatible track patches are downloaded and SHA-256 verified without touching older breaking tracks, while offline feeds log `[WARN]` and show `OFFLINE / SKIPPED` with zero crash dumps.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.2.1] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log`, and unexpected failures record structured JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Peer Suite Installation Layout
- `<SuiteRoot>\reEngine\reEngine.exe` — lightweight launcher entrypoint (zero background daemon).
- `<SuiteRoot>\reEngine\app\` — router binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<SuiteRoot>\reEngine\settings.example.json` & `THIRDPARTY_LICENSES.txt` — documented configuration overlay template and third-party notices.
- `<SuiteRoot>\reEngine\scripts\` — optional Windows shell association and helper `.bat` scripts.
- `<SuiteRoot>\toolchain\` — offline headless Ghidra + OpenJDK + `THIRDPARTY_LICENSES.txt` (`Full` installs; portable `reEngine\toolchain\` also supported).
- `<SuiteRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`, `reNes`, etc.; portable `reEngine\engines\` also supported).
- `<DataDir>\settings.json`, `<DataDir>\cache\`, `<DataDir>\modules\<moduleId>\` — user settings (with `.bak` self-healing), AOT cache, and isolated per-game settings and logs.

### 5.2 Common Remedies
- **No Compatible `.recomp` Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId`, `compatibleEngineTrack`, and minimum `engineVersion` exists under `<SuiteRoot>\engines\` or pass `--engines-dir <path>`.
- **Unbuilt `.decomp` Platform Engine (Exit `3`):** Check the 3-part error diagnostic (`[1/3 What Failed]`, `[2/3 Missing]`, `[3/3 Remediation]`) to see which engine track is missing or pass `--engine-dir <path>`.
- **Shows `DECOMP: ROUTER` After Installing Tools:** Ensure headless Ghidra (`ghidra\support\analyzeHeadless.bat`, with `ghidra\GPL\` stripped) and OpenJDK (`jdk\bin\java.exe`) plus `THIRDPARTY_LICENSES.txt` reside in `<SuiteRoot>\toolchain\` (or portable `.\toolchain\`).
- **Package Rejected (Exit `2`):** Ensure the `.recomp` / `.decomp` ZIP archive contains valid JSON (`module.recomp.json` / `module.decomp.json`), a valid lowercase `moduleId`, and no rooted or `..` traversal paths.
- **Offline Update Failure (Exit `4`):** Occurs only when `--check-updates --strict` is passed while offline; omit `--strict` during normal interactive sessions so `reEngine` degrades safely to `OFFLINE / SKIPPED`.
- **Media Picker Still Appears:** Verify your personal disc/ROM dump sits directly inside `<GameInstallRoot>\data\rom\` with a supported extension (`.cue`, `.chd`, `.iso`, `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`, `.7z`, `.zip`, `.img`, `.bin`).
- **Corrupt Settings or Cache Index:** Both `<DataDir>\settings.json` and `<DataDir>\cache\aot-index.json` retain `.bak` backups alongside atomic writes.
- **Uninstalling `reEngine`:** Run `reEngine-Setup-v0.2.1-win-x64.exe --uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting user game saves or preloaded media inside game directories.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to verify the active router build (`0.2.1`).
- Run `reEngine.exe --headless` to verify active `profile`, `toolchain`, `aotAssemblies`, discovered engines, and association status in a single line.
- Attach `<LogDir>\reengine.log` and any matching `<LogDir>\crashes\crash_reengine_*.json` bundle when reporting issues.

---

## License & Legal

- **Router & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0**. A verbatim copy of `LICENSE` is included inside `app\LICENSE`, the source archive, and the SDK distribution.
- **Offline Toolchain Licenses (`THIRDPARTY_LICENSES.txt`):** Optional `Full` toolchain staging bundles headless **Ghidra** (`Apache-2.0`, `ghidra\GPL\` and `ghidra\docs\` stripped) and a `jlink`-trimmed **OpenJDK** runtime (**GPL-2.0 with Classpath Exception**). Full notices ship in `THIRDPARTY_LICENSES.txt`.
- **Bundled Archiver Attribution (`7-Zip`):** Build-time archive packaging uses `7z.exe` and `7z.dll`, distributed under the **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause and BSD 2-clause licenses for LZFSE, ZSTD, and XXH64 decompression components).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or console hardware manufacturer. All trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must supply their own legally obtained optical discs or cartridge dumps inside `data\rom\`.
