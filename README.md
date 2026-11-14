# reEngine (`v0.1.2`)

- **Active Version:** `v0.1.2` (`2026-11-14`)
- **Lifecycle State:** `active` ([`active.state`](<REPO_ROOT>/active.state))
- **Group / Path:** `<PROJECTS_ROOT>\Personal\reEngine\`
- **Logo Assets:** [`Dev\assets\reEngine.svg`](<REPO_ROOT>/Dev/assets/reEngine.svg) | [`Dev\assets\reEngine.png`](<REPO_ROOT>/Dev/assets/reEngine.png) | [`Dev\assets\reEngine.ico`](<REPO_ROOT>/Dev/assets/reEngine.ico)

---

## 1. What It Is & Core Goal
`reEngine` is the lightweight, **zero-daemon top-level router and suite manager** (`0 MB` background RAM, `AllowAutoStart = false`) for static recompilation modules (`.recomp`) and on-demand per-architecture **4-Tier Recomp / Shared-IR RyuJIT / Clean-Room HLE** engines (`reStation`, `re64`, `reCube`, `rePS2`, `reAdvance`, plus optional `reNes`/`reSnes`/`reBoy` spawned on demand for `R1–R3` `.recomp` tracks; graduated `R4`/`R5` full native ports ship as standalone game EXEs/installers and do not use `reEngine`). Rather than chasing generic cycle-accurate emulation—which already lives in standalone emulators and [`uStation`](<PROJECTS_ROOT>/Personal/uStation/README.md)—the `reEngine` + `re*` suite delivers **native C# gameplay/interpolation hooks, ahead-of-time static `.recomp` translation, and runtime CoreCLR RyuJIT dynamic translation**, keeping interpretation strictly as a `[WARN]`-logged safety-net fallback.

Any `re*` or `<Game>Recomp` installer auto-bootstraps/updates `reEngine`, installs platform engines side-by-side under `<PROGRAMS_ROOT>\reEngine\engines\re<Platform>\v<Ver>\`, and lets double-clicked `.recomp` files (or `uStation` delegations via `reEngine --open <pkg> --wait`) route straight to the exact compatible engine track—building toward **`v1.0.0` (`win-x64` Going Gold)** and **`v2.0.0` (Full 10-Platform Compatibility North Star)**.

---

## 2. Quickstart & CLI Flags

```powershell
reEngine.exe --open "D:\Games\KingsField\module.recomp.json" --wait
reEngine.exe --check-updates --strict
reEngine.exe --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs --test-mode
reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate `.recomp` manifest, resolve side-by-side `re*` engine, isolate child storage under `<dataDir>\modules\<moduleId>\`, check `data\rom\` preload, and launch. |
| `--wait` | Synchronously wait across root `reEngine.exe` and child engine execution (caller-CWD relative paths normalized; exit codes `0, 1, 2, 3, 4, 5, 6, 130` propagated). |
| `--register-assoc` | Register per-user `.recomp` Windows shell file association (`HKCU\Software\Classes`; virtualized in `--test-mode`). |
| `--check-updates` (`--strict`) | Sweep all installed `(engineId, track)` engines side-by-side; offline socket drops log `[WARN]` (`Exit 0`, or `Exit 4` with `--strict`, `0` crash dumps). |
| `--data-dir` / `--config` / `--log-dir` | Redirect writable runtime data, `settings.json`, and Standard 15 `[v0.1.2] [PID/T]` telemetry logs (`--test-mode` fail-closed). |
| `--headless` / `--visible-test` | Automated CI mode (`--headless`) or short dark-UI foreground showcase (`--visible-test`). |
| `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--input-script`, `--enable-logs` | Containment-checked runtime, diagnostic, and automated test-capture flags forwarded cleanly to resolved `re*` child engines. |

---

## 3. Architecture At a Glance
- **Application Archetype:** Zero-Daemon Suite Router & Dark Manager (`Tier A`, `net8.0` Core + `net8.0-windows` App/Launcher/Installer consuming `Foundation.Core`, `Foundation.Launcher`, and `Foundation.Installer`; `34/34` passing xUnit tests at `v0.1.2`; migrating to a cross-platform `SDL3` shell across `v1.1.0–v2.0.0`).
- **4-Tier Recomp Routing & Sister-App Boundary:** Targets `.recomp` modules first (plus developer `--jit-bringup` raw-disc seeding in `re*` engines). `uStation` never builds a competing recompiler and delegates `.recomp` launches through `reEngine.exe --open <pkg> --wait`.
- **Engine Track Resolution, Isolation & Canonical Media Preload:** Matches `compatibleEngineTrack` and `>= engineVersion` across `engines\re<Platform>\v<Ver>\`, resolves `app\<Module>.recomp` parent `<InstallRoot>\data\rom\` via `RecompPackageReader.ResolveModuleRootDirectory`, isolates child `--data-dir`/`--config`/`--log-dir` inside `<dataDir>\modules\<moduleId>\`, and auto-detects gitignored user optical/ROM media in `<module>\data\rom\` before prompting media pickers.
- **Horizon (`v1.0.0` & `v2.0.0`):** `v1.0.0` certifies multi-engine `win-x64` routing across `>= 2` `re*` engines and `>= 2` `R1–R3` `.recomp` modules plus `uStation` synchronous delegation (decoupled from `R4` standalone ports and per-game overlay counts); `v2.0.0` delivers full-stack 10-platform routing (`win-x64`, `linux-x64`, `osx-arm64`, `steamos-deck`, `linux-bionic-arm64`, `ios-arm64`, `browser-wasm`, `switch`, `xbox`, `playstation`) with graceful Zero-RWX static module resolution on locked-down hosts.

---

## 4. Project Navigation & Living Docs
- **Feature Inventory & Code Map:** [`FEATURES.md`](<REPO_ROOT>/FEATURES.md)
- **Milestones & Strategic Horizon:** [`ROADMAP.md`](<REPO_ROOT>/ROADMAP.md)
- **Version History Ledger:** [`CHANGELOG.md`](<REPO_ROOT>/CHANGELOG.md)
- **Implementation Plans (`@pland`):** [`Dev\docs\plans\`](<REPO_ROOT>/Dev/docs/plans/)
- **Diagnostic Reports (`@inspecter`):** [`Dev\docs\reports\`](<REPO_ROOT>/Dev/docs/reports/)

---

## 5. Manual Shelf (`Dev\docs\`)
- [`ARCHITECTURE.md`](<REPO_ROOT>/Dev/docs/ARCHITECTURE.md) | [`INTERFACES.md`](<REPO_ROOT>/Dev/docs/INTERFACES.md) | [`USER_GUIDE.md`](<REPO_ROOT>/Dev/docs/USER_GUIDE.md) | [`OPERATIONS.md`](<REPO_ROOT>/Dev/docs/OPERATIONS.md)
