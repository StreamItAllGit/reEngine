# reEngine — Operations Runbook

**Synced Version:** `0.3.0` (`2026-11-18`)  
**Scope:** Build Verification, Offline Toolchain Staging, Release Packaging, Live Sandbox E2E & Log Checks

---

## 1. Build, Zero-Warning Test Gate & Pre-Flight Checks

1. **Dual Zero-Warning Build & Full xUnit Suite (`77` Tests, `0` Warnings, `0` Skips):**
   ```powershell
   $env:APP_ISOLATED_TEST = "1"
   dotnet test Dev\reEngine.slnx -c Release /warnaserror
   dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror
   ```
   Confirm `77/77` deterministic tests pass across all 14 suites (`ManifestAndResolverTests`, `DecompAndToolchainTests`, `DecompProviderAndD4ValidatorTests`, `AotCacheManagerTests`, `InstallerAndAppStatusTests`, `InstallerWizardAutoSortTests`, `AssociationUpdateAndContainmentTests`, `UpdateStagingAndTracksTests`, `UpdateSweepAndTelemetryTests`, `LauncherAndLaunchPlanTests`, `SettingsMigrationAndLaunchTests`, `HostSurfaceAndInputTests`, `HostUiDrawerAndGateTests`, and `HostReleaseAutomationStripTests`).
2. **Code Size, Documentation & Leak Pre-Flight:**
   - Verify source `.cs` and `.ps1` files stay concise (`<= 400` code-only lines, `<= 250` preferred for modular files).
   - Verify `AGENTS.md` stays compact (`<= 40` lines / `<= 2,048 B`) with zero machine-local paths or personal usernames.
   - Verify `Dev\docs\ARCHITECTURE.md` keeps all 10 architecture sections and all 4 manuals match `v0.3.0`.
3. **Clearance Before Packaging:** Verify tests, binary purity checks (`0` `Foundation.Automation` or WinForms leaks in `net8.0` SDK libraries), and manual parity before building final release artifacts.

---

## 2. Offline Toolchain Staging & Single-Pass Release Packaging

### 2.1 Offline CLI Toolchain Staging (`<= 160 MB`, Zero GPL Leak)
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
Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_release.ps1 -Version 0.3.0
```

Pipeline stages executed by `build_release.ps1`:
1. **`[1/6]` Test Gate & OS Junk Scrub:** Removes OS metadata files (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) and runs `APP_ISOLATED_TEST=1 dotnet test Dev\reEngine.slnx -c Release /warnaserror` (skippable only with `-SkipTests`).
2. **`[2/6]` Portable Staging & Binary Purity:**
   - Publishes `reEngine.App` (`Builds\v0.3.0\app\`) and single-file `reEngine.Launcher` (`Builds\v0.3.0\reEngine.exe`).
   - Writes `app\version.txt` (`0.3.0`), copies root `LICENSE` to `Builds\v0.3.0\app\LICENSE`, copies `Dev\settings.example.json` and `Dev\scripts\THIRDPARTY_LICENSES.txt` into `Builds\v0.3.0\`, copies shell association scripts, and strips `.pdb`/`.xml` (`0` `AGENTS.md` inside `Builds\`).
   - Asserts `0` `Foundation.Automation` references in staged `reEngine.Host.dll` and `reEngine.dll`, and Authenticode-signs both `app\reEngine.exe` and `reEngine.exe`.
   - Retains at most `2` newest `Builds\v*` staging directories.
3. **`[3/6]` Pre-Package Scan & Single-EXE Installer:** Fails if any `.pdb`, `.db`, `.sqlite`, `.sqlite3`, `.log`, `.env`, or `*secret*.json` sits in `Builds\v0.3.0\`; packs `payload.zip` and publishes self-contained `reEngine.Installer` embedding [`Dev\Installer\installer.manifest.json`](../Installer/installer.manifest.json) (`AllowAutoStart = false`).
4. **`[4/6]` Sanitized 7-File `Releases\` Bundles (+ Portable ZIP), Hardlinks & Audit:**
   - Authenticode-signs `reEngine-Setup-v0.3.0-win-x64.exe` directly into `Releases\v0.3.0\`.
   - Compiles `Dev\docs\USER_GUIDE.md` (`-> README.md`), `FEATURES.md`, `RELEASE_NOTES.md`, and `CHANGELOG.md` using UTF-8 (no BOM), runs `Sanitize-Markdown` + `Assert-NoEndUserLeaks`, packages `reEngine-v0.3.0-win-x64-Portable.zip` (portable release binaries + sanitized docs with zero installer EXEs inside), writes `SHA256SUMS.txt` (`6` sibling release files), mirrors via NTFS hardlinks (`New-Item -ItemType HardLink` with copy fallback) to `Releases\Latest\` + hub junction `<PROJECTS_ROOT>\Releases\Personal\reEngine\`, and packs `-mx=9` `.7z` archives.
   - Runs installer structure and security verification (`AllowAutoStart = false`, zero PII leaks).
5. **`[5/6]` SharedLibs SDK Drop & Purity Check:** Builds `-f net8.0 -c Release` `reEngine.Host.csproj`, asserts `0` `Foundation.Automation` / `System.Windows.Forms` refs in `net8.0\reEngine.Host.dll`, and copies `reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `THIRDPARTY_LICENSES.txt`, and `reEngine-Setup-v0.3.0-win-x64.exe` into `<SHARED_LIBS>\reEngine\v0.3.0\` and `Latest\`, recording SHA-256 checksums in `sdk-manifest.json`.
6. **`[6/6]` Source Archive:** Packs `Archive\reEngine-v0.3.0-src.7z` including `Dev`, root docs, `AGENTS.md`, `LICENSE`, `SECURITY.md`, and `active.state` while excluding `bin`, `obj`, `TestResults`, `data`, `logs`, `.vs`, `*.pdb`, `*.tmp`, `*.bak`, and `*.user` (keeping local workspace state out of source archives).

---

## 3. Visible-Window Stage 2 E2E & CLI Verification (`APP_ISOLATED_TEST=1`)

All runtime, installer, WinForms, detached child-window, and SDL3 E2E checks run strictly isolated against temporary directories and 4-drive virtual sandboxes:

1. **Full Stage 2 E2E Runner (`Dev\scripts\run_stage2_sandbox_e2e.ps1`):**
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\run_stage2_sandbox_e2e.ps1
   ```
   - **`[1/5]` Foundation 12-Point 4-Drive Sandbox:** Executes `run_sandbox_e2e.py` (real silent install into virtualized `O:\Programs\reengine`, shortcuts + uninstall hive entry, `--version`, coexistence, missing-path standby, unplug/remount, optical verification, upgrade preserving `data\` & `data\rom\`, real uninstall, log sweep, and host snapshot check validating `0` host filesystem/registry mutations).
   - **`[2/5]` Visible WinForms E2E (`Debug + ReEngineTestAutomation`):** Launches `reEngine.exe --window --automation-port <port>`, verifies `Nav_PillStrip`, clicks `Pill_Engines` -> `Pill_BringUp` (verifying no legacy demo button appears) -> `Pill_Library` -> `Btn_ToggleDrawer`, captures `winforms_live.png`, and closes via `Btn_Close`.
   - **`[3/5]` Real Detached Child-Window `.recomp` E2E:** Compiles a real standalone `MockReStationEngine.exe` stub inside the sandbox (`reStation\v0.2.8\`), launches `reEngine.exe --open SampleRecomp.recomp --backend opengl --scale 4 --jit-bringup` interactively without `--wait`, verifies `MockReStationEngine` receives `--backend opengl`, `--scale 4`, and `--jit-bringup`, detaches its visible child window (`MainWindowHandle != 0`), exits `0`, and confirms the child window stays alive cleanly after handoff.
   - **`[4/5]` Visible SDL3 Host E2E (`Debug + ReEngineTestAutomation`):** Launches `reEngine.exe --host-demo --window --preload-media --automation-port <port>`, dismisses `MediaGate_DemoBypassButton`, toggles `F1` drawer (`Tab_Keybinds`) + `F3` HUD (`F3Hud`), captures `sdl_live.png`, and exits cleanly via `ESC`.
   - **`[5/5]` Release Fail-Closed Automation Gate:** Runs `Release` `reEngine.exe --automation-port 5199` and asserts `ExitCode = 2`.
2. **Additional CLI & `.decomp` Probes:**
   - `reEngine.exe decomp --module Dev\tests\reEngine.Tests\Fixtures\sample-re64.decomp --data-dir <tmpData> --log-dir <tmpLogs>` -> emits 3-part unbuilt-engine diagnostic and returns Exit `3`.
   - Offline `--check-updates` (`Exit 0`, `[WARN]`) vs `--check-updates --strict` (`Exit 4`), plus Zip-Slip/escape containment (`Exit 2`).

---

## 4. Log Audit, Downstream SDK Consumers & Git Release Workflow

- **Two-Tier Log Check:**
  - Read-only inspection of live user install logs (`%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\logs\` or configured install root) paired with local `Dev\logs\` test captures (`reengine.log`, `crashes\crash_reengine_*.json`).
  - Confirm `[v0.3.0] [PID:<id>/T:<tid>] [<LEVEL>]` formatting, single `[HEARTBEAT]` tags, and populated `"recentLogs"` arrays on crash bundles.
- **Downstream Consumer Lockstep:**
  - Platform engines (`reStation`/`reSlate`, `reNes`/`reCartOne`) consume `<SHARED_LIBS>\reEngine\v0.3.0\` (`reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `sdk-manifest.json`, and `reEngine-Setup-v0.3.0-win-x64.exe`).
  - Standalone game modules install into `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\games\<ModuleSlug>\`.
- **Outer Git & Release:**
  - Scrub any OS metadata files (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) before running any `git` command.
  - Commit tracked files on `main`, tag `v0.3.0`, and await explicit maintainer approval before running `git push` or creating the public GitHub Release (`Releases\v0.3.0\*`, `Releases\reEngine-v0.3.0-win-x64.7z`).
