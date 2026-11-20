# Contributing to reEngine

Thank you for helping build **`reEngine`**, a zero-daemon clean-room static recompilation package router, unified 5-backend `reEngine.Host.dll` runtime, interactive `.decomp` Bring-Up runner, and multi-track engine manager.

## Scope & Clean-Room Rules

1. **Static Recompilation Routing & Shared Host Only:** Keep `reEngine.Host` and `reEngine.Core` cleanly decoupled from platform-specific recompilation logic and legacy emulator framing.
2. **Zero Copyrighted Media or Proprietary Secrets:** Never commit, attach, or bundle copyrighted disc images, ROM dumps, console BIOS/firmware files, retail strings/source bodies, or pre-generated game binaries (`*.g.cs`, `*.Generated.dll`).
3. **Zero Background Daemons:** All processes must exit cleanly when work completes (`AllowAutoStart = false`, zero background services or startup entries).
4. **Release Automation Isolation:** Never allow `Foundation.Automation` or `System.Windows.Forms` references to leak into `Release` `reEngine.Core.dll`, `reEngine.Host.dll` (`net8.0`), `reEngine.exe`, or `reEngine.Installer.exe` (`ExitCode = 2` when `--automation-port` is passed in `Release`).
5. **Licensing (`MPL-2.0`):** All original contributions are accepted under the **Mozilla Public License Version 2.0** ([LICENSE](LICENSE)). Do not introduce incompatible GPL-only runtime dependencies without explicit isolation.

## Running Isolated Tests Locally

All automated tests run strictly isolated under `APP_ISOLATED_TEST=1` without mutating user installations or Windows Registry keys:

```powershell
$env:APP_ISOLATED_TEST = "1"
dotnet build Dev\reEngine.slnx -c Release /warnaserror
dotnet test Dev\reEngine.slnx -c Release --no-build
dotnet build Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror
dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true --no-build
```

## Pull Request Checklist

- Zero compiler warnings in both `Release` and `Debug -p:ReEngineTestAutomation=true` (`/warnaserror`).
- Zero workstation-specific paths, secrets, or personal data in code or documentation.
- Public JSON config/manifest keys follow consistent `camelCase` naming and preserve unknown fields cleanly (`[JsonExtensionData]`).
- Relevant technical manuals (`Dev\docs\`) updated alongside any public contract or CLI change.
