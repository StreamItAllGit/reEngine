# reEngine (`v0.2.0`)

- **Active Version:** `v0.2.0` (`2026-11-15`)
- **Lifecycle State:** `active` ([`active.state`](active.state))
- **Group / Path:** `<PROJECTS_ROOT>\Personal\reEngine\`
- **Logo Assets:** [`Dev\assets\reEngine.svg`](Dev/assets/reEngine.svg) | [`Dev\assets\reEngine.png`](Dev/assets/reEngine.png) | [`Dev\assets\reEngine.ico`](Dev/assets/reEngine.ico)

---

## 1. What It Is & Core Goal
`reEngine` is the lightweight, **zero-daemon top-level router, `.decomp` pipeline coordinator, and suite manager** (`0 MB` background RAM, `AllowAutoStart = false`) for static recompilation modules (`.recomp`), clean-room decompilation packages (`.decomp`), and on-demand per-architecture **4-Tier Recomp / Shared-IR RyuJIT / Clean-Room HLE** engines (`reStation`, `re64`, `reCube`, `rePS2`, `reAdvance`, plus optional `reNes`/`reSnes`/`reBoy` spawned on demand for `R1–R3` `.recomp` tracks; graduated `R4`/`R5` full native ports ship as standalone game EXEs/installers and do not use `reEngine`). Rather than chasing generic cycle-accurate emulation—which already lives in standalone emulators and `uStation`—the `reEngine` + `re*` suite delivers **native C# gameplay/interpolation hooks, ahead-of-time static `.recomp` translation, shared `<dataDir>\cache\` AOT assembly management, and runtime CoreCLR RyuJIT dynamic translation**, keeping interpretation strictly as a `[WARN]`-logged safety-net fallback.

`reEngine v0.2.0` installs per-user by default under `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\` (suite root `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\`) with peer `toolchain\` (optional offline Apache-2.0 headless Ghidra + `jlink` OpenJDK under `--profile Full`), `engines\re<Platform>\v<Ver>\`, and `games\` folders. Any `re*` or `<Game>Recomp` installer auto-bootstraps/updates `reEngine`, preserves user `data\rom\` media across upgrades, and lets double-clicked `.recomp`/`.decomp` files (or `uStation` delegations via `reEngine --open <pkg> --wait`) route straight to the compatible engine track—building toward **`v1.0.0` (`win-x64` Going Gold)** and **`v2.0.0` (Full 10-Platform Compatibility North Star)**.

---

## 2. Quickstart & CLI Flags

```powershell
reEngine-Setup-v0.2.0-win-x64.exe --silent --no-launch --profile Lightweight
reEngine.exe --open "D:\Games\KingsField\module.recomp.json" --wait
reEngine.exe decomp --module ".\sample-re64.decomp" --stage D3 --test-mode --data-dir .\Sandbox\data --log-dir .\Sandbox\logs
reEngine.exe --check-updates --strict
reEngine.exe --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs --test-mode
reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate `.recomp` manifest (rejecting non-clean-room `*.Generated.dll` / `*.g.cs`), resolve side-by-side `re*` engine, isolate child storage under `<dataDir>\modules\<moduleId>\`, check `data\rom\` preload, and launch. |
| `decomp --module <path>` | Parse `.decomp` package (`--stage D0..D5`, `--engine-dir`, `--toolchain-dir`, `--output-dir`), detect offline Ghidra + OpenJDK under `toolchain\`, and delegate to installed `re*` engine (returns Exit `3` with a 3-part remediation message if unbuilt). |
| `--wait` | Synchronously wait across root `reEngine.exe` and child engine execution (caller-CWD relative paths normalized; exit codes `0, 1, 2, 3, 4, 5, 6, 130` propagated). |
| `--register-assoc` | Register per-user `.recomp` and `.decomp` Windows shell file associations (`HKCU\Software\Classes`; virtualized in `--test-mode`). |
| `--profile <Lightweight\|Full>` / `--suite-dir` / `--reengine-dir` | Installer & bootstrap profile selection (compact router vs. `<= 160 MB` offline Ghidra + OpenJDK `toolchain\`) and peer suite directory overrides. |
| `--migrate-legacy` (`--confirm-legacy-cleanup`) | Opt-in migration helper for older pre-suite installs, preserving `data\rom\` files safely. |
| `--check-updates` (`--strict`) | Sweep all installed `(engineId, track)` engines side-by-side; offline socket drops log `[WARN]` (`Exit 0`, or `Exit 4` with `--strict`, `0` crash dumps). |
| `--data-dir` / `--config` / `--log-dir` | Redirect writable runtime data, `settings.json` (see `Dev\settings.example.json`), shared `<dataDir>\cache\` AOT index, and Standard 15 `[v0.2.0] [PID/T]` telemetry logs (`--test-mode` fail-closed). |
| `--headless` / `--visible-test` | Automated CI mode (`--headless`) or short dark-UI foreground showcase (`--visible-test`). |
| `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--input-script`, `--enable-logs` | Containment-checked runtime, diagnostic, and automated test-capture flags forwarded cleanly to resolved `re*` child engines. |

---

## 3. Architecture At a Glance
- **Application Archetype:** Zero-Daemon Suite Router, `.decomp` Orchestrator & Dark Slate Manager (`Tier A`, `net8.0` Core + `net8.0-windows` App/Launcher/Installer consuming `Foundation.Core`, `Foundation.Retro`, `Foundation.Launcher`, and `Foundation.Installer` from `Foundation v0.4.0`; `50/50` passing xUnit tests with `0` warnings at `v0.2.0`; migrating to a cross-platform `SDL3` shell across `v1.1.0–v2.0.0`).
- **4-Tier Recomp Routing & Sister-App Boundary:** Targets `.recomp` modules and clean-room `.decomp` pipelines first (plus developer `--jit-bringup` raw-disc seeding in `re*` engines). `uStation` never builds a competing recompiler and delegates `.recomp` launches through `reEngine.exe --open <pkg> --wait`.
- **Shared `<dataDir>\cache\` AOT Manager & Offline Toolchain:** `AotCacheManager` indexes compiled AOT assemblies inside `<dataDir>\cache\aot-index.json` with cross-process locking, SHA-256 tamper invalidation, and clean-room `.recomp` validation, while `OfflineToolchainLocator` + `Dev\scripts\stage_offline_toolchain.ps1` stage stripped Apache-2.0 headless Ghidra + `jlink` OpenJDK under `toolchain\` (`<= 160 MB`, zero runtime internet calls).
- **Engine Track Resolution, Isolation & Canonical Media Preload:** Matches `compatibleEngineTrack` and `>= engineVersion` across `engines\re<Platform>\v<Ver>\`, resolves `app\<Module>.recomp` parent `<InstallRoot>\data\rom\` via `RecompPackageReader.ResolveModuleRootDirectory`, isolates child `--data-dir`/`--config`/`--log-dir` inside `<dataDir>\modules\<moduleId>\`, and auto-detects gitignored user optical/ROM media in `<module>\data\rom\` before prompting media pickers.
- **Horizon (`v1.0.0` & `v2.0.0`):** `v1.0.0` certifies multi-engine `win-x64` routing across `>= 2` `re*` engines and `>= 2` `R1–R3` `.recomp` modules plus `uStation` synchronous delegation (decoupled from `R4` standalone ports and per-game overlay counts); `v2.0.0` delivers full-stack 10-platform routing (`win-x64`, `linux-x64`, `osx-arm64`, `steamos-deck`, `linux-bionic-arm64`, `ios-arm64`, `browser-wasm`, `switch`, `xbox`, `playstation`) with graceful Zero-RWX static module resolution on locked-down hosts.

---

## 4. Project Navigation & Living Docs
- **Feature Inventory & Code Map:** [`FEATURES.md`](FEATURES.md)
- **Milestones & Strategic Horizon:** [`ROADMAP.md`](ROADMAP.md)
- **Version History Ledger:** [`CHANGELOG.md`](CHANGELOG.md)
- **Implementation Plans (`@pland`):** [`Dev\docs\plans\`](Dev/docs/plans/)
- **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\`](Dev/docs/reports/)

---

## 5. Manual Shelf (`Dev\docs\`)
- [`ARCHITECTURE.md`](Dev/docs/ARCHITECTURE.md) | [`INTERFACES.md`](Dev/docs/INTERFACES.md) | [`USER_GUIDE.md`](Dev/docs/USER_GUIDE.md) | [`OPERATIONS.md`](Dev/docs/OPERATIONS.md)

---

## License & Legal

- **Router & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0** ([`LICENSE`](LICENSE)). Verbatim copies of `LICENSE` are distributed in the repository root, portable `Builds\v<Version>\app\LICENSE`, source archives, and `<PROJECTS_ROOT>\SharedLibs\reEngine\v<Version>\LICENSE`.
- **Bundled Third-Party Tooling Notices (`THIRDPARTY_LICENSES.txt` & `LICENSE-7ZIP.txt`):** Build-time archive packaging uses `7z.exe` and `7z.dll` ([`LICENSE-7ZIP.txt`](Dev/assets/native/LICENSE-7ZIP.txt)), licensed under **GNU LGPL-2.1+** with the **unRAR license restriction** (and BSD 3-clause / BSD 2-clause components for LZFSE, ZSTD, and XXH64). Optional `Full` installs distribute [`THIRDPARTY_LICENSES.txt`](Dev/scripts/THIRDPARTY_LICENSES.txt) covering stripped Apache-2.0 headless Ghidra (`GPL/` and `docs/` removed) and `jlink` OpenJDK (`GPLv2 + Classpath Exception`).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room software router and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or console hardware manufacturer. All product names and trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must legally supply their own optical discs or cartridge dumps inside each module's gitignored `data\rom\` preload directory.
