# reEngine — Operations Runbook

**Synced Version:** `0.4.0` (`2026-11-19`)  
**Scope:** Build Verification, Static Audit, Offline Toolchain Staging, Release Packaging & Retention, 7-Step Sandbox E2E & Log Checks

---

## 1. Build, Zero-Warning Test Gate & Pre-Flight Checks

1. **Dual Zero-Warning Build & Full xUnit Suite (`18` Suites, `98` Tests, `0` Warnings, `0` Skips):**
   ```powershell
   $env:APP_ISOLATED_TEST = "1"
   dotnet test Dev\reEngine.slnx -c Release /warnaserror
   dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror
   ```
   Confirm `98/98` deterministic tests pass across all `18` suites:
   - `ManifestAndResolverTests`, `DecompAndToolchainTests`, `DecompProviderAndD4ValidatorTests`, `AotCacheManagerTests`, `InstallerAndAppStatusTests`, `InstallerWizardAutoSortTests`, `AssociationUpdateAndContainmentTests`, `UpdateStagingAndTracksTests`, `UpdateSweepAndTelemetryTests`, `LauncherAndLaunchPlanTests`, `SettingsMigrationAndLaunchTests`, `HostSurfaceAndInputTests`, `HostUiDrawerAndGateTests`, `HostReleaseAutomationStripTests`, `HostMultiBackendAndAudioTests`, `HostMultiBindInputAndTouchTests`, `MediaTreeWalkerAndVerifyModuleTests`, and `SyncWaitShutdownAndDecompUiTests`.
2. **Static Scanner, SLOC Caps & Doc Pre-Flight:**
   - Run static scanner against `Dev\` (`0` `P0`–`P3` findings):
     ```powershell
     python "C:\Users\janov\.gemini\config\skills\audit\scripts\audit_scanner.py" "D:\Projects\Personal\reEngine\Dev" --format markdown
     ```
   - Verify source `.cs` and `.ps1` files stay concise (`<= 250` preferred, `<= 400` code-only SLOC ceiling), manuals (`Dev\docs\*.md`) stay `<= 600` SLOC, and `AGENTS.md` stays `<= 40` lines / `<= 2,048 B` with zero machine-local paths or personal usernames.
   - Verify all 4 Root Quartet docs, `active.state`, and all 4 Manual Shelf files match `0.4.0` with zero legacy console-simulation phrasing.
3. **Clearance Before Packaging:** Verify `98/98` tests in both builds, `0` `P0`–`P3` findings, binary purity (`0` `Foundation.Automation` or `System.Windows.Forms` refs in `net8.0` `reEngine.Host.dll`), and full manual parity before running `build_release.ps1`.

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

### 2.2 Single-Pass Release Build, 3-MINOR Retention & SDK Drop
Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_release.ps1 -Version 0.4.0
```

Pipeline stages executed by `build_release.ps1`:
1. **`[1/6]` Test Gate & OS Junk Scrub:** Removes OS metadata files (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) and runs `APP_ISOLATED_TEST=1 dotnet test Dev\reEngine.slnx -c Release /warnaserror` (skippable only with `-SkipTests`).
2. **`[2/6]` Portable Staging & Binary Purity:**
   - Publishes `reEngine.App` (`Builds\v0.4.0\app\`) and single-file `reEngine.Launcher` (`Builds\v0.4.0\reEngine.exe`, `< 2 MB`).
   - Writes `app\version.txt` (`0.4.0`), copies root `LICENSE` to `Builds\v0.4.0\app\LICENSE`, copies `Dev\settings.example.json` and `Dev\scripts\THIRDPARTY_LICENSES.txt` into `Builds\v0.4.0\`, copies association batch scripts, strips `.pdb`/`.xml` (`0` `AGENTS.md` inside `Builds\`), verifies `0` `Foundation.Automation` / `System.Windows.Forms` refs in `reEngine.Host.dll`, Authenticode-signs both executables, and keeps at most `2` newest `Builds\v*` folders.
3. **`[3/6]` Pre-Package Scan & Single-EXE Installer:** Fails if any `.pdb`, `.db`, `.sqlite`, `.sqlite3`, `.log`, `.env`, or `*secret*.json` sits in `Builds\v0.4.0\`; packs `payload.zip` and publishes self-contained `reEngine.Installer` embedding [`Dev\Installer\installer.manifest.json`](../Installer/installer.manifest.json) (`AllowAutoStart = false`).
4. **`[4/6]` Sanitized 7-File `Releases\` Bundle (+ Portable ZIP), Hardlinks & 3-MINOR Retention Pruning:**
   - Authenticode-signs `reEngine-Setup-v0.4.0-win-x64.exe` into `Releases\v0.4.0\`, compiles sanitized `README.md` (from `USER_GUIDE.md`), `FEATURES.md`, `RELEASE_NOTES.md`, and `CHANGELOG.md` via `Sanitize-Markdown` + `Assert-NoEndUserLeaks`, builds `reEngine-v0.4.0-win-x64-Portable.zip` (zero installer EXEs inside), writes `SHA256SUMS.txt`, mirrors via NTFS hardlinks (`New-Item -ItemType HardLink` with copy fallback) to `Releases\Latest\` + hub junction `<PROJECTS_ROOT>\Releases\Personal\reEngine\`, packs `reEngine-v0.4.0-win-x64.7z` & `Latest.7z` (`-mx=9`), and runs `audit_installer.ps1`.
   - Enforces the 3-MINOR retention window (`0.4`, `0.3`, `0.2`): removes suffixed pre-release/hotfix unpacked directories (`v0.2.1-hf.1\`), retains only the `2` newest unpacked versioned folders (`Releases\v0.4.0\`, `Releases\v0.3.0\`, plus `Latest\`), and prunes `.7z` release archives older than `0.2.x` (removing `v0.1.*.7z`).
5. **`[5/6]` SharedLibs SDK Drop & Purity Check:** Builds `-f net8.0 -c Release` `reEngine.Host.csproj`, asserts `0` `Foundation.Automation` / `System.Windows.Forms` refs in `net8.0\reEngine.Host.dll`, and copies `reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `THIRDPARTY_LICENSES.txt`, and `reEngine-Setup-v0.4.0-win-x64.exe` into `<SHARED_LIBS>\reEngine\v0.4.0\` and `Latest\`, recording SHA-256 checksums in `sdk-manifest.json`.
6. **`[6/6]` Source Archive:** Packs `Archive\reEngine-v0.4.0-src.7z` including `Dev`, Root Quartet, `AGENTS.md`, `LICENSE`, `SECURITY.md`, and `active.state` while excluding `bin`, `obj`, `TestResults`, `data`, `logs`, `.vs`, `*.pdb`, `*.tmp`, `*.bak`, and `*.user`.

---

## 3. Visible-Window 7-Step Stage 2 Sandbox E2E (`APP_ISOLATED_TEST=1`)

Run the parameterized Stage 2 E2E harness strictly inside temporary sandboxes (reports `GetThreadDesktop()` via `Write-DesktopPreflightInfo` so `exebox-*` vs `Default` desktop visibility is never misreported):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\run_stage2_sandbox_e2e.ps1 -Version 0.4.0
```
- **`[1/7]` Foundation 12-Point 4-Drive Sandbox:** Runs `run_sandbox_e2e.py` (silent install into virtualized `O:\Programs\reengine`, shortcuts + uninstall entry, `--version`, coexistence, missing-path standby, unplug/remount, upgrade preserving `data\` & `data\rom\`, real uninstall, log sweep, and `S2-12` host diff filtering external Google Drive `.tmp.driveupload` noise).
- **`[2/7]` Visible WinForms Library & Inspector Drawer E2E:** Launches `Debug + ReEngineTestAutomation` `reEngine.exe --window`, verifies `Chk_Fullscreen` and `Btn_VerifyPackage`, clicks `Pill_Engines` -> `Pill_BringUp` -> `Pill_Library` -> `Btn_ToggleDrawer`, captures non-blank `winforms_library_live.png`, and exits cleanly.
- **`[3/7]` Interactive Explorer `.decomp` Launch onto Bring-Up Shelf:** Launches `reEngine.exe decomp --module SampleDecomp.decomp` onto `BringUp`, verifies `Btn_RunStage` and clicks `Btn_VerifyDecomp`, and captures non-blank `winforms_bringup_decomp_live.png`.
- **`[4/7]` Standalone `--verify-module --json` Check:** Runs `reEngine.exe --verify-module` on `.recomp` and `.decomp` fixtures, asserting `ExitCode = 0` and `"isPassed": true`.
- **`[5/7]` Synchronous Frontend `--open --wait` & `WM_CLOSE` Save Flush:** Launches `--open SampleRecomp.recomp --wait --fullscreen --save-dir <saveDir> --backend directx --scale 4`, verifies forwarded args, posts `WM_CLOSE` (`0x0010`) to the child window, and confirms `memcard.sav` flushes into `<saveDir>` with `ExitCode = 0`.
- **`[6/7]` Visible SDL3 Host E2E & `ExitCode = 6` Check:** Launches `--host-demo --backend opengl --require-backend --window --preload-media`, clicks `MediaGate_DemoBypassButton`, toggles `F1` (`Tab_Keybinds`) + `F3` (`F3Hud`), captures `sdl_host_live.png`, exits via `ESC`, then runs `--host-demo --backend metal --require-backend --frames 1` and asserts `ExitCode = 6`.
- **`[7/7]` Release Fail-Closed Automation Gate:** Runs `Release` `reEngine.exe --automation-port 5199` and asserts `ExitCode = 2`.

---

## 4. Two-Tier Logs, `v0.4.0` `settings.json` Keys, SDK Lockstep & Git Release

- **Canonical `v0.4.0` `settings.json` Keys (`Dev\settings.example.json`):** `schemaVersion`, `appId`, `installProfile`, `backendId` (`"opengl"` default; supports `"directx"`, `"vulkan"`, `"parallelCpu"`, `"software"`), `resolutionScale`, `isFullscreen`, `audioDriver` (`null` -> platform default `wasapi` on Windows), `isHdTexturePackEnabled`, `shutdownTimeoutMs` (`3000`), `shouldCheckUpdates`, `shouldAutoStagePatches`, `pinnedEngineVersions`, `stagedUpdatePath`, `reEngineInstallDir`, `suiteInstallDir`, `umbrellaSubpath`, `decompSchemaVersion`, `goalDecompTier`, and `targetDecompTier` (preserved across upgrades with `.bak` recovery).
- **Two-Tier Log Check:** Inspect live install logs (`%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\logs\` or configured install root, read-only) alongside `Dev\logs\` (`reengine.log`, `crashes\crash_reengine_*.json`) verifying `[v0.4.0] [PID:<id>/T:<tid>]` headers, single `[HEARTBEAT]` tags, and zero crash dumps.
- **Downstream SDK Consumers:** Platform engines (`reStation`/`reSlate`, `reNes`/`reCartOne`) consume `<SHARED_LIBS>\reEngine\v0.4.0\` (`reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `THIRDPARTY_LICENSES.txt`, `sdk-manifest.json`, and `reEngine-Setup-v0.4.0-win-x64.exe`).
- **Outer Git & Release Publication:** Scrub OS metadata junk (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) before any `git` command, commit tracked files on `main`, tag `v0.4.0`, and publish release artifacts exclusively via **GitHub Releases** (`Releases\v0.4.0\*`, `Releases\reEngine-v0.4.0-win-x64.7z` — never upload release binaries or archives to Google Drive).
