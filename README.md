<div align="center">

<img src="Dev/assets/reEngine.png" alt="reEngine Logo" width="96" />

# reEngine

Lightweight, zero-daemon package router, unified 5-backend SDL3 host runtime (`reEngine.Host`), clean-room decompilation coordinator, and engine version manager for static recompilation modules.

![Version](https://img.shields.io/badge/version-0.5.0--beta-blue?style=flat) ![License](https://img.shields.io/badge/license-MPL--2.0-green?style=flat) ![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat)

**Milestone Progress:** `▓▓▓▓▓▓░░░░` **62% toward v1.0.0** (5/8 milestones) — `v0.5.0` adds `HostViewportGeometry` 5-aspect modes & overscan crop across all 5 presenters, OpenGL FBO/readback & pre-QuickSave sync, 4-port SDL3 gamepads & 3-tier input profiles, interactive `F1` drawer controls & `F5`/`F9`/`F11`/`Alt+Enter` hotkeys, 3-way `reCart`/`reCartOne`/`reNes` routing, `-hf.<n>` hotfix precedence, `hostProtocolVersion` >= `0.3.0` forwarding, `.fds` discovery, `--aspect-mode`, and Dual-Binary Release `ExitCode = 2`

</div>

---

## About

> **Beta (Pre-1.0):** `reEngine` (`v0.5.0`) is an active pre-1.0 beta release targeting `v1.0.0` desktop stability and `v2.0.0` cross-platform support.

`reEngine` routes `.recomp` and `.decomp` packages to compatible platform static recompilation engines (`reStation` / `reSlate`, `reCart` / `reCartOne` / `reNes`, `re64` / `reCart64`, `reGenesis` / `reBlast16`, `reCube`, `rePS2`, `reAdvance`) while leaving **0 MB** resident in memory when closed and adding **zero** Windows Startup entries. Built strictly for native static recompilation rather than legacy console simulation or bundled retail data, `reEngine` provides a clean-room `reEngine.Host.dll` video/audio/input runtime, isolates per-game settings and logs under `<dataDir>\modules\<moduleId>\`, manages local `<dataDir>\cache\` Ahead-of-Time (AOT) assemblies, and discovers your legally dumped disc or ROM files inside each game's `data\rom\` folder or suite directory tree.

## Key Features

- **5-Backend `HostViewportGeometry` & GL Readback Sync (`reEngine.Host.dll`):** Ships native OpenGL 3.3+ (`opengl`), Vulkan 1.3+ (`vulkan`), DirectX 12 (`directx` / CLI `dx12`), deterministic 16px-band `parallelCpu`, and `software` presenters with centralized `HostViewportGeometry` aspect preservation (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`), bounded `overscanCropPx`, inverse normalized content pointer mapping, OpenGL `0x0` / `0x8CD5` FBO safety guards, owner-thread `IHostGraphicsContext` surface readback (`TryReadSurfaceRect`) and pre-QuickSave / teardown GPU sync (`FlushAndSyncForSnapshot`), `<dataDir>\hdpacks\` texture overrides, `wasapi` audio pinning, `--aspect-mode`, and live `F11` / `Alt+Enter` fullscreen transitions.
- **4-Port SDL3 Gamepads, 3-Tier Input & Interactive `F1` Drawer:** Polls up to 4 hot-pluggable Xbox and open-standard HID SDL3 gamepads (`PadPorts[0..3]`), persists 3-tier input profiles (`Global 1:1`, `Game 1:1`, `Modern PC`) in `settings.controls.json` with collision and hidden-slot guards, isolates Menu `D-Pad` and `Left Stick` navigation, supports 3-slot `[1st, 2nd, 3rd]` simultaneous keyboard/mouse/wheel bindings, and enables interactive `F1` drawer controls (`AspectRatioMode`, `OverscanCrop`, `FastForward`, `CutsceneSkip`, `Controls`, 3-tier `Keybinds`, and `ExtraRows`), `F3` HUD telemetry, `F5`/`F9`/`F11`/`Alt+Enter` hotkeys, responsive SDL startup event pumping, and cross-platform touch overlay contracts (`IHostInputProvider`).
- **Interactive `.decomp` Bring-Up UI & `.fds` Suite Media Discovery:** Opening `.decomp` files from Windows Explorer launches the **Bring-Up** shelf pre-loaded with the recipe, `D0..D5` stage picker, automatic root-tree media search (`SuiteMediaTreeWalker` with `.fds` Famicom Disk System parity alongside `.nes` and disc images), `MissingMediaPromptDialog` (defaulting to use-in-place with opt-in `.cue`/`.bin` staging into `data\rom\`), and canonical `D4_CleanSource` clean-room stage validation.
- **3-Way Aliases, `hostProtocolVersion` Routing & `-hf.<n>` SemVer:** Resolves 3-way `reCart`/`reCartOne`/`reNes` aliases alongside `reSlate`/`reStation`, `reCart64`/`re64`, and `reBlast16`/`reGenesis`; ranks `-hf.<n>` hotfixes strictly above stable releases while preserving `.ready` enforcement on `0.3.0+`; forwards `hostProtocolVersion >= 0.3.0` CLI flags cleanly to pre-`0.3.0` engine versions (`--backend`, `--fullscreen`, `--audio-driver`, `--aspect-mode`); and supports bounded `<= 3s` `--wait` `WM_CLOSE` save flushing plus standalone `--verify-module`.
- **Zero-Daemon Updates, Unified Settings & Dual-Binary Release Guard:** Separates fast `2s` feed checks from resumable HTTP `Range` `.part` downloads with SHA-256 verification and atomic `.ready` promotion, consolidates config at `<InstallRoot>\data\config\settings.json` with `.bak` recovery, and enforces fail-closed Release automation rejection (`ExitCode = 2`) across both the router and Setup EXE.

## Getting Started

### Prerequisites

- **Windows 10 / 11 (`win-x64`)**
- **.NET 8.0 SDK / Desktop Runtime** *(the single-file Setup EXE bundles runtime dependencies; portable ZIP and source builds require the .NET 8.0 Desktop Runtime / SDK)*

### Releases & Downloads

Every release is published exclusively on [GitHub Releases](https://github.com/StreamItAllGit/reEngine/releases) in both formats:

- **`reEngine-Setup-v0.5.0-win-x64.exe`** — Signed Setup Wizard & silent CLI installer (detects existing install directories and settings automatically on upgrade).
- **`reEngine-v0.5.0-win-x64-Portable.zip`** — Standalone no-installer portable bundle containing `reEngine.exe`, `app\`, scripts, and sanitized release docs.

### Quick Try

```powershell
# Install silently in Lightweight mode (or Full for the offline CLI decompilation toolchain)
reEngine-Setup-v0.5.0-win-x64.exe --silent --no-launch --profile Lightweight

# Open the reEngine Suite Manager UI
reEngine.exe --window

# Launch a clean-room .recomp module synchronously from a frontend with DirectX 12, 4:3 aspect lock, fullscreen, and save flushing
reEngine.exe --open "D:\Games\MyGame\app\MyGame.recomp" --backend directx --aspect-mode fourByThree --scale 4 --fullscreen --save-dir ".\tmp-saves" --shutdown-timeout-ms 3000 --wait

# Verify a .recomp or .decomp package cleanly as JSON
reEngine.exe --verify-module ".\SampleModule.decomp" --json

# Run a clean-room .decomp recipe locally
reEngine.exe decomp --module ".\SampleModule.decomp" --stage D4 --data-dir .\tmp-data --log-dir .\tmp-logs
```

<details>
<summary>Developer verification, CLI overrides, and diagnostic options</summary>

```powershell
# Run all 21 isolated xUnit suites (117/117 tests passing in Release & Debug+Automation with 0 warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\reEngine.slnx -c Release /warnaserror
dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror

# Common CLI diagnostics and path overrides
reEngine.exe --check-updates --strict
reEngine.exe --register-assoc --data-dir .\tmp-data --log-dir .\tmp-logs
reEngine.exe --verify --data-dir .\tmp-data --config .\tmp-data\config\settings.json --log-dir .\tmp-logs
reEngine.exe --verify-module ".\SampleModule.recomp" --json --require-media
reEngine.exe --host-demo --backend opengl --require-backend --aspect-mode fourByThree --window --frames 120 --dump-screenshot .\tmp-logs\frame.png --dump-audio .\tmp-logs\audio.wav
reEngine.exe --version
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate a `.recomp` manifest, resolve a compatible engine or alias (`reCart`/`reCartOne`/`reNes`), isolate `<dataDir>\modules\<moduleId>\`, discover media (including `.fds`), and launch. |
| `decomp --module <path>` | Load a `.decomp` package into the **Bring-Up** UI (or run headless via `--headless` with `--stage D0..D5`, `--media-path`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--verify-module <pathOrEngineId>` | Validate a `.recomp`/`.decomp` package (`--json`, `--require-media`) or inspect installed tracks for a bare engine ID. |
| `--host-demo` | Launch the standalone `reEngine.Host` SDL3 window with 5-backend + 5-aspect presentation, interactive `F1` 5-tab drawer, `F3` HUD, `F5`/`F9`/`F11`/`Alt+Enter`, and `--preload-media` preview. |
| `--backend <id>` / `--aspect-mode <mode>` / `--scale <n>` | Select render backend (`opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, `software`), aspect preset (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`), and scale (`1`, `2`, `4`, `8`). `--require-backend` fails with Exit `6` if unavailable. |
| `--fullscreen` / `--hd-packs` / `--jit-bringup` | Start in fullscreen mode, enable `<dataDir>\hdpacks\` texture overrides, or enable live JIT fallback when no AOT cache is built. |
| `--audio-driver <id>` | Pin `SDL_AUDIO_DRIVER` prior to audio init (`wasapi` default on Windows). |
| `--wait` / `--save-dir` / `--shutdown-timeout-ms` | Wait synchronously, redirect per-game saves, flush child processes via bounded `WM_CLOSE` (`3000` ms default), and propagate exit codes (`0, 1, 2, 3, 4, 6, 130`). |
| `--register-assoc` | Register per-user `.recomp` and `.decomp` file associations under `HKCU\Software\Classes` (virtualized to `virtual_registry.json` in tests). |
| `--profile <Lightweight\|Full>` / `--suite-dir` / `--reengine-dir` | Choose installer profile (`Lightweight` router vs. `Full` offline CLI toolchain) and custom installation directories. |
| `--migrate-legacy` / `--check-updates` (`--strict`) | Opt-in legacy install migration preserving `data\rom\`, or poll engine update feeds (`2s` timeout; `--strict` returns Exit `4` if offline). |
| `--data-dir` / `--config` / `--log-dir` | Redirect writable runtime storage (`data\config\settings.json` default), AOT cache, and diagnostic logs. |
| `--verify` / `--doctor` / `--tray-only` / `--window` / `--browser` | Run non-UI diagnostics, force-open the manager window (`--window`, `--suite-tab <tab>`), or launch browser mode. |
| `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--enable-logs` | Containment-checked capture and diagnostic flags forwarded to child engines (`--automation-port` fails closed with Exit `2` in Release builds of both router and Setup EXE). |

</details>

## Project Structure

- `Dev/` — C# source code (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer`), `21` xUnit test suites (`117` tests), build/E2E scripts, and technical manuals
- `Builds/` — Local compiled portable staging builds
- `Releases/` — Shareable 7-file versioned release bundles (`Releases\v0.5.0\`, `Releases\Latest\`, including the signed Setup EXE and `reEngine-v0.5.0-win-x64-Portable.zip`), SHA-256 checksums, and release notes
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

## Code Signing Policy

Free code signing provided by [SignPath.io](https://signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- **Authors:** Repository contributors and maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Reviewers:** Repository maintainers ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Approvers:** Repository owners ([`@StreamItAllGit`](https://github.com/StreamItAllGit))
- **Privacy Policy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License & Legal

- **Router, Host & SDK License (`MPL-2.0`):** `reEngine` (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, and `reEngine.Installer`) is licensed under the **Mozilla Public License Version 2.0** ([LICENSE](LICENSE)). Verbatim copies of `LICENSE` ship in the repository root, portable `Builds\v<Version>\app\LICENSE`, source archives, and the SDK distribution.
- **Bundled Third-Party Tooling & Runtime Notices:** Windowing and audio use `ppy.SDL3-CS` (`MIT`) and native **SDL3** (`zlib`). Build-time packaging uses `7z.exe` and `7z.dll` ([LICENSE-7ZIP.txt](Dev/assets/native/LICENSE-7ZIP.txt)), distributed under **GNU LGPL-2.1+** with the **unRAR license restriction** (plus BSD 3-clause / BSD 2-clause components). Optional `Full` installs include [THIRDPARTY_LICENSES.txt](Dev/scripts/THIRDPARTY_LICENSES.txt) covering stripped Apache-2.0 CLI Ghidra and a `jlink` OpenJDK runtime (`GPLv2 + Classpath Exception`).
- **Non-Affiliation Notice:** `reEngine` is an independent clean-room software project and is not affiliated with, endorsed by, or sponsored by Sony Interactive Entertainment, Nintendo, Microsoft, Sega, or any game publisher or hardware manufacturer. All product names and trademarks belong to their respective owners.
- **Zero-ROM / Zero-BIOS Guarantee:** `reEngine` contains zero copyrighted retail game binaries, console BIOS/firmware dumps, or proprietary SDK assets. Users must legally supply their own optical discs or cartridge dumps inside each module's `data\rom\` directory or local suite folders.
