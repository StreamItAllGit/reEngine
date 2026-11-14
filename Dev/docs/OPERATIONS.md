# reEngine — Operations Runbook

**Synced Version:** `0.1.3` (`2026-11-14`)  
**Owner:** `@tessa` / `@inspecter` / `@devv`  
**Boundaries:** Zero unconfirmed mutations to `<PROGRAMS_ROOT>\reEngine\`; `Foundation\`, `_DevBible\`, and `<PROJECTS_ROOT>\Research\*` strictly read-only.

---

## 1. Build, Zero-Warning Test Gate & Pre-Flight Checks

1. **Release Build & Full xUnit Suite (`0` Warnings, `0` Skips):**
   ```powershell
   dotnet test <PROJECTS_ROOT>\Personal\reEngine\Dev\reEngine.slnx -c Release /warnaserror
   ```
   Confirm `34/34`+ tests pass across `ManifestAndResolverTests`, `AssociationUpdateAndContainmentTests`, `LauncherAndLaunchPlanTests`, and `UpdateSweepAndTelemetryTests`.
2. **Code Size, Manual Shelf & PII Pre-Flight:**
   - Verify every `.cs` file and `.ps1` script stays `<= 400` code-only lines.
   - Verify `AGENTS.md` stays `<= 25` lines and `< 1,600 B` with `0` `$env:USERNAME` hits.
   - Verify all 4 Manual Shelf docs (`ARCHITECTURE.md`, `INTERFACES.md`, `USER_GUIDE.md`, `OPERATIONS.md`) are `>= 85` lines.
   - Verify `Dev\app.manifest.json` uses clean workspace-relative `downstreamProjects` slugs (`Personal/reStation`, `Personal/KingsFieldRecomp`, `Personal/HMBackToNature-Recomp`).
3. **Gate 2 Clearance:** Post a Touched-Files + Line-Range Manifest, exact test count, warning count, and LOC check for `@dean-2` Gate 2 `PASS` **before** running packaging.

---

## 2. Single-Pass Release Packaging, Sanitization & SDK Drop

After `@dean-2` posts Gate 2 `PASS`, run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File <PROJECTS_ROOT>\Personal\reEngine\Dev\scripts\build_release.ps1 -Version 0.1.3
```

Pipeline stages executed by `build_release.ps1`:
1. **`[1/7]` Test Gate:** Runs `dotnet test Dev\reEngine.slnx -c Release /warnaserror` (skippable only with `-SkipTests`).
2. **`[2/7]` Portable Staging & Hygiene:**
   - Publishes `reEngine.App` (`Builds\v0.1.3\app\`) and single-file `reEngine.Launcher` (`Builds\v0.1.3\reEngine.exe`).
   - Writes `app\version.txt` (`0.1.3`), copies root `LICENSE` to `Builds\v0.1.3\app\LICENSE`, copies `.bat` helpers, strips `.pdb`/`.xml`, and writes clean-root runtime `AGENTS.md`.
   - Authenticode-signs both `app\reEngine.exe` and `reEngine.exe`.
   - Prunes older `Builds\v*` staging directories past max `2` (removes stale `v0.1.0` and `v0.1.1`; retains `v0.1.2` + `v0.1.3`).
3. **`[3/7]` Pre-Package Scan:** Fails if any `.pdb`, `.db`, `.sqlite`, `.sqlite3`, `.log`, `.env`, or `*secret*.json` sits in `Builds\v0.1.3\`.
4. **`[4/7]` Single-EXE Installer & Audit:**
   - Zips `Builds\v0.1.3\` into `payload.zip` and publishes self-contained `reEngine.Installer` (`AllowAutoStart = false`).
   - Authenticode-signs `Installers\reEngine-Setup-v0.1.3-win-x64.exe` and mirrors to `<PROJECTS_ROOT>\Installers\`.
   - Passes `$rawReleaseNotes` through `Sanitize-MarkdownForEndUser` before writing `Installers\RELEASE_NOTES.md`.
   - Runs `audit_installer.ps1` and requires `0` findings:
     ```powershell
     powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\.gemini\config\skills\release-packaging\scripts\audit_installer.ps1" `
       -InstallerPath <PROJECTS_ROOT>\Personal\reEngine\Installers `
       -StagingDir <PROJECTS_ROOT>\Personal\reEngine\Builds\v0.1.3 `
       -ExpectedVersion 0.1.3
     ```
5. **`[5/7]` Sanitized 6-File `Releases\` Bundles:**
   - Reads `Dev\docs\USER_GUIDE.md` (`-> README.md`), `FEATURES.md`, `RELEASE_NOTES.md`, and `CHANGELOG.md` using explicit UTF-8 encoding (`UTF8Encoding($false)`) to prevent PowerShell 5.1 `Â§` / `â€”` mojibake.
   - Runs `Sanitize-MarkdownForEndUser` across all 4 Markdown files (unwrapping `file:///` links, stripping `@handles` and `Standard \d+`, dropping internal path/flag lines) and runs `Assert-NoEndUserLeaks` fail-fast verification across `Releases\{v0.1.3,Latest}\*.md`.
   - Generates `SHA256SUMS.txt`, builds `-mx=9` `.7z` archives, and prunes unpacked `Releases\v*` past max `2`.
6. **`[6/7]` Source Archive:** Packs `Archive\reEngine-v0.1.3-src.7z` including `Dev`, Root Quartet, `AGENTS.md`, `LICENSE`, and `active.state` (excluding `bin/`, `obj/`, `*.user`).
7. **`[7/7]` SharedLibs SDK Drop:** Copies `reEngine.Core.{dll,xml,deps.json}`, `LICENSE`, and `reEngine-Setup-v0.1.3-win-x64.exe` into `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.3\` and `Latest\`, recording all 5 artifacts in `sdk-manifest.json`.

### 2.1 End-User Markdown Sanitization & Retention Rules
- **Explicit UTF-8 Read/Write:** Reads source `.md` inputs using `[System.Text.UTF8Encoding]::new($false)` so PowerShell 5.1 never emits `Â§` or `â€”` mojibake.
- **Inline Scrubbing:** Unwraps `[label](file:///[^)]+)` to `$1`, strips bare `file:///\S+`, strips agent `@handles`, and strips `Standard \d+` / `§` citations before filtering internal paths (`Dev\src`, `Dev\tests`, `Dev\docs`, `Dev\scripts`, `Dev\lib`, `Dev\assets`, `Sandbox\`, `SharedLibs\`, `--test-mode`, `--visible-test`, `audit_installer.ps1`, `[CD]:\(Projects|Users)`, `_DevBible`, `$env:USERNAME`).
- **Fail-Fast Guard (`Assert-NoEndUserLeaks`):** Scans `Installers\RELEASE_NOTES.md`, `Releases\v<Version>\*.md`, and `Releases\Latest\*.md`, throwing immediately if any prohibited token survives.
- **Max-2 Retention:** Retains at most `2` newest `Builds\v*` staging folders and `2` newest unpacked `Releases\v*` folders (alongside `Releases\Latest\`) while keeping all `Releases\v*.7z` and `Installers\reEngine-Setup-v*.exe` history.
- **Installer Notes Parity:** Generates `Installers\RELEASE_NOTES.md` from sanitized `$endUserReleaseNotes` before running `audit_installer.ps1` so both `Installers\` and `Releases\` stay leak-free.
- **Deterministic Checksums:** Computes lowercase SHA-256 digests (`SHA256SUMS.txt` in ASCII) across the installer EXE and all 4 sanitized Markdown files in both `Releases\v<Version>\` and `Releases\Latest\`.
- **SDK Manifest Tracking:** Records `reEngine.Core.dll`, `reEngine.Core.xml`, `reEngine.Core.deps.json`, `LICENSE`, and `reEngine-Setup-v<Version>-win-x64.exe` inside `sdk-manifest.json` (`artifacts[]`).
- **Host Deployment Gate:** Never overwrite canonical `<PROGRAMS_ROOT>\reEngine\` during automated packaging or `Sandbox\` verification; production deployment remains human-gated.

---

## 3. Isolated `Sandbox\` & `--visible-test` Verification (`@inspecter`)

All runtime, installer, and GUI QA runs strictly inside `<PROJECTS_ROOT>\Personal\reEngine\Sandbox\` (zero mutations to `<PROGRAMS_ROOT>\reEngine\` or live `HKCU`):

1. **Silent Installer & License Artifacts:**
   ```powershell
   <PROJECTS_ROOT>\Personal\reEngine\Installers\reEngine-Setup-v0.1.3-win-x64.exe --silent --test-mode --install-dir <PROJECTS_ROOT>\Personal\reEngine\Sandbox\installed --data-dir <PROJECTS_ROOT>\Personal\reEngine\Sandbox\data --log-dir <PROJECTS_ROOT>\Personal\reEngine\Sandbox\logs --no-shortcuts --no-launch
   Test-Path <PROJECTS_ROOT>\Personal\reEngine\Sandbox\installed\app\LICENSE
   Test-Path <PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.3\LICENSE
   ```
2. **CLI, Virtual Registry, Update & Containment Probes:**
   - `.\Sandbox\installed\reEngine.exe --version` -> prints `0.1.3`, Exit `0`.
   - `.\Sandbox\installed\reEngine.exe --test-mode --headless --verify --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` -> Exit `0`.
   - `.\Sandbox\installed\reEngine.exe --test-mode --headless --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` -> writes `Sandbox\data\virtual_registry.json`.
   - Offline `--check-updates` (`Exit 0`, `[WARN]`, `0` crash dumps) vs `--check-updates --strict` (`Exit 4`), plus Zip-Slip/escape containment (`Exit 2`).
3. **Foreground `--visible-test` Live-View QA:**
   - Run `reEngine-Setup-v0.1.3-win-x64.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` and `.\Sandbox\installed\reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs`.
   - Inspect captured PNGs under `Sandbox\logs\screenshots\` via `read_file` to verify Dark Slate `#38BDF8` layout and zero clipped pills/controls.

---

## 4. 3-Tier Log Audit, Downstream Lockstep & Private GitHub Sync

- **3-Tier Log Sweep:**
  - `<PROGRAMS_ROOT>\reEngine\logs\` (read-only production audit — never write dev/test outputs here).
  - `Dev\logs\` (build/test-run traces when generated locally).
  - `Sandbox\logs\` (`reengine.log`, `crashes\crash_reengine_*.json`, `screenshots\reengine_visible_test.png`).
  - Confirm `[v0.1.3] [PID:<id>/T:<tid>] [<LEVEL>]` formatting, single `[HEARTBEAT]` tags, and populated `"recentLogs"` arrays on simulated crash bundles.
- **Downstream Consumer Lockstep:**
  - Wave 2 (`reStation v0.1.3`) consumes `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.3\` (`reEngine.Core.{dll,xml,deps.json}`, `LICENSE`, `sdk-manifest.json`) and `reEngine-Setup-v0.1.3-win-x64.exe`.
  - Wave 3 (`KingsFieldRecomp v0.1.5`) consumes both `reEngine v0.1.3` and `reStation v0.1.3`.
- **Outer Git & Private GitHub Release (`@tessa-2`):**
  - Verify `gh repo view StreamItAllGit/reEngine --json isPrivate` returns `true` (`Private`).
  - Commit Root Quartet (`README.md`, `FEATURES.md`, `ROADMAP.md`, `CHANGELOG.md`), `AGENTS.md`, `LICENSE`, `active.state`, and `Dev\` on `main`.
  - Create annotated tag `v0.1.3`, push branch + tag to `origin`, and upload `Installers\reEngine-Setup-v0.1.3-win-x64.exe`, `Releases\Latest.7z`, and `Releases\Latest\SHA256SUMS.txt` to the GitHub Release.
  - Sync Notion project entry `<NOTION_PAGE_ID>` and its 4 manual shelf subpages.
