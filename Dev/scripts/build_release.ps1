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
function Read-Utf8Text([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, $utf8NoBom)
}

function Write-Utf8Text([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
# Scrub OS/Drive junk before packaging or testing
Get-ChildItem -Path $root -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { -not $_.PSIsContainer -and ($_.Name -match '^(?:desktop\.ini.*|Thumbs\.db.*|ehthumbs\.db|\.DS_Store.*|\._.*)$') } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
$devDir = Join-Path $root "Dev"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Read-Utf8Text (Join-Path $devDir "version.txt")).Trim()
}
$licensePath = Join-Path $root "LICENSE"
if (-not (Test-Path $licensePath)) { throw "Root LICENSE file not found at $licensePath" }

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

function Get-EndUserLeakPattern {
    $mojibake = [string]::Concat([char]0x00C2, [char]0x00A7)
    $userPart = ""
    if (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $userPart = "|" + [regex]::Escape($env:USERNAME)
    }
    return "(?i)(?:file:///|@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor)[a-z0-9-]*|\bStandards?\s+\d+|$([regex]::Escape($mojibake))|Dev\\(?:src|tests|docs|scripts|lib|assets)|SharedLibs\\|audit_installer\.ps1|[CD]:[\\/](?:Projects|Users)|_DevBible|\bDev Bible\b$userPart)"
}

function Sanitize-MarkdownForEndUser([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return "`r`n" }
    $mojibake = [string]::Concat([char]0x00C2, [char]0x00A7)
    $sectionSign = [string][char]0x00A7
    $enDash = [string][char]0x2013
    $clean = $Text.Replace($mojibake, $sectionSign)
    $clean = [regex]::Replace($clean, '\[([^\]]+)\]\(file:///[^)]+\)', '$1')
    $clean = [regex]::Replace($clean, '`file:///[^`\r\n]+`', '')
    $clean = [regex]::Replace($clean, 'file:///[^\s`)\]]+', '')

    $userPart = if ([string]::IsNullOrWhiteSpace($env:USERNAME)) { "" } else { "|" + [regex]::Escape($env:USERNAME) }
    $dropRegex = "(?i)Dev\\(?:src|tests|docs|scripts|lib|assets)|SharedLibs\\|audit_installer\.ps1|[CD]:[\\/](?:Projects|Users)|_DevBible|\bDev Bible\b$userPart"
    $handleBacktick = '`@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor)[a-z0-9-]*`'
    $handleBare = '@(?:pland|dean|devv|inspecter|tessa|nora|milo|simon|julian|holly|rowan|coordinator|monitor)[a-z0-9-]*'
    $stdBacktick = "(?i)``(?:per\s+)?\bStandards?\s+\d+(?:\s*(?:$enDash|-)\s*\d+)?(?:\s*$sectionSign[\d.]+)?``"
    $stdBare = "(?i)(?:per\s+)?\bStandards?\s+\d+(?:\s*(?:$enDash|-)\s*\d+)?(?:\s*$sectionSign[\d.]+)?"

    $lines = $clean -split "\r?\n"
    $filtered = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        if ($line -match $dropRegex) { continue }
        $s = [regex]::Replace($line, $handleBacktick, '')
        $s = [regex]::Replace($s, $handleBare, '')
        $s = [regex]::Replace($s, $stdBacktick, '')
        $s = [regex]::Replace($s, $stdBare, '')
        $s = [regex]::Replace($s, '\s*\(\s*\)', '')
        $s = [regex]::Replace($s, '(?<=\S) {2,}', ' ')
        $filtered.Add($s.TrimEnd())
    }
    $joined = ($filtered -join "`r`n")
    $joined = [regex]::Replace($joined, "(\r?\n){3,}", "`r`n`r`n")
    return ($joined.Trim() + "`r`n")
}

function Assert-NoEndUserLeaks([string[]]$FilePaths) {
    $leakRegex = Get-EndUserLeakPattern
    foreach ($fp in $FilePaths) {
        $body = Read-Utf8Text $fp
        $m = [regex]::Match($body, $leakRegex)
        if ($m.Success) {
            throw "End-user markdown sanitization leak detected in '$fp': matched '$($m.Value)'"
        }
    }
}

if (-not $SkipTests) {
    Write-Host "[1/7] Running Release tests with /warnaserror (APP_ISOLATED_TEST=1)..."
    $prevIsolated = $env:APP_ISOLATED_TEST
    $env:APP_ISOLATED_TEST = "1"
    try {
        & dotnet test (Join-Path $devDir "reEngine.slnx") -c Release /warnaserror
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed ($LASTEXITCODE)" }
    } finally {
        if ($null -eq $prevIsolated) { Remove-Item Env:APP_ISOLATED_TEST -ErrorAction SilentlyContinue } else { $env:APP_ISOLATED_TEST = $prevIsolated }
    }
}

Write-Host "[2/7] Staging portable build in Builds\v$Version..."
$buildsRoot = Join-Path $root "Builds"
$stageDir = Join-Path $buildsRoot "v$Version"
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

Write-Utf8Text (Join-Path $stageApp "version.txt") "$Version"
Copy-Item $licensePath (Join-Path $stageApp "LICENSE") -Force
Copy-Item (Join-Path $devDir "settings.example.json") (Join-Path $stageDir "settings.example.json") -Force
Copy-Item (Join-Path $devDir "scripts\THIRDPARTY_LICENSES.txt") (Join-Path $stageDir "THIRDPARTY_LICENSES.txt") -Force
Copy-Item (Join-Path $devDir "scripts\*.bat") $stageScripts -Force
Copy-Item (Join-Path $devDir "scripts\interactive\*.bat") $stageInteractive -Force
Get-ChildItem $stageDir -Include *.pdb, *.xml -Recurse -File | Remove-Item -Force

$runtimeAgents = (@'
# Runtime Context: reEngine (v__VER__)

- **Launcher:** `reEngine.exe` (forwards to `app\reEngine.exe`)
- **Suite Layout:** Peer `..\engines\re<Platform>\v<Ver>\` and `..\toolchain\` under `suiteInstallDir` (fallback to local `.\engines\` / `.\toolchain\` for isolated portable runs).
- **Data & Logs:** `data\settings.json` and `logs\` unless overridden by `--data-dir`, `--config`, `--log-dir` (or `REENGINE_{DATA_DIR,CONFIG,LOG_DIR}`).
- **Daemon Policy:** Zero background daemons; `AllowAutoStart = false`.
'@).Replace('__VER__', $Version)
Write-Utf8Text (Join-Path $stageDir "AGENTS.md") ($runtimeAgents + "`r`n")

Invoke-Sign (Join-Path $stageApp "reEngine.exe")
Invoke-Sign (Join-Path $stageDir "reEngine.exe")

# Retain at most the 2 newest portable Builds\v* directories (deletes stale v0.1.0 / v0.1.1).
$staleBuilds = Get-ChildItem -Path $buildsRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
    Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending |
    Select-Object -Skip 2
foreach ($sb in $staleBuilds) {
    Write-Host "  Removing stale staging folder 'Builds\$($sb.Name)'..."
    Remove-Item $sb.FullName -Recurse -Force
}

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

$emDash = [string][char]0x2014
$rawReleaseNotes = (@'
# reEngine __EMDASH__ Release v__VER__

Zero-daemon `.recomp` suite router, side-by-side platform engine track resolver, and self-bootstrapping SDK (`AllowAutoStart = false`).

## Highlights
- Parses Standard 18 `.recomp` and `.decomp` JSON/ZIP manifests with fail-closed Zip-Slip, clean-room artifact guards, and `data\rom\` preload media discovery.
- Orchestrates platform-agnostic `D0`-`D5` decompilation pipelines and resolves highest compatible `engines\re<Platform>\v<Ver>\` tracks (`>= engineVersion`).
- Manages deterministic `<dataDir>\cache\` AOT-compiled assemblies (`aot-index.json`) with SHA-256 verification and cross-process locking.
- Supports `%LOCALAPPDATA%\Programs\StreamItAll\reEngine\` user-scope installs (`--profile Lightweight|Full`) with zero background daemons (`AllowAutoStart = false`).
- Signed single-EXE 4-pillar installer (`AllowAutoStart = false`) with bundled `MPL-2.0` `LICENSE` (`app\LICENSE`) and redistributable SDK drop.
'@).Replace('__VER__', $Version).Replace('__EMDASH__', $emDash)
$endUserReleaseNotes = Sanitize-MarkdownForEndUser $rawReleaseNotes
$installerReleaseNotesPath = Join-Path $installersDir "RELEASE_NOTES.md"
Write-Utf8Text $installerReleaseNotesPath $endUserReleaseNotes
Assert-NoEndUserLeaks @($installerReleaseNotesPath)

$auditScript = Join-Path $env:USERPROFILE ".gemini\config\skills\dev\scripts\audit_installer.ps1"
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

$endUserReadme = Sanitize-MarkdownForEndUser (Read-Utf8Text (Join-Path $devDir "docs\USER_GUIDE.md"))
$endUserFeatures = Sanitize-MarkdownForEndUser (Read-Utf8Text (Join-Path $root "FEATURES.md"))
$endUserChangelog = Sanitize-MarkdownForEndUser (Read-Utf8Text (Join-Path $root "CHANGELOG.md"))

foreach ($dir in @($relVerDir, $relLatestDir)) {
    Copy-Item $installerPath (Join-Path $dir $installerName) -Force
    $mdPaths = @(
        (Join-Path $dir "README.md"),
        (Join-Path $dir "FEATURES.md"),
        (Join-Path $dir "RELEASE_NOTES.md"),
        (Join-Path $dir "CHANGELOG.md")
    )
    Write-Utf8Text $mdPaths[0] $endUserReadme
    Write-Utf8Text $mdPaths[1] $endUserFeatures
    Write-Utf8Text $mdPaths[2] $endUserReleaseNotes
    Write-Utf8Text $mdPaths[3] $endUserChangelog
    Assert-NoEndUserLeaks $mdPaths

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

$unpackedReleases = Get-ChildItem -Path $releasesDir -Directory | Where-Object { $_.Name -match '^v\d+\.\d+\.\d+' } |
    Sort-Object { try { [version]($_.Name.TrimStart('v').Split('-')[0]) } catch { [version]"0.0.0" } } -Descending
if ($unpackedReleases.Count -gt 2) {
    $unpackedReleases | Select-Object -Skip 2 | ForEach-Object {
        Write-Host "  Pruning older unpacked release folder '$($_.Name)' (.7z archive preserved)..."
        Remove-Item $_.FullName -Recurse -Force
    }
}

if (-not $SkipSourceArchive) {
    Write-Host "[6/7] Updating Archive\reEngine-v$Version-src.7z..."
    $srcArchive = Join-Path $root "Archive\reEngine-v$Version-src.7z"
    if (Test-Path $srcArchive) { Remove-Item $srcArchive -Force }
    Push-Location $root
    try {
        Invoke-7z @("a", "-t7z", "-mx=9", "-xr!bin", "-xr!obj", "-xr!TestResults", "-xr!*.user", $srcArchive, "Dev", "README.md", "FEATURES.md", "ROADMAP.md", "CHANGELOG.md", "AGENTS.md", "LICENSE", "active.state")
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
    Copy-Item $licensePath (Join-Path $sdkDir "LICENSE") -Force
    Copy-Item $installerPath (Join-Path $sdkDir $installerName) -Force
    $artifacts = @(
        "reEngine.Core.dll",
        "reEngine.Core.xml",
        "reEngine.Core.deps.json",
        "LICENSE",
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
    Write-Utf8Text (Join-Path $sdkDir "sdk-manifest.json") ($manifestObj + "`r`n")
}

Remove-Item $workTemp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "DONE: Build, signed installer, 6-file Releases, archive, and SharedLibs SDK staged for v$Version."
