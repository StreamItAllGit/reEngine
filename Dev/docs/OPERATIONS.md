# reEngine — Operations Runbook

**Synced Version:** `0.6.1` (`2026-11-20`)  
**Scope:** Build Verification, Static & GitHub Polish Audits, Offline Toolchain Staging, `SingleSetup7` Packaging & `0.6 / 0.5 / 0.4` Retention, Stage 2 Sandbox E2E (`Stage2V060Probes` + `Stage2V061ParityProbes`) & Two-Tier Log Checks

---

## 1. Build, Zero-Warning Test Gate & Pre-Flight Checks

1. **Dual Zero-Warning Build & Full xUnit Suite (`28` Suites, `148` Tests, `0` Warnings, `0` Skips):**
   ```powershell
   $env:APP_ISOLATED_TEST = "1"
   dotnet test Dev\reEngine.slnx -c Release /warnaserror
   dotnet publish Dev\src\reEngine.App\reEngine.App.csproj -c Debug -p:ReEngineTestAutomation=true "-t:Rebuild,Publish" /warnaserror
   dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror
   ```
   Confirm `148/148` deterministic tests pass across all `28` suites (`25` prior core/host/routing suites plus `3` new `v0.6.1` suites):
   - Prior Suites (`25`): `ManifestAndResolverTests`, `DecompAndToolchainTests`, `DecompProviderAndD4ValidatorTests`, `AotCacheManagerTests`, `InstallerAndAppStatusTests`, `InstallerWizardAutoSortTests`, `AssociationUpdateAndContainmentTests`, `UpdateStagingAndTracksTests`, `UpdateSweepAndTelemetryTests`, `LauncherAndLaunchPlanTests`, `SettingsMigrationAndLaunchTests`, `HostSurfaceAndInputTests`, `HostUiDrawerAndGateTests`, `HostReleaseAutomationStripTests`, `HostMultiBackendAndAudioTests`, `HostMultiBindInputAndTouchTests`, `MediaTreeWalkerAndVerifyModuleTests`, `SyncWaitShutdownAndDecompUiTests`, `HostViewportGeometryAndPresentersTests`, `HostMultiPortInputAndHotkeysTests`, `EngineRoutingAndMediaDiscoveryTests`, `HostExecutionResilienceAndSettingsSplitTests`, `HostDisplayVrrFiltersAndCaptureTests`, `HostAudioDspAndJukeboxTests`, `AppV060RoutingAndUiTests`.
   - Added in `v0.6.1` (`3`): `EngineResolutionV061AndCrashFallbackTests`, `HostParityUiScaleMediaGateAndControllerNavTests`, `HostNonQuittingEscMouseCaptureAndHotkeysTests`.
2. **Static Scanner, SLOC Caps, Installer & Public-Repo Polish Audits:**
   - Run static scanner against `Dev\` (`0` `P0`–`P3` findings across `167` scanned `.cs`/`.ps1` files under `Dev\src`, `Dev\tests`, `Dev\scripts`):
     ```powershell
     python "%USERPROFILE%\.gemini\config\skills\audit\scripts\audit_scanner.py" ".\Dev" --format markdown
     ```
   - Verify source `.cs` and `.ps1` files obey `<= 250` preferred / `<= 400` soft / `<= 600` hard code-only SLOC caps, manuals (`Dev\docs\*.md`) stay `<= 600` SLOC (or split flat into `<DOC>.<Topic>.md`), and `AGENTS.md` stays `<= 40` lines (`<= 2,048 B`) with zero machine-local paths or personal usernames.
   - Verify all 4 Root Quartet docs, `SECURITY.md`, `active.state`, and all 4 Manual Shelf files match `0.6.1` with zero internal jargon or emojis in public root docs, then run `audit_github_polish.ps1`:
     ```powershell
     powershell -NoProfile -ExecutionPolicy Bypass -File "%USERPROFILE%\.gemini\config\skills\audit\scripts\audit_github_polish.ps1" -ProjectRoot D:\Projects\Personal\reEngine
     ```
3. **Clearance Before Packaging:** Verify `148/148` tests in both builds, `0` `P0`–`P3` findings, binary purity (`0` `Foundation.Automation`, `Foundation.UI`, or `System.Windows.Forms` refs in `net8.0` `reEngine.Host.dll` and `reEngine.Core.dll`, `IHostAutomationBridge` decoupled via `AutomationBridgeFactory`), `installer.manifest.json` `"suiteId": "reEngine"` (`Foundation v0.5.3`), and full manual parity before running `build_release.ps1`.

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

### 2.2 Single-Pass Release Build (`SingleSetup7`), `0.6 / 0.5 / 0.4` Retention & SDK Drop
Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_release.ps1 -Version 0.6.1
```

Pipeline stages executed by [`Dev\scripts\build_release.ps1`](../scripts/build_release.ps1):
1. **`[1/6]` Test Gate & OS Junk Scrub:** Removes OS metadata files (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) and runs `APP_ISOLATED_TEST=1 dotnet test Dev\reEngine.slnx -c Release /warnaserror` (skippable only with `-SkipTests`).
2. **`[2/6]` Portable Staging & Binary Purity:**
   - Publishes `reEngine.App` (`Builds\v0.6.1\app\`) and single-file `reEngine.Launcher` (`Builds\v0.6.1\reEngine.exe`, `< 2 MB`, all `v0.6.1` CLI flags registered in `LauncherArgsHelper.ValueOptions` plus optional-value `--require-backend [<id>]` and wait-only `LauncherJobObjectGuard`).
   - Writes `app\version.txt` (`0.6.1`), copies root `LICENSE` to `Builds\v0.6.1\app\LICENSE`, copies `Dev\settings.example.json` (`schemaVersion: 2`, `domain: "core"`, `shouldEnableRuntimeLogs: false`) and `Dev\scripts\THIRDPARTY_LICENSES.txt` into `Builds\v0.6.1\`, copies association batch scripts, strips `.pdb`/`.xml` (`0` `AGENTS.md` inside `Builds\`), verifies `0` `Foundation.Automation` / `System.Windows.Forms` / `Foundation.UI` refs in `reEngine.Core.dll` and `reEngine.Host.dll` (`0` `Foundation.Automation` / `Foundation.UI` in `reEngine.dll`), Authenticode-signs both executables, asserts Release `--automation-port 5199` returns `ExitCode = 2`, and keeps at most `2` newest `Builds\v*` folders.
3. **`[3/6]` Pre-Package Scan & Single-EXE Installer (`Foundation v0.5.3` `"suiteId": "reEngine"`):** Fails if any `.pdb`, `.db`, `.sqlite`, `.sqlite3`, `.log`, `.env`, or `*secret*.json` sits in `Builds\v0.6.1\`; packs `payload.zip` and publishes self-contained `reEngine.Installer` embedding [`Dev\Installer\installer.manifest.json`](../Installer/installer.manifest.json) (`"suiteId": "reEngine"`, `AllowAutoStart = false`), asserting zero `Foundation.Automation` / `Foundation.UI` refs in both `reEngine.Installer.dll` and the self-contained Setup EXE.
4. **`[4/6]` Sanitized `SingleSetup7` `Releases\` Bundle (+ Portable ZIP), Hardlinks & `0.6 / 0.5 / 0.4` Retention:**
   - Authenticode-signs `reEngine-Setup-v0.6.1-win-x64.exe` into `Releases\v0.6.1\` (asserting Release `--automation-port 5199` exits `2`), compiles sanitized `README.md` (from `USER_GUIDE.md`), `FEATURES.md`, `RELEASE_NOTES.md`, and `CHANGELOG.md` via `Sanitize-Markdown` + `Assert-NoEndUserLeaks`, builds `reEngine-v0.6.1-win-x64-Portable.zip` (zero installer EXEs inside), writes `SHA256SUMS.txt`, mirrors via NTFS hardlinks (`New-Item -ItemType HardLink` with copy fallback) to `Releases\Latest\` + hub junction (`D:\Projects\Releases\Personal\reEngine\`), packs `reEngine-v0.6.1-win-x64.7z` & `Latest.7z` (`-mx=9`), and validates via `audit_installer.ps1 -ReleaseLayoutMode SingleSetup7` (`Passed: True`, `0` findings). *(Dual-Installer Online + Offline 8-file layout is scheduled for `v0.8.0`.)*
   - Enforces the 3-MINOR retention window (`0.6`, `0.5`, `0.4`): removes suffixed pre-release/hotfix unpacked directories, retains only the `2` newest unpacked versioned folders (`Releases\v0.6.1\`, `Releases\v0.5.0\`, plus `Latest\`), and prunes `.7z` release archives older than `0.4.x` (`0.3.x` retired).
5. **`[5/6]` SharedLibs SDK Drop & Purity Check:** Builds `-f net8.0 -c Release` `reEngine.Host.csproj`, asserts `0` `Foundation.Automation` / `System.Windows.Forms` / `Foundation.UI` refs in `net8.0\reEngine.Core.dll` and `net8.0\reEngine.Host.dll`, and copies `reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `THIRDPARTY_LICENSES.txt`, and `reEngine-Setup-v0.6.1-win-x64.exe` into `<SHARED_LIBS>\reEngine\v0.6.1\` and `Latest\`, recording SHA-256 checksums in `sdk-manifest.json`.
6. **`[6/6]` Source Archive:** Packs `Archive\reEngine-v0.6.1-src.7z` including `Dev`, Root Quartet, `AGENTS.md`, `LICENSE`, `SECURITY.md`, and `active.state` while excluding `bin`, `obj`, `TestResults`, `data`, `logs`, `.vs`, `*.pdb`, `*.tmp`, `*.bak`, and `*.user`.

---

## 3. Visible-Window Stage 2 Sandbox E2E (`APP_ISOLATED_TEST=1`)

Run the parameterized Stage 2 E2E harness strictly inside temporary sandboxes (`Write-DesktopPreflightInfo` reports live `GetThreadDesktop()` `Default` vs `WinSta0\exebox-*`, and `Start-ExeboxStreamForPort` streams live GUI runs over UDP `5004`):
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\run_stage2_sandbox_e2e.ps1 -Version 0.6.1
```
- **`[1/7]` Foundation 12-Point 4-Drive Installer Sandbox:** Runs `run_sandbox_e2e.py` (silent install into virtualized `O:\Programs\reengine`, shortcuts + uninstall entry, `--version`, coexistence, missing-path standby, unplug/remount, upgrade preserving `data\` & `data\rom\` + split configs, real uninstall, log sweep, and `S2-12` host diff filtering external Google Drive `.tmp.driveupload` noise).
- **`[2/7]` Visible WinForms Library & Inspector Drawer E2E:** Launches `Debug + ReEngineTestAutomation` `reEngine.exe --window`, verifies `Chk_Fullscreen`, `8x` scale option, and `Btn_VerifyPackage`, clicks `Pill_Engines` -> `Pill_BringUp` -> `Pill_Library` -> `Btn_ToggleDrawer`, verifies `Drawer_Panel`, captures non-blank `winforms_library_live.png`, and exits cleanly.
- **`[3/7]` Interactive Explorer `.decomp` Launch onto Bring-Up Shelf:** Launches `reEngine.exe decomp --module SampleDecomp.decomp` onto `BringUp`, verifies `Btn_RunStage` and clicks `Btn_VerifyDecomp`, and captures non-blank `winforms_bringup_decomp_live.png`.
- **`[4/7]` Standalone `--verify-module --json` Check:** Runs `reEngine.exe --verify-module` on `.recomp` and `.decomp` fixtures, asserting `ExitCode = 0` and `"isPassed": true`.
- **`[5/7]` Synchronous Frontend `--open --wait` & `WM_CLOSE` Save Flush:** Launches `--open SampleRecomp.recomp --wait --fullscreen --save-dir <saveDir> --backend directx --scale 4`, verifies forwarded args, posts `WM_CLOSE` (`0x0010`) to the child window, and confirms `memcard.sav` flushes into `<saveDir>` with `ExitCode = 0`.
- **`[6/7]` Visible SDL3 Host E2E (Aspect, Hotkeys, Non-Quitting `Esc`, `System_QuitGame` & `ExitCode = 6`):** Launches `--host-demo --backend opengl --require-backend --aspect-mode fourByThree --window --preload-media`, clicks `MediaGate_LaunchDemoButton`, presses `F1`, clicks `Video_AspectRatio`, dispatches `F5` and `F9` (`FlushAndSyncForSnapshot`), clicks `Tab_Keybinds`, toggles `F3` (`F3Hud`), captures `sdl_host_live.png`, asserts pressing `ESC` closes the drawer without terminating the process, opens `Tab_System` -> clicks `System_QuitGame` for `ExitCode = 0`, then runs `--host-demo --backend metal --require-backend --frames 1` and asserts `ExitCode = 6`.
- **`[7/8]` `Stage2V060Probes` ([`Stage2V060Probes.ps1`](../scripts/Stage2V060Probes.ps1)):**
  - **Probe A (WinForms Split Config):** Verifies `Chk_PreloadMediaToRam`, `Cmb_DisplayMode`, `Cmb_VideoFilter`, `Cmb_AudioFilterPreset`, and `Chk_AudioMuted`, captures `winforms_v060_controls.png`, and asserts `settings.json` (`schemaVersion >= 2`), `settings.video.json`, and `settings.audio.json` persist cleanly.
  - **Probe B (4-Backend Filters & `21:9` Capture):** Captures non-blank PNGs across `opengl` (`--video-filter crtLite`), `vulkan` (`--video-filter xbrz`), `directx` (`--video-filter ntscComposite`), and `parallelCpu` (`--aspect-mode horPlus21By9 --screenshot-mode filteredComposite`).
  - **Probe C (Dedicated `Audio` Tab + Sound Test WAV):** Opens `F1`, clicks `Tab_Audio` -> `Audio_SoundTest_PlayStop`, captures `sdl_v060_audio_tab_live.png`, confirms `sdl_v060_soundtest.wav` contains non-zero PCM samples beyond the 44-byte WAV header, and exits cleanly via `System_QuitGame`.
  - **Probe D (Dead-PID `booting.sentinel` Safe Mode & `--safe-mode-reset`):** Seeds a dead-PID `booting.sentinel`, verifies `--backend vulkan` enters Safe Mode (`isSafeModeActive: true`, `sdl_v060_safemode_live.png`) *without* modifying saved `settings.video.json`, exits via `System_QuitGame`, then runs `--safe-mode-reset` and confirms `isSafeModeActive` clears back to `false`.
- **`[7.5/8]` `Stage2V061ParityProbes` ([`Stage2V061ParityProbes.ps1`](../scripts/Stage2V061ParityProbes.ps1)):**
  - Runs `--demo-cart --frames 12 --dump-audio` confirming clean non-zero WAV capture; launches live `--host-demo --backend vulkan --window --fps-mode enhanced --target-fps 120 --ui-text-scale 1.25`, verifies full-screen Glass Media Gate (`MediaGate_LaunchDemoButton`, zero `"Continue"` text) -> `sdl_v061_mediagate_live.png`, verifies double-`ESC` leaves process alive (`isPassed`), opens `F1` (`Video_UiTextScale_Inc`), checks `Tab_Controls` (`Controls_Profile_ModernPc`, zero raw `"Schema A/B/C"`, cycles `Controls_MenuChord` to `select+start`) -> `sdl_v061_controls_scaled_live.png`, scrolls `Tab_Keybinds` (`Keybinds_ScrollDown`) to verify `Keybinds_Slot_toggleMenu_0` -> `sdl_v061_keybinds_host_live.png`, toggles `F3` (`F3Hud` badges) -> `sdl_v061_f3_hud_badges_live.png`, enables `System_ToggleCleanCapture` confirming `F3Hud` vanishes from `/v1/tree` -> `sdl_v061_clean_capture_live.png`, exits cleanly via `System_QuitGame` (`ExitCode = 0`), and asserts `settings.controls.json` persists `menuControllerChord: "select+start"`.
- **`[8/8]` Dual-Binary Release Fail-Closed Automation Gate:** Runs **both** `Release` `reEngine.exe --automation-port 5199` and `Release` `reEngine-Setup-v0.6.1-win-x64.exe --automation-port 5199` and asserts `ExitCode = 2` on both executables.

---

## 4. Split-Domain Configs, Protocol Gating, Logs, Exit Codes & Git Release

- **Standard 19 Split-Domain Config (`<DataDir>\config\`, [`Dev\settings.example.json`](../settings.example.json)):**
  - `settings.json` (`schemaVersion: 2`, `domain: "core"`): stores `appId`, `updatedAtUtc`, `isJitEnabled`, `executionTierMode`, `shouldPreloadMediaToRam`, `isFastBootEnabled`, `isSafeModeActive`, `shouldFastLoadData`, `shouldEnableRuntimeLogs`, `fastForwardMultiplier`, `installProfile`, `shutdownTimeoutMs`, `shouldCheckUpdates`, `shouldAutoStagePatches`, `pinnedEngineVersions`, `stagedUpdatePath`, install roots, and decomp schema/tier prefs.
  - `settings.video.json` (`schemaVersion: 2`, `domain: "video"`): stores `backendId`, `resolutionScale`, `displayMode`, `monitorIndex`, `isFullscreen`, `isVsyncEnabled`, `frameRateMode`, `targetFps`, `backgroundFpsCap`, `aspectRatioMode`, `overscanCropPx`, `isFastForwardEnabled`, `isHdTexturePackEnabled`, `hudAnchorMode`, `videoFilterMode`, `isTrueColor24BitEnabled`, `isDitheringEnabled`, `screenshotCaptureMode`, `uiTextScale`, `isFpsOverlayEnabled`, and `isCleanCaptureModeEnabled`.
  - `settings.audio.json` (`schemaVersion: 2`, `domain: "audio"`): stores `audioDriver`, `audioOutputDevice`, `masterVolume`, `isAudioMuted`, `bgmVolume`, `sfxVolume`, `voiceVolume`, `shouldMuteOnFocusLoss`, `audioLowPassHz`, `audioHighPassHz`, `audioStereoWidthPercent`, `audioResamplingMode`, and `fastForwardAudioMode`.
  - `settings.controls.json` (`HostControlTierStore`): stores `schemaVersion`, `appId`, `domain` (`"controls"`), `updatedAtUtc`, `inputProfileMode`, `activeSchema`, `controlMode`, `mouseSensitivity`, `menuControllerChord`, `hostActionBindings`, and 3-tier bindings (`globalController`, `perGameController`, `perGameModern`) atomically with `[JsonExtensionData]` preservation, conflict/hidden-slot validation, and `SerializeDirtyCheckJson()` idle write prevention.
  - **Self-Healing & Installer Preservation:** `SettingsDomainStore` snapshots `v1 -> v2` conversions into `data\config_history\`, isolates corrupt JSON into `data\quarantine\`, and restores `.bak` or history snapshots automatically. `LegacyMigrationHelper` preserves all 4 split domain files and `Math.Max(existingSchemaVersion, 1)` across upgrades.
- **Engine Resolution, Crash Recovery, Resilience, Protocol Gating & Exit Codes:**
  - `EngineCompatibilityEvaluator` + `EngineVersionResolver` (`pinnedEngineVersions` `moduleId` -> `engineId` -> aliases -> latest `.ready` excluding `incompatibleEngineVersions`), `SessionRecoveryLocator` + prompt-first `TryCrashFallbackRelaunchAsync`, `BootCrashSentinel`, `MediaRamPreloader` (`>= 512 MiB` / `<= 75%`), `ExecutionTierPolicy` (`interpreterOracle` quarantined to `AllowCliOnly`), wait-only `LauncherJobObjectGuard`, and `RecompLaunchFlagRouter` (`>= 0.6.1`, `>= 0.6.0`, `>= 0.3.0`, pre-`0.3.0`).
  - Clean process exit codes: `0` (success / valid `--verify-module`), `1` (missing compatible engine / `.recomp` `--require-media` fail), `2` (invalid CLI args / malformed package / Zip-Slip / containment error / Release `--automation-port`), `3` (unbuilt `.decomp` engine / `.decomp` `--require-media` fail), `4` (offline `--check-updates --strict`), `6` (unavailable backend under `--require-backend`), `130` (`Ctrl+C` + bounded `<= 3s` `WM_CLOSE` tree flush).
- **Two-Tier Log Check:** Inspect live install logs (`%LOCALAPPDATA%\Programs\StreamItAll\reEngine\reEngine\logs\` or configured install root, read-only) alongside `Dev\logs\` (`reengine.log`, `crashes\crash_reengine_*.json`) verifying `[v0.6.1] [PID:<id>/T:<tid>]` headers, single `[HEARTBEAT]` tags, and zero crash dumps.
- **Downstream SDK Consumers:** Platform engines (`reStation`/`reSlate`, `reCart`/`reCartOne`/`reNes`) consume `<SHARED_LIBS>\reEngine\v0.6.1\` (`reEngine.Core.{dll,xml,deps.json}`, `reEngine.Host.{dll,xml,deps.json}`, `SDL3.dll`, `LICENSE`, `THIRDPARTY_LICENSES.txt`, `sdk-manifest.json`, and `reEngine-Setup-v0.6.1-win-x64.exe`).
- **Outer Git & Public Release Publication:** Scrub OS metadata junk (`desktop.ini`, `Thumbs.db`, `ehthumbs.db`, `.DS_Store`, `._*` by exact filename) before any `git` command, commit tracked files on `main`, tag `v0.6.1`, pause for explicit human review on public repos (`isPublicRepo: true` if required), push `main --tags`, keep GitHub Pages enabled (`build_type=workflow`) for `.github\workflows\pages.yml`, and publish release artifacts exclusively via **GitHub Releases** (`--prerelease` for `0.x`) + Discord `/post` (`Standard 28 §3.1`, zero Google Drive binary uploads; manual-only Notion per `Standard 36`).
