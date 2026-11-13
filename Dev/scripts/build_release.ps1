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

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $devDir "version.txt") -Raw).Trim()
}
$sevenZip = Join-Path $devDir "assets\native\7z.exe"
if (-not (Test-Path $sevenZip)) { throw "7z.exe not found at $sevenZip" }

$cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction Stop
function Invoke-Sign([string]$PathToSign) {
    $res = Set-AuthenticodeSignature -Certificate $cert -HashAlgorithm SHA256 -FilePath $PathToSign
    if ($res.Status -ne "Valid") {
        throw "Authenticode signing failed for ${PathToSign}: $($res.Status) - $($res.StatusMessage)"
    }
}

function Invoke-7z([string[]]$SevenZipArgs) {
    & $sevenZip @SevenZipArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z.exe exited with $LASTEXITCODE ($($SevenZipArgs -join ' '))" }
}

function Sanitize-MarkdownForEndUser([string]$Text) {
    $lines = $Text -split "\r?\n"
    $filtered = foreach ($line in $lines) {
        if ($line -match 'Dev\\(?:src|tests|docs\\plans|docs\\reports)|Sandbox\\|--test-mode|--visible-test|@pland|@inspecter|@dean|@devv|@tessa') {
            continue
        }
        $line
    }
    return (($filtered -join "`r`n").Trim() + "`r`n")
}

if (-not $SkipTests) {
    Write-Host "[1/7] Running Release tests with /warnaserror..."
    & dotnet test (Join-Path $devDir "reEngine.slnx") -c Release /warnaserror
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" }
}

Write-Host "[2/7] Staging portable build in Builds\v$Version..."
$stageDir = Join-Path $root "Builds\v$Version"
$stageApp = Join-Path $stageDir "app"
$stageScripts = Join-Path $stageDir "scripts"
$stageInteractive = Join-Path $stageScripts "interactive"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force $stageApp, $stageInteractive | Out-Null

& dotnet publish (Join-Path $devDir "src\reEngine.App\reEngine.App.csproj") `
    -c Release -r win-x64 --self-contained false -o $stageApp /warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish reEngine.App failed ($LASTEXITCODE)" }

$launcherStage = Join-Path $env:TEMP "reengine_launcher_$Version"
if (Test-Path $launcherStage) { Remove-Item $launcherStage -Recurse -Force }
& dotnet publish (Join-Path $devDir "src\reEngine.Launcher\reEngine.Launcher.csproj") `
    -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $launcherStage /warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish reEngine.Launcher failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $launcherStage "reEngine.exe") (Join-Path $stageDir "reEngine.exe") -Force
Remove-Item $launcherStage -Recurse -Force -ErrorAction SilentlyContinue

[System.IO.File]::WriteAllText((Join-Path $stageApp "version.txt"), "$Version", [System.Text.UTF8Encoding]::new($false))
Copy-Item (Join-Path $devDir "scripts\*.bat") $stageScripts -Force
Copy-Item (Join-Path $devDir "scripts\interactive\*.bat") $stageInteractive -Force
Get-ChildItem $stageDir -Include *.pdb, *.xml -Recurse -File | Remove-Item -Force

$runtimeAgents = (@'
# Runtime Context: reEngine (v__VER__)

- **Launcher:** `reEngine.exe` (forwards to `app\reEngine.exe`)
- **Engines Root:** `engines\re<Platform>\v<Ver>\` (side-by-side non-breaking tracks)
- **Data & Logs:** `data\settings.json` and `logs\` unless overridden by `--data-dir`, `--config`, `--log-dir` (or `REENGINE_{DATA_DIR,CONFIG,LOG_DIR}`).
- **Daemon Policy:** Zero background daemons; `AllowAutoStart = false`.
'@).Replace('__VER__', $Version)
[System.IO.File]::WriteAllText((Join-Path $stageDir "AGENTS.md"), $runtimeAgents + "`r`n", [System.Text.UTF8Encoding]::new($false))

Invoke-Sign (Join-Path $stageApp "reEngine.exe")
Invoke-Sign (Join-Path $stageDir "reEngine.exe")

Write-Host "[3/7] Running pre-package Zero-PII & forbidden-extension scan..."
$forbiddenExts = @(".pdb", ".db", ".sqlite", ".sqlite3", ".log")
$badFiles = @(Get-ChildItem $stageDir -Recurse -File | Where-Object {
    ($_.Extension -in $forbiddenExts) -or ($_.Name -ieq ".env") -or ($_.Name -ilike "*secret*.json")
})
if ($badFiles.Count -gt 0) {
    throw "Forbidden staging file(s) detected: $($badFiles.FullName -join ', ')"
}

Write-Host "[4/7] Building & auditing signed single-EXE installer..."
$workTemp = Join-Path $env:TEMP ("reengine_pkg_" + $Version)
if (Test-Path $workTemp) { Remove-Item $workTemp -Recurse -Force }
New-Item -ItemType Directory -Force $workTemp | Out-Null
$payloadZip = Join-Path $workTemp "payload.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$instOut = Join-Path $workTemp "installer_out"
& dotnet publish (Join-Path $devDir "src\reEngine.Installer\reEngine.Installer.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false -p:InstallerPayloadZip="$payloadZip" `
    -o $instOut /warnaserror
if ($LASTEXITCODE -ne 0) { throw "dotnet publish reEngine.Installer failed ($LASTEXITCODE)" }

$builtInstaller = Get-ChildItem $instOut -Filter *.exe | Select-Object -First 1
if (-not $builtInstaller) { throw "No installer EXE generated under $instOut" }

$installerName = "reEngine-Setup-v$Version-win-x64.exe"
$installersDir = Join-Path $root "Installers"
New-Item -ItemType Directory -Force $installersDir | Out-Null
$installerPath = Join-Path $installersDir $installerName
Copy-Item $builtInstaller.FullName $installerPath -Force
Invoke-Sign $installerPath

$centralInstallers = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "Installers"
New-Item -ItemType Directory -Force $centralInstallers | Out-Null
Get-ChildItem $centralInstallers -Filter "reEngine-Setup-*.exe" -File | Remove-Item -Force
Copy-Item $installerPath (Join-Path $centralInstallers $installerName) -Force

$releaseNotes = (@'
# reEngine — Release v__VER__

Zero-daemon `.recomp` suite router, side-by-side platform engine track resolver, and self-bootstrapping SDK (`AllowAutoStart = false`).

## Highlights
- Parses Standard 18 `.recomp` JSON and ZIP manifests with fail-closed Zip-Slip and `data\rom\` preload media discovery.
- Resolves highest compatible `engines\re<Platform>\v<Ver>\` track (`>= engineVersion`) while preserving older breaking tracks.
- Registers per-user `.recomp` Windows shell association (`HKCU\Software\Classes`) or `virtual_registry.json` under `--test-mode`.
- Non-blocking launch update check (`SafeNetworkTransfer` + SHA-256 verification) and Dark Slate WinForms manager UI.
- Signed single-EXE 4-pillar installer (`AllowAutoStart = false`) and immutable `SharedLibs\reEngine\v__VER__\` SDK drop.
'@).Replace('__VER__', $Version)
[System.IO.File]::WriteAllText((Join-Path $installersDir "RELEASE_NOTES.md"), $releaseNotes + "`r`n", [System.Text.UTF8Encoding]::new($false))

$auditScript = Join-Path $env:USERPROFILE ".gemini\config\skills\release-packaging\scripts\audit_installer.ps1"
if (Test-Path $auditScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $auditScript -InstallerPath $installersDir -StagingDir $stageDir -ExpectedVersion $Version
    if ($LASTEXITCODE -ne 0) { throw "audit_installer.ps1 failed ($LASTEXITCODE)" }
}

Write-Host "[5/7] Assembling 6-file Releases\v$Version and Releases\Latest..."
$releasesDir = Join-Path $root "Releases"
$relVerDir = Join-Path $releasesDir "v$Version"
$relLatestDir = Join-Path $releasesDir "Latest"
foreach ($d in @($relVerDir, $relLatestDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force $d | Out-Null
}

$userGuide = Get-Content (Join-Path $devDir "docs\USER_GUIDE.md") -Raw
$endUserReadme = Sanitize-MarkdownForEndUser $userGuide
$endUserFeatures = Sanitize-MarkdownForEndUser (Get-Content (Join-Path $root "FEATURES.md") -Raw)
$changelogText = Get-Content (Join-Path $root "CHANGELOG.md") -Raw

foreach ($dir in @($relVerDir, $relLatestDir)) {
    Copy-Item $installerPath (Join-Path $dir $installerName) -Force
    [System.IO.File]::WriteAllText((Join-Path $dir "README.md"), $endUserReadme, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $dir "FEATURES.md"), $endUserFeatures, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $dir "RELEASE_NOTES.md"), $releaseNotes + "`r`n", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $dir "CHANGELOG.md"), $changelogText, [System.Text.UTF8Encoding]::new($false))

    $sumLines = foreach ($fn in @($installerName, "README.md", "FEATURES.md", "RELEASE_NOTES.md", "CHANGELOG.md")) {
        $h = (Get-FileHash (Join-Path $dir $fn) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$h  $fn"
    }
    [System.IO.File]::WriteAllText((Join-Path $dir "SHA256SUMS.txt"), ($sumLines -join "`r`n") + "`r`n", [System.Text.Encoding]::ASCII)
}

$relVerZip = Join-Path $releasesDir "v$Version.7z"
$relLatestZip = Join-Path $releasesDir "Latest.7z"
if (Test-Path $relVerZip) { Remove-Item $relVerZip -Force }
if (Test-Path $relLatestZip) { Remove-Item $relLatestZip -Force }
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", $relVerZip, (Join-Path $relVerDir "*"))
Invoke-7z @("a", "-t7z", "-mx=9", "-mmt=on", $relLatestZip, (Join-Path $relLatestDir "*"))

if (-not $SkipSourceArchive) {
    Write-Host "[6/7] Updating Archive\reEngine-v$Version-src.7z..."
    $srcArchive = Join-Path $root "Archive\reEngine-v$Version-src.7z"
    if (Test-Path $srcArchive) { Remove-Item $srcArchive -Force }
    Push-Location $root
    try {
        Invoke-7z @("a", "-t7z", "-mx=9", "-xr!bin", "-xr!obj", "-xr!*.user", $srcArchive, "Dev", "README.md", "FEATURES.md", "ROADMAP.md", "CHANGELOG.md", "AGENTS.md", "active.state")
    } finally {
        Pop-Location
    }
}

Write-Host "[7/7] Publishing immutable SDK drop to SharedLibs\reEngine\v$Version and Latest..."
$sharedRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "SharedLibs\reEngine"
$coreOut = Join-Path $devDir "src\reEngine.Core\bin\Release\net8.0"
foreach ($sdkDir in @((Join-Path $sharedRoot "v$Version"), (Join-Path $sharedRoot "Latest"))) {
    New-Item -ItemType Directory -Force $sdkDir | Out-Null
    foreach ($fn in @("reEngine.Core.dll", "reEngine.Core.xml", "reEngine.Core.deps.json")) {
        Copy-Item (Join-Path $coreOut $fn) (Join-Path $sdkDir $fn) -Force
    }
    Copy-Item $installerPath (Join-Path $sdkDir $installerName) -Force
    $artifacts = @(
        "reEngine.Core.dll",
        "reEngine.Core.xml",
        "reEngine.Core.deps.json",
        $installerName
    ) | ForEach-Object {
        $p = Join-Path $sdkDir $_
        [ordered]@{
            fileName      = $_
            fileSizeBytes = (Get-Item $p).Length
            sha256Hex     = (Get-FileHash $p -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifestObj = [ordered]@{
        schemaVersion = 1
        appId         = "reengine"
        displayName   = "reEngine"
        sdkVersion    = $Version
        version       = $Version
        updatedAtUtc  = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        artifacts     = $artifacts
    } | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText((Join-Path $sdkDir "sdk-manifest.json"), $manifestObj + "`r`n", [System.Text.UTF8Encoding]::new($false))
}

Remove-Item $workTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE: Build, signed installer, 6-file Releases, archive, and SharedLibs SDK staged for v$Version."
