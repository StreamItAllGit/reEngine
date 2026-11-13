# reEngine — Feature Map & Code Registry

> Maintained by `@tessa`. Maps planned and active `reEngine` capabilities (`v0.1.0`) to slices in `Dev\src\` and tests in `Dev\tests\`.

---

## 1. At-a-Glance Feature Summary

| Feature | Status | Version Added | Primary Code Location | 1-Line Summary |
| :--- | :--- | :--- | :--- | :--- |
| **Brand Logo & Identity** | `Live` | `v0.1.0` | [`Dev\assets\reEngine.*`](<REPO_ROOT>/Dev/assets/reEngine.svg) | Original routing-diamond vector SVG, `512px` PNG, and 4-size Windows `.ico`. |
| **`.recomp` Manifest Reader & Schema** | `Live` | `v0.1.0` | [`RecompPackageManifest.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Manifests/RecompPackageManifest.cs) | Parses Standard 18 JSON/ZIP `.recomp` manifests with Zip-Slip guard and `data\rom\` preload discovery. |
| **Side-by-Side Track Resolver** | `Live` | `v0.1.0` | [`EngineVersionResolver.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Resolution/EngineVersionResolver.cs) | Selects highest installed `engines\re<Platform>\v<Ver>\` matching track and minimum version. |
| **`.recomp` File Association Store** | `Live` | `v0.1.0` | [`FileAssociationStores.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Associations/FileAssociationStores.cs) | Registers `HKCU\Software\Classes\.recomp` live or `virtual_registry.json` under `--test-mode`. |
| **Launch Update Check & Patch Staging** | `Live` | `v0.1.0` | [`UpdateCoordinator.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Updates/UpdateCoordinator.cs) | Non-blocking launch update check (`shouldCheckUpdates`, `shouldAutoStagePatches`) using `SafeNetworkTransfer`. |
| **Dark Suite Manager UI & `--visible-test`** | `Live` | `v0.1.0` | [`ReEngineMainForm.cs`](<REPO_ROOT>/Dev/src/reEngine.App/ReEngineMainForm.cs) | Dark Slate WinForms manager listing installed engines, `.recomp` status, and `--visible-test` PNG showcase. |
| **Self-Bootstrapping SDK & Setup EXE** | `Live` | `v0.1.0` | [`ReEngineBootstrapHelper.cs`](<REPO_ROOT>/Dev/src/reEngine.Core/Bootstrap/ReEngineBootstrapHelper.cs) | Reusable `reEngine.Core` bootstrap SDK (`SharedLibs\reEngine\v0.1.0\`), `reEngine.Launcher`, signed `reEngine.Installer` (`AllowAutoStart = false`), and 6-file `Releases\Latest\`. |
