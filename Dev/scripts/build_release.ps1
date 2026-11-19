#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$CertThumbprint = "A5414BDD36F7F113C7B2356F189B7A43F69C35E6",
    [switch]$SkipSourceArchive,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
function Read-Utf8([string]$Path) { [System.IO.File]::ReadAllText($Path, $utf8NoBom) }
function Write-Utf8([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom) }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Get-ChildItem -Path $root -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { -not $_.PSIsContainer -and ($_.Name -in @("desktop.ini", "Thumbs.db", "ehthumbs.db", ".DS_Store") -or $_.Name -like "._*") } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

$devDir = Join-Path $root "Dev"
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = (Read-Utf8 (Join-Path $devDir "version.txt")).Trim() }
$licensePath = Join-Path $root "LICENSE"
$sevenZip = Join-Path $devDir "assets\native\7z.exe"
if (-not (Test-Path $licensePath)) { throw "LICENSE missing at $licensePath" }
if (-not (Test-Path $sevenZip)) { throw "7z.exe missing at $sevenZip" }

$cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction Stop
function Invoke-Sign([string]$PathToSign) {
    $res = Set-AuthenticodeSignature -Certificate $cert -HashAlgorithm SHA256 -FilePath $PathToSign
    if ($res.Status -ne "Valid") { throw "Signing failed for ${PathToSign}: $($res.Status)" }
}

function Invoke-7z([string[]]$ArgsList) {
    & $sevenZip @ArgsList | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z.exe exited $LASTEXITCODE" }
}

function Sanitize-Markdown([string]$Text) {
    $c = $Text.Replace([string]::Concat([char]0x00C2, [char]0x00A7), [string][char]0x00A7)
    $c = [regex]::Replace($c, '\[([^\]]+)\]\(file:///[^)]+\)', '$1')
    $c = [regex]::Replace($c, '\[([^\]]+)\]\((?:\.\.[\\/])*(?:Dev|src|tests|docs)[\\/][^)]+\)', '$1')
    $c = [regex]::Replace($c, '`file:///[^`\r\n]+`|file:///[^\s`)\]]+', '')
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $drop = "(?i)Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_installer\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev Bible\b$userEsc"
    $lines = foreach ($line in ($c -split "\r?\n")) {
        if ($line -match $drop) { continue }
        $s = [regex]::Replace($line, '`?@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor)[a-z0-9-]*`?', '')
        $s = [regex]::Replace($s, "(?i)``?(?:per\s+)?\bStandards?\s+\d+(?:\s*[-–]\s*\d+)?(?:\s*§[\d.]+)?``?", '')
        $s = [regex]::Replace($s, '\s*\(\s*\)', '')
        ([regex]::Replace($s, '(?<=\S) {2,}', ' ')).TrimEnd()
    }
    return (($lines -join "`r`n") -replace "(\r?\n){3,}", "`r`n`r`n").Trim() + "`r`n"
}

function Assert-NoEndUserLeaks([string]$Path, [string]$Text) {
    $userEsc = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $leak = "(?i)file:///|Dev[\\/](?:src|tests|docs|scripts|lib|assets)|SharedLibs[\\/]|audit_installer\.ps1|[CD]:[\\/](?:Projects|Users|Programs)|_DevBible|\bDev Bible\b|@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor)\b$userEsc"
    $m = [regex]::Match($Text, $leak)
    if ($m.Success) { throw "End-user release markdown leak in '$Path': matched '$($m.Value)'." }
}

function Assert-BinaryPurity([string]$DllPath, [string[]]$ForbiddenTokens) {
    if (-not (Test-Path $DllPath)) { throw "Binary missing for purity check: $DllPath" }
    $ascii = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($DllPath))
    foreach ($tok in $ForbiddenTokens) {
        if ($ascii.IndexOf($tok, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Release binary purity violation in '$DllPath': found '$tok'." }
    }
    $deps = [System.IO.Path]::ChangeExtension($DllPath, ".deps.json")
    if (Test-Path $deps) {
        $depsText = Read-Utf8 $deps
        foreach ($tok in $ForbiddenTokens) {
            if ($depsText.IndexOf($tok, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "Release deps.json purity violation in '$deps': found '$tok'." }
        }
    }
}

if (-not $SkipTests) {
    Write-Host "[1/6] Running Release tests with /warnaserror (APP_ISOLATED_TEST=1)..."
    $prevIso = $env:APP_ISOLATED_TEST
    $env:APP_ISOLATED_TEST = "1"
    try {
        & dotnet test (Join-Path $devDir "reEngine.slnx") -c Release /warnaserror
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" }
    } finally {
        if ($null -eq $prevIso) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prevIso }
    }
}

Write-Host "[2/6] Staging single-pass Release build in Builds\v$Version..."
$buildsRoot = Join-Path $root "Builds"
$stageDir = Join-Path $buildsRoot "v$Version"
$stageApp = Join-Path $stageDir "app"
$stageScripts = Join-Path $stageDir "scripts"
$stageInteractive = Join-Path $stageScripts "interactive"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force $stageApp, $stageInteractive | Out-Null

& dotnet publish (Join-Path $devDir "src\reEngine.App\reEngine.App.csproj") -c Release -r win-x64 --self-contained false -o $stageApp /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reEngine.App failed ($LASTEXITCODE)" }

$launcherTmp = Join-Path $env:TEMP "reengine_launcher_$Version"
if (Test-Path $launcherTmp) { Remove-Item $launcherTmp -Recurse -Force }
& dotnet publish (Join-Path $devDir "src\reEngine.Launcher\reEngine.Launcher.csproj") -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $launcherTmp /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reEngine.Launcher failed ($LASTEXITCODE)" }
$launcherBin = Join-Path $launcherTmp "reEngine.exe"
$launcherBytes = (Get-Item $launcherBin).Length
if ($launcherBytes -ge 2MB) { throw "reEngine.Launcher.exe exceeded 2 MB cap ($launcherBytes bytes)" }
Copy-Item $launcherBin (Join-Path $stageDir "reEngine.exe") -Force
Remove-Item $launcherTmp -Recurse -Force -ErrorAction SilentlyContinue

Write-Utf8 (Join-Path $stageApp "version.txt") "$Version"
Copy-Item $licensePath (Join-Path $stageApp "LICENSE") -Force
Copy-Item (Join-Path $devDir "settings.example.json") (Join-Path $stageDir "settings.example.json") -Force
Copy-Item (Join-Path $devDir "scripts\THIRDPARTY_LICENSES.txt") (Join-Path $stageDir "THIRDPARTY_LICENSES.txt") -Force
Copy-Item (Join-Path $devDir "scripts\*.bat") $stageScripts -Force
Copy-Item (Join-Path $devDir "scripts\interactive\*.bat") $stageInteractive -Force
Get-ChildItem $stageDir -Include *.pdb, *.xml -Recurse -File | Remove-Item -Force
Assert-BinaryPurity (Join-Path $stageApp "reEngine.Host.dll") @("Foundation.Automation", "System.Windows.Forms")
Assert-BinaryPurity (Join-Path $stageApp "reEngine.dll") @("Foundation.Automation")

Invoke-Sign (Join-Path $stageApp "reEngine.exe")
Invoke-Sign (Join-Path $stageDir "reEngine.exe")

Get-ChildItem -Path $buildsRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
    Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending |
    Select-Object -Skip 2 | ForEach-Object { Remove-Item $_.FullName -Recurse -Force }

Write-Host "[3/6] Verifying zero PII/forbidden artifacts and building single-EXE installer..."
$bad = @(Get-ChildItem $stageDir -Recurse -File | Where-Object { ($_.Extension -in @(".pdb", ".db", ".sqlite", ".sqlite3", ".log")) -or ($_.Name -ieq ".env") -or ($_.Name -ilike "*secret*.json") })
if ($bad.Count -gt 0) { throw "Forbidden file in staging: $($bad.FullName -join ', ')" }

$workTemp = Join-Path $env:TEMP ("reengine_pkg_" + $Version)
if (Test-Path $workTemp) { Remove-Item $workTemp -Recurse -Force }
New-Item -ItemType Directory -Force $workTemp | Out-Null
$payloadZip = Join-Path $workTemp "payload.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$instOut = Join-Path $workTemp "installer_out"
& dotnet publish (Join-Path $devDir "src\reEngine.Installer\reEngine.Installer.csproj") `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -p:InstallerPayloadZip="$payloadZip" -o $instOut /warnaserror
if ($LASTEXITCODE -ne 0) { throw "publish reEngine.Installer failed ($LASTEXITCODE)" }
$builtInstaller = Get-ChildItem $instOut -Filter *.exe | Select-Object -First 1
if (-not $builtInstaller) { throw "No installer EXE generated under $instOut" }

Write-Host "[4/6] Assembling 7-file Releases\v$Version (+ Portable ZIP) and NTFS-hardlinked Releases\Latest..."
$releasesDir = Join-Path $root "Releases"
$relVerDir = Join-Path $releasesDir "v$Version"
$relLatestDir = Join-Path $releasesDir "Latest"
foreach ($d in @($relVerDir, $relLatestDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force $d | Out-Null
}

$installerName = "reEngine-Setup-v$Version-win-x64.exe"
$installerPath = Join-Path $relVerDir $installerName
Copy-Item $builtInstaller.FullName $installerPath -Force
Invoke-Sign $installerPath

$rawNotes = (@'
# reEngine — Release v__VER__

Clean-room static recompilation suite router, unified 5-backend `reEngine.Host.dll` runtime, interactive `.decomp` Bring-Up runner, and multi-track side-by-side engine manager.

## Highlights
- **Unified 5-Backend `reEngine.Host.dll` & Native Audio:** Adds native `OpenGL 3.3+`, `Vulkan 1.3+`, `DirectX 12` (`directx` / `dx12`), deterministic `ParallelCpu`, and `Software` presenters with `IHostGraphicsContext` interop, path-contained `<dataDir>\hdpacks\` texture overrides, `--require-backend` (`ExitCode = 6`) or graceful fallback, and native OS audio pinning (`wasapi`, `pipewire`/`alsa`, `coreaudio`).
- **3-Slot Multi-Bind Input, Gamepads & Upgraded `F1` Drawer:** Supports 3-slot `[1st, 2nd, 3rd]` keyboard/mouse/wheel bindings, Xbox and open-standard HID SDL3 gamepads with rumble, dynamic per-game `Controls` options, a 5-column interactive `Keybinds` drawer, and cross-platform touch overlay contracts.
- **Interactive `.decomp` Bring-Up & Root-Tree Media Walk:** Opening `.decomp` modules launches the `Bring-Up` shelf with `D0..D5` stage execution, automatic root-tree media search, and a Missing Media prompt defaulting to use-in-place with opt-in copy into `data\rom\` (including `.cue` companion `.bin` files).
- **Frontend Bridge, `--verify-module` & `D4_CleanSource`:** Adds `--fullscreen`, `--save-dir`, bounded `<= 3s` graceful `--wait` child-tree teardown so saves flush cleanly, dual-mode `--verify-module` (`--json`), canonical `D4_CleanSource` checks (`D4_FunctionalRewrite` read-alias), and `.bak`-protected `settings.json` upgrades.
'@).Replace('__VER__', $Version)

$docMap = [ordered]@{
    "README.md"        = Sanitize-Markdown (Read-Utf8 (Join-Path $devDir "docs\USER_GUIDE.md"))
    "FEATURES.md"      = Sanitize-Markdown (Read-Utf8 (Join-Path $root "FEATURES.md"))
    "RELEASE_NOTES.md" = Sanitize-Markdown $rawNotes
    "CHANGELOG.md"     = Sanitize-Markdown (Read-Utf8 (Join-Path $root "CHANGELOG.md"))
}
foreach ($kv in $docMap.GetEnumerator()) {
    $outMd = Join-Path $relVerDir $kv.Key
    Assert-NoEndUserLeaks $outMd $kv.Value
    Write-Utf8 $outMd $kv.Value
}

$portableZipName = "reEngine-v$Version-win-x64-Portable.zip"
$portableZipPath = Join-Path $relVerDir $portableZipName
$portableStage = Join-Path $workTemp "portable_stage"
if (Test-Path $portableStage) { Remove-Item $portableStage -Recurse -Force }
New-Item -ItemType Directory -Force $portableStage | Out-Null
Copy-Item -Path (Join-Path $stageDir "*") -Destination $portableStage -Recurse -Force
foreach ($kv in $docMap.GetEnumerator()) {
    Write-Utf8 (Join-Path $portableStage $kv.Key) $kv.Value
}
$portableInstallers = @(Get-ChildItem -Path $portableStage -Recurse -File -Filter '*-Setup-*.exe' -ErrorAction SilentlyContinue)
if ($portableInstallers.Count -gt 0) {
    throw "Portable release must not contain an installer EXE: $($portableInstallers.FullName -join ', ')"
}
[System.IO.Compression.ZipFile]::CreateFromDirectory($portableStage, $portableZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$sumLines = foreach ($fn in @($installerName, $portableZipName, "README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md")) {
    $h = (Get-FileHash (Join-Path $relVerDir $fn) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$h  $fn"
}
[System.IO.File]::WriteAllText((Join-Path $relVerDir "SHA256SUMS.txt"), ($sumLines -join "`r`n") + "`r`n", [System.Text.Encoding]::ASCII)

foreach ($fn in @($installerName, $portableZipName, "README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md", "SHA256SUMS.txt")) {
    $src = Join-Path $relVerDir $fn
    $dst = Join-Path $relLatestDir $fn
    try { New-Item -ItemType HardLink -Path $dst -Target $src | Out-Null } catch { Copy-Item $src $dst -Force }
}

$relVerZip = Join-Path $releasesDir "reEngine-v$Version-win-x64.7z"
$relLatestZip = Join-Path $releasesDir "Latest.7z"
Remove-Item $relVerZip, $relLatestZip -Force -ErrorAction SilentlyContinue
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", $relVerZip, (Join-Path $relVerDir "*"))
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", $relLatestZip, (Join-Path $relLatestDir "*"))

$hubReleasesParent = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "Releases\Personal"
New-Item -ItemType Directory -Force $hubReleasesParent | Out-Null
$hubJunction = Join-Path $hubReleasesParent "reEngine"
if (-not (Test-Path $hubJunction)) { & cmd /c mklink /J "`"$hubJunction`"" "`"$releasesDir`"" | Out-Null }

$auditScript = Join-Path $env:USERPROFILE ".gemini\config\skills\dev\scripts\audit_installer.ps1"
if (Test-Path $auditScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $auditScript -InstallerPath $relVerDir -StagingDir $stageDir -ExpectedVersion $Version
    if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed ($LASTEXITCODE)" }
}

$vm = [regex]::Match($Version, '^(\d+)\.(\d+)\.(\d+)')
if ($vm.Success) {
    $curMaj = [int]$vm.Groups[1].Value; $curMin = [int]$vm.Groups[2].Value
    Get-ChildItem -Path $releasesDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+-' } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    Get-ChildItem -Path $releasesDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name.Substring(1) } -Descending | Select-Object -Skip 2 |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    $arcRows = @(Get-ChildItem -Path $releasesDir -File -Filter "*.7z" -ErrorAction SilentlyContinue | ForEach-Object {
        $am = [regex]::Match($_.Name, '^(?:reEngine-)?v(\d+)\.(\d+)\.(\d+)')
        if ($am.Success) { [PSCustomObject]@{ File = $_; Maj = [int]$am.Groups[1].Value; Min = [int]$am.Groups[2].Value; Key = ([int]$am.Groups[1].Value * 10000 + [int]$am.Groups[2].Value) } }
    })
    $allKeys = [System.Collections.Generic.List[int]]::new(); $allKeys.Add($curMaj * 10000 + $curMin)
    foreach ($a in $arcRows) { $allKeys.Add($a.Key) }
    $keepKeys = @($allKeys | Sort-Object -Descending -Unique | Select-Object -First 3)
    foreach ($a in $arcRows) { if (($a.Maj -eq $curMaj -and $a.Min -lt ($curMin - 2)) -or ($a.Maj -lt $curMaj -and $keepKeys -notcontains $a.Key)) { Remove-Item -LiteralPath $a.File.FullName -Force } }
}

Write-Host "[5/6] Publishing immutable SharedLibs\reEngine\v$Version (Release net8.0 purity verified)..."
$sharedRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "SharedLibs\reEngine"
$coreOut = Join-Path $devDir "src\reEngine.Core\bin\Release\net8.0"
$hostNet8Out = Join-Path $devDir "src\reEngine.Host\bin\Release\net8.0"
& dotnet build (Join-Path $devDir "src\reEngine.Host\reEngine.Host.csproj") -c Release -f net8.0 /warnaserror | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Release net8.0 build of reEngine.Host failed" }
Assert-BinaryPurity (Join-Path $hostNet8Out "reEngine.Host.dll") @("Foundation.Automation", "System.Windows.Forms")

$sdl3Src = Join-Path $stageApp "SDL3.dll"
if (-not (Test-Path $sdl3Src)) { $sdl3Src = Join-Path $stageApp "runtimes\win-x64\native\SDL3.dll" }

foreach ($sdkDir in @((Join-Path $sharedRoot "v$Version"), (Join-Path $sharedRoot "Latest"))) {
    New-Item -ItemType Directory -Force $sdkDir | Out-Null
    foreach ($fn in @("reEngine.Core.dll", "reEngine.Core.xml", "reEngine.Core.deps.json")) { Copy-Item (Join-Path $coreOut $fn) (Join-Path $sdkDir $fn) -Force }
    foreach ($fn in @("reEngine.Host.dll", "reEngine.Host.xml", "reEngine.Host.deps.json")) { Copy-Item (Join-Path $hostNet8Out $fn) (Join-Path $sdkDir $fn) -Force }
    Assert-BinaryPurity (Join-Path $sdkDir "reEngine.Host.dll") @("Foundation.Automation", "System.Windows.Forms")
    if (Test-Path $sdl3Src) { Copy-Item $sdl3Src (Join-Path $sdkDir "SDL3.dll") -Force }
    Copy-Item $licensePath (Join-Path $sdkDir "LICENSE") -Force
    Copy-Item (Join-Path $devDir "scripts\THIRDPARTY_LICENSES.txt") (Join-Path $sdkDir "THIRDPARTY_LICENSES.txt") -Force
    Copy-Item (Join-Path $relLatestDir $installerName) (Join-Path $sdkDir $installerName) -Force
    $artifacts = @(Get-ChildItem $sdkDir -File | Where-Object { $_.Name -ne "sdk-manifest.json" } | Sort-Object Name | ForEach-Object {
        [ordered]@{ fileName = $_.Name; fileSizeBytes = $_.Length; sha256Hex = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifestJson = ([ordered]@{ schemaVersion = 1; appId = "reengine"; displayName = "reEngine"; sdkVersion = $Version; version = $Version; updatedAtUtc = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"); artifacts = $artifacts } | ConvertTo-Json -Depth 5) + "`r`n"
    Write-Utf8 (Join-Path $sdkDir "sdk-manifest.json") $manifestJson
}

if (-not $SkipSourceArchive) {
    Write-Host "[6/6] Updating Archive\reEngine-v$Version-src.7z..."
    $srcArchive = Join-Path $root "Archive\reEngine-v$Version-src.7z"
    Remove-Item $srcArchive -Force -ErrorAction SilentlyContinue
    Push-Location $root
    try { Invoke-7z @("a", "-t7z", "-mx=9", "-xr!bin", "-xr!obj", "-xr!TestResults", "-xr!data", "-xr!logs", "-xr!.vs", "-xr!*.pdb", "-xr!*.tmp", "-xr!*.bak", "-xr!*.user", $srcArchive, "Dev", "README.md", "FEATURES.md", "ROADMAP.md", "CHANGELOG.md", "AGENTS.md", "LICENSE", "SECURITY.md", "active.state") }
    finally { Pop-Location }
}

Remove-Item $workTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE: Single-pass build, signed installer, 7-file Releases (+ Portable ZIP), hardlinked Latest, and SharedLibs SDK staged for v$Version."
