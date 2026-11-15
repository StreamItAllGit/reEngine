#Requires -Version 5.1
<#
.SYNOPSIS
    Stages and validates the <= 160 MB offline Ghidra (Apache-2.0, GPL/ + docs/ + Swing stripped)
    + jlink OpenJDK + THIRDPARTY_LICENSES.txt toolchain for Full reEngine installations.
#>
[CmdletBinding()]
param(
    [string]$GhidraSourceDir = "",
    [string]$JdkSourceDir = "",
    [string]$OutputDir = "",
    [string]$LicenseTemplatePath = "",
    [int]$MaxBudgetMB = 160,
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "Builds\toolchain"
}
if ([string]::IsNullOrWhiteSpace($LicenseTemplatePath)) {
    $LicenseTemplatePath = Join-Path $PSScriptRoot "THIRDPARTY_LICENSES.txt"
}

function Test-StagedToolchain {
    param([string]$ToolchainRoot, [int]$BudgetMB)

    if (-not (Test-Path $ToolchainRoot)) {
        throw "Toolchain directory not found: '$ToolchainRoot'"
    }

    $ghidraRoot = Join-Path $ToolchainRoot "ghidra"
    $jdkRoot = Join-Path $ToolchainRoot "jdk"
    $licenseFile = Join-Path $ToolchainRoot "THIRDPARTY_LICENSES.txt"

    if (Test-Path (Join-Path $ghidraRoot "GPL")) {
        throw "Apache-2.0 compliance failure: unstripped 'ghidra\GPL' directory detected at '$ghidraRoot\GPL'."
    }
    if (Test-Path (Join-Path $ghidraRoot "docs")) {
        throw "Size budget failure: unstripped 'ghidra\docs' directory detected at '$ghidraRoot\docs'."
    }

    $headlessBat = Join-Path $ghidraRoot "support\analyzeHeadless.bat"
    $headlessSh = Join-Path $ghidraRoot "support\analyzeHeadless"
    if (-not (Test-Path $headlessBat) -and -not (Test-Path $headlessSh)) {
        throw "Missing Ghidra headless entrypoint under '$ghidraRoot\support\analyzeHeadless(.bat)'."
    }

    $javaExe = Join-Path $jdkRoot "bin\java.exe"
    $javaBin = Join-Path $jdkRoot "bin\java"
    if (-not (Test-Path $javaExe) -and -not (Test-Path $javaBin)) {
        throw "Missing jlink OpenJDK runtime under '$jdkRoot\bin\java(.exe)'."
    }

    if (-not (Test-Path $licenseFile)) {
        throw "Missing required license notice: '$licenseFile'."
    }

    $totalBytes = (Get-ChildItem -Path $ToolchainRoot -Recurse -File | Measure-Object -Property Length -Sum).Sum
    if ($null -eq $totalBytes) { $totalBytes = 0 }
    $budgetBytes = [int64]$BudgetMB * 1024 * 1024
    $totalMB = [math]::Round($totalBytes / 1MB, 2)
    if ($totalBytes -gt $budgetBytes) {
        throw "Offline toolchain footprint ($totalMB MB) exceeds the $BudgetMB MB budget."
    }

    return [pscustomobject]@{
        ToolchainDirectory = $ToolchainRoot
        TotalBytes         = $totalBytes
        TotalMB            = $totalMB
        BudgetMB           = $BudgetMB
        AnalyzeHeadless    = if (Test-Path $headlessBat) { $headlessBat } else { $headlessSh }
        JavaExecutable     = if (Test-Path $javaExe) { $javaExe } else { $javaBin }
        ThirdPartyLicenses = $licenseFile
    }
}

if ($ValidateOnly) {
    $report = Test-StagedToolchain -ToolchainRoot $OutputDir -BudgetMB $MaxBudgetMB
    Write-Output "Toolchain verified: $($report.TotalMB) MB / $($report.BudgetMB) MB at '$($report.ToolchainDirectory)'."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($GhidraSourceDir) -or -not (Test-Path $GhidraSourceDir)) {
    throw "Specify a valid -GhidraSourceDir (extracted Ghidra distribution) to stage the offline toolchain."
}

if ([string]::IsNullOrWhiteSpace($JdkSourceDir)) {
    $JdkSourceDir = $env:JAVA_HOME
}
if ([string]::IsNullOrWhiteSpace($JdkSourceDir) -or -not (Test-Path (Join-Path $JdkSourceDir "bin\jlink.exe"))) {
    throw "Specify a valid -JdkSourceDir containing bin\jlink.exe (or set JAVA_HOME)."
}
if (-not (Test-Path $LicenseTemplatePath)) {
    throw "License template not found at '$LicenseTemplatePath'."
}

if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$stagedGhidra = Join-Path $OutputDir "ghidra"
New-Item -ItemType Directory -Path $stagedGhidra -Force | Out-Null

# Strip GPL/, docs/, server/, Extensions/, and interactive Swing GUI modules.
$excludedDirs = @(
    "GPL", "docs", "server", "Extensions",
    "Gui", "Docking", "Graph", "CodeCompare", "VersionTracking", "Debugger", "GhidraServer"
)
$robocopyArgs = @(
    $GhidraSourceDir, $stagedGhidra, "/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS", "/XD"
) + $excludedDirs
& robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with exit code $LASTEXITCODE while staging stripped Ghidra."
}

$stagedJdk = Join-Path $OutputDir "jdk"
$jlinkExe = Join-Path $JdkSourceDir "bin\jlink.exe"
$jlinkModules = "java.base,java.logging,java.xml,java.sql,java.desktop,java.management,java.naming,java.instrument,jdk.unsupported"
& $jlinkExe --add-modules $jlinkModules --strip-debug --no-man-pages --no-header-files --compress=zip-6 --output $stagedJdk
if ($LASTEXITCODE -ne 0) {
    throw "jlink.exe failed with exit code $LASTEXITCODE."
}

Copy-Item -Path $LicenseTemplatePath -Destination (Join-Path $OutputDir "THIRDPARTY_LICENSES.txt") -Force

$verified = Test-StagedToolchain -ToolchainRoot $OutputDir -BudgetMB $MaxBudgetMB
$manifest = [ordered]@{
    schemaVersion      = 1
    maxBudgetMB        = $MaxBudgetMB
    totalBytes         = $verified.TotalBytes
    totalMB            = $verified.TotalMB
    gplStripped        = $true
    docsStripped       = $true
    swingStripped      = $true
    analyzeHeadlessSha = (Get-FileHash -Path $verified.AnalyzeHeadless -Algorithm SHA256).Hash
    javaExeSha         = (Get-FileHash -Path $verified.JavaExecutable -Algorithm SHA256).Hash
    licensesSha        = (Get-FileHash -Path $verified.ThirdPartyLicenses -Algorithm SHA256).Hash
}
$manifestJson = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText((Join-Path $OutputDir "toolchain-manifest.json"), $manifestJson, [System.Text.UTF8Encoding]::new($false))

Write-Output "Staged offline toolchain ($($verified.TotalMB) MB / $MaxBudgetMB MB) at '$OutputDir'."
