# reEngine — User Guide

**Synced Version:** `0.1.1` (`2026-11-14`)  
**Owner:** `@tessa`

---

## 1. Launching `.recomp` Modules
1. Install `reEngine` (or any `re*` / `<Game>Recomp` installer, which automatically bootstraps/updates `reEngine` with zero background daemons and `AllowAutoStart = false`).
2. Double-click any `.recomp` file (or run `reEngine.exe --open <path>` / `reEngine.exe --open <path> --wait`). `reEngine` resolves caller-CWD relative paths automatically, locates the highest installed compatible engine track under `<PROGRAMS_ROOT>\reEngine\engines\`, isolates per-module child storage inside `<dataDir>\modules\<moduleId>\` (keeping each module's `settings.json` and `logs\` separate from `reEngine` itself), and propagates exact child exit codes (`0` success, `1` missing engine/runtime failure, `2` invalid package/containment violation, `4` strict update error, `130` Ctrl+C interrupt, or module exit codes `3`, `5`, `6`).
3. Supported runtime/test passthrough flags for `--open`: `--wait`, `--headless`, `--frames <n>`, `--fixture <path>`, `--dump-screenshot <path>`, `--dump-audio <path>`, `--input-script <path>`, `--enable-logs`, `--log-dir <dir>`, `--config <path>`, `--data-dir <dir>`.

## 2. Preloading Optical Discs / ROMs (`data\rom\`)
Place your legal `.iso`, `.bin`+`.cue`, `.chd`, `.7z`, or `.zip` dump inside the game module's `data\rom\` folder (`<PROGRAMS_ROOT>\<Game>Recomp\data\rom\` in installed modules).
- For isolated dev/QA verification, place test dumps under `<Game>Recomp\Sandbox\data\rom\` alongside `--test-mode`.
- When present, `reEngine` passes `--preload-media` automatically (prioritizing `.cue` > `.chd` > `.iso` > disc/ROM images > `.7z`/`.zip` > `.bin`) so compatible engines skip the first-run media picker.

## 3. Managing Engines & Updates
Launch `reEngine.exe` directly to inspect installed side-by-side engines, toggle **Check Updates on Launch** (`shouldCheckUpdates`) or **Auto-Stage Compatible Patches** (`shouldAutoStagePatches`), register `.recomp` associations (`--register-assoc`), or trigger manual update checks (`--check-updates`).
- **Multi-Engine Side-by-Side Sweep:** `reEngine` evaluates every distinct installed `(engineId, track)` pair rather than stopping on a newer track, and displays breaking `MINOR`/`MAJOR` updates as non-blocking alerts without overwriting older engine tracks.
- **Offline / Socket Resilience:** When offline or an update feed drops a connection, normal `--check-updates` runs log a `[WARN]` and return exit `0` (`OFFLINE / SKIPPED` pill in the UI, zero crash dumps); pass `--check-updates --strict` in automation/CI to return exit `4` on feed failure.
