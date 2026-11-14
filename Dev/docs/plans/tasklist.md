# reEngine — `v0.1.1` Tasklist

- **Target Version:** `v0.1.1` (Shipped: `v0.1.0`, Active Plan: [`v0.1.1.2026-11-13.md`](<REPO_ROOT>/Dev/docs/plans/v0.1.1.2026-11-13.md))
- **Detail Level:** `3` *(1 = Basic · 2 = Informative · 3 = Guided Hints · 4 = Full How-To)*
- **Last Synced:** `2026-11-14`

## Block 1 — Zero-Daemon Router & `.recomp` Associations (`v0.1.0` Shipped) ✅
- [x] **1.1 Initialized `reEngine` Tier A Workspace, `.recomp` Routing & `app.manifest.json` (`v0.1.0`):**
  - **Targets:** `Dev\`, `Dev\app.manifest.json`, `active.state`
  - **Why:** Establishes the zero-daemon (`0 MB` idle, `AllowAutoStart = false`) side-by-side platform engine router.

## Block 2 — `v0.1.1` Lockstep CLI Sync, Offline Resilience & Isolation (`Active PATCH+1`)
- [x] **T1 — Launcher Sync-Wait, CWD Path Normalization & `Foundation` Proposal Report:**
  - **Targets:** `reEngine.Launcher\LauncherArgsHelper.cs`, `reEngine.Launcher\Program.cs`, `Foundation\Dev\docs\reports\0.3.1.20261114-022857.-.rootlauncher-shouldwait-proposal.md`
  - **Why:** Propagate child exit codes `(0, 1, 2, 3, 4, 5, 6, 130)` synchronously and resolve caller-CWD relative paths without touching `Foundation\Dev\src\`.
- [x] **T2 — Multi-Engine Sweep, Offline `[WARN]` Resilience, Crash `recentLogs` & `[HEARTBEAT]` Hygiene:**
  - **Targets:** `reEngine.Core\Updates\UpdateModels.cs`, `UpdateCoordinator.cs`, `ReEngineTelemetryFormatter.cs`, `reEngine.App\Program.cs`, `ReEngineMainForm.cs`, `ReEngineMainForm.Layout.cs`
  - **Why:** Sweep every `(EngineId, Track)`, log offline/socket drops cleanly (`Exit 0` / `--strict 4`, `0` crash dumps), pass `SnapshotRecentLogs()` on fatal crashes, strip duplicate `[HEARTBEAT]`, and prevent UI pill clipping.
- [x] **T3 — Router CLI Passthrough & Per-Module `<dataDir>\modules\<moduleId>\` Isolation:**
  - **Targets:** `reEngine.App\RecompLaunchOptions.cs`, `RecompLaunchCoordinator.cs`, `Program.PrintUsage()`
  - **Why:** Forward `--preload-media`, `--wait`, `--headless`, `--visible-test`, `--enable-logs`, `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--input-script`, and isolate child `--data-dir`/`--config`/`--log-dir`.
- [x] **T4 — Version Sync (`0.1.1`), Doc-Bleed Cleanup, Alias Fix & Synthetic xUnit Tests:**
  - **Targets:** `Dev\version.txt`, `Directory.Build.props`, `ReEngineBootstrapHelper.cs`, `app.manifest.json`, `ARCHITECTURE.md`, `INTERFACES.md`, `CHANGELOG.md`, `ROADMAP.md`, `reEngine.Tests\`
  - **Why:** Keep `0.1.1` lockstep clean, replace banned `"alsoSupportsTracks"`, and lock deterministic test coverage with `0` warnings.
- [x] **T5 — `@dean` Gate 2, Single-Pass `v0.1.1` Packaging (`SharedLibs\reEngine\v0.1.1\`), `@inspecter-reengine` `Sandbox\` Verify & `@tessa` Closeout Sync:**
  - **Targets:** `build_release.ps1 -Version 0.1.1`, `SharedLibs\reEngine\v0.1.1\`, `Sandbox\`, `StreamItAllGit/reEngine`
  - **Why:** Package only after Gate 2 `PASS`, verify live `--visible-test` + `Sandbox\` probes, and sync Notion + private GitHub Release.

## Deferred Backlog (`v0.2.0` / `v0.3.0` — Strictly Out of `v0.1.1` Scope)
- [ ] **`v0.2.0` — Decouple `2s` Manifest Check from Resumable `.part` Staging & Atomic Apply (`staged_updates\` $\rightarrow$ `engines\`).**
- [ ] **`v0.2.0` — Runtime `"supportedEngineTracks"` Multi-Track Matcher in `EngineVersionResolver`.**
- [ ] **`v0.3.0` — `--jit-bringup` & 4-Backend (`opengl`/`vulkan`/`parallelCpu`/`software`) Routing + Standard 09 Pill/Drawer Library UI.**
