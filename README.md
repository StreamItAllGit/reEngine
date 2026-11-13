# reEngine (`v0.1.0`)

- **Active Version:** `v0.1.0` (`2026-11-13`)
- **Lifecycle State:** `active` ([`active.state`](<REPO_ROOT>/active.state))
- **Group / Path:** `<PROJECTS_ROOT>\Personal\reEngine\`
- **Logo Assets:** [`Dev\assets\reEngine.svg`](<REPO_ROOT>/Dev/assets/reEngine.svg) | [`Dev\assets\reEngine.png`](<REPO_ROOT>/Dev/assets/reEngine.png) | [`Dev\assets\reEngine.ico`](<REPO_ROOT>/Dev/assets/reEngine.ico)

---

## 1. What It Is & Core Goal
`reEngine` is the lightweight, **zero-daemon top-level router and suite manager** (`0 MB` background RAM, `AllowAutoStart = false`) for static recompilation modules (`.recomp`) and platform engines (`reStation`, `re64`, `reCube`, etc.). Any `re*` or `<Game>Recomp` installer auto-bootstraps/updates `reEngine`, installs platform engines side-by-side under `<PROGRAMS_ROOT>\reEngine\engines\re<Platform>\v<Ver>\`, and lets double-clicked `.recomp` files route directly to the exact compatible engine track without duplicating platform backends across games.

---

## 2. Quickstart & CLI Flags

```powershell
reEngine.exe --open "D:\Games\KingsField\module.recomp.json"
reEngine.exe --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs --test-mode
reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate `.recomp` manifest, resolve side-by-side `re*` engine, check `data\rom\` preload, and launch. |
| `--register-assoc` | Register per-user `.recomp` Windows shell file association (`HKCU\Software\Classes`; virtualized in `--test-mode`). |
| `--check-updates` | Run non-blocking launch-time update check and stage compatible patch updates per user settings. |
| `--data-dir` / `--config` / `--log-dir` | Redirect writable runtime data, `settings.json`, and telemetry logs (`--test-mode` fail-closed). |
| `--headless` / `--visible-test` | Automated CI mode (`--headless`) or short dark-UI foreground showcase (`--visible-test`). |

---

## 3. Architecture At a Glance
- **Application Archetype:** Zero-Daemon Suite Router & WinForms Dark Manager (`Tier A`, `net8.0` Core + `net8.0-windows` App/Installer consuming `Foundation.Core` & `Foundation.Installer`).
- **Engine Track Resolution:** Matches `compatibleEngineTrack` and `>= engineVersion` across `engines\re<Platform>\v<Ver>\`, preserving older tracks safely alongside newer breaking releases.
- **Canonical Media Preload:** Auto-detects gitignored user optical/ROM files inside `<module>\data\rom\` before invoking media pickers.

---

## 4. Project Navigation & Living Docs
- **Feature Inventory & Code Map:** [`FEATURES.md`](<REPO_ROOT>/FEATURES.md)
- **Milestones & Strategic Horizon:** [`ROADMAP.md`](<REPO_ROOT>/ROADMAP.md)
- **Version History Ledger:** [`CHANGELOG.md`](<REPO_ROOT>/CHANGELOG.md)
- **Plans (`@pland`):** [`Dev\docs\plans\`](<REPO_ROOT>/Dev/docs/plans/)

---

## 5. Manual Shelf (`Dev\docs\`)
- [`ARCHITECTURE.md`](<REPO_ROOT>/Dev/docs/ARCHITECTURE.md) | [`INTERFACES.md`](<REPO_ROOT>/Dev/docs/INTERFACES.md) | [`USER_GUIDE.md`](<REPO_ROOT>/Dev/docs/USER_GUIDE.md) | [`OPERATIONS.md`](<REPO_ROOT>/Dev/docs/OPERATIONS.md)
