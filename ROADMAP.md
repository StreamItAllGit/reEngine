# reEngine — Roadmap & Milestones

**Current Active Version:** `0.1.0`  
**Lifecycle State:** `active.state`  
**Last Updated:** 2026-11-13

## 1. North Star Goal
Provide a lightweight, zero-daemon top-level `.recomp` suite router and engine version manager (`<PROGRAMS_ROOT>\reEngine\`) shared automatically by `reStation`, future `re64`/`reCube` engines, and `<Game>Recomp` installers—with immutable version retention, `.recomp` Windows shell integration, canonical `data\rom\` preload discovery, and safe launch-time updates.

## 2. Active Milestone (`0.1.0` — Core Router, Track Resolution, Association, Update Staging & Installer)
- [x] Tier A 6-folder scaffold, outer Git, `Foundation v0.3.1` SDK sync, `AGENTS.md`, Root Quartet, Manual Shelf stubs, and original `.svg`/`.png`/`.ico` brand artwork.
- [x] Lock Gate-1 implementation plan (`Dev\docs\plans\v0.1.0.2026-11-13.md`) via `@pland` and `@dean`.
- [x] Implement `reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer`, `Dev\scripts\`, and `reEngine.Tests` (`0` warnings, `<= 400 LOC` slices, `14/14` passing xUnit tests, fail-closed `--test-mode` + `--visible-test`).
- [x] Pass `@dean` Gate 2, execute `build_release.ps1` for signed installer, 6-file `Releases\`, and immutable `SharedLibs\reEngine\v0.1.0\`.
- [x] Verify end-to-end in `Sandbox\` (`@inspecter`) and sync Notion + Git tag `v0.1.0`.

## 3. Upcoming Minor Horizons
- **`0.2.0` — Live Multi-Engine Dispatch Validation**: Validate routing `.recomp` bundles across `reStation v0.1.0` (`KingsFieldRecomp v0.1.3`) and second platform stub (`re64`).
- **`1.0.0` — Going Gold**: Full suite stability milestone upon explicit approval.

## 4. Completed Milestones
- `0.1.0` *(2026-11-13)*: Delivered `reEngine.Core`, `reEngine.App`, `reEngine.Launcher`, `reEngine.Installer` (`AllowAutoStart = false`, `0` `audit_installer.ps1` findings), `14/14` passing xUnit tests (`0` warnings), immutable `SharedLibs\reEngine\v0.1.0\` SDK drop, 6-file `Releases\Latest\`, and `@inspecter` live `Sandbox\` + `--visible-test` verification ([`0.1.0.2026-11-13_13-29-52.-.v010-sandbox-and-visible-test-verify.md`](<REPO_ROOT>/Dev/docs/reports/0.1.0.2026-11-13_13-29-52.-.v010-sandbox-and-visible-test-verify.md)).
