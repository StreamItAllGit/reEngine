# reEngine — `v0.2.0` Tasklist

- **Target Version:** `v0.2.0` (`Shipped`; Prior Shipped: `v0.1.3`; Active Plan: [`0.2.0.2026-11-14.md`](0.2.0.2026-11-14.md))
- **Detail Level:** `3` *(1 = Basic · 2 = Informative · 3 = Guided Hints · 4 = Full How-To)*
- **Last Synced:** `2026-11-15`

## Block 4 — `v0.1.3` Release Sanitizer Hardening, Legal Parity & Manual Shelf Expansion (`Shipped`) ✅
- [x] **T1 — Lockstep `v0.1.3` Bump, `app.manifest.json` Scrub & `AGENTS.md` Path Cleanup:**
  - **Targets:** `Dev\version.txt`, `Dev\Directory.Build.props`, `ReEngineBootstrapHelper.cs`, `active.state`, `Dev\app.manifest.json`, `AGENTS.md`
- [x] **T2 — Manual Shelf Expansion (`>= 85` Lines Each), `MPL-2.0`/7-Zip Legal Parity & Root Quartet Sync:**
  - **Targets:** `Dev\docs\{ARCHITECTURE,INTERFACES,USER_GUIDE,OPERATIONS}.md`, `README.md`, `FEATURES.md`, `CHANGELOG.md`
- [x] **T3 — Hardened `Dev\scripts\build_release.ps1` (`LICENSE` Bundling, Stale Build Cleanup & Fail-Fast Sanitizer):**
  - **Targets:** `Dev\scripts\build_release.ps1`
- [x] **T4 — Build/Test Gate, `@dean-2` Gate 2, Single-Pass Packaging & Release Verify:**
  - [x] Pass `dotnet test Dev\reEngine.slnx -c Release /warnaserror` (`34/34`, `0` warnings, `0` skips)
  - [x] `@dean-2` Gate 2 `PASS` + run `build_release.ps1 -Version 0.1.3` + `audit_installer.ps1` (`0` findings)
  - [x] `@inspecter` isolated `APP_ISOLATED_TEST=1` verification & `@tessa-2` Dual-Track Git/GitHub/Docs closeout sync

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
- [x] **T4 — `@dean` Gate 2, `Releases\v0.1.0\` Prune, Single-Pass `v0.1.2` Packaging & Unit Verify:**
  - **Targets:** `build_release.ps1`, `SharedLibs\reEngine\v0.1.2\`, `Dev\tests\`

## Block 2 — `v0.1.1` Lockstep CLI Sync, Offline Resilience & Isolation (`Shipped`) ✅
- [x] **T1 — Launcher Sync-Wait, CWD Path Normalization & `Foundation` Proposal Report:**
  - **Targets:** `reEngine.Launcher\LauncherArgsHelper.cs`, `reEngine.Launcher\Program.cs`, `Foundation\Dev\docs\reports\0.3.1.20261114-022857.-.rootlauncher-shouldwait-proposal.md`
  - **Why:** Propagate child exit codes `(0, 1, 2, 3, 4, 5, 6, 130)` synchronously and resolve caller-CWD relative paths without touching `Foundation\Dev\src\`.
- [x] **T2 — Multi-Engine Sweep, Offline `[WARN]` Resilience, Crash `recentLogs` & `[HEARTBEAT]` Hygiene:**
  - **Targets:** `reEngine.Core\Updates\UpdateModels.cs`, `UpdateCoordinator.cs`, `ReEngineTelemetryFormatter.cs`, `reEngine.App\Program.cs`, `ReEngineMainForm.cs`, `ReEngineMainForm.Layout.cs`
  - **Why:** Sweep every `(EngineId, Track)`, log offline/socket drops cleanly (`Exit 0` / `--strict 4`, `0` crash dumps), pass `SnapshotRecentLogs()` on fatal crashes, strip duplicate `[HEARTBEAT]`, and prevent UI pill clipping.
- [x] **T3 — Router CLI Passthrough & Per-Module `<dataDir>\modules\<moduleId>\` Isolation:**
  - **Targets:** `reEngine.App\RecompLaunchOptions.cs`, `RecompLaunchCoordinator.cs`, `Program.PrintUsage()`
  - **Why:** Forward `--preload-media`, `--wait`, `--tray-only`, `--enable-logs`, `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, and isolate child `--data-dir`/`--config`/`--log-dir`.
- [x] **T4 — Version Sync (`0.1.1`), Doc-Bleed Cleanup, Alias Fix & Synthetic xUnit Tests:**
  - **Targets:** `Dev\version.txt`, `Directory.Build.props`, `ReEngineBootstrapHelper.cs`, `app.manifest.json`, `ARCHITECTURE.md`, `INTERFACES.md`, `CHANGELOG.md`, `ROADMAP.md`, `reEngine.Tests\`
  - **Why:** Keep `0.1.1` lockstep clean, replace banned `"alsoSupportsTracks"`, and lock deterministic test coverage with `0` warnings.
- [x] **T5 — `@dean` Gate 2, Single-Pass `v0.1.1` Packaging (`SharedLibs\reEngine\v0.1.1\`), `@inspecter-reengine` Release Verify & `@tessa` Closeout Sync:**
  - **Targets:** `build_release.ps1 -Version 0.1.1`, `SharedLibs\reEngine\v0.1.1\`, `StreamItAllGit/reEngine`
  - **Why:** Package only after Gate 2 `PASS`, verify release probes, and sync Notion + private GitHub Release.

## Block 5 — `v0.2.0` Platform-Agnostic `.decomp` Router, Offline Ghidra, Install Profile & Public Readiness (`Shipped`) ✅
- **Target Version:** `0.2.0` (Plan: [`0.2.0.2026-11-14.md`](0.2.0.2026-11-14.md) | Spoke: [`v0.2.0-decomp-and-toolchain.md`](../architecture/v0.2.0-decomp-and-toolchain.md))
- **Upstream Gates:** Wave 0 (`_DevBible v1.5.0`) and Wave 1 (`Foundation v0.4.0`) cleared; `@dean` Gate 1 locked (`2026-11-14`).
- [x] **RE-201 (`T1`, Wave 2A):** Define `DecompPackageManifest`, `IDecompPlatformProvider`, `AotCacheManifest`, and HKCU `.decomp` association contract (Std 18 keys post-Wave 0 `/define`).
- [x] **RE-202 (`T2`, Wave 2B):** Package Apache-2.0 CLI Ghidra (`GPL/` + `docs/` + Swing stripped, `<= 160 MB`) + `jlink` OpenJDK (`GPLv2+CE`) + `THIRDPARTY_LICENSES.txt` for offline `Full` installs under `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\toolchain\`.
- [x] **RE-203 (`T3`, Wave 2B):** Implement `DecompPipelineCoordinator` (`D0`–`D5` delegation) and deterministic 3-part error for unbuilt platform engines (`re64`, `reGenesis`).
- [x] **RE-204 (`T4`, Wave 2B):** Implement `<dataDir>\cache\` `AotCacheManager` + clean-room `.recomp` guard, and publish immutable `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.2.0\`.
- [x] **RE-205 (`T5`–`T7`, Wave 2B):** Adopt `Foundation v0.4.0` `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\`, `Lightweight`/`Full` installer profile, opt-in `--migrate-legacy`, `AllowAutoStart = false`, and `reEngine.App` `.decomp`/cache UI.
- [x] **RE-206 (`T8`, Wave 2C):** Archive `Archive\pre-scrub-v0.2.0.7z`, scrub host PII/paths via `git-filter-repo`, run `dotnet test -c Release /warnaserror`, isolated CLI checks (`.decomp` 3-part error), and `build_release.ps1` *(Gate 2 passed; Phase 3–5 closeout complete)*.

## Deferred Backlog (`v0.3.0` — Strictly Out of `v0.2.0` Scope)
- [ ] **`v0.3.0` — Decouple `2s` Manifest Check from Resumable `.part` Staging & Atomic Apply (`staged_updates\` $\rightarrow$ `engines\`).**
- [ ] **`v0.3.0` — Runtime `"supportedEngineTracks"` Multi-Track Matcher in `EngineVersionResolver`.**
- [ ] **`v0.3.0` — `--jit-bringup` & 4-Backend (`opengl`/`vulkan`/`parallelCpu`/`software`) Routing + Standard 09 Pill/Drawer Library UI.**
