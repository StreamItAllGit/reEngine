<div align="center">

<img src="Dev/assets/reEngine.png" alt="reEngine Logo" width="96" />

# reEngine

Lightweight, zero-daemon package router, shared SDL3 host runtime (`reEngine.Host`), clean-room decompilation coordinator, and engine version manager for static recompilation modules.

![Version](https://img.shields.io/badge/version-0.3.0--beta-blue?style=flat) ![License](https://img.shields.io/badge/license-MPL--2.0-green?style=flat) ![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat)

**Milestone Progress:** `▓▓▓▓░░░░░░` **35% toward v1.0.0** — `v0.3.0` shared `reEngine.Host.dll` SDL3 runtime, resumable updates, series aliases, and clean-room decompilation checks live; `uStation` bridge, standalone research modules, and multi-engine desktop certification remaining

</div>

---

## About

> **Beta (Pre-1.0):** `reEngine` (`v0.3.0`) is an active pre-1.0 beta release targeting `v1.0.0` desktop stability and `v2.0.0` cross-platform support.

`reEngine` routes `.recomp` and `.decomp` packages to compatible platform static recompilation engines (`reStation` / `reSlate`, `reNes` / `reCartOne`, `re64` / `reCart64`, `reGenesis` / `reBlast16`, `reCube`, `rePS2`, `reAdvance`) while leaving **0 MB** resident in memory when closed and adding **zero** Windows Startup entries. Rather than bundling generic cycle-accurate emulators or copyrighted game data, `reEngine` provides a clean-room SDL3 video/audio/input host (`reEngine.Host.dll`), isolates each game's settings and logs under `<dataDir>\modules\<moduleId>\`, manages local `<dataDir>\cache\` Ahead-of-Time (AOT) assemblies, and discovers your legally dumped disc or ROM files inside each game's `data\rom\` folder.

## Key Features

- **Shared `reEngine.Host.dll` SDL3 Runtime:** Console-agnostic SDL3 windowing, software/hardware framebuffer presentation, audio ring-buffer streaming, unified controller/keyboard mapping (`HostInputFrame`), `F1` 5-tab settings drawer (`Video`, `Controls`, `Keybinds`, `System`, `DiscMedia`), `F3` performance HUD, and first-run user media prompt.
- **Zero-Daemon Package & Backend Routing:** Launches `.recomp` packages cleanly in both interactive detached mode (`--open` or clicking **Open .recomp Package...**) and synchronous wait mode (`--wait`), forwarding `--scale` and render backends (`opengl`, `software` in the Library UI dropdown; CLI `--backend` also accepts `vulkan` and `parallelCpu` for upcoming engines) with `0.2.x` compatibility fallback.
- **Resumable Updates & Bidirectional Engine Aliases:** Separates fast `2s` feed checks from HTTP `Range` `.part` downloads with SHA-256 verification, atomic `.staging-<ver>` to `v<Ver>` promotion, version pinning/rollback, and symmetric alias matching (`reSlate` / `reStation`, `reCartOne` / `reNes`, `reCart64` / `re64`, `reBlast16` / `reGenesis`).
- **Clean-Room `.decomp` Orchestration:** Loads platform decompilation providers (`app\<engineId>.Decomp.dll`), auto-detects optional offline CLI Ghidra + OpenJDK toolchains (`--profile Full`), and enforces zero-warning / zero-decompiler-tell clean-room validation.
- **Unified Settings & Dark Slate 3-Shelf Manager:** Consolidates configuration at `<InstallRoot>\data\config\settings.json` (with automatic migration and backups for older installs), organizes `Library`, `Engines`, and `Bring-Up` shelves plus a collapsible `Inspector Drawer`, and preserves user `data\rom\` folders across upgrades.

## Getting Started

### Prerequisites

- **Windows 10 / 11 (`win-x64`)**
- **.NET 8.0 SDK / Desktop Runtime** *(the single-file Setup EXE bundles runtime dependencies; portable ZIP and source builds require the .NET 8.0 Desktop Runtime / SDK)*

### Releases & Downloads

Every release on [GitHub Releases](https://github.com/StreamItAllGit/reEngine/releases) provides both distribution formats:

- **`reEngine-Setup-v0.3.0-win-x64.exe`** — Signed Setup Wizard & silent CLI installer (detects existing install directories and settings automatically on upgrade).
- **`reEngine-v0.3.0-win-x64-Portable.zip`** — Standalone no-installer portable bundle containing `reEngine.exe`, `app\`, scripts, and sanitized release docs.

### Quick Try

```powershell
# Install silently in Lightweight mode (or Full for the offline CLI decompilation toolchain)
reEngine-Setup-v0.3.0-win-x64.exe --silent --no-launch --profile Lightweight

# Open the reEngine Suite Manager UI
reEngine.exe --window

# Launch a clean-room .recomp module synchronously with OpenGL and 4x scaling
reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend opengl --scale 4 --wait

# Run a clean-room .decomp recipe locally
reEngine.exe decomp --module ".\SampleModule.decomp" --stage D4 --data-dir .\tmp-data --log-dir .\tmp-logs
```

<details>
<summary>Developer verification, CLI overrides, and diagnostic options</summary>

```powershell
# Run the full isolated test suite in both Release and Debug+Automation modes (77/77 passing, 0 warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\reEngine.slnx -c Release /warnaserror
dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror

# Common CLI diagnostics and path overrides
reEngine.exe --check-updates --strict
reEngine.exe --register-assoc --data-dir .\tmp-data --log-dir .\tmp-logs
reEngine.exe --verify --data-dir .\tmp-data --config .\tmp-data\config\settings.json --log-dir .\tmp-logs
reEngine.exe --host-demo --window --frames 120 --dump-screenshot .\tmp-logs\frame.png --dump-audio .\tmp-logs\audio.wav
reEngine.exe --version
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate a `.recomp` manifest, resolve a compatible engine or alias, isolate `<dataDir>\modules\<moduleId>\`, detect `data\rom\`, and launch. |
| `decomp --module <path>` | Parse a `.decomp` package (`--stage D0..D5`, `--engine-dir`, `--toolchain-dir`, `--output-dir`) and invoke an installed decompilation provider (returns exit code `3` if unbuilt). |
| `--host-demo` | Launch the standalone `reEngine.Host` SDL3 test window with `F1` drawer, `F3` HUD, and `--preload-media` media gate preview. |
| `--backend <id>` / `--scale <n>` | Select render backend (`opengl`, `software`, or `vulkan`/`parallelCpu` for upcoming `0.3.x` engines) and resolution scale (`1`, `2`, `4`, `8`). `--require-backend` requires a `0.3.0+` engine; `--jit-bringup` enables live JIT fallback. |
| `--wait` | Wait synchronously for the launched engine process and propagate its exact exit code (`0, 1, 2, 3, 4, 5, 6, 130`). |
| `--register-assoc` | Register per-user `.recomp` and `.decomp` file associations under `HKCU\Software\Classes` (isolated to `virtual_registry.json` during tests). |
| `--profile <Lightweight\|Full>` / `--suite-dir` / `--reengine-dir` | Choose installer profile (`Lightweight` router vs. `Full` offline CLI toolchain) and custom installation directories. |
| `--migrate-legacy` (`--confirm-legacy-cleanup`) | Opt-in migration helper for older standalone installs that preserves existing `data\rom\` media. |
| `--check-updates` (`--strict`) | Poll update feeds across installed engine tracks (`2s` timeout) and stage `.part` updates; `--strict` returns exit code `4` if offline. |
| `--data-dir` / `--config` / `--log-dir` / `--save-dir` | Redirect writable runtime storage (`data\config\settings.json` default), AOT cache, logs, and per-game save folders. |
| `--verify` / `--doctor` / `--tray-only` / `--window` / `--browser` | Run non-UI diagnostics (`--verify`, `--verify-module <id>`, `--tray-only`), force-open the manager window (`--window`, `--suite-tab <tab>`), or launch browser mode. |
| `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--enable-logs` | Containment-checked capture and logging flags forwarded to child engines. |

</details>

## Project Structure

- `Dev/` — C# source code (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer`), 14 test suites, build/E2E scripts, and technical manuals
- `Builds/` — Local compiled portable staging builds
- `Releases/` — Shareable 7-file versioned release bundles (`Releases\v0.3.0\`, `Releases\Latest\`, including the signed Setup EXE and `reEngine-v0.3.0-win-x64-Portable.zip`), SHA-256 checksums, and release notes
- `Archive/` — Compressed source snapshots

## Documentation

- [Features](FEATURES.md) — Current capabilities and source mapping
- [Roadmap](ROADMAP.md) — Active progress, desktop milestones, and cross-platform goals
- [Changelog](CHANGELOG.md) — Release history
- [Security](SECURITY.md) — Security boundaries, clean-room guarantees, and vulnerability reporting
- [User Guide](Dev/docs/USER_GUIDE.md) — Installation, UI walkthrough, CLI reference, overlays, and troubleshooting
- [Architecture](Dev/docs/ARCHITECTURE.md) — System design, assembly graph, pipelines, file inventory, and data stores
- [Interfaces](Dev/docs/INTERFACES.md) — Manifest schemas, `reEngine.Host` contracts, CLI contracts, and exit codes
- [Operations](Dev/docs/OPERATIONS.md) — Build verification, offline toolchain staging, sandbox E2E, and release packaging

## License & Legal

- **Router, Host & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0** ([LICENSE](LICENSE)). Verbatim copies of `LICENSE` ship in the repository root, portable `Builds\v<Version>\app\LICENSE`, source archives, and the SDK distribution.
- **Bundled Third-Party Tooling & Runtime Notices:** Windowing and audio use `ppy.SDL3-CS` (`MIT`) and native **SDL3** (`zlib`). Build-time packaging uses `7z.exe` and `7z.dll` ([LICENSE-7ZIP.txt](Dev/assets/native/LICENSE-7ZIP.txt)), distributed under **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause / BSD 2-clause components). Optional `Full` installs include [THIRDPARTY_LICENSES.txt](Dev/scripts/THIRDPARTY_LICENSES.txt) covering stripped Apache-2.0 CLI Ghidra and a `jlink` OpenJDK runtime (`GPLv2 + Classpath Exception`).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room software project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or hardware manufacturer. All product names and trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must legally supply their own optical discs or cartridge dumps inside each module's `data\rom\` directory.
