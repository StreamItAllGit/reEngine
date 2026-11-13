# Changelog

All notable changes to **`reEngine`** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-11-13

### Added
- Initialized Tier A 6-folder repository scaffold (`Archive\`, `Builds\`, `Dev\`, `Installers\`, `Releases\`, `Sandbox\`), outer Git repository, Root Documentation Quartet, `AGENTS.md`, Manual Shelf docs, `Foundation v0.3.1` SDK sync (`Dev\lib\`), and original [`Dev\assets\reEngine.{svg,png,ico}`](<REPO_ROOT>/Dev/assets/reEngine.svg) brand artwork.
- Implemented `reEngine.slnx` (`Dev\Directory.Build.props`, `Dev\version.txt`) with `TreatWarningsAsErrors` and Release Zero-PII `<PathMap>` + `<DebugType>none</DebugType>`.
- Implemented `reEngine.Core` (`net8.0`, `Foundation.Core`-only): `RecompPackageManifest` + `RecompPackageReader` (JSON/ZIP + Zip-Slip guard + `data\rom\` preload discovery), `EngineVersionResolver` + `EngineTrackMatcher` (side-by-side track resolution), `IFileAssociationStore` + `VirtualRegistryAssociationStore` + `WindowsRegistryAssociationStore`, `UpdateCoordinator` (`GranularSettingsManager` + `SafeNetworkTransfer`), and `ReEngineBootstrapHelper` (`AllowAutoStart = false`).
- Implemented `reEngine.App` (`net8.0-windows`): CLI dispatcher (`--open`, `--register-assoc`, `--check-updates`, `--engines-dir`, `--data-dir`, `--config`, `--log-dir`, `--test-mode`, `--dry-run`, `--headless`, `--visible-test`), `RecompLaunchCoordinator`, and Dark Slate `ReEngineMainForm` (`#0E0E10` / `#18181B` / `#38BDF8`).
- Authored `reEngine.Launcher` (`RootLauncherHost` + `AssemblyResolve`), `reEngine.Installer` (`StandardInstallerHost` + compressed `AssemblyResolve` + `AllowAutoStart = false`), `.bat` helper scripts, and `Dev\scripts\build_release.ps1`; passed `@dean` Gate 2 and packaged `Builds\v0.1.0\`, signed zero-PII `Installers\reEngine-Setup-v0.1.0-win-x64.exe` + `<PROJECTS_ROOT>\Installers\` (`0` audit findings), 6-file `Releases\Latest\` + `.7z`, `Archive\reEngine-v0.1.0-src.7z`, and immutable `<PROJECTS_ROOT>\SharedLibs\reEngine\v0.1.0\` (`+ Latest\`).
- Added `reEngine.Tests` (`14/14` passing xUnit tests) and passed `@inspecter` live `Sandbox\` + `--visible-test` verification (`virtual_registry.json`, `reStation` `0.1.x` resolution, `data\rom\` preload auto-detect, SHA-256 patch staging, and screenshot capture).
