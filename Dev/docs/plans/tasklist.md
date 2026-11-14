# reEngine — `v0.1.3` Tasklist

- **Target Version:** `v0.1.3` (Shipped: `v0.1.2`, Active Plan: [`0.1.3.2026-11-14.md`](<REPO_ROOT>/Dev/docs/plans/0.1.3.2026-11-14.md))
- **Detail Level:** `3` *(1 = Basic · 2 = Informative · 3 = Guided Hints · 4 = Full How-To)*
- **Last Synced:** `2026-11-14`

## Block 4 — `v0.1.3` Release Sanitizer Hardening, Legal Parity & Manual Shelf Expansion (`Shipped`) ✅
- [x] **T1 — Lockstep `v0.1.3` Bump, `app.manifest.json` Scrub & `AGENTS.md` Path Cleanup:**
  - **Targets:** `Dev\version.txt`, `Dev\Directory.Build.props`, `ReEngineBootstrapHelper.cs`, `active.state`, `Dev\app.manifest.json`, `AGENTS.md`
- [x] **T2 — Manual Shelf Expansion (`>= 85` Lines Each), `MPL-2.0`/7-Zip Legal Parity & Root Quartet Sync:**
  - **Targets:** `Dev\docs\{ARCHITECTURE,INTERFACES,USER_GUIDE,OPERATIONS}.md`, `README.md`, `FEATURES.md`, `CHANGELOG.md`
- [x] **T3 — Hardened `Dev\scripts\build_release.ps1` (`LICENSE` Bundling, Stale Build Cleanup & Fail-Fast Sanitizer):**
  - **Targets:** `Dev\scripts\build_release.ps1`
- [x] **T4 — Build/Test Gate, `@dean-2` Gate 2, Single-Pass Packaging & Live-View `Sandbox\` Verify:**
  - [x] Pass `dotnet test Dev\reEngine.slnx -c Release /warnaserror` (`34/34`, `0` warnings, `0` skips)
  - [x] `@dean-2` Gate 2 `PASS` + run `build_release.ps1 -Version 0.1.3` + `audit_installer.ps1` (`0` findings)
  - [x] `@inspecter` isolated `Sandbox\` + `--visible-test` verification & `@tessa-2` Dual-Track Git/GitHub/Docs closeout sync

## Block 3 — `v0.1.2` Parent-Root `app\*.recomp` Cartridge & `data\rom\` Resolution (`Shipped`) ✅
- [x] **T1 — Parent-Root `ResolveModuleRootDirectory` (`RecompPackageReader`):**
  - **Targets:** `RecompPackageManifest.cs`
  - **Why:** Ensure clean-root `<InstallRoot>\app\<Game>.recomp` resolves `ModuleRootDirectory` to `<InstallRoot>\` so `data\rom\` discovery stays strictly under `manifest.PreloadRomDir`.
- [x] **T2 — `v0.1.2` Lockstep Version Bump (`0.1.2`):**
  - **Targets:** `Dev\version.txt`, `Directory.Build.props`, `app.manifest.json`, `ReEngineBootstrapHelper.cs`
  - **Why:** Align SDK & installer metadata for `reStation v0.1.2` and `KingsFieldRecomp v0.1.4`.
- [x] **T3 — Unit Tests & Zero-Warning Suite (`34/34`):**
  - **Targets:** `ManifestAndResolverTests.cs` (`ParseRecompArchiveInAppFolder_ResolvesParentModuleRootAndStrictDataRomPreload`)
  - **Why:** Regression-lock parent-root resolution while ignoring `data\gamedata\`.
- [x] **T4 — `@dean` Gate 2, `Releases\v0.1.0\` Prune, Single-Pass `v0.1.2` Packaging & `Sandbox\` Verify:**
  - **Targets:** `build_release.ps1`, `SharedLibs\reEngine\v0.1.2\`, `Sandbox\`

## Block 2 — `v0.1.1` Lockstep CLI Sync, Offline Resilience & Isolation (`Shipped`) ✅
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
