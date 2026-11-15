# reEngine — Operations Runbook

**Synced Version:** `0.2.1` (`2026-11-15`)  
**Owner:** `@tessa` / `@inspecter` / `@devv`  
**Boundaries:** Zero unconfirmed mutations to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` or legacy `<LEGACY_PROGRAMS_DIR>\reEngine\`; `Foundation\`, `_DevBible\`, and `<WORKSPACE_ROOT>\Research\*` strictly read-only.

---

## 1. Build, Zero-Warning Test Gate & Pre-Flight Checks

1. **Release Build & Full xUnit Suite (`0` Warnings, `0` Skips):**
   ```powershell
   dotnet test Dev\reEngine.slnx -c Release -p:TreatWarningsAsErrors=true
   ```
   Confirm `53+` deterministic tests pass across `ManifestAndResolverTests`, `DecompAndToolchainTests`, `AotCacheManagerTests`, `InstallerAndAppStatusTests`, `InstallerWizardAutoSortTests`, `AssociationUpdateAndContainmentTests`, `LauncherAndLaunchPlanTests`, and `UpdateSweepAndTelemetryTests`.
2. **Code Size, Manual Shelf & PII Pre-Flight:**
   - Verify every `.cs` file and `.ps1` script stays `<= 400` code-only lines (`<= 270` target).
   - Verify `AGENTS.md` stays `<= 25` lines and `< 1,600 B` with `0` `$env:USERNAME` or PII hits.
   - Verify `ARCHITECTURE.md` has all 10 mandatory sections + Mermaid diagrams and all 4 Manual Shelf docs are current for `v0.2.1`.
   - Verify `Dev\app.manifest.json` uses workspace-relative `downstreamProjects` slugs (`Personal/reStation`, `Personal/KingsFieldRecomp`, `Personal/HMBackToNature-Recomp`) and `Dev\settings.example.json` uses `"StreamItAll\\reEngine\\reEngine"` (`reEngineInstallDir`, `umbrellaSubpath`).
3. **Gate 2 Clearance:** Post a Touched-Files + Line-Range Manifest, exact test count, warning count, and LOC check for `@dean` Gate 2 `PASS` **before** running packaging.

---

## 2. Offline Toolchain Staging & Single-Pass Release Packaging

### 2.1 Offline Headless Toolchain Staging (`<= 160 MB`, Zero GPL Leak)
Run [`Dev\scripts\stage_offline_toolchain.ps1`](../scripts/stage_offline_toolchain.ps1) when preparing or validating a `Full` profile `toolchain\` bundle:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\stage_offline_toolchain.ps1 `
  -GhidraSourceDir "<LocalGhidraRoot>" `
  -JdkSourceDir "<LocalJlinkJdkRoot>" `
  -OutputDir "Builds\toolchain" `
  -MaxBudgetMB 160
```
- Strips `ghidra\GPL\`, `ghidra\docs\`, Swing GUI / `server\` / `Extensions\`, copies [`Dev\scripts\THIRDPARTY_LICENSES.txt`](../scripts/THIRDPARTY_LICENSES.txt), and enforces `-ValidateOnly` / `<= 160 MB` checks.

### 2.2 Single-Pass Release Build & SDK Drop
After `@dean` posts Gate 2 `PASS`, run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_release.ps1 -Version 0.2.1
```

Pipeline stages executed by `build_release.ps1`:
1. **`[1/7]` Test Gate:** Runs `dotnet test Dev\reEngine.slnx -c Release /warnaserror` (skippable only with `-SkipTests`).
2. **`[2/7]` Portable Staging & Hygiene:**
   - Publishes `reEngine.App` (`Builds\v0.2.1\app\`) and single-file `reEngine.Launcher` (`Builds\v0.2.1\reEngine.exe`).
   - Writes `app\version.txt` (`0.2.1`), copies root `LICENSE` to `Builds\v0.2.1\app\LICENSE`, copies `Dev\settings.example.json` and `Dev\scripts\THIRDPARTY_LICENSES.txt` into `Builds\v0.2.1\`, copies `.bat` helpers, strips `.pdb`/`.xml`, and writes runtime `AGENTS.md` referencing peer `..\engines\` and `..\toolchain\`.
   - Authenticode-signs both `app\reEngine.exe` and `reEngine.exe`.
   - Retains at most `2` newest `Builds\v*` staging directories.
3. **`[3/7]` Pre-Package Scan:** Fails if any `.pdb`, `.db`, `.sqlite`, `.sqlite3`, `.log`, `.env`, or `*secret*.json` sits in `Builds\v0.2.1\`.
4. **`[4/7]` Single-EXE Installer & Audit:**
   - Zips `Builds\v0.2.1\` into `payload.zip` and publishes self-contained `reEngine.Installer` (`AllowAutoStart = false`, supporting `--profile Lightweight|Full`, `--suite-dir`, `--reengine-dir`, `--migrate-legacy`, `--confirm-legacy-cleanup`).
   - Authenticode-signs `Installers\reEngine-Setup-v0.2.1-win-x64.exe` and mirrors to `<INSTALLERS_MIRROR>\`.
   - Runs `Sanitize-MarkdownForEndUser` on `Installers\RELEASE_NOTES.md`.
   - Audits via `audit_installer.ps1` (`0` findings):
     ```powershell
     powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.gemini\config\skills\dev\scripts\audit_installer.ps1" `
       -InstallerPath Installers `
       -StagingDir Builds\v0.2.1 `
       -ExpectedVersion 0.2.1
     ```
5. **`[5/7]` Sanitized 6-File `Releases\` Bundles:**
   - Compiles `Dev\docs\USER_GUIDE.md` (`-> README.md`), `FEATURES.md`, `RELEASE_NOTES.md`, and `CHANGELOG.md` using UTF-8 (no BOM), runs `Sanitize-MarkdownForEndUser` and `Assert-NoEndUserLeaks`, generates `SHA256SUMS.txt` and `-mx=9` `.7z`, and retains max `2` unpacked `Releases\v*` alongside `Releases\Latest\`.
6. **`[6/7]` Source Archive:** Packs `Archive\reEngine-v0.2.1-src.7z` including `Dev`, Root Quartet, `AGENTS.md`, `LICENSE`, and `active.state` (excluding `bin/`, `obj/`, `*.user`, OS junk).
7. **`[7/7]` SharedLibs SDK Drop:** Copies `reEngine.Core.{dll,xml,deps.json}`, `LICENSE`, and `reEngine-Setup-v0.2.1-win-x64.exe` into `<SHARED_LIBS>\reEngine\v0.2.1\` and `Latest\`, recording all 5 artifacts in `sdk-manifest.json`.

---

## 3. Isolated `Sandbox\` & `--visible-test` Verification (`@inspecter`)

All runtime, installer, and GUI QA runs strictly inside `Sandbox\` (zero mutations to `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` or live `HKCU`):

1. **Silent Peer-Suite Installer (`--suite-dir` / `--reengine-dir` / `--profile`):**
   ```powershell
   Installers\reEngine-Setup-v0.2.1-win-x64.exe --silent --test-mode --suite-dir Sandbox\suite --profile Lightweight --data-dir Sandbox\data --log-dir Sandbox\logs --no-shortcuts --no-launch
   Test-Path Sandbox\suite\reEngine\app\LICENSE
   Test-Path Sandbox\suite\reEngine\settings.example.json
   Test-Path Sandbox\suite\reEngine\THIRDPARTY_LICENSES.txt
   Test-Path Sandbox\suite\reEngine\data\settings.json
   ```
   Confirm `Sandbox\suite\reEngine\data\settings.json` records `"installProfile": "lightweight"`, `"suiteInstallDir"`, and `"reEngineInstallDir"`. Also verify `--reengine-dir` override and opt-in `--migrate-legacy` against isolated `Sandbox\legacy-programs\reEngine\` test fixtures.
2. **CLI, Virtual Registry, `.decomp` Exit `3`, AOT Cache & Containment Probes:**
   - `.\Sandbox\suite\reEngine\reEngine.exe --version` -> prints `0.2.1`, Exit `0`.
   - `.\Sandbox\suite\reEngine\reEngine.exe --test-mode --headless --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` -> prints `Headless sweep OK | testMode=True | profile=... | toolchain=... | aotAssemblies=... | engines=... | assoc=...`, Exit `0`.
   - `.\Sandbox\suite\reEngine\reEngine.exe --test-mode --headless --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` -> writes both `.recomp` and `.decomp` entries into `Sandbox\data\virtual_registry.json`.
   - `.\Sandbox\suite\reEngine\reEngine.exe decomp --module Dev\tests\reEngine.Tests\Fixtures\sample-re64.decomp --test-mode --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` -> emits deterministic 3-part unbuilt-engine diagnostic and returns Exit `3`.
   - Offline `--check-updates` (`Exit 0`, `[WARN]`, `0` crash dumps) vs `--check-updates --strict` (`Exit 4`), plus Zip-Slip/escape containment (`Exit 2`).
3. **Foreground `--visible-test` Live-View QA:**
   - Run `Installers\reEngine-Setup-v0.2.1-win-x64.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs`.
   - Run `.\Sandbox\suite\reEngine\reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs`.
   - Inspect PNGs under `Sandbox\logs\screenshots\` via `read_file` to verify Dark Slate `#38BDF8` layout, `DECOMP: FULL`/`ROUTER` & `ZERO-DAEMON` pills, `Profile:` dropdown, AOT cache summary, tooltips, and zero clipped controls.

---

## 4. 3-Tier Log Audit, Downstream Lockstep & Private GitHub Sync

- **3-Tier Log Sweep:**
  - `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\logs\` (read-only production audit — never write dev/test outputs here).
  - `Dev\logs\` (build/test-run traces when generated locally).
  - `Sandbox\logs\` (`reengine.log`, `crashes\crash_reengine_*.json`, `screenshots\reengine_visible_test.png`).
  - Confirm `[v0.2.1] [PID:<id>/T:<tid>] [<LEVEL>]` formatting, single `[HEARTBEAT]` tags, and populated `"recentLogs"` arrays on simulated crash bundles.
- **Downstream Consumer Lockstep:**
  - Wave 3 (`reStation v0.2.4`, `reNes v0.1.0`) consumes `<SHARED_LIBS>\reEngine\v0.2.1\` (`reEngine.Core.{dll,xml,deps.json}`, `LICENSE`, `sdk-manifest.json`) and `reEngine-Setup-v0.2.1-win-x64.exe`.
  - Wave 4 (`KingsFieldRecomp v0.2.0-hf.2`, `HMBackToNature-Recomp v0.2.0`, `MetalGearRecomp v0.1.0`) installs into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\<GameRecomp>\` (or auto-sorted `<parent>\<Game>\`).
- **Outer Git & Private GitHub Release (`@tessa`):**
  - Recursively scrub any `desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` before any `git` command.
  - Verify `gh repo view StreamItAllGit/reEngine --json isPrivate` returns `true`, commit tracked files on `main`, create annotated tag `v0.2.1`, push branch + tag to `origin`, upload release artifacts (`Installers\reEngine-Setup-v0.2.1-win-x64.exe`, `Releases\Latest.7z`, `Releases\Latest\SHA256SUMS.txt`), and sync the Notion project hub + 4 Manual subpages.
