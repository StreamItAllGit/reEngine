# reEngine — Operations Runbook

**Synced Version:** `0.1.1` (`2026-11-14`)  
**Owner:** `@tessa`

---

## 1. Build, Test & Packaging (`Dev\scripts\build_release.ps1`)
- Run unit tests & build with zero warnings: `dotnet test Dev\reEngine.slnx -c Release /warnaserror` (`33/33` xUnit tests).
- Strictly after `@dean` Gate 2 `PASS`, run `powershell -NoProfile -ExecutionPolicy Bypass -File Dev\scripts\build_release.ps1 -Version 0.1.1` to stage `Builds\v0.1.1\`, signed single-EXE installer (`Installers\reEngine-Setup-v0.1.1-win-x64.exe` + `<PROJECTS_ROOT>\Installers\`), 6-file `Releases\{v0.1.1,Latest}\` + `.7z`, `Archive\reEngine-v0.1.1-src.7z`, and immutable SDK drop `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.1\` (`+ Latest\`).
- Validate installer compliance: `audit_installer.ps1 -InstallerPath <PROJECTS_ROOT>\Personal\reEngine\Installers -StagingDir <PROJECTS_ROOT>\Personal\reEngine\Builds\v0.1.1 -ExpectedVersion 0.1.1` (`0` findings, `AllowAutoStart = false`).
- Downstream `re*` consumers (`reStation`) sync pinned binaries from `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.1\` via `sdk-manifest.json`.
- Outer Git & Private GitHub Release (`Standard 11`): after `@tessa` doc sync and `Releases\`/`Archive\` refresh, commit on `main`, annotate tag `v0.1.1`, push to private `StreamItAllGit/reEngine`, and upload `Installers\reEngine-Setup-v0.1.1-win-x64.exe`, `Releases\Latest.7z`, and `Releases\Latest\SHA256SUMS.txt`.

## 2. Contained Sandbox Verification (`Sandbox\`)
- **Isolated Installer Stage:** `Installers\reEngine-Setup-v0.1.1-win-x64.exe --silent --test-mode --install-dir .\Sandbox\installed --log-dir .\Sandbox\logs` (`0` mutations to `<PROGRAMS_ROOT>\reEngine\`, `HKCU:\Software\Classes\.recomp`, host shortcuts, or Windows Startup).
- **CLI Sync & Module Isolation Probe:** From `Sandbox\`, run `.\installed\reEngine.exe --test-mode --headless --open .\data\sample.recomp.json --wait --frames 5 --data-dir .\data --log-dir .\logs` to verify caller-CWD relative paths, child exit propagation `(0, 1, 2, 3, 4, 5, 6, 130)`, `--preload-media`, and isolated `<dataDir>\modules\<moduleId>\` (`settings.json` + `logs\`).
- **Offline, Strict & Containment Probes:** Run `--test-mode --check-updates --feed http://127.0.0.1:15931/feed.json` (`ExitCode = 0`, `[WARN]`, `0` crash dumps; with `--strict` -> `ExitCode = 4`), multi-engine side-by-side `v0.1.0` + `v0.2.0` sweep, and fail-closed escape/Zip-Slip probes (`ExitCode = 2`).
- **Foreground UI Showcase:** `.\Sandbox\app\reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs` (auto-exits cleanly; inspect captured PNG in `Sandbox\logs\screenshots\` via `read_file` to confirm zero status pill clipping).
- **3-Tier Log Audit (`Standard 15`):** Sweep `<PROGRAMS_ROOT>\reEngine\logs\` (read-only), `Dev\logs\`, and `Sandbox\logs\`; confirm `[v0.1.1] [PID:<id>/T:<tid>] [<LEVEL>]` lines, single `[HEARTBEAT]` prefixes, and populated `"recentLogs"` in `crashes\crash_reengine_*.json`.
