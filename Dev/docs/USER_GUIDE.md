# reEngine — User Guide

**Synced Version:** `0.1.3` (`2026-11-14`)  
**Scope:** End-User Installation, Launching `.recomp` Modules, Preloading Media & Licensing

---

## 1. Overview & Installation

`reEngine` is the lightweight, zero-daemon suite router and engine manager for `.recomp` static recompilation packages. When closed, `reEngine` leaves zero background services running (`0 MB` idle RAM) and never adds Windows Startup entries (`AllowAutoStart = false`).

1. **Standalone Setup:** Run `reEngine-Setup-v0.1.3-win-x64.exe` to install interactively, or run with `--silent --no-launch` for unattended setup.
2. **Default Install Location:** Installs to `<PROGRAMS_ROOT>\reEngine\` by default unless overridden with `--install-dir <path>` or the `REENGINE_HOME` environment variable.
3. **Automatic Game-Suite Bootstrap:** Installing any compatible `re*` platform engine (such as `reStation`) or `<Game>Recomp` title checks the installed `reEngine` version and silently installs or upgrades `reEngine` to at least `v0.1.3` if needed.
4. **Side-by-Side Platform Engines:** Installed engines reside side-by-side under `<InstallRoot>\engines\<engineId>\v<Version>\`. Existing tracks remain untouched when newer `MINOR` or `MAJOR` releases arrive, so older `.recomp` modules keep running reliably.

---

## 2. Launching `.recomp` Modules

1. **Register Double-Click Association:** Click **Register .recomp Association** in the manager window (or run `reEngine.exe --register-assoc`) to bind `.recomp` files under your per-user Windows Registry (`HKCU\Software\Classes`).
2. **Launch from Explorer or Terminal:** Double-click any `.recomp` cartridge or run `reEngine.exe --open <path-to-module.recomp> --wait` (positional file paths are also accepted, and relative paths resolve from your current working directory).
3. **Quickstart CLI Examples:**
   ```powershell
   reEngine.exe --open "D:\Games\KingsField\app\KingsFieldRecomp.recomp" --wait
   reEngine.exe ".\sample.recomp.json" --dry-run
   reEngine.exe --check-updates --strict
   reEngine.exe --register-assoc
   reEngine.exe --version
   ```
4. **Automatic Per-Module Isolation:** Each module receives its own storage folder under `<DataDir>\modules\<moduleId>\` containing an isolated `settings.json` and `logs\` directory so settings and logs never collide across games.
5. **CLI Options Reference:**

| CLI Option | Behavior |
| :--- | :--- |
| `--open <path>` | Load `.recomp` / `.recomp.json`, detect `data\rom\` media, resolve compatible engine, and launch. |
| `--wait` | Wait synchronously for child execution and propagate exact exit codes. |
| `--register-assoc` | Register per-user `.recomp` file association handler. |
| `--check-updates` (`--strict`) | Sweep all installed `(engineId, track)` engines; `--strict` returns Exit `4` if offline. |
| `--feed <urlOrPath>` | Override the update feed URL or local feed JSON path. |
| `--engines-dir <dir>` | Override the `engines\` root directory used for discovery. |
| `--data-dir <dir>` / `--config <path>` / `--log-dir <dir>` | Redirect runtime data, `settings.json`, and log outputs. |
| `--headless` / `--dry-run` | Run automated headless diagnostics or log planned child launch args without spawning. |
| `--enable-logs` / `--frames <n>` / `--fixture <path>` | Forward diagnostic logging, frame caps, or fixture overrides to the child engine. |
| `--dump-screenshot <path>` / `--dump-audio <path>` / `--input-script <path>` | Forward capture and deterministic input playback options to the child engine. |
| `--version` / `--help` | Print the active router version (`0.1.3`) or CLI usage summary. |

5. **Exit Code Reference:**
   - `0`: Success (or non-strict offline `--check-updates` warning).
   - `1`: General runtime failure or no installed engine satisfies the module's version and track.
   - `2`: Invalid CLI flag, malformed `.recomp` package, Zip-Slip archive error, or containment violation.
   - `4`: Offline/network error under `--check-updates --strict`.
   - `130`: Interrupted by `Ctrl+C` (`CancelKeyPress`); child processes are cleanly terminated.

---

## 3. Preloading Legal Optical Discs & ROMs (`data\rom\`)

`reEngine` never bundles copyrighted game discs, ROM images, or console BIOS files. Place your legally dumped personal media inside the game module's `data\rom\` folder to skip first-run media prompts:

- **Installed Modules:** Place dumps in `<GameInstallRoot>\data\rom\` (for example `<PROGRAMS_ROOT>\KingsFieldRecomp\data\rom\`). Cartridges packaged under `<GameInstallRoot>\app\<Module>.recomp` automatically resolve `data\rom\` from `<GameInstallRoot>\`.
- **Automatic Format Priority:** `reEngine` scans `data\rom\` recursively and picks the primary image automatically in this order:
  1. `.cue` (disc sheet)
  2. `.chd` (compressed disc archive)
  3. `.iso` (raw disc image)
  4. `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`
  5. `.7z`, `.zip`, `.img`
  6. `.bin`

---

## 4. Using the Suite Manager UI & Diagnostics

Launch `reEngine.exe` without CLI flags to open the Dark Slate suite manager window:

- **Header Status Pills:** Displays `ZERO-DAEMON`, association status (`REGISTERED` or `UNREGISTERED`), and update status (`UP TO DATE`, `CHECKING`, `PATCH STAGED`, `PATCH AVAILABLE`, `BREAKING AVAILABLE`, `OFFLINE / SKIPPED`, or `CHECK FAILED`).
- **Installed Platform Engines Table:** Lists every discovered `engineId`, installed SemVer, compatibility track (`Major.Minor.x`), and installation path.
- **Controls & Settings (`settings.json`):**
  - **Check for compatible updates on launch** (`shouldCheckUpdates`)
  - **Automatically stage compatible patches** (`shouldAutoStagePatches`)
  - **Open .recomp Package**, **Register .recomp Association**, **Check For Updates**, and **Save Settings**
- **Offline & Multi-Engine Sweeps:** Every distinct `(engineId, track)` pair is checked independently. Compatible track patches are downloaded and SHA-256 verified without touching older breaking tracks, while offline feeds log a `[WARN]` and show `OFFLINE / SKIPPED` with zero crash dumps.
- **Logs & Crash Diagnostics:** Telemetry logs write with `[v0.1.3] [PID:<id>/T:<tid>]` headers to `<LogDir>\reengine.log`, and unexpected failures record structured JSON crash bundles (including recent log lines) under `<LogDir>\crashes\crash_reengine_*.json`.

---

## 5. Installed Layout & Troubleshooting

### 5.1 Clean-Root Installation Layout
- `<InstallRoot>\reEngine.exe` — lightweight launcher entrypoint (zero background daemon).
- `<InstallRoot>\app\` — router binaries, `version.txt`, and `LICENSE` (`MPL-2.0`).
- `<InstallRoot>\engines\<engineId>\v<Version>\` — side-by-side platform engines (`reStation`, etc.).
- `<InstallRoot>\scripts\` — optional Windows shell association and smoke helper `.bat` scripts.
- `<DataDir>\settings.json` — user update preferences and remembered window dimensions (`.bak` self-healing).
- `<DataDir>\modules\<moduleId>\` — isolated per-game settings and logs.

### 5.2 Common Remedies
- **No Compatible Engine Found (Exit `1`):** Confirm an engine matching the module's `engineId`, `compatibleEngineTrack`, and minimum `engineVersion` exists under `<InstallRoot>\engines\` or pass `--engines-dir <path>`.
- **Package Rejected (Exit `2`):** Ensure the `.recomp` ZIP archive contains a root `module.recomp.json`, valid lowercase `moduleId`, and no rooted or `..` traversal paths.
- **Offline Update Failure (Exit `4`):** Occurs only when `--check-updates --strict` is passed while offline; omit `--strict` during normal interactive sessions so `reEngine` degrades safely to `OFFLINE / SKIPPED`.
- **Media Picker Still Appears:** Verify your personal disc/ROM dump sits directly inside `<GameInstallRoot>\data\rom\` with a supported extension (`.cue`, `.chd`, `.iso`, `.gcm`, `.rvz`, `.n64`, `.z64`, `.v64`, `.sfc`, `.smc`, `.nes`, `.gb`, `.gbc`, `.gba`, `.7z`, `.zip`, `.img`, `.bin`).
- **Corrupt or Missing Settings:** `reEngine` keeps an automatic backup alongside `<DataDir>\settings.json` (`settings.json.bak`) and restores valid defaults cleanly if manual edits produce invalid JSON.
- **Uninstalling `reEngine`:** Run the setup executable with `--uninstall --install-dir <InstallRoot>` to remove router files cleanly without deleting user game saves or preloaded media inside game directories.

### 5.3 Support & Diagnostics Checklist
- Run `reEngine.exe --version` to verify the active router build (`0.1.3`).
- Run `reEngine.exe --headless` to verify discovered side-by-side engines and `.recomp` association status in a single line.
- Attach `<LogDir>\reengine.log` and any matching `<LogDir>\crashes\crash_reengine_*.json` bundle when reporting issues.

---

## License & Legal

- **Router & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0**. A verbatim copy of `LICENSE` is included inside the installed `app\` directory (`app\LICENSE`), the source archive, and the SDK distribution.
- **Bundled Archiver Attribution (`7-Zip`):** Build-time archive packaging uses `7z.exe` and `7z.dll`, distributed under the **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause and BSD 2-clause licenses for LZFSE, ZSTD, and XXH64 decompression components; see `LICENSE-7ZIP.txt` in the source repository).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or console hardware manufacturer. All trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must supply their own legally obtained optical discs or cartridge dumps inside `data\rom\`.
