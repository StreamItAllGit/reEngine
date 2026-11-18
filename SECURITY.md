# Security Policy

## Supported Versions

| Version | Supported | Security Boundary Notes |
| :--- | :--- | :--- |
| `0.3.x` | Yes | Active mainline (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer`) |
| `0.2.x` | Security fixes only | Prior minor release |
| `< 0.2.0` | No | Upgrade to `v0.3.0+` |

## Built-In Security & Containment Invariants

1. **Zero-Daemon Execution (`AllowAutoStart = false`):**
   `reEngine` never installs persistent background services or Windows sign-in Run registry entries; `--autostart` flags are rejected or ignored.
2. **Release Automation Stripping (`ExitCode = 2`):**
   Shipping `Release` binaries exclude `Foundation.Automation` and fail closed immediately with exit code `2` if `--automation-port` is supplied.
3. **SSRF, Path Containment & Zip-Slip Protection:**
   Remote update URLs enforce private/link-local SSRF IP blocking before streaming `.part` transfers, and `.recomp`/`.decomp` readers and staging unzip routines enforce `AppEnvironment.AssertPathContained`.
4. **Clean-Room & Copyright Boundaries:**
   Tracked repositories, `.decomp` recipes, and `reEngine.Host` binaries contain zero bundled optical disc images, ROMs, retail C function bodies, or generated cache assemblies (`data\rom\` and `<dataDir>\cache\` stay local).
5. **Isolated Test Sandbox (`APP_ISOLATED_TEST=1` / `FOUNDATION_SANDBOX_ROOT`):**
   Test and E2E harnesses enforce strict sandbox path containment (`ExitCode = 3` on host escape) and write associations strictly to virtual registry JSON files instead of live `HKCU`.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for suspected security vulnerabilities.

1. Report privately via GitHub **Security Advisories** (`Report a vulnerability` on the repository Security tab) or direct maintainer contact.
2. Include the affected component (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Installer`), version (`v0.3.x`), reproduction steps, and expected vs. actual behavior.
3. Maintainers acknowledge reports within **72 hours** and coordinate remediation prior to public disclosure.
