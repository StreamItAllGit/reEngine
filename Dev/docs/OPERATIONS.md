# reEngine — Operations Runbook

**Synced Version:** `0.1.0` (`2026-11-13`)  
**Owner:** `@tessa`

---

## 1. Build, Test & Packaging (`Dev\scripts\build_release.ps1`)
- Run unit tests & build with zero warnings: `dotnet test Dev\reEngine.slnx -c Release /warnaserror`
- Package `Builds\v0.1.0\`, signed single-EXE installer (`Installers\reEngine-Setup-v0.1.0-win-x64.exe` + `<PROJECTS_ROOT>\Installers\`), 6-file `Releases\Latest\` + `Latest.7z`, `Archive\reEngine-v0.1.0-src.7z`, and immutable SDK drop `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.0\` (`+ Latest\`).
- Validate installer compliance: `audit_installer.ps1 -InstallerPath <PROJECTS_ROOT>\Personal\reEngine\Installers -ExpectedVersion 0.1.0`.

## 2. Contained Sandbox Verification (`Sandbox\`)
- Standard headless test run: `.\Sandbox\app\reEngine.exe --test-mode --headless --register-assoc --data-dir .\Sandbox\data --log-dir .\Sandbox\logs`
- Foreground UI showcase: `.\Sandbox\app\reEngine.exe --test-mode --visible-test --data-dir .\Sandbox\data --log-dir .\Sandbox\logs`
- Inspect logs across `<PROGRAMS_ROOT>\reEngine\logs\` (read-only), `Dev\logs\`, and `Sandbox\logs\` per Standard 15.
