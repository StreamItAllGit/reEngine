# reEngine — User Guide

**Synced Version:** `0.1.0` (`2026-11-13`)  
**Owner:** `@tessa`

---

## 1. Launching `.recomp` Modules
1. Install `reEngine` (or any `re*` / `<Game>Recomp` installer, which automatically bootstraps/updates `reEngine`).
2. Double-click any `.recomp` file (or run `reEngine.exe --open <path>`). `reEngine` locates the highest installed compatible engine track in `<PROGRAMS_ROOT>\reEngine\engines\`.

## 2. Preloading Optical Discs / ROMs (`data\rom\`)
Place your legal `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, or `.zip` dump inside the game module's `data\rom\` folder (`<PROGRAMS_ROOT>\<Game>Recomp\data\rom\` or `Sandbox\data\rom\`). When present, `reEngine` passes the preloaded media automatically so compatible engines skip the first-run media picker.

## 3. Managing Engines & Updates
Launch `reEngine.exe` directly to inspect installed side-by-side engines, toggle **Check Updates on Launch** (`shouldCheckUpdates`) or **Auto-Stage Compatible Patches** (`shouldAutoStagePatches`), or trigger manual update checks. Breaking `MINOR`/`MAJOR` engine updates are shown as non-blocking notifications rather than overwriting older engine tracks.
