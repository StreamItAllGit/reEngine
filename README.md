<div align="center">

<img src="Dev/assets/reEngine.png" alt="reEngine Logo" width="96" />

# reEngine

Lightweight, zero-daemon package router, unified 5-backend SDL3 host runtime (`reEngine.Host`), clean-room decompilation coordinator, and engine version manager for static recompilation modules.

![Version](https://img.shields.io/badge/version-0.6.2--beta-blue?style=flat) ![License](https://img.shields.io/badge/license-MPL--2.0-green?style=flat) ![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey?style=flat)

**Milestone Progress:** `▓▓▓▓▓░░░░░` **50% toward v1.0.0** (6/12 milestones) — `v0.6.2` is now shipped atop `v0.6.1`, adding pre-launch host resilience and UX parity: thread-safe live AOT compile / Media Gate / drawer controls (`SetAotCompileState`, `SetMediaGateState`, `UpdateDrawerDescriptor`) via `HostSessionOverlayAndSettingsHelper`, automatic `HostSettingsStore` startup hydration & live `F1` dirty-save persistence (`settings.json`, `settings.video.json`, `settings.audio.json`, skipped during in-memory Safe Mode) with explicit CLI flag precedence (`PreferExplicitBackend`/`Scale`/`AspectRatio`), `FirstRunMediaGateView.AcceptPickedPath` Browse error banner retention, zero-jitter (`0 px`) invariant-width `F3` telemetry HUD, and 2-frame deferred synthetic non-hotkey tap release in `HostInputMapper` (`152/152` tests across `29` suites). Next milestone: `v0.7.0`.

</div>

---

## About

> **Beta (Pre-1.0):** `reEngine` (`v0.6.2`) is an active pre-1.0 beta release targeting `v1.0.0` desktop stability and `v2.0.0` cross-platform support.

`reEngine` routes `.recomp` and `.decomp` packages to compatible platform static recompilation engines (`reSlate` / `reStation`, `reCart` / `reCartOne` / `reNes`, `reCart64` / `re64`, `reBlast16` / `reGenesis`, `reSlateTwo` / `rePS2`, `reCartTwo` / `reSnes`, `reDotCart` / `reBoy`, `reCube`, `reAdvance`) while leaving **0 MB** resident in memory when closed and adding **zero** Windows Startup entries. Built strictly for native static recompilation rather than legacy console simulation or bundled retail data, `reEngine` provides a clean-room `reEngine.Host.dll` video/audio/input runtime, isolates per-game settings and logs under `<dataDir>\modules\<moduleId>\`, manages local `<dataDir>\cache\` Ahead-of-Time (AOT) assemblies, and discovers your legally dumped disc or ROM files inside each game's `data\rom\` folder or suite directory tree.

## Key Features

- **5-Backend Presenters, Late-Latch VRR, `21:9`, 8 Filters & Scaled HUD (`reEngine.Host.dll`):** Ships native OpenGL 3.3+ (`opengl`), Vulkan 1.3+ (`vulkan`), DirectX 12 (`directx` / CLI `dx12`), deterministic 16px-band `parallelCpu`, and `software` presenters with live in-session backend switching, `windowed`, `borderlessFullscreen`, and `exclusiveFullscreen` monitor routing, `1x–8x` internal resolution scale, `Framerate` (`Original`/`Enhanced`) + `Target FPS` (`30`, `60`, `120`, `144`, `240`, `Max`), late-latch VRR pacing, `HostViewportGeometry` (`21:9` ultrawide + auto-centered `4:3` FMVs + HUD anchoring), 24-bit color vs 15-bit RGB555 + 4x4 Bayer dithering, all 8 live filters (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite`/`crt-aperture`, `lcdGrid`, `xbrz`, `ntscComposite`), `<dataDir>\hdpacks\` texture overrides, post-present `F12` PNG capture, `F3` `[ORIG]`/`[ENH]`/`[MAX]` + `[FF <N>x]` telemetry badges, and `Clean-Capture Mode` overlay hiding.
- **Scaled Legible Overlays, Audio DSP, Rebindable Host Actions & Glass Media Gate:** Delivers crisp `~1.45x` scaled text (`uiTextScale` `0.75x–2.0x`), 6 full-title `F1` tabs (`Video`, `Audio`, `Controls`, `Keybinds`, `System`, `Media`) with dynamic sizing, scrolling, and `100%` controller navigation, dedicated 2nd-order Butterworth Audio DSP + hot-switched SDL3 playback devices + Sound Test jukebox, expressive input tiers (`Modern PC (Per-Action)`, `Game 1:1`, `Global 1:1`), `Modern WASD + Mouse` + relative mouse capture + sensitivity stepper, customizable `menuControllerChord` (`L3 + R3`, `Select + Start`, `Guide`, `None`), 8 rebindable host actions (`hostActionBindings`), unbound-`Tab` fast-forward hold, non-quitting 4-step `Esc` + `System -> Quit Game`, full-screen Deep Slate Glass Media Gate (`Drag & Drop` + Win32 native pickers + quiet-idle `Launch Demo / Test Cart`), and responsive AOT compile overlay.
- **Engine Precedence, Crash Recovery, Safe Mode & Split-Domain Settings:** Enforces `pinnedEngineVersions` (`moduleId` -> `engineId` -> alias), `incompatibleEngineVersions` SemVer/wildcard/range exclusions, `-hf.<n>` hotfix ranking, prompt-first crash fallback relaunch + closest pre-crash checkpoint/quicksave recovery (`SessionRecoveryLocator`), `<dataDir>\booting.sentinel` Safe Mode recovery (`--safe-mode-reset`), physical-RAM headroom-gated media preloading, Fast Boot, clean-room AOT tiers with CLI-only `interpreterOracle`, atomic split configs (`settings.json` v2, `settings.video.json`, `settings.audio.json`, `settings.controls.json` with idle dirty-check suppression) backed by `data\config_history\` and `data\quarantine\`.
- **Interactive `.decomp` Bring-Up, Suite Media Discovery & `>= 0.6.1` Unified CLI/Launcher:** Launches Explorer-opened `.decomp` files onto the **Bring-Up** shelf with root-tree media discovery (`.fds` parity alongside `.nes` and optical discs), use-in-place `MissingMediaPromptDialog`, 3-way `reCart`/`reCartOne`/`reNes` aliases, unified `--rom`/`--disc`/`--demo-cart`/`--window`/`--overscan`/`--fps-mode`/`--open-settings`/`--show-fps`/`--force-zero-rwx`/`--fast-forward`/`--ui-text-scale` + optional `--require-backend [<id>]` flags gated on `>= 0.6.1`, wait-only `LauncherJobObjectGuard` + bounded `<= 3s` `--wait` `WM_CLOSE` save flushing, and standalone `--verify-module`.
- **Zero-Daemon Updates, Upgrade Preservation & Dual-Binary Release Guard:** Performs fast `2s` feed checks and resumable HTTP `Range` `.part` downloads with SHA-256 verification and atomic `.ready` promotion, preserves all 4 split domain configs and `schemaVersion >= 1` across Setup upgrades, and enforces fail-closed Release automation rejection (`ExitCode = 2`) across both the router and Setup EXE.

## Getting Started

### Prerequisites

- **Windows 10 / 11 (`win-x64`)**
- **.NET 8.0 SDK / Desktop Runtime** *(the single-file Setup EXE bundles runtime dependencies; portable ZIP and source builds require the .NET 8.0 Desktop Runtime / SDK)*

### Releases & Downloads

Every release is published exclusively on [GitHub Releases](https://github.com/StreamItAllGit/reEngine/releases) in both formats:

- **`reEngine-Setup-v0.6.2-win-x64.exe`** — Signed Setup Wizard & silent CLI installer (detects existing install directories and split domain settings automatically on upgrade).
- **`reEngine-v0.6.2-win-x64-Portable.zip`** — Standalone no-installer portable bundle containing `reEngine.exe`, `app\`, scripts, and sanitized release docs.

### Quick Try

```powershell
# Install silently in Lightweight mode (or Full for the offline CLI decompilation toolchain)
reEngine-Setup-v0.6.2-win-x64.exe --silent --no-launch --profile Lightweight

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
# Clone from GitHub
git clone https://github.com/StreamItAllGit/reEngine.git
cd reEngine

# Run all 29 isolated xUnit suites (152/152 tests passing in Release & Debug+Automation with 0 warnings)
$env:APP_ISOLATED_TEST = "1"
dotnet test Dev\reEngine.slnx -c Release /warnaserror
dotnet test Dev\reEngine.slnx -c Debug -p:ReEngineTestAutomation=true /warnaserror

# Common CLI diagnostics and path overrides
reEngine.exe --check-updates --strict
reEngine.exe --register-assoc --data-dir .\tmp-data --log-dir .\tmp-logs
reEngine.exe --verify --data-dir .\tmp-data --config .\tmp-data\config\settings.json --log-dir .\tmp-logs
reEngine.exe --verify-module ".\SampleModule.recomp" --json --require-media
reEngine.exe --host-demo --backend opengl --require-backend --aspect-mode horPlus21By9 --video-filter crtLite --window --ui-text-scale 1.25 --show-fps --frames 120 --dump-screenshot .\tmp-logs\frame.png --dump-audio .\tmp-logs\audio.wav
reEngine.exe --version
```

| CLI Flag | Purpose |
| :--- | :--- |
| `--open <path>` / `<file.recomp>` | Validate a `.recomp` manifest, resolve compatible/pinned engines (`incompatibleEngineVersions` + 3-way aliases), isolate `<dataDir>\modules\<moduleId>\`, discover media, and launch. |
| `--rom <path>` / `--disc <path>` | Override media path cleanly (equivalent aliases for `--media-path`, normalized relative to caller working directory). |
| `decomp --module <path>` | Load a `.decomp` package into the **Bring-Up** UI (or run headless via `--headless` with `--stage D0..D5`, `--media-path`, `--engine-dir`, `--toolchain-dir`, `--output-dir`). |
| `--verify-module <pathOrEngineId>` | Validate a `.recomp`/`.decomp` package (`--json`, `--require-media`) or inspect installed tracks for a bare engine ID. |
| `--host-demo` / `--demo-cart` | Launch the standalone `reEngine.Host` SDL3 window with the Glass Media Gate, 5 backends, 6 aspects, scaled `F1` 6-tab drawer, `F3` telemetry, Sound Test jukebox, or quiet-idle `--demo-cart` mode. |
| `--backend <id>` / `--aspect-mode <mode>` / `--scale <n>` | Select render backend (`opengl`, `vulkan`, `directx`/`dx12`, `parallelCpu`, `software`), aspect preset (`nativePar`, `fourByThree`, `squareInteger`, `nativeFit`, `horPlus16By9`, `horPlus21By9`), and scale (`1`, `2`, `4`, `8`). Optional-value `--require-backend [<id>]` fails with Exit `6` if unavailable. |
| `--display-mode <mode>` / `--monitor <n>` / `--fullscreen` / `--window` | Choose `windowed`, `borderlessFullscreen`, or `exclusiveFullscreen`, target display index (`>= 0`), start fullscreen, or force windowed mode overriding saved fullscreen. |
| `--fps-mode <original\|enhanced>` / `--target-fps <n>` / `--bg-fps-cap <n>` / `--vsync` (`--no-vsync`) | Choose original console cadence (`[ORIG]`) or enhanced VRR frame pacing (`[ENH]`/`[MAX]`, `0` = unlocked), unfocused FPS cap, and swapchain VSync. |
| `--overscan <px\|t,b,l,r>` / `--video-filter <mode>` / `--hud-anchor <mode>` | Apply edge overscan crop (`0px`, `8px`, `16px`, or `t,b,l,r`), live filter (`nearest`, `bilinear`, `sharpBilinear`, `scanlines`, `crtLite`/`crt-aperture`, `lcdGrid`, `xbrz`, `ntscComposite`), and HUD anchor (`safeFourByThree`, `screenEdges`, `custom`). |
| `--true-color` (`--no-true-color`) / `--dithering` (`--no-dithering`) / `--screenshot-mode <mode>` | Toggle 24-bit RGBA vs 15-bit RGB555 + 4x4 Bayer dithering and `F12` PNG capture (`cleanNative` vs `filteredComposite`). |
| `--ui-text-scale <0.75..2.0>` / `--open-settings` / `--show-fps` | Scale overlay and drawer bitmap text (`0.75x–2.0x`), open the `F1` drawer immediately on launch, or start with `F3` HUD telemetry visible. |
| `--execution-tier <mode>` / `--jit-bringup` / `--force-zero-rwx` / `--safe-mode-reset` | Select execution tier (`staticAotOnly`, `aotWithJitFallback`, `aotWithThreadedIr`, `jitBringup`, CLI-only `interpreterOracle`), opt into JIT bring-up, force zero-RWX threaded IR (`aotWithThreadedIr`), or clear crash-sentinel Safe Mode. |
| `--preload-media-to-ram` (`--no-preload-media-to-ram`) / `--fast-boot` (`--no-fast-boot`) / `--fast-forward` (`--no-fast-forward`) / `--ff-multiplier <2.0..4.0>` | Control physical-RAM headroom-checked media preloading, optional splash skip, initial fast-forward state, and fast-forward cadence multiplier. |
| `--hd-packs` / `--audio-driver <id>` / `--audio-device <name>` | Enable `<dataDir>\hdpacks\` texture overrides, pin `SDL_AUDIO_DRIVER` (`wasapi` default on Windows), or select an explicit playback endpoint. |
| `--master-volume`, `--bgm-volume`, `--sfx-volume`, `--voice-volume` | Set normalized `0.0..1.0` master and category gains. |
| `--audio-muted` (`--no-audio-muted`) / `--mute-on-focus-loss` (`--no-mute-on-focus-loss`) | Toggle volume-preserving mute or 5ms anti-pop focus-loss muting. |
| `--audio-lowpass-hz` / `--audio-highpass-hz` / `--stereo-width` / `--audio-resampling` / `--ff-audio-mode` | Configure 2nd-order Butterworth cutoffs, mid-side stereo width (`0..200%`), resampling (`linear`, `sincCubic`), and FF audio clamping (`attenuatedLowPass`, `muted`, `normal`). |
| `--wait` / `--save-dir` / `--shutdown-timeout-ms` | Wait synchronously with `LauncherJobObjectGuard`, redirect per-game saves, flush child processes via bounded `WM_CLOSE` (`3000` ms default), and propagate exit codes (`0, 1, 2, 3, 4, 6, 130`). |
| `--register-assoc` / `--profile <Lightweight\|Full>` / `--suite-dir` / `--reengine-dir` | Register `.recomp`/`.decomp` file associations under `HKCU\Software\Classes` or configure installer profiles and directories. |
| `--migrate-legacy` / `--check-updates` (`--strict`) | Opt-in legacy migration preserving split settings and `data\rom\`, or poll engine update feeds (`2s` timeout; `--strict` returns Exit `4` if offline). |
| `--data-dir` / `--config` / `--log-dir` | Redirect writable runtime storage (`data\config\settings.json` + split `video`/`audio`/`controls` files), AOT cache, and diagnostic logs. |
| `--verify` / `--doctor` / `--tray-only` / `--browser` | Run non-UI diagnostics, exit after diagnostics (`--tray-only`), or launch browser mode. |
| `--frames`, `--fixture`, `--dump-screenshot`, `--dump-audio`, `--enable-logs` | Containment-checked capture and diagnostic flags forwarded to child engines (`--automation-port` fails closed with Exit `2` in Release builds of both router and Setup EXE). |

</details>

## Project Structure

- `Dev/` — C# source code (`reEngine.Core`, `reEngine.Host`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer`), `29` xUnit test suites (`152` tests), build/E2E scripts, and technical manuals
- `Builds/` — Local compiled portable staging builds
- `Releases/` — Shareable 7-file versioned release bundles (`Releases\v0.6.2\`, `Releases\Latest\`, including the signed Setup EXE and `reEngine-v0.6.2-win-x64-Portable.zip`), SHA-256 checksums, and release notes
- `Archive/` — Compressed source snapshots

## Documentation

- [Features](FEATURES.md) — Current capabilities and source mapping
- [Roadmap](ROADMAP.md) — Active progress, desktop milestones, and cross-platform goals
- [Changelog](CHANGELOG.md) — Release history
- [Contributing](CONTRIBUTING.md) — Clean-room rules, local test commands, and pull request checklist
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
