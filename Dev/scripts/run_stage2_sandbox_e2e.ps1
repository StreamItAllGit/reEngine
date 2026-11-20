#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$ArtifactDir = "",
    [switch]$SkipInstallerSandbox,
    [int]$TimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "Stage2SandboxHelpers.ps1")
. (Join-Path $PSScriptRoot "Stage2V060Probes.ps1")
Initialize-Stage2Win32Types

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$devDir = Join-Path $root "Dev"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -LiteralPath (Join-Path $devDir "version.txt") -Raw).Trim()
}

$sandboxRoot = Join-Path $env:TEMP ("reengine_vis_e2e_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $sandboxRoot | Out-Null
Write-DesktopPreflightInfo

$spawnedChildPid = $null
try {
    $buildsExe = Join-Path $root "Builds\v$Version\reEngine.exe"
    $debugExe = Join-Path $devDir "src\reEngine.App\bin\Debug\net8.0-windows\reEngine.exe"
    $releaseExe = Join-Path $devDir "src\reEngine.App\bin\Release\net8.0-windows\reEngine.exe"
    if (-not (Test-Path $debugExe)) { throw "Debug automation binary missing: $debugExe" }
    if (-not (Test-Path $releaseExe)) { throw "Release binary missing: $releaseExe" }
    $openExe = if (Test-Path $buildsExe) { $buildsExe } else { $releaseExe }

    if (-not $SkipInstallerSandbox) {
        $installer = Join-Path $root "Releases\v$Version\reEngine-Setup-v$Version-win-x64.exe"
        $runnerPy = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "Personal\Foundation\Dev\tools\run_sandbox_e2e.py"
        if (-not (Test-Path $installer)) { throw "Installer missing at $installer" }
        if (-not (Test-Path $runnerPy)) { throw "Stage 2 runner missing at $runnerPy" }
        Write-Host "[1/7] Executing Foundation 12-point 4-drive Stage 2 installer sandbox (v$Version)..."
        $receiptFile = Join-Path $sandboxRoot "stage2_receipt.json"
        & python $runnerPy --app-id reengine --version $Version --installer $installer "--probe-arg=--version" --receipt-out $receiptFile --timeout-seconds $TimeoutSeconds
        $sbExit = $LASTEXITCODE
        if ($sbExit -eq 3 -and (Test-Path $receiptFile)) {
            $rcp = Get-Content -LiteralPath $receiptFile -Raw | ConvertFrom-Json
            $s01Note = ($rcp.checklistResults | Where-Object { $_.id -eq "S2-01" }).notes
            $nonS12Failed = @($rcp.checklistResults | Where-Object { $_.id -ne "S2-12" -and -not $_.isPassed })
            if ($nonS12Failed.Count -eq 0 -and $s01Note -match '(host-snapshot-[^.\s]+\.json)') {
                $snapName = $Matches[1].Replace("host-snapshot-", "").Replace(".json", "")
                $toolsDir = Split-Path -Parent $runnerPy
                $diffJson = & python -c "import argparse, json, sys; sys.path.insert(0, sys.argv[1]); from sandbox_ctl import resolve_sandbox_config, diff_host_snapshots; cfg = resolve_sandbox_config(argparse.Namespace()); print(json.dumps(diff_host_snapshots(cfg, name=sys.argv[2])))" $toolsDir $snapName
                $diff = $diffJson | ConvertFrom-Json
                $regCount = @($diff.addedRegistryEntries).Count + @($diff.removedRegistryEntries).Count + @($diff.modifiedRegistryEntries).Count
                $allFiles = @($diff.addedFiles) + @($diff.removedFiles) + @($diff.modifiedFiles)
                $realFiles = @($allFiles | Where-Object { $_ -and $_ -notmatch '^[CD]:\\Programs\\\.tmp\.driveupload(?:\\|$)' })
                if ($regCount -eq 0 -and $realFiles.Count -eq 0) {
                    Write-Host "[1/7] S2-01..S2-11 PASSED; S2-12 filtered $($allFiles.Count) external Google Drive .tmp.driveupload entry(s)."
                    $sbExit = 0
                }
            }
        }
        if ($sbExit -ne 0) { throw "Foundation Stage 2 sandbox runner failed ($sbExit)" }
    }

    $dataDir = Join-Path $sandboxRoot "data"
    $cfgPath = Join-Path $dataDir "config\settings.json"
    $logDir = Join-Path $sandboxRoot "logs"
    $enginesDir = Join-Path $sandboxRoot "engines"
    $engV031 = Join-Path $enginesDir "reStation\v0.3.1"
    $modDir = Join-Path $sandboxRoot "games\SampleRecomp"
    $romDir = Join-Path $modDir "data\rom"
    $saveDir = Join-Path $sandboxRoot "saves"
    New-Item -ItemType Directory -Force $dataDir, $logDir, $engV031, $modDir, $romDir, $saveDir | Out-Null

    New-DetachedChildStub -EngineDir $engV031 -WorkDir $sandboxRoot
    [System.IO.File]::WriteAllBytes((Join-Path $romDir "sample-recomp.iso"), [byte[]]@(0x50, 0x53, 0x2D, 0x58, 0x20, 0x45, 0x58, 0x45))
    $samplePkg = Join-Path $modDir "SampleRecomp.recomp"
    [System.IO.File]::WriteAllText($samplePkg, @"
{
  "schemaVersion": 1,
  "moduleId": "sample-recomp",
  "title": "Sample Recomp",
  "version": "0.4.0",
  "engineId": "reStation",
  "engineVersion": "0.3.1",
  "compatibleEngineTrack": "0.3.x"
}
"@)
    $sampleDecomp = Join-Path $modDir "SampleDecomp.decomp"
    [System.IO.File]::WriteAllText($sampleDecomp, @"
{
  "schemaVersion": 1,
  "moduleId": "sample-recomp",
  "title": "Sample Decomp Recipe",
  "platformId": "psx",
  "engineId": "reStation",
  "engineVersion": "0.3.1",
  "compatibleEngineTrack": "0.3.x",
  "targetDecompTier": "D4"
}
"@)

    $env:APP_ISOLATED_TEST = "1"
    $env:FOUNDATION_SANDBOX_ROOT = $sandboxRoot

    Write-Host "[2/7] Launching WinForms UI E2E (5-backend Library + Inspector Drawer)..."
    $p1 = Get-FreeLoopbackPort; $t1 = [Guid]::NewGuid().ToString("N"); $stream1 = $null
    $winProc = Start-Process -FilePath $debugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--window", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir, "--automation-port", "$p1", "--automation-token", $t1
    )
    try {
        $null = Wait-AutomationHealth -Port $p1 -Token $t1
        Show-WindowOnTop -Proc $winProc
        $stream1 = Start-ExeboxStreamForPort -Port $p1 -Token $t1 -SandboxDir $sandboxRoot -TargetPid $winProc.Id
        Start-Sleep -Milliseconds 600
        $tree1 = Wait-TreeControl -Port $p1 -Token $t1 -ControlId "Chk_Fullscreen"
        if ($null -eq (Find-TreeNode -Node $tree1.data -ControlId "Btn_VerifyPackage")) { throw "Missing Btn_VerifyPackage" }
        foreach ($btn in @("Pill_Engines", "Pill_BringUp", "Pill_Library", "Btn_ToggleDrawer")) {
            $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = $btn }
            Start-Sleep -Milliseconds 450
        }
        $treeDrawer = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "GET" -Route "/v1/tree"
        $drawerNode = Find-TreeNode -Node $treeDrawer.data -ControlId "Drawer_Panel"
        if ($null -eq $drawerNode -or -not $drawerNode.isVisible) { throw "Inspector Drawer (Drawer_Panel) did not open after clicking Btn_ToggleDrawer" }
        $shot1 = Join-Path $logDir "winforms_library_live.png"
        $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shot1 }
        Assert-NonBlankPng -Path $shot1 -ArtifactDir $ArtifactDir
        $null = Invoke-LoopbackJson -Port $p1 -Token $t1 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Btn_Close" }
        if (-not $winProc.WaitForExit(10000) -or $winProc.ExitCode -ne 0) { throw "WinForms Library E2E did not exit cleanly" }
    } finally { Stop-ExeboxStream $stream1; if (-not $winProc.HasExited) { $winProc.Kill() } }

    Write-Host "[3/7] Testing interactive Explorer .decomp launch onto Bring-Up shelf..."
    $pDecomp = Get-FreeLoopbackPort; $tDecomp = [Guid]::NewGuid().ToString("N"); $streamDecomp = $null
    $decompProc = Start-Process -FilePath $debugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "decomp", "--module", $sampleDecomp, "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir, "--automation-port", "$pDecomp", "--automation-token", $tDecomp
    )
    try {
        $null = Wait-AutomationHealth -Port $pDecomp -Token $tDecomp
        Show-WindowOnTop -Proc $decompProc
        $streamDecomp = Start-ExeboxStreamForPort -Port $pDecomp -Token $tDecomp -SandboxDir $sandboxRoot -TargetPid $decompProc.Id
        $null = Wait-TreeControl -Port $pDecomp -Token $tDecomp -ControlId "Btn_RunStage"
        $null = Invoke-LoopbackJson -Port $pDecomp -Token $tDecomp -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Btn_VerifyDecomp" }
        Start-Sleep -Milliseconds 450
        $shotDecomp = Join-Path $logDir "winforms_bringup_decomp_live.png"
        $null = Invoke-LoopbackJson -Port $pDecomp -Token $tDecomp -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shotDecomp }
        Assert-NonBlankPng -Path $shotDecomp -ArtifactDir $ArtifactDir
        $null = Invoke-LoopbackJson -Port $pDecomp -Token $tDecomp -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Btn_Close" }
        if (-not $decompProc.WaitForExit(10000) -or $decompProc.ExitCode -ne 0) { throw "Interactive .decomp UI E2E failed" }
    } finally { Stop-ExeboxStream $streamDecomp; if (-not $decompProc.HasExited) { $decompProc.Kill() } }

    Write-Host "[4/7] Verifying standalone --verify-module --json (.recomp & .decomp)..."
    foreach ($pkg in @($samplePkg, $sampleDecomp)) {
        $vRes = Invoke-CliBounded -ExePath $openExe -WorkDir $root -CliArgs @(
            "--verify-module", $pkg, "--json", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir
        )
        if ($vRes.ExitCode -ne 0) { throw "--verify-module failed ($($vRes.ExitCode)): $($vRes.StdErr) $($vRes.StdOut)" }
        $vJson = $vRes.StdOut | ConvertFrom-Json
        if (-not $vJson.isPassed -or $vJson.exitCode -ne 0) { throw "Invalid --verify-module JSON output for $pkg" }
    }

    Write-Host "[5/7] Verifying --open --wait --fullscreen --save-dir --backend directx & WM_CLOSE save flush..."
    $waitProc = Start-Process -FilePath $openExe -WindowStyle Hidden -PassThru -ArgumentList @(
        "--open", $samplePkg, "--wait", "--fullscreen", "--save-dir", $saveDir, "--backend", "directx", "--scale", "4",
        "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir
    )
    $pidFile = Join-Path $engV031 "child_window.pid"
    for ($w = 0; $w -lt 50 -and -not (Test-Path $pidFile); $w++) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path $pidFile)) { throw "Child window never started in --open --wait check" }
    $spawnedChildPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
    $childProc = Get-Process -Id $spawnedChildPid -ErrorAction Stop
    Show-WindowOnTop -Proc $childProc
    Start-Sleep -Milliseconds 400
    [Win32WindowTop]::CloseAllWindowsForPid($spawnedChildPid)
    if (-not $waitProc.WaitForExit(10000) -or $waitProc.ExitCode -ne 0) { throw "--open --wait failed to exit 0 cleanly ($($waitProc.ExitCode))" }
    $spawnedChildPid = $null
    if (-not (Test-Path (Join-Path $saveDir "memcard.sav"))) { throw "Save file was not flushed into --save-dir on WM_CLOSE" }
    $recvArgs = (Get-Content -LiteralPath (Join-Path $engV031 "received_args.txt")) -join " "
    foreach ($tok in @("--fullscreen", "--save-dir", "--backend", "directx", "--wait")) {
        if ($recvArgs -notmatch [regex]::Escape($tok)) { throw "Missing expected token '$tok' in child args: $recvArgs" }
    }

    Write-Host "[6/7] Launching visible SDL3 Host E2E (OpenGL + F1 Video Aspect + 5-col Keybinds + F3 HUD + Hotkeys) & Exit 6 check..."
    $p2 = Get-FreeLoopbackPort; $t2 = [Guid]::NewGuid().ToString("N"); $streamSdl = $null
    $sdlProc = Start-Process -FilePath $debugExe -WindowStyle Normal -PassThru -ArgumentList @(
        "--host-demo", "--backend", "opengl", "--require-backend", "--aspect-mode", "fourByThree", "--window", "--preload-media",
        "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir, "--engines-dir", $enginesDir, "--automation-port", "$p2", "--automation-token", $t2
    )
    try {
        $null = Wait-AutomationHealth -Port $p2 -Token $t2
        Show-WindowOnTop -Proc $sdlProc
        $streamSdl = Start-ExeboxStreamForPort -Port $p2 -Token $t2 -SandboxDir $sandboxRoot -TargetPid $sdlProc.Id
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "MediaGate_DemoBypassButton"
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "MediaGate_DemoBypassButton" }
        Start-Sleep -Milliseconds 400
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F1" }
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "Video_AspectRatio"
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Video_AspectRatio" }
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F5" }
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F9" }
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "Tab_Keybinds"
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/pointer" -Body @{ action = "click"; targetId = "Tab_Keybinds" }
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "F3" }
        $null = Wait-TreeControl -Port $p2 -Token $t2 -ControlId "F3Hud"
        Start-Sleep -Milliseconds 500
        $shot2 = Join-Path $logDir "sdl_host_live.png"
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/snapshot" -Body @{ filePath = $shot2 }
        Assert-NonBlankPng -Path $shot2 -ArtifactDir $ArtifactDir
        $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" }
        Start-Sleep -Milliseconds 150
        try { $null = Invoke-LoopbackJson -Port $p2 -Token $t2 -Method "POST" -Route "/v1/keyboard" -Body @{ action = "press"; key = "ESC" } } catch [System.Net.WebException] { }
        if (-not $sdlProc.WaitForExit(10000) -or $sdlProc.ExitCode -ne 0) { throw "SDL3 Host E2E exited abnormally" }
    } finally { Stop-ExeboxStream $streamSdl; if (-not $sdlProc.HasExited) { $sdlProc.Kill() } }

    $metalRes = Invoke-CliBounded -ExePath $openExe -WorkDir $root -CliArgs @(
        "--host-demo", "--backend", "metal", "--require-backend", "--frames", "1", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir
    )
    if ($metalRes.ExitCode -ne 6) { throw "Expected --backend metal --require-backend ExitCode 6, got $($metalRes.ExitCode)" }

    Invoke-Stage2V060Probes -DebugExe $debugExe -SandboxRoot $sandboxRoot -EnginesDir $enginesDir -ArtifactDir $ArtifactDir

    Write-Host "[8/8] Verifying Dual-Binary Release fail-closed automation gate (ExitCode = 2)..."
    $relRes = Invoke-CliBounded -ExePath $releaseExe -WorkDir $root -CliArgs @(
        "--automation-port", "5199", "--data-dir", $dataDir, "--config", $cfgPath, "--log-dir", $logDir
    )
    if ($relRes.ExitCode -ne 2) { throw "Expected Release reEngine.exe --automation-port ExitCode 2, got $($relRes.ExitCode)" }
    $relSetupPkg = Join-Path $root "Releases\v$Version\reEngine-Setup-v$Version-win-x64.exe"
    $relSetupBin = Join-Path $devDir "src\reEngine.Installer\bin\Release\net8.0-windows\reEngine.Installer.exe"
    $setupTarget = if (Test-Path $relSetupPkg) { $relSetupPkg } else { $relSetupBin }
    $instRelRes = Invoke-CliBounded -ExePath $setupTarget -WorkDir $root -CliArgs @("--automation-port", "5199")
    if ($instRelRes.ExitCode -ne 2) { throw "Expected Release Setup ($setupTarget) --automation-port ExitCode 2, got $($instRelRes.ExitCode)" }
    Write-Host "PASS: Stage 2 Sandbox v$Version + WinForms 5-Backend & Interactive .decomp + --verify-module + --wait Flush + SDL3 Host + Dual Release ExitCode=2 verified."
} finally {
    if ($null -ne $spawnedChildPid) {
        try { $p = Get-Process -Id $spawnedChildPid -ErrorAction SilentlyContinue; if ($p -and -not $p.HasExited) { $p.Kill() } } catch { }
    }
    Remove-Item Env:APP_ISOLATED_TEST, Env:FOUNDATION_SANDBOX_ROOT -ErrorAction SilentlyContinue
    if (Test-Path $sandboxRoot) { Remove-Item $sandboxRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
